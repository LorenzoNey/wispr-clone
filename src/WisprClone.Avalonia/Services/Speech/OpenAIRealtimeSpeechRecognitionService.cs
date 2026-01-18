using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using WisprClone.Core;
using WisprClone.Services.Interfaces;

#if WINDOWS
using NAudio.Wave;
#endif

namespace WisprClone.Services.Speech;

/// <summary>
/// OpenAI Realtime API implementation for true streaming speech recognition.
/// Uses WebSocket connection for real-time transcription with low latency.
/// </summary>
public class OpenAIRealtimeSpeechRecognitionService : ISpeechRecognitionService
{
    private const string RealtimeApiUrl = "wss://api.openai.com/v1/realtime?model=gpt-4o-realtime-preview";
    private const int SampleRate = 24000; // OpenAI Realtime requires 24kHz
    private const int BitsPerSample = 16;
    private const int Channels = 1;
    private const int BufferMs = 100; // Send audio every 100ms

    private string _apiKey = string.Empty;
    private ClientWebSocket? _webSocket;
    private CancellationTokenSource? _cts;
    private StringBuilder _transcriptionBuffer = new();
    private bool _isRecording;
    private bool _disposed;

#if WINDOWS
    private WaveInEvent? _waveIn;
    private readonly object _audioLock = new();
    private List<byte> _pendingAudioData = new();
#endif

    public event EventHandler<TranscriptionEventArgs>? RecognitionPartial;
    public event EventHandler<TranscriptionEventArgs>? RecognitionCompleted;
    public event EventHandler<RecognitionErrorEventArgs>? RecognitionError;
    public event EventHandler<RecognitionStateChangedEventArgs>? StateChanged;
    public event EventHandler<string>? LanguageChanged;

    public RecognitionState CurrentState { get; private set; } = RecognitionState.Idle;
    public string ProviderName => "OpenAI Realtime";
    public string CurrentLanguage { get; private set; } = "en";
    public bool IsAvailable => !string.IsNullOrEmpty(_apiKey);

    private static void Log(string message)
    {
        var logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WisprClone", "wispr_log.txt");
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [OpenAI-RT] {message}";
        try { File.AppendAllText(logPath, line + Environment.NewLine); } catch { }
    }

    /// <summary>
    /// Configures the service with the OpenAI API key.
    /// </summary>
    public void Configure(string apiKey)
    {
        _apiKey = apiKey;
        Log($"Configured with API key length: {apiKey?.Length ?? 0}");
    }

    public Task InitializeAsync(string language = "en-US")
    {
        // Extract language code (e.g., "en-US" -> "en")
        CurrentLanguage = language.Split('-')[0];
        Log($"Initialized with language: {CurrentLanguage}");
        return Task.CompletedTask;
    }

    public async Task StartRecognitionAsync(CancellationToken cancellationToken = default)
    {
        if (!IsAvailable)
        {
            throw new InvalidOperationException("OpenAI API key not configured");
        }

#if WINDOWS
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _transcriptionBuffer.Clear();
        _pendingAudioData.Clear();

        try
        {
            // Connect to OpenAI Realtime API
            await ConnectWebSocketAsync(_cts.Token);

            // Configure the session for transcription
            await SendSessionConfigAsync();

            // Start audio capture
            StartAudioCapture();

            _isRecording = true;
            UpdateState(RecognitionState.Listening);
            RecognitionPartial?.Invoke(this, new TranscriptionEventArgs("Listening...", false, false));

            Log("Started realtime transcription");

            // Start receiving messages
            _ = ReceiveMessagesAsync(_cts.Token);
        }
        catch (Exception ex)
        {
            Log($"Start error: {ex.Message}");
            RecognitionError?.Invoke(this, new RecognitionErrorEventArgs($"Failed to start: {ex.Message}", ex));
            UpdateState(RecognitionState.Error);
            throw;
        }
#else
        throw new PlatformNotSupportedException("OpenAI Realtime is currently only available on Windows.");
#endif
    }

