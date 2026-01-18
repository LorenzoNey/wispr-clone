#if WINDOWS
using NAudio.Wave;
#endif
using OpenAI;
using OpenAI.Audio;
using System.IO;
using System.Timers;
using WisprClone.Core;
using WisprClone.Services.Interfaces;
using Timer = System.Timers.Timer;

namespace WisprClone.Services.Speech;

/// <summary>
/// OpenAI Whisper API implementation with progressive transcription for real-time updates.
/// Currently requires Windows for audio capture via NAudio.
/// TODO: Add cross-platform audio capture support.
/// </summary>
public class OpenAIWhisperSpeechRecognitionService : ISpeechRecognitionService
{
    private string _apiKey = string.Empty;
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
    private const int TranscriptionIntervalMs = 2000;
    private const int MinAudioSize = 16000;
    private const float SilenceThreshold = 500f;
    private int _lastProcessedLength = 0;

    public event EventHandler<TranscriptionEventArgs>? RecognitionPartial;
    public event EventHandler<TranscriptionEventArgs>? RecognitionCompleted;
    public event EventHandler<RecognitionErrorEventArgs>? RecognitionError;
    public event EventHandler<RecognitionStateChangedEventArgs>? StateChanged;
    public event EventHandler<string>? LanguageChanged;

    public RecognitionState CurrentState { get; private set; } = RecognitionState.Idle;
    public string ProviderName => "OpenAI Whisper";
    public string CurrentLanguage { get; private set; } = "en-US";

    // On non-Windows, Whisper is not available until cross-platform audio capture is implemented
#if WINDOWS
    public bool IsAvailable => !string.IsNullOrEmpty(_apiKey);
#else
    public bool IsAvailable => false; // TODO: Implement cross-platform audio capture
#endif

    public void Configure(string apiKey)
    {
        _apiKey = apiKey;
    }

    public Task InitializeAsync(string language = "en-US")
    {
        CurrentLanguage = language;
        _language = language.Split('-')[0].ToLowerInvariant();
        return Task.CompletedTask;
    }

    public Task StartRecognitionAsync(CancellationToken cancellationToken = default)
    {
#if WINDOWS
        if (!IsAvailable)
            throw new InvalidOperationException("OpenAI API key not configured");

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

            _transcriptionTimer = new Timer(TranscriptionIntervalMs);
            _transcriptionTimer.Elapsed += OnTranscriptionTimerElapsed;
            _transcriptionTimer.AutoReset = true;
            _transcriptionTimer.Start();

            UpdateState(RecognitionState.Listening);
            RecognitionPartial?.Invoke(this, new TranscriptionEventArgs("Listening...", false, false));

            Log("Started recording with progressive full-audio transcription");
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
        throw new PlatformNotSupportedException("OpenAI Whisper is not available on this platform yet. Please use Azure Speech Service.");
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

            Log($"Transcribing full audio: {fullAudioData.Length} bytes");
            var fullText = await TranscribeWithWhisperAsync(fullAudioData);

            if (!string.IsNullOrWhiteSpace(fullText) && fullText != _lastTranscription)
            {
                _lastTranscription = fullText;
                Log($"Full transcription received (length: {fullText.Length})");
                // Whisper re-transcribes entire audio, so each update is finalized
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
        if (!_isRecording || _waveIn == null)
            return string.Empty;

        try
        {
            UpdateState(RecognitionState.Processing);

            _transcriptionTimer?.Stop();
            _transcriptionTimer?.Dispose();
            _transcriptionTimer = null;

            _waveIn.StopRecording();
            _isRecording = false;

            byte[]? finalAudioData = null;
            lock (_audioLock)
            {
                if (_fullAudioWriter != null && _fullAudioStream != null)
                {
                    _fullAudioWriter.Flush();
                    finalAudioData = _fullAudioStream.ToArray();
                }
            }

            string finalText = _lastTranscription;

            if (finalAudioData != null && finalAudioData.Length > MinAudioSize)
            {
                Log($"Final transcription of full audio: {finalAudioData.Length} bytes");
                var transcribedText = await TranscribeWithWhisperAsync(finalAudioData);

                if (!string.IsNullOrWhiteSpace(transcribedText))
                {
                    finalText = transcribedText;
                }
            }

            Log($"Final result received (length: {finalText.Length})");

            RecognitionCompleted?.Invoke(this, new TranscriptionEventArgs(finalText, true));
            UpdateState(RecognitionState.Idle);

            return finalText;
        }
        catch (Exception ex)
        {
            RecognitionError?.Invoke(this, new RecognitionErrorEventArgs(
                $"Failed to transcribe audio: {ex.Message}", ex));
            UpdateState(RecognitionState.Error);
            return _lastTranscription;
        }
        finally
        {
            CleanupRecording();
        }
#else
        return string.Empty;
#endif
    }

    private async Task<string> TranscribeWithWhisperAsync(byte[] audioData)
    {
        try
        {
            var client = new OpenAIClient(_apiKey);
            var audioClient = client.GetAudioClient("whisper-1");

            using var audioStream = new MemoryStream(audioData);

            var options = new AudioTranscriptionOptions
            {
                Language = _language,
                ResponseFormat = AudioTranscriptionFormat.Text
            };

            var result = await audioClient.TranscribeAudioAsync(
                audioStream,
                "audio.wav",
                options);

            return result.Value.Text?.Trim() ?? string.Empty;
        }
        catch (Exception ex)
        {
            Log($"Whisper API error: {ex.Message}");
            throw;
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

    private static void Log(string message)
    {
        var logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WisprClone", "wispr_log.txt");
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [Whisper] {message}";
        try { File.AppendAllText(logPath, line + Environment.NewLine); } catch { }
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

    ~OpenAIWhisperSpeechRecognitionService()
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
