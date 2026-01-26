using System.IO;
using System.Text.Json;
using WisprClone.Core;
using WisprClone.Models;
using WisprClone.Services.Interfaces;

namespace WisprClone.Services;

/// <summary>
/// Service for managing application settings.
/// Cross-platform compatible using standard .NET APIs.
/// </summary>
public class SettingsService : ISettingsService
{
    private readonly string _settingsPath;
    private AppSettings _settings = new();
    private readonly object _lock = new();

    /// <inheritdoc />
    public AppSettings Current => _settings;

    /// <inheritdoc />
    public event EventHandler<AppSettings>? SettingsChanged;

    public SettingsService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var appFolder = Path.Combine(appData, Constants.AppName);
        Directory.CreateDirectory(appFolder);
        _settingsPath = Path.Combine(appFolder, "settings.json");

        // Load settings synchronously during construction so they're available immediately
        LoadSync();
    }

    private void LoadSync()
    {
        try
        {
            if (File.Exists(_settingsPath))
            {
                var json = File.ReadAllText(_settingsPath);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json);
                if (loaded != null)
                {
                    _settings = loaded;
                }
            }
        }
        catch
        {
            _settings = new AppSettings();
        }
    }

    /// <inheritdoc />
    public async Task LoadAsync()
    {
        try
        {
            if (File.Exists(_settingsPath))
            {
                var json = await File.ReadAllTextAsync(_settingsPath);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json);
                if (loaded != null)
                {
                    lock (_lock)
                    {
                        _settings = loaded;
                    }
                }
            }
        }
        catch (Exception)
        {
            // If loading fails, use default settings
            _settings = new AppSettings();
        }
    }

    /// <inheritdoc />
    public async Task SaveAsync()
    {
        try
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true
            };

            string json;
            lock (_lock)
            {
                json = JsonSerializer.Serialize(_settings, options);
            }

            await File.WriteAllTextAsync(_settingsPath, json);
        }
        catch (Exception)
        {
            // Saving failed - could log
        }
    }

    /// <inheritdoc />
    public void Update(Action<AppSettings> updateAction)
    {
        lock (_lock)
        {
            updateAction(_settings);
        }

        SettingsChanged?.Invoke(this, _settings);

        // Fire and forget save
        _ = SaveAsync();
    }

    /// <inheritdoc />
    public void ResetToDefaults()
    {
        lock (_lock)
        {
            _settings = new AppSettings();
        }

        SettingsChanged?.Invoke(this, _settings);

        // Fire and forget save
        _ = SaveAsync();
    }
}
