namespace WisprClone.Models;

/// <summary>
/// Event arguments for when an update is available.
/// </summary>
public class UpdateAvailableEventArgs : EventArgs
{
    /// <summary>
    /// The latest available version.
    /// </summary>
    public Version LatestVersion { get; init; } = null!;

    /// <summary>
    /// The current application version.
    /// </summary>
    public Version CurrentVersion { get; init; } = null!;

    /// <summary>
    /// Release notes for the new version.
    /// </summary>
    public string? ReleaseNotes { get; init; }

    /// <summary>
    /// Direct download URL for the platform-specific asset.
    /// </summary>
    public string DownloadUrl { get; init; } = string.Empty;
}

/// <summary>
/// Event arguments for download progress updates.
/// </summary>
public class UpdateDownloadProgressEventArgs : EventArgs
{
    /// <summary>
    /// Download progress as a percentage (0-100).
    /// </summary>
    public double ProgressPercentage { get; init; }

    /// <summary>
    /// Number of bytes downloaded so far.
    /// </summary>
    public long BytesDownloaded { get; init; }

    /// <summary>
    /// Total size in bytes.
    /// </summary>
    public long TotalBytes { get; init; }
}
