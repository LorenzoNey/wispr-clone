namespace WisprClone.Core;

/// <summary>
/// Application-wide constants.
/// </summary>
public static class Constants
{
    /// <summary>
    /// Application name.
    /// </summary>
    public const string AppName = "WisprClone";

    /// <summary>
    /// Default language for speech recognition.
    /// </summary>
    public const string DefaultLanguage = "en-US";

    /// <summary>
    /// Maximum interval between key taps for double-tap detection (milliseconds).
    /// </summary>
    public const int DefaultDoubleTapIntervalMs = 400;

    /// <summary>
    /// Maximum duration a key can be held to count as a tap (milliseconds).
    /// </summary>
    public const int DefaultMaxKeyHoldDurationMs = 200;

    /// <summary>
    /// Number of consecutive errors before falling back to offline recognition.
    /// </summary>
    public const int MaxErrorsBeforeFallback = 3;

    /// <summary>
    /// Minimum confidence threshold for speech recognition results.
    /// </summary>
    public const float MinConfidenceThreshold = 0.3f;

    /// <summary>
    /// Default maximum recording duration in seconds.
    /// </summary>
    public const int DefaultMaxRecordingDurationSeconds = 120;

    /// <summary>
    /// Minimum allowed maximum recording duration in seconds.
    /// </summary>
    public const int MinMaxRecordingDurationSeconds = 10;

    /// <summary>
    /// Maximum allowed maximum recording duration in seconds (10 minutes).
    /// </summary>
    public const int MaxMaxRecordingDurationSeconds = 600;

    /// <summary>
    /// GitHub repository owner.
    /// </summary>
    public const string GitHubOwner = "blockchainadvisors";

    /// <summary>
    /// GitHub repository name.
    /// </summary>
    public const string GitHubRepo = "wispr-clone";

    /// <summary>
    /// Default update check interval in hours.
    /// </summary>
    public const int DefaultUpdateCheckIntervalHours = 6;
}
