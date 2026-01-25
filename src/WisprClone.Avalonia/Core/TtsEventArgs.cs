namespace WisprClone.Core;

/// <summary>
/// Represents the state of the speech synthesis engine.
/// </summary>
public enum SynthesisState
{
    Idle,
    Initializing,
    Speaking,
    Paused,
    Error
}

/// <summary>
/// Event args for speech synthesis started event.
/// </summary>
public class SpeechSynthesisEventArgs : EventArgs
{
    /// <summary>
    /// The text being synthesized.
    /// </summary>
    public string Text { get; }

    public SpeechSynthesisEventArgs(string text)
    {
        Text = text;
    }
}

/// <summary>
/// Event args for speech synthesis progress (percentage).
/// </summary>
public class SpeechProgressEventArgs : EventArgs
{
    /// <summary>
    /// Progress percentage (0-100).
    /// </summary>
    public double ProgressPercent { get; }

    /// <summary>
    /// Character position in the original text.
    /// </summary>
    public int CharacterPosition { get; }

    /// <summary>
    /// Total character count of the text.
    /// </summary>
    public int TotalCharacters { get; }

    public SpeechProgressEventArgs(double progressPercent, int characterPosition, int totalCharacters)
    {
        ProgressPercent = progressPercent;
        CharacterPosition = characterPosition;
        TotalCharacters = totalCharacters;
    }
}

/// <summary>
/// Event args for word boundary events during speech synthesis.
/// </summary>
public class WordBoundaryEventArgs : EventArgs
{
    /// <summary>
    /// Character position of the word in the original text.
    /// </summary>
    public int CharacterPosition { get; }

    /// <summary>
    /// Length of the word in characters.
    /// </summary>
    public int CharacterLength { get; }

    /// <summary>
    /// The word being spoken.
    /// </summary>
    public string Word { get; }

    /// <summary>
    /// Audio offset from the start of synthesis.
    /// </summary>
    public TimeSpan AudioOffset { get; }

    public WordBoundaryEventArgs(int characterPosition, int characterLength, string word, TimeSpan audioOffset)
    {
        CharacterPosition = characterPosition;
        CharacterLength = characterLength;
        Word = word;
        AudioOffset = audioOffset;
    }
}

/// <summary>
/// Event args for speech synthesis errors.
/// </summary>
public class SpeechSynthesisErrorEventArgs : EventArgs
{
    /// <summary>
    /// Error message.
    /// </summary>
    public string Message { get; }

    /// <summary>
    /// The exception that caused the error, if any.
    /// </summary>
    public Exception? Exception { get; }

    public SpeechSynthesisErrorEventArgs(string message, Exception? exception = null)
    {
        Message = message;
        Exception = exception;
    }
}

/// <summary>
/// Event args for synthesis state changes.
/// </summary>
public class SynthesisStateChangedEventArgs : EventArgs
{
    /// <summary>
    /// Previous state.
    /// </summary>
    public SynthesisState OldState { get; }

    /// <summary>
    /// New state.
    /// </summary>
    public SynthesisState NewState { get; }

    public SynthesisStateChangedEventArgs(SynthesisState oldState, SynthesisState newState)
    {
        OldState = oldState;
        NewState = newState;
    }
}

/// <summary>
/// Represents information about an available TTS voice.
/// </summary>
public class VoiceInfo
{
    /// <summary>
    /// Voice ID (used for selection).
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Display name of the voice.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Language/locale of the voice (e.g., "en-US").
    /// </summary>
    public string Language { get; set; } = string.Empty;

    /// <summary>
    /// Gender of the voice.
    /// </summary>
    public string Gender { get; set; } = string.Empty;

    /// <summary>
    /// Provider-specific additional info.
    /// </summary>
    public string? Description { get; set; }

    public override string ToString() => $"{Name} ({Language})";
}
