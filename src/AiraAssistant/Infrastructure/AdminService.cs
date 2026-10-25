using System;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace AiraAssistant.Infrastructure;

/// <summary>
/// Administrative-privilege helpers. Exposes whether the process is already
/// elevated and can attempt a self-restart under elevation when needed.
/// </summary>
public interface IAdminService
{
    bool IsRunAsAdministrator();
    void RestartElevated(string? arguments = null);
}

public sealed class AdminService : IAdminService
{
    public bool IsRunAsAdministrator()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    public void RestartElevated(string? arguments = null)
    {
        var exe = Process.GetCurrentProcess().MainModule?.FileName
                  ?? Assembly.GetEntryAssembly()?.Location
                  ?? throw new InvalidOperationException("Unable to resolve current executable path.");

        // The ShellExecute "runas" verb triggers the UAC prompt.
        var psi = new ProcessStartInfo
        {
            UseShellExecute = true,
            Verb = "runas",
            FileName = exe,
            Arguments = arguments ?? string.Empty
        };
        try
        {
            Process.Start(psi);
            Environment.Exit(0);
        }
        catch
        {
            // The user declined the UAC prompt; nothing to do.
        }
    }

    // P/Invoke so we can request shutdown / lock / sleep from a service without
    // referencing WPF or WinForms types directly.
    [DllImport("user32.dll")]
    private static extern void LockWorkStation();

    [DllImport("powrprof.dll", SetLastError = true)]
    private static extern bool SetSuspendState(bool hibernate, bool forceCritical, bool disableWakeEvent);

    /// <summary>Locks the workstation. Exposed via <see cref="Services.SystemControl.ISystemControlService"/>.</summary>
    public static void Lock() => LockWorkStation();

    /// <summary>Puts the system to sleep.</summary>
    public static void Sleep() => SetSuspendState(false, false, false);
}
