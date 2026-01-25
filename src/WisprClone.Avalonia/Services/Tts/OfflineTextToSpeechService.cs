#if WINDOWS
using System.Speech.Synthesis;
using WisprClone.Core;
using WisprClone.Services.Interfaces;

namespace WisprClone.Services.Tts;

/// <summary>
/// Offline text-to-speech service using System.Speech.Synthesis (Windows only).
/// </summary>
public class OfflineTextToSpeechService : ITextToSpeechService
{
    private SpeechSynthesizer? _synthesizer;
    private string _currentText = string.Empty;
    private double _rate = 1.0;
    private double _volume = 1.0;
    private bool _disposed;
    private CancellationTokenSource? _speakCts;

    public event EventHandler<SpeechSynthesisEventArgs>? SpeakStarted;
    public event EventHandler<SpeechSynthesisEventArgs>? SpeakCompleted;
    public event EventHandler<SpeechProgressEventArgs>? SpeakProgress;
    public event EventHandler<WordBoundaryEventArgs>? WordBoundary;
    public event EventHandler<SpeechSynthesisErrorEventArgs>? SpeakError;
    public event EventHandler<SynthesisStateChangedEventArgs>? StateChanged;

    public SynthesisState CurrentState { get; private set; } = SynthesisState.Idle;
    public string ProviderName => "Offline (System.Speech)";
    public string CurrentVoice { get; private set; } = string.Empty;
    public bool IsAvailable => true;
    public bool SupportsPause => true;

    public Task InitializeAsync(string language = "en-US", string? voice = null)
    {
        try
        {
            _synthesizer = new SpeechSynthesizer();
            _synthesizer.SetOutputToDefaultAudioDevice();

            // Wire up events
            _synthesizer.SpeakStarted += OnSpeakStarted;
            _synthesizer.SpeakCompleted += OnSpeakCompleted;
            _synthesizer.SpeakProgress += OnSpeakProgress;
            _synthesizer.StateChanged += OnSynthesizerStateChanged;

            // Set voice if specified, otherwise try to match language
            if (!string.IsNullOrEmpty(voice))
            {
                try
                {
                    _synthesizer.SelectVoice(voice);
                    CurrentVoice = voice;
                }
                catch
                {
                    // Fallback to default voice
                    CurrentVoice = _synthesizer.Voice?.Name ?? "Default";
                }
            }
            else
            {
                // Try to select a voice matching the language
                var voices = _synthesizer.GetInstalledVoices();
                var matchingVoice = voices.FirstOrDefault(v =>
                    v.VoiceInfo.Culture.Name.StartsWith(language.Split('-')[0], StringComparison.OrdinalIgnoreCase));

                if (matchingVoice != null)
                {
                    _synthesizer.SelectVoice(matchingVoice.VoiceInfo.Name);
                    CurrentVoice = matchingVoice.VoiceInfo.Name;
                }
                else
                {
                    CurrentVoice = _synthesizer.Voice?.Name ?? "Default";
                }
            }

            // Apply initial rate and volume
            ApplyRate();
            ApplyVolume();

            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            SpeakError?.Invoke(this, new SpeechSynthesisErrorEventArgs(
                $"Failed to initialize offline TTS: {ex.Message}", ex));
            throw;
        }
    }

    public async Task SpeakAsync(string text, CancellationToken cancellationToken = default)
    {
        if (_synthesizer == null)
            throw new InvalidOperationException("TTS not initialized");

        if (string.IsNullOrWhiteSpace(text))
            return;

        // Stop any ongoing speech
        await StopAsync();

        _currentText = text;
        _speakCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        try
        {
            UpdateState(SynthesisState.Speaking);

            // Use SpeakAsync for non-blocking speech
            var prompt = _synthesizer.SpeakAsync(text);

            // Wait for completion or cancellation
            var tcs = new TaskCompletionSource<bool>();

            void completedHandler(object? s, System.Speech.Synthesis.SpeakCompletedEventArgs e)
            {
                _synthesizer.SpeakCompleted -= completedHandler;
                tcs.TrySetResult(true);
            }

            _synthesizer.SpeakCompleted += completedHandler;

            using var registration = _speakCts.Token.Register(() =>
            {
                _synthesizer?.SpeakAsyncCancelAll();
                tcs.TrySetCanceled();
            });

            await tcs.Task;
        }
        catch (OperationCanceledException)
        {
            // Cancelled - expected
        }
        catch (Exception ex)
        {
            SpeakError?.Invoke(this, new SpeechSynthesisErrorEventArgs(ex.Message, ex));
            UpdateState(SynthesisState.Error);
        }
        finally
        {
            _speakCts?.Dispose();
            _speakCts = null;
        }
    }

