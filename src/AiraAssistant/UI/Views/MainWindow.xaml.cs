using System;
using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using AiraAssistant.Infrastructure;
using AiraAssistant.Models;
using AiraAssistant.UI.ViewModels;
using System.Diagnostics;

namespace AiraAssistant.UI.Views;

/// <summary>
/// The single top-level window. Code-behind is intentionally minimal —
/// it forwards keyboard events to the view model, opens the Settings
/// dialog, and applies user-configurable window options.
/// </summary>
public partial class MainWindow : Window
{
    private readonly IServiceProvider _services;

    public MainWindow(MainViewModel viewModel, IAppConfig config, IServiceProvider services)
    {
        _services = services;
        InitializeComponent();

        // v1.1: real app icon on the title bar (fail-safe: cosmetic only,
        // an icon failure must never block startup).
        try
        {
            Icon = System.Windows.Media.Imaging.BitmapFrame.Create(
                new Uri("pack://application:,,,/Aira.ico"));
        }
        catch { /* icon is decorative */ }

        DataContext = viewModel;

        // v1.1: Ui.AlwaysOnTop is now actually honored (dead config in v1).
        Topmost = config.Ui.AlwaysOnTop;

        Loaded += (_, _) => InputBox.Focus();

        // v1.1: auto-scroll to the newest bubble as messages stream in.
        viewModel.AutoScrollRequested += (_, _) =>
        {
            if (ChatList.Items.Count > 0)
            {
                ChatList.ScrollIntoView(ChatList.Items[^1]);
            }
        };

        // VIP choreography: halo pulse + 360 mic spin while listening,
        // breathing status dot while busy. Driven from code because WPF
        // styles cannot target named visual-tree elements.
        viewModel.PropertyChanged += OnViewModelPropertyChanged;

        // Fit the window inside the actual work area — on high-DPI setups
        // (150%) the logical desktop can be smaller than the design size,
        // which would push the composer off-screen.
        var work = SystemParameters.WorkArea;
        if (Height > work.Height)
        {
            Height = work.Height * 0.96;
        }
        if (Width > work.Width)
        {
            Width = work.Width * 0.98;
        }
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
    }

    // ==== VIP voice choreography (cosmetic, fully fail-safe) ====

