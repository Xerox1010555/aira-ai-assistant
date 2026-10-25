using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AiraAssistant.Services.Ai;

// =========================================================================
// Anthropic Messages API wire format (https://docs.anthropic.com/claude/reference/messages_post)
// =========================================================================

internal sealed class AnthropicMessageRequest
{
    [JsonPropertyName("model")] public string Model { get; set; } = string.Empty;
    [JsonPropertyName("max_tokens")] public int MaxTokens { get; set; } = 1024;
    [JsonPropertyName("system")] public string? System { get; set; }
    [JsonPropertyName("messages")] public List<AnthropicMessage> Messages { get; set; } = new();
    [JsonPropertyName("tools")] public List<AnthropicTool>? Tools { get; set; }
    [JsonPropertyName("stream")] public bool Stream { get; set; } = true;
    [JsonPropertyName("temperature")] public double Temperature { get; set; } = 0.4;
}

internal sealed class AnthropicMessage
{
    [JsonPropertyName("role")] public string Role { get; set; } = string.Empty;
    [JsonPropertyName("content")] public List<AnthropicContentBlock> Content { get; set; } = new();
}

internal sealed class AnthropicContentBlock
{
    [JsonPropertyName("type")] public string Type { get; set; } = "text";
    [JsonPropertyName("text")] public string? Text { get; set; }
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("input")] public Dictionary<string, object?>? Input { get; set; }
    [JsonPropertyName("tool_use_id")] public string? ToolUseId { get; set; }
    [JsonPropertyName("content")] public object? ToolResultContent { get; set; }
    [JsonPropertyName("is_error")] public bool? IsError { get; set; }
}

internal sealed class AnthropicTool
{
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("description")] public string Description { get; set; } = string.Empty;
    [JsonPropertyName("input_schema")] public object InputSchema { get; set; } = new { };
}

// Streaming event frames.
internal sealed class AnthropicStreamEvent
{
    [JsonPropertyName("type")] public string Type { get; set; } = string.Empty;

    [JsonPropertyName("message")] public AnthropicStreamMessage? Message { get; set; }
    [JsonPropertyName("index")] public int? Index { get; set; }

    [JsonPropertyName("content_block")] public AnthropicStreamContentBlock? ContentBlock { get; set; }

    [JsonPropertyName("delta")] public AnthropicStreamDelta? Delta { get; set; }
}

internal sealed class AnthropicStreamMessage
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("model")] public string? Model { get; set; }
    [JsonPropertyName("stop_reason")] public string? StopReason { get; set; }
}

internal sealed class AnthropicStreamContentBlock
{
    [JsonPropertyName("type")] public string Type { get; set; } = string.Empty;
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("input")] public Dictionary<string, object?>? Input { get; set; }
}

internal sealed class AnthropicStreamDelta
{
    [JsonPropertyName("type")] public string Type { get; set; } = "text";
    [JsonPropertyName("text")] public string? Text { get; set; }
    [JsonPropertyName("partial_json")] public string? PartialJson { get; set; }
    [JsonPropertyName("stop_reason")] public string? StopReason { get; set; }
}
