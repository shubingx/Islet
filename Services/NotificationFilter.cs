namespace Islet.Services;

/// <summary>
/// Decides which notifications Islet shows. Default is "social apps only": everything else
/// (screenshots, Chrome promos, Windows Update, …) is dropped before it reaches the list.
/// Edit the list below to add or remove apps.
/// </summary>
public static class NotificationFilter
{
    /// <summary>Turn off to let every notification through.</summary>
    public static bool SocialOnly { get; set; } = true;

    /// <summary>Desktop / Store apps, matched against the app name or AppUserModelID (case-insensitive).</summary>
    private static readonly string[] SocialApps =
    {
        "line", "discord", "instagram", "messenger", "facebook", "telegram", "whatsapp",
        "slack", "teams", "signal", "wechat", "weixin", "skype", "threads", "twitter",
    };

    private static readonly string[] Browsers =
    {
        "chrome", "msedge", "edge", "firefox", "308046b0af4a39cb", "brave", "opera", "vivaldi", "arc",
    };

    public static bool IsAllowed(string appName, string appId)
    {
        if (!SocialOnly) return true;

        // Browser notifications (Instagram web etc.) aren't handled yet: drop them all,
        // which also keeps out Chrome's own promos ("10 大熱門節目").
        if (IsBrowser(appName, appId)) return false;

        string app = (appName + " " + appId).ToLowerInvariant();
        return SocialApps.Any(k => ContainsWord(app, k, wholeWord: false));
    }

    public static bool IsBrowser(string appName, string appId)
    {
        string app = (appName + " " + appId).ToLowerInvariant();
        return Browsers.Any(b => ContainsWord(app, b));
    }

    /// <summary>
    /// Word-start match: "line" matches "LINE" / "LINE.exe" / "NaverCorp.LINE" but not "Timeline" or "Online".
    /// With wholeWord the end must be a boundary too.
    /// </summary>
    private static bool ContainsWord(string haystack, string word, bool wholeWord = true)
    {
        int i = 0;
        while ((i = haystack.IndexOf(word, i, StringComparison.Ordinal)) >= 0)
        {
            bool startOk = i == 0 || !char.IsLetter(haystack[i - 1]);
            int end = i + word.Length;
            bool endOk = !wholeWord || end >= haystack.Length || !char.IsLetter(haystack[end]);
            if (startOk && endOk) return true;
            i = end;
        }
        return false;
    }
}
