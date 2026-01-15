using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.IO;
using WisprClone.Infrastructure.Keyboard;
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
    private OverlayWindow? _overlayWindow;
    private SettingsWindow? _settingsWindow;

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

        // Initialize main view model
        _mainViewModel = _serviceProvider.GetRequiredService<MainViewModel>();

        // Subscribe to events
        _mainViewModel.ShowOverlayRequested += OnShowOverlayRequested;
        _mainViewModel.HideOverlayRequested += OnHideOverlayRequested;
        _mainViewModel.OpenSettingsRequested += OnOpenSettingsRequested;

        try
        {
            await _mainViewModel.InitializeAsync();
        }
        catch (Exception ex)
        {
            Log($"Failed to initialize: {ex.Message}");
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.Shutdown(1);
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

        base.OnFrameworkInitializationCompleted();
    }

    private void OnShowOverlayRequested(object? sender, EventArgs e)
    {
        _overlayWindow?.Show();
    }

    private void OnHideOverlayRequested(object? sender, EventArgs e)
    {
        _overlayWindow?.Hide();
    }

    private void OnOpenSettingsRequested(object? sender, EventArgs e)
    {
        if (_settingsWindow != null && _settingsWindow.IsVisible)
        {
            _settingsWindow.Activate();
            return;
        }

        var settingsService = _serviceProvider.GetRequiredService<ISettingsService>();
        _settingsWindow = new SettingsWindow(settingsService);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
    }

    private void ConfigureServices(IServiceCollection services)
    {
        // Settings (cross-platform)
        services.AddSingleton<ISettingsService, SettingsService>();

        // Clipboard service (cross-platform via Avalonia)
        services.AddSingleton<IClipboardService, AvaloniaClipboardService>();

        // Global keyboard hook (cross-platform via SharpHook)
        services.AddSingleton<IGlobalKeyboardHook, SharpHookKeyboardHook>();

        // Speech services
        services.AddSingleton<AzureSpeechRecognitionService>();
        services.AddSingleton<OpenAIWhisperSpeechRecognitionService>();

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
            Menu = CreateTrayMenu(trayViewModel)
        };

        _trayIcon.Clicked += (_, _) => trayViewModel.ToggleOverlayCommand.Execute(null);

        TrayIcon.SetIcons(this, new TrayIcons { _trayIcon });
    }

    private NativeMenu CreateTrayMenu(SystemTrayViewModel viewModel)
    {
        var menu = new NativeMenu();

        var showItem = new NativeMenuItem("Show Overlay");
        showItem.Click += (_, _) => viewModel.ShowOverlayCommand.Execute(null);
        menu.Items.Add(showItem);

        var hideItem = new NativeMenuItem("Hide Overlay");
        hideItem.Click += (_, _) => viewModel.HideOverlayCommand.Execute(null);
        menu.Items.Add(hideItem);

        menu.Items.Add(new NativeMenuItemSeparator());

        var settingsItem = new NativeMenuItem("Settings...");
        settingsItem.Click += (_, _) => viewModel.OpenSettingsCommand.Execute(null);
        menu.Items.Add(settingsItem);

        menu.Items.Add(new NativeMenuItemSeparator());

        var exitItem = new NativeMenuItem("Exit");
        exitItem.Click += (_, _) =>
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.Shutdown();
            }
        };
        menu.Items.Add(exitItem);

        return menu;
    }

    private Stream GetIconStream(string iconName)
    {
        var uri = new Uri($"avares://WisprClone/Resources/Icons/{iconName}");
        return AssetLoader.Open(uri);
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
