using System;
using System.Globalization;
using System.Speech.Recognition;
using AiraAssistant.Infrastructure;
using AiraAssistant.Infrastructure.Logging;

namespace AiraAssistant.Services.Speech;

/// <summary>
/// System.Speech implementation of <see cref="ISpeechToTextService"/>.
///
/// v1.1 fixes:
///  - The dictation grammar is loaded exactly ONCE (loading it twice caused
///    duplicate hypotheses/recognitions in the old code).
///  - The unsupported desktop "AssumeCFGEnabled" recognizer setting was
///    removed; it threw an exception and killed every voice session start.
///  - Added Suspend/Resume so Aira cannot transcribe her own TTS output,
///    and a clean re-Start after Stop.
/// </summary>
public sealed class SystemSpeechSttService : ISpeechToTextService, IDisposable
{
    private readonly ILogger _logger;
    private readonly string _cultureName;
    private SpeechRecognitionEngine? _engine;
    private bool _recognizing;
    private bool _suspended;

    public event EventHandler<string>? PartialTranscript;
    public event EventHandler<string>? FinalTranscript;
    public event EventHandler<SpeechErrorEventArgs>? RecognitionError;

    // System.Speech pulls from the sound card itself; no raw PCM push needed.
    public bool IsPushAudio => false;

    public SystemSpeechSttService(ILogger logger, IAppConfig cfg)
    {
        _logger = logger;
        _cultureName = string.IsNullOrWhiteSpace(cfg.Speech.SttCulture) ? "en-US" : cfg.Speech.SttCulture;
    }

    public void Start()
    {
        if (_engine is null)
        {
            try
            {
                var culture = new CultureInfo(_cultureName);
                var engine = new SpeechRecognitionEngine(culture);
                engine.SetInputToDefaultAudioDevice();

                // Load the free dictation grammar once so we accept any utterance.
                var dictation = new DictationGrammar { Name = "Default Dictation", Enabled = true };
                engine.LoadGrammar(dictation);

                engine.SpeechRecognized += OnSpeechRecognized;
                engine.SpeechHypothesized += OnSpeechHypothesized;
                engine.SpeechRecognitionRejected += OnSpeechRejected;
                engine.RecognizeCompleted += OnRecognizeCompleted;

                _engine = engine;
            }
            catch (Exception ex)
            {
                _logger.Error("Failed to start STT", ex);
                RecognitionError?.Invoke(this, new SpeechErrorEventArgs(ex.Message, ex));
                throw;
            }
        }

        if (!_recognizing)
        {
            try
            {
                _engine.RecognizeAsync(RecognizeMode.Multiple);
                _recognizing = true;
            }
            catch (Exception ex)
            {
                _logger.Error("Failed to begin recognition", ex);
                RecognitionError?.Invoke(this, new SpeechErrorEventArgs(ex.Message, ex));
                throw;
            }
        }

        _suspended = false;
        _logger.Information($"STT running (culture={_cultureName}).");
    }

    public void Stop()
    {
        if (_engine is not null && _recognizing)
        {
            try
            {
                _engine.RecognizeAsyncStop();
            }
            catch (Exception ex)
            {
                _logger.Warning($"RecognizeAsyncStop: {ex.Message}");
            }
        }
        _recognizing = false;
        _suspended = false;
    }

    public void Suspend()
    {
        if (_engine is not null && _recognizing)
        {
            try { _engine.RecognizeAsyncStop(); }
            catch (Exception ex) { _logger.Warning($"Suspend RecognizeAsyncStop: {ex.Message}"); }
        }
        _recognizing = false;
        _suspended = true;
    }

    public void Resume()
    {
        if (!_suspended) return;
        _suspended = false;
        try { Start(); }
        catch (Exception ex) { _logger.Warning($"Resume recognition failed: {ex.Message}"); }
    }

    public void Dispose()
    {
        try
        {
            if (_engine is not null)
            {
                _engine.SpeechRecognized -= OnSpeechRecognized;
                _engine.SpeechHypothesized -= OnSpeechHypothesized;
                _engine.SpeechRecognitionRejected -= OnSpeechRejected;
                _engine.RecognizeCompleted -= OnRecognizeCompleted;
                try { if (_recognizing) _engine.RecognizeAsyncCancel(); } catch { /* ignore */ }
                _engine.Dispose();
                _engine = null;
            }
        }
        catch
        {
            // ignore
        }
    }

    // The System.Speech recognizer pulls from the sound card itself, so
    // PushAudioFrame is a no-op in this implementation. It stays on the
    // interface for future push-based engines (Whisper, Vosk, Azure...).
    public void PushAudioFrame(byte[] pcm, int sampleRate, int channels)
    {
    }

    private void OnSpeechHypothesized(object? sender, SpeechHypothesizedEventArgs e)
    {
        var text = e.Result?.Text;
        if (!string.IsNullOrWhiteSpace(text))
        {
            try { PartialTranscript?.Invoke(this, text); } catch { /* subscriber safety */ }
        }
    }

    private void OnSpeechRecognized(object? sender, SpeechRecognizedEventArgs e)
    {
        var text = e.Result?.Text;
        if (string.IsNullOrWhiteSpace(text)) return;
        if (e.Result!.Confidence < 0.3) return;     // filter noise
        try { FinalTranscript?.Invoke(this, text); } catch { /* subscriber safety */ }
    }

    private void OnSpeechRejected(object? sender, SpeechRecognitionRejectedEventArgs e)
    {
        // Not really an error; just nothing useful was said. We don't raise
        // RecognitionError to avoid noise in the status bar.
    }

    private void OnRecognizeCompleted(object? sender, RecognizeCompletedEventArgs e)
    {
        _recognizing = false;
        if (e.Error is not null)
        {
            _logger.Error("STT completed with error", e.Error);
            RecognitionError?.Invoke(this, new SpeechErrorEventArgs(e.Error.Message, e.Error));
        }
    }
}
