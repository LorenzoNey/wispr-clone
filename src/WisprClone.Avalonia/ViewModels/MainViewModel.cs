using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using WisprClone.Core;
using WisprClone.Infrastructure.Keyboard;
using WisprClone.Services.Interfaces;
using WisprClone.ViewModels.Base;

namespace WisprClone.ViewModels;

/// <summary>
/// Main application view model that orchestrates all services and UI (Avalonia version).
/// </summary>
public partial class MainViewModel : ViewModelBase, IDisposable
{
    private readonly ISpeechRecognitionService _speechService;
    private readonly IClipboardService _clipboardService;
    private readonly ISettingsService _settingsService;
    private readonly IGlobalKeyboardHook _keyboardHook;
    private readonly DoubleKeyTapDetector _hotkeyDetector;

    private OverlayViewModel? _overlayViewModel;
    private CancellationTokenSource? _recognitionCts;
    private TranscriptionState _currentState = TranscriptionState.Idle;
    private readonly object _stateLock = new();
    private bool _disposed;

    /// <summary>
    /// Raised when the transcription state changes.
    /// </summary>
    public event EventHandler<TranscriptionState>? StateChanged;

    /// <summary>
    /// Raised when overlay should be shown.
    /// </summary>
    public event EventHandler? ShowOverlayRequested;

    /// <summary>
    /// Raised when overlay should be hidden.
    /// </summary>
    public event EventHandler? HideOverlayRequested;

    /// <summary>
    /// Raised when settings window should be opened.
    /// </summary>
    public event EventHandler? OpenSettingsRequested;

    [ObservableProperty]
    private bool _isTranscribing;

    public OverlayViewModel? OverlayViewModel => _overlayViewModel;

    public MainViewModel(
        ISpeechRecognitionService speechService,
        IClipboardService clipboardService,
        ISettingsService settingsService,
        IGlobalKeyboardHook keyboardHook)
    {
        _speechService = speechService;
        _clipboardService = clipboardService;
        _settingsService = settingsService;
        _keyboardHook = keyboardHook;

        var settings = settingsService.Current;
        _hotkeyDetector = new DoubleKeyTapDetector(
            keyboardHook,
            GlobalKeyCode.LeftCtrl,
            settings.DoubleTapIntervalMs,
            settings.MaxKeyHoldDurationMs);

        _hotkeyDetector.DoubleTapDetected += OnDoubleTapDetected;
        _speechService.RecognitionCompleted += OnRecognitionCompleted;
        _speechService.RecognitionPartial += OnRecognitionPartial;
    }

    /// <summary>
    /// Initializes the application.
    /// </summary>
    public async Task InitializeAsync()
    {
        // Load settings
        await _settingsService.LoadAsync();

        // Initialize speech recognition
        await _speechService.InitializeAsync(_settingsService.Current.RecognitionLanguage);

        // Start hotkey detection
        _hotkeyDetector.Start();

        // Create overlay view model
        _overlayViewModel = new OverlayViewModel(_speechService, _settingsService);

        // Show overlay if not set to start minimized
        if (!_settingsService.Current.StartMinimized)
        {
            ShowOverlay();
        }
    }

    private async void OnDoubleTapDetected(object? sender, EventArgs e)
    {
        Log($"DoubleTap detected, current state: {_currentState}");

        // Toggle transcription
        if (_currentState == TranscriptionState.Idle)
        {
            await StartTranscriptionAsync();
        }
        else if (_currentState == TranscriptionState.Listening)
        {
            await StopTranscriptionAsync();
        }
    }

