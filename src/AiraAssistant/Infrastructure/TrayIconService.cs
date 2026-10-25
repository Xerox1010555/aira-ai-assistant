using System;
using System.Windows.Forms;
using System.Windows;
using AiraAssistant.Infrastructure.Logging;
using AiraAssistant.UI.ViewModels;

namespace AiraAssistant.Infrastructure;

/// <summary>
/// System-tray presence for Aira (v1 had none): quick access to show/hide the
/// window, toggle the voice session and exit, plus double-click toggle.
/// Desktop "always-on assistant" apps are expected to live in the tray.
/// Fail-safe: any tray problem is logged and never blocks startup.
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private readonly ILogger _logger;
    private NotifyIcon? _icon;

    public bool IsAvailable => _icon is not null;

    public TrayIconService(ILogger logger)
    {
        _logger = logger;
    }

    public void Initialize(MainViewModel viewModel)
    {
        try
        {
            _icon = new NotifyIcon
            {
                Text = "Aira — Windows AI Assistant",
                Visible = true
            };
            _icon.Icon = CreateIcon();

            var menu = new ContextMenuStrip();
            menu.Items.Add("Show / Hide window", null, (_, _) => ToggleWindow());
            menu.Items.Add("Start / Stop voice session", null, (_, _) => viewModel.ToggleListeningCommand.Execute(null));
            menu.Items.Add(new ToolStripSeparator());
        // v1.1: qualifier is explicit because this file mixes System.Windows
        // (WPF) and System.Windows.Forms (tray icon) namespaces.
        menu.Items.Add("Exit", null, (_, _) => System.Windows.Application.Current?.Shutdown());

            _icon.ContextMenuStrip = menu;
            _icon.DoubleClick += (_, _) => ToggleWindow();
        }
        catch (Exception ex)
        {
            _logger.Warning($"Tray icon unavailable: {ex.Message}");
            try { _icon?.Dispose(); } catch { /* ignore */ }
            _icon = null;
        }
    }

    private static void ToggleWindow()
    {
        Ui.Post(() =>
        {
            var w = System.Windows.Application.Current?.MainWindow;
            if (w is null) return;
            if (w.IsVisible)
            {
                w.Hide();
            }
            else
            {
                w.Show();
                w.Activate();
            }
        });
    }

    private static System.Drawing.Icon CreateIcon()
    {
        try
        {
            using var bmp = new System.Drawing.Bitmap(32, 32);
            using (var g = System.Drawing.Graphics.FromImage(bmp))
            {
                g.Clear(System.Drawing.Color.FromArgb(88, 101, 242));
                using var font = new System.Drawing.Font("Segoe UI", 17f, System.Drawing.FontStyle.Bold);
                var size = g.MeasureString("A", font);
                g.DrawString("A", font, System.Drawing.Brushes.White, 16 - (size.Width / 2f), 16 - (size.Height / 2f));
            }
            return (System.Drawing.Icon)System.Drawing.Icon.FromHandle(bmp.GetHicon()).Clone();
        }
        catch
        {
            return System.Drawing.SystemIcons.Application;
        }
    }

    public void Dispose()
    {
        try
        {
            if (_icon is not null)
            {
                _icon.Visible = false;
                _icon.Dispose();
                _icon = null;
            }
        }
        catch
        {
            // ignore
        }
    }
}
