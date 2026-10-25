using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AiraAssistant.Infrastructure.Logging;
using AiraAssistant.Models;

namespace AiraAssistant.Services.Ai;

/// <summary>
/// Graceful fallback when no API key is configured. Instead of throwing in
/// AiServiceFactory (which used to abort the whole application with a fatal
/// dialog), Aira starts normally and the first chat attempt shows actionable
/// guidance; the in-app Settings dialog collects and stores the key.
/// </summary>
public sealed class UnconfiguredAiService : IAiService
{
    private readonly string _provider;
    private readonly ILogger _logger;

    public UnconfiguredAiService(string provider, ILogger logger)
    {
        _provider = provider;
        _logger = logger;
    }

    public string ProviderName => _provider;
    public string ModelName => "(not configured)";
    public string GetSystemPrompt() => string.Empty;

    // Interface-complete stub events; the implementation never streams.
#pragma warning disable CS0067 // event is never used (intentional stub)
    public event EventHandler<AiChunkEventArgs>? ResponseChunkReceived;
    public event EventHandler<AiCompletedEventArgs>? ResponseCompleted;
    public event EventHandler<AiFunctionCallEventArgs>? FunctionCallRequested;
    public event EventHandler<AiErrorEventArgs>? ErrorOccurred;
#pragma warning restore CS0067

    public Task SendUserMessageAsync(Conversation conversation, CancellationToken cancellationToken = default)
    {
        RaiseMissingKey();
        return Task.CompletedTask;
    }

    public Task SendUserMessageAsync(IReadOnlyList<ChatMessage> messages, CancellationToken cancellationToken = default)
    {
        RaiseMissingKey();
        return Task.CompletedTask;
    }

    public Task SubmitFunctionResultAsync(string callId, string functionName, string resultJson, CancellationToken cancellationToken = default)
    {
        // No pending turn exists in this state; nothing to do.
        return Task.CompletedTask;
    }

    private void RaiseMissingKey()
    {
        var message =
            $"No {_provider} API key is configured.\n" +
            "Click ⚙ (Settings), paste your key into the API key field and press Save — " +
            "it is stored DPAPI-encrypted — then restart Aira.\n" +
            "(Alternative: set Ai." + _provider + "ApiKey in appsettings.json next to the app.)";
        _logger.Warning(message);
        ErrorOccurred?.Invoke(this, new AiErrorEventArgs(message));
    }
}
