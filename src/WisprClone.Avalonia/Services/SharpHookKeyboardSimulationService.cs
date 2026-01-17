using SharpHook;
using SharpHook.Native;
using WisprClone.Services.Interfaces;

namespace WisprClone.Services;

/// <summary>
/// Keyboard simulation service using SharpHook's EventSimulator.
/// Cross-platform support for Windows, macOS, and Linux.
/// </summary>
public class SharpHookKeyboardSimulationService : IKeyboardSimulationService
{
    private readonly ISettingsService _settingsService;
    private readonly EventSimulator _simulator;
    private bool _disposed;

    public SharpHookKeyboardSimulationService(ISettingsService settingsService)
    {
        _settingsService = settingsService;
        _simulator = new EventSimulator();
    }

    public bool IsAvailable => true;

    public async Task<bool> SimulatePasteAsync()
    {
        try
        {
            var delayMs = _settingsService.Current.PasteDelayMs;

            // Wait before simulating paste to allow clipboard to be ready
            // and give focus back to the target application
            await Task.Delay(delayMs);

            // Determine modifier key based on platform
            // macOS uses Cmd (Meta), Windows/Linux use Ctrl
            var modifierKey = OperatingSystem.IsMacOS()
                ? KeyCode.VcLeftMeta
                : KeyCode.VcLeftControl;

            // Simulate paste: Modifier down, V down, V up, Modifier up
            _simulator.SimulateKeyPress(modifierKey);
            await Task.Delay(10); // Small delay between key events

            _simulator.SimulateKeyPress(KeyCode.VcV);
            await Task.Delay(10);

            _simulator.SimulateKeyRelease(KeyCode.VcV);
            await Task.Delay(10);

            _simulator.SimulateKeyRelease(modifierKey);

            return true;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        // EventSimulator does not require disposal
        _disposed = true;
    }
}
