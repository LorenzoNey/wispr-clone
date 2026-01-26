using WisprClone.Core;
using WisprClone.Models;
using WisprClone.Services.Interfaces;

namespace WisprClone.Services.Tts;

/// <summary>
/// Cross-platform TTS service manager for non-Windows platforms.
/// Supports cloud providers (Azure and OpenAI) and native macOS TTS.
/// </summary>
public class CrossPlatformTtsServiceManager : ITextToSpeechService
{
    private readonly ISettingsService _settingsService;
    private readonly ILoggingService _loggingService;
    private readonly AzureTextToSpeechService _azureService;
    private readonly OpenAITextToSpeechService _openaiService;
    private readonly MacOSTextToSpeechService? _macOSService;

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
        if (service == _macOSService) return TtsProvider.MacOSNative;
        return TtsProvider.Offline; // Fallback
    }

    public CrossPlatformTtsServiceManager(
        AzureTextToSpeechService azureService,
        OpenAITextToSpeechService openaiService,
        MacOSTextToSpeechService? macOSService,
        ISettingsService settingsService,
        ILoggingService loggingService)
    {
        _azureService = azureService;
        _openaiService = openaiService;
        _macOSService = macOSService;
        _settingsService = settingsService;
        _loggingService = loggingService;

        // Configure cloud services with API keys from settings
        ConfigureCloudServices(settingsService.Current);

        _activeService = GetServiceForProvider(settingsService.Current.TtsProvider);

        WireEvents(_azureService);
        WireEvents(_openaiService);
        if (_macOSService != null)
        {
            WireEvents(_macOSService);
        }

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
            TtsProvider.Azure => _azureService,
            TtsProvider.OpenAI => _openaiService,
            TtsProvider.MacOSNative when _macOSService != null => _macOSService,
            TtsProvider.Offline when _macOSService != null => _macOSService, // On macOS, "Offline" maps to native
            _ => (ITextToSpeechService?)_macOSService ?? _azureService // Fallback
        };

        // If the selected provider isn't available, fall back
        if (!service.IsAvailable)
        {
            // Try macOS native first if available
            if (_macOSService?.IsAvailable == true)
            {
                Log($"Provider {provider} not available, falling back to macOS native");
                return _macOSService;
            }
            // Then try Azure
            if (_azureService.IsAvailable)
            {
                Log($"Provider {provider} not available, falling back to Azure");
                return _azureService;
            }
            // Then OpenAI
            if (_openaiService.IsAvailable)
            {
                Log($"Provider {provider} not available, falling back to OpenAI");
                return _openaiService;
            }
        }

        return service;
    }

    private string? GetVoiceForProvider(AppSettings settings)
    {
        return settings.TtsProvider switch
        {
            TtsProvider.Azure => settings.AzureTtsVoice,
            TtsProvider.OpenAI => settings.OpenAITtsVoice,
            _ => settings.TtsVoice
        };
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

        Log($"Settings changed - Provider: {providerChanged}, Language: {languageChanged}, Voice: {voiceChanged}");

        // Stop current speech if active
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
                Log($"Failed to initialize provider: {ex.Message}");
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
            // If initialization fails, try to fall back
            Log($"Failed to initialize {_activeService.ProviderName}: {ex.Message}");

            if (_macOSService != null && _activeService != _macOSService && _macOSService.IsAvailable)
            {
                SpeakError?.Invoke(this, new SpeechSynthesisErrorEventArgs(
                    $"Failed to initialize {_activeService.ProviderName}, falling back to macOS native: {ex.Message}", ex));
                _activeService = _macOSService;
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
        Log($"CrossPlatformTtsServiceManager initialized with {_activeService.ProviderName}");
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

        if (_activeService.CurrentState == SynthesisState.Speaking)
        {
            try
            {
                _activeService.StopAsync().GetAwaiter().GetResult();
            }
            catch { /* Ignore errors during shutdown */ }
        }

        _azureService.Dispose();
        _openaiService.Dispose();
        _macOSService?.Dispose();
    }

    private void Log(string message)
    {
        _loggingService.Log("CrossPlatformTtsManager", message);
    }
}
