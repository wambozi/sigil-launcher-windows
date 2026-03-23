using System.Runtime.InteropServices;

namespace SigilLauncher.Services;

/// <summary>
/// Detected host hardware capabilities.
/// </summary>
public sealed class HardwareInfo
{
    public int TotalRAMGB { get; init; }
    public int CpuCores { get; init; }
    public string CpuArch { get; init; } = "x64";
    public int DiskAvailableGB { get; init; }
    public string? GpuName { get; init; }
}

/// <summary>
/// Recommended VM resource allocation based on detected hardware.
/// </summary>
public sealed class ResourceRecommendation
{
    public int MemoryGB { get; init; }
    public int Cpus { get; init; }
    public int DiskGB { get; init; }
}

/// <summary>
/// Detects host hardware and produces resource recommendations for the VM.
/// Uses WMI/runtime APIs on Windows.
/// </summary>
public static class HardwareDetector
{
    /// <summary>
    /// Detect the host machine's hardware capabilities.
    /// </summary>
    public static HardwareInfo Detect()
    {
        // RAM via GC or WMI — use a simple P/Invoke-free approach
        var totalRAMBytes = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        var totalRAMGB = (int)(totalRAMBytes / (1024L * 1024 * 1024));

        // CPU cores
        var cpuCores = Environment.ProcessorCount;

        // Architecture
        var cpuArch = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            Architecture.X86 => "x86",
            _ => "unknown",
        };

        // Disk space — check the drive containing %APPDATA%
        var diskAvailableGB = 0;
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var driveName = Path.GetPathRoot(appDataPath);
        if (driveName != null)
        {
            try
            {
                var driveInfo = new DriveInfo(driveName);
                if (driveInfo.IsReady)
                {
                    diskAvailableGB = (int)(driveInfo.AvailableFreeSpace / (1024L * 1024 * 1024));
                }
            }
            catch
            {
                // DriveInfo can throw on network/virtual drives
            }
        }

        // GPU name via PowerShell WMI query (best-effort, non-blocking)
        string? gpuName = null;
        try
        {
            var (exitCode, output, _) = ProcessRunner.RunPowerShellAsync(
                "(Get-CimInstance Win32_VideoController | Select-Object -First 1).Name").GetAwaiter().GetResult();
            if (exitCode == 0 && !string.IsNullOrWhiteSpace(output))
                gpuName = output.Trim();
        }
        catch
        {
            // GPU detection is best-effort
        }

        return new HardwareInfo
        {
            TotalRAMGB = totalRAMGB,
            CpuCores = cpuCores,
            CpuArch = cpuArch,
            DiskAvailableGB = diskAvailableGB,
            GpuName = gpuName,
        };
    }

    /// <summary>
    /// Produce a resource recommendation: 50% RAM (min 4, max 12), 50% cores (min 2), 20GB disk.
    /// </summary>
    public static ResourceRecommendation Recommend(HardwareInfo hardware)
    {
        var memoryGB = Math.Min(Math.Max(hardware.TotalRAMGB / 2, 4), 12);
        var cpus = Math.Max(hardware.CpuCores / 2, 2);
        var diskGB = 20;

        return new ResourceRecommendation
        {
            MemoryGB = memoryGB,
            Cpus = cpus,
            DiskGB = diskGB,
        };
    }

    /// <summary>
    /// Check whether the host meets minimum requirements (8GB RAM, 10GB disk).
    /// Returns (meets, reason) where reason is null if requirements are met.
    /// </summary>
    public static (bool Meets, string? Reason) MeetsMinimumRequirements(HardwareInfo hardware)
    {
        if (hardware.TotalRAMGB < 8)
            return (false, $"Sigil requires at least 8GB of system RAM. Detected: {hardware.TotalRAMGB}GB.");

        if (hardware.DiskAvailableGB < 10)
            return (false, $"Sigil requires at least 10GB of free disk space. Available: {hardware.DiskAvailableGB}GB.");

        return (true, null);
    }
}
