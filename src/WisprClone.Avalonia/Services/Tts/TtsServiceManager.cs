#if WINDOWS
using WisprClone.Core;
using WisprClone.Models;
using WisprClone.Services.Interfaces;

namespace WisprClone.Services.Tts;

/// <summary>
/// Manages TTS providers and allows switching at runtime (Windows version with all providers).
/// </summary>
public class TtsServiceManager : ITextToSpeechService
{
    private readonly OfflineTextToSpeechService _offlineService;
    private readonly AzureTextToSpeechService _azureService;
    private readonly OpenAITextToSpeechService _openaiService;
    private readonly PiperTextToSpeechService _piperService;
    private readonly ISettingsService _settingsService;

    private ITextToSpeechService _activeService;
    private bool _isInitialized;
    private string _currentLanguage = "en-US";
    private string? _currentVoice;

    public event EventHandler<SpeechSynthesisEventArgs>? SpeakStarted;
    public event EventHandler<SpeechSynthesisEventArgs>? SpeakCompleted;
    public event EventHandler<SpeechProgressEventArgs>? SpeakProgress;
    public event EventHandler<WordBoundaryEventArgs>? WordBoundary;
    public event EventHandler<SpeechSynthesisErrorEventArgs>? SpeakError;
    public event EventHandler<SynthesisStateChangedEventArgs>? StateChanged;

    public SynthesisState CurrentState => _activeService.CurrentState;
    public string ProviderName => _activeService.ProviderName;
    public string CurrentVoice => _activeService.CurrentVoice;
    public bool IsAvailable => _activeService.IsAvailable;
    public bool SupportsPause => _activeService.SupportsPause;

    /// <summary>
    /// Gets the actual active provider (may differ from settings if fallback occurred).
    /// </summary>
    public TtsProvider ActiveProvider => GetProviderForService(_activeService);

    private TtsProvider GetProviderForService(ITextToSpeechService service)
    {
        if (service == _azureService) return TtsProvider.Azure;
        if (service == _openaiService) return TtsProvider.OpenAI;
        if (service == _piperService) return TtsProvider.Piper;
        return TtsProvider.Offline;
    }

    public TtsServiceManager(
        OfflineTextToSpeechService offlineService,
        AzureTextToSpeechService azureService,
        OpenAITextToSpeechService openaiService,
        PiperTextToSpeechService piperService,
        ISettingsService settingsService)
    {
        _offlineService = offlineService;
        _azureService = azureService;
        _openaiService = openaiService;
        _piperService = piperService;
        _settingsService = settingsService;

        // Configure cloud services with API keys from settings
        ConfigureCloudServices(settingsService.Current);

        _activeService = GetServiceForProvider(settingsService.Current.TtsProvider);

        WireEvents(_offlineService);
        WireEvents(_azureService);
        WireEvents(_openaiService);
        WireEvents(_piperService);

        _settingsService.SettingsChanged += OnSettingsChanged;
    }

    private void ConfigureCloudServices(AppSettings settings)
    {
        // Configure Azure TTS
        if (!string.IsNullOrEmpty(settings.AzureSubscriptionKey) && !string.IsNullOrEmpty(settings.AzureRegion))
        {
            _azureService.Configure(settings.AzureSubscriptionKey, settings.AzureRegion);
        }

        // Configure OpenAI TTS
        if (!string.IsNullOrEmpty(settings.OpenAIApiKey))
        {
            _openaiService.Configure(settings.OpenAIApiKey, settings.OpenAITtsModel, settings.OpenAITtsVoice);
        }
    }

    private void WireEvents(ITextToSpeechService service)
    {
        service.SpeakStarted += (s, e) => { if (s == _activeService) SpeakStarted?.Invoke(this, e); };
        service.SpeakCompleted += (s, e) => { if (s == _activeService) SpeakCompleted?.Invoke(this, e); };
        service.SpeakProgress += (s, e) => { if (s == _activeService) SpeakProgress?.Invoke(this, e); };
        service.WordBoundary += (s, e) => { if (s == _activeService) WordBoundary?.Invoke(this, e); };
        service.SpeakError += (s, e) => { if (s == _activeService) SpeakError?.Invoke(this, e); };
        service.StateChanged += (s, e) => { if (s == _activeService) StateChanged?.Invoke(this, e); };
    }

    private ITextToSpeechService GetServiceForProvider(TtsProvider provider)
    {
        ITextToSpeechService service = provider switch
        {
            TtsProvider.Offline => _offlineService,
            TtsProvider.Azure => _azureService,
            TtsProvider.OpenAI => _openaiService,
            TtsProvider.Piper => _piperService,
            _ => _offlineService // Default to offline
        };

        // If the selected provider isn't available, fall back to offline
        if (!service.IsAvailable && provider != TtsProvider.Offline)
        {
            return _offlineService;
        }

        return service;
    }

