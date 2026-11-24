using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Media;
using System.Windows.Threading;
using Islet.Models;
using Islet.Services;

namespace Islet.ViewModels;

/// <summary>
/// Main view model. Music comes from Windows media controls (MediaService), notifications from
/// the Windows notification centre (NotificationService); todos are still in-memory.
/// </summary>
public sealed class MainViewModel : ViewModelBase
{
    private static readonly CultureInfo Zh = CultureInfo.GetCultureInfo("zh-TW");
    private static readonly string[] Modes = { "一般", "專注", "自訂" };

    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly MediaService _media = new(Dispatcher.CurrentDispatcher);
    private readonly NotificationService _notif = new(Dispatcher.CurrentDispatcher);

    private bool _isDnd;
    private string _mode = "一般";
    private DateTime _now = DateTime.Now;
    private string _peekBadge = "";
    private string _peekText = "";
    private Brush _peekBadgeBrush = Brushes.Gray;

    public MainViewModel()
    {
        Notifications.CollectionChanged += OnNotificationsChanged;
        Todos.CollectionChanged += OnTodosChanged;

        _clock.Tick += OnTick;
        _clock.Start();

        _media.Changed += OnMediaChanged;
        _ = _media.StartAsync();

        _notif.Arrived += OnNotificationArrived;
        _notif.Existing += OnExistingNotifications;
        _notif.AccessChanged += _ => RaiseNotificationState();
        _ = _notif.StartAsync();
    }

    /// <summary>Raised when the islet should show a short "peek" summary.</summary>
    public event Action? PeekRequested;

    // ---- Clock -------------------------------------------------------------

    public string TimeText => _now.ToString("HH:mm", Zh);
    public string DateText => _now.ToString("M月d日 dddd", Zh);
    /// <summary>Compact date + time for the header chip on every page, e.g. "11/24 週二 15:09".</summary>
    public string ClockText => _now.ToString("M/d ddd HH:mm", Zh);

    // ---- Mode / DND --------------------------------------------------------

    public string Mode
    {
        get => _mode;
        private set => Set(ref _mode, value);
    }

    public bool IsDnd
    {
        get => _isDnd;
        set => Set(ref _isDnd, value);
    }

    public void SetMode(string mode)
    {
        Mode = mode;
        if (mode == "專注") IsDnd = true;
        else if (mode == "一般") IsDnd = false;
        Peek("◎", Brushes.DimGray, $"模式切換 · {mode}", force: true);
    }

    public void CycleMode()
    {
        int i = Array.IndexOf(Modes, Mode);
        SetMode(Modes[(i + 1) % Modes.Length]);
    }

    // ---- Peek --------------------------------------------------------------

    public string PeekBadge { get => _peekBadge; private set => Set(ref _peekBadge, value); }
    public string PeekText { get => _peekText; private set => Set(ref _peekText, value); }
    public Brush PeekBadgeBrush { get => _peekBadgeBrush; private set => Set(ref _peekBadgeBrush, value); }

    private void Peek(string badge, Brush brush, string text, bool force = false)
    {
        if (IsDnd && !force) return;
        PeekBadge = badge;
        PeekBadgeBrush = brush;
        PeekText = text;
        PeekRequested?.Invoke();
    }

    // ---- Music -------------------------------------------------------------
    // Real data from Windows media controls (Spotify desktop, YouTube in a browser, …).

    private static readonly Brush DefaultAccent = MakeBrush(Color.FromRgb(0x4C, 0x9A, 0xFF));

    private TrackInfo? _track;
    private readonly System.Diagnostics.Stopwatch _sinceSnapshot = new();
    private Brush _accentBrush = DefaultAccent;
    private Brush _coverBrush = DefaultAccent;

    /// <summary>False when no app is playing media (the music page then only shows a hint).</summary>
    public bool HasMedia => _track is not null;
    public bool HasNoMedia => !HasMedia;

    public string TrackTitle => _track?.Title ?? "";
    public string TrackArtist => _track?.Artist ?? "";
    public string TrackSource => _track?.Source ?? "";
    public Brush AccentBrush => _accentBrush;
    /// <summary>Album art / video thumbnail, or the accent colour when there is none.</summary>
    public Brush CoverBrush => _coverBrush;
    public bool HasNoCover => _track?.Cover is null;
    public bool IsPlaying => _track?.IsPlaying ?? false;
    public string PlayGlyph => IsPlaying ? "\uE769" : "\uE768";

    private TimeSpan Duration => _track?.Duration ?? TimeSpan.Zero;

