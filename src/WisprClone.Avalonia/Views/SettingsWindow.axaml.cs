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

        // Speech provider
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

        // Update offline provider visibility based on platform
        UpdateProviderVisibility();
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

    private void UpdateProviderVisibility()
    {
        // Platform-specific provider visibility
        bool isWindows = OperatingSystem.IsWindows();
        bool isMacOS = OperatingSystem.IsMacOS();

        foreach (var item in SpeechProviderComboBox.Items.Cast<ComboBoxItem>())
        {
            var tag = item.Tag?.ToString();
            switch (tag)
            {
                case "Offline":
                    // Windows Speech is only available on Windows
                    item.IsVisible = isWindows;
                    break;
                case "MacOSNative":
                    // macOS Native Speech is only available on macOS
                    item.IsVisible = isMacOS;
                    break;
            }
        }

        // Handle selection if current provider is not available on this platform
        var selectedItem = SpeechProviderComboBox.SelectedItem as ComboBoxItem;
        var selectedTag = selectedItem?.Tag?.ToString();

        if (selectedTag == "Offline" && !isWindows)
        {
            // On macOS, default to MacOSNative; on Linux, default to Azure
            SelectComboBoxItemByTag(SpeechProviderComboBox, isMacOS ? "MacOSNative" : "Azure");
        }
        else if (selectedTag == "MacOSNative" && !isMacOS)
        {
            // On Windows, default to Offline; on Linux, default to Azure
            SelectComboBoxItemByTag(SpeechProviderComboBox, isWindows ? "Offline" : "Azure");
        }

        // Update info text based on platform
        if (isMacOS)
        {
            ProviderInfoText.Text = "macOS Native Speech uses Apple's on-device recognition (no API costs). " +
                                    "Cloud providers (Azure, OpenAI) are also available.";
        }
        else if (!isWindows)
        {
            ProviderInfoText.Text = "Note: On Linux, only cloud providers (Azure, OpenAI) are available.";
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
                DownloadStatusText.Text = "Download complete! Opening...";
                DownloadProgressBar.Value = 100;
                _updateService.LaunchInstaller(filePath);
            }
            else
            {
                DownloadStatusText.Text = "Download failed. Please try again.";
            }
        }
        catch (Exception ex)
        {
            DownloadStatusText.Text = $"Download failed: {ex.Message}";
        }
        finally
        {
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
}