    private Storyboard? _micChoreo;
    private Storyboard? _breathChoreo;

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // The VM raises property changes from background threads (speech and
        // HTTP callbacks); the choreography must run on the UI thread.
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            var args = e;
            dispatcher.BeginInvoke(() => ApplyVoiceOrStateChange(args));
            return;
        }
        ApplyVoiceOrStateChange(e);
    }

    private void ApplyVoiceOrStateChange(PropertyChangedEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
        {
            return;
        }

        try
        {
            if (e.PropertyName == nameof(MainViewModel.IsListening))
            {
                RunMicChoreography(vm.IsListening);
            }
            else if (e.PropertyName == nameof(MainViewModel.SessionState))
            {
                RunStateBreathing(vm.SessionState);
            }
        }
        catch
        {
            // Animation is decorative — never disturb the session.
        }
    }
    private void RunMicChoreography(bool listening)
    {
        if (_micChoreo is not null)
        {
            _micChoreo.Remove(this);
            _micChoreo = null;
        }

        if (!listening)
        {
            MicRing.Opacity = 0;
            MicRingScale.ScaleX = 1;
            MicRingScale.ScaleY = 1;
            MicSpin.Angle = 0;
            return;
        }

        var story = new Storyboard();

        var ringOpacity = new DoubleAnimation(0, 0.85, TimeSpan.FromMilliseconds(200));
        Storyboard.SetTarget(ringOpacity, MicRing);
        Storyboard.SetTargetProperty(ringOpacity, new PropertyPath("Opacity"));
        story.Children.Add(ringOpacity);

        var ringScaleX = new DoubleAnimation(1, 1.9, TimeSpan.FromMilliseconds(1150))
        {
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(ringScaleX, MicRingScale);
        Storyboard.SetTargetProperty(ringScaleX, new PropertyPath("ScaleX"));
        story.Children.Add(ringScaleX);

        var ringScaleY = new DoubleAnimation(1, 1.9, TimeSpan.FromMilliseconds(1150))
        {
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(ringScaleY, MicRingScale);
        Storyboard.SetTargetProperty(ringScaleY, new PropertyPath("ScaleY"));
        story.Children.Add(ringScaleY);

        var spin = new DoubleAnimation(0, 360, TimeSpan.FromMilliseconds(700))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
        };
        Storyboard.SetTarget(spin, MicSpin);
        Storyboard.SetTargetProperty(spin, new PropertyPath("Angle"));
        story.Children.Add(spin);

        _micChoreo = story;
        story.Begin(this, true);
    }

    private void RunStateBreathing(VoiceSessionState sessionState)
    {
        if (_breathChoreo is not null)
        {
            _breathChoreo.Remove(this);
            _breathChoreo = null;
        }
        StateDot.Opacity = 1;

        switch (sessionState)
        {
            case VoiceSessionState.Listening:
            case VoiceSessionState.Thinking:
                break;
            default:
                return;
        }

        var pulse = new DoubleAnimation(
            1,
            sessionState == VoiceSessionState.Listening ? 0.3 : 0.35,
            TimeSpan.FromMilliseconds(sessionState == VoiceSessionState.Listening ? 800 : 500))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = sessionState == VoiceSessionState.Listening
                ? new SineEase { EasingMode = EasingMode.EaseInOut }
                : null
        };
        Storyboard.SetTarget(pulse, StateDot);
        Storyboard.SetTargetProperty(pulse, new PropertyPath("Opacity"));

        var story = new Storyboard();
        story.Children.Add(pulse);
        _breathChoreo = story;
        story.Begin(this, true);
    }

    /// <summary>Allow the user to send a message with the Enter key.</summary>
    private void OnInputKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is MainViewModel vm)
        {
            if (vm.SendCommand.CanExecute(null))
            {
                vm.SendCommand.Execute(null);
                e.Handled = true;
            }
        }
    }

    // ==== Custom chrome (WindowChrome borderless window) ====

    private void OnMinimizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximizeToggleClick(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    // ==== System glass + borderless maximize fix ====
    //
    // WM_GETMINMAXINFO: a borderless (WindowChrome) window maximizes over the
    // taskbar by default; this clamps the maximized size to the monitor's
    // work area like a native app window.

    private const int WM_GETMINMAXINFO = 0x0024;

    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        public NativePoint ptReserved;
        public NativePoint ptMaxSize;
        public NativePoint ptMaxPosition;
        public NativePoint ptMinTrackSize;
        public NativePoint ptMaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMonitorInfo
    {
        public int cbSize;
        public NativeRect rcMonitor;
        public NativeRect rcWork;
        public uint dwFlags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref NativeMonitorInfo lpmi);

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // VIP system glass: rounded corners + acrylic/mica backdrop.
        WindowGlassHost.Apply(this);

        if (new WindowInteropHelper(this).Handle is IntPtr hwnd)
        {
            HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_GETMINMAXINFO)
        {
            var mmi = (MINMAXINFO)Marshal.PtrToStructure(lParam, typeof(MINMAXINFO))!;
            var monitor = MonitorFromWindow(hwnd, 2 /* MONITOR_DEFAULTTONEAREST */);
            if (monitor != IntPtr.Zero)
            {
                var info = new NativeMonitorInfo { cbSize = Marshal.SizeOf(typeof(NativeMonitorInfo)) };
                if (GetMonitorInfo(monitor, ref info))
                {
                    mmi.ptMaxSize.X = info.rcWork.Right - info.rcWork.Left;
                    mmi.ptMaxSize.Y = info.rcWork.Bottom - info.rcWork.Top;
                    mmi.ptMaxPosition.X = Math.Abs(info.rcWork.Left - info.rcMonitor.Left);
                    mmi.ptMaxPosition.Y = Math.Abs(info.rcWork.Top - info.rcMonitor.Top);
                    Marshal.StructureToPtr(mmi, lParam, true);
                    handled = true;
                }
            }
        }
        return IntPtr.Zero;
    }

    private void OnSessionsClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = _services.GetRequiredService<SessionsDialog>();
            dialog.Owner = this;
            dialog.ShowDialog();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not open sessions:\n{ex.Message}", "Aira",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = _services.GetRequiredService<SettingsDialog>();
            dialog.Owner = this;
            dialog.ShowDialog();

            if (dialog.DialogResult == true)
            {
                // Always-on-top applies immediately; provider/model/voice
                // changes need a restart because the AI/TTS engines were
                // built at startup.
                if (dialog.AppliedAlwaysOnTop != Topmost)
                {
                    Topmost = dialog.AppliedAlwaysOnTop;
                }
                if (dialog.NeedsRestart)
                {
                    var answer = MessageBox.Show(
                        "Some changes (provider, model, system prompt, TTS voice) only apply after a restart. Restart Aira now?",
                        "Aira",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question,
                        MessageBoxResult.No);
                    if (answer == MessageBoxResult.Yes)
                    {
                        var exe = Process.GetCurrentProcess().MainModule?.FileName;
                        if (!string.IsNullOrEmpty(exe))
                        {
                            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
                        }
                        Application.Current.Shutdown();
                    }
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not open settings:\n{ex.Message}", "Aira",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
