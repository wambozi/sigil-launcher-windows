using SigilLauncher.Services;

namespace SigilLauncher.VM;

/// <summary>
/// Polls SSH connectivity and daemon readiness with configurable timeouts.
/// </summary>
public class HealthChecker
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Polls until SSH is reachable on the given host.
    /// </summary>
    public static async Task<bool> WaitForSshAsync(string host, CancellationToken ct, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? DefaultTimeout);

        while (!ct.IsCancellationRequested && DateTime.UtcNow < deadline)
        {
            if (await SshClient.TestConnectionAsync(host, ct))
                return true;

            await Task.Delay(PollInterval, ct);
        }

        return false;
    }

    /// <summary>
    /// Polls until sigilctl status returns successfully via SSH.
    /// </summary>
    public static async Task<bool> WaitForDaemonAsync(string host, CancellationToken ct, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? DefaultTimeout);

        while (!ct.IsCancellationRequested && DateTime.UtcNow < deadline)
        {
            var (exitCode, _, _) = await SshClient.RunCommandAsync(host, "sigilctl status", ct: ct);
            if (exitCode == 0)
                return true;

            await Task.Delay(PollInterval, ct);
        }

        return false;
    }
}
