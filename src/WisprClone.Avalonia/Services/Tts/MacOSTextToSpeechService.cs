using System.Diagnostics;
using System.Text.RegularExpressions;
using WisprClone.Core;
using WisprClone.Services.Interfaces;

namespace WisprClone.Services.Tts;

/// <summary>
/// Native macOS text-to-speech using the built-in 'say' command.
/// Only available on macOS.
/// </summary>
public class MacOSTextToSpeechService : ITextToSpeechService
{
    private readonly ILoggingService _loggingService;
    private string _currentText = string.Empty;
    private double _rate = 1.0;
    private double _volume = 1.0;
    private bool _disposed;
    private Process? _sayProcess;
    private CancellationTokenSource? _speakCts;

    public event EventHandler<SpeechSynthesisEventArgs>? SpeakStarted;
    public event EventHandler<SpeechSynthesisEventArgs>? SpeakCompleted;
    public event EventHandler<SpeechProgressEventArgs>? SpeakProgress;
    public event EventHandler<WordBoundaryEventArgs>? WordBoundary;
    public event EventHandler<SpeechSynthesisErrorEventArgs>? SpeakError;
    public event EventHandler<SynthesisStateChangedEventArgs>? StateChanged;

    public SynthesisState CurrentState { get; private set; } = SynthesisState.Idle;
    public string ProviderName => "macOS Native TTS";
    public string CurrentVoice { get; private set; } = "Samantha";
    public bool IsAvailable => OperatingSystem.IsMacOS();
    public bool SupportsPause => false; // macOS say command doesn't support pause/resume

    public MacOSTextToSpeechService(ILoggingService loggingService)
    {
        _loggingService = loggingService;
    }

    public Task InitializeAsync(string language = "en-US", string? voice = null)
    {
        if (!string.IsNullOrEmpty(voice))
        {
            CurrentVoice = voice;
        }
        else
        {
            // Set default voice based on language
            CurrentVoice = GetDefaultVoiceForLanguage(language);
        }

        Log($"Initialized with voice: {CurrentVoice}");
        return Task.CompletedTask;
    }

    private string GetDefaultVoiceForLanguage(string language)
    {
        // Map common languages to their default macOS voices
        return language.ToLower() switch
        {
            "en-us" => "Samantha",
            "en-gb" => "Daniel",
            "en-au" => "Karen",
            "de-de" => "Anna",
            "fr-fr" => "Thomas",
            "es-es" => "Monica",
            "it-it" => "Alice",
            "pt-br" => "Luciana",
            "ja-jp" => "Kyoko",
            "zh-cn" => "Ting-Ting",
            "ro-ro" => "Ioana",
            _ => "Samantha" // Default to US English
        };
    }

