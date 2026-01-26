namespace WisprClone.Services.Interfaces;

/// <summary>
/// Interface for simulating keyboard input.
/// </summary>
public interface IKeyboardSimulationService : IDisposable
{
    /// <summary>
    /// Simulates a copy operation (Ctrl+C on Windows/Linux, Cmd+C on macOS).
    /// Copies the currently selected text to clipboard.
    /// </summary>
    /// <returns>True if the copy simulation was successful.</returns>
    Task<bool> SimulateCopyAsync();

    /// <summary>
    /// Simulates a paste operation (Ctrl+V on Windows/Linux, Cmd+V on macOS).
    /// </summary>
    /// <returns>True if the paste simulation was successful.</returns>
    Task<bool> SimulatePasteAsync();

    /// <summary>
    /// Indicates if the keyboard simulation service is available.
    /// </summary>
    bool IsAvailable { get; }
}
