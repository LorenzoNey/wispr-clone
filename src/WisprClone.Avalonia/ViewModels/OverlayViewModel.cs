using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
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
/// Represents a TTS provider option for the dropdown.
/// </summary>
public class TtsProviderOption
{
    public TtsProvider Provider { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public override string ToString() => DisplayName;
}

/// <summary>
/// Represents a TTS voice option for the dropdown.
/// </summary>
public class TtsVoiceOption
{
    public string VoiceId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public override string ToString() => DisplayName;
}

/// <summary>
/// ViewModel for the overlay window (Avalonia version).
/// </summary>
public partial class OverlayViewModel : ViewModelBase
{
    private readonly ISpeechRecognitionService _speechService;
    private readonly ITextToSpeechService _ttsService;
    private readonly ISettingsService _settingsService;
    private readonly ILoggingService _loggingService;
    private readonly AudioLevelMonitor _audioMonitor;
    private readonly DispatcherTimer _elapsedTimer;
    private readonly DispatcherTimer _temporaryMessageTimer;
    private DateTime _recordingStartTime;
    private string _savedTranscriptionText = string.Empty;

    /// <summary>
    /// Raised when the overlay requests to be hidden.
    /// </summary>
    public event EventHandler? HideRequested;

    /// <summary>
    /// Raised when TTS pause/resume is requested.
    /// </summary>
    public event EventHandler? TtsPauseResumeRequested;

    /// <summary>
    /// Raised when TTS stop is requested.
    /// </summary>
    public event EventHandler? TtsStopRequested;

    /// <summary>
    /// Raised when TTS run is requested (from the run button).
    /// </summary>
    public event EventHandler? TtsRunRequested;

    // Avalonia brush colors
    private static readonly IBrush GrayBrush = new SolidColorBrush(Color.Parse("#808080"));
    private static readonly IBrush YellowBrush = new SolidColorBrush(Color.Parse("#FFFF00"));
    private static readonly IBrush LimeGreenBrush = new SolidColorBrush(Color.Parse("#32CD32"));
    private static readonly IBrush OrangeBrush = new SolidColorBrush(Color.Parse("#FFA500"));
    private static readonly IBrush RedBrush = new SolidColorBrush(Color.Parse("#FF0000"));

    [ObservableProperty]
    private string _transcriptionText = "Ctrl+Ctrl: STT | Shift+Shift: TTS";

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

    // TTS dropdown collections
    [ObservableProperty]
    private ObservableCollection<TtsProviderOption> _availableTtsProviders = new();

    [ObservableProperty]
    private ObservableCollection<TtsVoiceOption> _availableTtsVoices = new();

    [ObservableProperty]
    private TtsProviderOption? _selectedTtsProvider;

    [ObservableProperty]
    private TtsVoiceOption? _selectedTtsVoice;

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

    // TTS-related properties
    [ObservableProperty]
    private bool _isTtsSpeaking;

    [ObservableProperty]
    private bool _isTtsPaused;

    [ObservableProperty]
    private bool _ttsSupportsPause;

    /// <summary>
    /// Returns true if the app is idle (not listening and not speaking).
    /// </summary>
    public bool IsIdle => !IsListening && !IsTtsSpeaking && CurrentAppMode == AppMode.Idle;

    /// <summary>
    /// Returns true if the TTS pause button should be shown (speaking and provider supports pause).
    /// </summary>
    public bool ShowTtsPauseButton => IsTtsSpeaking && TtsSupportsPause;

    [ObservableProperty]
    private ObservableCollection<TtsWordViewModel> _ttsWords = new();

    [ObservableProperty]
    private int _currentWordIndex = -1;

    // Pre-computed word position lookup for O(1) highlighting
    private int[] _wordStartPositions = Array.Empty<int>();

    [ObservableProperty]
    private AppMode _currentAppMode = AppMode.Idle;

    // Mode indicator text
    public string ModeIndicator => CurrentAppMode switch
    {
        AppMode.SttListening => "Listening...",
        AppMode.SttProcessing => "Processing...",
        AppMode.TtsSpeaking => "Speaking...",
        _ => "Ready"
    };

