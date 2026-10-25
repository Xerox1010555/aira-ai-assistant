using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AiraAssistant.Services.Ai;

// =========================================================================
// OpenAI Chat Completions wire format (https://platform.openai.com/docs/api-reference/chat)
// Kept as DTOs separate from the IAiService so we can swap providers cleanly.
// =========================================================================

internal sealed class OpenAiChatRequest
{
    [JsonPropertyName("model")] public string Model { get; set; } = string.Empty;
    [JsonPropertyName("messages")] public List<OpenAiChatMessage> Messages { get; set; } = new();
    [JsonPropertyName("tools")] public List<OpenAiTool>? Tools { get; set; }
    [JsonPropertyName("tool_choice")] public string? ToolChoice { get; set; } = "auto";
    [JsonPropertyName("stream")] public bool Stream { get; set; } = true;
    [JsonPropertyName("temperature")] public double Temperature { get; set; } = 0.4;
}

internal sealed class OpenAiChatMessage
{
    [JsonPropertyName("role")] public string Role { get; set; } = string.Empty;
    [JsonPropertyName("content")] public string? Content { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("tool_call_id")] public string? ToolCallId { get; set; }
    [JsonPropertyName("tool_calls")] public List<OpenAiToolCall>? ToolCalls { get; set; }
}

internal sealed class OpenAiTool
{
    [JsonPropertyName("type")] public string Type { get; set; } = "function";
    [JsonPropertyName("function")] public OpenAiFunction Function { get; set; } = new();
}

internal sealed class OpenAiFunction
{
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("description")] public string Description { get; set; } = string.Empty;
    [JsonPropertyName("parameters")] public object Parameters { get; set; } = new { };
}

internal sealed class OpenAiToolCall
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("type")] public string Type { get; set; } = "function";
    [JsonPropertyName("function")] public OpenAiToolCallFunction Function { get; set; } = new();
}

internal sealed class OpenAiToolCallFunction
{
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("arguments")] public string Arguments { get; set; } = string.Empty;
}

// Streaming chunk payload.
internal sealed class OpenAiStreamChunk
{
    [JsonPropertyName("choices")] public List<OpenAiStreamChoice> Choices { get; set; } = new();
}

internal sealed class OpenAiStreamChoice
{
    [JsonPropertyName("delta")] public OpenAiStreamDelta Delta { get; set; } = new();
    [JsonPropertyName("finish_reason")] public string? FinishReason { get; set; }
}

internal sealed class OpenAiStreamDelta
{
    [JsonPropertyName("role")] public string? Role { get; set; }
    [JsonPropertyName("content")] public string? Content { get; set; }
    /// <summary>Streaming tool-call deltas are matched back together by <c>index</c>.</summary>
    [JsonPropertyName("tool_calls")] public List<OpenAiToolCallDelta>? ToolCalls { get; set; }
}

/// <summary>One incremental tool-call delta. Multiple deltas for the same call share the same Index.</summary>
internal sealed class OpenAiToolCallDelta
{
    [JsonPropertyName("index")] public int Index { get; set; }
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("type")] public string? Type { get; set; }
    [JsonPropertyName("function")] public OpenAiToolCallFunction? Function { get; set; }
}