    public Task StopAsync()
    {
        if (_synthesizer == null)
            return Task.CompletedTask;

        _speakCts?.Cancel();
        _synthesizer.SpeakAsyncCancelAll();
        UpdateState(SynthesisState.Idle);

        return Task.CompletedTask;
    }

    public Task PauseAsync()
    {
        if (_synthesizer == null || CurrentState != SynthesisState.Speaking)
            return Task.CompletedTask;

        _synthesizer.Pause();
        UpdateState(SynthesisState.Paused);

        return Task.CompletedTask;
    }

    public Task ResumeAsync()
    {
        if (_synthesizer == null || CurrentState != SynthesisState.Paused)
            return Task.CompletedTask;

        _synthesizer.Resume();
        UpdateState(SynthesisState.Speaking);

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Core.VoiceInfo>> GetAvailableVoicesAsync()
    {
        var voices = new List<Core.VoiceInfo>();

        if (_synthesizer != null)
        {
            foreach (var installed in _synthesizer.GetInstalledVoices())
            {
                var info = installed.VoiceInfo;
                voices.Add(new Core.VoiceInfo
                {
                    Id = info.Name,
                    Name = info.Name,
                    Language = info.Culture.Name,
                    Gender = info.Gender.ToString(),
                    Description = info.Description
                });
            }
        }

        return Task.FromResult<IReadOnlyList<Core.VoiceInfo>>(voices);
    }

    public void SetRate(double rate)
    {
        _rate = Math.Clamp(rate, 0.5, 2.0);
        ApplyRate();
    }

    public void SetVolume(double volume)
    {
        _volume = Math.Clamp(volume, 0.0, 1.0);
        ApplyVolume();
    }

    private void ApplyRate()
    {
        if (_synthesizer == null) return;

        // System.Speech.Synthesis rate is -10 to 10
        // Convert from 0.5-2.0 scale to -10 to 10 scale
        // 0.5 -> -10, 1.0 -> 0, 2.0 -> 10
        int systemRate = (int)((_rate - 1.0) * 10);
        _synthesizer.Rate = Math.Clamp(systemRate, -10, 10);
    }

    private void ApplyVolume()
    {
        if (_synthesizer == null) return;

        // System.Speech.Synthesis volume is 0-100
        _synthesizer.Volume = (int)(_volume * 100);
    }

    private void OnSpeakStarted(object? sender, System.Speech.Synthesis.SpeakStartedEventArgs e)
    {
        SpeakStarted?.Invoke(this, new SpeechSynthesisEventArgs(_currentText));
    }

    private void OnSpeakCompleted(object? sender, System.Speech.Synthesis.SpeakCompletedEventArgs e)
    {
        if (e.Cancelled)
        {
            UpdateState(SynthesisState.Idle);
        }
        else if (e.Error != null)
        {
            SpeakError?.Invoke(this, new SpeechSynthesisErrorEventArgs(e.Error.Message, e.Error));
            UpdateState(SynthesisState.Error);
        }
        else
        {
            SpeakCompleted?.Invoke(this, new SpeechSynthesisEventArgs(_currentText));
            UpdateState(SynthesisState.Idle);
        }
    }

    private void OnSpeakProgress(object? sender, System.Speech.Synthesis.SpeakProgressEventArgs e)
    {
        // Emit progress event
        double progress = _currentText.Length > 0
            ? (double)(e.CharacterPosition + e.CharacterCount) / _currentText.Length * 100
            : 0;
        SpeakProgress?.Invoke(this, new SpeechProgressEventArgs(progress, e.CharacterPosition, _currentText.Length));

        // Emit word boundary event
        WordBoundary?.Invoke(this, new WordBoundaryEventArgs(
            e.CharacterPosition,
            e.CharacterCount,
            e.Text,
            e.AudioPosition));
    }

    private void OnSynthesizerStateChanged(object? sender, System.Speech.Synthesis.StateChangedEventArgs e)
    {
        // This is for the synthesizer's internal state, we manage our own state
    }

    private void UpdateState(SynthesisState newState)
    {
        var oldState = CurrentState;
        CurrentState = newState;
        StateChanged?.Invoke(this, new SynthesisStateChangedEventArgs(oldState, newState));
    }

    public void Dispose()
    {
        if (_disposed) return;

        _speakCts?.Cancel();
        _speakCts?.Dispose();

        if (_synthesizer != null)
        {
            _synthesizer.SpeakAsyncCancelAll();
            _synthesizer.SpeakStarted -= OnSpeakStarted;
            _synthesizer.SpeakCompleted -= OnSpeakCompleted;
            _synthesizer.SpeakProgress -= OnSpeakProgress;
            _synthesizer.StateChanged -= OnSynthesizerStateChanged;
            _synthesizer.Dispose();
            _synthesizer = null;
        }

        _disposed = true;
        GC.SuppressFinalize(this);
    }

    ~OfflineTextToSpeechService() => Dispose();
}
#endif
