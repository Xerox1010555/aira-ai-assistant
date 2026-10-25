using System;
using System.Collections.Generic;
using AiraAssistant.Models;
using AiraAssistant.Services.SystemControl;

namespace AiraAssistant.Services.Ai;

/// <summary>
/// Bridge between tool calls issued by the AI and the OS-level actions
/// that satisfy them. Returns a JSON-serializable result so the AI service
/// can feed it back into the model verbatim.
/// </summary>
public interface IFunctionRouter
{
    Task<string> ExecuteAsync(string functionName, string argumentsJson);

    event EventHandler<ToolExecutedEventArgs>? ToolExecuted;
}

public sealed class ToolExecutedEventArgs : EventArgs
{
    public string ToolName { get; }
    public string Result { get; }
    public ToolExecutedEventArgs(string toolName, string result)
    {
        ToolName = toolName;
        Result = result;
    }
}
