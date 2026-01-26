#if WINDOWS
using NAudio.Wave;
#endif
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Timers;
using WisprClone.Core;
using WisprClone.Models;
using WisprClone.Services.Interfaces;

namespace WisprClone.Services.Speech;

/// <summary>
/// Whisper speech recognition using a persistent HTTP server.
/// Keeps the model loaded in memory for instant transcription (~1 second instead of ~7 seconds).
/// Uses whisper.cpp server for efficient GPU-accelerated inference.
/// </summary>
public class WhisperServerSpeechRecognitionService : ISpeechRecognitionService
{
    private readonly ILoggingService _loggingService;
    private readonly ISettingsService _settingsService;
    private readonly HttpClient _httpClient;

#if WINDOWS
    private WaveInEvent? _waveIn;
    private WaveFileWriter? _fullAudioWriter;
#endif
    private MemoryStream? _fullAudioStream;
    private readonly object _audioLock = new();
    private bool _disposed;
    private bool _isRecording;

    // Sliding window streaming
    private System.Timers.Timer? _streamingTimer;
    private readonly List<TranscribedWord> _confirmedWords = new();
    private double _lastConfirmedTimestamp = 0;
    private long _recordingStartTime;  // When recording started (milliseconds since epoch)

    private static Process? _serverProcess;
    private static readonly object _serverLock = new();
    private static bool _serverStarted;
    private static int _serverPort = 8178;
    private static string? _currentLoadedModel;

    // PID file to track our whisper-server instance
    private static readonly string PidFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "WisprClone", "whisper-server.pid");

    public event EventHandler<TranscriptionEventArgs>? RecognitionPartial;
    public event EventHandler<TranscriptionEventArgs>? RecognitionCompleted;
    public event EventHandler<RecognitionErrorEventArgs>? RecognitionError;
    public event EventHandler<RecognitionStateChangedEventArgs>? StateChanged;
    public event EventHandler<string>? LanguageChanged;

    public RecognitionState CurrentState { get; private set; } = RecognitionState.Idle;
    public string ProviderName => "Whisper Server";
    public string CurrentLanguage { get; private set; } = "en-US";

#if WINDOWS
    public bool IsAvailable => File.Exists(GetServerExePath());
#else
    public bool IsAvailable => false;
