#if WINDOWS
using NAudio.Wave;
#endif
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Timers;
using WisprClone.Core;
using WisprClone.Models;
using WisprClone.Services.Interfaces;
using Timer = System.Timers.Timer;

namespace WisprClone.Services.Speech;

/// <summary>
/// Faster-Whisper-XXL implementation with model preloading for fast transcription.
/// Uses a persistent server process to keep the model loaded in GPU memory.
/// </summary>
public class FasterWhisperSpeechRecognitionService : ISpeechRecognitionService
{
    private readonly ILoggingService _loggingService;
    private readonly ISettingsService _settingsService;
    private string _language = "en";
#if WINDOWS
    private WaveInEvent? _waveIn;
#endif
    private MemoryStream? _fullAudioStream;
#if WINDOWS
    private WaveFileWriter? _fullAudioWriter;
#endif
    private bool _disposed;
    private bool _isRecording;

    private string _lastTranscription = string.Empty;
    private Timer? _transcriptionTimer;
    private readonly object _audioLock = new();
    private const int TranscriptionIntervalMs = 3000; // Increased to reduce overhead
    private const int MinAudioSize = 16000;
    private const float SilenceThreshold = 500f;
    private int _lastProcessedLength = 0;

    // Server process for model preloading
    private static Process? _serverProcess;
    private static readonly object _serverLock = new();
    private static string? _serverWatchDir;
    private static bool _serverStarting;
    private static bool _modelLoaded;

    public event EventHandler<TranscriptionEventArgs>? RecognitionPartial;
    public event EventHandler<TranscriptionEventArgs>? RecognitionCompleted;
    public event EventHandler<RecognitionErrorEventArgs>? RecognitionError;
    public event EventHandler<RecognitionStateChangedEventArgs>? StateChanged;
    public event EventHandler<string>? LanguageChanged;

    public RecognitionState CurrentState { get; private set; } = RecognitionState.Idle;
    public string ProviderName => "Faster-Whisper";
    public string CurrentLanguage { get; private set; } = "en-US";

#if WINDOWS
    public bool IsAvailable => File.Exists(GetExePath());
#else
    public bool IsAvailable => false;
#endif

    public FasterWhisperSpeechRecognitionService(ILoggingService loggingService, ISettingsService settingsService)
    {
        _loggingService = loggingService;
        _settingsService = settingsService;
    }

    private string GetExePath()
    {
        return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "faster-whisper-xxl", "faster-whisper-xxl.exe");
    }

    public Task InitializeAsync(string language = "en-US")
    {
        CurrentLanguage = language;
        _language = language.Split('-')[0].ToLowerInvariant();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Warms up the model by running a tiny transcription at startup.
    /// Call this early to pre-load the model into GPU memory.
    /// </summary>
    public async Task WarmupModelAsync()
    {
#if WINDOWS
        if (!IsAvailable || _modelLoaded) return;

        try
        {
            Log("Warming up Faster-Whisper model...");

            // Create a tiny silent audio file (0.1 seconds)
            var tempDir = Path.Combine(Path.GetTempPath(), "wispr-faster-whisper");
            Directory.CreateDirectory(tempDir);
            var warmupFile = Path.Combine(tempDir, "warmup.wav");

            // Create minimal WAV file (16kHz, 16-bit, mono, 0.1 seconds = 1600 samples)
            using (var fs = new FileStream(warmupFile, FileMode.Create))
            using (var writer = new BinaryWriter(fs))
            {
                // WAV header
                writer.Write("RIFF".ToCharArray());
                writer.Write(3244); // File size - 8
                writer.Write("WAVE".ToCharArray());
                writer.Write("fmt ".ToCharArray());
                writer.Write(16); // Subchunk1Size
                writer.Write((short)1); // AudioFormat (PCM)
                writer.Write((short)1); // NumChannels
                writer.Write(16000); // SampleRate
                writer.Write(32000); // ByteRate
                writer.Write((short)2); // BlockAlign
                writer.Write((short)16); // BitsPerSample
                writer.Write("data".ToCharArray());
                writer.Write(3200); // Subchunk2Size

                // Write silence (1600 samples * 2 bytes)
                for (int i = 0; i < 1600; i++)
                    writer.Write((short)0);
            }

            var settings = _settingsService.Current;
            var args = BuildFasterWhisperArgs(warmupFile, tempDir, settings);

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = GetExePath(),
                    Arguments = args,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetDirectoryName(GetExePath())
                }
            };

            process.Start();
            await process.WaitForExitAsync();

            _modelLoaded = true;
            Log("Model warmup complete");

            // Cleanup
            try { File.Delete(warmupFile); } catch { }
        }
        catch (Exception ex)
        {
            Log($"Model warmup failed: {ex.Message}");
        }
