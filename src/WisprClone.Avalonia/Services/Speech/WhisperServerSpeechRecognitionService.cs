#if WINDOWS
using NAudio.Wave;
#endif
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
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

    private static Process? _serverProcess;
    private static readonly object _serverLock = new();
    private static bool _serverStarted;
    private static int _serverPort = 8178;

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
    /// </summary>
    public async Task EnsureServerRunningAsync()
    {
#if WINDOWS
        lock (_serverLock)
        {
            if (_serverStarted && _serverProcess != null && !_serverProcess.HasExited)
            {
                Log("Server already running");
                return;
            }
        }

        var serverExe = GetServerExePath();
        var modelPath = GetModelPath();
        var settings = _settingsService.Current;
        _serverPort = settings.WhisperCppServerPort;

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
                Log($"Server started with PID: {_serverProcess.Id}");
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
                Log("Sending to whisper server...");
                transcription = await TranscribeViaServerAsync(audioData);
                Log($"Transcription: '{transcription}'");
            }
            else
            {
                Log($"Audio too short: {audioData?.Length ?? 0} bytes");
            }

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
                return textProp.GetString()?.Trim() ?? string.Empty;
            }

            // Alternative format with segments
            if (root.TryGetProperty("segments", out var segments))
            {
                var texts = new List<string>();
                foreach (var segment in segments.EnumerateArray())
                {
                    if (segment.TryGetProperty("text", out var segText))
                    {
                        var text = segText.GetString()?.Trim();
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
            return responseText.Trim();
        }
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
            if (_serverProcess != null && !_serverProcess.HasExited)
            {
                try
                {
                    _serverProcess.Kill();
                }
                catch { }
            }
            _serverProcess = null;
            _serverStarted = false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

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
