using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Runtime.InteropServices;
using SharpCompress.Archives;
using SharpCompress.Archives.SevenZip;
using SharpCompress.Common;
using SharpCompress.Readers;
using WisprClone.Models;
using WisprClone.Services.Interfaces;

namespace WisprClone.Services;

/// <summary>
/// Helper for downloading and extracting external tools like faster-whisper and piper.
/// Supports Windows, macOS, and Linux platforms.
/// </summary>
public class DownloadHelper
{
    private readonly HttpClient _httpClient;
    private readonly ILoggingService? _loggingService;

    #region Platform-specific Download URLs

    // whisper.cpp server URLs by platform (v1.8.3)
    // Windows: CUDA 12.4 build for GPU acceleration
    // macOS: Metal build for GPU acceleration
    // Linux: CPU build (CUDA builds require specific CUDA versions)
    private const string WhisperServerUrl_Windows = "https://github.com/ggml-org/whisper.cpp/releases/download/v1.8.3/whisper-cublas-12.4.0-bin-x64.zip";
    private const string WhisperServerUrl_MacOS_Arm64 = "https://github.com/ggml-org/whisper.cpp/releases/download/v1.8.3/whisper-bin-arm64.zip";
    private const string WhisperServerUrl_MacOS_x64 = "https://github.com/ggml-org/whisper.cpp/releases/download/v1.8.3/whisper-bin-x64.zip";
    private const string WhisperServerUrl_Linux = "https://github.com/ggml-org/whisper.cpp/releases/download/v1.8.3/whisper-bin-x64.zip";

    // Piper TTS URLs by platform (2023.11.14-2)
    private const string PiperUrl_Windows = "https://github.com/rhasspy/piper/releases/download/2023.11.14-2/piper_windows_amd64.zip";
    private const string PiperUrl_MacOS_Arm64 = "https://github.com/rhasspy/piper/releases/download/2023.11.14-2/piper_macos_aarch64.tar.gz";
    private const string PiperUrl_MacOS_x64 = "https://github.com/rhasspy/piper/releases/download/2023.11.14-2/piper_macos_x64.tar.gz";
    private const string PiperUrl_Linux = "https://github.com/rhasspy/piper/releases/download/2023.11.14-2/piper_linux_x86_64.tar.gz";

    // Faster-Whisper-XXL is Windows-only (uses Windows-specific dependencies)
    private const string FasterWhisperUrl_Windows = "https://github.com/Purfview/whisper-standalone-win/releases/download/Faster-Whisper-XXL/Faster-Whisper-XXL_r245.4_windows.7z";

    // Default ggml model (base.en - small and fast for English) - cross-platform
    public const string WhisperServerModelUrl = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.en.bin";

    // Default Piper voice (en_US-amy-medium) - cross-platform
    public const string DefaultPiperVoiceUrl = "https://huggingface.co/rhasspy/piper-voices/resolve/v1.0.0/en/en_US/amy/medium/en_US-amy-medium.onnx";
    public const string DefaultPiperVoiceConfigUrl = "https://huggingface.co/rhasspy/piper-voices/resolve/v1.0.0/en/en_US/amy/medium/en_US-amy-medium.onnx.json";

    // 7-Zip standalone for extracting .7z files (Windows only - macOS/Linux use tar)
    public const string SevenZipUrl = "https://www.7-zip.org/a/7zr.exe";

    #endregion

    #region Platform Detection Helpers

