using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using SharpCompress.Archives;
using SharpCompress.Archives.SevenZip;
using SharpCompress.Common;
using SharpCompress.Readers;
using WisprClone.Models;
using WisprClone.Services.Interfaces;

namespace WisprClone.Services;

/// <summary>
/// Helper for downloading and extracting external tools like faster-whisper and piper.
/// </summary>
public class DownloadHelper
{
    private readonly HttpClient _httpClient;
    private readonly ILoggingService? _loggingService;

    // Download URLs - verified working releases
    public const string FasterWhisperUrl = "https://github.com/Purfview/whisper-standalone-win/releases/download/Faster-Whisper-XXL/Faster-Whisper-XXL_r245.4_windows.7z";
    public const string PiperUrl = "https://github.com/rhasspy/piper/releases/download/2023.11.14-2/piper_windows_amd64.zip";

    // whisper.cpp server - CUDA 12.4 build for Windows (from ggml-org repo)
    public const string WhisperServerUrl = "https://github.com/ggml-org/whisper.cpp/releases/download/v1.8.3/whisper-cublas-12.4.0-bin-x64.zip";
    // Default ggml model (base.en - small and fast for English)
    public const string WhisperServerModelUrl = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.en.bin";

    // 7-Zip standalone for extracting .7z files (SharpCompress has limited support)
    public const string SevenZipUrl = "https://www.7-zip.org/a/7zr.exe";

    public DownloadHelper(ILoggingService? loggingService = null)
    {
        _loggingService = loggingService;
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("WisprClone/1.0");
        _httpClient.Timeout = TimeSpan.FromMinutes(30);
    }

    /// <summary>
    /// Downloads faster-whisper-xxl to the app directory.
    /// </summary>
    public async Task DownloadFasterWhisperAsync(IProgress<(double progress, string status)>? progress = null, CancellationToken ct = default)
    {
        var appDir = AppDomain.CurrentDomain.BaseDirectory;
        var targetDir = Path.Combine(appDir, "faster-whisper-xxl");
        var tempFile = Path.Combine(Path.GetTempPath(), $"faster-whisper-xxl-{Guid.NewGuid():N}.7z");
        var sevenZipExe = Path.Combine(appDir, "7zr.exe");

        try
        {
            // First, ensure we have 7zr.exe for extraction
            if (!File.Exists(sevenZipExe))
            {
                progress?.Report((0, "Downloading 7-Zip extractor..."));
                Log("Downloading 7zr.exe for .7z extraction");
                await DownloadFileAsync(SevenZipUrl, sevenZipExe, null, ct);
            }

            progress?.Report((2, "Downloading Faster-Whisper-XXL (~1.4 GB)..."));
            Log("Starting Faster-Whisper-XXL download from: " + FasterWhisperUrl);

            await DownloadFileAsync(FasterWhisperUrl, tempFile, new Progress<(double, string)>(p =>
            {
                progress?.Report((2 + p.Item1 * 0.83, p.Item2)); // 2-85% for download
            }), ct);

            progress?.Report((85, "Extracting (this may take a while)..."));
            Log("Extracting Faster-Whisper-XXL to: " + targetDir);

            // Create target directory
            if (Directory.Exists(targetDir))
            {
                Directory.Delete(targetDir, true);
            }
            Directory.CreateDirectory(targetDir);

            // Extract using 7zr.exe (more reliable for .7z files)
            await Extract7zWithExeAsync(sevenZipExe, tempFile, targetDir, progress, ct);

            // Flatten nested directories if needed
            FlattenDirectoryIfNeeded(targetDir);

            // Verify extraction
            var exePath = Path.Combine(targetDir, "faster-whisper-xxl.exe");
            if (!File.Exists(exePath))
            {
                var foundExe = Directory.GetFiles(targetDir, "faster-whisper-xxl.exe", SearchOption.AllDirectories).FirstOrDefault();
                if (foundExe != null)
                {
                    Log($"Found exe in subdirectory: {foundExe}");
                    var subDir = Path.GetDirectoryName(foundExe)!;
                    MoveContentsUp(subDir, targetDir);
                }
                else
                {
                    // List what was extracted for debugging
                    var files = Directory.GetFiles(targetDir, "*", SearchOption.AllDirectories).Take(20);
                    Log($"Extracted files (first 20): {string.Join(", ", files.Select(Path.GetFileName))}");
                    throw new FileNotFoundException("faster-whisper-xxl.exe not found after extraction. Check if the download completed successfully.");
                }
            }

            progress?.Report((100, "Done!"));
            Log("Faster-Whisper-XXL download complete");
        }
        finally
        {
            try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch { }
        }
    }