    private TimeSpan Position
    {
        get
        {
            if (_track is null) return TimeSpan.Zero;
            var pos = _track.Position + (_track.IsPlaying ? _sinceSnapshot.Elapsed : TimeSpan.Zero);
            return Duration > TimeSpan.Zero && pos > Duration ? Duration : pos;
        }
    }

    public double ProgressFraction => Duration > TimeSpan.Zero ? Position / Duration : 0;
    public string ElapsedText => FormatTime(Position);
    public string DurationText => Duration > TimeSpan.Zero ? FormatTime(Duration) : "直播";

    public void TogglePlay() => _ = _media.TogglePlayPauseAsync();
    public void NextTrack() => _ = _media.NextAsync();
    public void PrevTrack() => _ = _media.PreviousAsync();

    public void Seek(double fraction)
    {
        if (_track is not { CanSeek: true } t) return;
        var target = Duration * Math.Clamp(fraction, 0, 1);
        _track = t with { Position = target };   // move the bar right away; the app confirms shortly
        _sinceSnapshot.Restart();
        RaiseProgress();
        _ = _media.SeekAsync(target);
    }

    private void OnMediaChanged(TrackInfo? track)
    {
        var previous = _track;
        _track = track;
        _sinceSnapshot.Restart();

        if (track is null)
        {
            _accentBrush = _coverBrush = DefaultAccent;
        }
        else if (previous is null || previous.Accent != track.Accent || !ReferenceEquals(previous.Cover, track.Cover))
        {
            _accentBrush = MakeBrush(track.Accent);
            if (track.Cover is { } cover)
            {
                var img = new ImageBrush(cover) { Stretch = Stretch.UniformToFill };
                img.Freeze();
                _coverBrush = img;
            }
            else
            {
                _coverBrush = _accentBrush;
            }
        }

        foreach (var name in new[]
        {
            nameof(HasMedia), nameof(HasNoMedia), nameof(TrackTitle), nameof(TrackArtist), nameof(TrackSource),
            nameof(AccentBrush), nameof(CoverBrush), nameof(HasNoCover), nameof(IsPlaying), nameof(PlayGlyph),
            nameof(DurationText),
        })
            OnPropertyChanged(name);
        RaiseProgress();

        bool newSong = track is not null && track.IsPlaying &&
                       (previous is null || previous.Title != track.Title || previous.Artist != track.Artist);
        if (newSong) Peek("♪", _accentBrush, $"{track!.Source} · {track.Title}");
    }

    private void RaiseProgress()
    {
        OnPropertyChanged(nameof(ProgressFraction));
        OnPropertyChanged(nameof(ElapsedText));
    }

    private static string FormatTime(TimeSpan t) =>
        t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{(int)t.TotalMinutes}:{t.Seconds:00}";

    // ---- Notifications -----------------------------------------------------

    public ObservableCollection<NotificationItem> Notifications { get; } = new();

    public int UnreadCount => Notifications.Count(n => n.IsUnread);
    public bool HasNoNotifications => Notifications.Count == 0;
    public bool HasNotifications => Notifications.Count > 0;
    public string NotifTabText => UnreadCount > 0 ? $"通知 {UnreadCount}" : "通知";
    public string NotifSummary => $"共 {Notifications.Count} 則，{UnreadCount} 則未讀";

    /// <summary>Windows hasn't given us access yet: show the "open settings" hint instead of an empty list.</summary>
    public bool NotifNeedsAccess => _notif.Access == NotificationAccess.Denied;

    public string NotifEmptyText => _notif.Access switch
    {
        NotificationAccess.Denied => "尚未允許 Islet 讀取通知",
        NotificationAccess.Unsupported => "這個 Windows 版本無法讀取通知（需要打包版）",
        NotificationAccess.Unknown => "正在連接通知中心…",
        _ => "沒有通知",
    };

    private const int MaxNotifications = 100;
    private static readonly TimeSpan MergeWindow = TimeSpan.FromSeconds(8);
    private string _lastPeekApp = "";
    private DateTime _lastPeekAt;
    private int _mergedCount;

    public void MarkRead(NotificationItem item) => item.IsRead = true;

    /// <summary>Click on a card: mark it read and bring up the app that sent it.</summary>
    public void OpenNotification(NotificationItem item)
    {
        item.IsRead = true;
        NotificationService.OpenSourceApp(item);
    }

    public void MarkAllRead()
    {
        foreach (var n in Notifications) n.IsRead = true;
    }

    public void OpenNotificationSettings() => NotificationService.OpenAccessSettings();

