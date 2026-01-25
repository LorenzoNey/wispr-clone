using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using WisprClone.Core;
using WisprClone.Services.Interfaces;

namespace WisprClone.Services.Tts;

/// <summary>
/// OpenAI TTS API implementation using HTTP streaming and NAudio for playback.
/// </summary>
public class OpenAITextToSpeechService : ITextToSpeechService
{
    private readonly HttpClient _httpClient;
    private string _apiKey = string.Empty;
    private string _model = "tts-1";
    private string _voice = "alloy";
    private string _currentText = string.Empty;
    private double _rate = 1.0;
    private double _volume = 1.0;
    private bool _disposed;
    private bool _isPaused;
    private CancellationTokenSource? _speakCts;

#if WINDOWS
    private NAudio.Wave.WaveOutEvent? _waveOut;
    private NAudio.Wave.Mp3FileReader? _mp3Reader;
#endif

    public event EventHandler<SpeechSynthesisEventArgs>? SpeakStarted;
    public event EventHandler<SpeechSynthesisEventArgs>? SpeakCompleted;
    public event EventHandler<SpeechProgressEventArgs>? SpeakProgress;
    public event EventHandler<WordBoundaryEventArgs>? WordBoundary;
    public event EventHandler<SpeechSynthesisErrorEventArgs>? SpeakError;
    public event EventHandler<SynthesisStateChangedEventArgs>? StateChanged;

    public SynthesisState CurrentState { get; private set; } = SynthesisState.Idle;
    public string ProviderName => "OpenAI TTS";
    public string CurrentVoice { get; private set; } = "alloy";
    public bool IsAvailable => !string.IsNullOrEmpty(_apiKey);
#if WINDOWS
    public bool SupportsPause => true; // NAudio supports pause on Windows
#else
    public bool SupportsPause => false;
#endif

