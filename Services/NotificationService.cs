using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Islet.Models;
using WinNotif = global::Windows.UI.Notifications;
using WinListener = global::Windows.UI.Notifications.Management.UserNotificationListener;
using WinAccess = global::Windows.UI.Notifications.Management.UserNotificationListenerAccessStatus;

namespace Islet.Services;

public enum NotificationAccess
{
    /// <summary>Not asked yet / still starting.</summary>
    Unknown,
    Allowed,
    /// <summary>The user (or a policy) has turned notification access off.</summary>
    Denied,
    /// <summary>This Windows build / an unpackaged app can't use the listener at all.</summary>
    Unsupported,
}

/// <summary>
/// Reads toast notifications from Windows' notification centre (UserNotificationListener), the
/// same list the Action Center shows. Only the visible text is available: no replying or
/// marking as read in the source app. Everything stays on this PC.
///
/// The NotificationChanged event only works for packaged apps (and even then not reliably for
/// desktop apps), so we poll the list and diff by notification id. All events fire on the UI thread.
/// </summary>
public sealed class NotificationService : IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1.5);

    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _poll;
    private readonly HashSet<uint> _seen = new();
    private readonly Dictionary<string, ImageSource?> _iconCache = new();

    private WinListener? _listener;
    private bool _polling;

    public NotificationService(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        _poll = new DispatcherTimer(PollInterval, DispatcherPriority.Background,
            async (_, _) => await PollAsync(initial: false), dispatcher);
        _poll.Stop();
    }

    /// <summary>A notification that arrived while Islet was running.</summary>
    public event Action<NotificationItem>? Arrived;

    /// <summary>Notifications that were already in the notification centre when Islet started.</summary>
    public event Action<IReadOnlyList<NotificationItem>>? Existing;

    public event Action<NotificationAccess>? AccessChanged;

    public NotificationAccess Access { get; private set; } = NotificationAccess.Unknown;

    // Debugger output only (Visual Studio "Output" window); compiled out of Release builds.
    private static void Log(string line) => Debug.WriteLine("[Islet] " + line);

    /// <summary>
    /// Asks for access (Windows shows its consent prompt the first time) and starts listening.
    /// Must be called on the UI thread. Safe to call again, e.g. after the user changed the setting.
    /// </summary>
    public async Task StartAsync()
    {
        try
        {
            _listener ??= WinListener.Current;
            var status = _listener.GetAccessStatus();
            if (status == WinAccess.Unspecified)
                status = await _listener.RequestAccessAsync();

            SetAccess(status == WinAccess.Allowed ? NotificationAccess.Allowed : NotificationAccess.Denied);
        }
        catch (Exception ex)
        {
            // Typically "Element not found"/E_ACCESSDENIED when the app has no package identity
            // on this Windows build. Needs a sparse package / MSIX to work.
            Log($"listener unavailable: {ex.GetType().Name} 0x{ex.HResult:X8} {ex.Message}");
            SetAccess(NotificationAccess.Unsupported);
            return;
        }

        if (Access != NotificationAccess.Allowed) return;

        await PollAsync(initial: _seen.Count == 0);
        _poll.Start();
    }

    private void SetAccess(NotificationAccess access)
    {
        if (Access == access) return;
        Access = access;
        Log($"access = {access}");
        AccessChanged?.Invoke(access);
    }

    // ---- Polling ---------------------------------------------------------------

    private async Task PollAsync(bool initial)
    {
        if (_listener is null || _polling) return;
        _polling = true;
        try
        {
            IReadOnlyList<WinNotif.UserNotification> list;
            try
            {
                list = await _listener.GetNotificationsAsync(WinNotif.NotificationKinds.Toast);
            }
            catch (UnauthorizedAccessException)
            {
                // User switched access off while we were running.
                _poll.Stop();
                SetAccess(NotificationAccess.Denied);
                return;
            }
            catch (Exception ex)
            {
                Log($"GetNotificationsAsync failed: {ex.GetType().Name} {ex.Message}");
                return;
            }

            if (initial) Log($"first poll: {list.Count} notification(s) already in the notification centre");

            var fresh = new List<NotificationItem>();
            foreach (var n in list.OrderBy(n => n.CreationTime))
            {
                if (!_seen.Add(n.Id)) continue;
                var item = await ConvertAsync(n);
                if (item is not null) fresh.Add(item);
            }

            // Forget ids Windows no longer has, so the set doesn't grow forever.
            // (Ids are not reused while a notification is still listed, so this is safe.)
            if (_seen.Count > list.Count + 200)
            {
                var live = list.Select(n => n.Id).ToHashSet();
                _seen.RemoveWhere(id => !live.Contains(id));
            }

            if (fresh.Count == 0) return;
            if (initial)
            {
                foreach (var item in fresh) item.IsRead = true;
                Existing?.Invoke(fresh);
            }
            else
            {
                foreach (var item in fresh) Arrived?.Invoke(item);
            }
        }
        finally
        {
            _polling = false;
        }
    }

    // ---- Conversion --------------------------------------------------------------

    private async Task<NotificationItem?> ConvertAsync(WinNotif.UserNotification n)
    {
        string appName = "", aumid = "";
        global::Windows.ApplicationModel.AppDisplayInfo? display = null;
        try
        {
            var app = n.AppInfo;
            aumid = app?.AppUserModelId ?? "";
            display = app?.DisplayInfo;
            appName = display?.DisplayName ?? "";
        }
        catch (Exception)
        {
            // AppInfo can throw for apps that were uninstalled since.
        }
        if (appName.Length == 0) appName = PrettyName(aumid);
        bool fromBrowser = NotificationFilter.IsBrowser(appName, aumid);

        string title = "", body = "", site = "", rawTexts = "(none)";
        try
        {
            var binding = n.Notification?.Visual?.GetBinding(WinNotif.KnownNotificationBindings.ToastGeneric);
            if (binding is not null)
            {
                var texts = binding.GetTextElements()
                    .Select(t => CleanText(t.Text))
                    .Where(t => t.Length > 0)
                    .ToList();
                rawTexts = string.Join(" | ", texts.Select(t => $"\"{t}\""));

                // Browsers put the sending site ("www.instagram.com") in its own attribution line.
                if (fromBrowser && texts.LastOrDefault(t => SiteLike.IsMatch(t)) is { } siteLine)
                {
                    site = siteLine.ToLowerInvariant();
                    texts.Remove(siteLine);
                }

                if (texts.Count > 0) title = texts[0];
                if (texts.Count > 1) body = string.Join("  ", texts.Skip(1));
            }
        }
        catch (Exception ex)
        {
            // Some apps send visuals we can't read; still list them by app name.
            rawTexts = $"(unreadable: {ex.Message})";
        }

        if (title.Length == 0 && body.Length == 0)
        {
            Log($"toast #{n.Id} app='{appName}' aumid='{aumid}' has no text -> skipped");
            return null;
        }

        bool allowed = NotificationFilter.IsAllowed(appName, aumid, site, title + "\n" + body);
        Log($"toast #{n.Id} app='{appName}' aumid='{aumid}' browser={fromBrowser} site='{site}' texts={rawTexts} -> {(allowed ? "SHOWN" : "filtered")}");
        if (!allowed) return null;

        ImageSource? icon = null;
        if (site.Length > 0)
            appName = SiteName(site);   // show "Instagram", not "Google Chrome"; browser icon would mislead
        else if (fromBrowser && NotificationFilter.SocialSiteIn(title + "\n" + body) is { } host)
            appName = SiteName(host);
        else if (fromBrowser && NotificationFilter.SocialBrandIn(title + "\n" + body) is { } brand)
            appName = brand;
        else if (display is not null)
            icon = await LoadIconAsync(display, aumid.Length > 0 ? aumid : appName);

        if (title.Length == 0) title = appName;

        // Discord: "黃宏緯 (#社課, 一般)" → sender "黃宏緯", context "#社課 · 一般".
        string context = "";
        if (appName.Equals("Discord", StringComparison.OrdinalIgnoreCase))
        {
            var m = DiscordTitle.Match(title);
            if (m.Success)
            {
                title = m.Groups[1].Value.Trim();
                context = m.Groups[3].Success
                    ? $"{m.Groups[2].Value.Trim()} · {m.Groups[3].Value.Trim()}"
                    : m.Groups[2].Value.Trim();
            }
        }

        var (badge, brush) = BadgeFor(appName, aumid);
        return new NotificationItem
        {
            Id = n.Id,
            AppName = appName,
            AppId = aumid,
            Badge = badge,
            BadgeBrush = brush,
            Icon = icon,
            Title = title,
            Context = context,
            Body = body,
            Time = n.CreationTime.LocalDateTime,
        };
    }

    private async Task<ImageSource?> LoadIconAsync(global::Windows.ApplicationModel.AppDisplayInfo display, string key)
    {
        if (_iconCache.TryGetValue(key, out var cached)) return cached;

        ImageSource? result = null;
        try
        {
            var logo = display.GetLogo(new global::Windows.Foundation.Size(64, 64));
            using var ras = await logo.OpenReadAsync();
            using var input = ras.AsStreamForRead();
            var ms = new MemoryStream();
            await input.CopyToAsync(ms);
            ms.Position = 0;

            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.DecodePixelWidth = 56;
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();
            result = bmp;
        }
        catch (Exception)
        {
            result = null; // fall back to the letter badge
        }

        _iconCache[key] = result;
        return result;
    }

    private static (string Badge, Brush Brush) BadgeFor(string appName, string aumid)
    {
        string s = (appName + " " + aumid).ToLowerInvariant();
        Color c =
            s.Contains("line") ? Color.FromRgb(0x06, 0xC7, 0x55) :
            s.Contains("discord") ? Color.FromRgb(0x58, 0x65, 0xF2) :
            s.Contains("instagram") ? Color.FromRgb(0xE1, 0x30, 0x6C) :
            s.Contains("messenger") ? Color.FromRgb(0x00, 0x84, 0xFF) :
            s.Contains("telegram") ? Color.FromRgb(0x29, 0xA9, 0xEB) :
            s.Contains("whatsapp") ? Color.FromRgb(0x25, 0xD3, 0x66) :
            s.Contains("teams") ? Color.FromRgb(0x50, 0x59, 0xC9) :
            s.Contains("outlook") || s.Contains("mail") ? Color.FromRgb(0x0F, 0x6C, 0xBD) :
            HashColor(appName);

        var brush = new SolidColorBrush(c);
        brush.Freeze();

        string badge = appName.Length > 0 ? char.ToUpperInvariant(appName[0]).ToString() : "•";
        return (badge, brush);
    }

    private static Color HashColor(string s)
    {
        Color[] palette =
        {
            Color.FromRgb(0x4C, 0x9A, 0xFF), Color.FromRgb(0xF5, 0x9E, 0x0B), Color.FromRgb(0x8B, 0x5C, 0xF6),
            Color.FromRgb(0x14, 0xB8, 0xA6), Color.FromRgb(0xEC, 0x48, 0x99), Color.FromRgb(0x64, 0x74, 0x8B),
        };
        int h = 0;
        foreach (char ch in s) h = unchecked(h * 31 + ch);
        return palette[(h & 0x7FFFFFFF) % palette.Length];
    }

    /// <summary>Discord server-channel title: "Name (#channel, category)" or "Name (#channel)".</summary>
    private static readonly Regex DiscordTitle = new(@"^(.+?) \((#[^,()]+)(?:, ([^()]+))?\)$");

    /// <summary>Trims and drops Unicode bidi isolates (U+2066–2069) that apps wrap names in.</summary>
    private static string CleanText(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var sb = new System.Text.StringBuilder(text.Length);
        foreach (char c in text)
            if (c is < '\u2066' or > '\u2069') sb.Append(c);
        return sb.ToString().Trim();
    }

    /// <summary>A bare host name, optionally with "https://" in front.</summary>
    private static readonly Regex SiteLike = new(@"^(https?://)?([a-z0-9-]+\.)+[a-z]{2,}/?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static string SiteName(string site)
    {
        string s = Regex.Replace(site, @"^(https?://)?(www\.|web\.|m\.)?", "").TrimEnd('/');
        return s switch
        {
            _ when s.StartsWith("instagram.") => "Instagram",
            _ when s.StartsWith("facebook.") => "Facebook",
            _ when s.StartsWith("messenger.") => "Messenger",
            _ when s.StartsWith("discord.") => "Discord",
            _ when s.StartsWith("whatsapp.") => "WhatsApp",
            _ when s.StartsWith("telegram.") => "Telegram",
            _ when s.StartsWith("threads.") => "Threads",
            _ when s is "x.com" or "twitter.com" => "X",
            _ => s,
        };
    }

    private static string PrettyName(string aumid)
    {
        string n = aumid;
        int bang = n.LastIndexOf('!');
        if (bang >= 0) n = n[(bang + 1)..];
        int slash = Math.Max(n.LastIndexOf('\\'), n.LastIndexOf('/'));
        if (slash >= 0) n = n[(slash + 1)..];
        if (n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) n = n[..^4];
        return n.Length == 0 ? "通知" : n;
    }

    // ---- Actions -------------------------------------------------------------------

    /// <summary>Brings up the app that sent the notification (works for Store and desktop apps).</summary>
    public static void OpenSourceApp(NotificationItem item)
    {
        if (string.IsNullOrEmpty(item.AppId)) return;
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"shell:AppsFolder\\{item.AppId}")
            {
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Islet] Could not open {item.AppId}: {ex.Message}");
        }
    }

    /// <summary>Opens Settings › Privacy › Notifications so the user can grant access.</summary>
    public static void OpenAccessSettings()
    {
        try
        {
            Process.Start(new ProcessStartInfo("ms-settings:privacy-notifications") { UseShellExecute = true });
        }
        catch (Exception) { }
    }

    public void Dispose()
    {
        _poll.Stop();
        _listener = null;
    }
}
