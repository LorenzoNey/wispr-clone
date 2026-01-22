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
    private ILoggingService? _loggingService;
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

        // Initialize logging service first
        _loggingService = _serviceProvider.GetRequiredService<ILoggingService>();

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

        // Setup macOS application menu
        if (OperatingSystem.IsMacOS())
        {
            SetupMacOSMenu();
        }

        // Initialize update checking
        InitializeUpdateChecking();

        base.OnFrameworkInitializationCompleted();
    }

    private void InitializeUpdateChecking()
    {
        _updateService = _serviceProvider.GetRequiredService<IUpdateService>();
        _updateService.UpdateAvailable += OnUpdateAvailable;

        var settings = _serviceProvider.GetRequiredService<ISettingsService>();
        Log($"CheckForUpdatesAutomatically: {settings.Current.CheckForUpdatesAutomatically}");
        if (settings.Current.CheckForUpdatesAutomatically)
        {
            // Check for updates on startup (fire and forget)
            Log("Starting initial update check...");
            _ = _updateService.CheckForUpdatesAsync();

            // Start periodic checks
            Log($"Starting periodic checks every {Constants.DefaultUpdateCheckIntervalHours} hours");
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

    private void OnShowOverlayRequested(object? sender, bool activate)
    {
        try
        {
            if (_overlayWindow != null)
            {
                _overlayWindow.Show();

                if (activate)
                {
                    _overlayWindow.Activate();
                }
                else
                {
                    // Bring to foreground without stealing focus by toggling Topmost
                    // This forces Windows to re-evaluate the Z-order
                    _overlayWindow.Topmost = false;
                    _overlayWindow.Topmost = true;
                }
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

            var updateService = _serviceProvider.GetRequiredService<IUpdateService>();
            _aboutWindow = new AboutWindow(updateService);
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

        // Logging service (must be after SettingsService)
        services.AddSingleton<ILoggingService, LoggingService>();

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
            var logging = sp.GetRequiredService<ILoggingService>();
            logging.Log("App", $"Initial speech provider: {settings.Current.SpeechProvider}");

            return new SpeechServiceManager(
                sp.GetRequiredService<OfflineSpeechRecognitionService>(),
                sp.GetRequiredService<AzureSpeechRecognitionService>(),
                sp.GetRequiredService<OpenAIWhisperSpeechRecognitionService>(),
                sp.GetRequiredService<OpenAIRealtimeSpeechRecognitionService>(),
                sp.GetRequiredService<HybridSpeechRecognitionService>(),
                settings);
        });
#else
        // macOS/Linux: Cloud providers + native macOS speech
        services.AddSingleton<MacOSSpeechRecognitionService>();
        services.AddSingleton<ISpeechRecognitionService>(sp =>
        {
            var settings = sp.GetRequiredService<ISettingsService>();
            var logging = sp.GetRequiredService<ILoggingService>();
            logging.Log("App", $"Initial speech provider (non-Windows): {settings.Current.SpeechProvider}");

            // macOS native service is only available on macOS
            MacOSSpeechRecognitionService? macOSService = null;
            if (OperatingSystem.IsMacOS())
            {
                macOSService = sp.GetRequiredService<MacOSSpeechRecognitionService>();
            }

            return new CrossPlatformSpeechServiceManager(
                sp.GetRequiredService<AzureSpeechRecognitionService>(),
                sp.GetRequiredService<OpenAIWhisperSpeechRecognitionService>(),
                macOSService,
                settings,
                logging);
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

    private void SetupMacOSMenu()
    {
        // The native menu is defined in App.axaml with "About WisprClone"
        // which overrides Avalonia's default "About Avalonia" menu item.
        // Avalonia automatically provides standard macOS menu items like
        // Hide, Hide Others, Show All, Quit with proper keyboard shortcuts.
        // No additional setup needed here - just log for debugging.
        Log("macOS native menu configured via XAML");
    }

    // Handler for the About menu item defined in App.axaml
    private void AboutMenuItem_Click(object? sender, EventArgs e)
    {
        SafeExecute(() =>
        {
            var trayViewModel = _serviceProvider.GetRequiredService<SystemTrayViewModel>();
            trayViewModel.OpenAboutCommand.Execute(null);
        }, "About");
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
                if (OperatingSystem.IsMacOS())
                {
                    // macOS: Use automated update flow
                    if (_updateMenuItem != null)
                    {
                        Dispatcher.UIThread.Post(() =>
                        {
                            _updateMenuItem.Header = "Installing... Restarting...";
                        });
                    }

                    // Small delay to show the message
                    await Task.Delay(500);

                    _updateService.LaunchMacOSUpdate(installerPath, () =>
                    {
                        // Force exit immediately
                        Environment.Exit(0);
                    });
                }
                else
                {
                    // Windows/Linux: Launch installer directly
                    _updateService.LaunchInstaller(installerPath);
                }
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

    private void Log(string message)
    {
        _loggingService?.Log("App", message);
    }
}
