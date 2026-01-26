namespace WisprClone.Core;

/// <summary>
/// Available text-to-speech providers.
/// </summary>
public enum TtsProvider
{
    /// <summary>
    /// Offline TTS using Windows System.Speech.Synthesis.
    /// Only available on Windows.
    /// </summary>
    Offline,

    /// <summary>
    /// Cloud TTS using Azure Cognitive Services.
    /// </summary>
    Azure,

    /// <summary>
    /// Cloud TTS using OpenAI TTS API (tts-1 or tts-1-hd models).
    /// </summary>
    OpenAI,

    /// <summary>
    /// Native macOS TTS using AVSpeechSynthesizer.
    /// Only available on macOS.
    /// </summary>
    MacOSNative,

    /// <summary>
    /// Offline TTS using piper executable.
    /// Provides high-quality neural text-to-speech locally.
    /// </summary>
    Piper
}
