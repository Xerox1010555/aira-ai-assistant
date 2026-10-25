using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AiraAssistant.Infrastructure;
using AiraAssistant.Infrastructure.Logging;
using AiraAssistant.Services.SystemControl;

namespace AiraAssistant.Services.Ai;

/// <summary>
/// Bridge between tool calls issued by the AI and the OS-level actions
/// that satisfy them. Returns a JSON-serializable result so the AI service
/// can feed it back into the model verbatim.
///
/// v1.1: destructive tools (shutdown/sleep/lock/type/clipboard-write) are
/// gated behind a user confirmation when <c>Tools.RequireConfirmation</c>
/// is enabled — the old code executed whatever the LLM asked, immediately.
/// </summary>
public sealed class FunctionRouter : IFunctionRouter
{
    private static readonly HashSet<string> DestructiveTools = new(StringComparer.OrdinalIgnoreCase)
    {
        "shutdown_system",
        "sleep_system",
        "lock_workstation",
        "type_text",
        "clipboard_write"
    };

    private readonly ISystemControlService _system;
    private readonly ILogger _logger;
    private readonly bool _requireConfirmation;

    /// <summary>
    /// Wired by <see cref="AppBootstrapper"/> to a UI confirmation dialog.
    /// Returns true when the user approves the action.
    /// </summary>
    public Func<string, string, Task<bool>>? ConfirmationHandler { get; set; }

    public event EventHandler<ToolExecutedEventArgs>? ToolExecuted;

    public FunctionRouter(ISystemControlService system, ILogger logger, IAppConfig config)
    {
        _system = system;
        _logger = logger;
        _requireConfirmation = config.Tools.RequireConfirmation;
    }

    public async Task<string> ExecuteAsync(string functionName, string argumentsJson)
    {
        if (string.IsNullOrWhiteSpace(functionName))
        {
            return Result(false, "Empty function name.");
        }

        _logger.Information($"Tool call: {functionName} ({argumentsJson})");

        try
        {
            if (_requireConfirmation && DestructiveTools.Contains(functionName) && ConfirmationHandler is not null)
            {
                var approved = await ConfirmationHandler(functionName, argumentsJson).ConfigureAwait(false);
                if (!approved)
                {
                    return Result(false, "The user declined this action.");
                }
            }

            switch (functionName.ToLowerInvariant())
            {
                case "open_url":
                {
                    var args = JsonArg.Parse(argumentsJson);
                    var url = args.GetString("url");
                    if (string.IsNullOrWhiteSpace(url)) return Result(false, "Missing 'url'.");
                    if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                    {
                        // Allow bare hostnames by promoting them to https.
                        uri = new Uri("https://" + url);
                    }
                    await _system.OpenUrlAsync(uri).ConfigureAwait(false);
                    return Result(true, $"Opened {uri}");
                }
                case "launch_app":
                {
                    var args = JsonArg.Parse(argumentsJson);
                    var name = args.GetString("name");
                    if (string.IsNullOrWhiteSpace(name)) return Result(false, "Missing 'name'.");
                    await _system.LaunchAppAsync(name!).ConfigureAwait(false);
                    return Result(true, $"Launched {name}");
                }
                case "set_volume":
                {
                    var args = JsonArg.Parse(argumentsJson);
                    var level = args.GetInt("level", 50);
                    await _system.SetVolumeAsync(level).ConfigureAwait(false);
                    return Result(true, $"Volume set to {level}%");
                }
                case "mute_volume":
                {
                    var args = JsonArg.Parse(argumentsJson);
                    var muted = args.GetBool("muted", true);
                    await _system.SetMuteAsync(muted).ConfigureAwait(false);
                    return Result(true, muted ? "Muted." : "Unmuted.");
                }
                case "lock_workstation":
                {
                    await _system.LockAsync().ConfigureAwait(false);
                    return Result(true, "Workstation locked.");
                }
                case "sleep_system":
                {
                    await _system.SleepAsync().ConfigureAwait(false);
                    return Result(true, "Sleep requested.");
                }
                case "shutdown_system":
                {
                    var args = JsonArg.Parse(argumentsJson);
                    var delay = args.GetInt("delaySeconds", 0);
                    await _system.ShutdownAsync(delay).ConfigureAwait(false);
                    return Result(true, $"Shutdown requested (delay {delay}s).");
                }
                case "type_text":
                {
                    var args = JsonArg.Parse(argumentsJson);
                    var text = args.GetString("text");
                    if (string.IsNullOrEmpty(text)) return Result(false, "Missing 'text'.");
                    await _system.TypeTextAsync(text!).ConfigureAwait(false);
                    return Result(true, $"Typed {text!.Length} characters.");
                }
                case "clipboard_write":
                {
                    var args = JsonArg.Parse(argumentsJson);
                    var text = args.GetString("text");
                    if (string.IsNullOrEmpty(text)) return Result(false, "Missing 'text'.");
                    await _system.WriteClipboardAsync(text!).ConfigureAwait(false);
                    return Result(true, "Copied to clipboard.");
                }
                default:
                    return Result(false, $"Unknown function: {functionName}");
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"Tool {functionName} failed", ex);
            return Result(false, $"{ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            ToolExecuted?.Invoke(this, new ToolExecutedEventArgs(functionName, argumentsJson));
        }
    }

    private static string Result(bool ok, string message) =>
        System.Text.Json.JsonSerializer.Serialize(new { ok, message });
}
