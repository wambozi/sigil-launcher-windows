using SigilLauncher.Models;
using SigilLauncher.Services;

namespace SigilLauncher.VM;

/// <summary>
/// Manages the Hyper-V VM lifecycle: create, start, stop, and health monitoring.
/// Includes periodic daemon health checks and VM crash detection.
/// </summary>
public class VMManager
{
    private LauncherProfile _profile;
    private CancellationTokenSource? _healthCts;
    private int _consecutiveHealthFailures;
    private const int MaxConsecutiveFailures = 3;
    private static readonly TimeSpan HealthCheckInterval = TimeSpan.FromSeconds(30);

    public VMState State { get; private set; } = VMState.Stopped;
    public string? ErrorMessage { get; private set; }
    public bool SshReady { get; private set; }
    public bool DaemonReady { get; private set; }
    public string? VmIpAddress { get; private set; }
    public LauncherProfile CurrentProfile => _profile;

    /// <summary>
    /// Whether the VM image (vmlinuz) exists and is ready to boot.
    /// </summary>
    public bool ImageReady =>
        File.Exists(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Sigil", "images", "vmlinuz"));

    public event Action? StateChanged;

    public VMManager()
    {
        _profile = LauncherProfile.Load();
    }

    private void SetState(VMState state, string? error = null)
    {
        State = state;
        if (error != null) ErrorMessage = error;
        StateChanged?.Invoke();
    }

    private void NotifyChanged() => StateChanged?.Invoke();

    // MARK: - Lifecycle

    public async Task StartAsync()
    {
        if (State != VMState.Stopped && State != VMState.Error)
            return;

        SetState(VMState.Starting);
        ErrorMessage = null;
        SshReady = false;
        DaemonReady = false;
        VmIpAddress = null;
        _consecutiveHealthFailures = 0;
        NotifyChanged();

        try
        {
            // Verify VM image exists before attempting anything
            if (!ImageReady)
            {
                throw new Exception(
                    "No VM image found. Run the setup wizard first to build a VM image.\n" +
                    $"Expected: {Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Sigil", "images", "vmlinuz")}");
            }

            if (!File.Exists(_profile.DiskImagePath))
            {
                throw new Exception(
                    $"VM disk image not found at: {_profile.DiskImagePath}\n" +
                    "Run the setup wizard to build the VM image.");
            }

            // Create VM if it doesn't exist
            var (exitCode, output, error) = await ProcessRunner.RunPowerShellAsync(
                VMConfiguration.CreateVmScript(_profile));
            if (exitCode != 0)
                throw new Exception($"Failed to create VM: {error}");

            // Configure CPU and memory
            (exitCode, output, error) = await ProcessRunner.RunPowerShellAsync(
                VMConfiguration.ConfigureVmScript(_profile));
            if (exitCode != 0)
                throw new Exception($"Failed to configure VM: {error}");

            // Create SMB shares
            (exitCode, output, error) = await ProcessRunner.RunPowerShellAsync(
                VMConfiguration.CreateSmbSharesScript(_profile));
            if (exitCode != 0)
                throw new Exception($"Failed to create SMB shares: {error}");

            // Start the VM
            (exitCode, output, error) = await ProcessRunner.RunPowerShellAsync(
                VMConfiguration.StartVmScript(_profile));
            if (exitCode != 0)
                throw new Exception($"Failed to start VM: {error}");

            SetState(VMState.Running);

            // Start health check polling in background
            _healthCts = new CancellationTokenSource();
            _ = PollForReadyAsync(_healthCts.Token);
        }
        catch (Exception ex)
        {
            SetState(VMState.Error, ex.Message);
        }
    }

    public async Task StopAsync()
    {
        if (State != VMState.Running)
            return;

        SetState(VMState.Stopping);
        _healthCts?.Cancel();

        // Try graceful shutdown via SSH
        if (VmIpAddress != null)
        {
            try
            {
                await SshClient.RunCommandAsync(VmIpAddress, "sudo shutdown now", timeoutSeconds: 5);

                // Wait up to 10s for VM to stop
                var deadline = DateTime.UtcNow.AddSeconds(10);
                while (DateTime.UtcNow < deadline)
                {
                    var (exitCode, output, _) = await ProcessRunner.RunPowerShellAsync(
                        VMConfiguration.GetVmStateScript(_profile));
                    if (exitCode == 0 && output.Contains("Off", StringComparison.OrdinalIgnoreCase))
                        break;
                    await Task.Delay(500);
                }
            }
            catch
            {
                // Graceful shutdown failed — force stop below
            }
        }

        // Force stop if still running
        await ProcessRunner.RunPowerShellAsync(VMConfiguration.StopVmScript(_profile));

        // Remove SMB shares
        await ProcessRunner.RunPowerShellAsync(VMConfiguration.RemoveSmbSharesScript());

        VmIpAddress = null;
        SshReady = false;
        DaemonReady = false;
        SetState(VMState.Stopped);
    }