    private async Task Extract7zWithExeAsync(string sevenZipExe, string archivePath, string destinationDir,
        IProgress<(double progress, string status)>? progress, CancellationToken ct)
    {
        Log($"Extracting with 7zr.exe: {archivePath} -> {destinationDir}");

        var psi = new ProcessStartInfo
        {
            FileName = sevenZipExe,
            Arguments = $"x \"{archivePath}\" -o\"{destinationDir}\" -y",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = psi };
        process.Start();

        // Read output asynchronously
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();

        // Wait for process with cancellation support
        while (!process.HasExited)
        {
            if (ct.IsCancellationRequested)
            {
                process.Kill();
                ct.ThrowIfCancellationRequested();
            }
            await Task.Delay(100, ct);
            progress?.Report((90, "Extracting..."));
        }

        var output = await outputTask;
        var error = await errorTask;

        if (process.ExitCode != 0)
        {
            Log($"7zr.exe output: {output}");
            Log($"7zr.exe error: {error}");
            throw new Exception($"7-Zip extraction failed (exit code {process.ExitCode}): {error}");
        }

        Log("7zr.exe extraction complete");
    }

    /// <summary>
    /// Downloads piper to the app directory (without voice files).
    /// </summary>
    public async Task DownloadPiperAsync(IProgress<(double progress, string status)>? progress = null, CancellationToken ct = default)
    {
        var appDir = AppDomain.CurrentDomain.BaseDirectory;
        var targetDir = Path.Combine(appDir, "piper");
        var voicesDir = Path.Combine(targetDir, "voices");
        var tempFile = Path.Combine(Path.GetTempPath(), $"piper-{Guid.NewGuid():N}.zip");

        try
        {
            progress?.Report((0, "Downloading Piper TTS (~22 MB)..."));
            Log("Starting Piper download from: " + PiperUrl);

            await DownloadFileAsync(PiperUrl, tempFile, new Progress<(double, string)>(p =>
            {
                progress?.Report((p.Item1 * 0.8, p.Item2)); // 0-80% for download
            }), ct);

            progress?.Report((80, "Extracting..."));
            Log("Extracting Piper to: " + targetDir);

            // Create target directory
            if (Directory.Exists(targetDir))
            {
                // Preserve existing voices
                var existingVoices = new List<string>();
                if (Directory.Exists(voicesDir))
                {
                    existingVoices.AddRange(Directory.GetFiles(voicesDir, "*.onnx"));
                }

                Directory.Delete(targetDir, true);
                Directory.CreateDirectory(targetDir);
                Directory.CreateDirectory(voicesDir);

                // Note: voices will need to be re-downloaded
            }
            else
            {
                Directory.CreateDirectory(targetDir);
                Directory.CreateDirectory(voicesDir);
            }

            // Extract using ZipFile (standard .zip)
            ZipFile.ExtractToDirectory(tempFile, targetDir);

            // Flatten if needed
            FlattenDirectoryIfNeeded(targetDir, excludeDir: "voices");

            // Verify piper.exe exists
            var exePath = Path.Combine(targetDir, "piper.exe");
            if (!File.Exists(exePath))
            {
                var foundExe = Directory.GetFiles(targetDir, "piper.exe", SearchOption.AllDirectories).FirstOrDefault();
                if (foundExe != null)
                {
                    Log($"Found piper.exe in subdirectory: {foundExe}");
                    var subDir = Path.GetDirectoryName(foundExe)!;
                    MoveContentsUp(subDir, targetDir);
                }
                else
                {
                    throw new FileNotFoundException("piper.exe not found after extraction");
                }
            }

            // Ensure voices directory exists
            if (!Directory.Exists(voicesDir))
            {
                Directory.CreateDirectory(voicesDir);
            }

            progress?.Report((100, "Done! Now download a voice."));
            Log("Piper download complete");
        }
        finally
        {
            try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch { }
        }
    }