    /// <summary>
    /// Gets the current operating system platform.
    /// </summary>
    public static OSPlatform CurrentPlatform
    {
        get
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return OSPlatform.Windows;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return OSPlatform.OSX;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return OSPlatform.Linux;
            return OSPlatform.Windows; // Default fallback
        }
    }

    /// <summary>
    /// Checks if we're running on Apple Silicon (ARM64 Mac).
    /// </summary>
    public static bool IsAppleSilicon => RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
        && RuntimeInformation.ProcessArchitecture == Architecture.Arm64;

    /// <summary>
    /// Gets the whisper server download URL for the current platform.
    /// </summary>
    public static string GetWhisperServerUrl()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return WhisperServerUrl_Windows;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return IsAppleSilicon ? WhisperServerUrl_MacOS_Arm64 : WhisperServerUrl_MacOS_x64;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return WhisperServerUrl_Linux;
        return WhisperServerUrl_Windows;
    }

    /// <summary>
    /// Gets the Piper TTS download URL for the current platform.
    /// </summary>
    public static string GetPiperUrl()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return PiperUrl_Windows;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return IsAppleSilicon ? PiperUrl_MacOS_Arm64 : PiperUrl_MacOS_x64;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return PiperUrl_Linux;
        return PiperUrl_Windows;
    }

    /// <summary>
    /// Gets the Faster-Whisper-XXL download URL. Only available on Windows.
    /// </summary>
    public static string? GetFasterWhisperUrl()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return FasterWhisperUrl_Windows;
        return null; // Not available on macOS/Linux
    }

    /// <summary>
    /// Gets the whisper server executable name for the current platform.
    /// </summary>
    public static string GetWhisperServerExecutableName()
    {
        return RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "whisper-server.exe" : "whisper-server";
    }

    /// <summary>
    /// Gets the Piper executable name for the current platform.
    /// </summary>
    public static string GetPiperExecutableName()
    {
        return RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "piper.exe" : "piper";
    }

    /// <summary>
    /// Checks if Faster-Whisper-XXL is available on the current platform.
    /// </summary>
    public static bool IsFasterWhisperAvailable => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    #endregion

    // Legacy properties for backwards compatibility
    public static string FasterWhisperUrl => GetFasterWhisperUrl() ?? "";
    public static string PiperUrl => GetPiperUrl();
    public static string WhisperServerUrl => GetWhisperServerUrl();

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
    /// Downloads piper to the app directory with the default voice (en_US-amy-medium).
    /// Supports Windows, macOS, and Linux platforms.
    /// </summary>
    public async Task DownloadPiperAsync(IProgress<(double progress, string status)>? progress = null, CancellationToken ct = default)
    {
        var appDir = AppDomain.CurrentDomain.BaseDirectory;
        var targetDir = Path.Combine(appDir, "piper");
        var voicesDir = Path.Combine(targetDir, "voices");
        var piperExeName = GetPiperExecutableName();
        var exePath = Path.Combine(targetDir, piperExeName);

        var downloadUrl = GetPiperUrl();
        var isTarGz = downloadUrl.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase);
        var extension = isTarGz ? ".tar.gz" : ".zip";
        var tempFile = Path.Combine(Path.GetTempPath(), $"piper-{Guid.NewGuid():N}{extension}");

        try
        {
            progress?.Report((0, "Downloading Piper TTS (~22 MB)..."));
            Log($"Starting Piper download from: {downloadUrl}");

            await DownloadFileAsync(downloadUrl, tempFile, new Progress<(double, string)>(p =>
            {
                progress?.Report((p.Item1 * 0.25, p.Item2)); // 0-25% for exe download
            }), ct);

            progress?.Report((25, "Extracting..."));
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

            // Extract based on archive type
            if (isTarGz)
            {
                await ExtractTarGzAsync(tempFile, targetDir, ct);
            }
            else
            {
                ZipFile.ExtractToDirectory(tempFile, targetDir);
            }

            // Flatten if needed
            FlattenDirectoryIfNeeded(targetDir, excludeDir: "voices");

            // Verify piper executable exists (platform-specific name)
            if (!File.Exists(exePath))
            {
                var foundExe = Directory.GetFiles(targetDir, piperExeName, SearchOption.AllDirectories).FirstOrDefault();

                // Also try without extension on Unix platforms
                if (foundExe == null && !RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    foundExe = Directory.GetFiles(targetDir, "piper", SearchOption.AllDirectories).FirstOrDefault();
                }

                if (foundExe != null)
                {
                    Log($"Found {piperExeName} in subdirectory: {foundExe}");
                    var subDir = Path.GetDirectoryName(foundExe)!;
                    MoveContentsUp(subDir, targetDir);
                }
                else
                {
                    throw new FileNotFoundException($"{piperExeName} not found after extraction");
                }
            }

            // On macOS/Linux, make the binary executable
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && File.Exists(exePath))
            {
                await MakeExecutableAsync(exePath);
            }

            // Ensure voices directory exists
            if (!Directory.Exists(voicesDir))
            {
                Directory.CreateDirectory(voicesDir);
            }

            // Download default voice (en_US-amy-medium)
            var defaultVoicePath = Path.Combine(voicesDir, "en_US-amy-medium.onnx");
            var defaultVoiceConfigPath = Path.Combine(voicesDir, "en_US-amy-medium.onnx.json");

            if (!File.Exists(defaultVoicePath))
            {
                progress?.Report((30, "Downloading default voice (~65 MB)..."));
                Log("Downloading default Piper voice: en_US-amy-medium");

                await DownloadFileAsync(DefaultPiperVoiceUrl, defaultVoicePath, new Progress<(double, string)>(p =>
                {
                    progress?.Report((30 + p.Item1 * 0.68, p.Item2)); // 30-98% for voice download
                }), ct);

                progress?.Report((98, "Downloading voice config..."));
                await DownloadFileAsync(DefaultPiperVoiceConfigUrl, defaultVoiceConfigPath, null, ct);
            }

            progress?.Report((100, "Done!"));
            Log("Piper download complete (with default voice)");
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
    /// Downloads whisper.cpp server and a default model.
    /// Uses platform-specific binaries (CUDA on Windows, Metal on macOS, CPU on Linux).
    /// Skips downloading components that already exist.
    /// </summary>
    public async Task DownloadWhisperServerAsync(IProgress<(double progress, string status)>? progress = null, CancellationToken ct = default)
    {
        var appDir = AppDomain.CurrentDomain.BaseDirectory;
        var targetDir = Path.Combine(appDir, "whisper-server");
        var modelsDir = Path.Combine(targetDir, "models");
        var serverExeName = GetWhisperServerExecutableName();
        var serverExePath = Path.Combine(targetDir, serverExeName);
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

        var downloadUrl = GetWhisperServerUrl();
        var isTarGz = downloadUrl.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase);
        var extension = isTarGz ? ".tar.gz" : ".zip";
        var tempFile = Path.Combine(Path.GetTempPath(), $"whisper-server-{Guid.NewGuid():N}{extension}");

        // Determine download size based on platform
        var downloadSize = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "~460 MB" : "~15 MB";

        try
        {
            double progressOffset = 0;

            // Download server if needed
            if (needsServer)
            {
                progress?.Report((0, $"Downloading whisper.cpp server ({downloadSize})..."));
                Log($"Starting whisper.cpp server download from: {downloadUrl}");

                await DownloadFileAsync(downloadUrl, tempFile, new Progress<(double, string)>(p =>
                {
                    progress?.Report((p.Item1 * 0.5, p.Item2)); // 0-50% for server download
                }), ct);

                progress?.Report((50, "Extracting server..."));
                Log("Extracting whisper.cpp server to: " + targetDir);

                // Extract based on archive type
                if (isTarGz)
                {
                    await ExtractTarGzAsync(tempFile, targetDir, ct);
                }
                else
                {
                    ZipFile.ExtractToDirectory(tempFile, targetDir, overwriteFiles: true);
                }

                // Flatten if needed
                FlattenDirectoryIfNeeded(targetDir, excludeDir: "models");

                // Find the whisper-server executable (platform-specific name)
                var serverExe = Directory.GetFiles(targetDir, serverExeName, SearchOption.AllDirectories).FirstOrDefault();

                // Also try without extension on Unix platforms
                if (serverExe == null && !RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    serverExe = Directory.GetFiles(targetDir, "whisper-server", SearchOption.AllDirectories).FirstOrDefault();
                }

                if (serverExe != null && Path.GetDirectoryName(serverExe) != targetDir)
                {
                    var sourceDir = Path.GetDirectoryName(serverExe)!;
                    MoveContentsUp(sourceDir, targetDir);
                }

                // On macOS/Linux, make the binary executable
                if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && File.Exists(serverExePath))
                {
                    await MakeExecutableAsync(serverExePath);
                }

                // Verify server executable exists
                if (!File.Exists(serverExePath))
                {
                    var files = Directory.GetFiles(targetDir, "*", SearchOption.AllDirectories).Take(20);
                    Log($"Extracted files (first 20): {string.Join(", ", files.Select(Path.GetFileName))}");
                    throw new FileNotFoundException($"{serverExeName} not found after extraction");
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
    /// Downloads a specific whisper model for whisper.cpp server.
    /// </summary>
    public async Task DownloadWhisperModelAsync(string modelName, IProgress<(double progress, string status)>? progress = null, CancellationToken ct = default)
    {
        var appDir = AppDomain.CurrentDomain.BaseDirectory;
        var modelsDir = Path.Combine(appDir, "whisper-server", "models");

        Directory.CreateDirectory(modelsDir);

        var modelFileName = $"ggml-{modelName}.bin";
        var modelPath = Path.Combine(modelsDir, modelFileName);

        // Get model size for display
        var modelSizes = new Dictionary<string, string>
        {
            { "tiny.en", "~75 MB" },
            { "tiny", "~75 MB" },
            { "base.en", "~142 MB" },
            { "base", "~142 MB" },
            { "small.en", "~466 MB" },
            { "small", "~466 MB" },
            { "medium.en", "~1.5 GB" },
            { "medium", "~1.5 GB" },
            { "large-v3", "~3 GB" },
            { "large-v3-turbo", "~1.6 GB" }
        };

        var sizeDisplay = modelSizes.TryGetValue(modelName, out var size) ? size : "unknown size";

        // Model URL from HuggingFace ggerganov/whisper.cpp repo
        var modelUrl = $"https://huggingface.co/ggerganov/whisper.cpp/resolve/main/{modelFileName}";

        progress?.Report((0, $"Downloading {modelName} model ({sizeDisplay})..."));
        Log($"Downloading whisper model: {modelUrl}");

        try
        {
            await DownloadFileAsync(modelUrl, modelPath, new Progress<(double, string)>(p =>
            {
                progress?.Report((p.Item1 * 0.95, p.Item2));
            }), ct);

            progress?.Report((100, "Done!"));
            Log($"Whisper model download complete: {modelFileName}");
        }
        catch (Exception ex)
        {
            Log($"Failed to download model {modelName}: {ex.Message}");
            // Clean up partial download
            try { if (File.Exists(modelPath)) File.Delete(modelPath); } catch { }
            throw;
        }
    }

    /// <summary>
    /// Checks if a specific whisper model is installed.
    /// </summary>
    public bool IsWhisperModelInstalled(string modelName)
    {
        var modelFileName = $"ggml-{modelName}.bin";
        var modelPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "whisper-server", "models", modelFileName);
        return File.Exists(modelPath);
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

    #region Cross-Platform Archive Extraction

    /// <summary>
    /// Extracts a .tar.gz archive using SharpCompress (cross-platform).
    /// </summary>
    private async Task ExtractTarGzAsync(string archivePath, string destinationDir, CancellationToken ct)
    {
        Log($"Extracting tar.gz: {archivePath} -> {destinationDir}");

        await Task.Run(() =>
        {
            using var stream = File.OpenRead(archivePath);
            using var reader = ReaderFactory.Open(stream);

            while (reader.MoveToNextEntry())
            {
                ct.ThrowIfCancellationRequested();

                if (!reader.Entry.IsDirectory)
                {
                    var entryPath = Path.Combine(destinationDir, reader.Entry.Key);
                    var entryDir = Path.GetDirectoryName(entryPath);

                    if (!string.IsNullOrEmpty(entryDir) && !Directory.Exists(entryDir))
                        Directory.CreateDirectory(entryDir);

                    reader.WriteEntryToDirectory(destinationDir, new ExtractionOptions
                    {
                        ExtractFullPath = true,
                        Overwrite = true
                    });
                }
            }
        }, ct);

        Log("tar.gz extraction complete");
    }

    /// <summary>
    /// Makes a file executable on Unix-like systems (macOS/Linux).
    /// </summary>
    private async Task MakeExecutableAsync(string filePath)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return;

        Log($"Making executable: {filePath}");

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "chmod",
                Arguments = $"+x \"{filePath}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process != null)
            {
                await process.WaitForExitAsync();
                if (process.ExitCode != 0)
                {
                    var error = await process.StandardError.ReadToEndAsync();
                    Log($"chmod failed: {error}");
                }
            }
        }
        catch (Exception ex)
        {
            Log($"Failed to make file executable: {ex.Message}");
            // Not fatal - the file might still work
        }
    }

    #endregion
}
