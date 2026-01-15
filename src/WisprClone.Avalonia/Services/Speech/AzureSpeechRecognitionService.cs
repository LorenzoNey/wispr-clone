using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using System.Text;
using WisprClone.Core;
using WisprClone.Services.Interfaces;

namespace WisprClone.Services.Speech;

/// <summary>
/// Azure Speech Services implementation for speech recognition.
/// Cross-platform compatible - Azure SDK supports Windows, macOS, and Linux.
/// </summary>
public class AzureSpeechRecognitionService : ISpeechRecognitionService
{
    private string _subscriptionKey = string.Empty;
    private string _region = string.Empty;

    private SpeechRecognizer? _recognizer;
    private AudioConfig? _audioConfig;
    private readonly StringBuilder _transcriptionBuffer = new();
    private bool _disposed;

    public event EventHandler<TranscriptionEventArgs>? RecognitionPartial;
    public event EventHandler<TranscriptionEventArgs>? RecognitionCompleted;
    public event EventHandler<RecognitionErrorEventArgs>? RecognitionError;
    public event EventHandler<RecognitionStateChangedEventArgs>? StateChanged;
    public event EventHandler<string>? LanguageChanged;

    public RecognitionState CurrentState { get; private set; } = RecognitionState.Idle;
    public string ProviderName => "Azure Speech Service";
    public string CurrentLanguage { get; private set; } = "en-US";
    public bool IsAvailable => !string.IsNullOrEmpty(_subscriptionKey) && !string.IsNullOrEmpty(_region);

