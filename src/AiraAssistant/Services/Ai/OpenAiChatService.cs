using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using AiraAssistant.Infrastructure.Logging;
using AiraAssistant.Models;

namespace AiraAssistant.Services.Ai;

/// <summary>
/// OpenAI Chat Completions client. Streams the assistant response (SSE),
/// parses tool-call deltas, and posts tool results back so the model can
/// finish its turn.
///
/// v1.1 fixes:
///  - The caller's Conversation object is reused directly, so assistant/tool
///    turns stay in the caller's history (no context loss between turns).
///  - Multi-tool turns are persisted with ALL tool calls and results are
///    awaited before a single continuation request (OpenAI requires every
///    prior tool_call_id echoed back; the old code echoed only the first).
///  - Tool rounds are capped so a looping model can't spin forever.
/// </summary>
public sealed class OpenAiChatService : IAiService
{
    private const int MaxToolRounds = 8;

    private readonly string _apiKey;
    private readonly string _model;
    private readonly double _temperature;
    private readonly string _systemPrompt;
    private readonly IToolCatalog _toolCatalog;
    private readonly ILogger _logger;
    private readonly IHttpClientFactory _httpFactory;

    private Conversation? _activeConversation;
    private int _pendingToolCalls;
    private int _toolRounds;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly JsonSerializerOptions WriteOpts = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public event EventHandler<AiChunkEventArgs>? ResponseChunkReceived;
    public event EventHandler<AiCompletedEventArgs>? ResponseCompleted;
    public event EventHandler<AiFunctionCallEventArgs>? FunctionCallRequested;
    public event EventHandler<AiErrorEventArgs>? ErrorOccurred;

    public string ProviderName => "OpenAI";
    public string ModelName => _model;

    public OpenAiChatService(
        string apiKey,
        string model,
        string systemPrompt,
        double temperature,
        IToolCatalog toolCatalog,
        ILogger logger,
        IHttpClientFactory httpFactory)
    {
        _apiKey = apiKey;
        _model = model;
        _systemPrompt = systemPrompt;
        _temperature = temperature;
        _toolCatalog = toolCatalog;
        _logger = logger;
        _httpFactory = httpFactory;
    }

    public string GetSystemPrompt() => _systemPrompt;

    // Caller owns the conversation: assistant/tool turns are appended to it
    // and survive into the next user turn.
    public Task SendUserMessageAsync(Conversation conversation, CancellationToken cancellationToken = default)
    {
        _activeConversation = conversation ?? throw new ArgumentNullException(nameof(conversation));
        Interlocked.Exchange(ref _pendingToolCalls, 0);
        _toolRounds = 0;
        return RunToCompletionAsync(conversation, cancellationToken);
    }

