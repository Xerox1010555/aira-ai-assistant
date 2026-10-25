using System;
using System.Collections.ObjectModel;
using System.Speech.Synthesis;
using System.Windows.Input;
using AiraAssistant.Infrastructure;
using AiraAssistant.Models;

namespace AiraAssistant.UI.ViewModels;

/// <summary>
/// Backing model for the in-app Settings dialog (v1.1). Every property maps
/// to one config key; API keys are stored separately, DPAPI-encrypted, via
/// <see cref="SecretStore"/> (invisible even if appsettings.json is committed).
/// Provider / model / system prompt / voice / key changes need a restart
/// because the AI and TTS engines are singletons built at startup.
/// </summary>
public sealed class SettingsViewModel : ViewModelBase
{
    private static readonly string[] Cultures =
    {
        "en-US", "en-GB", "en-AU", "ar-SA", "ar-EG", "fr-FR", "de-DE", "es-ES"
    };

    private readonly Models.AppConfig _config;
    private readonly SecretStore _secrets;
    private readonly SpeechSynthesizer _previewSynth = new();

    private string _provider;
    private string _model;
    private string _systemPrompt;
    private string _sttCulture;
    private string _ttsCulture;
    private string _ttsVoiceName;
    private bool _bargeInEnabled;
    private bool _alwaysOnTop;
    private string _themeName;
    private string _openAiKeyStatus = string.Empty;
    private string _anthropicKeyStatus = string.Empty;

    // Snapshot of the values a restart actually needs.
    private readonly string _origProvider, _origModel, _origSystemPrompt, _origTtsCulture, _origSttCulture, _origTtsVoice;
    private readonly string _origOpenAiKey, _origAnthropicKey;

    public string[] Providers { get; } = { "OpenAI", "Anthropic" };
    public string[] CulturesList { get; } = Cultures;

    public string[] ThemeNames { get; } = ThemeEngine.ThemeNames;
    public string ThemeName
    {
        get => _themeName;
        set => SetProperty(ref _themeName, value);
    }

    public ObservableCollection<string> TtsVoices { get; } = new();

    public string Provider { get => _provider; set => SetProperty(ref _provider, value); }
    public string Model { get => _model; set => SetProperty(ref _model, value); }
    public string SystemPrompt { get => _systemPrompt; set => SetProperty(ref _systemPrompt, value); }

    public string SttCulture
    {
        get => _sttCulture;
        set { if (SetProperty(ref _sttCulture, value)) RefreshVoices(); }
    }

    public string TtsCulture
    {
        get => _ttsCulture;
        set { if (SetProperty(ref _ttsCulture, value)) RefreshVoices(); }
    }

    public string TtsVoiceName { get => _ttsVoiceName; set => SetProperty(ref _ttsVoiceName, value); }
    public bool BargeInEnabled { get => _bargeInEnabled; set => SetProperty(ref _bargeInEnabled, value); }
    public bool AlwaysOnTop { get => _alwaysOnTop; set => SetProperty(ref _alwaysOnTop, value); }

    public string OpenAiKeyStatus
    {
        get => _openAiKeyStatus;
        private set => SetProperty(ref _openAiKeyStatus, value);
    }

    public string AnthropicKeyStatus
    {
        get => _anthropicKeyStatus;
        private set => SetProperty(ref _anthropicKeyStatus, value);
    }

    /// <summary>True when saved values require rebuilding the AI/TTS engines (i.e. an app restart).</summary>
    public bool NeedsRestart { get; private set; }

    public ICommand PreviewVoiceCommand { get; }

    public SettingsViewModel(Models.AppConfig config, SecretStore secrets)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _secrets = secrets ?? throw new ArgumentNullException(nameof(secrets));

        _provider = string.IsNullOrWhiteSpace(config.Ai.Provider) ? "OpenAI" : config.Ai.Provider;
        _model = config.Ai.Model ?? string.Empty;
        _systemPrompt = config.Ai.SystemPrompt ?? string.Empty;
        _sttCulture = string.IsNullOrWhiteSpace(config.Speech.SttCulture) ? "en-US" : config.Speech.SttCulture;
        _ttsCulture = string.IsNullOrWhiteSpace(config.Speech.TtsCulture) ? "en-US" : config.Speech.TtsCulture;
        _ttsVoiceName = config.Speech.TtsVoiceName ?? string.Empty;
        _bargeInEnabled = config.Speech.BargeInEnabled;
        _alwaysOnTop = config.Ui.AlwaysOnTop;
        _themeName = ThemeEngine.Normalize(config.Ui.Theme);

        _origProvider = _provider;
        _origModel = _model;
        _origSystemPrompt = _systemPrompt;
        _origSttCulture = _sttCulture;
        _origTtsCulture = _ttsCulture;
        _origTtsVoice = _ttsVoiceName;