    // Hotkey hint text
    public string HotkeyHint => CurrentAppMode switch
    {
        AppMode.Idle => "Ctrl+Ctrl to speak | Shift+Shift to read",
        AppMode.SttListening => "Ctrl+Ctrl to stop",
        AppMode.TtsSpeaking => "Shift+Shift to stop",
        _ => "Ctrl+Ctrl to toggle"
    };

    public OverlayViewModel(ISpeechRecognitionService speechService, ITextToSpeechService ttsService, ISettingsService settingsService, ILoggingService loggingService)
    {
        _speechService = speechService;
        _ttsService = ttsService;
        _settingsService = settingsService;
        _loggingService = loggingService;
        _audioMonitor = new AudioLevelMonitor();

        // Initialize elapsed time timer
        _elapsedTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _elapsedTimer.Tick += OnElapsedTimerTick;

        // Initialize temporary message timer
        _temporaryMessageTimer = new DispatcherTimer();
        _temporaryMessageTimer.Tick += OnTemporaryMessageTimerTick;

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

        // Initialize provider options using centralized helper
        var providerOptions = new ObservableCollection<ProviderOption>();
        foreach (var (provider, _, shortName) in ProviderHelper.GetAvailableSpeechProviders())
        {
            providerOptions.Add(new ProviderOption { Provider = provider, DisplayName = shortName });
        }
        AvailableProviders = providerOptions;

        // Initialize TTS provider options using centralized helper
        var ttsProviderOptions = new ObservableCollection<TtsProviderOption>();
        foreach (var (provider, _, shortName) in ProviderHelper.GetAvailableTtsProviders())
        {
            ttsProviderOptions.Add(new TtsProviderOption { Provider = provider, DisplayName = shortName });
        }
        AvailableTtsProviders = ttsProviderOptions;

        // Set initial selections based on actual active provider (may differ from settings if fallback occurred)
        SelectedLanguage = AvailableLanguages.FirstOrDefault(l => l.Code == settingsService.Current.RecognitionLanguage)
                           ?? AvailableLanguages.First();

        // Use active provider from service manager if available (handles fallback case)
        var activeSpeechProvider = (speechService as Services.Speech.SpeechServiceManager)?.ActiveProvider
                                   ?? settingsService.Current.SpeechProvider;
        var activeTtsProvider = (ttsService as Services.Tts.TtsServiceManager)?.ActiveProvider
                                ?? settingsService.Current.TtsProvider;

        SelectedProvider = AvailableProviders.FirstOrDefault(p => p.Provider == activeSpeechProvider)
                           ?? AvailableProviders.First();
        SelectedTtsProvider = AvailableTtsProviders.FirstOrDefault(p => p.Provider == activeTtsProvider)
                              ?? AvailableTtsProviders.First();

        // Initialize TTS voices based on active provider
        UpdateTtsVoicesForProvider(activeTtsProvider);

        // Subscribe to speech service events
        _speechService.RecognitionPartial += OnRecognitionPartial;
        _speechService.RecognitionCompleted += OnRecognitionCompleted;
        _speechService.StateChanged += OnStateChanged;
        _speechService.RecognitionError += OnRecognitionError;
        _speechService.LanguageChanged += OnLanguageChanged;

        // Subscribe to TTS service events
        _ttsService.WordBoundary += OnTtsWordBoundary;
        _ttsService.SpeakProgress += OnTtsSpeakProgress;

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

    /// <summary>
    /// Called when SelectedTtsProvider changes - updates settings and voice list.
    /// </summary>
    partial void OnSelectedTtsProviderChanged(TtsProviderOption? value)
    {
        if (value == null || _isUpdatingFromExternal) return;

        _settingsService.Update(s => s.TtsProvider = value.Provider);
        UpdateTtsVoicesForProvider(value.Provider);
    }

    /// <summary>
    /// Called when SelectedTtsVoice changes - updates settings immediately.
    /// </summary>
    partial void OnSelectedTtsVoiceChanged(TtsVoiceOption? value)
    {
        if (value == null || _isUpdatingFromExternal) return;

        var provider = SelectedTtsProvider?.Provider ?? TtsProvider.Offline;
        switch (provider)
        {
            case TtsProvider.OpenAI:
                _settingsService.Update(s => s.OpenAITtsVoice = value.VoiceId);
                break;
            case TtsProvider.Azure:
                _settingsService.Update(s => s.AzureTtsVoice = value.VoiceId);
                break;
            case TtsProvider.Piper:
                _settingsService.Update(s => s.PiperVoicePath = value.VoiceId);
                break;
            case TtsProvider.Offline:
            case TtsProvider.MacOSNative:
                _settingsService.Update(s => s.TtsVoice = value.VoiceId);
                break;
        }
    }

    /// <summary>
    /// Updates the available TTS voices based on the selected provider.
    /// </summary>
    private void UpdateTtsVoicesForProvider(TtsProvider provider)
    {
        AvailableTtsVoices.Clear();

        switch (provider)
        {
            case TtsProvider.OpenAI:
                // OpenAI TTS voices
                AvailableTtsVoices.Add(new TtsVoiceOption { VoiceId = "alloy", DisplayName = "Alloy" });
                AvailableTtsVoices.Add(new TtsVoiceOption { VoiceId = "echo", DisplayName = "Echo" });
                AvailableTtsVoices.Add(new TtsVoiceOption { VoiceId = "fable", DisplayName = "Fable" });
                AvailableTtsVoices.Add(new TtsVoiceOption { VoiceId = "onyx", DisplayName = "Onyx" });
                AvailableTtsVoices.Add(new TtsVoiceOption { VoiceId = "nova", DisplayName = "Nova" });
                AvailableTtsVoices.Add(new TtsVoiceOption { VoiceId = "shimmer", DisplayName = "Shimmer" });
                SelectedTtsVoice = AvailableTtsVoices.FirstOrDefault(v => v.VoiceId == _settingsService.Current.OpenAITtsVoice)
                                   ?? AvailableTtsVoices.First();
                break;

            case TtsProvider.Azure:
                // Azure Neural TTS voices (common ones)
                AvailableTtsVoices.Add(new TtsVoiceOption { VoiceId = "en-US-JennyNeural", DisplayName = "Jenny (US)" });
                AvailableTtsVoices.Add(new TtsVoiceOption { VoiceId = "en-US-GuyNeural", DisplayName = "Guy (US)" });
                AvailableTtsVoices.Add(new TtsVoiceOption { VoiceId = "en-GB-RyanNeural", DisplayName = "Ryan (UK)" });
                AvailableTtsVoices.Add(new TtsVoiceOption { VoiceId = "en-GB-SoniaNeural", DisplayName = "Sonia (UK)" });
                AvailableTtsVoices.Add(new TtsVoiceOption { VoiceId = "en-AU-NatashaNeural", DisplayName = "Natasha (AU)" });
                SelectedTtsVoice = AvailableTtsVoices.FirstOrDefault(v => v.VoiceId == _settingsService.Current.AzureTtsVoice)
                                   ?? AvailableTtsVoices.First();
                break;

            case TtsProvider.Piper:
                // Load installed Piper voices from disk
                LoadPiperVoices();
                break;

            case TtsProvider.Offline:
            case TtsProvider.MacOSNative:
            default:
                // System voices - show default option
                AvailableTtsVoices.Add(new TtsVoiceOption { VoiceId = "", DisplayName = "System Default" });
                SelectedTtsVoice = AvailableTtsVoices.First();
                break;
        }
    }

    /// <summary>
    /// Loads installed Piper voices from the voices directory.
    /// </summary>
    private void LoadPiperVoices()
    {
        var voicesDir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "piper", "voices");

        if (System.IO.Directory.Exists(voicesDir))
        {
            var onnxFiles = System.IO.Directory.GetFiles(voicesDir, "*.onnx")
                .Where(f => !f.EndsWith(".onnx.json"))
                .ToList();

            foreach (var file in onnxFiles)
            {
                var fileName = System.IO.Path.GetFileNameWithoutExtension(file);
                var relativePath = $"voices/{System.IO.Path.GetFileName(file)}";

                // Parse voice name: en_US-amy-medium -> Language: en_US, Name: amy, Quality: medium
                var parts = fileName.Split('-');
                var name = parts.Length > 1 ? parts[1] : fileName;
                var quality = parts.Length > 2 ? parts[2] : "medium";
                var displayName = $"{char.ToUpper(name[0])}{name[1..]} ({quality})";

                AvailableTtsVoices.Add(new TtsVoiceOption { VoiceId = relativePath, DisplayName = displayName });
            }
        }

        // Add default if no voices found
        if (AvailableTtsVoices.Count == 0)
        {
            AvailableTtsVoices.Add(new TtsVoiceOption { VoiceId = "voices/en_US-amy-medium.onnx", DisplayName = "Amy (medium) - Not Installed" });
        }

        // Select current voice or first available
        SelectedTtsVoice = AvailableTtsVoices.FirstOrDefault(v => v.VoiceId == _settingsService.Current.PiperVoicePath)
                           ?? AvailableTtsVoices.First();
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

                // Sync TTS provider
                var newTtsProvider = AvailableTtsProviders.FirstOrDefault(p => p.Provider == settings.TtsProvider);
                if (newTtsProvider != null && newTtsProvider != SelectedTtsProvider)
                {
                    SelectedTtsProvider = newTtsProvider;
                    UpdateTtsVoicesForProvider(settings.TtsProvider);
                }
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
        var preview = e.Text?.Length > 50 ? e.Text.Substring(0, 50) + "..." : e.Text ?? "";
        _loggingService.Log("OverlayVM", $"OnRecognitionPartial received: '{preview}' (length={e.Text?.Length ?? 0})");

        // Use InvokeAsync with high priority for immediate UI update
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            var newText = string.IsNullOrEmpty(e.Text) ? "Listening..." : e.Text;
            var uiPreview = newText.Length > 50 ? newText.Substring(0, 50) + "..." : newText;
            _loggingService.Log("OverlayVM", $"Setting TranscriptionText on UI thread: '{uiPreview}'");
            TranscriptionText = newText;
        }, DispatcherPriority.Send); // Highest priority for immediate update
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
            RecognitionState.Idle => ("Ready", GrayBrush, false),
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

