using System;

namespace AiraAssistant.Services.Speech;

/// <summary>
/// Push-style STT engine. The microphone feeds raw PCM frames; the engine
/// raises partial and final transcripts.
/// </summary>
public interface ISpeechToTextService
{
    /// <summary>
    /// True when the engine consumes raw PCM pushed via <see cref="PushAudioFrame"/>
    /// (e.g. a Whisper implementation), so the NAudio capture must run.
    /// False when the engine opens its own audio device (System.Speech/WinRT),
    /// in which case starting the NAudio capture is wasted work.
    /// </summary>
    bool IsPushAudio { get; }

    void PushAudioFrame(byte[] pcm, int sampleRate, int channels);
    void Start();
    void Stop();

    /// <summary>
    /// Temporarily stop recognizing (e.g. while Aira is speaking, so she cannot
    /// hear her own voice through the speakers → mic loop). Recognition may
    /// later be restored with <see cref="Resume"/>.
    /// </summary>
    void Suspend();

    /// <summary>Restore recognition after <see cref="Suspend"/>. Safe to call anytime.</summary>
    void Resume();

    event EventHandler<string>? PartialTranscript;       // interim (no auto-submit)
    event EventHandler<string>? FinalTranscript;         // committed phrase
    event EventHandler<SpeechErrorEventArgs>? RecognitionError;
}

public sealed class SpeechErrorEventArgs : EventArgs
{
    public string Message { get; }
    public Exception? Exception { get; }
    public SpeechErrorEventArgs(string message, Exception? ex = null)
    {
        Message = message;
        Exception = ex;
    }
}
