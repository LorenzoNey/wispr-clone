using System;
using WisprClone.Core;

namespace WisprClone.Infrastructure.Keyboard;

/// <summary>
/// Cross-platform detector for double-tap of a specific key (e.g., Ctrl key).
/// </summary>
public class DoubleKeyTapDetector : IDisposable
{
    private readonly IGlobalKeyboardHook _hook;
    private readonly GlobalKeyCode _targetKey;
    private readonly int _maxIntervalMs;
    private readonly int _maxHoldDurationMs;
    private readonly Action<string>? _logAction;
    private readonly string _keyName;

    private DateTime _lastKeyDownTime = DateTime.MinValue;
    private DateTime _lastKeyUpTime = DateTime.MinValue;
    private int _tapCount;
    private bool _keyCurrentlyDown;
    private bool _disposed;
    private bool _started;

    /// <summary>
    /// Raised when a double-tap is detected.
    /// </summary>
    public event EventHandler? DoubleTapDetected;

    /// <summary>
    /// Initializes a new instance of the DoubleKeyTapDetector.
    /// </summary>
    /// <param name="hook">The global keyboard hook to use.</param>
    /// <param name="targetKey">The key to detect double-tap for (default: LeftCtrl).</param>
    /// <param name="maxIntervalMs">Maximum interval between taps in milliseconds.</param>
    /// <param name="maxHoldDurationMs">Maximum hold duration to count as a tap.</param>
    /// <param name="logAction">Optional logging action for debugging.</param>
    public DoubleKeyTapDetector(
        IGlobalKeyboardHook hook,
        GlobalKeyCode targetKey = GlobalKeyCode.LeftCtrl,
        int maxIntervalMs = Constants.DefaultDoubleTapIntervalMs,
        int maxHoldDurationMs = Constants.DefaultMaxKeyHoldDurationMs,
        Action<string>? logAction = null)
    {
        _hook = hook;
        _targetKey = targetKey;
        _maxIntervalMs = maxIntervalMs;
        _maxHoldDurationMs = maxHoldDurationMs;
        _logAction = logAction;
        _keyName = targetKey.ToString();

        _hook.KeyDown += OnKeyDown;
        _hook.KeyUp += OnKeyUp;

        Log($"Created detector for {_keyName}, maxInterval={maxIntervalMs}ms, maxHold={maxHoldDurationMs}ms");
    }

    /// <summary>
    /// Starts listening for double-tap events.
    /// </summary>
    public void Start()
    {
        Log($"Start() called, _started={_started}");
        _started = true;
        _hook.Install();
        Log($"Hook installed for {_keyName}");
    }

    /// <summary>
    /// Stops listening for double-tap events.
    /// </summary>
    public void Stop()
    {
        Log($"Stop() called");
        _started = false;
        _hook.Uninstall();
    }

    private void Log(string message)
    {
        _logAction?.Invoke($"[{_keyName}Detector] {message}");
    }

    private void OnKeyDown(object? sender, GlobalKeyEventArgs e)
    {
        // Always log key events for debugging (only for target key type)
        var isTarget = IsTargetKey(e.KeyCode);
        if (isTarget)
        {
            Log($"KeyDown: {e.KeyCode}, isTarget={isTarget}, _keyCurrentlyDown={_keyCurrentlyDown}, _started={_started}");
        }

        // Check for target key
        if (!isTarget || _keyCurrentlyDown)
            return;

        _keyCurrentlyDown = true;
        var now = DateTime.UtcNow;

        // Check if this is within the double-tap window
        var timeSinceLastKeyUp = (now - _lastKeyUpTime).TotalMilliseconds;

        if (timeSinceLastKeyUp > _maxIntervalMs)
        {
            // Too much time passed, reset count
            Log($"Reset tapCount (timeSinceLastKeyUp={timeSinceLastKeyUp:F0}ms > {_maxIntervalMs}ms)");
            _tapCount = 0;
        }

        _lastKeyDownTime = now;
        Log($"KeyDown processed, tapCount={_tapCount}");
    }

    private void OnKeyUp(object? sender, GlobalKeyEventArgs e)
    {
        var isTarget = IsTargetKey(e.KeyCode);
        if (isTarget)
        {
            Log($"KeyUp: {e.KeyCode}, isTarget={isTarget}, _keyCurrentlyDown={_keyCurrentlyDown}");
        }

        if (!isTarget || !_keyCurrentlyDown)
            return;

        _keyCurrentlyDown = false;
        var now = DateTime.UtcNow;

        // Check if the key was held too long (not a tap)
        var holdDuration = (now - _lastKeyDownTime).TotalMilliseconds;
        if (holdDuration > _maxHoldDurationMs)
        {
            Log($"Key held too long ({holdDuration:F0}ms > {_maxHoldDurationMs}ms), reset tapCount");
            _tapCount = 0;
            return;
        }

        _tapCount++;
        _lastKeyUpTime = now;
        Log($"Valid tap! tapCount={_tapCount}, holdDuration={holdDuration:F0}ms");

        if (_tapCount >= 2)
        {
            Log($"DOUBLE-TAP DETECTED! Firing event...");
            _tapCount = 0;
            DoubleTapDetected?.Invoke(this, EventArgs.Empty);
            Log($"Event fired, handlers count={(DoubleTapDetected?.GetInvocationList().Length ?? 0)}");
        }
    }

    private bool IsTargetKey(GlobalKeyCode key)
    {
        // Handle both left and right variants of the target key
        // For Ctrl: also accept Meta/Command on macOS
        // For Shift: only accept Shift keys
        return _targetKey switch
        {
            GlobalKeyCode.LeftCtrl or GlobalKeyCode.RightCtrl =>
                key == GlobalKeyCode.LeftCtrl ||
                key == GlobalKeyCode.RightCtrl ||
                key == GlobalKeyCode.LeftMeta ||
                key == GlobalKeyCode.RightMeta,

            GlobalKeyCode.LeftShift or GlobalKeyCode.RightShift =>
                key == GlobalKeyCode.LeftShift ||
                key == GlobalKeyCode.RightShift,

            _ => key == _targetKey
        };
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _hook.KeyDown -= OnKeyDown;
        _hook.KeyUp -= OnKeyUp;
        // Note: Don't dispose the hook here as it's injected and may be shared
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    ~DoubleKeyTapDetector()
    {
        Dispose();
    }
}