    private static void Log(string message)
    {
        var logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "wispr_log.txt");
        var line = $"[{DateTime.Now:HH:mm:ss.fff}] [Azure] {message}";
        try { File.AppendAllText(logPath, line + Environment.NewLine); } catch { }
    }

    /// <summary>
    /// Configures the Azure Speech Service with credentials.
    /// </summary>
    public void Configure(string subscriptionKey, string region)
    {
        _subscriptionKey = subscriptionKey;
        _region = region;
        Log($"Configured with region: '{region}', key length: {subscriptionKey?.Length ?? 0}");
    }

    public async Task InitializeAsync(string language = "en-US")
    {
        Log($"InitializeAsync called, IsAvailable: {IsAvailable}, language: {language}");
        CurrentLanguage = language;

        if (!IsAvailable)
        {
            Log("Azure not configured, skipping initialization");
            return;
        }

        try
        {
            Log($"Creating SpeechConfig with region: '{_region}'");
            var speechConfig = SpeechConfig.FromSubscription(_subscriptionKey, _region);
            speechConfig.SpeechRecognitionLanguage = language;

            // Enable real-time partial results
            speechConfig.SetProperty(
                PropertyId.SpeechServiceResponse_RequestSentenceBoundary, "true");

            Log("Creating AudioConfig from default microphone");
            _audioConfig = AudioConfig.FromDefaultMicrophoneInput();

            Log("Creating SpeechRecognizer");
            _recognizer = new SpeechRecognizer(speechConfig, _audioConfig);

            // Wire up events
            _recognizer.Recognizing += OnRecognizing;
            _recognizer.Recognized += OnRecognized;
            _recognizer.Canceled += OnCanceled;
            _recognizer.SessionStarted += OnSessionStarted;
            _recognizer.SessionStopped += OnSessionStopped;

            Log("Azure Speech Service initialized successfully");
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            Log($"Initialization error: {ex.Message}");
            RecognitionError?.Invoke(this, new RecognitionErrorEventArgs(
                $"Failed to initialize Azure recognition: {ex.Message}", ex));
            throw;
        }
    }

    public async Task StartRecognitionAsync(CancellationToken cancellationToken = default)
    {
        Log($"StartRecognitionAsync called, recognizer null: {_recognizer == null}");

        if (_recognizer == null)
        {
            Log("ERROR: Recognizer is null!");
            throw new InvalidOperationException("Recognizer not initialized or Azure not configured");
        }

        _transcriptionBuffer.Clear();
        UpdateState(RecognitionState.Listening);

        Log("Calling StartContinuousRecognitionAsync...");
        await _recognizer.StartContinuousRecognitionAsync();
        Log("StartContinuousRecognitionAsync completed");
    }

    public async Task<string> StopRecognitionAsync()
    {
        Log("StopRecognitionAsync called");

        if (_recognizer == null)
        {
            Log("Recognizer is null, returning empty");
            return string.Empty;
        }

        UpdateState(RecognitionState.Processing);

        Log("Calling StopContinuousRecognitionAsync...");
        await _recognizer.StopContinuousRecognitionAsync();
        Log("StopContinuousRecognitionAsync completed");

        var finalText = _transcriptionBuffer.ToString().Trim();
        Log($"Final text: '{finalText}' (length: {finalText.Length})");

        RecognitionCompleted?.Invoke(this, new TranscriptionEventArgs(finalText, true));
        UpdateState(RecognitionState.Idle);

        return finalText;
    }

    private void OnRecognizing(object? sender, SpeechRecognitionEventArgs e)
    {
        Log($"OnRecognizing: Reason={e.Result.Reason}, Text='{e.Result.Text}'");

        if (e.Result.Reason == ResultReason.RecognizingSpeech)
        {
            // Interim hypothesis - show in UI but don't copy to clipboard
            var currentText = _transcriptionBuffer.ToString() + e.Result.Text;
            RecognitionPartial?.Invoke(this, new TranscriptionEventArgs(currentText, false, false));
        }
    }

    private void OnRecognized(object? sender, SpeechRecognitionEventArgs e)
    {
        Log($"OnRecognized: Reason={e.Result.Reason}, Text='{e.Result.Text}'");

        if (e.Result.Reason == ResultReason.RecognizedSpeech)
        {
            // Finalized segment - append to buffer and copy to clipboard
            _transcriptionBuffer.Append(e.Result.Text + " ");
            RecognitionPartial?.Invoke(this,
                new TranscriptionEventArgs(_transcriptionBuffer.ToString(), false, true));
        }
        else if (e.Result.Reason == ResultReason.NoMatch)
        {
            Log("NoMatch - no speech was recognized");
        }
    }

    private void OnCanceled(object? sender, SpeechRecognitionCanceledEventArgs e)
    {
        Log($"OnCanceled: Reason={e.Reason}, ErrorCode={e.ErrorCode}, ErrorDetails='{e.ErrorDetails}'");

        if (e.Reason == CancellationReason.Error)
        {
            RecognitionError?.Invoke(this, new RecognitionErrorEventArgs(
                $"Azure Speech Error: {e.ErrorDetails} (Code: {e.ErrorCode})", null));
            UpdateState(RecognitionState.Error);
        }
        else if (e.Reason == CancellationReason.EndOfStream)
        {
            Log("EndOfStream - audio stream ended");
        }
    }

    private void OnSessionStarted(object? sender, SessionEventArgs e)
    {
        Log($"Session started: {e.SessionId}");
    }

    private void OnSessionStopped(object? sender, SessionEventArgs e)
    {
        Log($"Session stopped: {e.SessionId}");
    }

    private void UpdateState(RecognitionState newState)
    {
        var oldState = CurrentState;
        CurrentState = newState;
        StateChanged?.Invoke(this, new RecognitionStateChangedEventArgs(oldState, newState));
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        if (_recognizer != null)
        {
            _recognizer.Recognizing -= OnRecognizing;
            _recognizer.Recognized -= OnRecognized;
            _recognizer.Canceled -= OnCanceled;
            _recognizer.SessionStarted -= OnSessionStarted;
            _recognizer.SessionStopped -= OnSessionStopped;
            _recognizer.Dispose();
            _recognizer = null;
        }

        _audioConfig?.Dispose();
        _audioConfig = null;

        _disposed = true;
        GC.SuppressFinalize(this);
    }

    ~AzureSpeechRecognitionService()
    {
        Dispose();
    }
}