    #region TTS Methods

    /// <summary>
    /// Sets the current application mode and updates UI accordingly.
    /// </summary>
    public void SetAppMode(AppMode mode)
    {
        DispatchToUI(() =>
        {
            CurrentAppMode = mode;
            OnPropertyChanged(nameof(ModeIndicator));
            OnPropertyChanged(nameof(HotkeyHint));
        });
    }

    /// <summary>
    /// Toggles TTS pause/resume state.
    /// </summary>
    public void ToggleTtsPauseResume()
    {
        TtsPauseResumeRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Stops TTS playback.
    /// </summary>
    public void StopTts()
    {
        TtsStopRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Requests TTS to start (reads from clipboard).
    /// If STT is active, it will be stopped first.
    /// </summary>
    public void RunTts()
    {
        TtsRunRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Sets whether the TTS provider supports pause.
    /// </summary>
    public void SetTtsSupportsPause(bool supportsPause)
    {
        DispatchToUI(() =>
        {
            TtsSupportsPause = supportsPause;
        });
    }

    /// <summary>
    /// Sets the TTS paused state.
    /// </summary>
    public void SetTtsPaused(bool isPaused)
    {
        DispatchToUI(() =>
        {
            IsTtsPaused = isPaused;
            if (IsTtsSpeaking)
            {
                StatusText = isPaused ? "Paused" : "Speaking...";
            }
        });
    }

    /// <summary>
    /// Starts TTS display mode with the given text.
    /// </summary>
    public void StartTtsDisplay(string text)
    {
        DispatchToUI(() =>
        {
            // Save current transcription text
            _savedTranscriptionText = TranscriptionText;

            // Clear any existing highlights
            foreach (var word in TtsWords)
            {
                word.IsCurrentWord = false;
            }

            // Reset state
            CurrentWordIndex = -1;
            _wordStartPositions = Array.Empty<int>();
            IsTtsPaused = false;

            // Parse text into words
            TtsWords.Clear();
            ParseTextIntoWords(text);

            // Switch to TTS mode
            IsTtsSpeaking = true;
            StatusText = "Speaking...";
            StatusColor = LimeGreenBrush;
        });
    }

    /// <summary>
    /// Stops TTS display mode (called when manually stopped by user).
    /// Keeps the spoken text visible in the overlay.
    /// </summary>
    public void StopTtsDisplay(string? spokenText = null)
    {
        DispatchToUI(() =>
        {
            // Clear highlights
            foreach (var word in TtsWords)
            {
                word.IsCurrentWord = false;
            }

            IsTtsSpeaking = false;
            IsTtsPaused = false;
            CurrentWordIndex = -1;
            TtsWords.Clear();
            _wordStartPositions = Array.Empty<int>();

            // Keep the spoken text visible (if provided), otherwise keep current text
            if (!string.IsNullOrEmpty(spokenText))
            {
                TranscriptionText = spokenText;
            }
            // If no text provided and we have TTS words, reconstruct the text
            // Otherwise just keep the current TranscriptionText as-is

            StatusText = "Ready";
            StatusColor = GrayBrush;
        });
    }

    /// <summary>
    /// Called when TTS completes naturally (not manually stopped).
    /// Keeps the text visible in the overlay.
    /// </summary>
    public void OnTtsCompleted(string spokenText)
    {
        DispatchToUI(() =>
        {
            IsTtsSpeaking = false;
            CurrentWordIndex = -1;
            TtsWords.Clear();
            _wordStartPositions = Array.Empty<int>();

            // Keep the spoken text visible (don't restore old text)
            TranscriptionText = spokenText;
            StatusText = "Ready";
            StatusColor = GrayBrush;
        });
    }

    /// <summary>
    /// Highlights the last word in the TTS display.
    /// Called when TTS completes to ensure the last word is shown.
    /// </summary>
    public void HighlightLastWord()
    {
        DispatchToUI(() =>
        {
            if (TtsWords.Count == 0) return;

            // Clear previous highlight
            if (CurrentWordIndex >= 0 && CurrentWordIndex < TtsWords.Count)
            {
                TtsWords[CurrentWordIndex].IsCurrentWord = false;
            }

            // Find and highlight the last non-whitespace word
            for (int i = TtsWords.Count - 1; i >= 0; i--)
            {
                if (!TtsWords[i].IsWhitespace)
                {
                    TtsWords[i].IsCurrentWord = true;
                    CurrentWordIndex = i;
                    break;
                }
            }
        });
    }

    /// <summary>
    /// Shows a temporary message in the overlay.
    /// </summary>
    public void ShowTemporaryMessage(string message, int durationMs)
    {
        DispatchToUI(() =>
        {
            _savedTranscriptionText = TranscriptionText;
            TranscriptionText = message;

            _temporaryMessageTimer.Interval = TimeSpan.FromMilliseconds(durationMs);
            _temporaryMessageTimer.Start();
        });
    }

    private void OnTemporaryMessageTimerTick(object? sender, EventArgs e)
    {
        _temporaryMessageTimer.Stop();
        TranscriptionText = _savedTranscriptionText;
    }

    /// <summary>
    /// Parses text into individual words for TTS highlighting.
    /// Preserves line breaks (CR/LF) from the original text.
    /// </summary>
    private void ParseTextIntoWords(string text)
    {
        var startPositions = new List<int>();
        int position = 0;

        // Split text by lines first to preserve line breaks
        var lines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

        for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            var line = lines[lineIndex];

            // Parse words in this line
            var matches = Regex.Matches(line, @"(\S+)(\s*)");

            foreach (Match match in matches)
            {
                var word = match.Groups[1].Value;
                var whitespace = match.Groups[2].Value;

                // Add the word with trailing whitespace
                TtsWords.Add(new TtsWordViewModel(word + whitespace, position + match.Index, match.Length));
                startPositions.Add(position + match.Index);
            }

            // Update position for next line (account for line length + newline chars)
            position += line.Length;

            // Add line break marker between lines (not after the last line)
            if (lineIndex < lines.Length - 1)
            {
                // Determine newline length (1 for \n or \r, 2 for \r\n)
                int newlineLength = 1;
                if (lineIndex < lines.Length - 1)
                {
                    // Check original text for \r\n
                    int originalPos = 0;
                    for (int i = 0; i <= lineIndex; i++)
                    {
                        originalPos += lines[i].Length;
                        if (i < lineIndex) originalPos += (text.Length > originalPos && text[originalPos] == '\r' && text.Length > originalPos + 1 && text[originalPos + 1] == '\n') ? 2 : 1;
                    }
                    if (text.Length > originalPos && text[originalPos] == '\r' && text.Length > originalPos + 1 && text[originalPos + 1] == '\n')
                        newlineLength = 2;
                }

                TtsWords.Add(new TtsWordViewModel("\n", position, newlineLength, isWhitespace: true, isLineBreak: true));
                startPositions.Add(position);
                position += newlineLength;
            }
        }

        // Store sorted start positions for binary search
        _wordStartPositions = startPositions.ToArray();
    }

    /// <summary>
    /// Handles word boundary events from TTS service.
    /// Uses binary search for O(log n) lookup and immediate UI dispatch.
    /// </summary>
    private void OnTtsWordBoundary(object? sender, WordBoundaryEventArgs e)
    {
        // Find word index using binary search for fast lookup
        var wordIndex = FindWordIndexByPosition(e.CharacterPosition);
        if (wordIndex < 0) return;

        // Use InvokeAsync for immediate execution instead of Post
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (wordIndex >= TtsWords.Count) return;

            // Clear previous highlight
            if (CurrentWordIndex >= 0 && CurrentWordIndex < TtsWords.Count)
            {
                TtsWords[CurrentWordIndex].IsCurrentWord = false;
            }

            // Set new highlight
            TtsWords[wordIndex].IsCurrentWord = true;
            CurrentWordIndex = wordIndex;
        }, Avalonia.Threading.DispatcherPriority.Send); // Highest priority
    }

