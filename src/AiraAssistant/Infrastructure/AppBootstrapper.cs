using System;
using System.Threading;
using System.Threading.Tasks;
using AiraAssistant.Services.Ai;
using AiraAssistant.Services.Speech;
using AiraAssistant.UI.ViewModels;
using AiraAssistant.Services.Audio;
using AiraAssistant.Infrastructure.Logging;

namespace AiraAssistant.Infrastructure;

/// <summary>
/// Performs post-construction wiring between services (STT pipeline to the
/// microphone pipeline, TTS events to AI, router to OS control...). Kept out
/// of the DI registration so the object graph remains obvious.
///
/// v1.1 changes:
///  - EVERY view-model mutation is marshaled onto the UI thread via <see cref="Ui"/>,
///    because System.Speech/NAudio/HttpClient raise events on worker threads.
///  - A serialized tool gate (SemaphoreSlim) guarantees exactly one
///    execute-and-report tool pipeline at a time.
///  - Barge-in: incoming user speech while Aira is speaking cancels the TTS.
///  - When barge-in is disabled the STT engine is paused while Aira speaks,
///    so she can never transcribe her own voice (feedback-loop fix).
///  - Destructive tools surface a confirmation dialog before executing.
/// </summary>
public sealed class AppBootstrapper
{
    private readonly MainViewModel _mainViewModel;
    private readonly IMicrophoneService _microphone;
    private readonly ISpeechToTextService _stt;
    private readonly ITextToSpeechService _tts;
    private readonly IAiService _ai;
    private readonly FunctionRouter _router;
    private readonly IAdminService _admin;
    private readonly IAppConfig _config;
    private readonly ILogger _logger;

    // Only one tool pipeline (execute → report → continue chat) at a time.
    private readonly SemaphoreSlim _toolGate = new(1, 1);

    public AppBootstrapper(
        MainViewModel mainViewModel,
        IMicrophoneService microphone,
        ISpeechToTextService stt,
        ITextToSpeechService tts,
        IAiService ai,
        FunctionRouter router,
        IAdminService admin,
        IAppConfig config,
        ILogger logger)
    {
        _mainViewModel = mainViewModel;
        _microphone = microphone;
        _stt = stt;
        _tts = tts;
        _ai = ai;
        _router = router;
        _admin = admin;
        _config = config;
        _logger = logger;
    }