    public async Task<string> StopRecognitionAsync()
    {
#if WINDOWS
        if (!_isRecording)
            return string.Empty;

        _isRecording = false;
        UpdateState(RecognitionState.Processing);

        try
        {
            // Stop audio capture
            StopAudioCapture();

            // Send any remaining audio
            await FlushAudioBufferAsync();

            // Commit the audio buffer to trigger final transcription
            await SendEventAsync(new { type = "input_audio_buffer.commit" });

            // Wait a moment for final transcription
            await Task.Delay(500);

            // Close WebSocket gracefully
            if (_webSocket?.State == WebSocketState.Open)
            {
                await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Done", CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            Log($"Stop error: {ex.Message}");
        }
        finally
        {
            _cts?.Cancel();
            _webSocket?.Dispose();
            _webSocket = null;
        }

        var finalText = _transcriptionBuffer.ToString().Trim();
        Log($"Final transcription received (length: {finalText.Length})");

        RecognitionCompleted?.Invoke(this, new TranscriptionEventArgs(finalText, true, true));
        UpdateState(RecognitionState.Idle);

        return finalText;
#else
        return string.Empty;
#endif
    }

#if WINDOWS
    private async Task ConnectWebSocketAsync(CancellationToken cancellationToken)
    {
        _webSocket = new ClientWebSocket();
        _webSocket.Options.SetRequestHeader("Authorization", $"Bearer {_apiKey}");
        _webSocket.Options.SetRequestHeader("OpenAI-Beta", "realtime=v1");

        Log($"Connecting to {RealtimeApiUrl}");
        await _webSocket.ConnectAsync(new Uri(RealtimeApiUrl), cancellationToken);
        Log("WebSocket connected");
    }

    private async Task SendSessionConfigAsync()
    {
        var sessionConfig = new
        {
            type = "session.update",
            session = new
            {
                modalities = new[] { "text", "audio" },
                input_audio_format = "pcm16",
                input_audio_transcription = new
                {
                    model = "whisper-1"
                },
                turn_detection = new
                {
                    type = "server_vad",
                    threshold = 0.5,
                    prefix_padding_ms = 300,
                    silence_duration_ms = 500
                }
            }
        };

        await SendEventAsync(sessionConfig);
        Log("Session configured for transcription");
    }

    private void StartAudioCapture()
    {
        _waveIn = new WaveInEvent
        {
            WaveFormat = new WaveFormat(SampleRate, BitsPerSample, Channels),
            BufferMilliseconds = BufferMs
        };

        _waveIn.DataAvailable += OnAudioDataAvailable;
        _waveIn.RecordingStopped += OnRecordingStopped;
        _waveIn.StartRecording();

        Log($"Audio capture started: {SampleRate}Hz, {BitsPerSample}-bit, {Channels} channel(s)");
    }

    private void StopAudioCapture()
    {
        if (_waveIn != null)
        {
            _waveIn.StopRecording();
            _waveIn.DataAvailable -= OnAudioDataAvailable;
            _waveIn.RecordingStopped -= OnRecordingStopped;
            _waveIn.Dispose();
            _waveIn = null;
        }
    }

    private async void OnAudioDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (!_isRecording || _webSocket?.State != WebSocketState.Open)
            return;

        try
        {
            // Convert audio to base64
            var base64Audio = Convert.ToBase64String(e.Buffer, 0, e.BytesRecorded);

            // Send audio chunk
            var audioEvent = new
            {
                type = "input_audio_buffer.append",
                audio = base64Audio
            };

            await SendEventAsync(audioEvent);
        }
        catch (Exception ex)
        {
            Log($"Audio send error: {ex.Message}");
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception != null)
        {
            Log($"Recording stopped with error: {e.Exception.Message}");
            RecognitionError?.Invoke(this, new RecognitionErrorEventArgs(e.Exception.Message, e.Exception));
        }
    }

    private async Task FlushAudioBufferAsync()
    {
        byte[] audioData;
        lock (_audioLock)
        {
            if (_pendingAudioData.Count == 0)
                return;
            audioData = _pendingAudioData.ToArray();
            _pendingAudioData.Clear();
        }

        var base64Audio = Convert.ToBase64String(audioData);
        await SendEventAsync(new { type = "input_audio_buffer.append", audio = base64Audio });
    }

    private async Task SendEventAsync(object eventData)
    {
        if (_webSocket?.State != WebSocketState.Open)
            return;

        var json = JsonSerializer.Serialize(eventData, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });

        var buffer = Encoding.UTF8.GetBytes(json);
        await _webSocket.SendAsync(buffer, WebSocketMessageType.Text, true, _cts?.Token ?? CancellationToken.None);
    }

    private async Task ReceiveMessagesAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        var messageBuilder = new StringBuilder();

        try
        {
            while (_webSocket?.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                var result = await _webSocket.ReceiveAsync(buffer, cancellationToken);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    Log("WebSocket closed by server");
                    break;
                }

                messageBuilder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));

                if (result.EndOfMessage)
                {
                    var message = messageBuilder.ToString();
                    messageBuilder.Clear();
                    ProcessServerMessage(message);
                }
            }
        }
        catch (OperationCanceledException)
        {
            Log("Receive loop cancelled");
        }
        catch (Exception ex)
        {
            Log($"Receive error: {ex.Message}");
        }
    }

    private void ProcessServerMessage(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("type", out var typeElement))
                return;

            var type = typeElement.GetString();
            Log($"Received event: {type}");

            switch (type)
            {
                case "session.created":
                    Log("Session created successfully");
                    break;

                case "session.updated":
                    Log("Session updated");
                    break;

                case "input_audio_buffer.speech_started":
                    Log("Speech detected");
                    break;

                case "input_audio_buffer.speech_stopped":
                    Log("Speech ended");
                    break;

                case "conversation.item.input_audio_transcription.completed":
                    if (root.TryGetProperty("transcript", out var transcriptElement))
                    {
                        var transcript = transcriptElement.GetString();
                        if (!string.IsNullOrWhiteSpace(transcript))
                        {
                            _transcriptionBuffer.Append(transcript + " ");
                            var fullText = _transcriptionBuffer.ToString().Trim();
                            Log($"Transcription received (segment: {transcript.Length} chars, total: {fullText.Length} chars)");
                            RecognitionPartial?.Invoke(this, new TranscriptionEventArgs(fullText, false, true));
                        }
                    }
                    break;

                case "response.audio_transcript.delta":
                    // Partial transcript from response
                    if (root.TryGetProperty("delta", out var deltaElement))
                    {
                        var delta = deltaElement.GetString();
                        if (!string.IsNullOrWhiteSpace(delta))
                        {
                            Log($"Transcript delta: '{delta}'");
                        }
                    }
                    break;

                case "error":
                    if (root.TryGetProperty("error", out var errorElement) &&
                        errorElement.TryGetProperty("message", out var msgElement))
                    {
                        var errorMsg = msgElement.GetString() ?? "Unknown error";
                        Log($"API Error: {errorMsg}");
                        RecognitionError?.Invoke(this, new RecognitionErrorEventArgs(errorMsg, null));
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            Log($"Message processing error: {ex.Message}");
        }
    }
#endif

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

#if WINDOWS
        StopAudioCapture();
#endif
        _cts?.Cancel();
        _webSocket?.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    ~OpenAIRealtimeSpeechRecognitionService()
    {
        Dispose();
    }
}
