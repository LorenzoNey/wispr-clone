namespace WisprClone.Core;

/// <summary>
/// Unified application mode for coordinating STT and TTS operations.
/// </summary>
public enum AppMode
{
    /// <summary>
    /// Ready and waiting for user activation.
    /// </summary>
    Idle,

    /// <summary>
    /// Speech-to-text: actively listening and transcribing.
    /// </summary>
    SttListening,

    /// <summary>
    /// Speech-to-text: processing final results.
    /// </summary>
    SttProcessing,

    /// <summary>
    /// Text-to-speech: speaking text aloud.
    /// </summary>
    TtsSpeaking,

    /// <summary>
    /// Brief transitional state during mode switches.
    /// </summary>
    Transitioning
}