    private async void OnSettingsChanged(object? sender, AppSettings settings)
    {
        // Update cloud service configurations
        ConfigureCloudServices(settings);

        var newService = GetServiceForProvider(settings.TtsProvider);
        var providerChanged = newService != _activeService;
        var languageChanged = settings.RecognitionLanguage != _currentLanguage;
        var newVoice = GetVoiceForProvider(settings);
        var voiceChanged = newVoice != _currentVoice;

        if (!providerChanged && !languageChanged && !voiceChanged)
        {
            // Still apply rate/volume changes
            _activeService.SetRate(settings.TtsRate);
            _activeService.SetVolume(settings.TtsVolume);
            return;
        }

        // Stop any ongoing speech
        if (_activeService.CurrentState == SynthesisState.Speaking)
        {
            await _activeService.StopAsync();
        }

        var newLanguage = settings.RecognitionLanguage;

        if (_isInitialized && (providerChanged || languageChanged || voiceChanged))
        {
            try
            {
                await newService.InitializeAsync(newLanguage, newVoice);
            }
            catch (Exception ex)
            {
                SpeakError?.Invoke(this, new SpeechSynthesisErrorEventArgs($"Failed to apply settings: {ex.Message}", ex));
                return;
            }
        }

        _activeService = newService;

        // Apply rate and volume from settings
        _activeService.SetRate(settings.TtsRate);
        _activeService.SetVolume(settings.TtsVolume);

        if (languageChanged)
        {
            _currentLanguage = newLanguage;
        }

        if (voiceChanged)
        {
            _currentVoice = newVoice;
        }

        StateChanged?.Invoke(this, new SynthesisStateChangedEventArgs(SynthesisState.Idle, SynthesisState.Idle));
    }

    private string? GetVoiceForProvider(AppSettings settings)
    {
        return settings.TtsProvider switch
        {
            TtsProvider.Azure => settings.AzureTtsVoice,
            TtsProvider.OpenAI => settings.OpenAITtsVoice,
            TtsProvider.Piper => settings.PiperVoicePath,
            _ => settings.TtsVoice
        };
    }

    public async Task InitializeAsync(string language = "en-US", string? voice = null)
    {
        _currentLanguage = language;
        var settings = _settingsService.Current;

        // Configure cloud services
        ConfigureCloudServices(settings);

        // Determine voice to use
        var voiceToUse = voice ?? GetVoiceForProvider(settings);
        _currentVoice = voiceToUse;

        try
        {
            await _activeService.InitializeAsync(language, voiceToUse);
        }
        catch (Exception ex)
        {
            // If initialization fails (e.g., missing API keys), fall back to offline
            if (_activeService != _offlineService)
            {
                SpeakError?.Invoke(this, new SpeechSynthesisErrorEventArgs(
                    $"Failed to initialize {_activeService.ProviderName}, falling back to offline: {ex.Message}", ex));
                _activeService = _offlineService;
                await _activeService.InitializeAsync(language, null);
            }
            else
            {
                throw;
            }
        }

        // Apply rate and volume
        _activeService.SetRate(settings.TtsRate);
        _activeService.SetVolume(settings.TtsVolume);

        _isInitialized = true;
    }

    public Task SpeakAsync(string text, CancellationToken cancellationToken = default)
        => _activeService.SpeakAsync(text, cancellationToken);

    public Task StopAsync()
        => _activeService.StopAsync();

    public Task PauseAsync()
        => _activeService.PauseAsync();

    public Task ResumeAsync()
        => _activeService.ResumeAsync();

    public Task<IReadOnlyList<VoiceInfo>> GetAvailableVoicesAsync()
        => _activeService.GetAvailableVoicesAsync();

    public void SetRate(double rate)
        => _activeService.SetRate(rate);

    public void SetVolume(double volume)
        => _activeService.SetVolume(volume);

    public void Dispose()
    {
        _settingsService.SettingsChanged -= OnSettingsChanged;

        // Stop active speech first
        if (_activeService.CurrentState == SynthesisState.Speaking)
        {
            try
            {
                _activeService.StopAsync().GetAwaiter().GetResult();
            }
            catch { /* Ignore errors during shutdown */ }
        }

        _offlineService.Dispose();
        _azureService.Dispose();
        _openaiService.Dispose();
        _piperService.Dispose();
    }
}
#endif
