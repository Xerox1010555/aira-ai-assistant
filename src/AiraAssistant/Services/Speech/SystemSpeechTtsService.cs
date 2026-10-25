using System;
using System.Globalization;
using System.Speech.Synthesis;
using AiraAssistant.Infrastructure;
using AiraAssistant.Infrastructure.Logging;

namespace AiraAssistant.Services.Speech;

/// <summary>
/// System.Speech synthesizer used as the default TTS engine. The synthesizer
/// runs on its own thread; callers can await <see cref="SpeakAsync"/> so they
/// know when the speech is finished.
///
/// v1.1 fixes:
///  - <c>SelectVoice</c> is only called with a NON-EMPTY name, wrapped in
///    try/catch. The old code called it with the default empty string and
///    threw inside the constructor, which aborted the entire application
///    startup through the DI container.
///  - TTS rate/volume come from configuration (Speech.TtsRate/TtsVolume).
/// </summary>
public sealed class SystemSpeechTtsService : ITextToSpeechService, IDisposable
{
    private readonly ILogger _logger;
    private readonly SpeechSynthesizer _synth = new();
    private readonly object _gate = new();
    private TaskCompletionSource<bool>? _currentTcs;

    public event EventHandler? SpeakStarted;
    public event EventHandler? SpeakCompleted;

    public SystemSpeechTtsService(ILogger logger, IAppConfig cfg)
    {
        _logger = logger;

        var installed = _synth.GetInstalledVoices();
        if (installed.Count > 0)
        {
            try
            {
                var culture = new CultureInfo(string.IsNullOrWhiteSpace(cfg.Speech.TtsCulture) ? "en-US" : cfg.Speech.TtsCulture);

                // Only select a configured voice when the user actually
                // configured one, and never let a bad name crash startup.
                if (!string.IsNullOrWhiteSpace(cfg.Speech.TtsVoiceName))
                {
                    _synth.SelectVoice(cfg.Speech.TtsVoiceName);
                }
                else
                {
                    foreach (var v in installed)
                    {
                        if (v.VoiceInfo.Culture.Name == culture.Name && v.Enabled)
                        {
                            _synth.SelectVoice(v.VoiceInfo.Name);
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"TTS voice selection failed, using synth default voice: {ex.Message}");
            }
        }
        else
        {
            _logger.Warning("No TTS voices are installed; speech output will be unavailable.");
        }

        _synth.Rate = Math.Clamp(cfg.Speech.TtsRate, -10, 10);
        _synth.Volume = Math.Clamp(cfg.Speech.TtsVolume, 0, 100);

        _synth.SpeakStarted += (_, _) => SafeRaise(SpeakStarted);
        _synth.SpeakCompleted += (_, _) =>
        {
            SafeRaise(SpeakCompleted);
            lock (_gate)
            {
                _currentTcs?.TrySetResult(true);
                _currentTcs = null;
            }
        };
    }

    public Task SpeakAsync(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return Task.CompletedTask;
        TaskCompletionSource<bool> tcs;
        lock (_gate)
        {
            // If something is already speaking, cancel it so the new utterance
            // wins — this is a common requirement for "always-on" voice UX.
            if (_currentTcs is not null)
            {
                try { _synth.SpeakAsyncCancelAll(); } catch { /* ignore */ }
            }
            tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _currentTcs = tcs;
        }
        try
        {
            _synth.SpeakAsync(text);
        }
        catch (Exception ex)
        {
            _logger.Error("TTS failed", ex);
            lock (_gate) { _currentTcs = null; }
            return Task.FromException(ex);
        }
        return tcs.Task;
    }

    public void StopSpeaking()
    {
        try { _synth.SpeakAsyncCancelAll(); } catch { /* ignore */ }
        lock (_gate)
        {
            _currentTcs?.TrySetResult(false);
            _currentTcs = null;
        }
    }

    public void Dispose()
    {
        try { _synth.SpeakAsyncCancelAll(); } catch { /* ignore */ }
        try { _synth.Dispose(); } catch { /* ignore */ }
    }

    private void SafeRaise(EventHandler? handler)
    {
        try { handler?.Invoke(this, EventArgs.Empty); }
        catch (Exception ex) { _logger.Error("TTS event subscriber threw", ex); }
    }
}
