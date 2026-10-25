using System.Collections.Generic;
using AiraAssistant.Models;

namespace AiraAssistant.Services.Ai;

/// <summary>
/// Single source of truth for the tool definitions exposed to the LLM.
/// Each function here is implemented in <see cref="FunctionRouter"/>.
/// </summary>
public interface IToolCatalog
{
    IReadOnlyList<FunctionDefinition> GetFunctions();
}

public sealed class ToolCatalog : IToolCatalog
{
    public IReadOnlyList<FunctionDefinition> GetFunctions() => new[]
    {
        new FunctionDefinition
        {
            Name = "open_url",
            Description = "Open a URL in the user's default web browser.",
            Parameters = new Dictionary<string, object?>
            {
                ["type"] = "object",
                ["properties"] = new Dictionary<string, object?>
                {
                    ["url"] = new Dictionary<string, object?>
                    {
                        ["type"] = "string",
                        ["description"] = "Fully qualified URL, e.g. https://www.youtube.com"
                    }
                },
                ["required"] = new[] { "url" }
            }
        },
        new FunctionDefinition
        {
            Name = "launch_app",
            Description = "Launch a Windows application by its friendly name.",
            Parameters = new Dictionary<string, object?>
            {
                ["type"] = "object",
                ["properties"] = new Dictionary<string, object?>
                {
                    ["name"] = new Dictionary<string, object?>
                    {
                        ["type"] = "string",
                        ["description"] = "One of: chrome, firefox, edge, youtube, calculator, notepad, cmd, powershell, explorer, task_manager, settings"
                    }
                },
                ["required"] = new[] { "name" }
            }
        },
        new FunctionDefinition
        {
            Name = "set_volume",
            Description = "Set the system master volume to a value between 0 and 100.",
            Parameters = new Dictionary<string, object?>
            {
                ["type"] = "object",
                ["properties"] = new Dictionary<string, object?>
                {
                    ["level"] = new Dictionary<string, object?>
                    {
                        ["type"] = "integer",
                        ["minimum"] = 0,
                        ["maximum"] = 100,
                        ["description"] = "Volume level 0..100"
                    }
                },
                ["required"] = new[] { "level" }
            }
        },
        new FunctionDefinition
        {
            Name = "mute_volume",
            Description = "Mute or unmute the system master audio.",
            Parameters = new Dictionary<string, object?>
            {
                ["type"] = "object",
                ["properties"] = new Dictionary<string, object?>
                {
                    ["muted"] = new Dictionary<string, object?>
                    {
                        ["type"] = "boolean",
                        ["description"] = "True to mute, false to unmute"
                    }
                },
                ["required"] = new[] { "muted" }
            }
        },
        new FunctionDefinition
        {
            Name = "lock_workstation",
            Description = "Lock the workstation (Windows+L equivalent).",
            Parameters = new Dictionary<string, object?>
            {
                ["type"] = "object",
                ["properties"] = new Dictionary<string, object?>(),
                ["required"] = new string[0]
            }
        },
        new FunctionDefinition
        {
            Name = "sleep_system",
            Description = "Put the computer to sleep.",
            Parameters = new Dictionary<string, object?>
            {
                ["type"] = "object",
                ["properties"] = new Dictionary<string, object?>(),
                ["required"] = new string[0]
            }
        },
        new FunctionDefinition
        {
            Name = "shutdown_system",
            Description = "Shut down Windows. Asks for confirmation in the UI.",
            Parameters = new Dictionary<string, object?>
            {
                ["type"] = "object",
                ["properties"] = new Dictionary<string, object?>
                {
                    ["delaySeconds"] = new Dictionary<string, object?>
                    {
                        ["type"] = "integer",
                        ["description"] = "How long to wait before shutting down, in seconds (0..3600).",
                        ["minimum"] = 0,
                        ["maximum"] = 3600
                    }
                },
                ["required"] = new string[0]
            }
        },
        new FunctionDefinition
        {
            Name = "type_text",
            Description = "Type text into whatever window has focus, as if typed on the keyboard. Asks for confirmation in the UI.",
            Parameters = new Dictionary<string, object?>
            {
                ["type"] = "object",
                ["properties"] = new Dictionary<string, object?>
                {
                    ["text"] = new Dictionary<string, object?>
                    {
                        ["type"] = "string",
                        ["description"] = "The exact text to type."
                    }
                },
                ["required"] = new[] { "text" }
            }
        },
        new FunctionDefinition
        {
            Name = "clipboard_write",
            Description = "Replace the clipboard content with text. Asks for confirmation in the UI.",
            Parameters = new Dictionary<string, object?>
            {
                ["type"] = "object",
                ["properties"] = new Dictionary<string, object?>
                {
                    ["text"] = new Dictionary<string, object?>
                    {
                        ["type"] = "string",
                        ["description"] = "The text to place on the clipboard."
                    }
                },
                ["required"] = new[] { "text" }
            }
        }
    };
}
