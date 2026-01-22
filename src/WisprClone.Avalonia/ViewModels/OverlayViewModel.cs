using System.Collections.ObjectModel;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using WisprClone.Core;
using WisprClone.Services;
using WisprClone.Services.Interfaces;
using WisprClone.ViewModels.Base;

namespace WisprClone.ViewModels;

/// <summary>
/// Represents a language option for the dropdown.
/// </summary>
public class LanguageOption
{
    public string Code { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public override string ToString() => DisplayName;
}

/// <summary>
/// Represents a speech provider option for the dropdown.
/// </summary>
public class ProviderOption
{
    public SpeechProvider Provider { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public override string ToString() => DisplayName;
}

/// <summary>
/// ViewModel for the overlay window (Avalonia version).
/// </summary>
public partial class OverlayViewModel : ViewModelBase
{
    private readonly ISpeechRecognitionService _speechService;
    private readonly ISettingsService _settingsService;
    private readonly AudioLevelMonitor _audioMonitor;
    private readonly DispatcherTimer _elapsedTimer;
    private DateTime _recordingStartTime;

    /// <summary>
    /// Raised when the overlay requests to be hidden.
    /// </summary>
    public event EventHandler? HideRequested;

    // Avalonia brush colors
    private static readonly IBrush GrayBrush = new SolidColorBrush(Color.Parse("#808080"));
    private static readonly IBrush YellowBrush = new SolidColorBrush(Color.Parse("#FFFF00"));
    private static readonly IBrush LimeGreenBrush = new SolidColorBrush(Color.Parse("#32CD32"));
    private static readonly IBrush OrangeBrush = new SolidColorBrush(Color.Parse("#FFA500"));
    private static readonly IBrush RedBrush = new SolidColorBrush(Color.Parse("#FF0000"));

    [ObservableProperty]
    private string _transcriptionText = "Press Ctrl+Ctrl to start...";

    [ObservableProperty]
    private string _statusText = "Ready";

    [ObservableProperty]
    private bool _isListening;

    [ObservableProperty]
    private IBrush _statusColor = GrayBrush;

    [ObservableProperty]
    private double _windowLeft;

    [ObservableProperty]
    private double _windowTop;

    [ObservableProperty]
    private string _providerInfo = "Local";

    [ObservableProperty]
    private string _languageDisplay = "en-US";

    [ObservableProperty]
    private bool _isVisible;

    // Dropdown collections for fast switching
    [ObservableProperty]
    private ObservableCollection<LanguageOption> _availableLanguages = new();

    [ObservableProperty]
    private ObservableCollection<ProviderOption> _availableProviders = new();

    [ObservableProperty]
    private LanguageOption? _selectedLanguage;

    [ObservableProperty]
    private ProviderOption? _selectedProvider;

    private bool _isUpdatingFromExternal;

    // Track if user is interacting with the overlay
    [ObservableProperty]
    private bool _isDropdownOpen;

    [ObservableProperty]
    private bool _isMouseOverWindow;

    /// <summary>
    /// Returns true if the user is interacting with the overlay (mouse over or dropdown open).
    /// </summary>
    public bool IsUserInteracting => IsMouseOverWindow || IsDropdownOpen;

    // Audio level properties for waveform visualization (0-20 range for bar heights)
    [ObservableProperty]
    private double _audioLevel1 = 4;

    [ObservableProperty]
    private double _audioLevel2 = 4;

    [ObservableProperty]
    private double _audioLevel3 = 4;

    [ObservableProperty]
    private double _audioLevel4 = 4;

    [ObservableProperty]
    private double _audioLevel5 = 4;

    // Elapsed recording time display (e.g., "0:00", "1:23")
    [ObservableProperty]
    private string _elapsedTimeDisplay = "0:00";

    [ObservableProperty]
    private bool _showElapsedTime;

