using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http.Headers;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using WisprClone.Core;
using WisprClone.Models;
using WisprClone.Services.Interfaces;

namespace WisprClone.Services;

/// <summary>
/// Service for checking and downloading application updates from GitHub releases.
/// </summary>
public class UpdateService : IUpdateService
{
    private readonly HttpClient _httpClient;
    private readonly ILoggingService _loggingService;
    private readonly SemaphoreSlim _checkLock = new(1, 1);
    private PeriodicTimer? _periodicTimer;
    private CancellationTokenSource? _periodicCts;
    private GitHubRelease? _latestRelease;
    private bool _disposed;

    public bool IsUpdateAvailable { get; private set; }
    public Version? LatestVersion { get; private set; }
    public Version CurrentVersion { get; }
    public string? ReleaseNotes => _latestRelease?.Body;
    public string? DownloadUrl { get; private set; }

    public event EventHandler<UpdateAvailableEventArgs>? UpdateAvailable;
    public event EventHandler<UpdateDownloadProgressEventArgs>? DownloadProgressChanged;

    public UpdateService(ILoggingService loggingService)
    {
        _loggingService = loggingService;
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd($"{Constants.AppName}-UpdateChecker");
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json"));

        CurrentVersion = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);
    }

    public async Task<bool> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
    {
        Log($"CheckForUpdatesAsync called. Current version: {CurrentVersion}");
        await _checkLock.WaitAsync(cancellationToken);
        try
        {
            var apiUrl = $"https://api.github.com/repos/{Constants.GitHubOwner}/{Constants.GitHubRepo}/releases/latest";
            Log($"Fetching: {apiUrl}");
            var response = await _httpClient.GetAsync(apiUrl, cancellationToken);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            _latestRelease = JsonSerializer.Deserialize<GitHubRelease>(json);

            if (_latestRelease == null || _latestRelease.Draft || _latestRelease.Prerelease)
            {
                IsUpdateAvailable = false;
                return false;
            }

            // Parse version from tag (e.g., "v2.1.0" -> "2.1.0")
            var versionString = _latestRelease.TagName.TrimStart('v', 'V');
            Log($"Latest release tag: {_latestRelease.TagName}, parsed version: {versionString}");
            if (Version.TryParse(versionString, out var latestVersion))
            {
                LatestVersion = latestVersion;

                // Compare only major.minor.build (ignore revision)
                var currentComparable = new Version(CurrentVersion.Major, CurrentVersion.Minor, CurrentVersion.Build);
                var latestComparable = new Version(latestVersion.Major, latestVersion.Minor, latestVersion.Build);

                IsUpdateAvailable = latestComparable > currentComparable;
                DownloadUrl = GetPlatformSpecificAssetUrl();
                Log($"Version comparison: current={currentComparable}, latest={latestComparable}, updateAvailable={IsUpdateAvailable}");

                if (IsUpdateAvailable)
                {
                    Log("Raising UpdateAvailable event");
                    UpdateAvailable?.Invoke(this, new UpdateAvailableEventArgs
                    {
                        LatestVersion = latestVersion,
                        CurrentVersion = CurrentVersion,
                        ReleaseNotes = _latestRelease.Body,
                        DownloadUrl = DownloadUrl ?? string.Empty
                    });
                }

                return IsUpdateAvailable;
            }

            Log($"Failed to parse version: {versionString}");
            return false;
        }
        catch (Exception ex)
        {
            Log($"CheckForUpdatesAsync error: {ex.Message}");
            return false;
        }
        finally
        {
            _checkLock.Release();
        }
    }

    public void StartPeriodicChecks(TimeSpan interval)
    {
        StopPeriodicChecks();

        _periodicCts = new CancellationTokenSource();
        _periodicTimer = new PeriodicTimer(interval);

        _ = RunPeriodicChecksAsync(_periodicCts.Token);
    }

    private async Task RunPeriodicChecksAsync(CancellationToken cancellationToken)
    {
        try
        {
            Log("Periodic update checker started");
            while (await _periodicTimer!.WaitForNextTickAsync(cancellationToken))
            {
                Log("Periodic update check triggered");
                var hasUpdate = await CheckForUpdatesAsync(cancellationToken);
                Log($"Periodic update check completed. Update available: {hasUpdate}");
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when stopping
            Log("Periodic update checker stopped");
        }
        catch (Exception ex)
        {
            Log($"Periodic update checker error: {ex.Message}");
        }
    }

    private void Log(string message)
    {
        _loggingService.Log("UpdateService", message);
    }

    public void StopPeriodicChecks()
    {
        _periodicCts?.Cancel();
        _periodicCts?.Dispose();
        _periodicCts = null;
        _periodicTimer?.Dispose();
        _periodicTimer = null;
    }

    public async Task<string?> DownloadUpdateAsync(IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        var assetUrl = DownloadUrl ?? GetPlatformSpecificAssetUrl();
        if (string.IsNullOrEmpty(assetUrl))
            return null;

        try
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "WisprClone-Update");
            Directory.CreateDirectory(tempDir);

            var fileName = Path.GetFileName(new Uri(assetUrl).LocalPath);
            var filePath = Path.Combine(tempDir, fileName);

            // Delete existing file if present
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }

            using var response = await _httpClient.GetAsync(assetUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? -1;
            var downloadedBytes = 0L;

            await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

            var buffer = new byte[8192];
            int bytesRead;

            while ((bytesRead = await contentStream.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                downloadedBytes += bytesRead;

                if (totalBytes > 0)
                {
                    var percentage = (double)downloadedBytes / totalBytes * 100;
                    progress?.Report(percentage);
                    DownloadProgressChanged?.Invoke(this, new UpdateDownloadProgressEventArgs
                    {
                        ProgressPercentage = percentage,
                        BytesDownloaded = downloadedBytes,
                        TotalBytes = totalBytes
                    });
                }
            }

            return filePath;
        }
        catch
        {
            return null;
        }
    }

    private string? GetPlatformSpecificAssetUrl()
    {
        if (_latestRelease == null)
            return null;

        // Determine platform - match asset naming from release workflow
        string[] assetPatterns;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Windows installer is named "WisprClone-Setup-X.X.X.exe"
            assetPatterns = ["Setup", "Windows-x64"];
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            // macOS DMG is named "WisprClone-macOS-{arch}-X.X.X.dmg"
            // Detect architecture: ARM64 (Apple Silicon) vs x64 (Intel)
            var arch = RuntimeInformation.ProcessArchitecture;
            if (arch == Architecture.Arm64)
                assetPatterns = ["macOS-arm64"];
            else
                assetPatterns = ["macOS-x64"];
        }
        else // Linux
        {
            // Linux AppImage is named "WisprClone-Linux-x64-X.X.X.AppImage"
            assetPatterns = ["Linux-x64"];
        }

        var asset = _latestRelease.Assets.FirstOrDefault(a =>
            assetPatterns.Any(pattern => a.Name.Contains(pattern, StringComparison.OrdinalIgnoreCase)));

        return asset?.BrowserDownloadUrl;
    }

    public void LaunchInstaller(string installerPath)
    {
        if (!File.Exists(installerPath))
            return;

        try
        {
            var extension = Path.GetExtension(installerPath).ToLowerInvariant();

            // For zip/tar.gz files, open the containing folder
            if (extension is ".zip" or ".gz")
            {
                var directory = Path.GetDirectoryName(installerPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = directory,
                        UseShellExecute = true
                    });
                }
            }
            else
            {
                // For executables/installers, run them directly
                Process.Start(new ProcessStartInfo
                {
                    FileName = installerPath,
                    UseShellExecute = true
                });
            }
        }
        catch
        {
            // Silently fail
        }
    }

    public void LaunchMacOSUpdate(string dmgPath, Action? onBeforeQuit = null)
    {
        if (!OperatingSystem.IsMacOS() || !File.Exists(dmgPath))
            return;

        try
        {
            var currentPid = Environment.ProcessId;
            var appPath = "/Applications/WisprClone.app";

            // Create updater script
            var scriptPath = Path.Combine(Path.GetTempPath(), "wispr-update.sh");
            var script = $@"#!/bin/bash
# WisprClone macOS Updater
# Wait for app to quit
while kill -0 {currentPid} 2>/dev/null; do sleep 0.5; done

# Mount DMG silently
MOUNT_OUTPUT=$(hdiutil attach ""{dmgPath}"" -nobrowse -quiet 2>&1)
MOUNT_POINT=$(echo ""$MOUNT_OUTPUT"" | grep -o '/Volumes/[^""]*' | head -1)
if [ -z ""$MOUNT_POINT"" ]; then
    MOUNT_POINT=""/Volumes/WisprClone""
fi

# Wait for mount
sleep 1

# Copy app to Applications (overwrite existing)
if [ -d ""$MOUNT_POINT/WisprClone.app"" ]; then
    rm -rf ""{appPath}""
    cp -R ""$MOUNT_POINT/WisprClone.app"" ""/Applications/""
fi

# Unmount DMG
hdiutil detach ""$MOUNT_POINT"" -quiet 2>/dev/null

# Clean up DMG
rm -f ""{dmgPath}""

# Relaunch app
open ""{appPath}""
";

            File.WriteAllText(scriptPath, script);

            // Make executable
            Process.Start("chmod", $"+x \"{scriptPath}\"")?.WaitForExit();

            // Launch script in background
            Process.Start(new ProcessStartInfo
            {
                FileName = "/bin/bash",
                Arguments = $"\"{scriptPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            });

            Log($"macOS update script launched: {scriptPath}");

            // Notify caller to quit
            onBeforeQuit?.Invoke();
        }
        catch (Exception ex)
        {
            Log($"LaunchMacOSUpdate error: {ex.Message}");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        StopPeriodicChecks();
        _httpClient.Dispose();
        _checkLock.Dispose();

        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
