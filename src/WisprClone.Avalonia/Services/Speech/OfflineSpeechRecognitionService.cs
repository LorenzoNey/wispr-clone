#if WINDOWS
using System.Globalization;
using System.Speech.Recognition;
using System.Text;
using WisprClone.Core;
using WisprClone.Services.Interfaces;

namespace WisprClone.Services.Speech;

/// <summary>
/// Offline speech recognition service using System.Speech (Windows only).
/// </summary>
public class OfflineSpeechRecognitionService : ISpeechRecognitionService
{
    private SpeechRecognitionEngine? _recognizer;
    private readonly StringBuilder _transcriptionBuffer = new();
    private string _lastDisplayedText = string.Empty;
    private string _currentHypothesis = string.Empty;
    private bool _isRecognizing;
    private bool _disposed;
    private Task? _resetTask;

    public event EventHandler<TranscriptionEventArgs>? RecognitionPartial;
    public event EventHandler<TranscriptionEventArgs>? RecognitionCompleted;
    public event EventHandler<RecognitionErrorEventArgs>? RecognitionError;
    public event EventHandler<RecognitionStateChangedEventArgs>? StateChanged;
    public event EventHandler<string>? LanguageChanged;

    public RecognitionState CurrentState { get; private set; } = RecognitionState.Idle;
    public string ProviderName => "Offline (System.Speech)";
    public string CurrentLanguage { get; private set; } = "en-US";
    public bool IsAvailable => true;

    public Task InitializeAsync(string language = "en-US")
    {
        try
        {
            CurrentLanguage = language;
            var culture = new CultureInfo(language);
            _recognizer = new SpeechRecognitionEngine(culture);

            var dictationGrammar = new DictationGrammar { Name = "Dictation Grammar" };
            _recognizer.LoadGrammar(dictationGrammar);
            _recognizer.SetInputToDefaultAudioDevice();

            _recognizer.EndSilenceTimeout = TimeSpan.FromSeconds(1.5);
            _recognizer.EndSilenceTimeoutAmbiguous = TimeSpan.FromSeconds(2.0);

            _recognizer.SpeechRecognized += OnSpeechRecognized;
            _recognizer.SpeechHypothesized += OnSpeechHypothesized;
            _recognizer.RecognizeCompleted += OnRecognizeCompleted;
            _recognizer.SpeechRecognitionRejected += OnSpeechRejected;

            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            RecognitionError?.Invoke(this, new RecognitionErrorEventArgs(
                $"Failed to initialize offline recognition: {ex.Message}", ex));
            throw;
        }
    }

    public async Task StartRecognitionAsync(CancellationToken cancellationToken = default)
    {
        if (_recognizer == null)
            throw new InvalidOperationException("Recognizer not initialized");

        if (_resetTask != null)
        {
            await _resetTask;
            _resetTask = null;
        }

        _transcriptionBuffer.Clear();
        _lastDisplayedText = string.Empty;
        _currentHypothesis = string.Empty;
        _isRecognizing = true;

        UpdateState(RecognitionState.Listening);
        _recognizer.RecognizeAsync(RecognizeMode.Multiple);
    }

    public Task<string> StopRecognitionAsync()
    {
        if (_recognizer == null || !_isRecognizing)
            return Task.FromResult(string.Empty);

        _recognizer.RecognizeAsyncCancel();
        _isRecognizing = false;

        var finalText = _lastDisplayedText.Trim();
        RecognitionCompleted?.Invoke(this, new TranscriptionEventArgs(finalText, true));
        UpdateState(RecognitionState.Idle);

        _resetTask = Task.Run(() =>
        {
            try
            {
                _recognizer?.SetInputToNull();
                _recognizer?.SetInputToDefaultAudioDevice();
            }
            catch { }
        });

        return Task.FromResult(finalText);
    }

    private void OnSpeechHypothesized(object? sender, SpeechHypothesizedEventArgs e)
    {
        _currentHypothesis = e.Result.Text;
        var currentText = _transcriptionBuffer.ToString() + _currentHypothesis;

        if (currentText.Length >= _lastDisplayedText.Length)
        {
            _lastDisplayedText = currentText;
        }

        // Interim hypothesis - show in UI but don't copy to clipboard
        RecognitionPartial?.Invoke(this, new TranscriptionEventArgs(_lastDisplayedText, false, false));
    }

    private void OnSpeechRecognized(object? sender, SpeechRecognizedEventArgs e)
    {
        if (e.Result.Confidence > Constants.MinConfidenceThreshold)
        {
            _transcriptionBuffer.Append(e.Result.Text + " ");
            _currentHypothesis = string.Empty;

            var currentText = _transcriptionBuffer.ToString();
            _lastDisplayedText = currentText;

            // Finalized segment - copy to clipboard
            RecognitionPartial?.Invoke(this, new TranscriptionEventArgs(currentText, false, true));
        }
    }

    private void OnSpeechRejected(object? sender, SpeechRecognitionRejectedEventArgs e) { }

    private void OnRecognizeCompleted(object? sender, RecognizeCompletedEventArgs e)
    {
        if (e.Error != null)
        {
            RecognitionError?.Invoke(this, new RecognitionErrorEventArgs(e.Error.Message, e.Error));
            UpdateState(RecognitionState.Error);
        }
    }

    private void UpdateState(RecognitionState newState)
    {
        var oldState = CurrentState;
        CurrentState = newState;
        StateChanged?.Invoke(this, new RecognitionStateChangedEventArgs(oldState, newState));
    }

    public void Dispose()
    {
        if (_disposed) return;

        if (_recognizer != null)
        {
            if (_isRecognizing)
                _recognizer.RecognizeAsyncStop();

            _recognizer.SpeechRecognized -= OnSpeechRecognized;
            _recognizer.SpeechHypothesized -= OnSpeechHypothesized;
            _recognizer.RecognizeCompleted -= OnRecognizeCompleted;
            _recognizer.SpeechRecognitionRejected -= OnSpeechRejected;
            _recognizer.Dispose();
            _recognizer = null;
        }

        _disposed = true;
        GC.SuppressFinalize(this);
    }

    ~OfflineSpeechRecognitionService() => Dispose();
}
#endif