    public OverlayViewModel(ISpeechRecognitionService speechService, ISettingsService settingsService)
    {
        _speechService = speechService;
        _settingsService = settingsService;
        _audioMonitor = new AudioLevelMonitor();

        // Initialize elapsed time timer
        _elapsedTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _elapsedTimer.Tick += OnElapsedTimerTick;

        // Load saved position
        WindowLeft = settingsService.Current.OverlayLeft;
        WindowTop = settingsService.Current.OverlayTop;

        // Initialize language options
        AvailableLanguages = new ObservableCollection<LanguageOption>
        {
            new() { Code = "en-US", DisplayName = "English (US)" },
            new() { Code = "en-GB", DisplayName = "English (UK)" },
            new() { Code = "ro-RO", DisplayName = "Romanian" },
            new() { Code = "de-DE", DisplayName = "German" },
            new() { Code = "fr-FR", DisplayName = "French" },
            new() { Code = "es-ES", DisplayName = "Spanish" },
            new() { Code = "it-IT", DisplayName = "Italian" },
            new() { Code = "pt-BR", DisplayName = "Portuguese" },
            new() { Code = "zh-CN", DisplayName = "Chinese" },
            new() { Code = "ja-JP", DisplayName = "Japanese" }
        };

        // Initialize provider options (OpenAI Realtime temporarily disabled due to cost/quality)
        var providerOptions = new ObservableCollection<ProviderOption>
        {
            new() { Provider = SpeechProvider.Azure, DisplayName = "Azure Speech" },
            new() { Provider = SpeechProvider.OpenAI, DisplayName = "OpenAI Whisper" }
        };

        // Add platform-specific local speech options
        if (OperatingSystem.IsWindows())
        {
            providerOptions.Insert(0, new ProviderOption { Provider = SpeechProvider.Offline, DisplayName = "Local" });
        }
        else if (OperatingSystem.IsMacOS())
        {
            providerOptions.Insert(0, new ProviderOption { Provider = SpeechProvider.MacOSNative, DisplayName = "Local" });
        }

        AvailableProviders = providerOptions;

        // Set initial selections based on current settings
        SelectedLanguage = AvailableLanguages.FirstOrDefault(l => l.Code == settingsService.Current.RecognitionLanguage)
                           ?? AvailableLanguages.First();
        SelectedProvider = AvailableProviders.FirstOrDefault(p => p.Provider == settingsService.Current.SpeechProvider)
                           ?? AvailableProviders.First();

        // Subscribe to speech service events
        _speechService.RecognitionPartial += OnRecognitionPartial;
        _speechService.RecognitionCompleted += OnRecognitionCompleted;
        _speechService.StateChanged += OnStateChanged;
        _speechService.RecognitionError += OnRecognitionError;
        _speechService.LanguageChanged += OnLanguageChanged;

        // Subscribe to audio level events
        _audioMonitor.LevelChanged += OnAudioLevelChanged;

        // Subscribe to settings changes for external sync
        _settingsService.SettingsChanged += OnSettingsChangedExternally;

        ProviderInfo = speechService.ProviderName;
        LanguageDisplay = GetLanguageDisplayName(speechService.CurrentLanguage);
    }

    /// <summary>
    /// Called when SelectedLanguage changes - updates settings immediately.
    /// </summary>
    partial void OnSelectedLanguageChanged(LanguageOption? value)
    {
        if (value == null || _isUpdatingFromExternal) return;

        _settingsService.Update(s => s.RecognitionLanguage = value.Code);
        LanguageDisplay = value.DisplayName;
    }

    /// <summary>
    /// Called when SelectedProvider changes - updates settings immediately.
    /// </summary>
    partial void OnSelectedProviderChanged(ProviderOption? value)
    {
        if (value == null || _isUpdatingFromExternal) return;

        _settingsService.Update(s => s.SpeechProvider = value.Provider);
    }

    private void OnSettingsChangedExternally(object? sender, Models.AppSettings settings)
    {
        DispatchToUI(() =>
        {
            _isUpdatingFromExternal = true;
            try
            {
                var newLang = AvailableLanguages.FirstOrDefault(l => l.Code == settings.RecognitionLanguage);
                if (newLang != null && newLang != SelectedLanguage)
                    SelectedLanguage = newLang;

                var newProvider = AvailableProviders.FirstOrDefault(p => p.Provider == settings.SpeechProvider);
                if (newProvider != null && newProvider != SelectedProvider)
                    SelectedProvider = newProvider;
            }
            finally
            {
                _isUpdatingFromExternal = false;
            }
        });
    }

    private void OnLanguageChanged(object? sender, string newLanguage)
    {
        DispatchToUI(() =>
        {
            LanguageDisplay = GetLanguageDisplayName(newLanguage);
        });
    }