#endif
    }

    public Task StartRecognitionAsync(CancellationToken cancellationToken = default)
    {
#if WINDOWS
        if (!IsAvailable)
            throw new InvalidOperationException("faster-whisper-xxl.exe not found. Extract to: app/faster-whisper-xxl/");

        if (_isRecording)
            return Task.CompletedTask;

        try
        {
            UpdateState(RecognitionState.Initializing);

            _lastTranscription = string.Empty;
            _lastProcessedLength = 0;

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

            // Progressive transcription - disabled by default for speed
            // Only enable if user wants real-time feedback (at cost of slower processing)
            var enableProgressive = false; // Could be a setting
            if (enableProgressive)
            {
                _transcriptionTimer = new Timer(TranscriptionIntervalMs);
                _transcriptionTimer.Elapsed += OnTranscriptionTimerElapsed;
                _transcriptionTimer.AutoReset = true;
                _transcriptionTimer.Start();
            }

            UpdateState(RecognitionState.Listening);
            RecognitionPartial?.Invoke(this, new TranscriptionEventArgs("Listening...", false, false));

            Log("Started recording with Faster-Whisper");
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
        throw new PlatformNotSupportedException("Faster-Whisper is not available on this platform yet.");
#endif
    }

#if WINDOWS
    private bool HasNewSpeechActivity(byte[] wavData)
    {
        const int wavHeaderSize = 44;
        if (wavData.Length <= wavHeaderSize + _lastProcessedLength)
            return false;

        int startOffset = wavHeaderSize + _lastProcessedLength;
        int newDataLength = wavData.Length - startOffset;

        if (newDataLength < 3200)
            return false;

        double sumSquares = 0;
        int sampleCount = 0;

        for (int i = startOffset; i < wavData.Length - 1; i += 2)
        {
            short sample = (short)(wavData[i] | (wavData[i + 1] << 8));
            sumSquares += sample * sample;
            sampleCount++;
        }

        if (sampleCount == 0)
            return false;

        double rms = Math.Sqrt(sumSquares / sampleCount);
        _lastProcessedLength = wavData.Length - wavHeaderSize;

        Log($"New audio segment RMS: {rms:F0} (threshold: {SilenceThreshold})");
        return rms > SilenceThreshold;
    }

    private async void OnTranscriptionTimerElapsed(object? sender, ElapsedEventArgs e)
    {
        if (!_isRecording) return;

        try
        {
            byte[]? fullAudioData = null;

            lock (_audioLock)
            {
                if (_fullAudioWriter == null || _fullAudioStream == null)
                    return;

                _fullAudioWriter.Flush();
                fullAudioData = _fullAudioStream.ToArray();
            }

            if (fullAudioData == null || fullAudioData.Length < MinAudioSize)
                return;

            if (!HasNewSpeechActivity(fullAudioData))
            {
                Log("Skipping transcription - no new speech detected");
                return;
            }

            Log($"Transcribing with Faster-Whisper: {fullAudioData.Length} bytes");
            var fullText = await TranscribeWithFasterWhisperAsync(fullAudioData);

            if (!string.IsNullOrWhiteSpace(fullText) && fullText != _lastTranscription)
            {
                _lastTranscription = fullText;
                Log($"Faster-Whisper transcription received (length: {fullText.Length})");
                RecognitionPartial?.Invoke(this, new TranscriptionEventArgs(fullText, false, true));
            }
        }
        catch (Exception ex)
        {
            Log($"Transcription error: {ex.Message}");
        }
    }
#endif

    public async Task<string> StopRecognitionAsync()
    {
#if WINDOWS
        Log("StopRecognitionAsync called");

        if (!_isRecording || _waveIn == null)
        {
            Log("Not recording or waveIn is null, returning empty");
            return string.Empty;
        }

        try
        {
            Log("Updating state to Processing");
            UpdateState(RecognitionState.Processing);

            Log("Stopping transcription timer");
            _transcriptionTimer?.Stop();
            _transcriptionTimer?.Dispose();
            _transcriptionTimer = null;

            Log("Stopping audio recording");
            _waveIn.StopRecording();
            _isRecording = false;

            byte[]? finalAudioData = null;
            lock (_audioLock)
            {
                if (_fullAudioWriter != null && _fullAudioStream != null)
                {
                    Log("Flushing audio writer");
                    _fullAudioWriter.Flush();
                    finalAudioData = _fullAudioStream.ToArray();
                    Log($"Final audio data: {finalAudioData?.Length ?? 0} bytes");
                }
            }

            string finalText = _lastTranscription;
            Log($"Last transcription was: '{(finalText.Length > 0 ? finalText.Substring(0, Math.Min(50, finalText.Length)) : "(empty)")}'...");

            if (finalAudioData != null && finalAudioData.Length > MinAudioSize)
            {
                Log($"Starting final Faster-Whisper transcription: {finalAudioData.Length} bytes");
                var transcribedText = await TranscribeWithFasterWhisperAsync(finalAudioData);
                Log($"Final transcription complete: '{(transcribedText.Length > 0 ? transcribedText.Substring(0, Math.Min(50, transcribedText.Length)) : "(empty)")}'...");

                if (!string.IsNullOrWhiteSpace(transcribedText))
                {
                    finalText = transcribedText;
                }
            }
            else
            {
                Log($"Skipping final transcription - audio data too small: {finalAudioData?.Length ?? 0} bytes");
            }

            Log($"Final result received (length: {finalText.Length})");

            RecognitionCompleted?.Invoke(this, new TranscriptionEventArgs(finalText, true));
            Log("Updating state to Idle");
            UpdateState(RecognitionState.Idle);

            return finalText;
        }
        catch (Exception ex)
        {
            Log($"StopRecognitionAsync error: {ex.GetType().Name}: {ex.Message}");
            RecognitionError?.Invoke(this, new RecognitionErrorEventArgs(
                $"Failed to transcribe audio: {ex.Message}", ex));
            UpdateState(RecognitionState.Error);
            return _lastTranscription;
        }
        finally
        {
            Log("Cleaning up recording resources");
            CleanupRecording();
        }
#else
        return string.Empty;
#endif
    }

    private async Task<string> TranscribeWithFasterWhisperAsync(byte[] audioData)
    {
        var settings = _settingsService.Current;
        var tempDir = Path.Combine(Path.GetTempPath(), "wispr-faster-whisper");
        Directory.CreateDirectory(tempDir);

        var tempWavPath = Path.Combine(tempDir, $"audio_{Guid.NewGuid():N}.wav");
        var tempJsonPath = Path.ChangeExtension(tempWavPath, ".json");

        try
        {
            // Write audio to temp file
            Log($"Writing {audioData.Length} bytes to temp WAV file: {tempWavPath}");
            await File.WriteAllBytesAsync(tempWavPath, audioData);
            Log("Temp WAV file written successfully");

            // Build command arguments
            var args = BuildFasterWhisperArgs(tempWavPath, tempDir, settings);
            Log($"Running faster-whisper-xxl with args: {args}");
            Log($"Executable path: {GetExePath()}");

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = GetExePath(),
                    Arguments = args,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetDirectoryName(GetExePath())
                }
            };

            Log("Starting faster-whisper-xxl process...");
            process.Start();
            Log($"Process started with PID: {process.Id}");

            // Read stdout and stderr concurrently to avoid deadlock
            Log("Reading stdout and stderr...");
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

            // Wait for process with timeout
            Log("Waiting for process to exit...");
            var processExitTask = process.WaitForExitAsync();
            var timeoutTask = Task.Delay(TimeSpan.FromMinutes(5));

            var completedTask = await Task.WhenAny(processExitTask, timeoutTask);

            if (completedTask == timeoutTask)
            {
                Log("Process timed out after 5 minutes, killing...");
                try { process.Kill(); } catch { }
                throw new TimeoutException("Faster-Whisper process timed out after 5 minutes");
            }

            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            Log($"Process exited with code: {process.ExitCode}");
            if (!string.IsNullOrWhiteSpace(stdout))
                Log($"Stdout (first 500 chars): {stdout.Substring(0, Math.Min(500, stdout.Length))}");
            if (!string.IsNullOrWhiteSpace(stderr))
                Log($"Stderr (first 500 chars): {stderr.Substring(0, Math.Min(500, stderr.Length))}");

            if (process.ExitCode != 0)
            {
                Log($"Faster-Whisper error (exit {process.ExitCode}): {stderr}");
                throw new Exception($"Faster-Whisper failed: {stderr}");
            }

            // Mark model as loaded after successful run
            _modelLoaded = true;

            // Parse JSON output
            var jsonOutputPath = Path.Combine(tempDir, Path.GetFileNameWithoutExtension(tempWavPath) + ".json");
            Log($"Looking for JSON output at: {jsonOutputPath}");

            if (File.Exists(jsonOutputPath))
            {
                Log("JSON output file found, reading...");
                var jsonContent = await File.ReadAllTextAsync(jsonOutputPath);
                Log($"JSON content length: {jsonContent.Length}");
                var result = ParseFasterWhisperJson(jsonContent);
                Log($"Parsed transcription: '{(result.Length > 0 ? result.Substring(0, Math.Min(100, result.Length)) : "(empty)")}'...");
                return result;
            }

            // Fallback: try to parse stdout as JSON
            if (!string.IsNullOrWhiteSpace(stdout) && stdout.TrimStart().StartsWith("{"))
            {
                Log("Parsing stdout as JSON...");
                return ParseFasterWhisperJson(stdout);
            }

            Log($"No JSON output found, stdout: {stdout}");
            return string.Empty;
        }
        catch (Exception ex)
        {
            Log($"TranscribeWithFasterWhisperAsync exception: {ex.GetType().Name}: {ex.Message}");
            throw;
        }
        finally
        {
            // Cleanup temp files
            Log("Cleaning up temp files...");
            try
            {
                if (File.Exists(tempWavPath)) File.Delete(tempWavPath);
                var possibleJsonPaths = new[]
                {
                    tempJsonPath,
                    Path.Combine(tempDir, Path.GetFileNameWithoutExtension(tempWavPath) + ".json")
                };
                foreach (var jsonPath in possibleJsonPaths)
                {
                    if (File.Exists(jsonPath)) File.Delete(jsonPath);
                }
            }
            catch { /* Ignore cleanup errors */ }
        }
    }

    private string BuildFasterWhisperArgs(string audioPath, string outputDir, AppSettings settings)
    {
        var args = new List<string>();

        // Model
        args.Add($"--model \"{settings.FasterWhisperModel}\"");

        // Output format and directory
        args.Add("--output_format json");
        args.Add($"--output_dir \"{outputDir}\"");

        // Language (omit for auto-detect)
        if (!string.IsNullOrEmpty(settings.FasterWhisperLanguage) &&
            settings.FasterWhisperLanguage.ToLower() != "auto")
        {
            args.Add($"--language {settings.FasterWhisperLanguage}");
        }

        // Device (GPU or CPU)
        if (settings.FasterWhisperUseGpu)
        {
            args.Add($"--device cuda:{settings.FasterWhisperDeviceId}");
        }
        else
        {
            args.Add("--device cpu");
        }

        // Compute type
        if (!string.IsNullOrEmpty(settings.FasterWhisperComputeType))
        {
            args.Add($"--compute_type {settings.FasterWhisperComputeType}");
        }

        // VAD method - map simple names to actual values
        if (!string.IsNullOrEmpty(settings.FasterWhisperVadMethod))
        {
            // Map user-friendly names to actual parameter values
            var vadMethod = settings.FasterWhisperVadMethod.ToLower() switch
            {
                "silero" => "silero_v5_fw",
                "silero_v5" => "silero_v5_fw",
                "silero_v4" => "silero_v4_fw",
                "pyannote" => "pyannote_v3",
                _ => settings.FasterWhisperVadMethod // Pass through if already valid
            };
            args.Add($"--vad_method {vadMethod}");
        }

        // Diarization - requires specific model name
        if (settings.FasterWhisperEnableDiarization)
        {
            args.Add("--diarize pyannote_v3.1");
        }

        // Audio file must be at the end (positional argument)
        args.Add($"\"{audioPath}\"");

        return string.Join(" ", args);
    }

    private string ParseFasterWhisperJson(string jsonContent)
    {
        try
        {
            using var doc = JsonDocument.Parse(jsonContent);
            var root = doc.RootElement;

            // Try to get segments array
            if (root.TryGetProperty("segments", out var segments))
            {
                var texts = new List<string>();
                foreach (var segment in segments.EnumerateArray())
                {
                    if (segment.TryGetProperty("text", out var textProp))
                    {
                        var text = textProp.GetString()?.Trim();
                        if (!string.IsNullOrEmpty(text))
                        {
                            texts.Add(text);
                        }
                    }
                }
                return string.Join(" ", texts);
            }

            // Try to get text directly
            if (root.TryGetProperty("text", out var directText))
            {
                return directText.GetString()?.Trim() ?? string.Empty;
            }

            return string.Empty;
        }
        catch (JsonException ex)
        {
            Log($"Failed to parse JSON: {ex.Message}");
            return string.Empty;
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
        _transcriptionTimer?.Stop();
        _transcriptionTimer?.Dispose();
        _transcriptionTimer = null;

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
        _loggingService.Log("FasterWhisper", message);
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

        _disposed = true;
        GC.SuppressFinalize(this);
    }

    ~FasterWhisperSpeechRecognitionService()
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

        protected override void Dispose(bool disposing)
        {
            // Don't dispose the inner stream
        }
    }
#endif
}
