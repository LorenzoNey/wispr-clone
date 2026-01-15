namespace WisprClone.Core;

/// <summary>
/// Event arguments for transcription events.
/// </summary>
public class TranscriptionEventArgs : EventArgs
{
    public string Text { get; }
    public bool IsFinal { get; }

    /// <summary>
    /// Indicates if this update contains a newly finalized segment.
    /// When true, the text should be copied to clipboard (progressive update).
    /// When false, this is an interim hypothesis that may change.
    /// </summary>
    public bool IsSegmentFinalized { get; }

    public TranscriptionEventArgs(string text, bool isFinal, bool isSegmentFinalized = false)
    {
        Text = text;
        IsFinal = isFinal;
        IsSegmentFinalized = isSegmentFinalized;
    }
}

/// <summary>
/// Event arguments for recognition errors.
/// </summary>
public class RecognitionErrorEventArgs : EventArgs
{
    public string Message { get; }
    public Exception? Exception { get; }

    public RecognitionErrorEventArgs(string message, Exception? exception = null)
    {
        Message = message;
        Exception = exception;
    }
}

/// <summary>
/// Event arguments for state changes.
/// </summary>
public class RecognitionStateChangedEventArgs : EventArgs
{
    public RecognitionState OldState { get; }
    public RecognitionState NewState { get; }

    public RecognitionStateChangedEventArgs(RecognitionState oldState, RecognitionState newState)
    {
        OldState = oldState;
        NewState = newState;
    }
}

/// <summary>
/// Event arguments for transcription state changes.
/// </summary>
public class TranscriptionStateChangedEventArgs : EventArgs
{
    public TranscriptionState OldState { get; }
    public TranscriptionState NewState { get; }

    public TranscriptionStateChangedEventArgs(TranscriptionState oldState, TranscriptionState newState)
    {
        OldState = oldState;
        NewState = newState;
    }
}
