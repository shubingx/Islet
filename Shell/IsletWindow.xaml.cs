using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Islet.Models;
using Islet.ViewModels;

namespace Islet.Shell;

/// <summary>Islet visual states. Order matters: a higher value means a bigger islet.</summary>
public enum IsletState
{
    Idle,
    Peek,
    Expanded,
}

public partial class IsletWindow : Window
{
    // Space around the islet inside the window (shadow + spring overshoot).
    private const double HPad = 28;
    private const double TopMargin = 8;
    private const double BottomPad = 28;

    /// <summary>Overall size of the islet. 1.0 = original design size; lower = smaller.</summary>
    public const double UiScale = 1.0;

    // Collapsed islet: hovering this long expands it. Longer than before so that reaching
    // for a browser tab / the new-tab button underneath doesn't pop it open.
    private static readonly TimeSpan HoverDelay = TimeSpan.FromMilliseconds(400);

    // Spring tuning: higher stiffness = faster. Shrinking uses a stiffer spring so collapse feels snappy.
    private const double GrowStiffness = 950, GrowDamping = 46;
    private const double ShrinkStiffness = 1500, ShrinkDamping = 66;

    private readonly MainViewModel _vm = new();
    private readonly SpringAnimator _spring;
    private readonly DispatcherTimer _hoverTimer = new() { Interval = HoverDelay };
    private readonly DispatcherTimer _hoverPoll = new() { Interval = TimeSpan.FromMilliseconds(30) };
    private readonly DispatcherTimer _collapseTimer = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private readonly DispatcherTimer _peekTimer = new() { Interval = TimeSpan.FromSeconds(4) };

    private IsletState _state = IsletState.Idle;
    private bool _pinned;
    private bool _mouseOver;
    private bool _hoverSuppressed;   // user clicked through the islet; ignore hover until the cursor leaves
    private bool _clickThrough;
    private IntPtr _hwnd;
    private bool _menuOpen;

    // Paging
    private FrameworkElement[] _pages = Array.Empty<FrameworkElement>();
    private RadioButton[] _dots = Array.Empty<RadioButton>();
    private int _pageIndex = -1;
    private int _wheelAccum;
    private long _lastWheelTick;
    private long _lastFlipTick;
    private const int WheelCooldownMs = 320;  // min gap between two page flips