    private void OnExistingNotifications(IReadOnlyList<NotificationItem> items)
    {
        // Oldest first from the service; the list shows newest on top.
        foreach (var item in items) AddOrReplace(item);
        TrimNotifications();
    }

    private void OnNotificationArrived(NotificationItem item)
    {
        // Apps like Instagram web keep re-sending the same "你有未查看的通知。": keep one copy,
        // move it to the top, and don't peek again for the same text.
        bool repeat = AddOrReplace(item);
        TrimNotifications();
        if (repeat) return;

        // Several messages from the same app in a short burst fold into one "N 則新訊息" peek.
        var now = DateTime.Now;
        if (item.AppName == _lastPeekApp && now - _lastPeekAt < MergeWindow)
            _mergedCount++;
        else
            _mergedCount = 1;
        _lastPeekApp = item.AppName;
        _lastPeekAt = now;

        string text = _mergedCount > 1
            ? $"{item.AppName} · {_mergedCount} 則新訊息"
            : item.HasBody ? $"{item.Title}：{item.Body}" : $"{item.AppName} · {item.Title}";
        Peek(item.Badge, item.BadgeBrush, text);   // suppressed while DND; it's still in the list
    }

    /// <summary>
    /// Puts the item on top. An older notification with the same app, title and body is removed
    /// first (it stays unread if either copy is). Returns true when it was such a repeat.
    /// </summary>
    private bool AddOrReplace(NotificationItem item)
    {
        var old = Notifications.FirstOrDefault(n =>
            n.AppName == item.AppName && n.Title == item.Title && n.Body == item.Body);
        if (old is not null)
        {
            if (old.IsUnread) item.IsRead = false;
            Notifications.Remove(old);
        }
        Notifications.Insert(0, item);
        return old is not null;
    }

    private void TrimNotifications()
    {
        while (Notifications.Count > MaxNotifications) Notifications.RemoveAt(Notifications.Count - 1);
    }

    public void Dismiss(NotificationItem item) => Notifications.Remove(item);

    public void ClearNotifications() => Notifications.Clear();

    private void OnNotificationsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
            foreach (NotificationItem n in e.NewItems) n.PropertyChanged += OnNotificationItemChanged;
        if (e.OldItems != null)
            foreach (NotificationItem n in e.OldItems) n.PropertyChanged -= OnNotificationItemChanged;
        RaiseNotificationState();
    }

    private void OnNotificationItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NotificationItem.IsRead)) RaiseNotificationState();
    }

    private void RaiseNotificationState()
    {
        OnPropertyChanged(nameof(UnreadCount));
        OnPropertyChanged(nameof(HasNoNotifications));
        OnPropertyChanged(nameof(HasNotifications));
        OnPropertyChanged(nameof(NotifTabText));
        OnPropertyChanged(nameof(NotifSummary));
        OnPropertyChanged(nameof(NotifNeedsAccess));
        OnPropertyChanged(nameof(NotifEmptyText));
    }

    // ---- Todos -------------------------------------------------------------

    public ObservableCollection<TodoItem> Todos { get; } = new();

    private TodoItem? NextTodo => Todos.FirstOrDefault(t => !t.IsDone);
    public string NextTodoTitle => NextTodo?.Text ?? "沒有待辦";
    public string NextTodoDue => NextTodo?.Due ?? "";

    public void AddTodo(string text)
    {
        text = text.Trim();
        if (text.Length == 0) return;
        Todos.Add(new TodoItem { Text = text });
    }

    private void OnTodosChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
            foreach (TodoItem t in e.NewItems) t.PropertyChanged += OnTodoItemChanged;
        if (e.OldItems != null)
            foreach (TodoItem t in e.OldItems) t.PropertyChanged -= OnTodoItemChanged;
        RaiseNextTodo();
    }

    private void OnTodoItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TodoItem.IsDone)) RaiseNextTodo();
    }

    private void RaiseNextTodo()
    {
        OnPropertyChanged(nameof(NextTodoTitle));
        OnPropertyChanged(nameof(NextTodoDue));
    }

    // ---- Timer -------------------------------------------------------------

    private void OnTick(object? sender, EventArgs e)
    {
        _now = DateTime.Now;
        OnPropertyChanged(nameof(TimeText));
        OnPropertyChanged(nameof(DateText));
        OnPropertyChanged(nameof(ClockText));

        if (IsPlaying) RaiseProgress();

        // After the user flips the switch in Settings, pick it up without a restart.
        if (_notif.Access == NotificationAccess.Denied && _now.Second % 5 == 0) _ = _notif.StartAsync();
    }

    private static SolidColorBrush MakeBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
