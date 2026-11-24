using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Islet.Models;
using GsmtcManager = global::Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager;
using GsmtcSession = global::Windows.Media.Control.GlobalSystemMediaTransportControlsSession;
using GsmtcStatus = global::Windows.Media.Control.GlobalSystemMediaTransportControlsSessionPlaybackStatus;

namespace Islet.Services;

/// <summary>
/// Reads "now playing" from Windows' System Media Transport Controls (the same source as the
/// volume flyout). Spotify desktop and browsers playing YouTube both publish sessions here,
/// so no API keys or sign-in are needed. All events are raised on the UI dispatcher.
/// </summary>
public sealed class MediaService : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _debounce;
    private readonly HashSet<GsmtcSession> _hooked = new(ReferenceEqualityComparer.Instance);

    private GsmtcManager? _manager;
    private GsmtcSession? _active;
    private bool _refreshing, _refreshAgain;

    // Cover cache so timeline updates don't re-decode the image.
    private string? _coverKey;
    private ImageSource? _cover;
    private Color? _coverAccent;

    public MediaService(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        // Apps fire several events per track change (and sometimes stale data first): coalesce them.
        _debounce = new DispatcherTimer(TimeSpan.FromMilliseconds(150), DispatcherPriority.Normal,
            async (_, _) => { _debounce.Stop(); await RefreshAsync(); }, dispatcher);
        _debounce.Stop();
    }

    /// <summary>Raised with the current track, or null when nothing is playing.</summary>
    public event Action<TrackInfo?>? Changed;

    public TrackInfo? Current { get; private set; }

    public async Task StartAsync()
    {
        try
        {
            _manager = await GsmtcManager.RequestAsync();
        }
        catch (Exception)
        {
            return; // SMTC unavailable (very old Windows build); the islet just shows "no media".
        }

        _manager.SessionsChanged += (_, _) => QueueRefresh();
        _manager.CurrentSessionChanged += (_, _) => QueueRefresh();
        await RefreshAsync();
    }

    // ---- Controls ----------------------------------------------------------

    public Task TogglePlayPauseAsync() => Run(s => s.TryTogglePlayPauseAsync().AsTask());
    public Task NextAsync() => Run(s => s.TrySkipNextAsync().AsTask());
    public Task PreviousAsync() => Run(s => s.TrySkipPreviousAsync().AsTask());

    public Task SeekAsync(TimeSpan position) => Run(s =>
    {
        var start = s.GetTimelineProperties().StartTime;
        return s.TryChangePlaybackPositionAsync((start + position).Ticks).AsTask();
    });

    private async Task Run(Func<GsmtcSession, Task<bool>> action)
    {
        if (_active is not { } s) return;
        try { await action(s); }
        catch (Exception) { /* session closed in the meantime */ }
        QueueRefresh();
    }

    // ---- Session tracking ---------------------------------------------------

    private void QueueRefresh() => _dispatcher.BeginInvoke(() =>
    {
        _debounce.Stop();
        _debounce.Start();
    });

    private async Task RefreshAsync()
    {
        if (_manager is null) return;
        if (_refreshing) { _refreshAgain = true; return; }
        _refreshing = true;
        try
        {
            do
            {
                _refreshAgain = false;
                await RefreshCoreAsync(_manager);
            } while (_refreshAgain);
        }
        finally
        {
            _refreshing = false;
        }
    }

    private async Task RefreshCoreAsync(GsmtcManager manager)
    {
        IReadOnlyList<GsmtcSession> sessions;
        try { sessions = manager.GetSessions(); }
        catch (Exception) { return; }

        HookSessions(sessions);
        _active = Choose(manager, sessions);

        TrackInfo? track = null;
        if (_active is { } s)
        {
            try { track = await SnapshotAsync(s); }
            catch (Exception) { track = null; } // session vanished mid-read
        }

        Current = track;
        Changed?.Invoke(track);
    }

    private void HookSessions(IReadOnlyList<GsmtcSession> sessions)
    {
        _hooked.RemoveWhere(h => !sessions.Any(x => ReferenceEquals(x, h)));
        foreach (var s in sessions)
        {
            if (!_hooked.Add(s)) continue;
            s.MediaPropertiesChanged += (_, _) => QueueRefresh();
            s.PlaybackInfoChanged += (_, _) => QueueRefresh();
            s.TimelinePropertiesChanged += (_, _) => QueueRefresh();
        }
    }

    /// <summary>
    /// Prefer whatever is actually playing; keep showing the last active session while it's paused
    /// so pausing Spotify doesn't make the islet jump to some idle browser tab.
    /// </summary>
    private GsmtcSession? Choose(GsmtcManager manager, IReadOnlyList<GsmtcSession> sessions)
    {
        if (sessions.Count == 0) return null;

        GsmtcSession? previous = _active is null ? null
            : sessions.FirstOrDefault(s => ReferenceEquals(s, _active))
              ?? sessions.FirstOrDefault(s => SafeId(s) == SafeId(_active));

        GsmtcSession? current = null;
        try { current = manager.GetCurrentSession(); } catch (Exception) { }

        if (previous is not null && IsPlaying(previous)) return previous;
        if (current is not null && IsPlaying(current)) return current;
        return sessions.FirstOrDefault(IsPlaying) ?? previous ?? current ?? sessions[0];
    }

    private static bool IsPlaying(GsmtcSession s)
    {
        try { return s.GetPlaybackInfo().PlaybackStatus == GsmtcStatus.Playing; }
        catch (Exception) { return false; }
    }

    private static string SafeId(GsmtcSession s)
    {
        try { return s.SourceAppUserModelId ?? ""; }
        catch (Exception) { return ""; }
    }

    // ---- Snapshot -----------------------------------------------------------

    private async Task<TrackInfo> SnapshotAsync(GsmtcSession s)
    {
        var props = await s.TryGetMediaPropertiesAsync();
        var playback = s.GetPlaybackInfo();
        var timeline = s.GetTimelineProperties();

        string appId = s.SourceAppUserModelId ?? "";
        string title = props?.Title ?? "";
        string artist = props?.Artist ?? "";
        if (string.IsNullOrEmpty(artist)) artist = props?.AlbumArtist ?? "";

        bool playing = playback.PlaybackStatus == GsmtcStatus.Playing;

        var duration = timeline.EndTime - timeline.StartTime;
        if (duration < TimeSpan.Zero) duration = TimeSpan.Zero;
        var position = timeline.Position - timeline.StartTime;

        // Apps only push the timeline every few seconds; extrapolate to "now".
        if (playing && timeline.LastUpdatedTime.Year > 2000)
        {
            var drift = DateTimeOffset.Now - timeline.LastUpdatedTime;
            if (drift > TimeSpan.Zero && drift < TimeSpan.FromHours(1))
                position += drift * (playback.PlaybackRate ?? 1.0);
        }
        if (position < TimeSpan.Zero) position = TimeSpan.Zero;
        if (duration > TimeSpan.Zero && position > duration) position = duration;

        var (source, fallbackAccent) = DescribeSource(appId, title);
        if (string.IsNullOrEmpty(title)) title = source;

        await LoadCoverAsync(props?.Thumbnail, $"{appId}|{title}|{artist}");

        return new TrackInfo(
            Title: title,
            Artist: artist,
            Source: source,
            Duration: duration,
            Position: position,
            IsPlaying: playing,
            CanSeek: duration > TimeSpan.Zero && playback.Controls.IsPlaybackPositionEnabled,
            Cover: _cover,
            Accent: _coverAccent ?? fallbackAccent);
    }

    private async Task LoadCoverAsync(global::Windows.Storage.Streams.IRandomAccessStreamReference? thumb, string key)
    {
        if (key == _coverKey && _cover is not null) return;

        _coverKey = key;
        _cover = null;
        _coverAccent = null;
        if (thumb is null) return; // Spotify sometimes sends the art a moment later; we'll retry then.

        try
        {
            using var ras = await thumb.OpenReadAsync();
            using var input = ras.AsStreamForRead();
            var ms = new MemoryStream();
            await input.CopyToAsync(ms);
            ms.Position = 0;

            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.DecodePixelWidth = 240;
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();

            if (_coverKey != key) return; // track changed while decoding
            _cover = bmp;
            _coverAccent = DominantColor(bmp);
        }
        catch (Exception)
        {
            _cover = null;
        }
    }

    /// <summary>Saturation-weighted average colour, lifted so it stays readable on the dark islet.</summary>
    private static Color? DominantColor(BitmapSource src)
    {
        if (src.PixelWidth == 0 || src.PixelHeight == 0) return null;

        var small = new TransformedBitmap(src, new ScaleTransform(24.0 / src.PixelWidth, 24.0 / src.PixelHeight));
        var bgra = new FormatConvertedBitmap(small, PixelFormats.Bgra32, null, 0);
        int w = bgra.PixelWidth, h = bgra.PixelHeight;
        var px = new byte[w * h * 4];
        bgra.CopyPixels(px, w * 4, 0);

        double r = 0, g = 0, b = 0, total = 0;
        for (int i = 0; i < px.Length; i += 4)
        {
            int B = px[i], G = px[i + 1], R = px[i + 2];
            int max = Math.Max(R, Math.Max(G, B)), min = Math.Min(R, Math.Min(G, B));
            double sat = max == 0 ? 0 : (max - min) / (double)max;
            double weight = 0.02 + sat * sat * (max / 255.0);
            r += R * weight; g += G * weight; b += B * weight; total += weight;
        }
        r /= total; g /= total; b /= total;

        double peak = Math.Max(r, Math.Max(g, b));
        if (peak < 150 && peak > 0)
        {
            double k = 150 / peak;
            r *= k; g *= k; b *= k;
        }
        return Color.FromRgb((byte)Math.Min(255, r), (byte)Math.Min(255, g), (byte)Math.Min(255, b));
    }

    // ---- Source naming --------------------------------------------------------

    private static (string Name, Color Accent) DescribeSource(string appId, string title)
    {
        string id = appId.ToLowerInvariant();
        if (id.Contains("spotify")) return ("Spotify", Color.FromRgb(0x1E, 0xD7, 0x60));

        // Browsers don't say which site is playing, so look for a YouTube window/tab
        // whose title contains this media's title (the active tab's title is the window title).
        string? yt = FindYouTubeWindow(title);
        if (yt is not null) return (yt, Color.FromRgb(0xFF, 0x33, 0x33));

        string name = id switch
        {
            _ when id.Contains("chrome") => "Chrome",
            _ when id.Contains("msedge") => "Edge",
            _ when id.Contains("firefox") || id == "308046b0af4a39cb" => "Firefox",
            _ when id.Contains("brave") => "Brave",
            _ when id.Contains("opera") => "Opera",
            _ => PrettyAppName(appId),
        };
        return (name, Color.FromRgb(0x4C, 0x9A, 0xFF));
    }

    private static string PrettyAppName(string appId)
    {
        string n = appId;
        int bang = n.LastIndexOf('!');
        if (bang >= 0) n = n[(bang + 1)..];
        if (n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) n = n[..^4];
        return n.Length == 0 ? "媒體" : n;
    }

    private static string? FindYouTubeWindow(string mediaTitle)
    {
        if (string.IsNullOrWhiteSpace(mediaTitle)) return null;
        string? found = null;
        var sb = new StringBuilder(512);

        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd)) return true;
            sb.Clear();
            if (GetWindowText(hwnd, sb, sb.Capacity) == 0) return true;
            string t = sb.ToString();
            if (!t.Contains("YouTube", StringComparison.OrdinalIgnoreCase)) return true;
            if (!t.Contains(mediaTitle, StringComparison.OrdinalIgnoreCase)) return true;
            found = t.Contains("YouTube Music", StringComparison.OrdinalIgnoreCase) ? "YouTube Music" : "YouTube";
            return false;
        }, IntPtr.Zero);

        return found;
    }

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int maxCount);

    public void Dispose()
    {
        _debounce.Stop();
        _hooked.Clear();
        _manager = null;
        _active = null;
    }
}
