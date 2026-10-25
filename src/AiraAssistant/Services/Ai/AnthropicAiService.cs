using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using AiraAssistant.Infrastructure;
using AiraAssistant.Infrastructure.Logging;
using AiraAssistant.Models;

namespace AiraAssistant.Services.Ai;

/// <summary>
/// Anthropic Messages API client (server-sent events). Supports tool_use and
/// feeds tool_result back to the model in a follow-up call.
///
/// v1.1 fixes:
///  - Reuses the caller's Conversation (no context loss).
///  - Multi-tool turns: every tool_use block is persisted; consecutive tool
///    results are GROUPED into one user message before continuing (Anthropic
///    requires each tool_result to sit next to its assistant tool_use turn).
///  - The invalid "Authorization: Bearer" duplicate header was removed —
///    Anthropic authenticates with `x-api-key` only.
///  - Tool rounds are capped so a looping model can't spin forever.
/// </summary>
public sealed class AnthropicAiService : IAiService
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

    public string ProviderName => "Anthropic";
    public string ModelName => _model;

    public AnthropicAiService(
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

    // Caller owns the conversation.
    public Task SendUserMessageAsync(Conversation conversation, CancellationToken cancellationToken = default)
    {
        _activeConversation = conversation ?? throw new ArgumentNullException(nameof(conversation));
        Interlocked.Exchange(ref _pendingToolCalls, 0);
        _toolRounds = 0;
        return RunToCompletionAsync(conversation, cancellationToken);
    }

    // Snapshot entry point.
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
        if (remaining > 0) return;                       // wait for the rest
        if (remaining < 0) { Interlocked.Increment(ref _pendingToolCalls); return; } // stray

        await RunToCompletionAsync(conv, cancellationToken).ConfigureAwait(false);
    }

    private async Task RunToCompletionAsync(Conversation conv, CancellationToken ct)
    {
        _toolRounds++;
        var includeTools = _toolRounds <= MaxToolRounds;
        var req = BuildRequest(conv.GetMessagesForModel(), includeTools);
        await StreamAsync(req, conv, ct).ConfigureAwait(false);
    }

    // -----------------------------------------------------------------

    private AnthropicMessageRequest BuildRequest(IReadOnlyList<ChatMessage> messages, bool includeTools)
    {
        var req = new AnthropicMessageRequest
        {
            Model = _model,
            System = string.IsNullOrWhiteSpace(_systemPrompt) ? null : _systemPrompt,
            Stream = true,
            Temperature = Math.Clamp(_temperature, 0.0, 1.0),
            MaxTokens = 2048
        };

        var i = 0;
        while (i < messages.Count)
        {
            var m = messages[i];
            if (m.Role == ChatRole.System) { i++; continue; } // top-level "system"

            if (m.Role == ChatRole.Tool)
            {
                // GROUP consecutive tool results into ONE user message. Anthropic
                // rejects stray tool_result blocks that don't follow their
                // assistant tool_use turn directly.
                var grouped = new AnthropicMessage { Role = "user" };
                while (i < messages.Count && messages[i].Role == ChatRole.Tool)
                {
                    grouped.Content.Add(new AnthropicContentBlock
                    {
                        Type = "tool_result",
                        ToolUseId = messages[i].ToolCallId,
                        ToolResultContent = messages[i].Content,
                        IsError = false
                    });
                    i++;
                }
                req.Messages.Add(grouped);
                continue;
            }

            var am = new AnthropicMessage { Role = m.Role == ChatRole.Assistant ? "assistant" : "user" };

            if (m.Role == ChatRole.Assistant && m.ToolCalls is { Count: > 0 })
            {
                if (!string.IsNullOrEmpty(m.Content))
                {
                    am.Content.Add(new AnthropicContentBlock { Type = "text", Text = m.Content });
                }
                foreach (var tc in m.ToolCalls)
                {
                    am.Content.Add(new AnthropicContentBlock
                    {
                        Type = "tool_use",
                        Id = string.IsNullOrEmpty(tc.Id) ? Guid.NewGuid().ToString("N") : tc.Id,
                        Name = string.IsNullOrEmpty(tc.Name) ? "unknown" : tc.Name,
                        Input = ParseJson(tc.ArgumentsJson) ?? new Dictionary<string, object?>()
                    });
                }
            }
            else
            {
                am.Content.Add(new AnthropicContentBlock { Type = "text", Text = m.Content ?? string.Empty });
            }
            req.Messages.Add(am);
            i++;
        }

        if (includeTools)
        {
            var funcs = _toolCatalog.GetFunctions();
            if (funcs.Count > 0)
            {
                req.Tools = new List<AnthropicTool>();
                foreach (var f in funcs)
                {
                    req.Tools.Add(new AnthropicTool
                    {
                        Name = f.Name,
                        Description = f.Description,
                        InputSchema = f.Parameters
                    });
                }
            }
        }

        return req;
    }

    private async Task StreamAsync(AnthropicMessageRequest req, Conversation conversation, CancellationToken ct)
    {
        var client = _httpFactory.CreateClient("anthropic");
        client.DefaultRequestHeaders.Add("x-api-key", _apiKey);
        client.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");

        const string url = "https://api.anthropic.com/v1/messages";
        var json = JsonSerializer.Serialize(req, WriteOpts);

        using var httpReq = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        using var resp = await client.SendAsync(httpReq, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            _logger.Error($"Anthropic HTTP {resp.StatusCode}: {body}");
            ErrorOccurred?.Invoke(this, new AiErrorEventArgs($"HTTP {(int)resp.StatusCode}: {Truncate(body, 200)}"));
            return;
        }

        await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        var textBuffer = new StringBuilder();
        // Tool-use accumulator keyed by content-block index.
        var toolUse = new SortedDictionary<int, (string id, string name, StringBuilder args)>();

        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            if (string.IsNullOrEmpty(line)) continue;

            // Anthropic SSE: "event: <type>" lines then "data: <json>" lines.
            if (line.StartsWith("event:", StringComparison.Ordinal)) continue;
            if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;
            var dataLine = line["data:".Length..].Trim();
            if (string.IsNullOrEmpty(dataLine) || dataLine == "[DONE]") continue;

            AnthropicStreamEvent? ev;
            try { ev = JsonSerializer.Deserialize<AnthropicStreamEvent>(dataLine, JsonOpts); }
            catch (Exception ex)
            {
                _logger.Warning($"Skipped malformed Anthropic event: {ex.Message}");
                continue;
            }
            if (ev is null) continue;

            switch (ev.Type)
            {
                case "content_block_start":
                {
                    if (ev.ContentBlock?.Type == "tool_use" && ev.Index is int idx)
                    {
                        toolUse[idx] = (ev.ContentBlock.Id ?? string.Empty,
                                        ev.ContentBlock.Name ?? string.Empty,
                                        new StringBuilder());
                    }
                    break;
                }
                case "content_block_delta":
                {
                    var delta = ev.Delta;
                    if (delta is null) break;
                    if (delta.Type == "text_delta" && !string.IsNullOrEmpty(delta.Text))
                    {
                        textBuffer.Append(delta.Text);
                        ResponseChunkReceived?.Invoke(this, new AiChunkEventArgs(delta.Text!));
                    }
                    else if (delta.Type == "input_json_delta" && ev.Index is int jsonIdx &&
                             toolUse.TryGetValue(jsonIdx, out var existing))
                    {
                        existing.args.Append(delta.PartialJson ?? string.Empty);
                        toolUse[jsonIdx] = existing;
                    }
                    break;
                }
                case "error":
                {
                    var msg = ev.Message?.Id ?? "stream error";
                    _logger.Error($"Anthropic stream error: {msg}");
                    ErrorOccurred?.Invoke(this, new AiErrorEventArgs($"Anthropic stream error: {msg}"));
                    return;
                }
                case "message_stop":
                {
                    if (toolUse.Count > 0)
                    {
                        var calls = new List<ToolCallInfo>(toolUse.Count);
                        foreach (var (_, call) in toolUse)
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
                            Content = textBuffer.ToString(),
                            ToolCalls = calls
                        });

                        Interlocked.Exchange(ref _pendingToolCalls, calls.Count);
                        foreach (var call in calls)
                        {
                            _logger.Information($"Anthropic tool call: {call.Name}({call.ArgumentsJson})");
                            FunctionCallRequested?.Invoke(this,
                                new AiFunctionCallEventArgs(call.Id, call.Name, call.ArgumentsJson));
                        }
                        return;
                    }

                    var fullText = textBuffer.ToString();
                    conversation.Add(new ChatMessage { Role = ChatRole.Assistant, Content = fullText });
                    ResponseCompleted?.Invoke(this, new AiCompletedEventArgs(fullText));
                    return;
                }
            }
        }
    }

    private static Dictionary<string, object?>? ParseJson(string s)
    {
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(s) ? "{}" : s);
            return JsonElementToDict(doc.RootElement);
        }
        catch
        {
            return null;
        }
    }

    private static Dictionary<string, object?> JsonElementToDict(JsonElement el)
    {
        var dict = new Dictionary<string, object?>();
        if (el.ValueKind != JsonValueKind.Object) return dict;
        foreach (var p in el.EnumerateObject())
        {
            dict[p.Name] = JsonElementToValue(p.Value);
        }
        return dict;
    }

    private static object? JsonElementToValue(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.String => el.GetString(),
        JsonValueKind.Number => el.TryGetInt64(out var l) ? l : el.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        JsonValueKind.Object => JsonElementToDict(el),
        JsonValueKind.Array => el.EnumerateArray().Select(JsonElementToValue).ToArray(),
        _ => null
    };

    private static string Truncate(string s, int n) => s.Length <= n ? s : s.Substring(0, n) + "…";
}
