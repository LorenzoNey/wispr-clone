#if WINDOWS
using WisprClone.Core;
using WisprClone.Models;
using WisprClone.Services.Interfaces;

namespace WisprClone.Services.Speech;

/// <summary>
/// Manages speech recognition providers and allows switching at runtime (Windows version with all providers).
/// </summary>
public class SpeechServiceManager : ISpeechRecognitionService
{
    private readonly OfflineSpeechRecognitionService _offlineService;
    private readonly AzureSpeechRecognitionService _azureService;
    private readonly OpenAIWhisperSpeechRecognitionService _whisperService;
    private readonly OpenAIRealtimeSpeechRecognitionService _realtimeService;
    private readonly HybridSpeechRecognitionService _hybridService;
    private readonly FasterWhisperSpeechRecognitionService _fasterWhisperService;
    private readonly WhisperServerSpeechRecognitionService _whisperServerService;
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

    /// <summary>
    /// Gets the actual active provider (may differ from settings if fallback occurred).
    /// </summary>
    public SpeechProvider ActiveProvider => GetProviderForService(_activeService);

    private SpeechProvider GetProviderForService(ISpeechRecognitionService service)
    {
        if (service == _azureService) return SpeechProvider.Azure;
        if (service == _whisperService) return SpeechProvider.OpenAI;
        if (service == _realtimeService) return SpeechProvider.OpenAIRealtime;
        if (service == _fasterWhisperService) return SpeechProvider.FasterWhisper;
        if (service == _whisperServerService) return SpeechProvider.WhisperServer;
        return SpeechProvider.Offline; // Hybrid/Offline
    }

    public SpeechServiceManager(
        OfflineSpeechRecognitionService offlineService,
        AzureSpeechRecognitionService azureService,
        OpenAIWhisperSpeechRecognitionService whisperService,
        OpenAIRealtimeSpeechRecognitionService realtimeService,
        HybridSpeechRecognitionService hybridService,
        FasterWhisperSpeechRecognitionService fasterWhisperService,
        WhisperServerSpeechRecognitionService whisperServerService,
        ISettingsService settingsService)
    {
        _offlineService = offlineService;
        _azureService = azureService;
        _whisperService = whisperService;
        _realtimeService = realtimeService;
        _hybridService = hybridService;
        _fasterWhisperService = fasterWhisperService;
        _whisperServerService = whisperServerService;
        _settingsService = settingsService;

        _activeService = GetServiceForProvider(settingsService.Current.SpeechProvider);

        WireEvents(_offlineService);
        WireEvents(_azureService);
        WireEvents(_whisperService);
        WireEvents(_realtimeService);
        WireEvents(_hybridService);
        WireEvents(_fasterWhisperService);
        WireEvents(_whisperServerService);

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
        ISpeechRecognitionService service = provider switch
        {
            SpeechProvider.Azure => _azureService,
            SpeechProvider.OpenAI => _whisperService,
            SpeechProvider.OpenAIRealtime => _realtimeService,
            SpeechProvider.FasterWhisper => _fasterWhisperService,
            SpeechProvider.WhisperServer => _whisperServerService,
            _ => _hybridService // Offline uses Hybrid for fallback support
        };

        // If the selected provider isn't available, fall back to offline/hybrid
        if (!service.IsAvailable && provider != SpeechProvider.Offline)
        {
            return _hybridService;
        }

        return service;
    }

    private async void OnSettingsChanged(object? sender, AppSettings settings)
    {
        var newService = GetServiceForProvider(settings.SpeechProvider);
        var providerChanged = newService != _activeService;
        var languageChanged = settings.RecognitionLanguage != _currentLanguage;

        if (!providerChanged && !languageChanged)
            return;

        if (_activeService.CurrentState == RecognitionState.Listening)
        {
            await _activeService.StopRecognitionAsync();
        }

        switch (settings.SpeechProvider)
        {
            case SpeechProvider.Azure:
                _azureService.Configure(settings.AzureSubscriptionKey, settings.AzureRegion);
                break;
            case SpeechProvider.OpenAI:
                _whisperService.Configure(settings.OpenAIApiKey ?? string.Empty);
                break;
            case SpeechProvider.OpenAIRealtime:
                _realtimeService.Configure(settings.OpenAIApiKey ?? string.Empty);
                break;
        }

        var newLanguage = settings.RecognitionLanguage;

        if (_isInitialized && (providerChanged || languageChanged))
        {
            try
            {
                await newService.InitializeAsync(newLanguage);
            }
            catch (Exception ex)
            {
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

        _azureService.Configure(settings.AzureSubscriptionKey, settings.AzureRegion);
        _whisperService.Configure(settings.OpenAIApiKey ?? string.Empty);
        _realtimeService.Configure(settings.OpenAIApiKey ?? string.Empty);

        await _activeService.InitializeAsync(language);
        _isInitialized = true;
    }

    public Task StartRecognitionAsync(CancellationToken cancellationToken = default)
        => _activeService.StartRecognitionAsync(cancellationToken);

    public Task<string> StopRecognitionAsync()
        => _activeService.StopRecognitionAsync();

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

        _offlineService.Dispose();
        _azureService.Dispose();
        _whisperService.Dispose();
        _realtimeService.Dispose();
        _fasterWhisperService.Dispose();
        _whisperServerService.Dispose();

        // Stop the whisper server process on app shutdown
        WhisperServerSpeechRecognitionService.StopServer();
    }
}
#endif