    public IsletWindow()
    {
        InitializeComponent();
        DataContext = _vm;
        Root.LayoutTransform = new ScaleTransform(UiScale, UiScale);

        var idle = SizeOf(IsletState.Idle);
        _spring = new SpringAnimator(new[] { idle.W, idle.H, idle.R }, stiffness: GrowStiffness, damping: GrowDamping);
        _spring.Updated += v => ApplyIsletSize(v[0], v[1], v[2]);
        _spring.Completed += () => ApplyWindowSize(_state);

        _hoverTimer.Tick += (_, _) =>
        {
            _hoverTimer.Stop();
            if (_mouseOver && !_hoverSuppressed) SetState(IsletState.Expanded);
        };
        _collapseTimer.Tick += (_, _) =>
        {
            _collapseTimer.Stop();
            if (!_pinned && !_mouseOver && !_menuOpen) SetState(IsletState.Idle);
        };
        _peekTimer.Tick += (_, _) =>
        {
            _peekTimer.Stop();
            if (_state == IsletState.Peek && !_mouseOver) SetState(IsletState.Idle);
        };

        _vm.PeekRequested += OnPeekRequested;
        _vm.PropertyChanged += OnViewModelPropertyChanged;

        ApplyIsletSize(idle.W, idle.H, idle.R);
        ApplyWindowSize(IsletState.Idle);
        _pages = new FrameworkElement[] { PageHome, PageMusic, PageNotif, PageTodo, PageTimer };
        _dots = new[] { TabHome, TabMusic, TabNotif, TabTodo, TabTimer };
        ShowPage(0);
        UpdateEqualizer();

        _hoverPoll.Tick += (_, _) => HoverPoll();
        _hoverPoll.Start();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // Hide from Alt+Tab. (WS_EX_NOACTIVATE is intentionally not set in this test build:
        // it would also stop the todo TextBox from receiving keyboard input.)
        _hwnd = new WindowInteropHelper(this).Handle;
        int ex = NativeMethods.GetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE);
        NativeMethods.SetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE, ex | NativeMethods.WS_EX_TOOLWINDOW);
        SetClickThrough(true);
    }

    /// <summary>
    /// Collapsed / peek: clicks pass through to the window below (e.g. Chrome's tab strip).
    /// Expanded: the islet takes clicks normally.
    /// </summary>
    private void SetClickThrough(bool on)
    {
        if (_hwnd == IntPtr.Zero || on == _clickThrough) return;
        _clickThrough = on;
        int ex = NativeMethods.GetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE);
        ex = on ? ex | NativeMethods.WS_EX_TRANSPARENT : ex & ~NativeMethods.WS_EX_TRANSPARENT;
        NativeMethods.SetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE, ex);
    }

    // ---- State machine -----------------------------------------------------

    private static (double W, double H, double R) SizeOf(IsletState state) => state switch
    {
        IsletState.Idle => (126, 34, 17),
        IsletState.Peek => (300, 46, 23),
        _ => (420, 200, 28),
    };

    private void SetState(IsletState next)
    {
        if (next == _state) return;

        var previous = _state;
        _state = next;
        SetClickThrough(next != IsletState.Expanded);

        var (w, h, r) = SizeOf(next);

        // Growing: enlarge the window first so the islet has room to animate into.
        // Shrinking: the window is trimmed after the spring settles (see Completed).
        if (next > previous) ApplyWindowSize(next);

        bool growing = next > previous;
        _spring.Stiffness = growing ? GrowStiffness : ShrinkStiffness;
        _spring.Damping = growing ? GrowDamping : ShrinkDamping;
        _spring.SetTarget(w, h, r);
        UpdateLayers(next);
    }

    private void UpdateLayers(IsletState state)
    {
        bool idle = state == IsletState.Idle;
        bool peek = state == IsletState.Peek;
        bool expanded = state == IsletState.Expanded;

        Fade(IdleLayer, idle ? 1 : 0, idle ? 130 : 60, idle ? 40 : 0);
        Fade(PeekLayer, peek ? 1 : 0, peek ? 150 : 70, peek ? 60 : 0);
        Fade(ExpandedLayer, expanded ? 1 : 0, expanded ? 140 : 50, expanded ? 50 : 0);
    }

    private static void Fade(UIElement element, double to, int durationMs, int delayMs)
    {
        element.IsHitTestVisible = to > 0;
        element.BeginAnimation(OpacityProperty, new DoubleAnimation(to, TimeSpan.FromMilliseconds(durationMs))
        {
            BeginTime = TimeSpan.FromMilliseconds(delayMs),
        });
    }

    private void ApplyIsletSize(double w, double h, double r)
    {
        IsletHost.Width = Math.Max(20, w);
        IsletHost.Height = Math.Max(20, h);
        IsletBg.CornerRadius = new CornerRadius(Math.Max(0, r));
    }

    /// <summary>Sizes and centers the (transparent) window around the islet for a given state.</summary>
    private void ApplyWindowSize(IsletState state)
    {
        var (w, h, _) = SizeOf(state);
        Width = (w + HPad * 2) * UiScale;
        Height = (h + TopMargin + BottomPad) * UiScale;
        Left = (SystemParameters.PrimaryScreenWidth - Width) / 2;
        Top = 0;
    }

    // ---- Mouse / keyboard --------------------------------------------------

    /// <summary>Tracks whether the cursor is over the islet (works even while click-through).</summary>
    private void HoverPoll()
    {
        if (!IsVisible || !NativeMethods.GetCursorPos(out var p)) return;

        bool over;
        try
        {
            var local = IsletHost.PointFromScreen(new Point(p.X, p.Y));
            over = local.X >= 0 && local.Y >= 0 && local.X <= IsletHost.ActualWidth && local.Y <= IsletHost.ActualHeight;
        }
        catch (InvalidOperationException)
        {
            return; // not yet connected to a presentation source
        }

        if (over && _clickThrough &&
            (NativeMethods.IsKeyDown(NativeMethods.VK_LBUTTON) || NativeMethods.IsKeyDown(NativeMethods.VK_RBUTTON)))
        {
            // The click went to the app underneath (e.g. Chrome's "+" button): don't pop open on it.
            _hoverSuppressed = true;
            _hoverTimer.Stop();
        }

        if (over == _mouseOver) return;
        if (over) OnHoverEnter();
        else OnHoverLeave();
    }

    private void OnHoverEnter()
    {
        _mouseOver = true;
        _collapseTimer.Stop();
        _peekTimer.Stop();
        if (_state != IsletState.Expanded && !_hoverSuppressed) _hoverTimer.Start();
    }

    private void OnHoverLeave()
    {
        _mouseOver = false;
        _hoverSuppressed = false;
        _hoverTimer.Stop();
        if (_state == IsletState.Peek) { _peekTimer.Stop(); _peekTimer.Start(); }
        if (_pinned) return;
        _collapseTimer.Stop();
        _collapseTimer.Start();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        _pinned = false;
        SetState(IsletState.Idle);
    }

    // ---- Peek --------------------------------------------------------------

    private void OnPeekRequested()
    {
        // The expanded islet already shows everything; only peek from smaller states.
        if (_state == IsletState.Expanded) return;

        SetState(IsletState.Peek);
        _peekTimer.Stop();
        _peekTimer.Start();
    }

    // ---- Equalizer bars ----------------------------------------------------

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsPlaying)) UpdateEqualizer();
    }

    private void UpdateEqualizer()
    {
        foreach (var (bar, ms) in new[] { (EqA, 420), (EqB, 330), (EqC, 520) })
        {
            var scale = (ScaleTransform)bar.RenderTransform;
            if (_vm.IsPlaying)
            {
                scale.BeginAnimation(ScaleTransform.ScaleYProperty,
                    new DoubleAnimation(0.25, 1.0, TimeSpan.FromMilliseconds(ms))
                    {
                        AutoReverse = true,
                        RepeatBehavior = RepeatBehavior.Forever,
                    });
            }
            else
            {
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                scale.ScaleY = 0.4;
            }
        }
    }

    // ---- Context menu ------------------------------------------------------

    private void Menu_Opened(object sender, RoutedEventArgs e)
    {
        _menuOpen = true;
        if (sender is ContextMenu menu) SyncMenu(menu.Items);
    }

    private void Menu_Closed(object sender, RoutedEventArgs e)
    {
        _menuOpen = false;
        if (_mouseOver || _pinned) return;
        _collapseTimer.Stop();
        _collapseTimer.Start();
    }

    private void SyncMenu(ItemCollection items)
    {
        foreach (var entry in items)
        {
            if (entry is not MenuItem item) continue;

            if (item.Tag is string tag)
            {
                if (tag == "dnd") item.IsChecked = _vm.IsDnd;
                else if (tag == "pin") item.IsChecked = _pinned;
                else if (tag.StartsWith("mode:")) item.IsChecked = tag.Substring(5) == _vm.Mode;
            }

            if (item.HasItems) SyncMenu(item.Items);
        }
    }

    private void Menu_Click(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not MenuItem { Tag: string tag } item) return;

        switch (tag)
        {
            case "dnd":
                _vm.IsDnd = item.IsChecked;
                break;
            case "pin":
                _pinned = item.IsChecked;
                if (_pinned) SetState(IsletState.Expanded);
                break;
            case "exit":
                Application.Current.Shutdown();
                break;
            default:
                if (tag.StartsWith("mode:")) _vm.SetMode(tag.Substring(5));
                break;
        }
    }

    // ---- Pages (dots + mouse wheel) ----------------------------------------

    private void Tab_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string tag }) return;
        if (int.TryParse(tag, out int index)) ShowPage(index);
    }

    /// <summary>Shows page <paramref name="index"/>, sliding in from the side it comes from.</summary>
    private void ShowPage(int index)
    {
        if (_pages.Length == 0) return;
        index = Math.Clamp(index, 0, _pages.Length - 1);
        if (index == _pageIndex) return;

        int direction = _pageIndex < 0 ? 0 : Math.Sign(index - _pageIndex);
        _pageIndex = index;

        for (int i = 0; i < _pages.Length; i++)
            _pages[i].Visibility = i == index ? Visibility.Visible : Visibility.Collapsed;

        if (_dots[index].IsChecked != true) _dots[index].IsChecked = true;

        if (direction != 0) SlideIn(_pages[index], direction * 36);
    }

    private static void SlideIn(UIElement element, double fromX)
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var duration = TimeSpan.FromMilliseconds(240);

        if (element.RenderTransform is TranslateTransform tt)
            tt.BeginAnimation(TranslateTransform.XProperty,
                new DoubleAnimation(fromX, 0, duration) { EasingFunction = ease });

        element.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration) { EasingFunction = ease });
    }

    private void Islet_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_state != IsletState.Expanded) return;

        // Let a list scroll first; flip the page only once it is already at its edge.
        var sv = FindAncestor<ScrollViewer>(e.OriginalSource as DependencyObject);
        if (sv != null && sv.ScrollableHeight > 0)
        {
            bool canScroll = e.Delta < 0
                ? sv.VerticalOffset < sv.ScrollableHeight - 0.5
                : sv.VerticalOffset > 0.5;
            if (canScroll) { _wheelAccum = 0; return; }
        }

        e.Handled = true;

        long now = Environment.TickCount64;
        if (now - _lastWheelTick > 250) _wheelAccum = 0;   // new gesture
        _lastWheelTick = now;

        // Touchpads send many small deltas; add them up to one notch (120) before flipping.
        _wheelAccum += e.Delta;
        if (Math.Abs(_wheelAccum) < Mouse.MouseWheelDeltaForOneLine) return;
        if (now - _lastFlipTick < WheelCooldownMs) return;

        int step = _wheelAccum < 0 ? +1 : -1;   // wheel down = next page
        _wheelAccum = 0;
        _lastFlipTick = now;
        ShowPage(_pageIndex + step);
    }

    private static T? FindAncestor<T>(DependencyObject? d) where T : DependencyObject
    {
        while (d != null)
        {
            if (d is T match) return match;
            d = d is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(d)
                : LogicalTreeHelper.GetParent(d);
        }
        return null;
    }

    private void Mode_Click(object sender, MouseButtonEventArgs e) => _vm.CycleMode();

    // ---- Music page --------------------------------------------------------

    private void Play_Click(object sender, RoutedEventArgs e) => _vm.TogglePlay();
    private void Next_Click(object sender, RoutedEventArgs e) => _vm.NextTrack();
    private void Prev_Click(object sender, RoutedEventArgs e) => _vm.PrevTrack();

    private void Progress_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement bar || bar.ActualWidth <= 0) return;
        _vm.Seek(e.GetPosition(bar).X / bar.ActualWidth);
    }

    // ---- Notifications page ------------------------------------------------

    private void Notif_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: NotificationItem item }) _vm.OpenNotification(item);
    }

    private void NotifSettings_Click(object sender, RoutedEventArgs e) => _vm.OpenNotificationSettings();

    private void Notif_Dismiss(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: NotificationItem item }) _vm.Dismiss(item);
        e.Handled = true;
    }

    private void ClearAll_Click(object sender, RoutedEventArgs e) => _vm.ClearNotifications();

    // ---- Todo page ---------------------------------------------------------

    private void Todo_TextChanged(object sender, TextChangedEventArgs e)
    {
        TodoHint.Visibility = string.IsNullOrEmpty(TodoInput.Text) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Todo_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        _vm.AddTodo(TodoInput.Text);
        TodoInput.Clear();
        e.Handled = true;
    }

    // ---- Timer page --------------------------------------------------------

    private void TimerMode_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string tag }) _vm.Timer.IsStopwatchMode = tag == "stopwatch";
    }

    private void TimerToggle_Click(object sender, RoutedEventArgs e) => _vm.Timer.Toggle();
    private void TimerReset_Click(object sender, RoutedEventArgs e) => _vm.Timer.Reset();

    private void TimerPreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag } && int.TryParse(tag, out int minutes))
            _vm.Timer.SetDuration(TimeSpan.FromMinutes(minutes));
    }

    private void TimerAdjust_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag } && int.TryParse(tag, out int minutes))
            _vm.Timer.Adjust(minutes);
    }
}
