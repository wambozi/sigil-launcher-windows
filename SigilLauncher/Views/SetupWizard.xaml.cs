using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SigilLauncher.Models;
using SigilLauncher.Services;

namespace SigilLauncher.Views;

/// <summary>
/// Setup wizard with 6 steps: Welcome, Hardware, Resources, Tools, Model, Build.
/// </summary>
public sealed partial class SetupWizard : Page
{
    private int _currentStep;
    private const int TotalSteps = 6;

    private HardwareInfo? _hardware;
    private ResourceRecommendation? _recommendation;
    private readonly ImageBuilder _imageBuilder = new();
    private readonly List<ModelInfo> _availableModels = new();

    /// <summary>
    /// Fired when the wizard finishes successfully.
    /// </summary>
    public event Action<LauncherProfile>? Completed;

    public SetupWizard()
    {
        this.InitializeComponent();
        UpdateStepVisibility();

        _imageBuilder.StateChanged += OnImageBuilderStateChanged;
    }

    // MARK: - Navigation

    private void OnBackClick(object sender, RoutedEventArgs e)
    {
        if (_currentStep > 0)
        {
            _currentStep--;
            UpdateStepVisibility();
        }
    }

    private void OnNextClick(object sender, RoutedEventArgs e)
    {
        if (_currentStep == 1) // Leaving hardware step
        {
            if (_hardware != null)
            {
                var (meets, _) = HardwareDetector.MeetsMinimumRequirements(_hardware);
                if (!meets) return; // Don't proceed if requirements not met
            }
        }

        if (_currentStep < TotalSteps - 1)
        {
            _currentStep++;
            OnStepEntered(_currentStep);
            UpdateStepVisibility();
        }
    }

    private void OnBuildClick(object sender, RoutedEventArgs e)
    {
        _ = StartBuildAsync();
    }

    private void OnFinishClick(object sender, RoutedEventArgs e)
    {
        var profile = BuildProfile();
        profile.Save();
        Completed?.Invoke(profile);
    }