        // Effective keys as of building the engines (JSON wins, then DPAPI).
        _origOpenAiKey = OperandKey(_config.Ai.OpenAiApiKey, SecretStore.OpenAiKeyName);
        _origAnthropicKey = OperandKey(_config.Ai.AnthropicApiKey, SecretStore.AnthropicKeyName);

        PreviewVoiceCommand = new RelayCommand(PreviewVoice);

        RefreshVoices();
        UpdateKeyStatuses();
    }

    private void RefreshVoices()
    {
        TtsVoices.Clear();
        TtsVoices.Add(string.Empty); // "auto" option — pick first voice matching culture
        try
        {
            foreach (var voice in _previewSynth.GetInstalledVoices())
            {
                if (!voice.Enabled) continue;
                TtsVoices.Add(voice.VoiceInfo.Name);
            }
        }
        catch
        {
            // voice list is decorative-only; never fail the dialog
        }
    }

    private void PreviewVoice()
    {
        try
        {
            try { _previewSynth.SpeakAsyncCancelAll(); } catch { /* ignore */ }
            if (!string.IsNullOrWhiteSpace(TtsVoiceName))
            {
                try { _previewSynth.SelectVoice(TtsVoiceName); } catch { /* fallback below */ }
            }
            _previewSynth.SpeakAsync("Hello! I am Aira, your Windows assistant.");
        }
        catch
        {
            // preview is best-effort
        }
    }

    private string OperandKey(string jsonKey, string storeName)
    {
        if (!string.IsNullOrWhiteSpace(jsonKey)) return "(appsettings.json)";
        if (_secrets.Has(storeName)) return "(stored — encrypted)";
        return "(missing)";
    }

    private void UpdateKeyStatuses()
    {
        OpenAiKeyStatus = $"Effective OpenAI key: {OperandKey(_config.Ai.OpenAiApiKey, SecretStore.OpenAiKeyName)}";
        AnthropicKeyStatus = $"Effective Anthropic key: {OperandKey(_config.Ai.AnthropicApiKey, SecretStore.AnthropicKeyName)}";
    }

    /// <summary>
    /// Copies UI values into the shared config and persists everything.
    /// Key fields stay empty on the dialog = keep current key. Returns false
    /// on I/O failure.
    /// </summary>
    public bool Save(string? openAiKey, string? anthropicKey)
    {
        try
        {
            _config.Ai.Provider = Provider;
            _config.Ai.Model = Model;
            _config.Ai.SystemPrompt = SystemPrompt;
            _config.Speech.SttCulture = SttCulture;
            _config.Speech.TtsCulture = TtsCulture;
            _config.Speech.TtsVoiceName = TtsVoiceName;
            _config.Speech.BargeInEnabled = BargeInEnabled;
            _config.Ui.AlwaysOnTop = AlwaysOnTop;
            _config.Ui.Theme = ThemeEngine.Normalize(ThemeName);

            AppConfigExtensions.SaveToFile(_config);

            if (!string.IsNullOrWhiteSpace(openAiKey))
            {
                _secrets.Set(SecretStore.OpenAiKeyName, openAiKey.Trim());
            }
            if (!string.IsNullOrWhiteSpace(anthropicKey))
            {
                _secrets.Set(SecretStore.AnthropicKeyName, anthropicKey.Trim());
            }

            UpdateKeyStatuses();

            // Restart only needed when what the engines captured actually changed.
            var newOpenAi = OperandKey(_config.Ai.OpenAiApiKey, SecretStore.OpenAiKeyName);
            var newAnthro = OperandKey(_config.Ai.AnthropicApiKey, SecretStore.AnthropicKeyName);
            var keysChanged = !string.Equals(_origOpenAiKey, newOpenAi, StringComparison.Ordinal)
                           || !string.Equals(_origAnthropicKey, newAnthro, StringComparison.Ordinal);

            NeedsRestart = keysChanged ||
                !string.Equals(_origProvider, Provider, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(_origModel, Model, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(_origSystemPrompt, SystemPrompt, StringComparison.Ordinal) ||
                !string.Equals(_origTtsCulture, TtsCulture, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(_origSttCulture, SttCulture, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(_origTtsVoice, TtsVoiceName, StringComparison.Ordinal);

            return true;
        }
        catch (UnauthorizedAccessException)
        {
            throw;
        }
        catch (System.IO.IOException)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Call when the dialog closes to free the preview synthesizer.</summary>
    public void DisposePreview()
    {
        try { _previewSynth.SpeakAsyncCancelAll(); } catch { /* ignore */ }
        try { _previewSynth.Dispose(); } catch { /* ignore */ }
    }
}
