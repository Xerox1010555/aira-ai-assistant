namespace AiraAssistant.Models;

/// <summary>
/// Indicates who originated a particular chat message from the UI's point of view.
/// Kept separate from <see cref="ChatRole"/> so that the VM can render messages
/// differently (e.g. tool outputs) without leaking provider semantics into the view.
/// </summary>
public enum MessageAuthor
{
    User,
    Assistant,
    System,
    Tool
}
