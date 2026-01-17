using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.IO;
using WisprClone.Core;
using WisprClone.Infrastructure.Keyboard;
using WisprClone.Models;
using WisprClone.Services;
using WisprClone.Services.Interfaces;
using WisprClone.Services.Speech;
using WisprClone.ViewModels;
using WisprClone.Views;

namespace WisprClone;

public partial class App : Application
{
    private IServiceProvider _serviceProvider = null!;
    private MainViewModel _mainViewModel = null!;
    private TrayIcon? _trayIcon;
    private TrayIcons? _trayIcons; // Keep reference to prevent GC
    private OverlayWindow? _overlayWindow;
    private SettingsWindow? _settingsWindow;
    private AboutWindow? _aboutWindow;
    private NativeMenu? _trayMenu;
    private NativeMenuItem? _updateMenuItem;
    private NativeMenuItemSeparator? _updateSeparator;
    private IUpdateService? _updateService;
    private bool _updateAvailable;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override async void OnFrameworkInitializationCompleted()
    {
        // Configure services
        var services = new ServiceCollection();
        ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider();

        // Subscribe to shutdown event to properly dispose resources
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Keep app running even when no windows are visible (for tray icon)
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            desktop.ShutdownRequested += OnShutdownRequested;
        }

        // Initialize main view model
        _mainViewModel = _serviceProvider.GetRequiredService<MainViewModel>();

        // Subscribe to events
        _mainViewModel.ShowOverlayRequested += OnShowOverlayRequested;
        _mainViewModel.HideOverlayRequested += OnHideOverlayRequested;
        _mainViewModel.OpenSettingsRequested += OnOpenSettingsRequested;
        _mainViewModel.OpenAboutRequested += OnOpenAboutRequested;