    private static string GetLanguageDisplayName(string languageCode)
    {
        return languageCode switch
        {
            "en-US" => "English (US)",
            "en-GB" => "English (UK)",
            "ro-RO" => "Romanian",
            "de-DE" => "German",
            "fr-FR" => "French",
            "es-ES" => "Spanish",
            "it-IT" => "Italian",
            "pt-BR" => "Portuguese",
            "zh-CN" => "Chinese",
            "ja-JP" => "Japanese",
            _ => languageCode
        };
    }

    private void OnAudioLevelChanged(object? sender, AudioLevelEventArgs e)
    {
        DispatchToUI(() =>
        {
            // Convert 0-1 range to 4-20 pixel height range
            const double minHeight = 4;
            const double maxHeight = 20;
            double range = maxHeight - minHeight;

            AudioLevel1 = minHeight + (e.Level1 * range);
            AudioLevel2 = minHeight + (e.Level2 * range);
            AudioLevel3 = minHeight + (e.Level3 * range);
            AudioLevel4 = minHeight + (e.Level4 * range);
            AudioLevel5 = minHeight + (e.Level5 * range);
        });
    }

    private void OnRecognitionPartial(object? sender, TranscriptionEventArgs e)
    {
        DispatchToUI(() =>
        {
            TranscriptionText = string.IsNullOrEmpty(e.Text) ? "Listening..." : e.Text;
        });
    }

    private void OnRecognitionCompleted(object? sender, TranscriptionEventArgs e)
    {
        DispatchToUI(() =>
        {
            TranscriptionText = string.IsNullOrEmpty(e.Text) ? "No speech detected" : e.Text;
        });
    }

    private void OnStateChanged(object? sender, RecognitionStateChangedEventArgs e)
    {
        DispatchToUI(() =>
        {
            UpdateUIForState(e.NewState);
            ProviderInfo = _speechService.ProviderName;
        });
    }

    private void OnRecognitionError(object? sender, RecognitionErrorEventArgs e)
    {
        DispatchToUI(() =>
        {
            StatusText = $"Error: {e.Message}";
            StatusColor = RedBrush;
            ProviderInfo = _speechService.ProviderName;
        });
    }

    private void UpdateUIForState(RecognitionState state)
    {
        var wasListening = IsListening;

        (StatusText, StatusColor, IsListening) = state switch
        {
            RecognitionState.Idle => ("Ready - Press Ctrl+Ctrl", GrayBrush, false),
            RecognitionState.Initializing => ("Initializing...", YellowBrush, false),
            RecognitionState.Listening => ("Listening...", LimeGreenBrush, true),
            RecognitionState.Processing => ("Processing...", OrangeBrush, false),
            RecognitionState.Error => ("Error", RedBrush, false),
            _ => ("Unknown", GrayBrush, false)
        };

        // Start/stop audio level monitoring and elapsed timer based on listening state
        if (IsListening && !wasListening)
        {
            _audioMonitor.Start();
            TranscriptionText = "Listening...";

            // Start elapsed time tracking
            _recordingStartTime = DateTime.Now;
            ElapsedTimeDisplay = "0:00";
            ShowElapsedTime = true;
            _elapsedTimer.Start();
        }
        else if (!IsListening && wasListening)
        {
            _audioMonitor.Stop();

            // Stop elapsed time tracking
            _elapsedTimer.Stop();
            ShowElapsedTime = false;
        }
    }

    private void OnElapsedTimerTick(object? sender, EventArgs e)
    {
        var elapsed = DateTime.Now - _recordingStartTime;
        ElapsedTimeDisplay = elapsed.TotalHours >= 1
            ? $"{(int)elapsed.TotalHours}:{elapsed.Minutes:D2}:{elapsed.Seconds:D2}"
            : $"{(int)elapsed.TotalMinutes}:{elapsed.Seconds:D2}";
    }

    /// <summary>
    /// Saves the current window position to settings.
    /// </summary>
    public void SavePosition()
    {
        _settingsService.Update(s =>
        {
            s.OverlayLeft = WindowLeft;
            s.OverlayTop = WindowTop;
        });
    }

    /// <summary>
    /// Shows the overlay.
    /// </summary>
    public void Show()
    {
        IsVisible = true;
    }

    /// <summary>
    /// Hides the overlay.
    /// </summary>
    public void Hide()
    {
        IsVisible = false;
        HideRequested?.Invoke(this, EventArgs.Empty);
    }

    private static void DispatchToUI(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
        }
        else
        {
            Dispatcher.UIThread.Post(action);
        }
    }
}
