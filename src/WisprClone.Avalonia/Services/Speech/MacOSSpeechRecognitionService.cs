using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using WisprClone.Core;
using WisprClone.Services.Interfaces;

namespace WisprClone.Services.Speech;

/// <summary>
/// Native macOS speech recognition using Apple's Speech Framework via a Swift helper process.
/// Only available on macOS.
/// </summary>
public class MacOSSpeechRecognitionService : ISpeechRecognitionService
{
    private readonly ILoggingService _loggingService;
    private Process? _helperProcess;
    private readonly StringBuilder _transcriptionBuffer = new();
    private bool _disposed;
    private bool _isInitialized;
    private CancellationTokenSource? _readCts;

    public event EventHandler<TranscriptionEventArgs>? RecognitionPartial;
    public event EventHandler<TranscriptionEventArgs>? RecognitionCompleted;
    public event EventHandler<RecognitionErrorEventArgs>? RecognitionError;
    public event EventHandler<RecognitionStateChangedEventArgs>? StateChanged;
    public event EventHandler<string>? LanguageChanged;

    public RecognitionState CurrentState { get; private set; } = RecognitionState.Idle;
    public string ProviderName => "macOS Native Speech";
    public string CurrentLanguage { get; private set; } = "en-US";

    public bool IsAvailable
    {
        get
        {
            if (!OperatingSystem.IsMacOS())
                return false;

            var helperPath = GetHelperPath();
            return !string.IsNullOrEmpty(helperPath) && File.Exists(helperPath);
        }
    }

    public MacOSSpeechRecognitionService(ILoggingService loggingService)
    {
        _loggingService = loggingService;
    }

    private void Log(string message)
    {
        _loggingService.Log("MacOSSpeech", message);
    }

    private static string? GetHelperPath()
    {
        // The helper should be in the app bundle's Resources directory
        var appPath = AppContext.BaseDirectory;

        // Try several possible locations
        var possiblePaths = new[]
        {
            Path.Combine(appPath, "MacOSSpeechHelper"),
            Path.Combine(appPath, "..", "Resources", "MacOSSpeechHelper"),
            Path.Combine(appPath, "Resources", "MacOSSpeechHelper"),
            // For development
            Path.Combine(appPath, "..", "..", "..", "..", "MacOSSpeechHelper", "MacOSSpeechHelper")
        };

        foreach (var path in possiblePaths)
        {
            var fullPath = Path.GetFullPath(path);
            if (File.Exists(fullPath))
                return fullPath;
        }

        return null;
    }

    public async Task InitializeAsync(string language = "en-US")
    {
        Log($"InitializeAsync called with language: {language}");
        CurrentLanguage = language;

        if (!OperatingSystem.IsMacOS())
        {
            Log("Not running on macOS, service unavailable");
            return;
        }

        var helperPath = GetHelperPath();
        if (string.IsNullOrEmpty(helperPath) || !File.Exists(helperPath))
        {
            Log($"Helper not found at expected paths");
            throw new InvalidOperationException("macOS Speech Helper not found");
        }

        try
        {
            await StartHelperProcess(helperPath);
            _isInitialized = true;
            Log("macOS Speech Recognition Service initialized");
        }
        catch (Exception ex)
        {
            Log($"Failed to initialize: {ex.Message}");
            throw;
        }
    }

