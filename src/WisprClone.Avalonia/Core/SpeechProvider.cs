namespace WisprClone.Core;

/// <summary>
/// Available speech recognition providers.
/// </summary>
public enum SpeechProvider
{
    /// <summary>
    /// Offline recognition using Windows System.Speech.
    /// Only available on Windows.
    /// </summary>
    Offline,

    /// <summary>
    /// Cloud recognition using Azure Cognitive Services.
    /// </summary>
    Azure,

    /// <summary>
    /// Cloud recognition using OpenAI Whisper API (batch, re-transcribes periodically).
    /// </summary>
    OpenAI,

    /// <summary>
    /// Cloud recognition using OpenAI Realtime API (true streaming via WebSocket).
    /// Provides real-time transcription with lower latency than batch Whisper.
    /// </summary>
    OpenAIRealtime,

    /// <summary>
    /// Native macOS speech recognition using Apple's Speech Framework (SFSpeechRecognizer).
    /// Only available on macOS. Provides on-device recognition with no API costs.
    /// </summary>
    MacOSNative,

    /// <summary>
    /// Offline recognition using faster-whisper-xxl executable.
    /// Provides high-quality local transcription using Whisper models.
    /// </summary>
    FasterWhisper,

    /// <summary>
    /// Offline recognition using whisper.cpp server with model kept in memory.
    /// Provides instant transcription (~1 second) by keeping the model loaded.
    /// </summary>
    WhisperServer
}
