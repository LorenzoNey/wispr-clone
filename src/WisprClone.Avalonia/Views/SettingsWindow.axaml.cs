using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using WisprClone.Core;
using WisprClone.Services.Interfaces;

namespace WisprClone.Views;

/// <summary>
/// Settings window for configuring the application (Avalonia version).
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly ISettingsService _settingsService;
    private readonly IUpdateService? _updateService;

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
        UpdateApiSettingsPanelVisibility();
        UpdateTtsSettingsPanelVisibility();
        SetupTtsSliderEvents();
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
        // Dynamically populate STT providers based on platform
        bool isWindows = OperatingSystem.IsWindows();
        bool isMacOS = OperatingSystem.IsMacOS();

        SpeechProviderComboBox.Items.Clear();

        // Add platform-specific local option first
        if (isWindows)
        {
            SpeechProviderComboBox.Items.Add(new ComboBoxItem { Content = "Local (Windows Speech)", Tag = "Offline" });
        }
        else if (isMacOS)
        {
            SpeechProviderComboBox.Items.Add(new ComboBoxItem { Content = "Local (macOS Speech)", Tag = "MacOSNative" });
        }

        // Add cloud providers (available on all platforms)
        SpeechProviderComboBox.Items.Add(new ComboBoxItem { Content = "Azure Speech", Tag = "Azure" });
        SpeechProviderComboBox.Items.Add(new ComboBoxItem { Content = "OpenAI Whisper", Tag = "OpenAI" });

        // Update info text based on platform
        if (isMacOS)
        {
            ProviderInfoText.Text = "Ctrl+Ctrl to dictate. Local uses Apple's on-device recognition.";
        }
        else if (isWindows)
        {
            ProviderInfoText.Text = "Ctrl+Ctrl to dictate. Local uses Windows Speech Recognition.";
        }
        else
        {
            ProviderInfoText.Text = "Ctrl+Ctrl to dictate. On Linux, only cloud providers are available.";
        }
    }

    private void SpeechProviderComboBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        UpdateApiSettingsPanelVisibility();
    }

    private void UpdateApiSettingsPanelVisibility()
    {
        var selectedTag = GetSelectedComboBoxTag(SpeechProviderComboBox);
        AzureSettingsPanel.IsVisible = selectedTag == "Azure";
        OpenAISettingsPanel.IsVisible = selectedTag == "OpenAI";
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

    private void SaveButton_Click(object? sender, RoutedEventArgs e)
    {
        if (!ValidateInputs())
            return;

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
        });

        Close();
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
        // Validate numeric inputs
        if (!int.TryParse(DoubleTapIntervalTextBox.Text, out var doubleTapInterval) ||
            doubleTapInterval < 100 || doubleTapInterval > 1000)
        {
            DoubleTapIntervalTextBox.Focus();
            return false;
        }

        if (!int.TryParse(MaxKeyHoldTextBox.Text, out var maxKeyHold) ||
            maxKeyHold < 50 || maxKeyHold > 500)
        {
            MaxKeyHoldTextBox.Focus();
            return false;
        }

        if (!int.TryParse(MaxRecordingDurationTextBox.Text, out var maxDuration) ||
            maxDuration < 10 || maxDuration > 600)
        {
            MaxRecordingDurationTextBox.Focus();
            return false;
        }

        return true;
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        Close();
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

    private void TtsProviderComboBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        UpdateTtsSettingsPanelVisibility();
    }

    private void UpdateTtsSettingsPanelVisibility()
    {
        var selectedTag = GetSelectedComboBoxTag(TtsProviderComboBox);
        OpenAITtsSettingsPanel.IsVisible = selectedTag == "OpenAI";
        AzureTtsSettingsPanel.IsVisible = selectedTag == "Azure";
    }

    private void PopulateTtsProviders()
    {
        // Dynamically populate TTS providers based on platform
        bool isWindows = OperatingSystem.IsWindows();
        bool isMacOS = OperatingSystem.IsMacOS();

        TtsProviderComboBox.Items.Clear();

        // Add platform-specific local option first
        if (isWindows)
        {
            TtsProviderComboBox.Items.Add(new ComboBoxItem { Content = "Local (Windows Speech)", Tag = "Offline" });
        }
        else if (isMacOS)
        {
            TtsProviderComboBox.Items.Add(new ComboBoxItem { Content = "Local (macOS Speech)", Tag = "MacOSNative" });
        }

        // Add cloud providers (available on all platforms)
        TtsProviderComboBox.Items.Add(new ComboBoxItem { Content = "Azure Speech", Tag = "Azure" });
        TtsProviderComboBox.Items.Add(new ComboBoxItem { Content = "OpenAI TTS", Tag = "OpenAI" });

        // Update info text based on platform
        if (isMacOS)
        {
            TtsProviderInfoText.Text = "Shift+Shift to read clipboard. Local uses macOS system voices.";
        }
        else if (!isWindows)
        {
            TtsProviderInfoText.Text = "Shift+Shift to read clipboard. On Linux, only cloud providers are available.";
        }
    }

    private void UpdateTtsProviderVisibility()
    {
        // This method now just ensures proper selection after provider list is populated
        bool isWindows = OperatingSystem.IsWindows();
        bool isMacOS = OperatingSystem.IsMacOS();

        var selectedItem = TtsProviderComboBox.SelectedItem as ComboBoxItem;
        var selectedTag = selectedItem?.Tag?.ToString();

        // Handle selection if current provider is not available on this platform
        if (selectedTag == "Offline" && !isWindows)
        {
            // On macOS, default to MacOSNative; on Linux, default to Azure
            SelectComboBoxItemByTag(TtsProviderComboBox, isMacOS ? "MacOSNative" : "Azure");
        }
        else if (selectedTag == "MacOSNative" && !isMacOS)
        {
            // On Windows, default to Offline; on Linux, default to Azure
            SelectComboBoxItemByTag(TtsProviderComboBox, isWindows ? "Offline" : "Azure");
        }
    }

    private void SetupTtsSliderEvents()
    {
        TtsRateSlider.PropertyChanged += (s, e) =>
        {
            if (e.Property.Name == "Value")
            {
                TtsRateValueText.Text = $"{TtsRateSlider.Value:F1}x";
            }
        };

        TtsVolumeSlider.PropertyChanged += (s, e) =>
        {
            if (e.Property.Name == "Value")
            {
                TtsVolumeValueText.Text = $"{(int)TtsVolumeSlider.Value}%";
            }
        };
    }
}
