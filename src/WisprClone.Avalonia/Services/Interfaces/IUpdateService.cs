using WisprClone.Models;

namespace WisprClone.Services.Interfaces;

/// <summary>
/// Interface for application update checking and downloading.
/// </summary>
public interface IUpdateService : IDisposable
{
    /// <summary>
    /// Gets whether an update is available.
    /// </summary>
    bool IsUpdateAvailable { get; }

    /// <summary>
    /// Gets the latest available version, or null if not checked yet.
    /// </summary>
    Version? LatestVersion { get; }

    /// <summary>
    /// Gets the current application version.
    /// </summary>
    Version CurrentVersion { get; }

    /// <summary>
    /// Gets the release notes for the latest version.
    /// </summary>
    string? ReleaseNotes { get; }

    /// <summary>
    /// Gets the download URL for the platform-specific asset.
    /// </summary>
    string? DownloadUrl { get; }

    /// <summary>
    /// Raised when update availability changes.
    /// </summary>
    event EventHandler<UpdateAvailableEventArgs>? UpdateAvailable;

    /// <summary>
    /// Raised when download progress changes.
    /// </summary>
    event EventHandler<UpdateDownloadProgressEventArgs>? DownloadProgressChanged;

    /// <summary>
    /// Checks for updates asynchronously.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if an update is available.</returns>
    Task<bool> CheckForUpdatesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts periodic update checking.
    /// </summary>
    /// <param name="interval">Interval between checks.</param>
    void StartPeriodicChecks(TimeSpan interval);

    /// <summary>
    /// Stops periodic update checking.
    /// </summary>
    void StopPeriodicChecks();

    /// <summary>
    /// Downloads the update to temp directory and returns the file path.
    /// </summary>
    /// <param name="progress">Progress reporter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Path to the downloaded file, or null if failed.</returns>
    Task<string?> DownloadUpdateAsync(IProgress<double>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Launches the downloaded installer or opens the containing folder.
    /// </summary>
    /// <param name="installerPath">Path to the downloaded file.</param>
    void LaunchInstaller(string installerPath);
}
