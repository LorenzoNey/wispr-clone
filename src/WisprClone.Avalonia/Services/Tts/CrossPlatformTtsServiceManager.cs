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

    // TODO: Add provider services when implemented:
    // private readonly AzureTextToSpeechService? _azureService;
    // private readonly OpenAITextToSpeechService? _openaiService;
    // private readonly MacOSTextToSpeechService? _macOSService;

    private ITextToSpeechService? _activeService;
    private bool _isInitialized;
    private string _currentLanguage = "en-US";

    public event EventHandler<SpeechSynthesisEventArgs>? SpeakStarted;
    public event EventHandler<SpeechSynthesisEventArgs>? SpeakCompleted;
    public event EventHandler<SpeechProgressEventArgs>? SpeakProgress;
    public event EventHandler<WordBoundaryEventArgs>? WordBoundary;
    public event EventHandler<SpeechSynthesisErrorEventArgs>? SpeakError;
    public event EventHandler<SynthesisStateChangedEventArgs>? StateChanged;

    public SynthesisState CurrentState => _activeService?.CurrentState ?? SynthesisState.Idle;
    public string ProviderName => _activeService?.ProviderName ?? "None";
    public string CurrentVoice => _activeService?.CurrentVoice ?? string.Empty;
    public bool IsAvailable => _activeService?.IsAvailable ?? false;
    public bool SupportsPause => _activeService?.SupportsPause ?? false;

    public CrossPlatformTtsServiceManager(
        ISettingsService settingsService,
        ILoggingService loggingService)
    {
        _settingsService = settingsService;
        _loggingService = loggingService;

        // TODO: Initialize with available providers
        // For now, no TTS available on non-Windows platforms until providers are implemented

        _settingsService.SettingsChanged += OnSettingsChanged;
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

    private ITextToSpeechService? GetServiceForProvider(TtsProvider provider)
    {
        // TODO: Return appropriate service when implemented
        // return provider switch
        // {
        //     TtsProvider.Azure => _azureService,
        //     TtsProvider.OpenAI => _openaiService,
        //     TtsProvider.MacOSNative when _macOSService?.IsAvailable == true => _macOSService,
        //     _ => _azureService // Fallback
        // };
        return null;
    }

    private async void OnSettingsChanged(object? sender, AppSettings settings)
    {
        var newService = GetServiceForProvider(settings.TtsProvider);
        var providerChanged = newService != _activeService;
        var languageChanged = settings.RecognitionLanguage != _currentLanguage;

        if (!providerChanged && !languageChanged)
            return;

        Log($"Settings changed - Provider: {providerChanged}, Language: {languageChanged}");

        // Stop current speech if active
        if (_activeService?.CurrentState == SynthesisState.Speaking)
        {
            await _activeService.StopAsync();
        }

        // TODO: Configure the new service

        var newLanguage = settings.RecognitionLanguage;
        var voice = GetVoiceForProvider(settings);

        if (_isInitialized && (providerChanged || languageChanged) && newService != null)
        {
            try
            {
                await newService.InitializeAsync(newLanguage, voice);
            }
            catch (Exception ex)
            {
                Log($"Failed to initialize provider: {ex.Message}");
                SpeakError?.Invoke(this, new SpeechSynthesisErrorEventArgs($"Failed to apply settings: {ex.Message}", ex));
                return;
            }
        }

        _activeService = newService;

        if (languageChanged)
        {
            _currentLanguage = newLanguage;
        }

        StateChanged?.Invoke(this, new SynthesisStateChangedEventArgs(SynthesisState.Idle, SynthesisState.Idle));
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

    public async Task InitializeAsync(string language = "en-US", string? voice = null)
    {
        _currentLanguage = language;
        var settings = _settingsService.Current;

        // TODO: Configure services with API keys

        if (_activeService != null)
        {
            var voiceToUse = voice ?? GetVoiceForProvider(settings);
            await _activeService.InitializeAsync(language, voiceToUse);
            _activeService.SetRate(settings.TtsRate);
            _activeService.SetVolume(settings.TtsVolume);
        }

        _isInitialized = true;
        Log($"CrossPlatformTtsServiceManager initialized");
    }

    public async Task SpeakAsync(string text, CancellationToken cancellationToken = default)
    {
        if (_activeService == null)
        {
            SpeakError?.Invoke(this, new SpeechSynthesisErrorEventArgs("No TTS provider available on this platform"));
            return;
        }
        await _activeService.SpeakAsync(text, cancellationToken);
    }

    public async Task StopAsync()
    {
        if (_activeService != null)
            await _activeService.StopAsync();
    }

    public async Task PauseAsync()
    {
        if (_activeService != null)
            await _activeService.PauseAsync();
    }

    public async Task ResumeAsync()
    {
        if (_activeService != null)
            await _activeService.ResumeAsync();
    }

    public async Task<IReadOnlyList<VoiceInfo>> GetAvailableVoicesAsync()
    {
        if (_activeService == null)
            return Array.Empty<VoiceInfo>();
        return await _activeService.GetAvailableVoicesAsync();
    }

    public void SetRate(double rate)
    {
        _activeService?.SetRate(rate);
    }

    public void SetVolume(double volume)
    {
        _activeService?.SetVolume(volume);
    }

    public void Dispose()
    {
        _settingsService.SettingsChanged -= OnSettingsChanged;

        if (_activeService?.CurrentState == SynthesisState.Speaking)
        {
            try
            {
                _activeService.StopAsync().GetAwaiter().GetResult();
            }
            catch { /* Ignore errors during shutdown */ }
        }

        _activeService?.Dispose();
    }

    private void Log(string message)
    {
        _loggingService.Log("CrossPlatformTtsManager", message);
    }
}