    private async Task StartHelperProcess(string helperPath)
    {
        Log($"Starting helper process: {helperPath}");

        _helperProcess = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = helperPath,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            },
            EnableRaisingEvents = true
        };

        _helperProcess.Exited += (s, e) =>
        {
            Log("Helper process exited");
            if (CurrentState == RecognitionState.Listening)
            {
                UpdateState(RecognitionState.Error);
                RecognitionError?.Invoke(this, new RecognitionErrorEventArgs("Speech helper process terminated unexpectedly", null));
            }
        };

        _helperProcess.Start();

        // Start reading output
        _readCts = new CancellationTokenSource();
        _ = ReadOutputAsync(_readCts.Token);

        // Wait for ready message
        var readyTimeout = Task.Delay(5000);
        var tcs = new TaskCompletionSource<bool>();

        EventHandler<RecognitionStateChangedEventArgs>? stateHandler = null;
        EventHandler<RecognitionErrorEventArgs>? errorHandler = null;

        stateHandler = (s, e) =>
        {
            if (e.NewState == RecognitionState.Idle && !_isInitialized)
            {
                StateChanged -= stateHandler;
                RecognitionError -= errorHandler;
                tcs.TrySetResult(true);
            }
        };

        errorHandler = (s, e) =>
        {
            StateChanged -= stateHandler;
            RecognitionError -= errorHandler;
            tcs.TrySetException(new Exception(e.Message));
        };

        StateChanged += stateHandler;
        RecognitionError += errorHandler;

        // Set language
        await SendCommand(new { action = "setLanguage", locale = CurrentLanguage });

        var completed = await Task.WhenAny(tcs.Task, readyTimeout);
        if (completed == readyTimeout)
        {
            StateChanged -= stateHandler;
            RecognitionError -= errorHandler;
            throw new TimeoutException("Helper process did not respond in time");
        }

        await tcs.Task; // Will throw if there was an error
    }

    private async Task ReadOutputAsync(CancellationToken cancellationToken)
    {
        if (_helperProcess?.StandardOutput == null)
            return;

        try
        {
            while (!cancellationToken.IsCancellationRequested && _helperProcess != null && !_helperProcess.HasExited)
            {
                var line = await _helperProcess.StandardOutput.ReadLineAsync(cancellationToken);
                if (line == null)
                    break;

                ProcessMessage(line);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when stopping
        }
        catch (Exception ex)
        {
            Log($"Error reading helper output: {ex.Message}");
        }
    }

    private void ProcessMessage(string json)
    {
        try
        {
            var message = JsonSerializer.Deserialize<HelperMessage>(json);
            if (message == null)
                return;

            Log($"Received message: type={message.Type}, text length={message.Text?.Length ?? 0}");

            switch (message.Type)
            {
                case "ready":
                    UpdateState(RecognitionState.Idle);
                    break;

                case "state":
                    var newState = message.State switch
                    {
                        "listening" => RecognitionState.Listening,
                        "idle" => RecognitionState.Idle,
                        _ => CurrentState
                    };
                    UpdateState(newState);
                    break;

                case "partial":
                    if (message.Text != null)
                    {
                        RecognitionPartial?.Invoke(this, new TranscriptionEventArgs(message.Text, false, false));
                    }
                    break;

                case "final":
                    if (message.Text != null)
                    {
                        _transcriptionBuffer.Clear();
                        _transcriptionBuffer.Append(message.Text);
                        RecognitionPartial?.Invoke(this, new TranscriptionEventArgs(message.Text, false, true));
                    }
                    break;

                case "error":
                    Log($"Helper error: {message.Error}");
                    RecognitionError?.Invoke(this, new RecognitionErrorEventArgs(message.Error ?? "Unknown error", null));
                    break;

                case "languageChanged":
                    if (message.Text != null)
                    {
                        CurrentLanguage = message.Text;
                        LanguageChanged?.Invoke(this, CurrentLanguage);
                    }
                    break;
            }
        }
        catch (JsonException ex)
        {
            Log($"Failed to parse message: {ex.Message}");
        }
    }

    private async Task SendCommand(object command)
    {
        if (_helperProcess?.StandardInput == null)
            return;

        var json = JsonSerializer.Serialize(command);
        Log($"Sending command: {json}");

        await _helperProcess.StandardInput.WriteLineAsync(json);
        await _helperProcess.StandardInput.FlushAsync();
    }

    public async Task StartRecognitionAsync(CancellationToken cancellationToken = default)
    {
        Log("StartRecognitionAsync called");

        if (!_isInitialized || _helperProcess == null || _helperProcess.HasExited)
        {
            throw new InvalidOperationException("Service not initialized");
        }

        _transcriptionBuffer.Clear();
        await SendCommand(new { action = "start" });
    }

    public async Task<string> StopRecognitionAsync()
    {
        Log("StopRecognitionAsync called");

        if (_helperProcess == null || _helperProcess.HasExited)
        {
            return _transcriptionBuffer.ToString();
        }

        UpdateState(RecognitionState.Processing);
        await SendCommand(new { action = "stop" });

        // Wait briefly for final result
        await Task.Delay(200);

        var finalText = _transcriptionBuffer.ToString().Trim();
        Log($"Final text: {finalText}");

        RecognitionCompleted?.Invoke(this, new TranscriptionEventArgs(finalText, true));
        UpdateState(RecognitionState.Idle);

        return finalText;
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

        Log("Disposing MacOSSpeechRecognitionService");

        _readCts?.Cancel();
        _readCts?.Dispose();
        _readCts = null;

        if (_helperProcess != null && !_helperProcess.HasExited)
        {
            try
            {
                // Send quit command
                _helperProcess.StandardInput.WriteLine("{\"action\":\"quit\"}");
                _helperProcess.StandardInput.Flush();

                // Wait briefly for graceful exit
                if (!_helperProcess.WaitForExit(1000))
                {
                    _helperProcess.Kill();
                }
            }
            catch (Exception ex)
            {
                Log($"Error stopping helper: {ex.Message}");
            }
            finally
            {
                _helperProcess.Dispose();
                _helperProcess = null;
            }
        }

        _disposed = true;
        GC.SuppressFinalize(this);
    }

    ~MacOSSpeechRecognitionService()
    {
        Dispose();
    }

    private class HelperMessage
    {
        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("text")]
        public string? Text { get; set; }

        [JsonPropertyName("isFinal")]
        public bool? IsFinal { get; set; }

        [JsonPropertyName("error")]
        public string? Error { get; set; }

        [JsonPropertyName("state")]
        public string? State { get; set; }
    }
}