#endif

    public WhisperServerSpeechRecognitionService(ILoggingService loggingService, ISettingsService settingsService)
    {
        _loggingService = loggingService;
        _settingsService = settingsService;
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(2)
        };
    }

    private string GetServerExePath()
    {
        return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "whisper-server", "whisper-server.exe");
    }

    private string GetModelsDirectory()
    {
        return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "whisper-server", "models");
    }

    private string GetModelPath()
    {
        var settings = _settingsService.Current;
        var modelName = settings.WhisperCppModel;

        // Add ggml- prefix and .bin suffix if needed
        if (!modelName.StartsWith("ggml-"))
            modelName = $"ggml-{modelName}";
        if (!modelName.EndsWith(".bin"))
            modelName = $"{modelName}.bin";

        return Path.Combine(GetModelsDirectory(), modelName);
    }

    public async Task InitializeAsync(string language = "en-US")
    {
        CurrentLanguage = language;

        // Ensure server is running
        await EnsureServerRunningAsync();
    }

    /// <summary>
    /// Starts the whisper.cpp server if not already running.
    /// The server keeps the model loaded in memory for fast inference.
    /// Will restart the server if the model has changed.
    /// </summary>
    public async Task EnsureServerRunningAsync()
    {
#if WINDOWS
        var settings = _settingsService.Current;
        var requestedModel = settings.WhisperCppModel;
        _serverPort = settings.WhisperCppServerPort;

        lock (_serverLock)
        {
            if (_serverStarted && _serverProcess != null && !_serverProcess.HasExited)
            {
                // Check if model has changed - if so, need to restart
                if (_currentLoadedModel == requestedModel)
                {
                    Log("Server already running with correct model");
                    return;
                }
                else
                {
                    Log($"Model changed from '{_currentLoadedModel}' to '{requestedModel}', restarting server...");
                    StopServerInternal();
                }
            }
        }

        // Check if our previously started server is still running
        var savedPid = ReadSavedPid();
        if (savedPid != null && await IsServerRespondingAsync())
        {
            // Verify it's actually our server process
            try
            {
                var process = Process.GetProcessById(savedPid.Value);
                if (process.ProcessName.Equals("whisper-server", StringComparison.OrdinalIgnoreCase))
                {
                    Log($"Found our existing server (PID {savedPid}) on port {_serverPort}, reusing it");
                    lock (_serverLock)
                    {
                        _serverStarted = true;
                        _currentLoadedModel = requestedModel; // Assume it has the right model
                    }
                    return;
                }
            }
            catch (ArgumentException)
            {
                // Process doesn't exist, will start a new one
                Log($"Saved PID {savedPid} no longer exists");
            }
        }

        // Kill our orphaned whisper-server process (if any) before starting a new one
        KillOrphanedServerProcesses();

        var serverExe = GetServerExePath();
        var modelPath = GetModelPath();

        if (!File.Exists(serverExe))
        {
            Log($"Server not found at: {serverExe}");
            throw new FileNotFoundException($"whisper server not found. Download it to: app/whisper-server/");
        }

        if (!File.Exists(modelPath))
        {
            Log($"Model not found at: {modelPath}");
            throw new FileNotFoundException($"Whisper model not found: {modelPath}");
        }

        lock (_serverLock)
        {
            try
            {
                Log($"Starting whisper server on port {_serverPort}...");
                Log($"Model: {modelPath}");

                var args = new List<string>
                {
                    $"-m \"{modelPath}\"",
                    $"--port {_serverPort}",
                    "--host 127.0.0.1"
                };

                // Disable GPU if not wanted (GPU is enabled by default)
                if (!settings.FasterWhisperUseGpu)
                {
                    args.Add("--no-gpu");
                }

                _serverProcess = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = serverExe,
                        Arguments = string.Join(" ", args),
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        WorkingDirectory = Path.GetDirectoryName(serverExe)
                    }
                };

                _serverProcess.OutputDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                        Log($"[Server] {e.Data}");
                };
                _serverProcess.ErrorDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                        Log($"[Server Error] {e.Data}");
                };

                _serverProcess.Start();
                _serverProcess.BeginOutputReadLine();
                _serverProcess.BeginErrorReadLine();

                _serverStarted = true;
                _currentLoadedModel = requestedModel;
                Log($"Server started with PID: {_serverProcess.Id}, model: {requestedModel}");

                // Save the PID so we can track this instance across app restarts
                SavePidFile(_serverProcess.Id);
            }
            catch (Exception ex)
            {
                Log($"Failed to start server: {ex.Message}");
                throw;
            }
        }

        // Wait for server to be ready
        await WaitForServerReadyAsync();
