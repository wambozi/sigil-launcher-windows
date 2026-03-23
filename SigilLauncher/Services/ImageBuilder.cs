using System.Diagnostics;
using SigilLauncher.Models;

namespace SigilLauncher.Services;

/// <summary>
/// Build lifecycle state for the VM image.
/// </summary>
public enum BuildState
{
    Idle,
    Building,
    Complete,
    Error,
}

/// <summary>
/// Builds the NixOS VM image by generating a flake.nix wrapper and invoking nix build.
/// Streams build output and manages artifact placement in %APPDATA%\Sigil\images\.
/// </summary>
public class ImageBuilder
{
    public BuildState State { get; private set; } = BuildState.Idle;
    public string ProgressMessage { get; private set; } = string.Empty;
    public string LogOutput { get; private set; } = string.Empty;
    public string? ErrorMessage { get; private set; }

    public event Action? StateChanged;

    public static string ImagesDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Sigil", "images");

    public static string ProfileDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Sigil", "profiles", "default");

    /// <summary>
    /// Check if a built image exists (vmlinuz present).
    /// </summary>
    public bool ImageExists =>
        File.Exists(Path.Combine(ImagesDirectory, "vmlinuz"));

    /// <summary>
    /// Locate the nix binary on this system.
    /// </summary>
    public static string? FindNixPath()
    {
        // Check common Windows Nix installation paths
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nix-profile", "bin", "nix.exe"),
            @"C:\nix\var\nix\profiles\default\bin\nix.exe",
            @"C:\ProgramData\nix\bin\nix.exe",
        };

        var found = candidates.FirstOrDefault(File.Exists);
        if (found != null) return found;

        // Try `where nix` as a last resort
        try
        {
            var (exitCode, output, _) = ProcessRunner.RunAsync("where", "nix").GetAwaiter().GetResult();
            if (exitCode == 0 && !string.IsNullOrWhiteSpace(output))
            {
                var firstLine = output.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
                if (firstLine != null && File.Exists(firstLine))
                    return firstLine;
            }
        }
        catch
        {
            // where may not exist or fail
        }

