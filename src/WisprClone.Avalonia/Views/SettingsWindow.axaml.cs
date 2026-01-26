using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using System.IO;
using WisprClone.Core;
using WisprClone.Services.Interfaces;

namespace WisprClone.Views;

/// <summary>
/// Settings window for configuring the application (Avalonia version).
/// Auto-saves on every change.
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly ISettingsService _settingsService;
    private readonly IUpdateService? _updateService;
    private bool _isLoading = true; // Prevent auto-save during initial load

    public SettingsWindow()
    {
        InitializeComponent();
        _settingsService = null!; // Will be set via property
    }

    public SettingsWindow(ISettingsService settingsService, IUpdateService? updateService = null)
    {
        InitializeComponent();
        _settingsService = settingsService;
        _updateService = updateService;

        LoadSettings();
        LoadUpdateStatus();
        UpdateSttSettingsPanelVisibility();
        UpdateTtsSettingsPanelVisibility();
        SetupAutoSaveEvents();
        PopulatePiperVoices();

        _isLoading = false; // Enable auto-save after initial load
    }

    private void LoadSettings()
    {
        var settings = _settingsService.Current;

        // About - set version from assembly
        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        if (version != null)
        {
            VersionText.Text = $"v{version.Major}.{version.Minor}.{version.Build}";
        }

        // Speech provider - populate options first based on platform
        PopulateSttProviders();
        SelectComboBoxItemByTag(SpeechProviderComboBox, settings.SpeechProvider.ToString());

        // Azure settings
        AzureKeyTextBox.Text = settings.AzureSubscriptionKey;
        AzureRegionTextBox.Text = settings.AzureRegion;
        UseAzureFallbackCheckBox.IsChecked = settings.UseAzureFallback;

        // OpenAI settings
        OpenAIKeyTextBox.Text = settings.OpenAIApiKey;

        // Language
        SelectComboBoxItemByTag(LanguageComboBox, settings.RecognitionLanguage);

        // Hotkey settings
        DoubleTapIntervalTextBox.Text = settings.DoubleTapIntervalMs.ToString();
        MaxKeyHoldTextBox.Text = settings.MaxKeyHoldDurationMs.ToString();

        // Recording limits
        MaxRecordingDurationTextBox.Text = settings.MaxRecordingDurationSeconds.ToString();

        // Behavior
        AutoCopyCheckBox.IsChecked = settings.AutoCopyToClipboard;
        AutoPasteCheckBox.IsChecked = settings.AutoPasteAfterCopy;
        StartMinimizedCheckBox.IsChecked = settings.StartMinimized;
        RunOnStartupCheckBox.IsChecked = settings.RunOnStartup;

        // Debugging
        EnableLoggingCheckBox.IsChecked = settings.EnableLogging;

        // Update settings
        AutoCheckUpdatesCheckBox.IsChecked = settings.CheckForUpdatesAutomatically;

        // TTS Provider - populate options first based on platform
        PopulateTtsProviders();
        SelectComboBoxItemByTag(TtsProviderComboBox, settings.TtsProvider.ToString());
        UpdateTtsProviderVisibility(); // Handle invalid selection

        // TTS Voice settings
        TtsRateSlider.Value = settings.TtsRate;
        TtsRateValueText.Text = $"{settings.TtsRate:F1}x";
        TtsVolumeSlider.Value = settings.TtsVolume * 100;
        TtsVolumeValueText.Text = $"{(int)(settings.TtsVolume * 100)}%";

        // OpenAI TTS settings
        SelectComboBoxItemByTag(OpenAITtsVoiceComboBox, settings.OpenAITtsVoice);
        SelectComboBoxItemByTag(OpenAITtsModelComboBox, settings.OpenAITtsModel);

        // Azure TTS settings
        SelectComboBoxItemByTag(AzureTtsVoiceComboBox, settings.AzureTtsVoice);

        // Faster-Whisper settings
        SelectComboBoxItemByTag(FasterWhisperModelComboBox, settings.FasterWhisperModel);
        SelectComboBoxItemByTag(FasterWhisperLanguageComboBox, settings.FasterWhisperLanguage);
        FasterWhisperGpuCheckBox.IsChecked = settings.FasterWhisperUseGpu;
        FasterWhisperDeviceIdTextBox.Text = settings.FasterWhisperDeviceId.ToString();
        SelectComboBoxItemByTag(FasterWhisperComputeTypeComboBox, settings.FasterWhisperComputeType);
        FasterWhisperDiarizationCheckBox.IsChecked = settings.FasterWhisperEnableDiarization;
        UpdateFasterWhisperGpuPanelVisibility();

        // Piper TTS settings
        SelectPiperVoice(settings.PiperVoicePath);

        // Whisper Server settings
        SelectComboBoxItemByTag(WhisperServerModelComboBox, settings.WhisperCppModel);
        WhisperServerPortTextBox.Text = settings.WhisperCppServerPort.ToString();

        // Whisper Server streaming settings
        WhisperStreamingEnabledCheckBox.IsChecked = settings.WhisperStreamingEnabled;
        WhisperWindowSlider.Value = settings.WhisperStreamingWindowSeconds;
        WhisperWindowValueText.Text = $"{settings.WhisperStreamingWindowSeconds} seconds";
        UpdateWhisperStreamingPanelVisibility();
    }

    private void LoadUpdateStatus()
    {
        // Set current version
        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        if (version != null)
        {
            CurrentVersionText.Text = $"v{version.Major}.{version.Minor}.{version.Build}";
        }

        // Check if update service available and has update
        if (_updateService != null && _updateService.IsUpdateAvailable && _updateService.LatestVersion != null)
        {
            UpdateAvailablePanel.IsVisible = true;
            NoUpdatePanel.IsVisible = false;
            CheckForUpdatesButton.IsVisible = false;
            NewVersionText.Text = $"v{_updateService.LatestVersion.Major}.{_updateService.LatestVersion.Minor}.{_updateService.LatestVersion.Build}";
        }
        else
        {
            UpdateAvailablePanel.IsVisible = false;
            NoUpdatePanel.IsVisible = true;
            CheckForUpdatesButton.IsVisible = true;
        }
    }

    private void PopulateSttProviders()
    {
        // Use centralized helper for provider population
        SpeechProviderComboBox.Items.Clear();

        foreach (var (provider, displayName, _) in Core.ProviderHelper.GetAvailableSpeechProviders())
        {
            SpeechProviderComboBox.Items.Add(new ComboBoxItem { Content = displayName, Tag = provider.ToString() });
        }

        ProviderInfoText.Text = Core.ProviderHelper.GetSpeechProviderInfoText();
    }

    private void SetupAutoSaveEvents()
    {
        // ComboBox selection changes
        SpeechProviderComboBox.SelectionChanged += (s, e) => { UpdateSttSettingsPanelVisibility(); AutoSave(); };
        TtsProviderComboBox.SelectionChanged += (s, e) => { UpdateTtsSettingsPanelVisibility(); AutoSave(); };
        LanguageComboBox.SelectionChanged += (s, e) => AutoSave();
        FasterWhisperModelComboBox.SelectionChanged += (s, e) => AutoSave();
        FasterWhisperLanguageComboBox.SelectionChanged += (s, e) => AutoSave();
        FasterWhisperComputeTypeComboBox.SelectionChanged += (s, e) => AutoSave();
        WhisperServerModelComboBox.SelectionChanged += (s, e) => { UpdateWhisperServerModelStatus(); AutoSave(); };
        OpenAITtsVoiceComboBox.SelectionChanged += (s, e) => AutoSave();
        OpenAITtsModelComboBox.SelectionChanged += (s, e) => AutoSave();
        AzureTtsVoiceComboBox.SelectionChanged += (s, e) => AutoSave();
        PiperVoiceComboBox.SelectionChanged += (s, e) => AutoSave();
        PiperLanguageFilterComboBox.SelectionChanged += (s, e) => FilterVoiceCatalog();

        // CheckBox changes
        AutoCopyCheckBox.IsCheckedChanged += (s, e) => AutoSave();
        AutoPasteCheckBox.IsCheckedChanged += (s, e) => AutoSave();
        StartMinimizedCheckBox.IsCheckedChanged += (s, e) => AutoSave();
        RunOnStartupCheckBox.IsCheckedChanged += (s, e) => AutoSave();
        AutoCheckUpdatesCheckBox.IsCheckedChanged += (s, e) => AutoSave();
        EnableLoggingCheckBox.IsCheckedChanged += (s, e) => AutoSave();
        UseAzureFallbackCheckBox.IsCheckedChanged += (s, e) => AutoSave();
        FasterWhisperGpuCheckBox.IsCheckedChanged += (s, e) => { UpdateFasterWhisperGpuPanelVisibility(); AutoSave(); };
        FasterWhisperDiarizationCheckBox.IsCheckedChanged += (s, e) => AutoSave();
        WhisperStreamingEnabledCheckBox.IsCheckedChanged += (s, e) => { UpdateWhisperStreamingPanelVisibility(); AutoSave(); };

        // TextBox LostFocus for auto-save
        AzureKeyTextBox.LostFocus += (s, e) => AutoSave();
        AzureRegionTextBox.LostFocus += (s, e) => AutoSave();
        OpenAIKeyTextBox.LostFocus += (s, e) => AutoSave();
        DoubleTapIntervalTextBox.LostFocus += (s, e) => AutoSave();
        MaxKeyHoldTextBox.LostFocus += (s, e) => AutoSave();
        MaxRecordingDurationTextBox.LostFocus += (s, e) => AutoSave();
        FasterWhisperDeviceIdTextBox.LostFocus += (s, e) => AutoSave();
        WhisperServerPortTextBox.LostFocus += (s, e) => AutoSave();

        // Slider value changes (with display update)
        TtsRateSlider.PropertyChanged += (s, e) =>
        {
            if (e.Property.Name == "Value")
            {
                TtsRateValueText.Text = $"{TtsRateSlider.Value:F1}x";
                AutoSave();
            }
        };

        TtsVolumeSlider.PropertyChanged += (s, e) =>
        {
            if (e.Property.Name == "Value")
            {
                TtsVolumeValueText.Text = $"{(int)TtsVolumeSlider.Value}%";
                AutoSave();
            }
        };

        WhisperWindowSlider.PropertyChanged += (s, e) =>
        {
            if (e.Property.Name == "Value")
            {
                WhisperWindowValueText.Text = $"{(int)WhisperWindowSlider.Value} seconds";
                AutoSave();
            }
        };

        // ListBox selection for Piper voice catalog
        PiperVoiceCatalogList.SelectionChanged += (s, e) =>
        {
            DownloadSelectedVoiceButton.IsEnabled = PiperVoiceCatalogList.SelectedItem != null;
        };
    }

    private void AutoSave()
    {
        if (_isLoading) return;
        if (!ValidateInputs()) return;

        _settingsService.Update(settings =>
        {
            // Speech provider
            var providerTag = GetSelectedComboBoxTag(SpeechProviderComboBox);
            if (Enum.TryParse<SpeechProvider>(providerTag, out var provider))
            {
                settings.SpeechProvider = provider;
            }

            // Azure settings
            settings.AzureSubscriptionKey = AzureKeyTextBox.Text ?? string.Empty;
            settings.AzureRegion = AzureRegionTextBox.Text ?? string.Empty;
            settings.UseAzureFallback = UseAzureFallbackCheckBox.IsChecked ?? false;

            // OpenAI settings
            settings.OpenAIApiKey = OpenAIKeyTextBox.Text ?? string.Empty;

            // Language
            settings.RecognitionLanguage = GetSelectedComboBoxTag(LanguageComboBox) ?? "en-US";

            // Hotkey settings
            if (int.TryParse(DoubleTapIntervalTextBox.Text, out var doubleTapInterval))
            {
                settings.DoubleTapIntervalMs = Math.Clamp(doubleTapInterval, 100, 1000);
            }
            if (int.TryParse(MaxKeyHoldTextBox.Text, out var maxKeyHold))
            {
                settings.MaxKeyHoldDurationMs = Math.Clamp(maxKeyHold, 50, 500);
            }

            // Recording limits
            if (int.TryParse(MaxRecordingDurationTextBox.Text, out var maxDuration))
            {
                settings.MaxRecordingDurationSeconds = Math.Clamp(maxDuration, 10, 600);
            }

            // Behavior
            settings.AutoCopyToClipboard = AutoCopyCheckBox.IsChecked ?? true;
            settings.AutoPasteAfterCopy = AutoPasteCheckBox.IsChecked ?? false;
            settings.StartMinimized = StartMinimizedCheckBox.IsChecked ?? false;
            settings.RunOnStartup = RunOnStartupCheckBox.IsChecked ?? false;

            // Debugging
            settings.EnableLogging = EnableLoggingCheckBox.IsChecked ?? false;

            // Update settings
            settings.CheckForUpdatesAutomatically = AutoCheckUpdatesCheckBox.IsChecked ?? true;

            // TTS Provider
            var ttsProviderTag = GetSelectedComboBoxTag(TtsProviderComboBox);
            if (Enum.TryParse<TtsProvider>(ttsProviderTag, out var ttsProvider))
            {
                settings.TtsProvider = ttsProvider;
            }

            // TTS Voice settings
            settings.TtsRate = TtsRateSlider.Value;
            settings.TtsVolume = TtsVolumeSlider.Value / 100.0;

            // OpenAI TTS settings
            settings.OpenAITtsVoice = GetSelectedComboBoxTag(OpenAITtsVoiceComboBox) ?? "alloy";
            settings.OpenAITtsModel = GetSelectedComboBoxTag(OpenAITtsModelComboBox) ?? "tts-1";

            // Azure TTS settings
            settings.AzureTtsVoice = GetSelectedComboBoxTag(AzureTtsVoiceComboBox) ?? "en-US-JennyNeural";

            // Faster-Whisper settings
            settings.FasterWhisperModel = GetSelectedComboBoxTag(FasterWhisperModelComboBox) ?? "base";
            settings.FasterWhisperLanguage = GetSelectedComboBoxTag(FasterWhisperLanguageComboBox) ?? "auto";
            settings.FasterWhisperUseGpu = FasterWhisperGpuCheckBox.IsChecked ?? true;
            if (int.TryParse(FasterWhisperDeviceIdTextBox.Text, out var deviceId))
            {
                settings.FasterWhisperDeviceId = Math.Max(0, deviceId);
            }
            settings.FasterWhisperComputeType = GetSelectedComboBoxTag(FasterWhisperComputeTypeComboBox) ?? "float16";
            settings.FasterWhisperEnableDiarization = FasterWhisperDiarizationCheckBox.IsChecked ?? false;

            // Piper TTS settings
            settings.PiperVoicePath = GetSelectedComboBoxTag(PiperVoiceComboBox) ?? "voices/en_US-amy-medium.onnx";

            // Whisper Server settings
            settings.WhisperCppModel = GetSelectedComboBoxTag(WhisperServerModelComboBox) ?? "base.en";
            if (int.TryParse(WhisperServerPortTextBox.Text, out var serverPort))
            {
                settings.WhisperCppServerPort = Math.Clamp(serverPort, 1024, 65535);
            }

            // Whisper Server streaming settings
            settings.WhisperStreamingEnabled = WhisperStreamingEnabledCheckBox.IsChecked ?? true;
            settings.WhisperStreamingWindowSeconds = (int)WhisperWindowSlider.Value;
        });
    }

    private void UpdateSttSettingsPanelVisibility()
    {
        var selectedTag = GetSelectedComboBoxTag(SpeechProviderComboBox);
        AzureSettingsPanel.IsVisible = selectedTag == "Azure";
        OpenAISettingsPanel.IsVisible = selectedTag == "OpenAI";
        FasterWhisperSettingsPanel.IsVisible = selectedTag == "FasterWhisper";
        WhisperServerSettingsPanel.IsVisible = selectedTag == "WhisperServer";

        // Update Faster-Whisper download panel visibility
        if (selectedTag == "FasterWhisper")
        {
            var exePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "faster-whisper-xxl", "faster-whisper-xxl.exe");
            var exeExists = File.Exists(exePath);
            FasterWhisperDownloadPanel.IsVisible = !exeExists;
        }

        // Update Whisper Server download panel visibility
        if (selectedTag == "WhisperServer")
        {
            var serverExePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "whisper-server", "whisper-server.exe");
            var modelPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "whisper-server", "models", "ggml-base.en.bin");
            var serverExists = File.Exists(serverExePath);
            var modelExists = File.Exists(modelPath);
            WhisperServerDownloadPanel.IsVisible = !serverExists || !modelExists;

            // Update warning text based on what's missing
            if (!serverExists)
                WhisperServerWarningText.Text = "whisper.cpp server not found.";
            else if (!modelExists)
                WhisperServerWarningText.Text = "Default model (ggml-base.en.bin) not found.";

            UpdateWhisperServerModelStatus();
        }
    }

    private static void SelectComboBoxItemByTag(ComboBox comboBox, string tag)
    {
        foreach (var item in comboBox.Items.Cast<ComboBoxItem>())
        {
            if (item.Tag?.ToString() == tag)
            {
                comboBox.SelectedItem = item;
                break;
            }
        }
    }

    private static string? GetSelectedComboBoxTag(ComboBox comboBox)
    {
        return (comboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString();
    }

    private async void CheckForUpdatesButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_updateService == null) return;

        NoUpdatePanel.IsVisible = false;
        UpdateAvailablePanel.IsVisible = false;

        var hasUpdate = await _updateService.CheckForUpdatesAsync();
        LoadUpdateStatus();

        if (!hasUpdate)
        {
            NoUpdatePanel.IsVisible = true;
        }
    }

    private async void DownloadUpdateButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_updateService == null) return;

        DownloadUpdateButton.IsEnabled = false;
        DownloadProgressBar.IsVisible = true;
        DownloadProgressBar.Value = 0;
        DownloadStatusText.Text = "Starting download...";

        var progress = new Progress<double>(p =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                DownloadProgressBar.Value = p;
                DownloadStatusText.Text = $"Downloading... {p:F0}%";
            });
        });

        try
        {
            var filePath = await _updateService.DownloadUpdateAsync(progress);
            if (filePath != null)
            {
                DownloadProgressBar.Value = 100;

                if (OperatingSystem.IsMacOS())
                {
                    // macOS: Immediately install after download (consolidated flow)
                    DownloadStatusText.Text = "Installing update... The app will restart.";

                    // Small delay to show the message before quitting
                    await Task.Delay(500);

                    _updateService.LaunchMacOSUpdate(filePath, () =>
                    {
                        // Force exit immediately - graceful shutdown can hang waiting for threads
                        // The update script is waiting for our PID to exit, so we must exit quickly
                        Environment.Exit(0);
                    });
                }
                else
                {
                    // Windows/Linux: Launch installer directly
                    DownloadStatusText.Text = "Download complete! Opening...";
                    _updateService.LaunchInstaller(filePath);
                    DownloadUpdateButton.IsEnabled = true;
                }
            }
            else
            {
                DownloadStatusText.Text = "Download failed. Please try again.";
                DownloadUpdateButton.IsEnabled = true;
            }
        }
        catch (Exception ex)
        {
            DownloadStatusText.Text = $"Download failed: {ex.Message}";
            DownloadUpdateButton.IsEnabled = true;
        }
    }

    private bool ValidateInputs()
    {
        // Validate numeric inputs - be lenient during auto-save
        if (!string.IsNullOrWhiteSpace(DoubleTapIntervalTextBox.Text))
        {
            if (!int.TryParse(DoubleTapIntervalTextBox.Text, out var doubleTapInterval) ||
                doubleTapInterval < 100 || doubleTapInterval > 1000)
            {
                return false;
            }
        }

        if (!string.IsNullOrWhiteSpace(MaxKeyHoldTextBox.Text))
        {
            if (!int.TryParse(MaxKeyHoldTextBox.Text, out var maxKeyHold) ||
                maxKeyHold < 50 || maxKeyHold > 500)
            {
                return false;
            }
        }

        if (!string.IsNullOrWhiteSpace(MaxRecordingDurationTextBox.Text))
        {
            if (!int.TryParse(MaxRecordingDurationTextBox.Text, out var maxDuration) ||
                maxDuration < 10 || maxDuration > 600)
            {
                return false;
            }
        }

        return true;
    }

    private void RevealAzureKeyButton_Click(object? sender, RoutedEventArgs e)
    {
        if (AzureKeyTextBox.PasswordChar == '*')
        {
            AzureKeyTextBox.PasswordChar = '\0';
            RevealAzureKeyButton.Content = "Hide";
        }
        else
        {
            AzureKeyTextBox.PasswordChar = '*';
            RevealAzureKeyButton.Content = "Show";
        }
    }

    private void RevealOpenAIKeyButton_Click(object? sender, RoutedEventArgs e)
    {
        if (OpenAIKeyTextBox.PasswordChar == '*')
        {
            OpenAIKeyTextBox.PasswordChar = '\0';
            RevealOpenAIKeyButton.Content = "Hide";
        }
        else
        {
            OpenAIKeyTextBox.PasswordChar = '*';
            RevealOpenAIKeyButton.Content = "Show";
        }
    }

    private void UpdateTtsSettingsPanelVisibility()
    {
        var selectedTag = GetSelectedComboBoxTag(TtsProviderComboBox);
        OpenAITtsSettingsPanel.IsVisible = selectedTag == "OpenAI";
        AzureTtsSettingsPanel.IsVisible = selectedTag == "Azure";
        PiperTtsSettingsPanel.IsVisible = selectedTag == "Piper";

        // Update Piper download panel visibility
        if (selectedTag == "Piper")
        {
            var exePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "piper", "piper.exe");
            var exeExists = File.Exists(exePath);
            PiperDownloadPanel.IsVisible = !exeExists;

            // Refresh voices if piper exists
            if (exeExists)
            {
                PopulatePiperVoices();
            }
        }
    }

    private void PopulateTtsProviders()
    {
        // Use centralized helper for provider population
        TtsProviderComboBox.Items.Clear();

        foreach (var (provider, displayName, _) in Core.ProviderHelper.GetAvailableTtsProviders())
        {
            TtsProviderComboBox.Items.Add(new ComboBoxItem { Content = displayName, Tag = provider.ToString() });
        }

        TtsProviderInfoText.Text = Core.ProviderHelper.GetTtsProviderInfoText();
    }

    private void UpdateTtsProviderVisibility()
    {
        // Ensure proper selection after provider list is populated
        var selectedItem = TtsProviderComboBox.SelectedItem as ComboBoxItem;
        var selectedTag = selectedItem?.Tag?.ToString();

        // Get available providers to check if current selection is valid
        var availableProviders = Core.ProviderHelper.GetAvailableTtsProviders();
        var isValidSelection = availableProviders.Any(p => p.Provider.ToString() == selectedTag);

        if (!isValidSelection)
        {
            // Select the default provider for this platform
            var defaultProvider = Core.ProviderHelper.GetDefaultTtsProvider();
            SelectComboBoxItemByTag(TtsProviderComboBox, defaultProvider.ToString());
        }
    }

    private void UpdateFasterWhisperGpuPanelVisibility()
    {
        FasterWhisperGpuSettingsPanel.IsVisible = FasterWhisperGpuCheckBox.IsChecked == true;
    }

    private void UpdateWhisperStreamingPanelVisibility()
    {
        WhisperStreamingSettingsPanel.IsVisible = WhisperStreamingEnabledCheckBox.IsChecked == true;
    }

    private async void DownloadFasterWhisperButton_Click(object? sender, RoutedEventArgs e)
    {
        DownloadFasterWhisperButton.IsEnabled = false;
        FasterWhisperDownloadProgress.IsVisible = true;
        FasterWhisperDownloadProgress.Value = 0;
        FasterWhisperDownloadStatus.Text = "Starting...";

        try
        {
            var helper = new global::WisprClone.Services.DownloadHelper();
            var progress = new Progress<(double progress, string status)>(p =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    FasterWhisperDownloadProgress.Value = p.progress;
                    FasterWhisperDownloadStatus.Text = p.status;
                });
            });

            await helper.DownloadFasterWhisperAsync(progress);

            // Hide download panel on success
            FasterWhisperDownloadPanel.IsVisible = false;
            FasterWhisperDownloadStatus.Text = "Downloaded successfully!";
        }
        catch (Exception ex)
        {
            FasterWhisperDownloadStatus.Text = $"Error: {ex.Message}";
            FasterWhisperDownloadStatus.Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#FFFF6B6B"));
            DownloadFasterWhisperButton.IsEnabled = true;
        }
        finally
        {
            FasterWhisperDownloadProgress.IsVisible = false;
        }
    }

    private async void DownloadPiperButton_Click(object? sender, RoutedEventArgs e)
    {
        DownloadPiperButton.IsEnabled = false;
        PiperDownloadProgress.IsVisible = true;
        PiperDownloadProgress.Value = 0;
        PiperDownloadStatus.Text = "Starting...";

        try
        {
            var helper = new global::WisprClone.Services.DownloadHelper();
            var progress = new Progress<(double progress, string status)>(p =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    PiperDownloadProgress.Value = p.progress;
                    PiperDownloadStatus.Text = p.status;
                });
            });

            await helper.DownloadPiperAsync(progress);

            // Hide download panel and refresh voices on success
            PiperDownloadPanel.IsVisible = false;
            PiperDownloadStatus.Text = "Downloaded successfully!";
            PopulatePiperVoices();
        }
        catch (Exception ex)
        {
            PiperDownloadStatus.Text = $"Error: {ex.Message}";
            PiperDownloadStatus.Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#FFFF6B6B"));
            DownloadPiperButton.IsEnabled = true;
        }
        finally
        {
            PiperDownloadProgress.IsVisible = false;
        }
    }

    private async void DownloadWhisperServerButton_Click(object? sender, RoutedEventArgs e)
    {
        DownloadWhisperServerButton.IsEnabled = false;
        WhisperServerDownloadProgress.IsVisible = true;
        WhisperServerDownloadProgress.Value = 0;
        WhisperServerDownloadStatus.Text = "Starting...";

        try
        {
            var helper = new global::WisprClone.Services.DownloadHelper();
            var progress = new Progress<(double progress, string status)>(p =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    WhisperServerDownloadProgress.Value = p.progress;
                    WhisperServerDownloadStatus.Text = p.status;
                });
            });

            await helper.DownloadWhisperServerAsync(progress);

            // Hide download panel on success
            WhisperServerDownloadPanel.IsVisible = false;
            WhisperServerDownloadStatus.Text = "Downloaded successfully!";

            // Update model status after server download
            UpdateWhisperServerModelStatus();
        }
        catch (Exception ex)
        {
            WhisperServerDownloadStatus.Text = $"Error: {ex.Message}";
            WhisperServerDownloadStatus.Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#FFFF6B6B"));
            DownloadWhisperServerButton.IsEnabled = true;
        }
        finally
        {
            WhisperServerDownloadProgress.IsVisible = false;
        }
    }

    private void UpdateWhisperServerModelStatus()
    {
        var selectedModel = GetSelectedComboBoxTag(WhisperServerModelComboBox);
        if (string.IsNullOrEmpty(selectedModel)) return;

        var modelFileName = $"ggml-{selectedModel}.bin";
        var modelPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "whisper-server", "models", modelFileName);
        var modelExists = File.Exists(modelPath);

        WhisperServerModelDownloadPanel.IsVisible = !modelExists;

        if (modelExists)
        {
            WhisperServerModelStatusText.Text = "Model downloaded";
            WhisperServerModelStatusText.Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#FF4CAF50"));
        }
        else
        {
            WhisperServerModelStatusText.Text = $"Model not downloaded ({modelFileName})";
            WhisperServerModelStatusText.Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#FFFF6B6B"));
        }
    }

    private async void DownloadWhisperModelButton_Click(object? sender, RoutedEventArgs e)
    {
        var selectedModel = GetSelectedComboBoxTag(WhisperServerModelComboBox);
        if (string.IsNullOrEmpty(selectedModel)) return;

        DownloadWhisperModelButton.IsEnabled = false;
        WhisperModelDownloadProgress.IsVisible = true;
        WhisperModelDownloadProgress.Value = 0;
        WhisperModelDownloadStatus.Text = "Starting...";

        try
        {
            var helper = new global::WisprClone.Services.DownloadHelper();
            var progress = new Progress<(double progress, string status)>(p =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    WhisperModelDownloadProgress.Value = p.progress;
                    WhisperModelDownloadStatus.Text = p.status;
                });
            });

            await helper.DownloadWhisperModelAsync(selectedModel, progress);

            // Update model status on success
            UpdateWhisperServerModelStatus();
            WhisperModelDownloadStatus.Text = "Downloaded successfully!";
        }
        catch (Exception ex)
        {
            WhisperModelDownloadStatus.Text = $"Error: {ex.Message}";
            WhisperModelDownloadStatus.Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#FFFF6B6B"));
        }
        finally
        {
            DownloadWhisperModelButton.IsEnabled = true;
            WhisperModelDownloadProgress.IsVisible = false;
        }
    }

    private void PopulatePiperVoices()
    {
        PiperVoiceComboBox.Items.Clear();

        try
        {
            var voicesDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "piper", "voices");
            if (Directory.Exists(voicesDir))
            {
                var onnxFiles = Directory.GetFiles(voicesDir, "*.onnx", SearchOption.AllDirectories);
                foreach (var file in onnxFiles)
                {
                    var fileName = Path.GetFileNameWithoutExtension(file);
                    var relativePath = Path.GetRelativePath(
                        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "piper"),
                        file);

                    // Parse voice name: en_US-amy-medium -> Language: en_US, Name: amy, Quality: medium
                    var parts = fileName.Split('-');
                    var name = parts.Length > 1 ? parts[1] : fileName;
                    var quality = parts.Length > 2 ? parts[2] : "medium";
                    var displayName = $"{char.ToUpper(name[0])}{name[1..]} ({quality})";

                    PiperVoiceComboBox.Items.Add(new ComboBoxItem
                    {
                        Content = displayName,
                        Tag = relativePath
                    });
                }
            }
        }
        catch
        {
            // Ignore errors scanning voices directory
        }

        // Add default voice if no voices found
        if (PiperVoiceComboBox.Items.Count == 0)
        {
            PiperVoiceComboBox.Items.Add(new ComboBoxItem
            {
                Content = "Amy (medium) - default",
                Tag = "voices/en_US-amy-medium.onnx"
            });
        }

        // Select first item by default
        if (PiperVoiceComboBox.Items.Count > 0)
        {
            PiperVoiceComboBox.SelectedIndex = 0;
        }
    }

    private void SelectPiperVoice(string voicePath)
    {
        foreach (var item in PiperVoiceComboBox.Items.Cast<ComboBoxItem>())
        {
            if (item.Tag?.ToString() == voicePath)
            {
                PiperVoiceComboBox.SelectedItem = item;
                return;
            }
        }

        // If not found, select first item
        if (PiperVoiceComboBox.Items.Count > 0)
        {
            PiperVoiceComboBox.SelectedIndex = 0;
        }
    }

    // Piper Voice Catalog
    private WisprClone.Models.PiperVoiceCatalog? _voiceCatalog;
    private List<WisprClone.Models.PiperVoiceEntry> _filteredVoices = new();

    private async void RefreshVoiceCatalogButton_Click(object? sender, RoutedEventArgs e)
    {
        await LoadVoiceCatalogAsync();
    }

    private async Task LoadVoiceCatalogAsync()
    {
        try
        {
            RefreshVoiceCatalogButton.IsEnabled = false;
            VoiceDownloadStatus.Text = "Loading voice catalog...";

            var helper = new global::WisprClone.Services.DownloadHelper();
            _voiceCatalog = await helper.GetPiperVoiceCatalogAsync();

            VoiceDownloadStatus.Text = $"Found {_voiceCatalog.Voices.Count} voices";
            FilterVoiceCatalog();
        }
        catch (Exception ex)
        {
            VoiceDownloadStatus.Text = $"Error: {ex.Message}";
        }
        finally
        {
            RefreshVoiceCatalogButton.IsEnabled = true;
        }
    }

    private void FilterVoiceCatalog()
    {
        if (_voiceCatalog == null) return;

        var languageTag = GetSelectedComboBoxTag(PiperLanguageFilterComboBox);
        var installedVoices = new global::WisprClone.Services.DownloadHelper().GetInstalledPiperVoices();
        var installedKeys = installedVoices.Select(v => v.key).ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (languageTag == "all")
        {
            _filteredVoices = _voiceCatalog.Voices.Values
                .Where(v => !installedKeys.Contains(v.Key)) // Exclude already installed
                .OrderBy(v => v.Language?.NameEnglish)
                .ThenBy(v => v.Name)
                .ThenBy(v => v.Quality)
                .ToList();
        }
        else
        {
            _filteredVoices = _voiceCatalog.Voices.Values
                .Where(v => v.Language?.Family?.Equals(languageTag, StringComparison.OrdinalIgnoreCase) == true)
                .Where(v => !installedKeys.Contains(v.Key)) // Exclude already installed
                .OrderBy(v => v.Name)
                .ThenBy(v => v.Quality)
                .ToList();
        }

        PiperVoiceCatalogList.Items.Clear();
        foreach (var voice in _filteredVoices)
        {
            var sizeMb = voice.GetTotalSizeBytes() / (1024.0 * 1024.0);
            var displayText = languageTag == "all"
                ? $"{voice.Language?.NameEnglish} - {voice.DisplayName} ({sizeMb:F0} MB)"
                : $"{voice.DisplayName} ({sizeMb:F0} MB)";

            PiperVoiceCatalogList.Items.Add(new ListBoxItem
            {
                Content = displayText,
                Tag = voice.Key
            });
        }

        DownloadSelectedVoiceButton.IsEnabled = false;
    }

    private async void DownloadSelectedVoiceButton_Click(object? sender, RoutedEventArgs e)
    {
        if (PiperVoiceCatalogList.SelectedItem is not ListBoxItem selectedItem) return;
        var voiceKey = selectedItem.Tag?.ToString();
        if (string.IsNullOrEmpty(voiceKey)) return;

        var voice = _filteredVoices.FirstOrDefault(v => v.Key == voiceKey);
        if (voice == null) return;

        DownloadSelectedVoiceButton.IsEnabled = false;
        VoiceDownloadProgress.IsVisible = true;
        VoiceDownloadProgress.Value = 0;
        VoiceDownloadStatus.Text = "Starting...";

        try
        {
            var helper = new global::WisprClone.Services.DownloadHelper();
            var progress = new Progress<(double progress, string status)>(p =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    VoiceDownloadProgress.Value = p.progress;
                    VoiceDownloadStatus.Text = p.status;
                });
            });

            await helper.DownloadPiperVoiceAsync(voice, progress);

            VoiceDownloadStatus.Text = "Downloaded! Refreshing...";

            // Refresh installed voices list
            PopulatePiperVoices();

            // Refresh catalog to remove downloaded voice
            FilterVoiceCatalog();

            // Select the newly downloaded voice
            SelectPiperVoice($"voices/{voice.Key}.onnx");

            VoiceDownloadStatus.Text = $"Downloaded {voice.DisplayName}";
        }
        catch (Exception ex)
        {
            VoiceDownloadStatus.Text = $"Error: {ex.Message}";
            VoiceDownloadStatus.Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#FFFF6B6B"));
        }
        finally
        {
            VoiceDownloadProgress.IsVisible = false;
            DownloadSelectedVoiceButton.IsEnabled = PiperVoiceCatalogList.SelectedItem != null;
        }
    }

    private async void RestoreDefaultsButton_Click(object? sender, RoutedEventArgs e)
    {
        // Show confirmation dialog
        var dialog = new ConfirmationDialog(
            "Restore Defaults",
            "Are you sure you want to restore all settings to their default values?\n\nThis will reset all your preferences and cannot be undone.");

        await dialog.ShowDialog(this);

        if (dialog.Result)
        {
            // Reset to defaults
            _isLoading = true; // Prevent auto-save during reload

            _settingsService.ResetToDefaults();
            LoadSettings();
            UpdateSttSettingsPanelVisibility();
            UpdateTtsSettingsPanelVisibility();

            _isLoading = false;
        }
    }
}
