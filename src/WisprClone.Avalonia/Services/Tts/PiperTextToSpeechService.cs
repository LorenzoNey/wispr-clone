using System.Diagnostics;
using System.IO;
using WisprClone.Core;
using WisprClone.Services.Interfaces;

namespace WisprClone.Services.Tts;

/// <summary>
/// Piper TTS implementation using the local piper executable.
/// Provides high-quality neural text-to-speech locally.
/// </summary>
public class PiperTextToSpeechService : ITextToSpeechService
{
    private readonly ISettingsService _settingsService;
    private readonly ILoggingService _loggingService;
    private string _currentText = string.Empty;
    private double _rate = 1.0;
    private double _volume = 1.0;
    private bool _disposed;
    private bool _isPaused;
    private CancellationTokenSource? _speakCts;

#if WINDOWS
    private NAudio.Wave.WaveOutEvent? _waveOut;
    private NAudio.Wave.RawSourceWaveStream? _rawStream;
#endif

    public event EventHandler<SpeechSynthesisEventArgs>? SpeakStarted;
    public event EventHandler<SpeechSynthesisEventArgs>? SpeakCompleted;
    public event EventHandler<SpeechProgressEventArgs>? SpeakProgress;
    public event EventHandler<WordBoundaryEventArgs>? WordBoundary;
    public event EventHandler<SpeechSynthesisErrorEventArgs>? SpeakError;
    public event EventHandler<SynthesisStateChangedEventArgs>? StateChanged;

    public SynthesisState CurrentState { get; private set; } = SynthesisState.Idle;
    public string ProviderName => "Piper TTS";
    public string CurrentVoice { get; private set; } = "en_US-amy-medium";
#if WINDOWS
    public bool IsAvailable => File.Exists(GetExePath());
    public bool SupportsPause => true;
#else
    public bool IsAvailable => false;
    public bool SupportsPause => false;
#endif

    public PiperTextToSpeechService(ISettingsService settingsService, ILoggingService loggingService)
    {
        _settingsService = settingsService;
        _loggingService = loggingService;
    }

    private string GetExePath()
    {
        return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "piper", "piper.exe");
    }

    private string GetVoicesDirectory()
    {
        return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "piper", "voices");
    }

    private string GetVoicePath()
    {
        var settings = _settingsService.Current;
        var voicePath = settings.PiperVoicePath;

        // If it's a relative path, make it absolute
        if (!Path.IsPathRooted(voicePath))
        {
            voicePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "piper", voicePath);
        }

        return voicePath;
    }

    public Task InitializeAsync(string language = "en-US", string? voice = null)
    {
        if (!string.IsNullOrEmpty(voice))
        {
            CurrentVoice = voice;
        }
        else
        {
            // Extract voice name from path
            var voicePath = GetVoicePath();
            CurrentVoice = Path.GetFileNameWithoutExtension(voicePath);
        }

        return Task.CompletedTask;
    }

    public async Task SpeakAsync(string text, CancellationToken cancellationToken = default)
    {
#if WINDOWS
        if (!IsAvailable)
            throw new InvalidOperationException("piper.exe not found. Extract to: app/piper/");

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

            var voicePath = GetVoicePath();
            if (!File.Exists(voicePath))
            {
                throw new FileNotFoundException($"Voice file not found: {voicePath}");
            }

            Log($"Starting Piper TTS with voice: {voicePath}");

            // Determine sample rate based on voice quality
            // Low quality: 16000Hz, Medium/High: 22050Hz
            var sampleRate = voicePath.Contains("-low") ? 16000 : 22050;

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = GetExePath(),
                    Arguments = $"--model \"{voicePath}\" --output-raw",
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetDirectoryName(GetExePath())
                }
            };

            process.Start();

            // Write text to stdin
            await process.StandardInput.WriteAsync(text);
            process.StandardInput.Close();

            // Read PCM data from stdout
            using var memoryStream = new MemoryStream();
            await process.StandardOutput.BaseStream.CopyToAsync(memoryStream, _speakCts.Token);

            // Wait for process to complete
            await process.WaitForExitAsync(_speakCts.Token);

            if (process.ExitCode != 0)
            {
                var stderr = await process.StandardError.ReadToEndAsync();
                Log($"Piper error: {stderr}");
                throw new Exception($"Piper TTS failed: {stderr}");
            }

            if (memoryStream.Length == 0)
            {
                Log("Piper produced no audio output");
                return;
            }

            // Play the audio
            memoryStream.Position = 0;
            await PlayPcmAudioAsync(memoryStream, sampleRate, _speakCts.Token);

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
#else
        SpeakError?.Invoke(this, new SpeechSynthesisErrorEventArgs(
            "Piper TTS is not supported on this platform.", null));
        await Task.CompletedTask;
#endif
    }