    public OpenAITextToSpeechService()
    {
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri("https://api.openai.com/v1/"),
            Timeout = TimeSpan.FromMinutes(2)
        };
    }

    /// <summary>
    /// Configures the OpenAI service with API credentials.
    /// </summary>
    public void Configure(string apiKey, string model = "tts-1", string voice = "alloy")
    {
        _apiKey = apiKey;
        _model = model;
        _voice = voice;
        CurrentVoice = voice;

        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", apiKey);
    }

    public Task InitializeAsync(string language = "en-US", string? voice = null)
    {
        if (!string.IsNullOrEmpty(voice))
        {
            _voice = voice;
            CurrentVoice = voice;
        }

        return Task.CompletedTask;
    }

    public async Task SpeakAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_apiKey))
            throw new InvalidOperationException("OpenAI TTS requires API key to be configured.");

        if (string.IsNullOrWhiteSpace(text))
            return;

        // Stop any ongoing speech
        await StopAsync();

        _currentText = text;
        _speakCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        try
        {
            UpdateState(SynthesisState.Speaking);
            SpeakStarted?.Invoke(this, new SpeechSynthesisEventArgs(_currentText));

            // Request audio from OpenAI
            var requestBody = new
            {
                model = _model,
                input = text,
                voice = _voice,
                speed = _rate
            };

            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync("audio/speech", content, _speakCts.Token);
            response.EnsureSuccessStatusCode();

            var audioData = await response.Content.ReadAsByteArrayAsync(_speakCts.Token);

            // Play audio
            await PlayAudioAsync(audioData, _speakCts.Token);

            if (!_speakCts.Token.IsCancellationRequested)
            {
                SpeakCompleted?.Invoke(this, new SpeechSynthesisEventArgs(_currentText));
                UpdateState(SynthesisState.Idle);
            }
        }
        catch (OperationCanceledException)
        {
            UpdateState(SynthesisState.Idle);
        }
        catch (HttpRequestException ex)
        {
            SpeakError?.Invoke(this, new SpeechSynthesisErrorEventArgs(
                $"OpenAI TTS API error: {ex.Message}", ex));
            UpdateState(SynthesisState.Error);
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

    private async Task PlayAudioAsync(byte[] audioData, CancellationToken cancellationToken)
    {
#if WINDOWS
        // Use NAudio for Windows playback
        var tempFile = Path.GetTempFileName() + ".mp3";
        try
        {
            await File.WriteAllBytesAsync(tempFile, audioData, cancellationToken);

            _mp3Reader = new NAudio.Wave.Mp3FileReader(tempFile);
            _waveOut = new NAudio.Wave.WaveOutEvent();

            // Apply volume using VolumeWaveProvider16
            var volumeProvider = new NAudio.Wave.VolumeWaveProvider16(_mp3Reader)
            {
                Volume = (float)_volume
            };

            _waveOut.Init(volumeProvider);

            var tcs = new TaskCompletionSource<bool>();

            _waveOut.PlaybackStopped += (s, e) =>
            {
                tcs.TrySetResult(true);
            };

            var totalTime = _mp3Reader.TotalTime;
            _waveOut.Play();

            // Estimate word boundaries based on audio duration
            _ = Task.Run(async () =>
            {
                await SimulateWordBoundariesAsync(totalTime, cancellationToken);
            }, cancellationToken);

            using var registration = cancellationToken.Register(() =>
            {
                _waveOut?.Stop();
                tcs.TrySetCanceled();
            });

            await tcs.Task;
        }
        finally
        {
            CleanupAudio();
            try { File.Delete(tempFile); } catch { }
        }
#else
        // For non-Windows, use a system command or show error
        SpeakError?.Invoke(this, new SpeechSynthesisErrorEventArgs(
            "OpenAI TTS playback not supported on this platform. Consider using Azure TTS.", null));
        await Task.CompletedTask;
#endif
    }

    private async Task SimulateWordBoundariesAsync(TimeSpan totalDuration, CancellationToken cancellationToken)
    {
        // Split text into words and estimate timing
        var words = _currentText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return;

        var timePerWord = totalDuration.TotalMilliseconds / words.Length;
        var position = 0;

        for (int i = 0; i < words.Length && !cancellationToken.IsCancellationRequested; i++)
        {
            // Wait while paused
            while (_isPaused && !cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(50, cancellationToken);
            }

            if (cancellationToken.IsCancellationRequested) break;

            var word = words[i];
            var wordStart = _currentText.IndexOf(word, position, StringComparison.Ordinal);
            if (wordStart >= 0)
            {
                // Emit word boundary
                WordBoundary?.Invoke(this, new WordBoundaryEventArgs(
                    wordStart,
                    word.Length,
                    word,
                    TimeSpan.FromMilliseconds(i * timePerWord)));

                // Emit progress
                double progress = (double)(wordStart + word.Length) / _currentText.Length * 100;
                SpeakProgress?.Invoke(this, new SpeechProgressEventArgs(progress, wordStart, _currentText.Length));

                position = wordStart + word.Length;
            }

            await Task.Delay((int)timePerWord, cancellationToken);
        }
    }

    private void CleanupAudio()
    {
#if WINDOWS
        _waveOut?.Stop();
        _waveOut?.Dispose();
        _waveOut = null;

        _mp3Reader?.Dispose();
        _mp3Reader = null;
#endif
    }

    public Task StopAsync()
    {
        _isPaused = false;
        _speakCts?.Cancel();
        CleanupAudio();
        UpdateState(SynthesisState.Idle);
        return Task.CompletedTask;
    }

    public Task PauseAsync()
    {
#if WINDOWS
        _isPaused = true;
        _waveOut?.Pause();
        UpdateState(SynthesisState.Paused);
#endif
        return Task.CompletedTask;
    }

    public Task ResumeAsync()
    {
#if WINDOWS
        _isPaused = false;
        _waveOut?.Play();
        UpdateState(SynthesisState.Speaking);
#endif
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<VoiceInfo>> GetAvailableVoicesAsync()
    {
        var voices = new List<VoiceInfo>
        {
            new() { Id = "alloy", Name = "Alloy", Language = "en", Gender = "Neutral", Description = "Balanced, versatile voice" },
            new() { Id = "echo", Name = "Echo", Language = "en", Gender = "Male", Description = "Warm male voice" },
            new() { Id = "fable", Name = "Fable", Language = "en", Gender = "Neutral", Description = "British-accented voice" },
            new() { Id = "onyx", Name = "Onyx", Language = "en", Gender = "Male", Description = "Deep male voice" },
            new() { Id = "nova", Name = "Nova", Language = "en", Gender = "Female", Description = "Energetic female voice" },
            new() { Id = "shimmer", Name = "Shimmer", Language = "en", Gender = "Female", Description = "Soft female voice" },
        };

        return Task.FromResult<IReadOnlyList<VoiceInfo>>(voices);
    }

    public void SetRate(double rate)
    {
        _rate = Math.Clamp(rate, 0.25, 4.0); // OpenAI supports 0.25 to 4.0
    }

    public void SetVolume(double volume)
    {
        _volume = Math.Clamp(volume, 0.0, 1.0);
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
        CleanupAudio();
        _httpClient.Dispose();

        _disposed = true;
        GC.SuppressFinalize(this);
    }

    ~OpenAITextToSpeechService() => Dispose();
}
