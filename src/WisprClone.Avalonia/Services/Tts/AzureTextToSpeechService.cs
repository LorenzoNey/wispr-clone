using Microsoft.CognitiveServices.Speech;
using WisprClone.Core;
using WisprClone.Services.Interfaces;

namespace WisprClone.Services.Tts;

/// <summary>
/// Azure Cognitive Services text-to-speech implementation.
/// </summary>
public class AzureTextToSpeechService : ITextToSpeechService
{
    private SpeechSynthesizer? _synthesizer;
    private SpeechConfig? _speechConfig;
    private string _currentText = string.Empty;
    private double _rate = 1.0;
    private double _volume = 1.0;
    private bool _disposed;
    private CancellationTokenSource? _speakCts;
    private string _subscriptionKey = string.Empty;
    private string _region = string.Empty;
    private int _ssmlTextOffset; // Offset of actual text within SSML

    public event EventHandler<Core.SpeechSynthesisEventArgs>? SpeakStarted;
    public event EventHandler<Core.SpeechSynthesisEventArgs>? SpeakCompleted;
    public event EventHandler<SpeechProgressEventArgs>? SpeakProgress;
    public event EventHandler<WordBoundaryEventArgs>? WordBoundary;
    public event EventHandler<SpeechSynthesisErrorEventArgs>? SpeakError;
    public event EventHandler<SynthesisStateChangedEventArgs>? StateChanged;

    public SynthesisState CurrentState { get; private set; } = SynthesisState.Idle;
    public string ProviderName => "Azure TTS";
    public string CurrentVoice { get; private set; } = string.Empty;
    public bool IsAvailable => !string.IsNullOrEmpty(_subscriptionKey) && !string.IsNullOrEmpty(_region);
    public bool SupportsPause => false; // Azure TTS doesn't support native pause/resume

    /// <summary>
    /// Configures the Azure service with API credentials.
    /// </summary>
    public void Configure(string subscriptionKey, string region)
    {
        _subscriptionKey = subscriptionKey;
        _region = region;
    }

    public Task InitializeAsync(string language = "en-US", string? voice = null)
    {
        try
        {
            if (string.IsNullOrEmpty(_subscriptionKey) || string.IsNullOrEmpty(_region))
            {
                throw new InvalidOperationException("Azure TTS requires subscription key and region to be configured.");
            }

            _speechConfig = SpeechConfig.FromSubscription(_subscriptionKey, _region);
            _speechConfig.SpeechSynthesisLanguage = language;

            // Set voice if specified
            if (!string.IsNullOrEmpty(voice))
            {
                _speechConfig.SpeechSynthesisVoiceName = voice;
                CurrentVoice = voice;
            }
            else
            {
                // Default to a neural voice for the language
                CurrentVoice = GetDefaultVoiceForLanguage(language);
                _speechConfig.SpeechSynthesisVoiceName = CurrentVoice;
            }

            // Apply rate via SSML (will be done in SpeakAsync)

            _synthesizer = new SpeechSynthesizer(_speechConfig);

            // Wire up events
            _synthesizer.SynthesisStarted += OnSynthesisStarted;
            _synthesizer.SynthesisCompleted += OnSynthesisCompleted;
            _synthesizer.WordBoundary += OnWordBoundary;
            _synthesizer.SynthesisCanceled += OnSynthesisCanceled;

            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            SpeakError?.Invoke(this, new SpeechSynthesisErrorEventArgs(
                $"Failed to initialize Azure TTS: {ex.Message}", ex));
            throw;
        }
    }

    private static string GetDefaultVoiceForLanguage(string language)
    {
        return language switch
        {
            "en-US" => "en-US-JennyNeural",
            "en-GB" => "en-GB-SoniaNeural",
            "de-DE" => "de-DE-KatjaNeural",
            "fr-FR" => "fr-FR-DeniseNeural",
            "es-ES" => "es-ES-ElviraNeural",
            "it-IT" => "it-IT-ElsaNeural",
            "pt-BR" => "pt-BR-FranciscaNeural",
            "zh-CN" => "zh-CN-XiaoxiaoNeural",
            "ja-JP" => "ja-JP-NanamiNeural",
            "ro-RO" => "ro-RO-AlinaNeural",
            _ => "en-US-JennyNeural"
        };
    }

