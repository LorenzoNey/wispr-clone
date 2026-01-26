using WisprClone.Models;

namespace WisprClone.Services.Interfaces;

/// <summary>
/// Interface for application settings management.
/// </summary>
public interface ISettingsService
{
    /// <summary>
    /// Gets the current application settings.
    /// </summary>
    AppSettings Current { get; }

    /// <summary>
    /// Loads settings from storage.
    /// </summary>
    Task LoadAsync();

    /// <summary>
    /// Saves current settings to storage.
    /// </summary>
    Task SaveAsync();

    /// <summary>
    /// Updates settings with the provided action and saves.
    /// </summary>
    void Update(Action<AppSettings> updateAction);

    /// <summary>
    /// Resets all settings to their default values.
    /// </summary>
    void ResetToDefaults();

    /// <summary>
    /// Raised when settings are changed.
    /// </summary>
    event EventHandler<AppSettings>? SettingsChanged;
}
