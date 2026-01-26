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
    private readonly ITextToSpeechService _ttsService;
    private readonly IClipboardService _clipboardService;
    private readonly ISettingsService _settingsService;
    private readonly IGlobalKeyboardHook _keyboardHook;
    private readonly IKeyboardSimulationService _keyboardSimulator;
    private readonly ILoggingService _loggingService;
    private readonly DoubleKeyTapDetector _sttHotkeyDetector;
    private readonly DoubleKeyTapDetector _ttsHotkeyDetector;

    private OverlayViewModel? _overlayViewModel;
    private CancellationTokenSource? _recognitionCts;
    private CancellationTokenSource? _ttsCts;
    private CancellationTokenRegistration? _maxDurationRegistration;
    private TranscriptionState _currentState = TranscriptionState.Idle;
    private AppMode _currentAppMode = AppMode.Idle;
    private readonly object _stateLock = new();
    private readonly object _modeLock = new();
    private bool _ttsQueuedAfterStt;
    private string _currentTtsText = string.Empty;
    private bool _disposed;

    /// <summary>
    /// Raised when the transcription state changes.
    /// </summary>
    public event EventHandler<TranscriptionState>? StateChanged;

    /// <summary>
    /// Raised when overlay should be shown. Boolean indicates whether to activate (steal focus).
    /// </summary>
    public event EventHandler<bool>? ShowOverlayRequested;

    /// <summary>
    /// Raised when overlay should be hidden.
    /// </summary>
    public event EventHandler? HideOverlayRequested;

    /// <summary>
    /// Raised when settings window should be opened.
    /// </summary>
    public event EventHandler? OpenSettingsRequested;

    /// <summary>
    /// Raised when about window should be opened.
    /// </summary>
    public event EventHandler? OpenAboutRequested;

    [ObservableProperty]
    private bool _isTranscribing;

    public OverlayViewModel? OverlayViewModel => _overlayViewModel;

    public MainViewModel(
        ISpeechRecognitionService speechService,
        ITextToSpeechService ttsService,
        IClipboardService clipboardService,
        ISettingsService settingsService,
        IGlobalKeyboardHook keyboardHook,
        IKeyboardSimulationService keyboardSimulator,
        ILoggingService loggingService)
    {
        _speechService = speechService;
        _ttsService = ttsService;
        _clipboardService = clipboardService;
        _settingsService = settingsService;
        _keyboardHook = keyboardHook;
        _keyboardSimulator = keyboardSimulator;
        _loggingService = loggingService;

        var settings = settingsService.Current;

        // STT hotkey: Ctrl+Ctrl double-tap
        _sttHotkeyDetector = new DoubleKeyTapDetector(
            keyboardHook,
            GlobalKeyCode.LeftCtrl,
            settings.DoubleTapIntervalMs,
            settings.MaxKeyHoldDurationMs,
            msg => loggingService.Log("STT-Hotkey", msg));

        // TTS hotkey: Shift+Shift double-tap
        _ttsHotkeyDetector = new DoubleKeyTapDetector(
            keyboardHook,
            GlobalKeyCode.LeftShift,
            settings.TtsDoubleTapIntervalMs,
            settings.TtsMaxKeyHoldDurationMs,
            msg => loggingService.Log("TTS-Hotkey", msg));

        _sttHotkeyDetector.DoubleTapDetected += OnSttDoubleTapDetected;
        _ttsHotkeyDetector.DoubleTapDetected += OnTtsDoubleTapDetected;

        Log($"STT detector created with interval={settings.DoubleTapIntervalMs}ms, maxHold={settings.MaxKeyHoldDurationMs}ms");
        Log($"TTS detector created with interval={settings.TtsDoubleTapIntervalMs}ms, maxHold={settings.TtsMaxKeyHoldDurationMs}ms");
        _speechService.RecognitionCompleted += OnRecognitionCompleted;
        _speechService.RecognitionPartial += OnRecognitionPartial;
        _ttsService.SpeakCompleted += OnTtsSpeakCompleted;
        _ttsService.SpeakError += OnTtsSpeakError;
        _ttsService.StateChanged += OnTtsStateChanged;
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

        // Initialize TTS
        await _ttsService.InitializeAsync(_settingsService.Current.RecognitionLanguage);

        // Start hotkey detection for both STT and TTS
        Log("Starting STT hotkey detector...");
        _sttHotkeyDetector.Start();
        Log("Starting TTS hotkey detector...");
        _ttsHotkeyDetector.Start(); // Both detectors share the same hook, but calling Start ensures proper initialization
        Log("Both hotkey detectors started");

        // Create overlay view model
        _overlayViewModel = new OverlayViewModel(_speechService, _ttsService, _settingsService);
        _overlayViewModel.HideRequested += OnOverlayHideRequested;
        _overlayViewModel.TtsPauseResumeRequested += OnTtsPauseResumeRequested;
        _overlayViewModel.TtsStopRequested += OnTtsStopRequested;
        _overlayViewModel.TtsRunRequested += OnTtsRunRequested;

        // Show overlay if not set to start minimized
        if (!_settingsService.Current.StartMinimized)
        {
            ShowOverlay();
        }
    }

    private async void OnSttDoubleTapDetected(object? sender, EventArgs e)
    {
        Log($"STT DoubleTap detected, current mode: {_currentAppMode}");

        AppMode currentMode;
        lock (_modeLock)
        {
            currentMode = _currentAppMode;
        }

        switch (currentMode)
        {
            case AppMode.TtsSpeaking:
                // Stop TTS immediately, then start STT
                await StopTtsAndStartSttAsync();
                break;

            case AppMode.SttListening:
                // Toggle off - stop STT
                await StopTranscriptionAsync();
                break;

            case AppMode.SttProcessing:
                // Ignore - already processing
                break;

            case AppMode.Idle:
                // Start STT
                await StartTranscriptionAsync();
                break;

            case AppMode.Transitioning:
                // Ignore during transitions
                break;
        }
    }

    private async void OnTtsDoubleTapDetected(object? sender, EventArgs e)
    {
        Log($"TTS DoubleTap detected! current mode: {_currentAppMode}");

        AppMode currentMode;
        lock (_modeLock)
        {
            currentMode = _currentAppMode;
        }

        Log($"TTS: Handling mode {currentMode}");

        switch (currentMode)
        {
            case AppMode.SttListening:
                // Stop STT gracefully, then start TTS with result
                Log("TTS: Stopping STT and starting TTS");
                await StopSttAndStartTtsAsync();
                break;

            case AppMode.SttProcessing:
                // Queue TTS for after STT completes
                Log("TTS: Queueing for after STT completes");
                _ttsQueuedAfterStt = true;
                break;

            case AppMode.TtsSpeaking:
                // Toggle off - stop TTS
                Log("TTS: Stopping TTS");
                await StopTtsAsync();
                break;

            case AppMode.Idle:
                // Start TTS (read clipboard)
                Log("TTS: Starting TTS from clipboard");
                await StartTtsFromClipboardAsync();
                break;

            case AppMode.Transitioning:
                // Ignore during transitions
                Log("TTS: Ignoring - currently transitioning");
                break;
        }
    }

    private void Log(string message)
    {
        _loggingService.Log("MainViewModel", message);
    }

    private void SetAppMode(AppMode newMode)
    {
        lock (_modeLock)
        {
            Log($"Mode change: {_currentAppMode} -> {newMode}");
            _currentAppMode = newMode;
        }

        // Update overlay view model mode
        Dispatcher.UIThread.Post(() =>
        {
            _overlayViewModel?.SetAppMode(newMode);
        });
    }

    private async Task StartTtsFromClipboardAsync()
    {
        Log("TTS: StartTtsFromClipboardAsync called");
        SetAppMode(AppMode.Transitioning);

        try
        {
            // First, simulate Ctrl+C to copy any selected text from the active window
            Log("TTS: Simulating copy to capture selected text...");
            await _keyboardSimulator.SimulateCopyAsync();

            // Read clipboard (now contains selected text, if any was selected)
            Log("TTS: Reading clipboard...");
            var text = await _clipboardService.GetTextAsync();
            Log($"TTS: Clipboard text length: {text?.Length ?? 0}");

            if (string.IsNullOrWhiteSpace(text))
            {
                Log("TTS: Nothing to speak (no text selected or clipboard empty)");
                _overlayViewModel?.ShowTemporaryMessage("No text selected", 2000);
                SetAppMode(AppMode.Idle);
                return;
            }

            Log($"TTS: Starting TTS with text: '{text.Substring(0, Math.Min(50, text.Length))}...'");
            await StartTtsWithTextAsync(text);
        }
        catch (Exception ex)
        {
            Log($"TTS startup error: {ex.Message}\n{ex.StackTrace}");
            SetAppMode(AppMode.Idle);
        }
    }

    private async Task StartTtsWithTextAsync(string text)
    {
        Log("TTS: StartTtsWithTextAsync called");
        SetAppMode(AppMode.TtsSpeaking);

        // Store text for use when TTS completes
        _currentTtsText = text;

        // Ensure overlay is shown before starting TTS
        // Use activate:true on first show to ensure window is properly initialized
        Log("TTS: Showing overlay...");
        ShowOverlay(activate: true);

        // Longer delay to ensure overlay is fully visible and initialized on Windows
        // This is especially important for the first display
        Log("TTS: Waiting for overlay to be visible...");
        await Task.Delay(150);

        _ttsCts = new CancellationTokenSource();

        try
        {
            // Tell overlay whether pause is supported by this provider
            _overlayViewModel?.SetTtsSupportsPause(_ttsService.SupportsPause);

            // Prepare the overlay for TTS display
            Log("TTS: Starting TTS display...");
            _overlayViewModel?.StartTtsDisplay(text);

            Log("TTS: Calling SpeakAsync...");
            await _ttsService.SpeakAsync(text, _ttsCts.Token);
            Log("TTS: SpeakAsync completed");
        }
        catch (OperationCanceledException)
        {
            Log("TTS cancelled");
        }
        catch (Exception ex)
        {
            Log($"TTS error: {ex.Message}\n{ex.StackTrace}");
            _overlayViewModel?.ShowTemporaryMessage($"TTS Error: {ex.Message}", 3000);
        }
    }

    private async Task StopTtsAsync()
    {
        Log("Stopping TTS");

        _ttsCts?.Cancel();
        await _ttsService.StopAsync();

        // Keep the spoken text visible in the overlay
        _overlayViewModel?.StopTtsDisplay(_currentTtsText);
        SetAppMode(AppMode.Idle);

        // Don't hide the overlay - keep text visible for user
    }

    private async Task StopTtsAndStartSttAsync()
    {
        SetAppMode(AppMode.Transitioning);

        // Stop TTS immediately
        _ttsCts?.Cancel();
        await _ttsService.StopAsync();
        _overlayViewModel?.StopTtsDisplay(_currentTtsText);

        // Start STT
        await StartTranscriptionAsync();
    }

    private async Task StopSttAndStartTtsAsync()
    {
        SetAppMode(AppMode.Transitioning);

        // Stop STT gracefully (allows final processing)
        var finalText = await _speechService.StopRecognitionAsync();
        _recognitionCts?.Cancel();
        _maxDurationRegistration?.Dispose();
        _maxDurationRegistration = null;
        _recognitionCts?.Dispose();
        _recognitionCts = null;

        Log($"STT stopped, finalText: '{finalText?.Substring(0, Math.Min(50, finalText?.Length ?? 0))}...'");

        // Use the final text from STT directly (not from clipboard)
        if (!string.IsNullOrWhiteSpace(finalText))
        {
            // Also copy to clipboard for consistency
            await _clipboardService.SetTextAsync(finalText);
            await StartTtsWithTextAsync(finalText);
        }
        else
        {
            Log("STT returned no text, not starting TTS");
            SetAppMode(AppMode.Idle);
            _ = HideOverlayAfterDelayAsync(3000);
        }
    }

    private void OnTtsSpeakCompleted(object? sender, SpeechSynthesisEventArgs e)
    {
        Log("TTS speak completed");

        Dispatcher.UIThread.Post(async () =>
        {
            // Highlight the last word before stopping
            _overlayViewModel?.HighlightLastWord();

            // Small delay to ensure the last word highlight is visible before clearing
            await Task.Delay(300);

            // Keep the spoken text visible in the overlay (don't restore old text)
            _overlayViewModel?.OnTtsCompleted(_currentTtsText);

            if (_currentAppMode == AppMode.TtsSpeaking)
            {
                SetAppMode(AppMode.Idle);
                // Don't hide the overlay after TTS - keep text visible for user
            }
        });
    }

    private void OnTtsSpeakError(object? sender, SpeechSynthesisErrorEventArgs e)
    {
        Log($"TTS error: {e.Message}");

        Dispatcher.UIThread.Post(() =>
        {
            _overlayViewModel?.StopTtsDisplay(_currentTtsText);
            _overlayViewModel?.ShowTemporaryMessage($"TTS Error: {e.Message}", 3000);
            SetAppMode(AppMode.Idle);
        });
    }

    private void OnTtsStateChanged(object? sender, SynthesisStateChangedEventArgs e)
    {
        Log($"TTS state changed: {e.OldState} -> {e.NewState}");

        Dispatcher.UIThread.Post(() =>
        {
            _overlayViewModel?.SetTtsPaused(e.NewState == SynthesisState.Paused);
        });
    }

    private async Task StartTranscriptionAsync()
    {
        if (!TryTransitionState(TranscriptionState.Idle, TranscriptionState.Initializing))
            return;

        SetAppMode(AppMode.SttListening);
        _recognitionCts = new CancellationTokenSource();

        var maxDurationSeconds = _settingsService.Current.MaxRecordingDurationSeconds;
        if (maxDurationSeconds > 0)
        {
            _recognitionCts.CancelAfter(TimeSpan.FromSeconds(maxDurationSeconds));

            // Register callback to handle max duration timeout
            // This is needed because StartRecognitionAsync returns immediately
            _maxDurationRegistration = _recognitionCts.Token.Register(() =>
            {
                Log($"Max recording duration ({maxDurationSeconds}s) reached, stopping recording");
                _ = HandleRecordingTimeoutAsync();
            });
        }

        try
        {
            ShowOverlay(activate: false); // Don't steal focus from current app
            SetState(TranscriptionState.Listening);
            IsTranscribing = true;

            await _speechService.StartRecognitionAsync(_recognitionCts.Token);
        }
        catch (Exception)
        {
            SetState(TranscriptionState.Error);
            SetAppMode(AppMode.Idle);
            await Task.Delay(1000);
            SetState(TranscriptionState.Idle);
            IsTranscribing = false;
        }
    }

    private async Task HandleRecordingTimeoutAsync()
    {
        // Only handle timeout if we're still listening (user might have stopped manually)
        if (!TryTransitionState(TranscriptionState.Listening, TranscriptionState.Processing))
            return;

        SetAppMode(AppMode.SttProcessing);

        try
        {
            var finalText = await _speechService.StopRecognitionAsync();

            if (!string.IsNullOrWhiteSpace(finalText))
            {
                // Insert at cursor: copy to clipboard and paste (works independently)
                if (_settingsService.Current.AutoPasteAfterCopy)
                {
                    await _clipboardService.SetTextAsync(finalText);
                    await _keyboardSimulator.SimulatePasteAsync();
                }
                // Just copy to clipboard (no paste)
                else if (_settingsService.Current.AutoCopyToClipboard)
                {
                    await _clipboardService.SetTextAsync(finalText);
                }
            }

            SetState(TranscriptionState.Idle);
            SetAppMode(AppMode.Idle);
            IsTranscribing = false;

            // Check if TTS was queued
            if (_ttsQueuedAfterStt)
            {
                _ttsQueuedAfterStt = false;
                await StartTtsFromClipboardAsync();
            }
            else
            {
                _ = HideOverlayAfterDelayAsync(3000);
            }
        }
        catch (Exception ex)
        {
            Log($"Timeout handling error: {ex.Message}");
            SetState(TranscriptionState.Error);
            SetAppMode(AppMode.Idle);
            await Task.Delay(1000);
            SetState(TranscriptionState.Idle);
            IsTranscribing = false;
        }
        finally
        {
            _maxDurationRegistration?.Dispose();
            _maxDurationRegistration = null;
            _recognitionCts?.Dispose();
            _recognitionCts = null;
        }
    }

    private async Task StopTranscriptionAsync()
    {
        if (!TryTransitionState(TranscriptionState.Listening, TranscriptionState.Processing))
            return;

        SetAppMode(AppMode.SttProcessing);

        try
        {
            var finalText = await _speechService.StopRecognitionAsync();

            if (!string.IsNullOrWhiteSpace(finalText))
            {
                // Insert at cursor: copy to clipboard and paste (works independently)
                if (_settingsService.Current.AutoPasteAfterCopy)
                {
                    await _clipboardService.SetTextAsync(finalText);
                    await _keyboardSimulator.SimulatePasteAsync();
                }
                // Just copy to clipboard (no paste)
                else if (_settingsService.Current.AutoCopyToClipboard)
                {
                    await _clipboardService.SetTextAsync(finalText);
                }
            }

            SetState(TranscriptionState.Idle);
            SetAppMode(AppMode.Idle);
            IsTranscribing = false;

            // Check if TTS was queued
            if (_ttsQueuedAfterStt)
            {
                _ttsQueuedAfterStt = false;
                await StartTtsFromClipboardAsync();
            }
            else
            {
                _ = HideOverlayAfterDelayAsync(3000);
            }
        }
        catch (Exception ex)
        {
            Log($"StopTranscriptionAsync error: {ex.Message}");
            SetState(TranscriptionState.Error);
            SetAppMode(AppMode.Idle);
            await Task.Delay(1000);
            SetState(TranscriptionState.Idle);
            IsTranscribing = false;
        }
        finally
        {
            _maxDurationRegistration?.Dispose();
            _maxDurationRegistration = null;
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

    private async void OnRecognitionPartial(object? sender, TranscriptionEventArgs e)
    {
        // Update clipboard when a segment is finalized (not interim hypotheses)
        if (e.IsSegmentFinalized && _settingsService.Current.AutoCopyToClipboard && !string.IsNullOrWhiteSpace(e.Text))
        {
            await _clipboardService.SetTextAsync(e.Text);
        }
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

    public void ShowOverlay(bool activate = true)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _overlayViewModel?.Show();
            ShowOverlayRequested?.Invoke(this, activate);
        });
    }

    public void HideOverlay()
    {
        Dispatcher.UIThread.Post(() =>
        {
            _overlayViewModel?.Hide();
        });
    }

    private void OnOverlayHideRequested(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _overlayViewModel?.SavePosition();
            HideOverlayRequested?.Invoke(this, EventArgs.Empty);
        });
    }

    private async void OnTtsPauseResumeRequested(object? sender, EventArgs e)
    {
        if (_currentAppMode != AppMode.TtsSpeaking) return;

        try
        {
            if (_ttsService.CurrentState == SynthesisState.Speaking)
            {
                await _ttsService.PauseAsync();
                _overlayViewModel?.SetAppMode(AppMode.TtsSpeaking); // Keep mode but update UI
            }
            else if (_ttsService.CurrentState == SynthesisState.Paused)
            {
                await _ttsService.ResumeAsync();
            }
        }
        catch (Exception ex)
        {
            Log($"TTS pause/resume error: {ex.Message}");
        }
    }

    private async void OnTtsStopRequested(object? sender, EventArgs e)
    {
        if (_currentAppMode != AppMode.TtsSpeaking) return;

        await StopTtsAsync();
    }

    private async void OnTtsRunRequested(object? sender, EventArgs e)
    {
        Log($"TTS Run button pressed! current mode: {_currentAppMode}");

        // Use the same lock pattern as the hotkey handler
        AppMode currentMode;
        lock (_modeLock)
        {
            currentMode = _currentAppMode;
        }

        Log($"TTS Run: Handling mode {currentMode}");

        switch (currentMode)
        {
            case AppMode.SttListening:
                // Stop STT gracefully, then start TTS with result (same as TTS hotkey)
                Log("TTS Run: Stopping STT and starting TTS");
                await StopSttAndStartTtsAsync();
                break;

            case AppMode.SttProcessing:
                // Queue TTS for after STT completes
                Log("TTS Run: Queueing for after STT completes");
                _ttsQueuedAfterStt = true;
                break;

            case AppMode.TtsSpeaking:
                // Already speaking - do nothing (user can use stop button)
                Log("TTS Run: Already speaking, ignoring");
                break;

            case AppMode.Idle:
                // Start TTS (read clipboard)
                Log("TTS Run: Starting TTS from clipboard");
                await StartTtsFromClipboardAsync();
                break;

            case AppMode.Transitioning:
                // Ignore during transitions
                Log("TTS Run: Ignoring - currently transitioning");
                break;
        }
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

    public void OpenAbout()
    {
        Dispatcher.UIThread.Post(() =>
        {
            OpenAboutRequested?.Invoke(this, EventArgs.Empty);
        });
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _sttHotkeyDetector.DoubleTapDetected -= OnSttDoubleTapDetected;
        _ttsHotkeyDetector.DoubleTapDetected -= OnTtsDoubleTapDetected;
        _speechService.RecognitionCompleted -= OnRecognitionCompleted;
        _speechService.RecognitionPartial -= OnRecognitionPartial;
        _ttsService.SpeakCompleted -= OnTtsSpeakCompleted;
        _ttsService.SpeakError -= OnTtsSpeakError;
        _ttsService.StateChanged -= OnTtsStateChanged;

        if (_currentState == TranscriptionState.Listening)
        {
            _recognitionCts?.Cancel();
        }

        if (_currentAppMode == AppMode.TtsSpeaking)
        {
            _ttsCts?.Cancel();
        }

        _sttHotkeyDetector.Dispose();
        _ttsHotkeyDetector.Dispose();
        _keyboardHook.Dispose();
        _keyboardSimulator.Dispose();
        _speechService.Dispose();
        _ttsService.Dispose();
        _maxDurationRegistration?.Dispose();
        _recognitionCts?.Dispose();
        _ttsCts?.Dispose();

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
