using System;
using AiraAssistant.Models;

namespace AiraAssistant.UI.ViewModels;

/// <summary>
/// A single row in the chat history list. Wraps a <see cref="ChatMessage"/>
/// with the data the view needs to render and animate the row.
/// </summary>
public sealed class ChatMessageViewModel : ViewModelBase
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public MessageAuthor Author { get; init; }
    public string Text { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Tool name, populated when <see cref="Author"/> is <see cref="MessageAuthor.Tool"/>.</summary>
    public string? ToolName { get; init; }

    /// <summary>True while the assistant message is still being streamed.</summary>
    public bool IsStreaming { get; private set; }

    public void Append(string text)
    {
        Text += text;
        RaisePropertyChanged(nameof(Text));
    }

    public void MarkStreamingComplete()
    {
        IsStreaming = false;
        RaisePropertyChanged(nameof(IsStreaming));
    }

    public void MarkStreaming()
    {
        IsStreaming = true;
        RaisePropertyChanged(nameof(IsStreaming));
    }
}