    private void Log(string message)
    {
        if (!_settingsService.Current.EnableLogging)
            return;

        try
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var logsDir = Path.Combine(appData, Constants.AppName, "logs");
            Directory.CreateDirectory(logsDir);

            var logFileName = $"wispr_{DateTime.Now:yyyy-MM-dd}.log";
            var logPath = Path.Combine(logsDir, logFileName);

            var line = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
            File.AppendAllText(logPath, line + Environment.NewLine);
        }
        catch
        {
            // Silently ignore logging errors
        }
    }

    private async Task StartTranscriptionAsync()
    {
        if (!TryTransitionState(TranscriptionState.Idle, TranscriptionState.Initializing))
            return;

        _recognitionCts = new CancellationTokenSource();

        var maxDurationSeconds = _settingsService.Current.MaxRecordingDurationSeconds;
        if (maxDurationSeconds > 0)
        {
            _recognitionCts.CancelAfter(TimeSpan.FromSeconds(maxDurationSeconds));
        }

        try
        {
            ShowOverlay();
            SetState(TranscriptionState.Listening);
            IsTranscribing = true;

            await _speechService.StartRecognitionAsync(_recognitionCts.Token);
        }
        catch (OperationCanceledException)
        {
            await HandleRecordingTimeoutAsync();
        }
        catch (Exception)
        {
            SetState(TranscriptionState.Error);
            await Task.Delay(1000);
            SetState(TranscriptionState.Idle);
            IsTranscribing = false;
        }
    }

    private async Task HandleRecordingTimeoutAsync()
    {
        SetState(TranscriptionState.Processing);

        try
        {
            var finalText = await _speechService.StopRecognitionAsync();

            if (_settingsService.Current.AutoCopyToClipboard && !string.IsNullOrWhiteSpace(finalText))
            {
                _ = _clipboardService.SetTextAsync(finalText);
            }

            SetState(TranscriptionState.Idle);
            IsTranscribing = false;

            _ = HideOverlayAfterDelayAsync(3000);
        }
        catch (Exception ex)
        {
            Log($"Timeout handling error: {ex.Message}");
            SetState(TranscriptionState.Error);
            await Task.Delay(1000);
            SetState(TranscriptionState.Idle);
            IsTranscribing = false;
        }
        finally
        {
            _recognitionCts?.Dispose();
            _recognitionCts = null;
        }
    }

    private async Task StopTranscriptionAsync()
    {
        if (!TryTransitionState(TranscriptionState.Listening, TranscriptionState.Processing))
            return;

        try
        {
            var finalText = await _speechService.StopRecognitionAsync();

            if (_settingsService.Current.AutoCopyToClipboard && !string.IsNullOrWhiteSpace(finalText))
            {
                _ = _clipboardService.SetTextAsync(finalText);
            }

            SetState(TranscriptionState.Idle);
            IsTranscribing = false;

            _ = HideOverlayAfterDelayAsync(3000);
        }
        catch (Exception ex)
        {
            Log($"StopTranscriptionAsync error: {ex.Message}");
            SetState(TranscriptionState.Error);
            await Task.Delay(1000);
            SetState(TranscriptionState.Idle);
            IsTranscribing = false;
        }
        finally
        {
            _recognitionCts?.Dispose();
            _recognitionCts = null;
        }
    }

    private async Task HideOverlayAfterDelayAsync(int delayMs)
    {
        await Task.Delay(delayMs);

        if (_currentState == TranscriptionState.Idle)
        {
            if (_overlayViewModel?.IsUserInteracting == true)
            {
                _ = RetryHideOverlayAsync();
                return;
            }

            HideOverlay();
        }
    }

    private async Task RetryHideOverlayAsync()
    {
        while (_overlayViewModel?.IsUserInteracting == true && _currentState == TranscriptionState.Idle)
        {
            await Task.Delay(500);
        }

        await Task.Delay(2000);

        if (_currentState == TranscriptionState.Idle && _overlayViewModel?.IsUserInteracting != true)
        {
            HideOverlay();
        }
    }

    private void OnRecognitionPartial(object? sender, TranscriptionEventArgs e)
    {
        // Partial results are displayed in the overlay via OverlayViewModel
        // Clipboard is only updated on final completion to avoid incomplete text
    }

    private async void OnRecognitionCompleted(object? sender, TranscriptionEventArgs e)
    {
        if (e.IsFinal && _settingsService.Current.AutoCopyToClipboard && !string.IsNullOrWhiteSpace(e.Text))
        {
            await _clipboardService.SetTextAsync(e.Text);
        }
    }

    private bool TryTransitionState(TranscriptionState expectedCurrent, TranscriptionState newState)
    {
        lock (_stateLock)
        {
            if (_currentState != expectedCurrent)
                return false;

            _currentState = newState;
            StateChanged?.Invoke(this, newState);
            return true;
        }
    }

    private void SetState(TranscriptionState newState)
    {
        lock (_stateLock)
        {
            _currentState = newState;
            StateChanged?.Invoke(this, newState);
        }
    }

    public void ShowOverlay()
    {
        Dispatcher.UIThread.Post(() =>
        {
            _overlayViewModel?.Show();
            ShowOverlayRequested?.Invoke(this, EventArgs.Empty);
        });
    }

    public void HideOverlay()
    {
        Dispatcher.UIThread.Post(() =>
        {
            _overlayViewModel?.SavePosition();
            _overlayViewModel?.Hide();
            HideOverlayRequested?.Invoke(this, EventArgs.Empty);
        });
    }

    public void ToggleOverlay()
    {
        if (_overlayViewModel?.IsVisible == true)
        {
            HideOverlay();
        }
        else
        {
            ShowOverlay();
        }
    }

    public void OpenSettings()
    {
        Dispatcher.UIThread.Post(() =>
        {
            OpenSettingsRequested?.Invoke(this, EventArgs.Empty);
        });
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _hotkeyDetector.DoubleTapDetected -= OnDoubleTapDetected;
        _speechService.RecognitionCompleted -= OnRecognitionCompleted;
        _speechService.RecognitionPartial -= OnRecognitionPartial;

        if (_currentState == TranscriptionState.Listening)
        {
            _recognitionCts?.Cancel();
        }

        _hotkeyDetector.Dispose();
        _keyboardHook.Dispose();
        _speechService.Dispose();
        _recognitionCts?.Dispose();

        Dispatcher.UIThread.Post(() =>
        {
            _overlayViewModel?.SavePosition();
        });

        _disposed = true;
        GC.SuppressFinalize(this);
    }

    ~MainViewModel()
    {
        Dispose();
    }
}
