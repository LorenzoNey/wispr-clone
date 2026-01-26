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
using WisprClone.Services.Tts;
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

        // Ensure required models are available for offline providers (fire and forget)
        EnsurePiperModelAvailableAsync();
        EnsureWhisperServerModelAvailableAsync();

        // Warm up speech models if selected (fire and forget)
        WarmupFasterWhisperIfNeeded();
        WarmupWhisperServerIfNeeded();

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
            // Ensure overlay window exists
            if (_overlayWindow == null)
            {
                Log("Creating overlay window on demand");
                _overlayWindow = new OverlayWindow
                {
                    DataContext = _mainViewModel.OverlayViewModel
                };
            }

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
        // Windows: All providers available including offline, Faster-Whisper, and Whisper Server
        services.AddSingleton<OfflineSpeechRecognitionService>();
        services.AddSingleton<FasterWhisperSpeechRecognitionService>();
        services.AddSingleton<WhisperServerSpeechRecognitionService>();
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
                sp.GetRequiredService<FasterWhisperSpeechRecognitionService>(),
                sp.GetRequiredService<WhisperServerSpeechRecognitionService>(),
                settings);
        });

        // Windows TTS services
        services.AddSingleton<OfflineTextToSpeechService>();
        services.AddSingleton<AzureTextToSpeechService>();
        services.AddSingleton<OpenAITextToSpeechService>();
        services.AddSingleton<PiperTextToSpeechService>();
        services.AddSingleton<ITextToSpeechService>(sp =>
        {
            var settings = sp.GetRequiredService<ISettingsService>();
            var logging = sp.GetRequiredService<ILoggingService>();
            logging.Log("App", $"Initial TTS provider: {settings.Current.TtsProvider}");

            return new TtsServiceManager(
                sp.GetRequiredService<OfflineTextToSpeechService>(),
                sp.GetRequiredService<AzureTextToSpeechService>(),
                sp.GetRequiredService<OpenAITextToSpeechService>(),
                sp.GetRequiredService<PiperTextToSpeechService>(),
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

        // macOS/Linux TTS services (cross-platform manager)
        services.AddSingleton<AzureTextToSpeechService>();
        services.AddSingleton<OpenAITextToSpeechService>();
        services.AddSingleton<MacOSTextToSpeechService>();
        services.AddSingleton<ITextToSpeechService>(sp =>
        {
            var settings = sp.GetRequiredService<ISettingsService>();
            var logging = sp.GetRequiredService<ILoggingService>();
            logging.Log("App", $"Initial TTS provider (non-Windows): {settings.Current.TtsProvider}");

            // macOS native TTS is only available on macOS
            MacOSTextToSpeechService? macOSTtsService = null;
            if (OperatingSystem.IsMacOS())
            {
                macOSTtsService = sp.GetRequiredService<MacOSTextToSpeechService>();
            }

            return new CrossPlatformTtsServiceManager(
                sp.GetRequiredService<AzureTextToSpeechService>(),
                sp.GetRequiredService<OpenAITextToSpeechService>(),
                macOSTtsService,
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

    private void WarmupFasterWhisperIfNeeded()
    {
        try
        {
            var settings = _serviceProvider?.GetService<ISettingsService>();
            if (settings?.Current.SpeechProvider == Core.SpeechProvider.FasterWhisper)
            {
                Log("Faster-Whisper selected, warming up model in background...");
                var fasterWhisperService = _serviceProvider?.GetService<Services.Speech.FasterWhisperSpeechRecognitionService>();
                if (fasterWhisperService?.IsAvailable == true)
                {
                    // Fire and forget - warmup in background
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await fasterWhisperService.WarmupModelAsync();
                            Log("Faster-Whisper model warmup completed");
                        }
                        catch (Exception ex)
                        {
                            Log($"Faster-Whisper warmup failed: {ex.Message}");
                        }
                    });
                }
            }
        }
        catch (Exception ex)
        {
            Log($"WarmupFasterWhisperIfNeeded error: {ex.Message}");
        }
    }

    private void WarmupWhisperServerIfNeeded()
    {
        try
        {
            var settings = _serviceProvider?.GetService<ISettingsService>();
            if (settings?.Current.SpeechProvider == Core.SpeechProvider.WhisperServer)
            {
                Log("WhisperServer selected, starting server in background...");
                var whisperServerService = _serviceProvider?.GetService<Services.Speech.WhisperServerSpeechRecognitionService>();
                if (whisperServerService?.IsAvailable == true)
                {
                    // Fire and forget - start server in background
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await whisperServerService.EnsureServerRunningAsync();
                            Log("WhisperServer started and ready");
                        }
                        catch (Exception ex)
                        {
                            Log($"WhisperServer startup failed: {ex.Message}");
                        }
                    });
                }
            }
        }
        catch (Exception ex)
        {
            Log($"WarmupWhisperServerIfNeeded error: {ex.Message}");
        }
    }

    private void Log(string message)
    {
        _loggingService?.Log("App", message);
    }

    private async void EnsurePiperModelAvailableAsync()
    {
        try
        {
            var settings = _serviceProvider?.GetService<ISettingsService>();
            if (settings?.Current.TtsProvider != Core.TtsProvider.Piper)
                return;

            var piperExePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "piper", "piper.exe");
            if (!File.Exists(piperExePath))
            {
                Log("Piper TTS not installed, skipping model check");
                return;
            }

            // Check if the configured voice file exists
            var voicePath = settings.Current.PiperVoicePath;
            if (!Path.IsPathRooted(voicePath))
            {
                voicePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "piper", voicePath);
            }

            if (File.Exists(voicePath))
            {
                Log($"Piper voice available: {voicePath}");
                return;
            }

            Log($"Piper voice not found: {voicePath}. Downloading default voice...");

            // Try to download the default voice
            var downloadHelper = new DownloadHelper(_loggingService);
            var voicesDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "piper", "voices");
            Directory.CreateDirectory(voicesDir);

            var defaultVoicePath = Path.Combine(voicesDir, "en_US-amy-medium.onnx");
            var defaultVoiceConfigPath = Path.Combine(voicesDir, "en_US-amy-medium.onnx.json");

            try
            {
                Log("Downloading default Piper voice (en_US-amy-medium)...");
                await DownloadFileAsync(DownloadHelper.DefaultPiperVoiceUrl, defaultVoicePath);
                await DownloadFileAsync(DownloadHelper.DefaultPiperVoiceConfigUrl, defaultVoiceConfigPath);

                // Update settings to use the default voice
                settings.Current.PiperVoicePath = "voices\\en_US-amy-medium.onnx";
                await settings.SaveAsync();

                Log("Default Piper voice downloaded and settings updated");
            }
            catch (Exception ex)
            {
                Log($"Failed to download default Piper voice: {ex.Message}");
                // Provider will gracefully fallback to Local in the service manager
            }
        }
        catch (Exception ex)
        {
            Log($"EnsurePiperModelAvailableAsync error: {ex.Message}");
        }
    }

    private async void EnsureWhisperServerModelAvailableAsync()
    {
        try
        {
            var settings = _serviceProvider?.GetService<ISettingsService>();
            if (settings?.Current.SpeechProvider != Core.SpeechProvider.WhisperServer)
                return;

            var serverExePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "whisper-server", "whisper-server.exe");
            if (!File.Exists(serverExePath))
            {
                Log("Whisper Server not installed, skipping model check");
                return;
            }

            // Check if the configured model exists
            var modelName = settings.Current.WhisperCppModel;
            var modelFileName = $"ggml-{modelName}.bin";
            var modelPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "whisper-server", "models", modelFileName);

            if (File.Exists(modelPath))
            {
                Log($"Whisper Server model available: {modelPath}");
                return;
            }

            Log($"Whisper Server model not found: {modelFileName}. Attempting to download...");

            var modelsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "whisper-server", "models");
            Directory.CreateDirectory(modelsDir);

            // Try to download the configured model first
            try
            {
                var modelUrl = $"https://huggingface.co/ggerganov/whisper.cpp/resolve/main/{modelFileName}";
                Log($"Downloading model: {modelName}...");
                await DownloadFileAsync(modelUrl, modelPath);
                Log($"Whisper Server model downloaded: {modelFileName}");
                return;
            }
            catch (Exception ex)
            {
                Log($"Failed to download model {modelName}: {ex.Message}");

                // If the configured model failed, try downloading the default (base.en)
                if (modelName != "base.en")
                {
                    var defaultModelPath = Path.Combine(modelsDir, "ggml-base.en.bin");
                    if (!File.Exists(defaultModelPath))
                    {
                        try
                        {
                            Log("Downloading default model (base.en)...");
                            await DownloadFileAsync(DownloadHelper.WhisperServerModelUrl, defaultModelPath);

                            // Update settings to use the default model
                            settings.Current.WhisperCppModel = "base.en";
                            await settings.SaveAsync();

                            Log("Default Whisper model downloaded and settings updated");
                        }
                        catch (Exception defaultEx)
                        {
                            Log($"Failed to download default model: {defaultEx.Message}");
                            // Provider will gracefully fallback to Local in the service manager
                        }
                    }
                    else
                    {
                        // Default model exists, switch to it
                        settings.Current.WhisperCppModel = "base.en";
                        await settings.SaveAsync();
                        Log("Switched to existing default model (base.en)");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log($"EnsureWhisperServerModelAvailableAsync error: {ex.Message}");
        }
    }

    private async Task DownloadFileAsync(string url, string destinationPath)
    {
        using var httpClient = new System.Net.Http.HttpClient();
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("WisprClone/1.0");
        httpClient.Timeout = TimeSpan.FromMinutes(30);

        Log($"Downloading: {url}");
        using var response = await httpClient.GetAsync(url, System.Net.Http.HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        await using var contentStream = await response.Content.ReadAsStreamAsync();
        await using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
        await contentStream.CopyToAsync(fileStream);

        Log($"Download complete: {destinationPath}");
    }
}