    // MARK: - Health Checks

    private async Task PollForReadyAsync(CancellationToken ct)
    {
        try
        {
            // Poll for VM IP address (90s timeout, 3s interval — first boot takes longer)
            var ipDeadline = DateTime.UtcNow.AddSeconds(90);
            while (!ct.IsCancellationRequested && DateTime.UtcNow < ipDeadline)
            {
                var (exitCode, output, _) = await ProcessRunner.RunPowerShellAsync(
                    VMConfiguration.GetVmIpScript(_profile), ct);
                if (exitCode == 0 && !string.IsNullOrWhiteSpace(output))
                {
                    VmIpAddress = output.Trim();
                    NotifyChanged();
                    break;
                }
                await Task.Delay(3000, ct);
            }

            if (VmIpAddress == null)
            {
                ErrorMessage = "Could not discover VM IP address";
                NotifyChanged();
                return;
            }

            // Poll SSH (30s timeout)
            SshReady = await HealthChecker.WaitForSshAsync(VmIpAddress, ct);
            NotifyChanged();

            if (!SshReady)
            {
                ErrorMessage = "SSH did not become available";
                NotifyChanged();
                return;
            }

            // Poll daemon (30s timeout)
            DaemonReady = await HealthChecker.WaitForDaemonAsync(VmIpAddress, ct);
            NotifyChanged();

            if (!DaemonReady)
            {
                ErrorMessage = "sigild did not start";
                NotifyChanged();
                return;
            }

            // Bootstrap TLS credentials on first run
            await CredentialBootstrap.RunAsync(VmIpAddress);

            // Start periodic health monitoring
            await MonitorHealthAsync(ct);
        }
        catch (OperationCanceledException)
        {
            // Health check cancelled during shutdown
        }
    }

    /// <summary>
    /// Periodic health monitoring: checks daemon every 30s.
    /// After 3 consecutive failures, marks daemon as not ready.
    /// Also detects VM crash via PowerShell.
    /// </summary>
    private async Task MonitorHealthAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(HealthCheckInterval);

        while (!ct.IsCancellationRequested && await timer.WaitForNextTickAsync(ct))
        {
            // Check if VM is still running
            var vmRunning = await IsVmRunningAsync(ct);
            if (!vmRunning)
            {
                DaemonReady = false;
                SshReady = false;
                SetState(VMState.Error, "VM is no longer running (possible crash)");
                return;
            }

            // Check daemon health
            if (VmIpAddress != null)
            {
                var (exitCode, _, _) = await SshClient.RunCommandAsync(
                    VmIpAddress, "sigilctl status", ct: ct);

                if (exitCode == 0)
                {
                    _consecutiveHealthFailures = 0;
                    if (!DaemonReady)
                    {
                        DaemonReady = true;
                        NotifyChanged();
                    }
                }
                else
                {
                    _consecutiveHealthFailures++;
                    if (_consecutiveHealthFailures >= MaxConsecutiveFailures && DaemonReady)
                    {
                        DaemonReady = false;
                        ErrorMessage = "sigild health check failed (3 consecutive failures)";
                        NotifyChanged();
                    }
                }
            }
        }
    }

    /// <summary>
    /// Check if the VM is still running via PowerShell Get-VM query.
    /// </summary>
    private async Task<bool> IsVmRunningAsync(CancellationToken ct)
    {
        try
        {
            var (exitCode, output, _) = await ProcessRunner.RunPowerShellAsync(
                VMConfiguration.GetVmStateScript(_profile), ct);
            return exitCode == 0 && output.Contains("Running", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    // MARK: - Shell Launch

    public void LaunchShell()
    {
        if (!DaemonReady) return;

        // Look for sigil-shell in standard locations
        var shellPaths = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Sigil", "sigil-shell.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Sigil", "sigil-shell.exe"),
            Path.Combine(AppContext.BaseDirectory, "sigil-shell.exe"),
        };

        var shellPath = shellPaths.FirstOrDefault(File.Exists);
        if (shellPath == null)
        {
            ErrorMessage = "sigil-shell not found";
            NotifyChanged();
            return;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = shellPath,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to launch shell: {ex.Message}";
            NotifyChanged();
        }
    }

    // MARK: - Configuration

    public void UpdateProfile(LauncherProfile newProfile)
    {
        _profile = newProfile;
        newProfile.Save();
    }
}
