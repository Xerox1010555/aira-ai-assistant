using System;
using System.Collections.Generic;

namespace AiraAssistant.Models;

/// <summary>
/// One assistant tool call (the "function" the model asked us to run).
/// A single assistant turn may carry MANY calls (multi-tool flow).
/// </summary>
public sealed class ToolCallInfo
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string ArgumentsJson { get; init; } = string.Empty;
}

/// <summary>
/// A single message in the conversation. Supports tool-call and tool-result payloads
/// so that function-calling conversations round-trip cleanly between turns.
/// </summary>
public sealed class ChatMessage
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public ChatRole Role { get; init; }
    public string Content { get; init; } = string.Empty;

    /// <summary>For Tool-role messages: the id of the call that produced this result.</summary>
    public string? ToolCallId { get; init; }

    /// <summary>For Tool-role messages: the tool that was called.</summary>
    public string? ToolName { get; init; }

    /// <summary>
    /// For Assistant messages that issued one or more tool calls. Multi-call turns
    /// carry every call so providers like OpenAI can match all subsequent tool results.
    /// </summary>
    public IReadOnlyList<ToolCallInfo>? ToolCalls { get; init; }

    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}