        return null;
    }

    /// <summary>
    /// Generate a flake.nix wrapper that imports sigil-os and applies tool selections.
    /// </summary>
    public void GenerateFlake(LauncherProfile profile, string sigilOSPath)
    {
        var dir = ProfileDirectory;
        Directory.CreateDirectory(dir);

        var flakeContent = $$"""
            {
              inputs.sigil-os.url = "path:{{sigilOSPath}}";
              inputs.nixpkgs.follows = "sigil-os/nixpkgs";

              outputs = { self, sigil-os, nixpkgs }: {
                nixosConfigurations.workspace = sigil-os.lib.mkLauncherVM {
                  system = "x86_64-linux";
                  tools = {
                    editor = "{{profile.Editor}}";
                    containerEngine = "{{profile.ContainerEngine}}";
                    shell = "{{profile.Shell}}";
                    notificationLevel = {{profile.NotificationLevel}};
                  };
                };
              };
            }
            """;

        File.WriteAllText(Path.Combine(dir, "flake.nix"), flakeContent);
    }

    /// <summary>
    /// Build the VM image from the generated flake.
    /// </summary>
    public async Task BuildAsync(LauncherProfile profile, string sigilOSPath = "github:sigil-tech/sigil-os", CancellationToken ct = default)
    {
        var nixPath = FindNixPath();
        if (nixPath == null)
        {
            State = BuildState.Error;
            ErrorMessage = "Nix is not installed. Install it from https://nixos.org/download/";
            StateChanged?.Invoke();
            return;
        }

        State = BuildState.Building;
        ProgressMessage = "Generating configuration...";
        ErrorMessage = null;
        LogOutput = string.Empty;
        StateChanged?.Invoke();

        try
        {
            // Generate the flake wrapper
            GenerateFlake(profile, sigilOSPath);

            ProgressMessage = "Building VM image (this may take several minutes)...";
            AppendLog("$ nix build .#nixosConfigurations.workspace.config.system.build.toplevel\n");
            StateChanged?.Invoke();

            var profileDir = ProfileDirectory;

            // Build the system toplevel
            await RunNixAsync(nixPath, new[]
            {
                "--extra-experimental-features", "nix-command flakes",
                "build",
                $"{profileDir}#nixosConfigurations.workspace.config.system.build.toplevel",
                "--out-link", Path.Combine(profileDir, "result-toplevel"),
                "--no-link",
            }, profileDir, ct);

            ProgressMessage = "Extracting kernel and initrd...";
            StateChanged?.Invoke();

            // Build kernel
            await RunNixAsync(nixPath, new[]
            {
                "--extra-experimental-features", "nix-command flakes",
                "build",
                $"{profileDir}#nixosConfigurations.workspace.config.system.build.kernel",
                "--out-link", Path.Combine(profileDir, "result-kernel"),
            }, profileDir, ct);

            // Build initrd
            await RunNixAsync(nixPath, new[]
            {
                "--extra-experimental-features", "nix-command flakes",
                "build",
                $"{profileDir}#nixosConfigurations.workspace.config.system.build.initialRamdisk",
                "--out-link", Path.Combine(profileDir, "result-initrd"),
            }, profileDir, ct);

            // Copy artifacts to images directory
            ProgressMessage = "Copying artifacts...";
            StateChanged?.Invoke();

            var imagesDir = ImagesDirectory;
            Directory.CreateDirectory(imagesDir);

            // x86_64 uses bzImage
            var kernelSrc = Path.Combine(profileDir, "result-kernel", "bzImage");
            var initrdSrc = Path.Combine(profileDir, "result-initrd", "initrd");

            // Copy kernel
            var vmlinuzDest = Path.Combine(imagesDir, "vmlinuz");
            if (File.Exists(vmlinuzDest)) File.Delete(vmlinuzDest);
            File.Copy(kernelSrc, vmlinuzDest);

            // Copy initrd
            var initrdDest = Path.Combine(imagesDir, "initrd");
            if (File.Exists(initrdDest)) File.Delete(initrdDest);
            File.Copy(initrdSrc, initrdDest);

            // Create VHDX disk image if it doesn't exist
            var diskDest = Path.Combine(imagesDir, "sigil-vm.vhdx");
            if (!File.Exists(diskDest))
            {
                ProgressMessage = "Creating disk image...";
                StateChanged?.Invoke();

                var (exitCode, _, error) = await ProcessRunner.RunPowerShellAsync(
                    $"New-VHD -Path '{diskDest}' -SizeBytes 8GB -Dynamic");
                if (exitCode != 0)
                    AppendLog($"Warning: could not create VHDX: {error}\n");
            }

            // Download model if selected
            if (profile.ModelId != null)
            {
                var model = ModelCatalog.Models.FirstOrDefault(m => m.Id == profile.ModelId);
                if (model != null)
                {
                    var modelManager = new ModelManager();
                    if (!modelManager.IsModelDownloaded(model))
                    {
                        ProgressMessage = $"Downloading {model.Name} model...";
                        AppendLog($"Downloading {model.Filename} ({model.SizeGB} GB)...\n");
                        StateChanged?.Invoke();
                        await modelManager.DownloadModelAsync(model, ct: ct);
                    }
                }
            }

            ProgressMessage = "Build complete!";
            State = BuildState.Complete;
            StateChanged?.Invoke();
        }
        catch (OperationCanceledException)
        {
            State = BuildState.Idle;
            ProgressMessage = "Build cancelled.";
            StateChanged?.Invoke();
        }
        catch (Exception ex)
        {
            State = BuildState.Error;
            ErrorMessage = ex.Message;
            AppendLog($"ERROR: {ex.Message}\n");
            StateChanged?.Invoke();
        }
    }

    /// <summary>
    /// Run a nix command with real-time output streaming.
    /// </summary>
    private async Task RunNixAsync(string nixPath, string[] args, string workingDir, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = nixPath,
            WorkingDirectory = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        using var process = new Process { StartInfo = psi };
        var tcs = new TaskCompletionSource<int>();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null)
                AppendLog(e.Data + "\n");
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null)
                AppendLog(e.Data + "\n");
        };

        process.EnableRaisingEvents = true;
        process.Exited += (_, _) =>
        {
            tcs.TrySetResult(process.ExitCode);
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var reg = ct.Register(() =>
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            tcs.TrySetCanceled(ct);
        });

        var exitCode = await tcs.Task;

        if (exitCode != 0)
            throw new InvalidOperationException($"Build failed with exit code {exitCode}. Check the build log for details.");
    }

    private void AppendLog(string text)
    {
        LogOutput += text;
    }
}
