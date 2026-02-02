using AITextVoice.Core;

namespace AITextVoice.Models;

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

    // STT Hotkey Settings (Ctrl+Ctrl by default)
    // New format: serialized HotkeyConfiguration string
    public string SttHotkeyConfig { get; set; } = "DoubleTap:Ctrl::400:200:1000";

    // Legacy properties (kept for backward compatibility, will be migrated on first load)
    [Obsolete("Use SttHotkeyConfig instead")]
    public string SttHotkey { get; set; } = "Ctrl";  // "Ctrl", "Shift", or "Alt"
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

    // Faster-Whisper-XXL STT Settings
    public string FasterWhisperModel { get; set; } = "base";
    public string FasterWhisperLanguage { get; set; } = "auto";
    public bool FasterWhisperUseGpu { get; set; } = true;
    public int FasterWhisperDeviceId { get; set; } = 0;
    public string FasterWhisperComputeType { get; set; } = "float16";
    public bool FasterWhisperEnableDiarization { get; set; } = false;
    public string FasterWhisperVadMethod { get; set; } = "silero";
    public bool FasterWhisperUseServer { get; set; } = true; // Use persistent server mode

    // Whisper.cpp Server Settings (for persistent model loading)
    public string WhisperCppModel { get; set; } = "base.en"; // ggml model name
    public int WhisperCppServerPort { get; set; } = 8178;

    // Whisper Server Streaming Settings
    public bool WhisperStreamingEnabled { get; set; } = false;      // Toggle streaming mode
    public int WhisperStreamingWindowSeconds { get; set; } = 8;     // Sliding window size
    public int WhisperStreamingIntervalMs { get; set; } = 1500;     // Transcription interval

    // Piper TTS Settings
    public string PiperVoicePath { get; set; } = "voices/en_US-amy-medium.onnx";

    // TTS Hotkey Settings (Shift+Shift by default)
    // New format: serialized HotkeyConfiguration string
    public string TtsHotkeyConfig { get; set; } = "DoubleTap:Shift::400:200:1000";

    // Legacy properties (kept for backward compatibility, will be migrated on first load)
    [Obsolete("Use TtsHotkeyConfig instead")]
    public string TtsHotkey { get; set; } = "Shift";  // "Ctrl", "Shift", or "Alt"
    public int TtsDoubleTapIntervalMs { get; set; } = 400;
    public int TtsMaxKeyHoldDurationMs { get; set; } = 200;

    // UI Settings
    public double OverlayLeft { get; set; } = 100;
    public double OverlayTop { get; set; } = 100;
    public double OverlayOpacity { get; set; } = 0.9;
    public bool StartMinimized { get; set; } = true;
    public bool MinimizeToTray { get; set; } = true;
    public int OverlayAutoHideSeconds { get; set; } = 0; // 0 = never auto-hide

    // Behavior Settings
    public bool AutoCopyToClipboard { get; set; } = false;
    public bool AutoPasteAfterCopy { get; set; } = true;
    public int PasteDelayMs { get; set; } = 100;
    public bool PlaySoundOnStart { get; set; } = false;
    public bool PlaySoundOnStop { get; set; } = false;
    public bool RunOnStartup { get; set; } = true;

    // Recording Limits
    public int MaxRecordingDurationSeconds { get; set; } = 120; // 2 minutes default

    // Update Settings
    public bool CheckForUpdatesAutomatically { get; set; } = true;

    // Debugging
    public bool EnableLogging { get; set; } = false;

    /// <summary>
    /// Gets the STT hotkey configuration, handling legacy migration.
    /// </summary>
    public HotkeyConfiguration GetSttHotkeyConfiguration()
    {
        // If the new config is not the default, use it
        if (!string.IsNullOrEmpty(SttHotkeyConfig) && SttHotkeyConfig != "DoubleTap:Ctrl::400:200:1000")
        {
            return HotkeyConfiguration.Deserialize(SttHotkeyConfig);
        }

        // Check if legacy settings have been customized
#pragma warning disable CS0618 // Type or member is obsolete
        if (SttHotkey != "Ctrl" || DoubleTapIntervalMs != 400 || MaxKeyHoldDurationMs != 200)
        {
            // Migrate legacy settings to new format
            return new HotkeyConfiguration
            {
                ActivationType = HotkeyActivationType.DoubleTap,
                PrimaryKey = SttHotkey,
                DoubleTapIntervalMs = DoubleTapIntervalMs,
                MaxTapHoldDurationMs = MaxKeyHoldDurationMs
            };
        }
#pragma warning restore CS0618

        return HotkeyConfiguration.Deserialize(SttHotkeyConfig);
    }

    /// <summary>
    /// Gets the TTS hotkey configuration, handling legacy migration.
    /// </summary>
    public HotkeyConfiguration GetTtsHotkeyConfiguration()
    {
        // If the new config is not the default, use it
        if (!string.IsNullOrEmpty(TtsHotkeyConfig) && TtsHotkeyConfig != "DoubleTap:Shift::400:200:1000")
        {
            return HotkeyConfiguration.Deserialize(TtsHotkeyConfig);
        }

        // Check if legacy settings have been customized
#pragma warning disable CS0618 // Type or member is obsolete
        if (TtsHotkey != "Shift" || TtsDoubleTapIntervalMs != 400 || TtsMaxKeyHoldDurationMs != 200)
        {
            // Migrate legacy settings to new format
            return new HotkeyConfiguration
            {
                ActivationType = HotkeyActivationType.DoubleTap,
                PrimaryKey = TtsHotkey,
                DoubleTapIntervalMs = TtsDoubleTapIntervalMs,
                MaxTapHoldDurationMs = TtsMaxKeyHoldDurationMs
            };
        }
#pragma warning restore CS0618

        return HotkeyConfiguration.Deserialize(TtsHotkeyConfig);
    }
}
