namespace WisprClone.Services.Interfaces;

/// <summary>
/// Interface for simulating keyboard input.
/// </summary>
public interface IKeyboardSimulationService : IDisposable
{
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
