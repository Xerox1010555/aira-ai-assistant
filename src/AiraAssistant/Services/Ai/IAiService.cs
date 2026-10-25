using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AiraAssistant.Models;

namespace AiraAssistant.Services.Ai;

/// <summary>
/// Carries one streamed piece of assistant text. Multiple chunks per turn.
/// </summary>
public sealed class AiChunkEventArgs : EventArgs
{
    public string Text { get; }
    public AiChunkEventArgs(string text) { Text = text; }
}

/// <summary>
/// Emitted when a complete assistant turn has been streamed and committed
/// to the conversation history. The text is the full final reply.
/// </summary>
public sealed class AiCompletedEventArgs : EventArgs
{
    public string FullText { get; }
    public AiCompletedEventArgs(string fullText) { FullText = fullText; }
}

/// <summary>
/// Emitted when the assistant decided to invoke a tool. Subscribers execute
/// the tool and call <see cref="IAiService.SubmitFunctionResultAsync"/>.
/// </summary>
public sealed class AiFunctionCallEventArgs : EventArgs
{
    public string CallId { get; }
    public string FunctionName { get; }
    public string ArgumentsJson { get; }
    public AiFunctionCallEventArgs(string callId, string functionName, string argumentsJson)
    {
        CallId = callId;
        FunctionName = functionName;
        ArgumentsJson = argumentsJson;
    }
}

public sealed class AiErrorEventArgs : EventArgs
{
    public string Message { get; }
    public AiErrorEventArgs(string message) { Message = message; }
}

/// <summary>
/// Cognitive core of the application. The implementation streams the
/// assistant response, raises tool-call events, and accepts tool results
/// so multi-step function-calling flows can be completed in one round trip.
/// </summary>
public interface IAiService
{
    string ProviderName { get; }
    string ModelName { get; }
    string GetSystemPrompt();

    Task SendUserMessageAsync(Conversation conversation, CancellationToken cancellationToken = default);
    Task SendUserMessageAsync(IReadOnlyList<ChatMessage> messages, CancellationToken cancellationToken = default);

    /// <summary>Feed a tool result back to the model and continue streaming.</summary>
    Task SubmitFunctionResultAsync(string callId, string functionName, string resultJson, CancellationToken cancellationToken = default);

    event EventHandler<AiChunkEventArgs> ResponseChunkReceived;
    event EventHandler<AiCompletedEventArgs> ResponseCompleted;
    event EventHandler<AiFunctionCallEventArgs> FunctionCallRequested;
    event EventHandler<AiErrorEventArgs> ErrorOccurred;
}
