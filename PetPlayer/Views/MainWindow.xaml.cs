using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Win32;
using PetPlayer.Helpers;
using PetPlayer.Models;
using PetPlayer.Services;
using PetPlayer.ViewModels;

namespace PetPlayer.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly DispatcherTimer _cursorHideTimer;
    private readonly DispatcherTimer _letterboxMaskTimer;
    private WindowState _preFullscreenState = WindowState.Normal;
    private Rect _preFullscreenBounds;
    private bool _cursorHidden;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel;
        DataContext = _viewModel;

        VideoViewControl.MediaPlayer = _viewModel.Player;

        Width = _viewModel.Settings.WindowWidth;
        Height = _viewModel.Settings.WindowHeight;
        if (_viewModel.Settings.WindowLeft >= 0 && _viewModel.Settings.WindowTop >= 0)
        {
            Left = _viewModel.Settings.WindowLeft;
            Top = _viewModel.Settings.WindowTop;
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        if (_viewModel.Settings.WindowMaximized)
        {
            WindowState = WindowState.Maximized;
        }

        Topmost = _viewModel.Settings.AlwaysOnTop;

        _cursorHideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(PlaybackConstants.CursorHideDelayMs) };
        _cursorHideTimer.Tick += (_, _) =>
        {
            _cursorHideTimer.Stop();
            if (_viewModel.IsFullscreen)
            {
                Cursor = Cursors.None;
                _cursorHidden = true;
            }
        };

        _viewModel.PropertyChanged += ViewModel_PropertyChanged;

        Closing += MainWindow_Closing;
        Loaded += (_, _) => FixVideoBackgroundColorWithRetries();

        // Keeps LetterboxMask*/*'s sizes in sync with the video's actual content
        // rect at all times - including live during an interactive resize or a
        // fullscreen toggle - rather than only recalculating on specific
        // triggers (which would lag behind a continuous drag). SizeChanged
        // catches most of it immediately (it fires synchronously as part of
        // WPF's own layout pass, on every native resize step), but the video's
        // own native rect can still briefly lag behind that - especially while
        // shrinking, where the padding is growing and a stale (smaller)
        // measurement would under-cover it, flashing the color underneath -
        // so a fast fallback timer plus a small safety buffer (see
        // UpdateLetterboxMask) covers that gap too.
        SizeChanged += (_, _) => UpdateLetterboxMask();
        _letterboxMaskTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
        _letterboxMaskTimer.Tick += (_, _) => UpdateLetterboxMask();
        _letterboxMaskTimer.Start();
    }

    /// <summary>
    /// LibVLCSharp.WPF's VideoHwndHost creates a raw Win32 "static" child window
    /// as a placeholder, but once a video actually starts playing, LibVLC layers
    /// its OWN child windows on top of that (confirmed by enumerating the real
    /// window tree while a video played): "VLC video main &lt;id&gt;" spans the
    /// whole video area and paints the letterbox/pillarbox bars around the
    /// video, and "VLC video output &lt;id&gt;" (a child of that) is sized to the
    /// actual video content rect. All three are raw HWNDs whose default class
    /// background brush shows through as flat light gray (SystemColors.Control)
    /// wherever no frame/letterbox fill has been drawn - most visible in the
    /// letterbox bars during normal playback, and briefly everywhere while
    /// interactively resizing. Retargeting each one's class background brush to
    /// black fixes the color at the source - these sit above ordinary WPF
    /// siblings (see the "airspace" comment on VideoViewControl's content in
    /// the XAML), so nothing paintable from WPF could cover it instead.
    /// The "VLC video *" windows only exist once LibVLC actually starts
    /// rendering (not merely when VideoView is constructed), so this retries on
    /// load AND is re-run every time playback starts (see ViewModel_PropertyChanged).
    /// Retries run on a real wall-clock timer (not just "next idle slot") -
    /// DispatcherPriority.ApplicationIdle can fire dozens of times within a
    /// handful of milliseconds when the app has nothing else queued, which
    /// isn't nearly enough real time for LibVLC's Direct3D device/swapchain to
    /// actually finish initializing on a slower first play. That race is why
    /// the background only sometimes came out black - a timer guarantees each
    /// attempt is genuinely spaced out, giving LibVLC real time to create
    /// those windows before retries give up.
    /// </summary>
    private DispatcherTimer? _videoBackgroundFixTimer;
    private int _videoBackgroundFixAttempts;

    private void FixVideoBackgroundColorWithRetries()
    {
        _videoBackgroundFixTimer?.Stop();

        if (TryFixVideoWindowBackground())
        {
            return;
        }

        _videoBackgroundFixAttempts = 0;
        _videoBackgroundFixTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _videoBackgroundFixTimer.Tick += (_, _) =>
        {
            if (TryFixVideoWindowBackground() || ++_videoBackgroundFixAttempts >= 40)
            {
                _videoBackgroundFixTimer!.Stop();
            }
        };
        _videoBackgroundFixTimer.Start();
    }

    private bool TryFixVideoWindowBackground()
    {
        var mainHwnd = new WindowInteropHelper(this).Handle;
        if (mainHwnd == IntPtr.Zero)
        {
            return false;
        }

        var blackBrush = GetStockObject(BLACK_BRUSH);
        var foundVlcRenderer = false;

        EnumChildWindows(mainHwnd, (hWnd, _) =>
        {
            var className = new StringBuilder(128);
            GetClassName(hWnd, className, className.Capacity);
            var name = className.ToString();

            if (string.Equals(name, "static", StringComparison.OrdinalIgnoreCase))
            {
                // This placeholder is the ONLY thing visible while idle (no media ever
                // loaded/played) - LibVLC's own "VLC video main/output" windows only get
                // created once playback actually starts and layer on top of it from then
                // on, so its color never matters again after that. Painting it the chrome
                // color (not black) is what makes the idle player area read as #2B2B2B
                // instead of white/gray while nothing is playing.
                SetClassLongPtr(hWnd, GCLP_HBRBACKGROUND, IdleBackgroundBrush);
                InvalidateRect(hWnd, IntPtr.Zero, true);
            }
            else if (name.StartsWith("VLC video main", StringComparison.Ordinal)
                     || name.StartsWith("VLC video output", StringComparison.Ordinal))
            {
                SetClassLongPtr(hWnd, GCLP_HBRBACKGROUND, blackBrush);
                InvalidateRect(hWnd, IntPtr.Zero, true);
                foundVlcRenderer = true;
            }

            return true;
        }, IntPtr.Zero);

        // Only report success once the actual VLC renderer windows are found -
        // the "static" placeholder alone isn't enough, since it gets fully
        // covered by them the moment a video starts playing.
        return foundVlcRenderer;
    }

    /// <summary>
    /// LibVLC's Direct3D11 output only ever paints the video's own content rect
    /// - not the full "VLC video main" area - so whenever the video's aspect
    /// ratio doesn't exactly match the window's, the padding around it keeps
    /// whatever color that render target happened to start with (a flat light
    /// gray in this build), regardless of the class-background fix above,
    /// which only affects GDI's erase-background and never gets a chance to
    /// paint once Direct3D takes over presenting. Since nothing reachable from
    /// LibVLCSharp's public API controls that color, this instead measures the
    /// real video content rect (the "VLC video output" child window) against
    /// VideoViewControl's own bounds and sizes four opaque black bars - living
    /// in this same overlay, so they render above the video - to cover exactly
    /// whatever padding exists, keeping the area outside the video solid black
    /// regardless of what LibVLC itself renders underneath.
    /// </summary>
    private void UpdateLetterboxMask()
    {
        if (VideoViewControl.ActualWidth <= 0 || VideoViewControl.ActualHeight <= 0)
        {
            return;
        }

        var mainHwnd = new WindowInteropHelper(this).Handle;
        if (mainHwnd == IntPtr.Zero)
        {
            return;
        }

        var videoOutputHwnd = IntPtr.Zero;
        EnumChildWindows(mainHwnd, (hWnd, _) =>
        {
            var className = new StringBuilder(128);
            GetClassName(hWnd, className, className.Capacity);
            if (className.ToString().StartsWith("VLC video output", StringComparison.Ordinal))
            {
                videoOutputHwnd = hWnd;
                return false;
            }

            return true;
        }, IntPtr.Zero);

        if (videoOutputHwnd == IntPtr.Zero || !GetWindowRect(videoOutputHwnd, out var videoRect))
        {
            LetterboxMaskTop.Height = 0;
            LetterboxMaskBottom.Height = 0;
            LetterboxMaskLeft.Width = 0;
            LetterboxMaskRight.Width = 0;
            return;
        }

        var topLeft = VideoViewControl.PointFromScreen(new Point(videoRect.Left, videoRect.Top));
        var bottomRight = VideoViewControl.PointFromScreen(new Point(videoRect.Right, videoRect.Bottom));

        var topPad = topLeft.Y;
        var bottomPad = VideoViewControl.ActualHeight - bottomRight.Y;
        var leftPad = topLeft.X;
        var rightPad = VideoViewControl.ActualWidth - bottomRight.X;

        // While shrinking the window, the padding around the video is growing,
        // but the "VLC video output" rect measured above can briefly lag one
        // tick behind the window's own new size - a mask sized to that stale
        // (too-small) padding would under-cover the real gap for a moment,
        // flashing the color underneath. Padding this measurement out a bit
        // whenever real padding already exists absorbs that lag; it's only
        // ever applied on top of a genuine gap, so a video that exactly fills
        // its area (no padding at all) never grows a false border from this.
        const double lagBuffer = 16;
        LetterboxMaskTop.Height = topPad > 0.5 ? topPad + lagBuffer : 0;
        LetterboxMaskBottom.Height = bottomPad > 0.5 ? bottomPad + lagBuffer : 0;
        LetterboxMaskLeft.Width = leftPad > 0.5 ? leftPad + lagBuffer : 0;
        LetterboxMaskRight.Width = rightPad > 0.5 ? rightPad + lagBuffer : 0;
    }

    private delegate bool EnumChildProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(IntPtr hWndParent, EnumChildProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern IntPtr SetClassLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll")]
    private static extern bool InvalidateRect(IntPtr hWnd, IntPtr lpRect, bool bErase);

    [DllImport("gdi32.dll")]
    private static extern IntPtr GetStockObject(int fnObject);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateSolidBrush(int crColor);

    private const int GCLP_HBRBACKGROUND = -10;
    private const int BLACK_BRUSH = 4;

    // #2B2B2B - R=G=B so byte order within the COLORREF doesn't matter. Created once
    // and left alive for the process's lifetime (used as a permanent window class
    // attribute, same as the stock black brush above).
    private static readonly IntPtr IdleBackgroundBrush = CreateSolidBrush(0x2B2B2B);

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainViewModel.IsFullscreen):
                ApplyFullscreenState();
                break;
            case nameof(MainViewModel.IsAlwaysOnTop):
                Topmost = _viewModel.IsAlwaysOnTop;
                break;
            case nameof(MainViewModel.IsTitleBarVisible):
            case nameof(MainViewModel.IsBottomBarVisible):
                if (_viewModel.IsTitleBarVisible || _viewModel.IsBottomBarVisible)
                {
                    RestoreCursorIfHidden();
                }
                break;
            case nameof(MainViewModel.IsPlaying):
                if (_viewModel.IsPlaying)
                {
                    // LibVLC's own "VLC video main/output" child windows (see
                    // FixVideoBackgroundColorWithRetries) are created fresh once
                    // playback actually starts, so this needs to run again for
                    // every video opened, not just once at startup.
                    FixVideoBackgroundColorWithRetries();
                }
                break;
        }
    }

    /// <summary>
    /// Fullscreen sizes the window to the monitor's full bounds - taskbar
    /// included - and pins it on top of it. (WindowState.Maximized is kept
    /// separately constrained to the work area instead - see WmGetMinMaxInfo -
    /// so the two states stay visually distinct: Maximized never covers the
    /// taskbar, Fullscreen always does.)
    /// </summary>
    private void ApplyFullscreenState()
    {
        if (_viewModel.IsFullscreen)
        {
            _preFullscreenState = WindowState;
            _preFullscreenBounds = new Rect(Left, Top, Width, Height);

            var monitorBounds = GetCurrentMonitorBoundsInDips();
            if (monitorBounds is { } bounds)
            {
                WindowState = WindowState.Normal;
                Left = bounds.Left;
                Top = bounds.Top;
                Width = bounds.Width;
                Height = bounds.Height;
            }
            else
            {
                WindowState = WindowState.Maximized;
            }

            Topmost = true;

            // Hide both bars immediately on entry, no delay - HandleFullscreenBarHover
            // takes over from here and shows either one instantly as soon as the
            // pointer is next reported within its hover zone.
            _viewModel.IsTitleBarVisible = false;
            _viewModel.IsBottomBarVisible = false;
        }
        else
        {
            Topmost = _viewModel.IsAlwaysOnTop;

            if (_preFullscreenState == WindowState.Maximized)
            {
                WindowState = WindowState.Maximized;
            }
            else
            {
                WindowState = WindowState.Normal;
                Left = _preFullscreenBounds.Left;
                Top = _preFullscreenBounds.Top;
                Width = _preFullscreenBounds.Width;
                Height = _preFullscreenBounds.Height;
            }

            // Normal window mode always shows both bars.
            _viewModel.IsTitleBarVisible = true;
            _viewModel.IsBottomBarVisible = true;

            RestoreCursorIfHidden();
        }

        // The fullscreen button lives in VideoView's separate overlay HWND (see
        // the comment on VideoArea_MouseUp below). Resizing/repositioning
        // MainWindow here happens synchronously, but the overlay window only
        // re-syncs its own bounds to match on a later layout pass - when it
        // does, it can re-steal foreground activation from MainWindow, undoing
        // the immediate Activate() on mouse-up. Re-activating once more after
        // that pass has had a chance to run keeps arrow-key seeking etc. working
        // right after toggling fullscreen, without needing an extra click first.
        Dispatcher.BeginInvoke(new Action(() => Activate()), DispatcherPriority.ApplicationIdle);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    private const uint MonitorDefaultToNearest = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X, Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }

    private const int WM_GETMINMAXINFO = 0x0024;

    /// <summary>
    /// A window with WindowStyle="None" (no standard non-client frame) doesn't
    /// get Windows' usual "maximize avoids the taskbar" behavior for free -
    /// WindowState.Maximized instead snaps to the monitor's FULL bounds,
    /// covering the taskbar, exactly like Fullscreen. Handling
    /// WM_GETMINMAXINFO here (the standard fix for this well-known
    /// WindowStyle=None quirk) constrains Maximized to the work area instead,
    /// so it behaves like a normal maximize and Fullscreen remains the only
    /// state that deliberately covers the taskbar.
    /// </summary>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        if (PresentationSource.FromVisual(this) is HwndSource hwndSource)
        {
            hwndSource.AddHook(WndProc);

            // Eliminates the white flash otherwise visible before WPF's first composed
            // (dark) frame paints, and again on every minimize->restore repaint: Windows
            // erases a plain HWND with its window CLASS's background brush before
            // anything else paints over it, and that brush defaults to white unless
            // changed. Runs before Show() so the very first paint is already black -
            // same technique (and same P/Invoke declarations) already used below in
            // TryFixVideoWindowBackground for LibVLC's own child windows.
            SetClassLongPtr(hwndSource.Handle, GCLP_HBRBACKGROUND, GetStockObject(BLACK_BRUSH));

            // VideoHwndHost's own native "static" placeholder child window (see
            // TryFixVideoWindowBackground) can only be created once this window's own
            // HWND exists (it needs a parent to attach to), which normally only
            // happens as part of Show()'s own internal layout pass - by which point
            // Windows has ALREADY painted the very first frame using that child's
            // default (white) class background, since Loaded (where the fix used to
            // run) fires only after that pass, i.e. after the window is already
            // visible. That's exactly why a later minimize/restore repaint looked
            // correct while the very first launch didn't - the fix was real, just
            // applied one frame too late. Forcing layout here - while the window is
            // still fully invisible (ShowWindow hasn't run yet) - makes VideoHwndHost
            // create that child window right now, so its class brush can be
            // retargeted to the idle chrome color before a single pixel of it is
            // ever painted, with no repaint/minimize-restore trick needed.
            UpdateLayout();
            TryFixVideoWindowBackground();
        }
    }

    private const int WM_SIZING = 0x0214;

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_GETMINMAXINFO)
        {
            ConstrainMaximizedSizeToWorkArea(hwnd, lParam);
        }
        else if (msg == WM_SIZING)
        {
            ShowLiveResizeOverlay(lParam);
        }

        return IntPtr.Zero;
    }

    /// <summary>
    /// WM_SIZING is sent by Windows specifically while the user is interactively
    /// resizing (not moving) the window, repeatedly, for every edge/corner - whether
    /// the drag started from the native WindowChrome resize border or from
    /// TryBeginEdgeResize's own WM_NCLBUTTONDOWN handoff, both funnel into the same
    /// OS resize loop. Reusing that as the trigger avoids having to separately guess
    /// "is this SizeChanged from a user drag or something else" (maximize, fullscreen,
    /// settings restore, etc. never send WM_SIZING). Shows the overlay through the
    /// same infrastructure as every other status message, so it disappears on its own
    /// once WM_SIZING stops arriving (resize ends) via the existing fade timeout.
    /// </summary>
    private void ShowLiveResizeOverlay(IntPtr lParam)
    {
        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget is null)
        {
            return;
        }

        var rect = Marshal.PtrToStructure<RECT>(lParam);
        var transform = source.CompositionTarget.TransformFromDevice;
        var topLeft = transform.Transform(new Point(rect.Left, rect.Top));
        var bottomRight = transform.Transform(new Point(rect.Right, rect.Bottom));

        var width = (int)Math.Round(bottomRight.X - topLeft.X);
        var height = (int)Math.Round(bottomRight.Y - topLeft.Y);
        _viewModel.ShowSizeOverlay(width, height);
    }

    private static void ConstrainMaximizedSizeToWorkArea(IntPtr hwnd, IntPtr lParam)
    {
        var monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero)
        {
            return;
        }

        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(monitor, ref info))
        {
            return;
        }

        var minMaxInfo = Marshal.PtrToStructure<MINMAXINFO>(lParam);

        // Position/size are relative to the monitor's own top-left, not the
        // desktop origin - required for this to work correctly on a secondary
        // monitor, not just the primary one.
        minMaxInfo.ptMaxPosition.X = info.rcWork.Left - info.rcMonitor.Left;
        minMaxInfo.ptMaxPosition.Y = info.rcWork.Top - info.rcMonitor.Top;
        minMaxInfo.ptMaxSize.X = info.rcWork.Right - info.rcWork.Left;
        minMaxInfo.ptMaxSize.Y = info.rcWork.Bottom - info.rcWork.Top;

        Marshal.StructureToPtr(minMaxInfo, lParam, true);
    }

    /// <summary>
    /// The monitor's full pixel bounds (rcMonitor, not rcWork - i.e. including
    /// the taskbar), converted to the device-independent units WPF's own
    /// Left/Top/Width/Height expect, using the same device-to-DIP transform
    /// LibVLCSharp.WPF's own overlay window uses to stay aligned.
    /// </summary>
    private Rect? GetCurrentMonitorBoundsInDips()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return null;
        }

        var monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero)
        {
            return null;
        }

        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(monitor, ref info))
        {
            return null;
        }

        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget is null)
        {
            return null;
        }

        var transform = source.CompositionTarget.TransformFromDevice;
        var topLeft = transform.Transform(new Point(info.rcMonitor.Left, info.rcMonitor.Top));
        var bottomRight = transform.Transform(new Point(info.rcMonitor.Right, info.rcMonitor.Bottom));

        return new Rect(topLeft, bottomRight);
    }

    private void RestoreCursorIfHidden()
    {
        if (_cursorHidden)
        {
            Cursor = Cursors.Arrow;
            _cursorHidden = false;
        }
    }

    // ----- Keyboard shortcuts (spec section 11-15, 19, 26, 31-32) -----

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is TextBox)
        {
            return;
        }

        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        var handled = true;

        switch (e.Key)
        {
            case Key.Space:
                _viewModel.PlayPauseCommand.Execute(null);
                break;
            case Key.Right:
                if (ctrl) _viewModel.SeekForwardPreciseCommand.Execute(null);
                else if (shift) _viewModel.SeekForwardMediumCommand.Execute(null);
                else _viewModel.SeekForwardCommand.Execute(null);
                break;
            case Key.Left:
                if (ctrl) _viewModel.SeekBackwardPreciseCommand.Execute(null);
                else if (shift) _viewModel.SeekBackwardMediumCommand.Execute(null);
                else _viewModel.SeekBackwardCommand.Execute(null);
                break;
            case Key.Up:
                _viewModel.VolumeUpCommand.Execute(null);
                break;
            case Key.Down:
                _viewModel.VolumeDownCommand.Execute(null);
                break;
            case Key.M:
                _viewModel.ToggleMuteCommand.Execute(null);
                break;
            case Key.F:
            case Key.Enter:
                _viewModel.ToggleFullscreenCommand.Execute(null);
                break;
            case Key.Escape:
                if (_viewModel.IsFullscreen) _viewModel.ExitFullscreen();
                else handled = false;
                break;
            case Key.O when ctrl:
                OpenMediaDialog();
                break;
            case Key.T:
                _viewModel.ToggleAlwaysOnTopCommand.Execute(null);
                break;
            case Key.J:
                _viewModel.JumpBackwardCommand.Execute(null);
                break;
            case Key.L:
                _viewModel.JumpForwardCommand.Execute(null);
                break;
            case Key.OemOpenBrackets:
                _viewModel.DecreaseSpeedCommand.Execute(null);
                break;
            case Key.OemCloseBrackets:
                _viewModel.IncreaseSpeedCommand.Execute(null);
                break;
            case Key.OemBackslash:
                _viewModel.ResetSpeedCommand.Execute(null);
                break;
            case Key.G:
                if (shift) _viewModel.ShiftSubtitleEarlierLargeCommand.Execute(null);
                else _viewModel.ShiftSubtitleEarlierSmallCommand.Execute(null);
                break;
            case Key.H:
                if (shift) _viewModel.ShiftSubtitleLaterLargeCommand.Execute(null);
                else _viewModel.ShiftSubtitleLaterSmallCommand.Execute(null);
                break;
            default:
                handled = false;
                break;
        }

        if (handled)
        {
            e.Handled = true;
        }

        // Deliberately does NOT touch the fullscreen bars here - per spec, fullscreen
        // show/hide is driven exclusively by pointer proximity to the top/bottom edge
        // (see HandleFullscreenBarHover), never by keyboard activity. NotifyUserActivity
        // itself no-ops while fullscreen; in normal window mode the bars are always
        // visible already, so this is otherwise harmless.
        _viewModel.NotifyUserActivity();
    }

    // ----- Mouse / auto-hide / cursor -----

    private void Window_MouseMove(object sender, MouseEventArgs e)
    {
        HandleUserMouseActivity();
        HandleFullscreenBarHover(GetWindowRelativePoint(this, e));
    }

    // VideoView hosts its content in a separate floating overlay window (see
    // MainWindow.xaml), so mouse movement over the video/controls never
    // bubbles up to the root Window's MouseMove - it needs its own handler
    // wired to the content placed inside VideoView.
    private void VideoArea_MouseMove(object sender, MouseEventArgs e)
    {
        HandleUserMouseActivity();
        UpdateResizeCursor(sender, e);
        HandleFullscreenBarHover(GetWindowRelativePoint((FrameworkElement)sender, e));
    }

    private Point GetWindowRelativePoint(IInputElement relativeTo, MouseEventArgs e) =>
        PointFromScreen(((FrameworkElement)relativeTo).PointToScreen(e.GetPosition(relativeTo)));

    /// <summary>
    /// Fullscreen-only: reveals the title bar / bottom control bar independently and
    /// instantly based on how close the pointer currently is to the top/bottom edge,
    /// using generous "reasonable" zones (not an exact 1px edge) rather than the exact
    /// rendered bar height. No delay either way - each bar's visibility is just a
    /// direct reflection of whether the pointer is currently inside its zone on this
    /// move event, so it appears the instant the pointer enters the zone and
    /// disappears the instant it leaves (hovering the bar itself keeps the pointer
    /// inside its own zone the whole time it's being used). The middle of the video
    /// deliberately does nothing here - see HandleUserMouseActivity, which no longer
    /// force-shows the bars on generic mouse movement.
    /// </summary>
    private void HandleFullscreenBarHover(Point pointInWindow)
    {
        if (!_viewModel.IsFullscreen)
        {
            return;
        }

        _viewModel.IsTitleBarVisible = pointInWindow.Y <= PlaybackConstants.FullscreenTopHoverZoneDips;
        _viewModel.IsBottomBarVisible = pointInWindow.Y >= ActualHeight - PlaybackConstants.FullscreenBottomHoverZoneDips;
    }

    /// <summary>
    /// Shows the OS's normal resize cursor whenever the pointer is over an
    /// edge/corner of the window, so the edge-resize hookup in
    /// TryBeginEdgeResize is actually discoverable - without this the only
    /// affordance was a few invisible pixels, which read as "resizing
    /// doesn't work" even once the underlying hit-testing was fixed.
    ///
    /// Sets the Cursor on the Grid itself (the sender), not on MainWindow:
    /// this Grid lives inside VideoView's separate overlay window (see the
    /// comment on VideoViewControl's content in the XAML), which has its own
    /// OS-level cursor independent of MainWindow.Cursor - setting the latter
    /// had no visible effect here at all.
    /// </summary>
    private void UpdateResizeCursor(object sender, MouseEventArgs e)
    {
        var element = (FrameworkElement)sender;

        if (WindowState != WindowState.Normal || _viewModel.IsFullscreen || ResizeMode == ResizeMode.NoResize
            || Mouse.LeftButton == MouseButtonState.Pressed)
        {
            element.Cursor = null;
            return;
        }

        var pointInWindow = PointFromScreen(element.PointToScreen(e.GetPosition(element)));
        element.Cursor = HitTestResizeEdge(pointInWindow) switch
        {
            ResizeEdge.Left or ResizeEdge.Right => Cursors.SizeWE,
            ResizeEdge.Top or ResizeEdge.Bottom => Cursors.SizeNS,
            ResizeEdge.TopLeft or ResizeEdge.BottomRight => Cursors.SizeNWSE,
            ResizeEdge.TopRight or ResizeEdge.BottomLeft => Cursors.SizeNESW,
            _ => null,
        };
    }

    /// <summary>
    /// The player controls (buttons, sliders) live in VideoView's own floating
    /// overlay window, a separate top-level HWND from MainWindow (see the
    /// comment on VideoViewControl's content in the XAML). Clicking one of
    /// them can activate that overlay window at the OS level instead of
    /// MainWindow, which silently steals keyboard focus - MainWindow's
    /// PreviewKeyDown then stops firing for arrow keys etc. until something
    /// reactivates it.
    ///
    /// This is wired to the TUNNELING PreviewMouseUp, not the bubbling MouseUp -
    /// controls like the seek bar (Slider/Thumb/RepeatButton) mark the bubbling
    /// MouseUp as handled internally once they've finished their own click/drag
    /// handling, which stopped it from ever reaching a bubble handler here (this
    /// is exactly why keyboard shortcuts kept breaking specifically after using
    /// the seek bar, not just buttons). Preview fires on the way DOWN to
    /// whichever control was actually clicked, before that control gets a
    /// chance to mark anything handled, so it reliably fires for every control
    /// in this Grid - buttons, sliders, everything - not just plain video clicks.
    /// </summary>
    private void VideoArea_MouseUp(object sender, MouseButtonEventArgs e) => Activate();

    private void VideoArea_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Delta > 0)
        {
            _viewModel.VolumeUpCommand.Execute(null);
        }
        else if (e.Delta < 0)
        {
            _viewModel.VolumeDownCommand.Execute(null);
        }

        e.Handled = true;
        HandleUserMouseActivity();
    }

    /// <summary>
    /// Cursor un-hide/reset always happens on any movement (unchanged fullscreen
    /// cursor auto-hide behavior). NotifyUserActivity is a cheap no-op in normal
    /// window mode (bars are always visible there anyway) and, deliberately, ALSO
    /// a no-op in fullscreen (generic movement must not reveal the bars on its own -
    /// only proximity to an edge should, see HandleFullscreenBarHover).
    /// </summary>
    private void HandleUserMouseActivity()
    {
        RestoreCursorIfHidden();
        ResetCursorHideTimer();
        _viewModel.NotifyUserActivity();
    }

    private void ResetCursorHideTimer()
    {
        _cursorHideTimer.Stop();
        _cursorHideTimer.Start();
    }

    private void SeekSlider_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _viewModel.IsSeekBarDragging = true;
        UpdateSeekSliderFromPointer(e);
    }

    private void SeekSlider_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_viewModel.IsSeekBarDragging)
        {
            UpdateSeekSliderFromPointer(e);
        }
    }

    private void SeekSlider_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        UpdateSeekSliderFromPointer(e);
        _viewModel.IsSeekBarDragging = false;
        _viewModel.SeekToMilliseconds(SeekSlider.Value);
    }

    /// <summary>
    /// Computes the seek position directly from the pointer's X coordinate
    /// rather than trusting Slider/Track's own click-to-jump, which was
    /// unreliable in both directions (see the comment on SeekSlider in the XAML).
    /// </summary>
    private void UpdateSeekSliderFromPointer(MouseEventArgs e)
    {
        if (SeekSlider.ActualWidth <= 0)
        {
            return;
        }

        var x = e.GetPosition(SeekSlider).X;
        var fraction = Math.Clamp(x / SeekSlider.ActualWidth, 0.0, 1.0);
        SeekSlider.Value = fraction * SeekSlider.Maximum;
    }

    private void VideoArea_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            _viewModel.PlayPauseCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (TryBeginEdgeResize(sender, e))
        {
            return;
        }

        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // Mouse was released before the move could start - safe to ignore.
        }
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            MaximizeRestoreButton_Click(sender, e);
            return;
        }

        if (TryBeginEdgeResize(sender, e))
        {
            return;
        }

        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // Mouse was released before the move could start - safe to ignore.
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    private const int WM_NCLBUTTONDOWN = 0xA1;
    private const double ResizeBorderThicknessDips = 6;

    private enum ResizeEdge
    {
        None = 0,
        Left = 10,
        Right = 11,
        Top = 12,
        TopLeft = 13,
        TopRight = 14,
        Bottom = 15,
        BottomLeft = 16,
        BottomRight = 17,
    }

    /// <summary>
    /// The video/controls overlay lives in a separate top-level HWND that sits
    /// exactly on top of (most of) MainWindow's client area (see the comment on
    /// VideoViewControl's content in the XAML). That overlay swallows every
    /// mouse message near the window's edges before WindowChrome's own
    /// resize-border hit-testing ever sees them, so dragging from an edge that's
    /// covered by video/controls silently does nothing. Detecting the edge
    /// ourselves here and handing off to Windows' native resize loop (the same
    /// WM_NCLBUTTONDOWN trick DragMove uses for HTCAPTION, just with an HT*
    /// edge code instead) restores it.
    /// </summary>
    private bool TryBeginEdgeResize(object sender, MouseButtonEventArgs e)
    {
        if (WindowState != WindowState.Normal || _viewModel.IsFullscreen || ResizeMode == ResizeMode.NoResize)
        {
            return false;
        }

        var screenPoint = ((FrameworkElement)sender).PointToScreen(e.GetPosition((IInputElement)sender));
        var pointInWindow = PointFromScreen(screenPoint);

        var edge = HitTestResizeEdge(pointInWindow);
        if (edge == ResizeEdge.None)
        {
            return false;
        }

        var hwnd = new WindowInteropHelper(this).Handle;
        ReleaseCapture();
        // WM_NCLBUTTONDOWN's lParam must carry the click's SCREEN coordinates
        // (packed x/y, both in physical pixels - PointToScreen already returns
        // device pixels). Passing 0 here made the native sizing loop anchor
        // its very first delta to (0,0) instead of the real cursor position,
        // which is what made every resize drag jump/stutter right from the
        // start rather than tracking the mouse smoothly.
        var lParam = MakeLParam((int)Math.Round(screenPoint.X), (int)Math.Round(screenPoint.Y));
        SendMessage(hwnd, WM_NCLBUTTONDOWN, (IntPtr)(int)edge, lParam);
        e.Handled = true;
        return true;
    }

    private static IntPtr MakeLParam(int x, int y) => (IntPtr)((y << 16) | (x & 0xFFFF));

    private ResizeEdge HitTestResizeEdge(Point pointInWindow)
    {
        var left = pointInWindow.X <= ResizeBorderThicknessDips;
        var right = pointInWindow.X >= ActualWidth - ResizeBorderThicknessDips;
        var top = pointInWindow.Y <= ResizeBorderThicknessDips;
        var bottom = pointInWindow.Y >= ActualHeight - ResizeBorderThicknessDips;

        if (top && left) return ResizeEdge.TopLeft;
        if (top && right) return ResizeEdge.TopRight;
        if (bottom && left) return ResizeEdge.BottomLeft;
        if (bottom && right) return ResizeEdge.BottomRight;
        if (left) return ResizeEdge.Left;
        if (right) return ResizeEdge.Right;
        if (top) return ResizeEdge.Top;
        if (bottom) return ResizeEdge.Bottom;
        return ResizeEdge.None;
    }

    // ----- Window chrome buttons -----

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void MaximizeRestoreButton_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    // ----- Drag and drop (spec section 17, 34) -----

    private void Window_DragEnter(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            return;
        }

        if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files)
        {
            _viewModel.OpenPath(files[0]);
        }
    }

    // ----- File dialogs (require a Window owner, so they live in the view) -----

    private void OpenMediaMenuItem_Click(object sender, RoutedEventArgs e) => OpenMediaDialog();

    private void OpenMediaDialog()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Open Media",
            Filter = BuildMediaFilter(),
            InitialDirectory = string.IsNullOrEmpty(_viewModel.Settings.LastOpenedDirectory)
                ? null
                : _viewModel.Settings.LastOpenedDirectory
        };

        if (dialog.ShowDialog(this) == true)
        {
            _viewModel.OpenPath(dialog.FileName);
        }
    }

    private void LoadSubtitleDialog()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Load Subtitle",
            Filter = "Subtitle Files|*.srt;*.vtt;*.ass;*.ssa;*.sub|All Files|*.*"
        };

        if (dialog.ShowDialog(this) == true)
        {
            _viewModel.AttachSubtitle(dialog.FileName, showOverlay: true);
        }
    }

    private static string BuildMediaFilter()
    {
        var videoPatterns = string.Join(";", FileTypeHelper.VideoExtensions.Select(ext => "*" + ext));
        var audioPatterns = string.Join(";", FileTypeHelper.AudioExtensions.Select(ext => "*" + ext));
        return $"Media Files|{videoPatterns};{audioPatterns}|All Files|*.*";
    }

    // ----- Context menu (built in code-behind to avoid fragile ContextMenu/Popup data-binding) -----

    private void RootContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        AlwaysOnTopMenuItem.IsChecked = _viewModel.IsAlwaysOnTop;
        FullscreenMenuItem.IsChecked = _viewModel.IsFullscreen;
        ShowTranscriptMenuItem.IsChecked = _viewModel.IsTranscriptPanelVisible;

        var intervalLabel = FormatSecondsLabelCompact(_viewModel.SeekIntervalSeconds);
        SeekBackwardMenuItem.Header = $"Seek Backward {intervalLabel}";
        SeekForwardMenuItem.Header = $"Seek Forward {intervalLabel}";

        PopulatePlaybackSpeedMenu();
        PopulateSubtitleMenu();
        PopulateAudioTrackMenu();
    }

    private void PopulatePlaybackSpeedMenu()
    {
        PlaybackSpeedMenuItem.Items.Clear();
        foreach (var speed in PlaybackConstants.PlaybackSpeeds)
        {
            var item = new MenuItem
            {
                Header = $"{speed:0.##}x",
                IsCheckable = true,
                IsChecked = Math.Abs(_viewModel.PlaybackSpeed - speed) < 0.001
            };
            item.Click += (_, _) => _viewModel.PlaybackSpeed = speed;
            PlaybackSpeedMenuItem.Items.Add(item);
        }
    }

    private void PopulateSubtitleMenu()
    {
        SubtitleMenuItem.Items.Clear();

        SubtitleMenuItem.Items.Add(new MenuItem
        {
            Header = "Show Subtitles",
            IsCheckable = true,
            IsChecked = _viewModel.SelectedSubtitleTrack is { Id: >= 0 },
            Command = _viewModel.ToggleShowSubtitlesCommand
        });
        SubtitleMenuItem.Items.Add(new Separator());

        foreach (var track in _viewModel.SubtitleTracks.Where(t => t.Id >= 0))
        {
            var item = new MenuItem
            {
                Header = track.Name,
                IsCheckable = true,
                IsChecked = _viewModel.SelectedSubtitleTrack?.Id == track.Id
            };
            item.Click += (_, _) => _viewModel.SelectedSubtitleTrack = track;
            SubtitleMenuItem.Items.Add(item);
        }

        SubtitleMenuItem.Items.Add(new Separator());
        var loadItem = new MenuItem { Header = "Load Subtitle File..." };
        loadItem.Click += (_, _) => LoadSubtitleDialog();
        SubtitleMenuItem.Items.Add(loadItem);
        SubtitleMenuItem.Items.Add(new Separator());

        SubtitleMenuItem.Items.Add(new MenuItem { Header = "Subtitle Earlier 100 ms", Command = _viewModel.ShiftSubtitleEarlierSmallCommand });
        SubtitleMenuItem.Items.Add(new MenuItem { Header = "Subtitle Later 100 ms", Command = _viewModel.ShiftSubtitleLaterSmallCommand });
        SubtitleMenuItem.Items.Add(new MenuItem { Header = "Reset Subtitle Delay", Command = _viewModel.ResetSubtitleDelayCommand });
        SubtitleMenuItem.Items.Add(new Separator());

        SubtitleMenuItem.Items.Add(new MenuItem { Header = "Position Up", Command = _viewModel.ShiftSubtitlePositionUpCommand });
        SubtitleMenuItem.Items.Add(new MenuItem { Header = "Position Down", Command = _viewModel.ShiftSubtitlePositionDownCommand });
        SubtitleMenuItem.Items.Add(new MenuItem { Header = "Reset Subtitle Position", Command = _viewModel.ResetSubtitlePositionCommand });
    }

    private void PopulateAudioTrackMenu()
    {
        AudioTrackMenuItem.Items.Clear();

        if (_viewModel.AudioTracks.Count == 0)
        {
            AudioTrackMenuItem.Items.Add(new MenuItem { Header = "(no audio tracks)", IsEnabled = false });
            return;
        }

        foreach (var track in _viewModel.AudioTracks)
        {
            var item = new MenuItem
            {
                Header = track.Name,
                IsCheckable = true,
                IsChecked = _viewModel.SelectedAudioTrack?.Id == track.Id
            };
            item.Click += (_, _) => _viewModel.SelectedAudioTrack = track;
            AudioTrackMenuItem.Items.Add(item);
        }
    }

    private static string FormatSecondsLabelCompact(double seconds) =>
        seconds % 1 == 0 ? $"{seconds:0}s" : $"{seconds:0.##}s";

    private void CopyFilePathMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_viewModel.CurrentFilePath))
        {
            Clipboard.SetText(_viewModel.CurrentFilePath);
        }
    }

    private void MediaInformationMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_viewModel.CurrentFilePath))
        {
            return;
        }

        var path = _viewModel.CurrentFilePath;
        var fileInfo = new FileInfo(path);
        var player = _viewModel.Player;

        var width = 0u;
        var height = 0u;
        player.Size(0, ref width, ref height);

        var lines = new List<string>
        {
            Path.GetFileName(path),
            path,
            $"Duration: {TimeFormatter.FormatMilliseconds(player.Length)}",
        };

        if (width > 0 && height > 0)
        {
            lines.Add($"Resolution: {width} x {height}");
        }

        if (player.Fps > 0)
        {
            lines.Add($"Frame rate: {player.Fps:0.##} fps");
        }

        if (fileInfo.Exists)
        {
            lines.Add($"File size: {fileInfo.Length / 1024.0 / 1024.0:0.##} MB");
        }

        MessageBox.Show(this, string.Join("\n", lines), "Media Information", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    // ----- Lifecycle -----

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        // While fullscreen, Left/Top/Width/Height are the monitor's full
        // bounds (see ApplyFullscreenState), not the window's real "normal"
        // size - saving those directly would make the next launch open
        // at that same monitor-covering size. _preFullscreenBounds holds
        // what the window looked like right before entering fullscreen.
        _viewModel.Settings.WindowMaximized = !_viewModel.IsFullscreen && WindowState == WindowState.Maximized;

        var bounds = _viewModel.IsFullscreen
            ? _preFullscreenBounds
            : WindowState == WindowState.Maximized ? RestoreBounds : new Rect(Left, Top, Width, Height);
        if (bounds.Width > 0 && bounds.Height > 0)
        {
            _viewModel.Settings.WindowWidth = bounds.Width;
            _viewModel.Settings.WindowHeight = bounds.Height;
            _viewModel.Settings.WindowLeft = bounds.Left;
            _viewModel.Settings.WindowTop = bounds.Top;
        }

        _viewModel.PersistSettings();
        _viewModel.Dispose();
    }
}
