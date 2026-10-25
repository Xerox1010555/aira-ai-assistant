using System;
using System.Collections.Generic;

namespace AiraAssistant.Models;

/// <summary>
/// Conversation history container. Holds an ordered list of <see cref="ChatMessage"/>
/// and provides a windowing helper so we never blow the model context.
/// </summary>
public sealed class Conversation
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public List<ChatMessage> Messages { get; } = new();

    /// <summary>
    /// Maximum number of most recent non-system messages to keep in memory
    /// when sending to the model. System prompt is always preserved.
    /// </summary>
    public int MaxContextMessages { get; set; } = 40;

    public void Add(ChatMessage message) => Messages.Add(message);

    public void Clear()
    {
        // Preserve the very first System message if present.
        var system = Messages.Find(m => m.Role == ChatRole.System);
        Messages.Clear();
        if (system is not null) Messages.Add(system);
    }

    /// <summary>
    /// Returns the messages that should be sent to the model, optionally
    /// truncating to the last <see cref="MaxContextMessages"/>.
    /// </summary>
    public IReadOnlyList<ChatMessage> GetMessagesForModel()
    {
        if (Messages.Count <= MaxContextMessages) return Messages;
        var system = Messages.Find(m => m.Role == ChatRole.System);
        var tail = Messages.GetRange(Messages.Count - MaxContextMessages, MaxContextMessages);
        if (system is not null) tail.Insert(0, system);
        return tail;
    }
}