        try
        {
            await _mainViewModel.InitializeAsync();
        }
        catch (Exception ex)
        {
            Log($"Failed to initialize: {ex.Message}");
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktopLifetime)
            {
                desktopLifetime.Shutdown(1);
            }
            return;
        }

        // Create overlay window
        _overlayWindow = new OverlayWindow
        {
            DataContext = _mainViewModel.OverlayViewModel
        };

        // Setup system tray
        SetupTrayIcon();

        // Initialize update checking
        InitializeUpdateChecking();

        base.OnFrameworkInitializationCompleted();
    }

    private void InitializeUpdateChecking()
    {
        _updateService = _serviceProvider.GetRequiredService<IUpdateService>();
        _updateService.UpdateAvailable += OnUpdateAvailable;

        var settings = _serviceProvider.GetRequiredService<ISettingsService>();
        if (settings.Current.CheckForUpdatesAutomatically)
        {
            // Check for updates on startup (fire and forget)
            _ = _updateService.CheckForUpdatesAsync();

            // Start periodic checks
            _updateService.StartPeriodicChecks(TimeSpan.FromHours(Constants.DefaultUpdateCheckIntervalHours));
        }
    }

    private void OnUpdateAvailable(object? sender, UpdateAvailableEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _updateAvailable = true;

            // Add update menu items dynamically if not already added
            if (_trayMenu != null && _updateMenuItem == null)
            {
                // Find the index of the last separator (before Exit)
                int exitSeparatorIndex = _trayMenu.Items.Count - 2; // Separator is second to last

                // Create and insert update separator
                _updateSeparator = new NativeMenuItemSeparator();
                _trayMenu.Items.Insert(exitSeparatorIndex, _updateSeparator);

                // Create and insert update menu item
                _updateMenuItem = new NativeMenuItem($"Update Available (v{e.LatestVersion.Major}.{e.LatestVersion.Minor}.{e.LatestVersion.Build})");
                _updateMenuItem.Click += (_, _) => SafeExecute(() => OnUpdateMenuClicked(), "Update");
                _trayMenu.Items.Insert(exitSeparatorIndex + 1, _updateMenuItem);
            }
            else if (_updateMenuItem != null)
            {
                // Update existing menu item text
                _updateMenuItem.Header = $"Update Available (v{e.LatestVersion.Major}.{e.LatestVersion.Minor}.{e.LatestVersion.Build})";
            }

            // Update tray icon to show update indicator
            UpdateTrayIconForState();

            // Update tooltip
            if (_trayIcon != null)
            {
                _trayIcon.ToolTipText = $"WisprClone - Update Available (v{e.LatestVersion.Major}.{e.LatestVersion.Minor}.{e.LatestVersion.Build})";
            }
        });
    }

    private void UpdateTrayIconForState()
    {
        if (_trayIcon == null) return;

        // Use update icon if available, otherwise fall back to idle
        var iconName = _updateAvailable ? "tray_idle_update.ico" : "tray_idle.ico";

        try
        {
            _trayIcon.Icon = new WindowIcon(GetIconStream(iconName));
        }
        catch
        {
            // Fallback to regular icon if update icon doesn't exist
            if (_updateAvailable)
            {
                try
                {
                    _trayIcon.Icon = new WindowIcon(GetIconStream("tray_idle.ico"));
                }
                catch { }
            }
        }
    }

    private void OnShowOverlayRequested(object? sender, EventArgs e)
    {
        try
        {
            if (_overlayWindow != null)
            {
                _overlayWindow.Show();
                _overlayWindow.Activate();
            }
        }
        catch (Exception ex)
        {
            Log($"Error showing overlay: {ex.Message}");
        }
    }

    private void OnHideOverlayRequested(object? sender, EventArgs e)
    {
        try
        {
            _overlayWindow?.Hide();
        }
        catch (Exception ex)
        {
            Log($"Error hiding overlay: {ex.Message}");
        }
    }

    private void OnOpenSettingsRequested(object? sender, EventArgs e)
    {
        try
        {
            if (_settingsWindow != null && _settingsWindow.IsVisible)
            {
                _settingsWindow.Activate();
                return;
            }

            var settingsService = _serviceProvider.GetRequiredService<ISettingsService>();
            var updateService = _serviceProvider.GetRequiredService<IUpdateService>();
            _settingsWindow = new SettingsWindow(settingsService, updateService);
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
            _settingsWindow.Show();
        }
        catch (Exception ex)
        {
            Log($"Error opening settings: {ex.Message}");
        }
    }

    private void OnOpenAboutRequested(object? sender, EventArgs e)
    {
        try
        {
            if (_aboutWindow != null && _aboutWindow.IsVisible)
            {
                _aboutWindow.Activate();
                return;
            }

            _aboutWindow = new AboutWindow();
            _aboutWindow.Closed += (_, _) => _aboutWindow = null;
            _aboutWindow.Show();
        }
        catch (Exception ex)
        {
            Log($"Error opening about: {ex.Message}");
        }
    }

    private void ConfigureServices(IServiceCollection services)
    {
        // Settings (cross-platform)
        services.AddSingleton<ISettingsService, SettingsService>();

        // Clipboard service (cross-platform via Avalonia)
        services.AddSingleton<IClipboardService, AvaloniaClipboardService>();

        // Global keyboard hook (cross-platform via SharpHook)
        services.AddSingleton<IGlobalKeyboardHook, SharpHookKeyboardHook>();

        // Keyboard simulation service (cross-platform via SharpHook)
        services.AddSingleton<IKeyboardSimulationService, SharpHookKeyboardSimulationService>();

        // Update service
        services.AddSingleton<IUpdateService, UpdateService>();

        // Speech services
        services.AddSingleton<AzureSpeechRecognitionService>();
        services.AddSingleton<OpenAIWhisperSpeechRecognitionService>();
        services.AddSingleton<OpenAIRealtimeSpeechRecognitionService>();

#if WINDOWS
        // Windows: All providers available including offline
        services.AddSingleton<OfflineSpeechRecognitionService>();
        services.AddSingleton<HybridSpeechRecognitionService>(sp =>
        {
            var settings = sp.GetRequiredService<ISettingsService>();
            var offline = sp.GetRequiredService<OfflineSpeechRecognitionService>();
            var azure = sp.GetRequiredService<AzureSpeechRecognitionService>();
            return new HybridSpeechRecognitionService(offline, azure, settings);
        });

        services.AddSingleton<ISpeechRecognitionService>(sp =>
        {
            var settings = sp.GetRequiredService<ISettingsService>();
            Log($"Initial speech provider: {settings.Current.SpeechProvider}");

            return new SpeechServiceManager(
                sp.GetRequiredService<OfflineSpeechRecognitionService>(),
                sp.GetRequiredService<AzureSpeechRecognitionService>(),
                sp.GetRequiredService<OpenAIWhisperSpeechRecognitionService>(),
                sp.GetRequiredService<OpenAIRealtimeSpeechRecognitionService>(),
                sp.GetRequiredService<HybridSpeechRecognitionService>(),
                settings);
        });
#else
        // macOS/Linux: Cloud providers only (Azure + OpenAI)
        services.AddSingleton<ISpeechRecognitionService>(sp =>
        {
            var settings = sp.GetRequiredService<ISettingsService>();
            Log($"Initial speech provider (non-Windows): {settings.Current.SpeechProvider}");

            return new CrossPlatformSpeechServiceManager(
                sp.GetRequiredService<AzureSpeechRecognitionService>(),
                sp.GetRequiredService<OpenAIWhisperSpeechRecognitionService>(),
                settings);
        });
#endif

        // ViewModels
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<OverlayViewModel>();
        services.AddSingleton<SystemTrayViewModel>();
    }

    private void SetupTrayIcon()
    {
        var trayViewModel = _serviceProvider.GetRequiredService<SystemTrayViewModel>();

        _trayIcon = new TrayIcon
        {
            Icon = new WindowIcon(GetIconStream("tray_idle.ico")),
            ToolTipText = "WisprClone - Ready (Ctrl+Ctrl to start)",
            Menu = CreateTrayMenu(trayViewModel),
            IsVisible = true
        };

        _trayIcon.Clicked += (_, _) =>
        {
            try
            {
                trayViewModel.ToggleOverlayCommand.Execute(null);
            }
            catch (Exception ex)
            {
                Log($"Tray click error: {ex.Message}");
            }
        };

        // Store reference to prevent garbage collection
        _trayIcons = new TrayIcons { _trayIcon };
        TrayIcon.SetIcons(this, _trayIcons);
    }

    private NativeMenu CreateTrayMenu(SystemTrayViewModel viewModel)
    {
        _trayMenu = new NativeMenu();

        var showItem = new NativeMenuItem("Show Overlay");
        showItem.Click += (_, _) => SafeExecute(() => viewModel.ShowOverlayCommand.Execute(null), "Show Overlay");
        _trayMenu.Items.Add(showItem);

        var hideItem = new NativeMenuItem("Hide Overlay");
        hideItem.Click += (_, _) => SafeExecute(() => viewModel.HideOverlayCommand.Execute(null), "Hide Overlay");
        _trayMenu.Items.Add(hideItem);

        _trayMenu.Items.Add(new NativeMenuItemSeparator());

        var settingsItem = new NativeMenuItem("Settings...");
        settingsItem.Click += (_, _) => SafeExecute(() => viewModel.OpenSettingsCommand.Execute(null), "Open Settings");
        _trayMenu.Items.Add(settingsItem);

        var aboutItem = new NativeMenuItem("About...");
        aboutItem.Click += (_, _) => SafeExecute(() => viewModel.OpenAboutCommand.Execute(null), "Open About");
        _trayMenu.Items.Add(aboutItem);

        // Note: Update separator and menu item will be added dynamically when an update is available

        _trayMenu.Items.Add(new NativeMenuItemSeparator());

        var exitItem = new NativeMenuItem("Exit");
        exitItem.Click += (_, _) =>
        {
            Log("Exit clicked - forcing cleanup...");
            ForceCleanupAndExit();
        };
        _trayMenu.Items.Add(exitItem);

        return _trayMenu;
    }

    private void SafeExecute(Action action, string actionName)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Log($"Error in {actionName}: {ex.Message}");
        }
    }

    private async void OnUpdateMenuClicked()
    {
        if (_updateService == null) return;

        try
        {
            // Download directly instead of opening settings
            var progress = new Progress<double>(p =>
            {
                // Update menu item text with progress
                if (_updateMenuItem != null)
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        _updateMenuItem.Header = $"Downloading... {p:F0}%";
                    });
                }
            });

            var installerPath = await _updateService.DownloadUpdateAsync(progress);

            if (!string.IsNullOrEmpty(installerPath))
            {
                _updateService.LaunchInstaller(installerPath);
            }
            else
            {
                // Fallback to settings if download fails
                OnOpenSettingsRequested(this, EventArgs.Empty);
            }
        }
        catch
        {
            // Fallback to settings on error
            OnOpenSettingsRequested(this, EventArgs.Empty);
        }
    }

    private void OnShutdownRequested(object? sender, ShutdownRequestedEventArgs e)
    {
        Log("Shutdown requested - disposing resources...");

        try
        {
            // Dispose update service
            _updateService?.Dispose();

            // Dispose main view model (this disposes keyboard hook, speech services, etc.)
            _mainViewModel?.Dispose();

            // Remove tray icon
            if (_trayIcon != null)
            {
                _trayIcon.IsVisible = false;
                _trayIcon.Dispose();
            }

            Log("Resources disposed successfully");
        }
        catch (Exception ex)
        {
            Log($"Error during shutdown: {ex.Message}");
        }
    }

    private Stream GetIconStream(string iconName)
    {
        var uri = new Uri($"avares://WisprClone/Resources/Icons/{iconName}");
        return AssetLoader.Open(uri);
    }

    private void ForceCleanupAndExit()
    {
        Log("ForceCleanupAndExit: Starting cleanup on background thread...");

        // Run cleanup on background thread with timeout
        var cleanupTask = Task.Run(() =>
        {
            try
            {
                Log("ForceCleanupAndExit: Disposing update service...");
                _updateService?.Dispose();

                Log("ForceCleanupAndExit: Disposing main view model...");
                _mainViewModel?.Dispose();

                Log("ForceCleanupAndExit: Cleanup complete");
            }
            catch (Exception ex)
            {
                Log($"ForceCleanupAndExit: Error during cleanup: {ex.Message}");
            }
        });

        // Wait max 3 seconds for cleanup, then force exit
        if (!cleanupTask.Wait(TimeSpan.FromSeconds(3)))
        {
            Log("ForceCleanupAndExit: Cleanup timed out, forcing exit...");
        }

        // Hide tray icon (must be on UI thread but don't wait)
        try
        {
            if (_trayIcon != null)
            {
                _trayIcon.IsVisible = false;
            }
        }
        catch { }

        Log("ForceCleanupAndExit: Exiting process...");
        Environment.Exit(0);
    }

    private static void Log(string message)
    {
        var logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            "wispr_log.txt");
        var line = $"[{DateTime.Now:HH:mm:ss.fff}] [App] {message}";
        try { File.AppendAllText(logPath, line + Environment.NewLine); } catch { }
    }
}
