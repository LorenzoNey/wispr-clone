using WisprClone.Core;
using WisprClone.Services.Interfaces;

namespace WisprClone.Services.Speech;

/// <summary>
/// Cross-platform speech service manager for non-Windows platforms.
/// Only supports cloud providers (Azure and OpenAI Whisper).
/// </summary>
public class CrossPlatformSpeechServiceManager : ISpeechRecognitionService
{
    private readonly AzureSpeechRecognitionService _azureService;
    private readonly OpenAIWhisperSpeechRecognitionService _whisperService;
    private readonly ISettingsService _settingsService;

    private ISpeechRecognitionService _activeService;
    private bool _isInitialized;
    private string _currentLanguage = "en-US";

    public event EventHandler<TranscriptionEventArgs>? RecognitionPartial;
    public event EventHandler<TranscriptionEventArgs>? RecognitionCompleted;
    public event EventHandler<RecognitionErrorEventArgs>? RecognitionError;
    public event EventHandler<RecognitionStateChangedEventArgs>? StateChanged;
    public event EventHandler<string>? LanguageChanged;

    public RecognitionState CurrentState => _activeService.CurrentState;
    public string ProviderName => _activeService.ProviderName;
    public bool IsAvailable => _activeService.IsAvailable;
    public string CurrentLanguage => _currentLanguage;

    public CrossPlatformSpeechServiceManager(
        AzureSpeechRecognitionService azureService,
        OpenAIWhisperSpeechRecognitionService whisperService,
        ISettingsService settingsService)
    {
        _azureService = azureService;
        _whisperService = whisperService;
        _settingsService = settingsService;

        // Start with Azure as default for non-Windows (more reliable than Whisper without NAudio)
        _activeService = GetServiceForProvider(settingsService.Current.SpeechProvider);

        // Wire up events
        WireEvents(_azureService);
        WireEvents(_whisperService);

        // Listen for settings changes
        _settingsService.SettingsChanged += OnSettingsChanged;
    }

    private void WireEvents(ISpeechRecognitionService service)
    {
        service.RecognitionPartial += (s, e) => { if (s == _activeService) RecognitionPartial?.Invoke(this, e); };
        service.RecognitionCompleted += (s, e) => { if (s == _activeService) RecognitionCompleted?.Invoke(this, e); };
        service.RecognitionError += (s, e) => { if (s == _activeService) RecognitionError?.Invoke(this, e); };
        service.StateChanged += (s, e) => { if (s == _activeService) StateChanged?.Invoke(this, e); };
    }

    private ISpeechRecognitionService GetServiceForProvider(SpeechProvider provider)
    {
        // On non-Windows, Offline maps to Azure (no System.Speech available)
        return provider switch
        {
            SpeechProvider.Azure => _azureService,
            SpeechProvider.OpenAI => _whisperService.IsAvailable ? _whisperService : _azureService,
            SpeechProvider.Offline => _azureService, // Fallback to Azure on non-Windows
            _ => _azureService
        };
    }

    private async void OnSettingsChanged(object? sender, Models.AppSettings settings)
    {
        var newService = GetServiceForProvider(settings.SpeechProvider);
        var providerChanged = newService != _activeService;
        var languageChanged = settings.RecognitionLanguage != _currentLanguage;

        if (!providerChanged && !languageChanged)
            return;

        Log($"Settings changed - Provider: {providerChanged}, Language: {languageChanged}");

        // Stop current recognition if active
        if (_activeService.CurrentState == RecognitionState.Listening)
        {
            await _activeService.StopRecognitionAsync();
        }

        // Configure the new service
        switch (settings.SpeechProvider)
        {
            case SpeechProvider.Azure:
            case SpeechProvider.Offline: // Offline falls back to Azure
                _azureService.Configure(settings.AzureSubscriptionKey, settings.AzureRegion);
                break;
            case SpeechProvider.OpenAI:
                _whisperService.Configure(settings.OpenAIApiKey ?? string.Empty);
                break;
        }

        // Update language
        var newLanguage = settings.RecognitionLanguage;

        // Re-initialize if needed
        if (_isInitialized && (providerChanged || languageChanged))
        {
            try
            {
                await newService.InitializeAsync(newLanguage);
            }
            catch (Exception ex)
            {
                Log($"Failed to initialize provider: {ex.Message}");
                RecognitionError?.Invoke(this, new RecognitionErrorEventArgs($"Failed to apply settings: {ex.Message}", ex));
                return;
            }
        }

        _activeService = newService;

        if (languageChanged)
        {
            _currentLanguage = newLanguage;
            LanguageChanged?.Invoke(this, _currentLanguage);
        }

        StateChanged?.Invoke(this, new RecognitionStateChangedEventArgs(RecognitionState.Idle, RecognitionState.Idle));
    }

    public async Task InitializeAsync(string language = "en-US")
    {
        _currentLanguage = language;
        var settings = _settingsService.Current;

        // Configure services
        _azureService.Configure(settings.AzureSubscriptionKey, settings.AzureRegion);
        _whisperService.Configure(settings.OpenAIApiKey ?? string.Empty);

        // Initialize the active service
        await _activeService.InitializeAsync(language);
        _isInitialized = true;

        Log($"CrossPlatformSpeechServiceManager initialized with {_activeService.ProviderName}");
    }

    public Task StartRecognitionAsync(CancellationToken cancellationToken = default)
    {
        return _activeService.StartRecognitionAsync(cancellationToken);
    }

    public Task<string> StopRecognitionAsync()
    {
        return _activeService.StopRecognitionAsync();
    }

    public void Dispose()
    {
        _settingsService.SettingsChanged -= OnSettingsChanged;

        // Stop active recognition first
        if (_activeService.CurrentState == RecognitionState.Listening)
        {
            try
            {
                _activeService.StopRecognitionAsync().GetAwaiter().GetResult();
            }
            catch { /* Ignore errors during shutdown */ }
        }

        _azureService.Dispose();
        _whisperService.Dispose();
    }

    private static void Log(string message)
    {
        var logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WisprClone", "wispr_log.txt");
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [CrossPlatformManager] {message}";
        try { File.AppendAllText(logPath, line + Environment.NewLine); } catch { }
    }
}
