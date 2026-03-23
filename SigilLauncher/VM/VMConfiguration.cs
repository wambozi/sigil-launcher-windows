using SigilLauncher.Models;
using SigilLauncher.Services;

namespace SigilLauncher.VM;

/// <summary>
/// Generates PowerShell scripts for Hyper-V VM creation and SMB share management.
/// </summary>
public static class VMConfiguration
{
    /// <summary>
    /// PowerShell script to create the VM if it doesn't already exist.
    /// Gen2 VM with UEFI, Secure Boot disabled, VHDX attached.
    /// </summary>
    public static string CreateVmScript(LauncherProfile profile)
    {
        var memoryMB = profile.MemorySize / (1024 * 1024);

        return $@"
$vmName = '{profile.VmName}'
if (-not (Get-VM -Name $vmName -ErrorAction SilentlyContinue)) {{
    New-VM -Name $vmName -Generation 2 -MemoryStartupBytes {profile.MemorySize} -SwitchName '{profile.VmSwitchName}' -VHDPath '{profile.DiskImagePath}'
    Set-VMFirmware -VMName $vmName -EnableSecureBoot Off
    Write-Output 'VM created'
}} else {{
    Write-Output 'VM already exists'
}}";
    }

    /// <summary>
    /// PowerShell script to configure CPU and memory on the VM.
    /// </summary>
    public static string ConfigureVmScript(LauncherProfile profile)
    {
        return $@"
$vmName = '{profile.VmName}'
Set-VMProcessor -VMName $vmName -Count {profile.CpuCount}
Set-VMMemory -VMName $vmName -StartupBytes {profile.MemorySize}
Write-Output 'VM configured'";
    }

    /// <summary>
    /// PowerShell script to create SMB shares for workspace, profile, and models directories.
    /// </summary>
    public static string CreateSmbSharesScript(LauncherProfile profile)
    {
        var profileDir = LauncherProfile.ProfileDir;
        var modelsDir = ModelManager.ModelsDirectory;

        return $@"
# Ensure directories exist
if (-not (Test-Path '{profile.WorkspacePath}')) {{ New-Item -ItemType Directory -Path '{profile.WorkspacePath}' -Force }}
if (-not (Test-Path '{profileDir}')) {{ New-Item -ItemType Directory -Path '{profileDir}' -Force }}
if (-not (Test-Path '{modelsDir}')) {{ New-Item -ItemType Directory -Path '{modelsDir}' -Force }}

# Create SMB shares (remove existing first to update paths)
if (Get-SmbShare -Name 'sigil-workspace' -ErrorAction SilentlyContinue) {{ Remove-SmbShare -Name 'sigil-workspace' -Force }}
New-SmbShare -Name 'sigil-workspace' -Path '{profile.WorkspacePath}' -FullAccess 'Everyone'

if (Get-SmbShare -Name 'sigil-profile' -ErrorAction SilentlyContinue) {{ Remove-SmbShare -Name 'sigil-profile' -Force }}
New-SmbShare -Name 'sigil-profile' -Path '{profileDir}' -FullAccess 'Everyone'

if (Get-SmbShare -Name 'sigil-models' -ErrorAction SilentlyContinue) {{ Remove-SmbShare -Name 'sigil-models' -Force }}
New-SmbShare -Name 'sigil-models' -Path '{modelsDir}' -ReadAccess 'Everyone'

Write-Output 'SMB shares created'";
    }

    /// <summary>
    /// PowerShell script to start the VM.
    /// </summary>
    public static string StartVmScript(LauncherProfile profile)
    {
        return $"Start-VM -Name '{profile.VmName}'";
    }

    /// <summary>
    /// PowerShell script to force-stop the VM.
    /// </summary>
    public static string StopVmScript(LauncherProfile profile)
    {
        return $"Stop-VM -Name '{profile.VmName}' -Force";
    }

    /// <summary>
    /// PowerShell script to get the VM's IP address from the Default Switch adapter.
    /// </summary>
    public static string GetVmIpScript(LauncherProfile profile)
    {
        return $@"
$adapter = Get-VMNetworkAdapter -VMName '{profile.VmName}'
$ips = $adapter.IPAddresses | Where-Object {{ $_ -match '^\d+\.\d+\.\d+\.\d+$' }}
if ($ips) {{ Write-Output $ips[0] }} else {{ exit 1 }}";
    }

    /// <summary>
    /// PowerShell script to remove SMB shares on shutdown.
    /// </summary>
    public static string RemoveSmbSharesScript()
    {
        return @"
if (Get-SmbShare -Name 'sigil-workspace' -ErrorAction SilentlyContinue) { Remove-SmbShare -Name 'sigil-workspace' -Force }
if (Get-SmbShare -Name 'sigil-profile' -ErrorAction SilentlyContinue) { Remove-SmbShare -Name 'sigil-profile' -Force }
if (Get-SmbShare -Name 'sigil-models' -ErrorAction SilentlyContinue) { Remove-SmbShare -Name 'sigil-models' -Force }
Write-Output 'SMB shares removed'";
    }

    /// <summary>
    /// PowerShell script to check VM state.
    /// </summary>
    public static string GetVmStateScript(LauncherProfile profile)
    {
        return $"(Get-VM -Name '{profile.VmName}').State";
    }
}
