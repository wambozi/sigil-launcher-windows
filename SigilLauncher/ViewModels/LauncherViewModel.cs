using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SigilLauncher.Models;
using SigilLauncher.Services;
using SigilLauncher.VM;

namespace SigilLauncher.ViewModels;

/// <summary>
/// MVVM wrapper for VMManager, exposes observable properties for WinUI data binding.
/// Includes build progress tracking and image rebuild support.
/// </summary>
public partial class LauncherViewModel : ObservableObject
{
    private readonly VMManager _vmManager = new();
    private readonly ImageBuilder _imageBuilder = new();

    [ObservableProperty]
    private VMState state = VMState.Stopped;

    [ObservableProperty]
    private string? errorMessage;

    [ObservableProperty]
    private bool sshReady;

    [ObservableProperty]
    private bool daemonReady;

    [ObservableProperty]
    private string statusText = "Stopped";

    [ObservableProperty]
    private bool canStart = true;

    [ObservableProperty]
    private bool canStop;

    [ObservableProperty]
    private bool canOpenShell;

    [ObservableProperty]
    private bool imageReady;

    [ObservableProperty]
    private bool canRebuild = true;

    // Build progress
    [ObservableProperty]
    private bool buildProgressVisible;

    [ObservableProperty]
    private string buildProgressMessage = string.Empty;

    [ObservableProperty]
    private string buildLog = string.Empty;

    [ObservableProperty]
    private bool buildLogVisible;

    // Configuration bindings
    [ObservableProperty]
    private double memoryGB;

    [ObservableProperty]
    private double cpuCores;

    [ObservableProperty]
    private string workspacePath = string.Empty;

    public LauncherViewModel()
    {
        _vmManager.StateChanged += OnVmStateChanged;
        _imageBuilder.StateChanged += OnBuildStateChanged;
        LoadProfileValues();
        ImageReady = _vmManager.ImageReady;
    }

    private void OnVmStateChanged()
    {
        // Marshal to UI thread
        Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()?.TryEnqueue(() =>
        {
            State = _vmManager.State;
            ErrorMessage = _vmManager.ErrorMessage;
            SshReady = _vmManager.SshReady;
            DaemonReady = _vmManager.DaemonReady;
            StatusText = _vmManager.State.DisplayName();
            CanStart = _vmManager.State is VMState.Stopped or VMState.Error;
            CanStop = _vmManager.State == VMState.Running;
            CanOpenShell = _vmManager.DaemonReady;
            ImageReady = _vmManager.ImageReady;
            CanRebuild = _vmManager.State is VMState.Stopped or VMState.Error;
        });
    }

    private void OnBuildStateChanged()
    {
        Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()?.TryEnqueue(() =>
        {
            BuildProgressMessage = _imageBuilder.ProgressMessage;
            BuildLog = _imageBuilder.LogOutput;
            BuildProgressVisible = _imageBuilder.State == BuildState.Building;
            BuildLogVisible = !string.IsNullOrEmpty(_imageBuilder.LogOutput);
            CanRebuild = _imageBuilder.State != BuildState.Building;
            ImageReady = _imageBuilder.ImageExists;

            if (_imageBuilder.State == BuildState.Error)
                ErrorMessage = _imageBuilder.ErrorMessage;
        });
    }

    [RelayCommand]
    private async Task StartAsync()
    {
        await _vmManager.StartAsync();
    }

    [RelayCommand]
    private async Task StopAsync()
    {
        await _vmManager.StopAsync();
    }

    [RelayCommand]
    private void OpenShell()
    {
        _vmManager.LaunchShell();
    }

    [RelayCommand]
    private async Task RebuildAsync()
    {
        CanRebuild = false;
        var profile = _vmManager.CurrentProfile;
        await _imageBuilder.BuildAsync(profile);
    }

    [RelayCommand]
    private void SaveConfiguration()
    {
        var profile = _vmManager.CurrentProfile;
        profile.MemorySize = (long)(MemoryGB * 1024 * 1024 * 1024);
        profile.CpuCount = (int)CpuCores;
        profile.WorkspacePath = WorkspacePath;
        _vmManager.UpdateProfile(profile);
    }

    private void LoadProfileValues()
    {
        var profile = _vmManager.CurrentProfile;
        MemoryGB = (double)profile.MemorySize / (1024 * 1024 * 1024);
        CpuCores = profile.CpuCount;
        WorkspacePath = profile.WorkspacePath;
    }
}
