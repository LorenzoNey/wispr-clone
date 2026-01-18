using SharpHook;
using SharpHook.Native;
using WisprClone.Services.Interfaces;
using System.IO;

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
            Log("SimulatePasteAsync called");

            // Wait to ensure user has fully released the hotkey and clipboard is ready
            await Task.Delay(250);

            if (OperatingSystem.IsMacOS())
            {
                // macOS: Use Cmd+V
                Log("Simulating paste with Cmd+V (macOS)");
                await SimulateKeyComboAsync(KeyCode.VcLeftMeta, KeyCode.VcV);
            }
            else
            {
                // Windows/Linux: Use Shift+Insert (more universal, works in terminals)
                Log("Simulating paste with Shift+Insert (Windows/Linux)");
                await SimulateKeyComboAsync(KeyCode.VcLeftShift, KeyCode.VcInsert);
            }

            Log("SimulatePasteAsync completed");
            return true;
        }
        catch (Exception ex)
        {
            Log($"SimulatePasteAsync error: {ex.Message}");
            return false;
        }
    }

    private async Task SimulateKeyComboAsync(KeyCode modifier, KeyCode key)
    {
        var result = _simulator.SimulateKeyPress(modifier);
        Log($"Modifier press result: {result}");
        await Task.Delay(20);

        result = _simulator.SimulateKeyPress(key);
        Log($"Key press result: {result}");
        await Task.Delay(20);

        result = _simulator.SimulateKeyRelease(key);
        Log($"Key release result: {result}");
        await Task.Delay(20);

        result = _simulator.SimulateKeyRelease(modifier);
        Log($"Modifier release result: {result}");
    }

    private static void Log(string message)
    {
        var logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "WisprClone", "wispr_log.txt");
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [KeyboardSim] {message}";
        try { File.AppendAllText(logPath, line + Environment.NewLine); } catch { }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        // EventSimulator does not require disposal
        _disposed = true;
    }
}