    // Snapshot entry point used when the caller cannot keep a Conversation.
    public async Task SendUserMessageAsync(IReadOnlyList<ChatMessage> messages, CancellationToken cancellationToken = default)
    {
        _activeConversation = new Conversation();
        foreach (var m in messages) _activeConversation.Add(m);
        Interlocked.Exchange(ref _pendingToolCalls, 0);
        _toolRounds = 0;
        try
        {
            await RunToCompletionAsync(_activeConversation, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _activeConversation = null;
        }
    }

    public async Task SubmitFunctionResultAsync(string callId, string functionName, string resultJson, CancellationToken cancellationToken = default)
    {
        var conv = _activeConversation;
        if (conv is null)
        {
            _logger.Warning("Received tool result with no active conversation; dropping.");
            return;
        }

        conv.Add(new ChatMessage
        {
            Role = ChatRole.Tool,
            Content = resultJson,
            ToolCallId = callId,
            ToolName = functionName
        });

        var remaining = Interlocked.Decrement(ref _pendingToolCalls);
        if (remaining > 0)
        {
            // More tool results still expected for this assistant turn.
            return;
        }
        if (remaining < 0)
        {
            // Stray result (e.g. after the turn already completed); ignore.
            Interlocked.Increment(ref _pendingToolCalls);
            return;
        }

        // Last result arrived for this round → stream the continuation.
        await RunToCompletionAsync(conv, cancellationToken).ConfigureAwait(false);
    }

    private async Task RunToCompletionAsync(Conversation conv, CancellationToken ct)
    {
        _toolRounds++;
        var includeTools = _toolRounds <= MaxToolRounds;
        if (!includeTools)
        {
            _logger.Warning($"Tool round limit ({MaxToolRounds}) reached; asking the model for a plain answer.");
        }

        var payload = BuildRequest(conv.GetMessagesForModel(), includeTools);
        await StreamAsync(payload, conv, ct).ConfigureAwait(false);
    }

    // -----------------------------------------------------------------
    // Internal: build, stream, parse.
    // -----------------------------------------------------------------

    private OpenAiChatRequest BuildRequest(IReadOnlyList<ChatMessage> messages, bool includeTools)
    {
        var req = new OpenAiChatRequest { Model = _model, Stream = true, Temperature = _temperature };

        foreach (var m in messages)
        {
            var om = new OpenAiChatMessage
            {
                Role = RoleToString(m.Role),
                Content = m.Content
            };

            if (m.Role == ChatRole.Assistant && m.ToolCalls is { Count: > 0 })
            {
                // Re-emit ALL tool calls so OpenAI can match every subsequent
                // tool message ("tool_call_id") — this is what the old single-call
                // persistence broke.
                om.Content = string.IsNullOrEmpty(m.Content) ? null : m.Content;
                om.ToolCalls = m.ToolCalls.Select(tc => new OpenAiToolCall
                {
                    Id = string.IsNullOrEmpty(tc.Id) ? Guid.NewGuid().ToString("N") : tc.Id,
                    Function = new OpenAiToolCallFunction
                    {
                        Name = string.IsNullOrEmpty(tc.Name) ? "unknown" : tc.Name,
                        Arguments = string.IsNullOrEmpty(tc.ArgumentsJson) ? "{}" : tc.ArgumentsJson
                    }
                }).ToList();
            }
            else if (m.Role == ChatRole.Tool)
            {
                om.ToolCallId = m.ToolCallId;
                om.Name = string.IsNullOrEmpty(m.ToolName) ? null : m.ToolName;
                if (string.IsNullOrEmpty(om.Content)) om.Content = "{}";
            }

            req.Messages.Add(om);
        }

        if (includeTools)
        {
            var funcs = _toolCatalog.GetFunctions();
            if (funcs.Count > 0)
            {
                req.Tools = new List<OpenAiTool>();
                foreach (var f in funcs)
                {
                    req.Tools.Add(new OpenAiTool
                    {
                        Function = new OpenAiFunction
                        {
                            Name = f.Name,
                            Description = f.Description,
                            Parameters = f.Parameters
                        }
                    });
                }
            }
        }
        else
        {
            req.ToolChoice = "none";
        }

        return req;
    }

    private async Task StreamAsync(OpenAiChatRequest payload, Conversation conversation, CancellationToken ct)
    {
        var client = _httpFactory.CreateClient("openai");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

        const string url = "https://api.openai.com/v1/chat/completions";
        var json = JsonSerializer.Serialize(payload, WriteOpts);

        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            _logger.Error($"OpenAI HTTP {resp.StatusCode}: {body}");
            ErrorOccurred?.Invoke(this, new AiErrorEventArgs($"HTTP {(int)resp.StatusCode}: {Truncate(body, 200)}"));
            return;
        }

        await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        var contentBuffer = new StringBuilder();
        // tool calls: keyed by streaming index, accumulate id + name + arguments.
        var toolCalls = new SortedDictionary<int, (string id, string name, StringBuilder args)>();

        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            if (string.IsNullOrEmpty(line)) continue;
            if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;
            var data = line["data:".Length..].Trim();
            if (data == "[DONE]") break;
            if (string.IsNullOrEmpty(data)) continue;

            OpenAiStreamChunk? chunk;
            try { chunk = JsonSerializer.Deserialize<OpenAiStreamChunk>(data, JsonOpts); }
            catch (Exception ex)
            {
                _logger.Warning($"Skipped malformed chunk: {ex.Message}");
                continue;
            }
            if (chunk is null || chunk.Choices.Count == 0) continue;

            var delta = chunk.Choices[0].Delta;
            if (!string.IsNullOrEmpty(delta.Content))
            {
                contentBuffer.Append(delta.Content);
                ResponseChunkReceived?.Invoke(this, new AiChunkEventArgs(delta.Content));
            }
            if (delta.ToolCalls is { Count: > 0 })
            {
                foreach (var tc in delta.ToolCalls)
                {
                    var idx = tc.Index;
                    if (!toolCalls.TryGetValue(idx, out var existing))
                    {
                        existing = (tc.Id ?? string.Empty, tc.Function?.Name ?? string.Empty, new StringBuilder());
                        toolCalls[idx] = existing;
                    }
                    var (id, name, args) = existing;
                    if (!string.IsNullOrEmpty(tc.Id)) id = tc.Id!;
                    if (tc.Function is not null)
                    {
                        if (!string.IsNullOrEmpty(tc.Function.Name)) name = tc.Function.Name!;
                        if (!string.IsNullOrEmpty(tc.Function.Arguments)) args.Append(tc.Function.Arguments);
                    }
                    toolCalls[idx] = (id, name, args);
                }
            }
        }

        // 1. Tool-call turn: persist the assistant message with ALL calls,
        //    then dispatch each through the router (pending counter gates the
        //    single continuation request after the last result).
        if (toolCalls.Count > 0)
        {
            var calls = new List<ToolCallInfo>(toolCalls.Count);
            foreach (var (_, call) in toolCalls)
            {
                calls.Add(new ToolCallInfo
                {
                    Id = string.IsNullOrEmpty(call.id) ? Guid.NewGuid().ToString("N") : call.id,
                    Name = call.name,
                    ArgumentsJson = call.args.ToString()
                });
            }

            conversation.Add(new ChatMessage
            {
                Role = ChatRole.Assistant,
                Content = contentBuffer.ToString(),
                ToolCalls = calls
            });

            Interlocked.Exchange(ref _pendingToolCalls, calls.Count);
            foreach (var call in calls)
            {
                _logger.Information($"OpenAI tool call: {call.Name}({call.ArgumentsJson})");
                FunctionCallRequested?.Invoke(this, new AiFunctionCallEventArgs(call.Id, call.Name, call.ArgumentsJson));
            }
            return;
        }

        // 2. Plain text turn.
        var fullText = contentBuffer.ToString();
        conversation.Add(new ChatMessage
        {
            Role = ChatRole.Assistant,
            Content = fullText
        });
        ResponseCompleted?.Invoke(this, new AiCompletedEventArgs(fullText));
    }

    private static string RoleToString(ChatRole r) => r switch
    {
        ChatRole.System => "system",
        ChatRole.User => "user",
        ChatRole.Assistant => "assistant",
        ChatRole.Tool => "tool",
        _ => "user"
    };

    private static string Truncate(string s, int n) => s.Length <= n ? s : s.Substring(0, n) + "…";
}
