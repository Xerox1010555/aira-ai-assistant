namespace AiraAssistant.Models;

/// <summary>
/// High-level state of the always-on voice session. Drives the indicator in the UI.
/// </summary>
public enum VoiceSessionState
{
    Idle,
    Listening,
    Thinking,
    Speaking,
    Error
}