    public async Task SpeakAsync(string text, CancellationToken cancellationToken = default)
    {
        if (!IsAvailable)
        {
            SpeakError?.Invoke(this, new SpeechSynthesisErrorEventArgs("macOS TTS is only available on macOS"));
            return;
        }

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

            // Calculate rate in words per minute (default is ~175 wpm)
            // Rate 1.0 = 175 wpm, 0.5 = 87 wpm, 2.0 = 350 wpm
            var wordsPerMinute = (int)(175 * _rate);

            // Escape the text for shell
            var escapedText = text.Replace("\"", "\\\"").Replace("$", "\\$");

            // Build the say command
            // Note: Volume is controlled at system level on macOS, we can adjust via osascript if needed
            var arguments = $"-v \"{CurrentVoice}\" -r {wordsPerMinute} \"{escapedText}\"";

            Log($"Running: say {arguments}");

            _sayProcess = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "say",
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                },
                EnableRaisingEvents = true
            };

            var tcs = new TaskCompletionSource<bool>();

            _sayProcess.Exited += (s, e) =>
            {
                tcs.TrySetResult(true);
            };

            _sayProcess.Start();

            // Simulate word boundaries while speaking
            _ = Task.Run(async () =>
            {
                await SimulateWordBoundariesAsync(_speakCts.Token);
            }, _speakCts.Token);

            // Wait for process to complete or cancellation
            using var registration = _speakCts.Token.Register(() =>
            {
                try
                {
                    if (_sayProcess != null && !_sayProcess.HasExited)
                    {
                        _sayProcess.Kill();
                    }
                }
                catch { }
                tcs.TrySetCanceled();
            });

            await tcs.Task;

            if (_sayProcess.ExitCode != 0 && !_speakCts.Token.IsCancellationRequested)
            {
                var stderr = await _sayProcess.StandardError.ReadToEndAsync();
                if (!string.IsNullOrEmpty(stderr))
                {
                    Log($"Say command error: {stderr}");
                    throw new Exception($"macOS TTS failed: {stderr}");
                }
            }

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
            Log($"Error in SpeakAsync: {ex.Message}");
            SpeakError?.Invoke(this, new SpeechSynthesisErrorEventArgs(ex.Message, ex));
            UpdateState(SynthesisState.Error);
        }
        finally
        {
            _sayProcess?.Dispose();
            _sayProcess = null;
            _speakCts?.Dispose();
            _speakCts = null;
        }
    }

    private async Task SimulateWordBoundariesAsync(CancellationToken cancellationToken)
    {
        var words = _currentText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return;

        // Estimate duration based on rate (175 wpm at rate 1.0)
        var wordsPerMinute = 175 * _rate;
        var msPerWord = 60000.0 / wordsPerMinute;
        var position = 0;

        // Emit first word boundary immediately
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
            await Task.Delay((int)msPerWord, cancellationToken);

            if (cancellationToken.IsCancellationRequested) break;

            var word = words[i];
            var wordStart = _currentText.IndexOf(word, position, StringComparison.Ordinal);
            if (wordStart >= 0)
            {
                WordBoundary?.Invoke(this, new WordBoundaryEventArgs(
                    wordStart,
                    word.Length,
                    word,
                    TimeSpan.FromMilliseconds(i * msPerWord)));

                double progress = (double)(wordStart + word.Length) / _currentText.Length * 100;
                SpeakProgress?.Invoke(this, new SpeechProgressEventArgs(progress, wordStart, _currentText.Length));

                position = wordStart + word.Length;
            }
        }
    }

    public Task StopAsync()
    {
        _speakCts?.Cancel();

        if (_sayProcess != null && !_sayProcess.HasExited)
        {
            try
            {
                _sayProcess.Kill();
            }
            catch (Exception ex)
            {
                Log($"Error stopping say process: {ex.Message}");
            }
        }

        UpdateState(SynthesisState.Idle);
        return Task.CompletedTask;
    }

    public Task PauseAsync()
    {
        // macOS say command doesn't support pause
        Log("Pause not supported on macOS native TTS");
        return Task.CompletedTask;
    }

    public Task ResumeAsync()
    {
        // macOS say command doesn't support resume
        Log("Resume not supported on macOS native TTS");
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<VoiceInfo>> GetAvailableVoicesAsync()
    {
        var voices = new List<VoiceInfo>();

        if (!IsAvailable)
            return voices;

        try
        {
            // Run "say -v ?" to get list of available voices
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "say",
                    Arguments = "-v ?",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            // Parse output format: "VoiceName    language    # description"
            // Example: "Samantha             en_US    # Samantha is a premium text-to-speech voice."
            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var voiceRegex = new Regex(@"^(\S+)\s+(\S+)\s+#\s*(.*)$");

            foreach (var line in lines)
            {
                var match = voiceRegex.Match(line.Trim());
                if (match.Success)
                {
                    var name = match.Groups[1].Value;
                    var locale = match.Groups[2].Value.Replace("_", "-");
                    var description = match.Groups[3].Value;

                    voices.Add(new VoiceInfo
                    {
                        Id = name,
                        Name = name,
                        Language = locale,
                        Gender = "Neutral",
                        Description = description
                    });
                }
            }

            Log($"Found {voices.Count} macOS voices");
        }
        catch (Exception ex)
        {
            Log($"Error getting available voices: {ex.Message}");
        }

        // Return some defaults if parsing failed
        if (voices.Count == 0)
        {
            voices.AddRange(new[]
            {
                new VoiceInfo { Id = "Samantha", Name = "Samantha", Language = "en-US", Gender = "Female", Description = "US English" },
                new VoiceInfo { Id = "Daniel", Name = "Daniel", Language = "en-GB", Gender = "Male", Description = "British English" },
                new VoiceInfo { Id = "Karen", Name = "Karen", Language = "en-AU", Gender = "Female", Description = "Australian English" },
                new VoiceInfo { Id = "Alex", Name = "Alex", Language = "en-US", Gender = "Male", Description = "US English" }
            });
        }

        return voices;
    }

    public void SetRate(double rate)
    {
        _rate = Math.Clamp(rate, 0.5, 2.0);
    }

    public void SetVolume(double volume)
    {
        _volume = Math.Clamp(volume, 0.0, 1.0);
        // Note: Volume control via say command is limited
        // Could use osascript to set system volume if needed
    }

    private void UpdateState(SynthesisState newState)
    {
        var oldState = CurrentState;
        CurrentState = newState;
        StateChanged?.Invoke(this, new SynthesisStateChangedEventArgs(oldState, newState));
    }

    private void Log(string message)
    {
        _loggingService.Log("MacOSTTS", message);
    }

    public void Dispose()
    {
        if (_disposed) return;

        _speakCts?.Cancel();
        _speakCts?.Dispose();

        if (_sayProcess != null && !_sayProcess.HasExited)
        {
            try
            {
                _sayProcess.Kill();
            }
            catch { }
        }
        _sayProcess?.Dispose();

        _disposed = true;
        GC.SuppressFinalize(this);
    }

    ~MacOSTextToSpeechService() => Dispose();
}