#endif
    }

    private async Task WaitForServerReadyAsync()
    {
        var maxAttempts = 30; // 30 seconds max wait
        for (int i = 0; i < maxAttempts; i++)
        {
            try
            {
                var response = await _httpClient.GetAsync($"http://127.0.0.1:{_serverPort}/");
                if (response.IsSuccessStatusCode)
                {
                    Log("Server is ready");
                    return;
                }
            }
            catch
            {
                // Server not ready yet
            }

            await Task.Delay(1000);
            Log($"Waiting for server... ({i + 1}/{maxAttempts})");
        }

        throw new TimeoutException("Whisper server failed to start within 30 seconds");
    }

    /// <summary>
    /// Checks if a whisper server is already responding on the configured port.
    /// </summary>
    private async Task<bool> IsServerRespondingAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            var response = await _httpClient.GetAsync($"http://127.0.0.1:{_serverPort}/", cts.Token);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Kills only the whisper-server process that WisprClone previously started (tracked via PID file).
    /// Does not kill other whisper-server instances that may be running independently.
    /// </summary>
    private void KillOrphanedServerProcesses()
    {
        try
        {
            var savedPid = ReadSavedPid();
            if (savedPid == null)
            {
                Log("No saved PID file found, no orphaned process to kill");
                return;
            }

            Log($"Found saved PID {savedPid}, checking if process is still running...");

            try
            {
                var process = Process.GetProcessById(savedPid.Value);

                // Verify it's actually a whisper-server process (not a reused PID)
                if (process.ProcessName.Equals("whisper-server", StringComparison.OrdinalIgnoreCase))
                {
                    Log($"Killing our orphaned whisper-server PID {savedPid}");
                    process.Kill();
                    process.WaitForExit(3000);
                    process.Dispose();

                    // Give the system a moment to release resources
                    Thread.Sleep(500);
                }
                else
                {
                    Log($"PID {savedPid} is now a different process ({process.ProcessName}), not killing");
                }
            }
            catch (ArgumentException)
            {
                // Process with this PID doesn't exist anymore
                Log($"Process with PID {savedPid} no longer exists");
            }
            catch (Exception ex)
            {
                Log($"Failed to kill process {savedPid}: {ex.Message}");
            }

            // Clean up the PID file
            DeletePidFile();
        }
        catch (Exception ex)
        {
            Log($"Error while killing orphaned process: {ex.Message}");
        }
    }

    /// <summary>
    /// Saves the server process ID to a file for tracking across app restarts.
    /// </summary>
    private void SavePidFile(int pid)
    {
        try
        {
            var dir = Path.GetDirectoryName(PidFilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(PidFilePath, pid.ToString());
            Log($"Saved server PID {pid} to {PidFilePath}");
        }
        catch (Exception ex)
        {
            Log($"Failed to save PID file: {ex.Message}");
        }
    }

    /// <summary>
    /// Reads the saved server process ID from the PID file.
    /// </summary>
    private int? ReadSavedPid()
    {
        try
        {
            if (!File.Exists(PidFilePath))
                return null;

            var content = File.ReadAllText(PidFilePath).Trim();
            if (int.TryParse(content, out var pid))
                return pid;
        }
        catch (Exception ex)
        {
            Log($"Failed to read PID file: {ex.Message}");
        }
        return null;
    }

    /// <summary>
    /// Deletes the PID file.
    /// </summary>
    private void DeletePidFile()
    {
        try
        {
            if (File.Exists(PidFilePath))
            {
                File.Delete(PidFilePath);
                Log("Deleted PID file");
            }
        }
        catch (Exception ex)
        {
            Log($"Failed to delete PID file: {ex.Message}");
        }
    }

    public Task StartRecognitionAsync(CancellationToken cancellationToken = default)
    {
#if WINDOWS
        if (!IsAvailable)
            throw new InvalidOperationException("Whisper server not found. Extract to: app/whisper-server/");

        if (_isRecording)
            return Task.CompletedTask;

        try
        {
            UpdateState(RecognitionState.Initializing);

            // Initialize streaming state
            _recordingStartTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            _confirmedWords.Clear();
            _lastConfirmedTimestamp = 0;
            _isStreamingInProgress = false;

            lock (_audioLock)
            {
                _fullAudioStream = new MemoryStream();
                _fullAudioWriter = new WaveFileWriter(
                    new IgnoreDisposeStream(_fullAudioStream),
                    new WaveFormat(16000, 16, 1));
            }

            _waveIn = new WaveInEvent
            {
                WaveFormat = new WaveFormat(16000, 16, 1)
            };

            _waveIn.DataAvailable += OnDataAvailable;
            _waveIn.RecordingStopped += OnRecordingStopped;

            _waveIn.StartRecording();
            _isRecording = true;

            // Start streaming timer if enabled
            var settings = _settingsService.Current;
            if (settings.WhisperStreamingEnabled)
            {
                _streamingTimer = new System.Timers.Timer(settings.WhisperStreamingIntervalMs);
                _streamingTimer.Elapsed += OnStreamingTimerElapsed;
                _streamingTimer.AutoReset = true;
                _streamingTimer.Start();
                Log($"Streaming enabled: window={settings.WhisperStreamingWindowSeconds}s, interval={settings.WhisperStreamingIntervalMs}ms");
            }

            UpdateState(RecognitionState.Listening);
            RecognitionPartial?.Invoke(this, new TranscriptionEventArgs("Listening...", false, false));

            Log("Started recording with Whisper Server");
        }
        catch (Exception ex)
        {
            RecognitionError?.Invoke(this, new RecognitionErrorEventArgs(
                $"Failed to start audio recording: {ex.Message}", ex));
            UpdateState(RecognitionState.Error);
            throw;
        }

        return Task.CompletedTask;
#else
        throw new PlatformNotSupportedException("Whisper Server is not available on this platform yet.");
#endif
    }

    public async Task<string> StopRecognitionAsync()
    {
#if WINDOWS
        Log("StopRecognitionAsync called");

        // Stop streaming timer first
        if (_streamingTimer != null)
        {
            _streamingTimer.Stop();
            _streamingTimer.Elapsed -= OnStreamingTimerElapsed;
            _streamingTimer.Dispose();
            _streamingTimer = null;
            Log("Streaming timer stopped");
        }

        if (!_isRecording || _waveIn == null)
        {
            Log("Not recording, returning empty");
            return string.Empty;
        }

        try
        {
            Log("Updating state to Processing");
            UpdateState(RecognitionState.Processing);

            Log("Stopping audio recording");
            _waveIn.StopRecording();
            _isRecording = false;

            byte[]? audioData = null;
            lock (_audioLock)
            {
                if (_fullAudioWriter != null && _fullAudioStream != null)
                {
                    _fullAudioWriter.Flush();
                    audioData = _fullAudioStream.ToArray();
                    Log($"Audio data: {audioData?.Length ?? 0} bytes");
                }
            }

            string transcription = string.Empty;

            if (audioData != null && audioData.Length > 16000) // At least ~0.5 seconds
            {
                Log("Sending to whisper server for final transcription...");
                transcription = await TranscribeViaServerAsync(audioData);
                Log($"Final transcription: '{transcription}'");
            }
            else
            {
                Log($"Audio too short: {audioData?.Length ?? 0} bytes");
            }

            // Clear streaming state
            _confirmedWords.Clear();
            _lastConfirmedTimestamp = 0;

            RecognitionCompleted?.Invoke(this, new TranscriptionEventArgs(transcription, true));
            UpdateState(RecognitionState.Idle);

            return transcription;
        }
        catch (Exception ex)
        {
            Log($"Error: {ex.Message}");
            RecognitionError?.Invoke(this, new RecognitionErrorEventArgs(ex.Message, ex));
            UpdateState(RecognitionState.Error);
            return string.Empty;
        }
        finally
        {
            CleanupRecording();
        }
#else
        return string.Empty;
#endif
    }

    private async Task<string> TranscribeViaServerAsync(byte[] audioData)
    {
        try
        {
            // Ensure server is running
            await EnsureServerRunningAsync();

            var settings = _settingsService.Current;
            var url = $"http://127.0.0.1:{_serverPort}/inference";

            // Create multipart form data with the audio file
            using var content = new MultipartFormDataContent();

            // Add audio file
            var audioContent = new ByteArrayContent(audioData);
            audioContent.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
            content.Add(audioContent, "file", "audio.wav");

            // Add parameters
            var language = settings.FasterWhisperLanguage;
            if (string.IsNullOrEmpty(language) || language.ToLower() == "auto")
                language = "en";
            content.Add(new StringContent(language), "language");
            content.Add(new StringContent("json"), "response_format");

            Log($"Sending request to {url}...");
            var startTime = DateTime.UtcNow;

            var response = await _httpClient.PostAsync(url, content);
            response.EnsureSuccessStatusCode();

            var elapsed = (DateTime.UtcNow - startTime).TotalMilliseconds;
            Log($"Server response in {elapsed:F0}ms");

            var responseText = await response.Content.ReadAsStringAsync();
            Log($"Response: {responseText.Substring(0, Math.Min(200, responseText.Length))}...");

            // Parse response
            return ParseServerResponse(responseText);
        }
        catch (HttpRequestException ex)
        {
            Log($"Server request failed: {ex.Message}");

            // Try to restart server
            lock (_serverLock)
            {
                _serverStarted = false;
                _serverProcess?.Kill();
                _serverProcess = null;
            }

            throw new Exception($"Whisper server not responding. Please restart the application.", ex);
        }
    }

    private string ParseServerResponse(string responseText)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseText);
            var root = doc.RootElement;

            // whisper.cpp server returns {"text": "..."}
            if (root.TryGetProperty("text", out var textProp))
            {
                return CleanTranscriptionText(textProp.GetString());
            }

            // Alternative format with segments
            if (root.TryGetProperty("segments", out var segments))
            {
                var texts = new List<string>();
                foreach (var segment in segments.EnumerateArray())
                {
                    if (segment.TryGetProperty("text", out var segText))
                    {
                        var text = CleanTranscriptionText(segText.GetString());
                        if (!string.IsNullOrEmpty(text))
                            texts.Add(text);
                    }
                }
                return string.Join(" ", texts);
            }

            return string.Empty;
        }
        catch (JsonException ex)
        {
            Log($"Failed to parse response: {ex.Message}");
            // Return raw text if not JSON
            return CleanTranscriptionText(responseText);
        }
    }

    /// <summary>
    /// Cleans transcription text by removing carriage returns, newlines, and extra whitespace.
    /// </summary>
    private static string CleanTranscriptionText(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        // Replace carriage returns and newlines with spaces
        var cleaned = text
            .Replace("\r\n", " ")
            .Replace("\r", " ")
            .Replace("\n", " ");

        // Collapse multiple spaces into single space and trim
        while (cleaned.Contains("  "))
            cleaned = cleaned.Replace("  ", " ");

        return cleaned.Trim();
    }