    private void OnResourceSliderChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (ResMemLabel != null)
            ResMemLabel.Text = ((int)ResMemSlider.Value).ToString();
        if (ResCpuLabel != null)
            ResCpuLabel.Text = ((int)ResCpuSlider.Value).ToString();
    }

    // MARK: - Step Logic

    private void OnStepEntered(int step)
    {
        switch (step)
        {
            case 1: // Hardware
                DetectHardware();
                break;
            case 2: // Resources
                ApplyRecommendation();
                break;
            case 4: // Model
                PopulateModels();
                break;
            case 5: // Build
                BuildStatus.Text = "Ready to build. Click 'Build' to start.";
                break;
        }
    }

    private void DetectHardware()
    {
        _hardware = HardwareDetector.Detect();
        _recommendation = HardwareDetector.Recommend(_hardware);

        HwCpu.Text = $"CPU Cores: {_hardware.CpuCores}";
        HwRam.Text = $"System RAM: {_hardware.TotalRAMGB} GB";
        HwDisk.Text = $"Free Disk: {_hardware.DiskAvailableGB} GB";
        HwArch.Text = $"Architecture: {_hardware.CpuArch}";
        HwGpu.Text = $"GPU: {_hardware.GpuName ?? "Not detected"}";

        var (meets, reason) = HardwareDetector.MeetsMinimumRequirements(_hardware);
        if (meets)
        {
            HwStatus.Text = "Your hardware meets the requirements.";
            HwStatus.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Green);
            HwWarning.Visibility = Visibility.Collapsed;
        }
        else
        {
            HwStatus.Text = "Requirements not met";
            HwStatus.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Red);
            HwWarning.Visibility = Visibility.Visible;
            HwWarningText.Text = reason ?? "Unknown issue";
        }
    }

    private void ApplyRecommendation()
    {
        if (_recommendation == null) return;

        ResMemSlider.Value = _recommendation.MemoryGB;
        ResCpuSlider.Value = _recommendation.Cpus;
        ResMemLabel.Text = _recommendation.MemoryGB.ToString();
        ResCpuLabel.Text = _recommendation.Cpus.ToString();
    }

    private void PopulateModels()
    {
        var vmRAM = (int)ResMemSlider.Value;
        _availableModels.Clear();
        _availableModels.AddRange(ModelCatalog.AvailableModels(vmRAM));

        ModelSelector.Items.Clear();
        foreach (var model in _availableModels)
        {
            var rb = new RadioButton
            {
                GroupName = "ModelGroup",
                Content = $"{model.Name} ({model.Parameters}, {model.SizeGB} GB) — {model.Description}",
                Tag = model.Id,
            };
            ModelSelector.Items.Add(rb);
        }

        ModelNone.IsChecked = true;
    }

    // MARK: - Build

    private async Task StartBuildAsync()
    {
        BtnBuild.IsEnabled = false;
        BtnBack.IsEnabled = false;
        BuildProgress.Visibility = Visibility.Visible;
        BuildLog.Visibility = Visibility.Visible;
        BuildError.Visibility = Visibility.Collapsed;

        var profile = BuildProfile();

        try
        {
            await _imageBuilder.BuildAsync(profile);
        }
        catch
        {
            // Error state handled via StateChanged event
        }
    }

    private void OnImageBuilderStateChanged()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            BuildStatus.Text = _imageBuilder.ProgressMessage;
            BuildLog.Text = _imageBuilder.LogOutput;

            // Auto-scroll log
            if (BuildLog.Text.Length > 0)
            {
                BuildLog.Select(BuildLog.Text.Length, 0);
            }

            switch (_imageBuilder.State)
            {
                case BuildState.Building:
                    BuildProgress.Visibility = Visibility.Visible;
                    BuildProgress.IsIndeterminate = true;
                    break;

                case BuildState.Complete:
                    BuildProgress.Visibility = Visibility.Collapsed;
                    BtnBuild.Visibility = Visibility.Collapsed;
                    BtnFinish.Visibility = Visibility.Visible;
                    break;

                case BuildState.Error:
                    BuildProgress.Visibility = Visibility.Collapsed;
                    BuildError.Visibility = Visibility.Visible;
                    BuildError.Text = _imageBuilder.ErrorMessage ?? "Unknown error";
                    BtnBuild.IsEnabled = true;
                    BtnBack.IsEnabled = true;
                    break;
            }
        });
    }

    // MARK: - Profile Assembly

    private LauncherProfile BuildProfile()
    {
        var profile = LauncherProfile.Default;
        profile.MemorySize = (long)(ResMemSlider.Value * 1024 * 1024 * 1024);
        profile.CpuCount = (int)ResCpuSlider.Value;

        // Tools
        if (ToolEditor.SelectedItem is ComboBoxItem editorItem)
            profile.Editor = editorItem.Tag?.ToString() ?? "vscode";
        if (ToolContainer.SelectedItem is ComboBoxItem containerItem)
            profile.ContainerEngine = containerItem.Tag?.ToString() ?? "docker";
        if (ToolShell.SelectedItem is ComboBoxItem shellItem)
            profile.Shell = shellItem.Tag?.ToString() ?? "zsh";
        if (ToolNotification.SelectedItem is ComboBoxItem notifItem)
            profile.NotificationLevel = int.TryParse(notifItem.Tag?.ToString(), out var level) ? level : 2;

        // Model
        string? selectedModelId = null;
        foreach (var item in ModelSelector.Items)
        {
            if (item is RadioButton rb && rb.IsChecked == true)
            {
                selectedModelId = rb.Tag?.ToString();
                break;
            }
        }

        if (selectedModelId != null)
        {
            profile.ModelId = selectedModelId;
            var model = ModelCatalog.Models.FirstOrDefault(m => m.Id == selectedModelId);
            if (model != null)
            {
                var modelManager = new ModelManager();
                profile.ModelPath = modelManager.ModelPath(model);
            }
        }

        return profile;
    }

    // MARK: - Step Visibility

    private void UpdateStepVisibility()
    {
        StepIndicator.Text = $"Step {_currentStep + 1} of {TotalSteps}";

        StepWelcome.Visibility = _currentStep == 0 ? Visibility.Visible : Visibility.Collapsed;
        StepHardware.Visibility = _currentStep == 1 ? Visibility.Visible : Visibility.Collapsed;
        StepResources.Visibility = _currentStep == 2 ? Visibility.Visible : Visibility.Collapsed;
        StepTools.Visibility = _currentStep == 3 ? Visibility.Visible : Visibility.Collapsed;
        StepModel.Visibility = _currentStep == 4 ? Visibility.Visible : Visibility.Collapsed;
        StepBuild.Visibility = _currentStep == 5 ? Visibility.Visible : Visibility.Collapsed;

        BtnBack.Visibility = _currentStep > 0 ? Visibility.Visible : Visibility.Collapsed;
        BtnNext.Visibility = _currentStep < TotalSteps - 1 ? Visibility.Visible : Visibility.Collapsed;
        BtnBuild.Visibility = _currentStep == TotalSteps - 1 && _imageBuilder.State != BuildState.Complete ? Visibility.Visible : Visibility.Collapsed;
        BtnFinish.Visibility = _imageBuilder.State == BuildState.Complete ? Visibility.Visible : Visibility.Collapsed;
    }
}