    /// <summary>
    /// Downloads a Piper voice from the catalog.
    /// </summary>
    public async Task DownloadPiperVoiceAsync(PiperVoiceEntry voice, IProgress<(double progress, string status)>? progress = null, CancellationToken ct = default)
    {
        var appDir = AppDomain.CurrentDomain.BaseDirectory;
        var voicesDir = Path.Combine(appDir, "piper", "voices");

        if (!Directory.Exists(voicesDir))
        {
            Directory.CreateDirectory(voicesDir);
        }

        var onnxUrl = voice.GetOnnxDownloadUrl();
        var jsonUrl = voice.GetOnnxJsonDownloadUrl();

        if (onnxUrl == null || jsonUrl == null)
        {
            throw new InvalidOperationException("Voice does not have valid download URLs");
        }

        var onnxFileName = $"{voice.Key}.onnx";
        var jsonFileName = $"{voice.Key}.onnx.json";
        var onnxPath = Path.Combine(voicesDir, onnxFileName);
        var jsonPath = Path.Combine(voicesDir, jsonFileName);

        var sizeMb = voice.GetTotalSizeBytes() / (1024.0 * 1024.0);
        progress?.Report((0, $"Downloading {voice.DisplayName} (~{sizeMb:F0} MB)..."));

        await DownloadFileAsync(onnxUrl, onnxPath, new Progress<(double, string)>(p =>
        {
            progress?.Report((p.Item1 * 0.95, p.Item2));
        }), ct);

        progress?.Report((96, "Downloading config..."));
        await DownloadFileAsync(jsonUrl, jsonPath, null, ct);

        progress?.Report((100, "Done!"));
        Log($"Downloaded voice: {voice.Key}");
    }

    /// <summary>
    /// Downloads whisper.cpp server (with CUDA support) and a default model.
    /// Skips downloading components that already exist.
    /// </summary>
    public async Task DownloadWhisperServerAsync(IProgress<(double progress, string status)>? progress = null, CancellationToken ct = default)
    {
        var appDir = AppDomain.CurrentDomain.BaseDirectory;
        var targetDir = Path.Combine(appDir, "whisper-server");
        var modelsDir = Path.Combine(targetDir, "models");
        var serverExePath = Path.Combine(targetDir, "whisper-server.exe");
        var modelPath = Path.Combine(modelsDir, "ggml-base.en.bin");

        var needsServer = !File.Exists(serverExePath);
        var needsModel = !File.Exists(modelPath);

        if (!needsServer && !needsModel)
        {
            progress?.Report((100, "Already installed!"));
            return;
        }

        Directory.CreateDirectory(targetDir);
        Directory.CreateDirectory(modelsDir);

        var tempFile = Path.Combine(Path.GetTempPath(), $"whisper-server-{Guid.NewGuid():N}.zip");

        try
        {
            double progressOffset = 0;

            // Download server if needed
            if (needsServer)
            {
                progress?.Report((0, "Downloading whisper.cpp server (~460 MB)..."));
                Log("Starting whisper.cpp server download from: " + WhisperServerUrl);

                await DownloadFileAsync(WhisperServerUrl, tempFile, new Progress<(double, string)>(p =>
                {
                    progress?.Report((p.Item1 * 0.5, p.Item2)); // 0-50% for server download
                }), ct);

                progress?.Report((50, "Extracting server..."));
                Log("Extracting whisper.cpp server to: " + targetDir);

                // Extract using ZipFile (standard .zip)
                ZipFile.ExtractToDirectory(tempFile, targetDir, overwriteFiles: true);

                // Flatten if needed
                FlattenDirectoryIfNeeded(targetDir, excludeDir: "models");

                // Find the whisper-server executable
                var serverExe = Directory.GetFiles(targetDir, "whisper-server.exe", SearchOption.AllDirectories).FirstOrDefault();

                if (serverExe != null && Path.GetDirectoryName(serverExe) != targetDir)
                {
                    var sourceDir = Path.GetDirectoryName(serverExe)!;
                    MoveContentsUp(sourceDir, targetDir);
                }

                // Verify server executable exists
                if (!File.Exists(serverExePath))
                {
                    var files = Directory.GetFiles(targetDir, "*", SearchOption.AllDirectories).Take(20);
                    Log($"Extracted files (first 20): {string.Join(", ", files.Select(Path.GetFileName))}");
                    throw new FileNotFoundException("whisper-server.exe not found after extraction");
                }

                progressOffset = 55;
            }

            // Download default model if needed
            if (needsModel)
            {
                var modelProgressStart = needsServer ? 55 : 0;
                var modelProgressRange = needsServer ? 0.4 : 0.95;

                progress?.Report((modelProgressStart, "Downloading default model (~150 MB)..."));
                Log("Downloading default model: ggml-base.en.bin");

                await DownloadFileAsync(WhisperServerModelUrl, modelPath, new Progress<(double, string)>(p =>
                {
                    progress?.Report((modelProgressStart + p.Item1 * modelProgressRange, p.Item2));
                }), ct);
            }

            progress?.Report((100, "Done!"));
            Log("whisper.cpp server download complete");
        }
        finally
        {
            try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch { }
        }
    }