#if WINDOWS
    private async Task PlayPcmAudioAsync(MemoryStream pcmData, int sampleRate, CancellationToken cancellationToken)
    {
        try
        {
            // PCM format: 16-bit signed, mono
            var format = new NAudio.Wave.WaveFormat(sampleRate, 16, 1);
            _rawStream = new NAudio.Wave.RawSourceWaveStream(pcmData, format);
            _waveOut = new NAudio.Wave.WaveOutEvent();

            // Apply volume
            var volumeProvider = new NAudio.Wave.VolumeWaveProvider16(_rawStream)
            {
                Volume = (float)_volume
            };

            _waveOut.Init(volumeProvider);

            var tcs = new TaskCompletionSource<bool>();

            _waveOut.PlaybackStopped += (s, e) =>
            {
                tcs.TrySetResult(true);
            };

            // Calculate total duration for word boundary simulation
            var bytesPerSecond = sampleRate * 2; // 16-bit = 2 bytes per sample
            var totalDuration = TimeSpan.FromSeconds((double)pcmData.Length / bytesPerSecond);

            _waveOut.Play();

            // Simulate word boundaries based on estimated duration
            _ = Task.Run(async () =>
            {
                await SimulateWordBoundariesAsync(totalDuration, cancellationToken);
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
        }
    }

    private async Task SimulateWordBoundariesAsync(TimeSpan totalDuration, CancellationToken cancellationToken)
    {
        var words = _currentText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return;

        var timePerWord = totalDuration.TotalMilliseconds / words.Length;
        var position = 0;

        // Emit first word boundary immediately (no delay for first word)
        if (words.Length > 0)
        {
            var firstWord = words[0];
            var firstWordStart = _currentText.IndexOf(firstWord, 0, StringComparison.Ordinal);
            if (firstWordStart >= 0)
            {
                WordBoundary?.Invoke(this, new WordBoundaryEventArgs(
                    firstWordStart,
                    firstWord.Length,
                    firstWord,
                    TimeSpan.Zero));
                SpeakProgress?.Invoke(this, new SpeechProgressEventArgs(0, firstWordStart, _currentText.Length));
                position = firstWordStart + firstWord.Length;
            }
        }

        // Process remaining words with timing
        for (int i = 1; i < words.Length && !cancellationToken.IsCancellationRequested; i++)
        {
            // Wait for the previous word's duration
            await Task.Delay((int)timePerWord, cancellationToken);

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
        }
    }

    private void CleanupAudio()
    {
        _waveOut?.Stop();
        _waveOut?.Dispose();
        _waveOut = null;

        _rawStream?.Dispose();
        _rawStream = null;
    }
#endif

    public Task StopAsync()
    {
        _isPaused = false;
        _speakCts?.Cancel();
#if WINDOWS
        CleanupAudio();
#endif
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
        var voices = new List<VoiceInfo>();

        try
        {
            var voicesDir = GetVoicesDirectory();
            if (Directory.Exists(voicesDir))
            {
                var onnxFiles = Directory.GetFiles(voicesDir, "*.onnx", SearchOption.AllDirectories);
                foreach (var file in onnxFiles)
                {
                    var fileName = Path.GetFileNameWithoutExtension(file);
                    var relativePath = Path.GetRelativePath(
                        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "piper"),
                        file);

                    // Parse voice name: en_US-amy-medium -> Language: en_US, Name: amy, Quality: medium
                    var parts = fileName.Split('-');
                    var language = parts.Length > 0 ? parts[0].Replace("_", "-") : "en-US";
                    var name = parts.Length > 1 ? parts[1] : fileName;
                    var quality = parts.Length > 2 ? parts[2] : "medium";

                    voices.Add(new VoiceInfo
                    {
                        Id = relativePath,
                        Name = $"{char.ToUpper(name[0])}{name[1..]} ({quality})",
                        Language = language,
                        Gender = "Neutral",
                        Description = $"Piper voice: {fileName}"
                    });
                }
            }
        }
        catch (Exception ex)
        {
            Log($"Failed to enumerate voices: {ex.Message}");
        }

        // Return default voice if no voices found
        if (voices.Count == 0)
        {
            voices.Add(new VoiceInfo
            {
                Id = "voices/en_US-amy-medium.onnx",
                Name = "Amy (medium)",
                Language = "en-US",
                Gender = "Female",
                Description = "Default Piper voice"
            });
        }

        return Task.FromResult<IReadOnlyList<VoiceInfo>>(voices);
    }

    public void SetRate(double rate)
    {
        _rate = Math.Clamp(rate, 0.5, 2.0);
        // Note: Piper doesn't support rate adjustment natively
        // Would need to use audio processing to change speed
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

    private void Log(string message)
    {
        _loggingService.Log("PiperTTS", message);
    }

    public void Dispose()
    {
        if (_disposed) return;

        _speakCts?.Cancel();
        _speakCts?.Dispose();
#if WINDOWS
        CleanupAudio();
#endif

        _disposed = true;
        GC.SuppressFinalize(this);
    }

    ~PiperTextToSpeechService() => Dispose();
}
