using System;
using System.Threading.Tasks;

namespace AiraAssistant.Services.SystemControl;

/// <summary>
/// All OS-level side effects that Aira is allowed to perform. The
/// implementation launches processes, opens URLs, controls volume, and
/// triggers power actions.
/// </summary>
public interface ISystemControlService
{
    Task OpenUrlAsync(Uri uri);
    Task LaunchAppAsync(string friendlyName);
    Task SetVolumeAsync(int level);
    Task SetMuteAsync(bool muted);
    Task LockAsync();
    Task SleepAsync();
    Task ShutdownAsync(int delaySeconds);

    /// <summary>Types text into the focused application via SendKeys (executed on the STA UI thread).</summary>
    Task TypeTextAsync(string text);

    /// <summary>Replaces the clipboard content with text (executed on the STA UI thread).</summary>
    Task WriteClipboardAsync(string text);
}