    /// <summary>
    /// Gets the Piper voice catalog from HuggingFace.
    /// </summary>
    public async Task<PiperVoiceCatalog> GetPiperVoiceCatalogAsync(CancellationToken ct = default)
    {
        return await PiperVoiceCatalog.LoadFromUrlAsync(_httpClient, ct);
    }

    /// <summary>
    /// Gets installed Piper voices.
    /// </summary>
    public List<(string key, string path)> GetInstalledPiperVoices()
    {
        var voicesDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "piper", "voices");
        var voices = new List<(string key, string path)>();

        if (Directory.Exists(voicesDir))
        {
            foreach (var onnxFile in Directory.GetFiles(voicesDir, "*.onnx"))
            {
                if (!onnxFile.EndsWith(".onnx.json"))
                {
                    var key = Path.GetFileNameWithoutExtension(onnxFile);
                    voices.Add((key, onnxFile));
                }
            }
        }

        return voices;
    }

    private void FlattenDirectoryIfNeeded(string targetDir, string? excludeDir = null)
    {
        var subdirs = Directory.GetDirectories(targetDir);
        var files = Directory.GetFiles(targetDir);

        if (subdirs.Length == 1 && files.Length == 0)
        {
            var subDir = subdirs[0];
            if (excludeDir == null || !Path.GetFileName(subDir).Equals(excludeDir, StringComparison.OrdinalIgnoreCase))
            {
                Log($"Flattening directory: {subDir}");
                MoveContentsUp(subDir, targetDir);
            }
        }
    }

    private void MoveContentsUp(string sourceDir, string targetDir)
    {
        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var destFile = Path.Combine(targetDir, Path.GetFileName(file));
            if (File.Exists(destFile)) File.Delete(destFile);
            File.Move(file, destFile);
        }

        foreach (var dir in Directory.GetDirectories(sourceDir))
        {
            var destDir = Path.Combine(targetDir, Path.GetFileName(dir));
            if (Directory.Exists(destDir)) Directory.Delete(destDir, true);
            Directory.Move(dir, destDir);
        }

        try { Directory.Delete(sourceDir, true); } catch { }
    }

    private async Task DownloadFileAsync(string url, string destinationPath, IProgress<(double progress, string status)>? progress, CancellationToken ct)
    {
        Log($"Downloading: {url}");

        using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? -1;
        var downloadedBytes = 0L;

        await using var contentStream = await response.Content.ReadAsStreamAsync(ct);
        await using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);

        var buffer = new byte[81920];
        int bytesRead;
        var lastProgressReport = DateTime.UtcNow;

        while ((bytesRead = await contentStream.ReadAsync(buffer, ct)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
            downloadedBytes += bytesRead;

            if (totalBytes > 0 && (DateTime.UtcNow - lastProgressReport).TotalMilliseconds > 100)
            {
                var percentage = (double)downloadedBytes / totalBytes * 100;
                var mbDownloaded = downloadedBytes / (1024.0 * 1024.0);
                var mbTotal = totalBytes / (1024.0 * 1024.0);
                progress?.Report((percentage, $"Downloading... {mbDownloaded:F1}/{mbTotal:F1} MB"));
                lastProgressReport = DateTime.UtcNow;
            }
        }

        Log($"Download complete: {destinationPath} ({downloadedBytes} bytes)");
    }

    private void Log(string message)
    {
        _loggingService?.Log("DownloadHelper", message);
        System.Diagnostics.Debug.WriteLine($"[DownloadHelper] {message}");
    }
}
