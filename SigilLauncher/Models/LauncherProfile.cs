using System.Text.Json;
using System.Text.Json.Serialization;

namespace SigilLauncher.Models;

/// <summary>
/// Persisted launcher settings stored at %APPDATA%\Sigil\launcher\settings.json.
/// </summary>
public class LauncherProfile
{
    /// <summary>RAM allocated to the VM in bytes.</summary>
    [JsonPropertyName("memorySize")]
    public long MemorySize { get; set; }

    /// <summary>Number of CPU cores allocated to the VM.</summary>
    [JsonPropertyName("cpuCount")]
    public int CpuCount { get; set; }

    /// <summary>Host directory shared as /workspace in the VM.</summary>
    [JsonPropertyName("workspacePath")]
    public string WorkspacePath { get; set; } = string.Empty;

    /// <summary>Path to the VHDX disk image.</summary>
    [JsonPropertyName("diskImagePath")]
    public string DiskImagePath { get; set; } = string.Empty;

    /// <summary>Hyper-V VM name.</summary>
    [JsonPropertyName("vmName")]
    public string VmName { get; set; } = "SigilVM";

    /// <summary>Hyper-V virtual switch name.</summary>
    [JsonPropertyName("vmSwitchName")]
    public string VmSwitchName { get; set; } = "Default Switch";

    /// <summary>Editor to install in the VM: "vscode", "neovim", "both", or "none".</summary>
    [JsonPropertyName("editor")]
    public string Editor { get; set; } = "vscode";

    /// <summary>Container engine: "docker" or "none".</summary>
    [JsonPropertyName("containerEngine")]
    public string ContainerEngine { get; set; } = "docker";

    /// <summary>Default shell: "zsh" or "bash".</summary>
    [JsonPropertyName("shell")]
    public string Shell { get; set; } = "zsh";

    /// <summary>Notification/suggestion level (0=silent, 1=digest, 2=ambient, 3=conversational, 4=autonomous).</summary>
    [JsonPropertyName("notificationLevel")]
    public int NotificationLevel { get; set; } = 2;

    /// <summary>Selected local model ID from the catalog, or null for cloud-only inference.</summary>
    [JsonPropertyName("modelId")]
    public string? ModelId { get; set; }

    /// <summary>Path to the downloaded model file on disk, or null if no local model.</summary>
    [JsonPropertyName("modelPath")]
    public string? ModelPath { get; set; }

    public static string SettingsDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Sigil", "launcher");

    public static string SettingsPath =>
        Path.Combine(SettingsDir, "settings.json");

    public static string ProfileDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Sigil", "profiles", "default");

    public static LauncherProfile Default => new()
    {
        MemorySize = 4L * 1024 * 1024 * 1024, // 4 GB
        CpuCount = 2,
        WorkspacePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "workspace"),
        DiskImagePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Sigil", "images", "sigil-vm.vhdx"),
        VmName = "SigilVM",
        VmSwitchName = "Default Switch",
        Editor = "vscode",
        ContainerEngine = "docker",
        Shell = "zsh",
        NotificationLevel = 2,
        ModelId = null,
        ModelPath = null,
    };

    /// <summary>
    /// Returns true if any field that requires a VM image rebuild has changed.
    /// Resource-only changes (RAM, CPU) do not require a rebuild.
    /// </summary>
    public bool NeedsRebuild(LauncherProfile other)
    {
        return Editor != other.Editor ||
               ContainerEngine != other.ContainerEngine ||
               Shell != other.Shell ||
               ModelId != other.ModelId;
    }

    public static LauncherProfile Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                return JsonSerializer.Deserialize<LauncherProfile>(json) ?? Default;
            }
        }
        catch
        {
            // Fall through to default
        }

        return Default;
    }

    public void Save()
    {
        Directory.CreateDirectory(SettingsDir);
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(SettingsPath, json);
    }
}
