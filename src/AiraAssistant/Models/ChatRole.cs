using System;

namespace AiraAssistant.Models;

/// <summary>
/// Role of a participant in a chat conversation. Mirrors the OpenAI/Anthropic role semantics.
/// </summary>
public enum ChatRole
{
    System,
    User,
    Assistant,
    Tool
}
