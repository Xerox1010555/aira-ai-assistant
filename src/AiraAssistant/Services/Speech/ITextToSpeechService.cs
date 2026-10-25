using System;
using System.Threading.Tasks;

namespace AiraAssistant.Services.Speech;

/// <summary>Output-side voice. Implementations may stream audio to the system device directly.</summary>
public interface ITextToSpeechService
{
    Task SpeakAsync(string text);
    void StopSpeaking();

    event EventHandler? SpeakStarted;
    event EventHandler? SpeakCompleted;
}
