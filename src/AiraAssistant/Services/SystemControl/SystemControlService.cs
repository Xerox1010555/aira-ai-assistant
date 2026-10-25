using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using AiraAssistant.Infrastructure;
using AiraAssistant.Infrastructure.Logging;
using CoreAudio;

namespace AiraAssistant.Services.SystemControl;

/// <inheritdoc cref="ISystemControlService" />
public sealed class SystemControlService : ISystemControlService
{
    private readonly ILogger _logger;
    private readonly IAdminService _admin;

    // Map of friendly-name aliases to the actual executable or URI scheme.
    private static readonly IReadOnlyDictionary<string, string> AppAliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["chrome"] = "https://www.google.com",                 // falls back to default browser if Chrome not installed
            ["google"] = "https://www.google.com",
            ["youtube"] = "https://www.youtube.com",
            ["gmail"] = "https://mail.google.com",
            ["github"] = "https://github.com",
            ["firefox"] = "https://www.mozilla.org/firefox",
            ["edge"] = "https://www.microsoft.com/edge",
            ["calculator"] = "calc.exe",
            ["notepad"] = "notepad.exe",
            ["cmd"] = "cmd.exe",
            ["command_prompt"] = "cmd.exe",
            ["powershell"] = "powershell.exe",
            ["explorer"] = "explorer.exe",
            ["file_explorer"] = "explorer.exe",
            ["task_manager"] = "taskmgr.exe",
            ["settings"] = "ms-settings:",
            ["control_panel"] = "control.exe"
        };

    public SystemControlService(ILogger logger, IAdminService admin)
    {
        _logger = logger;
        _admin = admin;
    }

    public Task OpenUrlAsync(Uri uri)
    {
        _logger.Information($"OpenUrl {uri}");
        var psi = new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true };
        Process.Start(psi);
        return Task.CompletedTask;
    }

    public async Task LaunchAppAsync(string friendlyName)
    {
        _logger.Information($"LaunchApp {friendlyName}");
        if (!AppAliases.TryGetValue(friendlyName, out var target))
        {
            throw new ArgumentException($"Unknown app: {friendlyName}");
        }

        if (target.StartsWith("http", StringComparison.OrdinalIgnoreCase) ||
            target.StartsWith("ms-", StringComparison.OrdinalIgnoreCase))
        {
            await OpenUrlAsync(new Uri(target)).ConfigureAwait(false);
            return;
        }

        var psi = new ProcessStartInfo
        {
            FileName = target,
            UseShellExecute = true
        };
        Process.Start(psi);
        await Task.CompletedTask.ConfigureAwait(false);
    }

    public Task SetVolumeAsync(int level)
    {
        if (level < 0 || level > 100) throw new ArgumentOutOfRangeException(nameof(level));
        _logger.Information($"SetVolume {level}%");
        var device = GetDefaultDevice();
        device.AudioEndpointVolume.MasterVolumeLevelScalar = level / 100f;
        return Task.CompletedTask;
    }

    public Task SetMuteAsync(bool muted)
    {
        _logger.Information($"SetMute {muted}");
        var device = GetDefaultDevice();
        device.AudioEndpointVolume.Mute = muted;
        return Task.CompletedTask;
    }

    public Task LockAsync()
    {
        _logger.Information("LockWorkstation");
        AdminService.Lock();
        return Task.CompletedTask;
    }

    public Task SleepAsync()
    {
        _logger.Information("Sleep");
        AdminService.Sleep();
        return Task.CompletedTask;
    }

    public Task ShutdownAsync(int delaySeconds)
    {
        if (delaySeconds < 0 || delaySeconds > 3600)
            throw new ArgumentOutOfRangeException(nameof(delaySeconds));

        _logger.Warning($"Shutdown requested in {delaySeconds}s.");
        var psi = new ProcessStartInfo("shutdown",
            $"/s /t {delaySeconds}")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true
        };
        Process.Start(psi);
        return Task.CompletedTask;
    }

    public Task TypeTextAsync(string text)
    {
        if (string.IsNullOrEmpty(text)) return Task.CompletedTask;
        _logger.Information($"TypeText ({text.Length} chars).");
        RunOnSta(() =>
        {
            // Escape SendKeys metacharacters so arbitrary user text types literally.
            var keys = EscapeSendKeys(text);
            System.Windows.Forms.SendKeys.SendWait(keys);
        });
        return Task.CompletedTask;
    }

    public Task WriteClipboardAsync(string text)
    {
        _logger.Information($"WriteClipboard ({text.Length} chars).");
        RunOnSta(() => System.Windows.Clipboard.SetText(text));
        return Task.CompletedTask;
    }

    /// <summary>Clipboard/keyboard injection requires an STA thread; the WPF UI thread is the obvious one.</summary>
    private static void RunOnSta(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null) return;
        _ = dispatcher.InvokeAsync(action);
    }

    private static string EscapeSendKeys(string s)
    {
        var t = s.Replace("{", "{{}").Replace("}", "{}}");
        const string specials = "+^%~()[]";
        foreach (var ch in specials)
        {
            t = t.Replace(ch.ToString(), "{" + ch + "}");
        }
        return t;
    }

    private static MMDevice GetDefaultDevice()
    {
        var enumerator = new MMDeviceEnumerator();
        return enumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eMultimedia);
    }
}
