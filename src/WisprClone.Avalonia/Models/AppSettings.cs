using WisprClone.Core;

namespace WisprClone.Models;

/// <summary>
/// Application settings model.
/// </summary>
public class AppSettings
{
    // Speech Provider Selection (STT)
    public SpeechProvider SpeechProvider { get; set; } = SpeechProvider.Offline;

    // Azure Speech Settings (shared for STT and TTS)
    public string AzureSubscriptionKey { get; set; } = string.Empty;
    public string AzureRegion { get; set; } = "eastus";
    public bool UseAzureFallback { get; set; } = false;

    // OpenAI Settings (shared for STT and TTS)
    public string OpenAIApiKey { get; set; } = string.Empty;

    // Language Settings
    public string RecognitionLanguage { get; set; } = "en-US";

    // STT Hotkey Settings (Ctrl+Ctrl)
    public int DoubleTapIntervalMs { get; set; } = 400;
    public int MaxKeyHoldDurationMs { get; set; } = 200;

    // TTS Provider Selection
    public TtsProvider TtsProvider { get; set; } = TtsProvider.Offline;

    // TTS Voice Settings
    public string TtsVoice { get; set; } = string.Empty;
    public double TtsRate { get; set; } = 1.0;      // 0.5 to 2.0
    public double TtsVolume { get; set; } = 1.0;    // 0.0 to 1.0

    // OpenAI TTS specific
    public string OpenAITtsModel { get; set; } = "tts-1";  // or "tts-1-hd"
    public string OpenAITtsVoice { get; set; } = "alloy";  // alloy/echo/fable/onyx/nova/shimmer

    // Azure TTS specific (reuses AzureSubscriptionKey/AzureRegion)
    public string AzureTtsVoice { get; set; } = "en-US-JennyNeural";

    // TTS Hotkey Settings (Shift+Shift)
    public int TtsDoubleTapIntervalMs { get; set; } = 400;
    public int TtsMaxKeyHoldDurationMs { get; set; } = 200;

    // UI Settings
    public double OverlayLeft { get; set; } = 100;
    public double OverlayTop { get; set; } = 100;
    public double OverlayOpacity { get; set; } = 0.9;
    public bool StartMinimized { get; set; } = false;
    public bool MinimizeToTray { get; set; } = true;

    // Behavior Settings
    public bool AutoCopyToClipboard { get; set; } = true;
    public bool AutoPasteAfterCopy { get; set; } = false;
    public int PasteDelayMs { get; set; } = 100;
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