    public async Task InitializeAsync()
    {
        _logger.Information("Aira starting up.");

        // Confirm elevation so the user knows if actions are limited.
        var elevated = _admin.IsRunAsAdministrator();
        _logger.Information($"Process elevation: {(elevated ? "Administrator" : "Standard user")}.");
        Ui.Post(() => _mainViewModel.SetElevationStatus(elevated));

        // ------------------------------------------------------------------
        // STT -> VM (marshaled to the UI thread).
        // ------------------------------------------------------------------
        _stt.PartialTranscript += (_, e) => Ui.Post(() => _mainViewModel.OnPartialTranscript(e));
        _stt.FinalTranscript += (_, e) => Ui.Post(() => _ = _mainViewModel.SubmitUserMessageAsync(e));
        _stt.RecognitionError += (_, e) => Ui.Post(() => _mainViewModel.OnRecognitionError(e.Message));

        // ------------------------------------------------------------------
        // Microphone -> STT, only for push-based engines (Whisper...).
        // The System.Speech engine opens its own device; pushing raw PCM into
        // a no-op sink just wasted the mic and CPU in v1.
        // ------------------------------------------------------------------
        if (_stt.IsPushAudio)
        {
            _microphone.AudioSampled += (_, e) =>
            {
                try { _stt.PushAudioFrame(e.Samples, e.SampleRate, e.Channels); }
                catch (Exception ex) { _logger.Error("PushAudioFrame failed", ex); }
            };
        }
        _microphone.CaptureError += (_, ex) =>
            Ui.Post(() => _mainViewModel.OnRecognitionError($"Microphone error: {ex.Message}"));

        // ------------------------------------------------------------------
        // AI events -> VM.
        // ------------------------------------------------------------------
        _ai.ResponseChunkReceived += (_, e) => Ui.Post(() => _mainViewModel.OnAssistantChunk(e.Text));
        _ai.ResponseCompleted += (_, e) => Ui.Post(() => _mainViewModel.OnAssistantComplete(e.FullText));
        _ai.ErrorOccurred += (_, e) => Ui.Post(() => _mainViewModel.OnAiError(e.Message));

        _ai.FunctionCallRequested += async (_, e) =>
        {
            try
            {
                await _toolGate.WaitAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.Error("Tool gate failure; skipping tool execution.", ex);
                return;
            }

            try
            {
                var result = await _router.ExecuteAsync(e.FunctionName, e.ArgumentsJson).ConfigureAwait(false);
                await _ai.SubmitFunctionResultAsync(e.CallId, e.FunctionName, result).ConfigureAwait(false);
                Ui.Post(() => _mainViewModel.OnToolExecuted(e.CallId, e.FunctionName, result));
            }
            catch (Exception ex)
            {
                _logger.Error($"Tool pipeline failed for '{e.FunctionName}'.", ex);
                await _ai.SubmitFunctionResultAsync(e.CallId, e.FunctionName,
                    "{\"ok\":false,\"message\":\"Tool pipeline error.\"}").ConfigureAwait(false);
            }
            finally
            {
                _toolGate.Release();
            }
        };

        // ------------------------------------------------------------------
        // Router: confirm destructive tools through the UI.
        // ------------------------------------------------------------------
        _router.ConfirmationHandler = async (tool, args) =>
        {
            bool approved;
            try
            {
                approved = await Ui.InvokeAsync(() =>
                {
                    var window = System.Windows.Application.Current?.MainWindow;
                    return System.Windows.MessageBox.Show(
                            window,
                            $"Aira requests permission to run:\n\n  {tool}\n\nArguments: {args}",
                            "Aira — tool approval",
                            System.Windows.MessageBoxButton.YesNo,
                            System.Windows.MessageBoxImage.Warning,
                            System.Windows.MessageBoxResult.No) == System.Windows.MessageBoxResult.Yes;
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.Error("Approval dialog failed; defaulting to deny.", ex);
                approved = false;
            }
            return approved;
        };

        // ------------------------------------------------------------------
        // TTS events -> state + self-hearing protection.
        // ------------------------------------------------------------------
        _tts.SpeakStarted += (_, _) =>
        {
            // While Aira speaks, the recognizer hears her own output through
            // the speakers. Unless real barge-in is enabled, silence STT.
            if (!_config.Speech.BargeInEnabled)
            {
                Ui.Post(() => _stt.Suspend());
            }
            Ui.Post(() => _mainViewModel.OnSpeechPlaybackStarted());
        };
        _tts.SpeakCompleted += (_, _) =>
        {
            Ui.Post(() => _stt.Resume());
            Ui.Post(() => _mainViewModel.OnSpeechPlaybackFinished());
        };
        _router.ToolExecuted += (_, e) => _logger.Information($"Tool executed: {e.ToolName}");

        // ------------------------------------------------------------------
        // VM -> services.
        // ------------------------------------------------------------------
        _mainViewModel.RequestSpeakAsync += async (_, text) => await _tts.SpeakAsync(text).ConfigureAwait(true);
        _mainViewModel.RequestStopSpeakingAsync += (_, _) => _tts.StopSpeaking(); // barge-in cancel path
        _mainViewModel.RequestStartListeningAsync += async (_, _) =>
        {
            try
            {
                if (_stt.IsPushAudio)
                {
                    await _microphone.StartAsync().ConfigureAwait(true);
                }
                _stt.Start();
            }
            catch (Exception ex)
            {
                _logger.Error("Failed to start voice session", ex);
                Ui.Post(() => _mainViewModel.OnRecognitionError($"Mic/STT error: {ex.Message}"));
            }
        };
        _mainViewModel.RequestStopListeningAsync += (_, _) =>
        {
            try
            {
                _stt.Stop();
                if (_stt.IsPushAudio)
                {
                    _microphone.Stop();
                }
            }
            catch (Exception ex)
            {
                _logger.Error("Failed to stop voice session", ex);
            }
        };

        await Task.CompletedTask.ConfigureAwait(false);
    }

    public void Shutdown()
    {
        try { _stt.Stop(); } catch { /* ignore */ }
        try { if (_stt.IsPushAudio) _microphone.Stop(); } catch { /* ignore */ }
        try { _tts.StopSpeaking(); } catch { /* ignore */ }
    }
}