    /// <summary>
    /// Binary search to find word index containing the character position.
    /// </summary>
    private int FindWordIndexByPosition(int charPosition)
    {
        if (_wordStartPositions.Length == 0) return -1;

        int left = 0;
        int right = _wordStartPositions.Length - 1;

        while (left < right)
        {
            int mid = left + (right - left + 1) / 2;
            if (_wordStartPositions[mid] <= charPosition)
            {
                left = mid;
            }
            else
            {
                right = mid - 1;
            }
        }

        // Verify the found word contains the position
        if (left < TtsWords.Count)
        {
            var word = TtsWords[left];
            if (charPosition >= word.StartIndex && charPosition < word.StartIndex + word.Length)
            {
                return left;
            }
        }

        return -1;
    }

    /// <summary>
    /// Handles speak progress events from TTS service.
    /// </summary>
    private void OnTtsSpeakProgress(object? sender, SpeechProgressEventArgs e)
    {
        // Could update a progress bar here if needed
    }

    /// <summary>
    /// Called when CurrentAppMode changes.
    /// </summary>
    partial void OnCurrentAppModeChanged(AppMode value)
    {
        OnPropertyChanged(nameof(ModeIndicator));
        OnPropertyChanged(nameof(HotkeyHint));
        OnPropertyChanged(nameof(IsIdle));
    }

    partial void OnIsTtsSpeakingChanged(bool value)
    {
        OnPropertyChanged(nameof(IsIdle));
        OnPropertyChanged(nameof(ShowTtsPauseButton));
    }

    partial void OnIsListeningChanged(bool value)
    {
        OnPropertyChanged(nameof(IsIdle));
    }

    partial void OnTtsSupportsPauseChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowTtsPauseButton));
    }

    #endregion
}