#if WINDOWS
    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        lock (_audioLock)
        {
            if (_fullAudioWriter != null && _isRecording)
            {
                _fullAudioWriter.Write(e.Buffer, 0, e.BytesRecorded);
            }
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception != null)
        {
            RecognitionError?.Invoke(this, new RecognitionErrorEventArgs(
                $"Recording error: {e.Exception.Message}", e.Exception));
        }
    }

    // Track if a streaming transcription is already in progress
    private bool _isStreamingInProgress = false;

    /// <summary>
    /// Timer handler for streaming transcription.
    /// Uses a simple approach: transcribe all audio accumulated so far for accurate progressive results.
    /// </summary>
    private async void OnStreamingTimerElapsed(object? sender, ElapsedEventArgs e)
    {
        Log("Streaming timer fired");

        if (!_isRecording)
        {
            Log("Streaming: not recording, skipping");
            return;
        }

        if (_fullAudioStream == null)
        {
            Log("Streaming: no audio stream, skipping");
            return;
        }

        // Prevent overlapping transcriptions
        if (_isStreamingInProgress)
        {
            Log("Streaming: previous transcription still in progress, skipping");
            return;
        }

        try
        {
            _isStreamingInProgress = true;

            // Extract ALL audio recorded so far (not just a window)
            var (audioData, pcmData) = ExtractAllAudioWithPcm();

            Log($"Streaming: extracted audio - WAV: {audioData?.Length ?? 0} bytes, PCM: {pcmData?.Length ?? 0} bytes");

            if (audioData == null || pcmData == null || audioData.Length < 16000) // Too short (~0.5s)
            {
                Log("Streaming: audio too short, skipping");
                return;
            }

            // Check if the recent audio is silent - use last 2 seconds for this check
            var recentPcm = GetRecentPcmData(pcmData, 2);
            var (isSilent, rmsValue) = IsAudioSilentWithRms(recentPcm);
            Log($"Streaming: Recent RMS={rmsValue:F0}, silent={isSilent}");

            if (isSilent)
            {
                Log("Streaming: recent audio is silent, skipping transcription");
                return;
            }

            var totalRecordedSeconds = GetTotalRecordedSeconds();
            Log($"Streaming: transcribing {totalRecordedSeconds:F1}s of audio");

            // Simple transcription of all audio - let Whisper handle it
            var text = await TranscribeSimpleAsync(audioData);

            if (!string.IsNullOrEmpty(text))
            {
                RecognitionPartial?.Invoke(this, new TranscriptionEventArgs(text, false, false));
                Log($"Streaming partial: '{text.Substring(0, Math.Min(50, text.Length))}...'");
            }
            else
            {
                Log("Streaming: no text returned from transcription");
            }
        }
        catch (Exception ex)
        {
            Log($"Streaming error: {ex.Message}\n{ex.StackTrace}");
            // Don't throw - just skip this cycle
        }
        finally
        {
            _isStreamingInProgress = false;
        }
    }

    /// <summary>
    /// Gets the last N seconds of PCM data from a buffer.
    /// </summary>
    private byte[] GetRecentPcmData(byte[] fullPcm, int seconds)
    {
        var bytesPerSecond = 16000 * 2; // 16kHz, 16-bit mono
        var bytesNeeded = seconds * bytesPerSecond;

        if (fullPcm.Length <= bytesNeeded)
            return fullPcm;

        var result = new byte[bytesNeeded];
        Array.Copy(fullPcm, fullPcm.Length - bytesNeeded, result, 0, bytesNeeded);
        return result;
    }

    /// <summary>
    /// Extracts all audio recorded so far.
    /// Returns both WAV data (for transcription) and raw PCM data (for silence detection).
    /// </summary>
    private (byte[]? wavData, byte[]? pcmData) ExtractAllAudioWithPcm()
    {
        lock (_audioLock)
        {
            if (_fullAudioStream == null || _fullAudioWriter == null) return (null, null);

            _fullAudioWriter.Flush();

            var totalBytes = _fullAudioStream.Position;
            if (totalBytes == 0) return (null, null);

            // Read all PCM data
            _fullAudioStream.Position = 0;
            var pcmBuffer = new byte[totalBytes];
            _fullAudioStream.Read(pcmBuffer, 0, (int)totalBytes);
            _fullAudioStream.Position = totalBytes; // Reset to end

            return (CreateWavFromPcm(pcmBuffer), pcmBuffer);
        }
    }

    /// <summary>
    /// Simple transcription without word timestamps - faster and more reliable for streaming.
    /// </summary>
    private async Task<string> TranscribeSimpleAsync(byte[] audioData)
    {
        await EnsureServerRunningAsync();

        var settings = _settingsService.Current;
        var url = $"http://127.0.0.1:{_serverPort}/inference";

        using var content = new MultipartFormDataContent();

        // Add audio file
        var audioContent = new ByteArrayContent(audioData);
        audioContent.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        content.Add(audioContent, "file", "audio.wav");

        // Use simple json format (faster than verbose_json)
        content.Add(new StringContent("json"), "response_format");

        var language = settings.FasterWhisperLanguage;
        if (string.IsNullOrEmpty(language) || language.ToLower() == "auto")
            language = "en";
        content.Add(new StringContent(language), "language");

        var response = await _httpClient.PostAsync(url, content);
        response.EnsureSuccessStatusCode();

        var responseText = await response.Content.ReadAsStringAsync();
        return ParseServerResponse(responseText);
    }

    /// <summary>
    /// Checks if audio data is silent by calculating RMS and comparing to threshold.
    /// Returns both the result and the RMS value for logging.
    /// </summary>
    private (bool isSilent, double rms) IsAudioSilentWithRms(byte[] pcmData, double silenceThreshold = 50)
    {
        if (pcmData.Length < 2) return (true, 0);

        // PCM 16-bit: 2 bytes per sample, little-endian
        double sumSquares = 0;
        int sampleCount = pcmData.Length / 2;

        for (int i = 0; i < pcmData.Length - 1; i += 2)
        {
            short sample = (short)(pcmData[i] | (pcmData[i + 1] << 8));
            sumSquares += sample * sample;
        }

        double rms = Math.Sqrt(sumSquares / sampleCount);

        // Threshold: ~50 catches actual silence while allowing quiet speech
        // (max RMS is 32767 for 16-bit audio, normal speech is typically 100-500+)
        return (rms < silenceThreshold, rms);
    }

    // Keep the old method for compatibility but delegate to new one
    private bool IsAudioSilent(byte[] pcmData, double silenceThreshold = 50)
    {
        return IsAudioSilentWithRms(pcmData, silenceThreshold).isSilent;
    }

    /// <summary>
    /// Extracts the last N seconds of audio from the recording buffer.
    /// Returns both WAV data (for transcription) and raw PCM data (for silence detection).
    /// </summary>
    private (byte[]? wavData, byte[]? pcmData) ExtractLastNSecondsWithPcm(int seconds)
    {
        lock (_audioLock)
        {
            if (_fullAudioStream == null || _fullAudioWriter == null) return (null, null);

            _fullAudioWriter.Flush();

            // Audio format: 16kHz, 16-bit, mono = 32000 bytes per second
            var bytesPerSecond = 16000 * 2;
            var windowBytes = seconds * bytesPerSecond;

            // Save current position
            var totalBytes = _fullAudioStream.Position;

            byte[] pcmBuffer;
            if (totalBytes < windowBytes)
            {
                // Return all audio if less than window
                _fullAudioStream.Position = 0;
                pcmBuffer = new byte[totalBytes];
                _fullAudioStream.Read(pcmBuffer, 0, (int)totalBytes);
                _fullAudioStream.Position = totalBytes; // Reset to end
            }
            else
            {
                // Extract last N seconds
                var startPosition = totalBytes - windowBytes;
                _fullAudioStream.Position = startPosition;
                pcmBuffer = new byte[windowBytes];
                _fullAudioStream.Read(pcmBuffer, 0, windowBytes);
                _fullAudioStream.Position = totalBytes; // Reset to end
            }

            return (CreateWavFromPcm(pcmBuffer), pcmBuffer);
        }
    }

    /// <summary>
    /// Gets the total recorded time in seconds.
    /// </summary>
    private double GetTotalRecordedSeconds()
    {
        lock (_audioLock)
        {
            if (_fullAudioStream == null) return 0;

            // Audio format: 16kHz, 16-bit, mono = 32000 bytes per second
            var bytesPerSecond = 16000 * 2;
            return _fullAudioStream.Position / (double)bytesPerSecond;
        }
    }

    /// <summary>
    /// Creates a WAV file from raw PCM data.
    /// </summary>
    private byte[] CreateWavFromPcm(byte[] pcmData)
    {
        using var ms = new MemoryStream();
        using var writer = new WaveFileWriter(ms, new WaveFormat(16000, 16, 1));
        writer.Write(pcmData, 0, pcmData.Length);
        writer.Flush();
        return ms.ToArray();
    }

    /// <summary>
    /// Transcribes audio with word-level timestamps using verbose_json format.
    /// </summary>
    private async Task<List<TranscribedWord>> TranscribeWithTimestampsAsync(byte[] audioData, double windowStartTime)
    {
        await EnsureServerRunningAsync();

        var settings = _settingsService.Current;
        var url = $"http://127.0.0.1:{_serverPort}/inference";

        using var content = new MultipartFormDataContent();

        // Add audio file
        var audioContent = new ByteArrayContent(audioData);
        audioContent.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        content.Add(audioContent, "file", "audio.wav");

        // Request verbose_json for word-level timestamps
        content.Add(new StringContent("verbose_json"), "response_format");

        var language = settings.FasterWhisperLanguage;
        if (string.IsNullOrEmpty(language) || language.ToLower() == "auto")
            language = "en";
        content.Add(new StringContent(language), "language");

        var response = await _httpClient.PostAsync(url, content);
        response.EnsureSuccessStatusCode();

        var responseText = await response.Content.ReadAsStringAsync();
        return ParseWordsWithTimestamps(responseText, windowStartTime);
    }

    /// <summary>
    /// Parses word-level timestamps from verbose_json response.
    /// </summary>
    private List<TranscribedWord> ParseWordsWithTimestamps(string json, double windowStartTime)
    {
        var words = new List<TranscribedWord>();

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // verbose_json format has "segments" with "words" array
            if (root.TryGetProperty("segments", out var segments))
            {
                foreach (var segment in segments.EnumerateArray())
                {
                    if (segment.TryGetProperty("words", out var wordsArray))
                    {
                        foreach (var word in wordsArray.EnumerateArray())
                        {
                            var text = word.TryGetProperty("word", out var wordProp)
                                ? wordProp.GetString()?.Trim()
                                : null;
                            var start = word.TryGetProperty("start", out var startProp)
                                ? startProp.GetDouble()
                                : 0;
                            var end = word.TryGetProperty("end", out var endProp)
                                ? endProp.GetDouble()
                                : 0;

                            if (!string.IsNullOrEmpty(text))
                            {
                                words.Add(new TranscribedWord
                                {
                                    Text = text,
                                    StartTime = windowStartTime + start,  // Convert to absolute time
                                    EndTime = windowStartTime + end
                                });
                            }
                        }
                    }
                    else
                    {
                        // Fallback: if no word-level timestamps, use segment text with segment timestamps
                        var segText = segment.TryGetProperty("text", out var textProp)
                            ? textProp.GetString()?.Trim()
                            : null;
                        var segStart = segment.TryGetProperty("start", out var segStartProp)
                            ? segStartProp.GetDouble()
                            : 0;
                        var segEnd = segment.TryGetProperty("end", out var segEndProp)
                            ? segEndProp.GetDouble()
                            : 0;

                        if (!string.IsNullOrEmpty(segText))
                        {
                            // Split segment text into words and distribute timestamps
                            var segWords = segText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                            var duration = segEnd - segStart;
                            var wordDuration = segWords.Length > 0 ? duration / segWords.Length : duration;

                            for (int i = 0; i < segWords.Length; i++)
                            {
                                words.Add(new TranscribedWord
                                {
                                    Text = segWords[i],
                                    StartTime = windowStartTime + segStart + (i * wordDuration),
                                    EndTime = windowStartTime + segStart + ((i + 1) * wordDuration)
                                });
                            }
                        }
                    }
                }
            }
            else if (root.TryGetProperty("text", out var textProp))
            {
                // Simple format without timestamps - treat as single block
                var text = textProp.GetString()?.Trim();
                if (!string.IsNullOrEmpty(text))
                {
                    var simpleWords = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    var estimatedDuration = simpleWords.Length * 0.3; // Rough estimate: 0.3s per word
                    for (int i = 0; i < simpleWords.Length; i++)
                    {
                        words.Add(new TranscribedWord
                        {
                            Text = simpleWords[i],
                            StartTime = windowStartTime + (i * 0.3),
                            EndTime = windowStartTime + ((i + 1) * 0.3)
                        });
                    }
                }
            }
        }
        catch (JsonException ex)
        {
            Log($"Failed to parse word timestamps: {ex.Message}");
        }

        return words;
    }

    /// <summary>
    /// Merges new words with confirmed words using timestamp-based alignment.
    /// </summary>
    private void MergeWords(List<TranscribedWord> newWords, double windowStartTime)
    {
        if (newWords.Count == 0) return;

        // Find where new words start after our last confirmed timestamp
        // Add small overlap tolerance (0.3 seconds) to handle boundary cases
        var overlapTolerance = 0.3;
        var cutoffTime = _lastConfirmedTimestamp - overlapTolerance;

        foreach (var word in newWords)
        {
            // Only add words that start after our last confirmed position
            if (word.StartTime >= cutoffTime)
            {
                // Check for duplicates (same word at approximately same time)
                var isDuplicate = _confirmedWords.Any(w =>
                    w.Text.Equals(word.Text, StringComparison.OrdinalIgnoreCase) &&
                    Math.Abs(w.StartTime - word.StartTime) < 0.5);

                if (!isDuplicate)
                {
                    _confirmedWords.Add(word);
                    _lastConfirmedTimestamp = Math.Max(_lastConfirmedTimestamp, word.EndTime);
                }
            }
        }

        // Sort by timestamp (in case of out-of-order additions)
        _confirmedWords.Sort((a, b) => a.StartTime.CompareTo(b.StartTime));
    }
