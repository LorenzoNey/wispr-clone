using WisprClone.Core;

namespace WisprClone.Models;

/// <summary>
/// Application settings model.
/// </summary>
public class AppSettings
{
    // Speech Provider Selection
    public SpeechProvider SpeechProvider { get; set; } = SpeechProvider.Offline;

    // Azure Speech Settings
    public string AzureSubscriptionKey { get; set; } = string.Empty;
    public string AzureRegion { get; set; } = "eastus";
    public bool UseAzureFallback { get; set; } = false;

    // OpenAI Whisper Settings
    public string OpenAIApiKey { get; set; } = string.Empty;

    // Language Settings
    public string RecognitionLanguage { get; set; } = "en-US";

    // Hotkey Settings
    public int DoubleTapIntervalMs { get; set; } = 400;
    public int MaxKeyHoldDurationMs { get; set; } = 200;

    // UI Settings
    public double OverlayLeft { get; set; } = 100;
    public double OverlayTop { get; set; } = 100;
    public double OverlayOpacity { get; set; } = 0.9;
    public bool StartMinimized { get; set; } = false;
    public bool MinimizeToTray { get; set; } = true;

    // Behavior Settings
    public bool AutoCopyToClipboard { get; set; } = true;
    public bool PlaySoundOnStart { get; set; } = false;
    public bool PlaySoundOnStop { get; set; } = false;
    public bool RunOnStartup { get; set; } = false;

    // Recording Limits
    public int MaxRecordingDurationSeconds { get; set; } = 120; // 2 minutes default

    // Update Settings
    public bool CheckForUpdatesAutomatically { get; set; } = true;

    // Debugging
    public bool EnableLogging { get; set; } = false;
}
