using System;
using System.Collections.Generic;

namespace AiraAssistant.Models;

/// <summary>
/// A provider-agnostic description of a function the assistant can call.
/// Serialized to provider-specific JSON by the AI service implementations.
/// </summary>
public sealed class FunctionDefinition
{
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// JSON Schema object describing the parameters. Kept as a dictionary so
    /// we can serialize it verbatim to either OpenAI or Anthropic.
    /// </summary>
    public Dictionary<string, object?> Parameters { get; init; } = new();
}
