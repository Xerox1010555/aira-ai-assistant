using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace AiraAssistant.Infrastructure;

/// <summary>
/// Applies VIP "frosted glass" depth to the main window:
/// <list type="number">
///   <item>Windows 11: rounded window corners (DWMWA_WINDOW_CORNER_PREFERENCE)
///   + system acrylic/mica backdrop (DWMWA_SYSTEMBACKDROP_TYPE — the API
///   itself reports unsupported on Windows 10, so we simply try it first
///   instead of checking build numbers).</item>
///   <item>Universal fallback: SetWindowCompositionAttribute with
///   ACCENT_ENABLE_ACRYLICBLURBEHIND and a dark navy tint (works Win10+).</item>
///   <item>If both fail: nothing happens — the window keeps its translucent
///   dark background and still looks intentional.</item>
/// </list>
/// Every call is fail-safe: an API mismatch must never crash startup.
/// </summary>
public static class WindowGlassHost
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
    private const int DWMWCP_ROUND = 2;
    private const int DWMSBT_MICA = 2;
    private const int DWMSBT_ACRYLIC = 4;

    private const int ACCENT_ENABLE_ACRYLICBLURBEHIND = 4;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowCompositionAttribute(IntPtr hwnd, ref AccentPolicy data);

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public int AccentState;
        public int AccentFlags;
        public uint GradientColor;
        public int AnimationId;
    }

    public static void Apply(Window window)
    {
        if (window is null) return;
        try
        {
            var hwnd = new WindowInteropHelper(window).EnsureHandle();

            // Dark system tint for anything host-drawn — cosmetic.
            TrySet(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, 1);

            // Windows 11 path (silently reported as unsupported on Win10):
            var corner = DWMWCP_ROUND;
            DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));

            var acrylic = DWMSBT_ACRYLIC;
            if (DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref acrylic, sizeof(int)) == 0)
            {
                return; // full system acrylic — done
            }

            var mica = DWMSBT_MICA;
            if (DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref mica, sizeof(int)) == 0)
            {
                return; // mica — still premium depth
            }

            // 2) Universal fallback: composition-acrylic (Win10 and 11).
            var policy = new AccentPolicy
            {
                AccentState = ACCENT_ENABLE_ACRYLICBLURBEHIND,
                AccentFlags = 2,
                // ABGR of dark navy #20120D at ~55% alpha (0x8C).
                GradientColor = 0x8C20120D,
                AnimationId = 0
            };
            SetWindowCompositionAttribute(hwnd, ref policy);
        }
        catch
        {
            // Glass is cosmetic — never fail startup for it.
        }
    }

    private static void TrySet(IntPtr hwnd, int attribute, int value)
    {
        try
        {
            var v = value;
            DwmSetWindowAttribute(hwnd, attribute, ref v, sizeof(int));
        }
        catch
        {
            // ignore
        }
    }
}
