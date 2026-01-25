using WisprClone.Core;

namespace WisprClone.Services.Interfaces;

/// <summary>
/// Interface for text-to-speech services.
/// </summary>
public interface ITextToSpeechService : IDisposable
{
    /// <summary>
    /// Raised when speech synthesis starts.
    /// </summary>
    event EventHandler<SpeechSynthesisEventArgs>? SpeakStarted;

    /// <summary>
    /// Raised when speech synthesis completes.
    /// </summary>
    event EventHandler<SpeechSynthesisEventArgs>? SpeakCompleted;

    /// <summary>
    /// Raised during speech synthesis to report progress.
    /// </summary>
    event EventHandler<SpeechProgressEventArgs>? SpeakProgress;

    /// <summary>
    /// Raised when a word boundary is reached during speech synthesis.
    /// Used for word highlighting.
    /// </summary>
    event EventHandler<WordBoundaryEventArgs>? WordBoundary;

    /// <summary>
    /// Raised when speech synthesis encounters an error.
    /// </summary>
    event EventHandler<SpeechSynthesisErrorEventArgs>? SpeakError;

    /// <summary>
    /// Raised when the synthesis state changes.
    /// </summary>
    event EventHandler<SynthesisStateChangedEventArgs>? StateChanged;

    /// <summary>
    /// Current state of the synthesis service.
    /// </summary>
    SynthesisState CurrentState { get; }

    /// <summary>
    /// Name of the provider (Offline, Azure, OpenAI, etc.).
    /// </summary>
    string ProviderName { get; }

    /// <summary>
    /// Current voice being used for synthesis.
    /// </summary>
    string CurrentVoice { get; }

    /// <summary>
    /// Indicates if the service is available and properly configured.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Indicates if this provider supports pause/resume functionality.
    /// </summary>
    bool SupportsPause { get; }

    /// <summary>
    /// Speak the specified text asynchronously.
    /// </summary>
    /// <param name="text">The text to speak.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SpeakAsync(string text, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stop speech synthesis immediately.
    /// </summary>
    Task StopAsync();

    /// <summary>
    /// Pause speech synthesis.
    /// </summary>
    Task PauseAsync();

    /// <summary>
    /// Resume paused speech synthesis.
    /// </summary>
    Task ResumeAsync();

    /// <summary>
    /// Initialize the synthesis engine with specified language and optional voice.
    /// </summary>
    /// <param name="language">Language code (e.g., "en-US").</param>
    /// <param name="voice">Optional voice name/ID.</param>
    Task InitializeAsync(string language = "en-US", string? voice = null);

    /// <summary>
    /// Get available voices for the current provider.
    /// </summary>
    Task<IReadOnlyList<VoiceInfo>> GetAvailableVoicesAsync();

    /// <summary>
    /// Set the speech rate.
    /// </summary>
    /// <param name="rate">Rate multiplier (0.5 to 2.0, where 1.0 is normal).</param>
    void SetRate(double rate);

    /// <summary>
    /// Set the speech volume.
    /// </summary>
    /// <param name="volume">Volume level (0.0 to 1.0).</param>
    void SetVolume(double volume);
}