#endif

    private void CleanupRecording()
    {
#if WINDOWS
        if (_waveIn != null)
        {
            _waveIn.DataAvailable -= OnDataAvailable;
            _waveIn.RecordingStopped -= OnRecordingStopped;
            _waveIn.Dispose();
            _waveIn = null;
        }
#endif

        lock (_audioLock)
        {
#if WINDOWS
            _fullAudioWriter?.Dispose();
            _fullAudioWriter = null;
#endif
            _fullAudioStream?.Dispose();
            _fullAudioStream = null;
        }
    }

    private void UpdateState(RecognitionState newState)
    {
        var oldState = CurrentState;
        CurrentState = newState;
        StateChanged?.Invoke(this, new RecognitionStateChangedEventArgs(oldState, newState));
    }

    private void Log(string message)
    {
        _loggingService.Log("WhisperServer", message);
    }

    /// <summary>
    /// Stops the whisper server process.
    /// </summary>
    public static void StopServer()
    {
        lock (_serverLock)
        {
            StopServerInternal();
        }
    }

    /// <summary>
    /// Internal method to stop the server. Must be called within _serverLock.
    /// </summary>
    private static void StopServerInternal()
    {
        if (_serverProcess != null && !_serverProcess.HasExited)
        {
            try
            {
                _serverProcess.Kill();
                _serverProcess.WaitForExit(3000); // Wait up to 3 seconds for clean exit
            }
            catch { }
        }
        _serverProcess = null;
        _serverStarted = false;
        _currentLoadedModel = null;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        // Stop streaming timer
        if (_streamingTimer != null)
        {
            _streamingTimer.Stop();
            _streamingTimer.Elapsed -= OnStreamingTimerElapsed;
            _streamingTimer.Dispose();
            _streamingTimer = null;
        }

        if (_isRecording)
        {
#if WINDOWS
            _waveIn?.StopRecording();
#endif
            _isRecording = false;
        }

        CleanupRecording();
        _httpClient.Dispose();

        // Don't stop the server on dispose - it should stay running for future use
        // StopServer();

        _disposed = true;
        GC.SuppressFinalize(this);
    }

    ~WhisperServerSpeechRecognitionService()
    {
        Dispose();
    }

    /// <summary>
    /// Represents a transcribed word with timestamp information for merging.
    /// </summary>
    private class TranscribedWord
    {
        public string Text { get; set; } = "";
        public double StartTime { get; set; }  // Absolute time from recording start (seconds)
        public double EndTime { get; set; }
    }

#if WINDOWS
    private class IgnoreDisposeStream : Stream
    {
        private readonly Stream _inner;
        public IgnoreDisposeStream(Stream inner) => _inner = inner;
        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => _inner.CanSeek;
        public override bool CanWrite => _inner.CanWrite;
        public override long Length => _inner.Length;
        public override long Position
        {
            get => _inner.Position;
            set => _inner.Position = value;
        }
        public override void Flush() => _inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
        public override void SetLength(long value) => _inner.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count) => _inner.Write(buffer, offset, count);
        protected override void Dispose(bool disposing) { }
    }
#endif
}