    public async Task SpeakAsync(string text, CancellationToken cancellationToken = default)
    {
        if (_synthesizer == null)
            throw new InvalidOperationException("Azure TTS not initialized");

        if (string.IsNullOrWhiteSpace(text))
            return;

        // Stop any ongoing speech
        await StopAsync();

        _currentText = text;
        _speakCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        try
        {
            UpdateState(SynthesisState.Speaking);

            // Build SSML with rate and volume
            var ssml = BuildSsml(text);

            using var registration = _speakCts.Token.Register(() =>
            {
                _synthesizer?.StopSpeakingAsync();
            });

            var result = await _synthesizer.SpeakSsmlAsync(ssml);

            if (result.Reason == ResultReason.Canceled)
            {
                var cancellation = SpeechSynthesisCancellationDetails.FromResult(result);
                if (cancellation.Reason == CancellationReason.Error)
                {
                    SpeakError?.Invoke(this, new SpeechSynthesisErrorEventArgs(
                        $"Azure TTS error: {cancellation.ErrorDetails}", null));
                    UpdateState(SynthesisState.Error);
                }
                else
                {
                    UpdateState(SynthesisState.Idle);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Cancelled - expected
            UpdateState(SynthesisState.Idle);
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

    private string BuildSsml(string text)
    {
        // Convert rate (0.5-2.0) to percentage string
        var ratePercent = (int)((_rate - 1.0) * 100);
        var rateStr = ratePercent >= 0 ? $"+{ratePercent}%" : $"{ratePercent}%";

        // Convert volume (0.0-1.0) to percentage string (0-100)
        var volumePercent = (int)(_volume * 100);

        // Build the prefix (everything before the actual text)
        var prefix = $@"<speak version='1.0' xmlns='http://www.w3.org/2001/10/synthesis' xml:lang='en-US'>
            <voice name='{CurrentVoice}'>
                <prosody rate='{rateStr}' volume='{volumePercent}%'>";

        // Store the offset where actual text begins in the SSML
        _ssmlTextOffset = prefix.Length;

        var escapedText = System.Security.SecurityElement.Escape(text);

        return $@"{prefix}{escapedText}</prosody>
            </voice>
        </speak>";
    }

    public async Task StopAsync()
    {
        if (_synthesizer == null)
            return;

        _speakCts?.Cancel();
        await _synthesizer.StopSpeakingAsync();
        UpdateState(SynthesisState.Idle);
    }

    public Task PauseAsync()
    {
        // Azure TTS doesn't support pause/resume natively
        // Would need to implement using audio stream buffering
        return Task.CompletedTask;
    }

    public Task ResumeAsync()
    {
        // Azure TTS doesn't support pause/resume natively
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Core.VoiceInfo>> GetAvailableVoicesAsync()
    {
        // Return a list of common Azure neural voices
        var voices = new List<Core.VoiceInfo>
        {
            new() { Id = "en-US-JennyNeural", Name = "Jenny", Language = "en-US", Gender = "Female", Description = "US English female" },
            new() { Id = "en-US-GuyNeural", Name = "Guy", Language = "en-US", Gender = "Male", Description = "US English male" },
            new() { Id = "en-US-AriaNeural", Name = "Aria", Language = "en-US", Gender = "Female", Description = "US English female" },
            new() { Id = "en-GB-SoniaNeural", Name = "Sonia", Language = "en-GB", Gender = "Female", Description = "UK English female" },
            new() { Id = "en-GB-RyanNeural", Name = "Ryan", Language = "en-GB", Gender = "Male", Description = "UK English male" },
            new() { Id = "de-DE-KatjaNeural", Name = "Katja", Language = "de-DE", Gender = "Female", Description = "German female" },
            new() { Id = "fr-FR-DeniseNeural", Name = "Denise", Language = "fr-FR", Gender = "Female", Description = "French female" },
            new() { Id = "es-ES-ElviraNeural", Name = "Elvira", Language = "es-ES", Gender = "Female", Description = "Spanish female" },
            new() { Id = "it-IT-ElsaNeural", Name = "Elsa", Language = "it-IT", Gender = "Female", Description = "Italian female" },
            new() { Id = "ro-RO-AlinaNeural", Name = "Alina", Language = "ro-RO", Gender = "Female", Description = "Romanian female" },
        };

        return Task.FromResult<IReadOnlyList<Core.VoiceInfo>>(voices);
    }

    public void SetRate(double rate)
    {
        _rate = Math.Clamp(rate, 0.5, 2.0);
    }

    public void SetVolume(double volume)
    {
        _volume = Math.Clamp(volume, 0.0, 1.0);
    }

    private void OnSynthesisStarted(object? sender, Microsoft.CognitiveServices.Speech.SpeechSynthesisEventArgs e)
    {
        SpeakStarted?.Invoke(this, new Core.SpeechSynthesisEventArgs(_currentText));
    }

    private void OnSynthesisCompleted(object? sender, Microsoft.CognitiveServices.Speech.SpeechSynthesisEventArgs e)
    {
        if (e.Result.Reason == ResultReason.SynthesizingAudioCompleted)
        {
            SpeakCompleted?.Invoke(this, new Core.SpeechSynthesisEventArgs(_currentText));
            UpdateState(SynthesisState.Idle);
        }
    }

    private void OnWordBoundary(object? sender, Microsoft.CognitiveServices.Speech.SpeechSynthesisWordBoundaryEventArgs e)
    {
        // Adjust text offset to account for SSML prefix
        // The TextOffset from Azure is relative to the SSML, not the original text
        var adjustedOffset = (int)e.TextOffset - _ssmlTextOffset;

        // Skip if the offset is before our actual text (i.e., within SSML tags)
        if (adjustedOffset < 0 || adjustedOffset >= _currentText.Length)
            return;

        // Calculate progress based on adjusted offset
        double progress = _currentText.Length > 0
            ? (double)(adjustedOffset + e.WordLength) / _currentText.Length * 100
            : 0;

        // Delay the word boundary event by 500ms to sync highlighting with audio playback
        // Azure sends word boundaries ahead of actual audio playback
        _ = Task.Run(async () =>
        {
            await Task.Delay(500);

            // Only emit if still speaking (not cancelled)
            if (CurrentState != SynthesisState.Speaking)
                return;

            SpeakProgress?.Invoke(this, new SpeechProgressEventArgs(progress, adjustedOffset, _currentText.Length));

            // Emit word boundary event with adjusted offset
            // AudioOffset is in 100-nanosecond units (ticks)
            WordBoundary?.Invoke(this, new WordBoundaryEventArgs(
                adjustedOffset,
                (int)e.WordLength,
                e.Text,
                TimeSpan.FromTicks((long)e.AudioOffset)));
        });
    }

    private void OnSynthesisCanceled(object? sender, Microsoft.CognitiveServices.Speech.SpeechSynthesisEventArgs e)
    {
        var cancellation = SpeechSynthesisCancellationDetails.FromResult(e.Result);
        if (cancellation.Reason == CancellationReason.Error)
        {
            SpeakError?.Invoke(this, new SpeechSynthesisErrorEventArgs(
                $"Azure TTS cancelled: {cancellation.ErrorDetails}", null));
            UpdateState(SynthesisState.Error);
        }
        else
        {
            UpdateState(SynthesisState.Idle);
        }
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
            _synthesizer.SynthesisStarted -= OnSynthesisStarted;
            _synthesizer.SynthesisCompleted -= OnSynthesisCompleted;
            _synthesizer.WordBoundary -= OnWordBoundary;
            _synthesizer.SynthesisCanceled -= OnSynthesisCanceled;
            _synthesizer.Dispose();
            _synthesizer = null;
        }

        _disposed = true;
        GC.SuppressFinalize(this);
    }

    ~AzureTextToSpeechService() => Dispose();
}
