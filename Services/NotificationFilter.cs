namespace Islet.Services;

/// <summary>
/// Decides which notifications Islet shows. Default is "social apps only": everything else
/// (screenshots, Chrome promos, Windows Update, …) is dropped before it reaches the list.
/// Edit the two lists below to add or remove apps.
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

    /// <summary>
    /// Browser notifications are let through only when they come from these sites
    /// (subdomains included, e.g. www.instagram.com). Everything else a browser sends is dropped.
    /// </summary>
    private static readonly string[] SocialSites =
    {
        "instagram.com", "facebook.com", "x.com", "twitter.com", "telegram.org",
    };

    private static readonly string[] Browsers =
    {
        "chrome", "msedge", "edge", "firefox", "308046b0af4a39cb", "brave", "opera", "vivaldi", "arc",
    };

    /// <summary>
    /// Brand names looked for in a browser notification's text when the sending site isn't
    /// available (Windows doesn't always hand over Chrome's "www.instagram.com" line).
    /// "LINE" and "X" are left out: as plain words they'd let far too much through.
    /// </summary>
    private static readonly (string Word, string Name)[] SocialBrands =
    {
        ("instagram", "Instagram"), ("facebook", "Facebook"),
    };

    /// <summary>The social platform named in a notification's text (e.g. title "Instagram"), or null.</summary>
    public static string? SocialBrandIn(string text)
    {
        string t = text.ToLowerInvariant();
        foreach (var (word, name) in SocialBrands)
            if (ContainsWord(t, word)) return name;
        return null;
    }

    public static bool IsAllowed(string appName, string appId, string site, string text = "")
    {
        if (!SocialOnly) return true;

        string app = (appName + " " + appId).ToLowerInvariant();

        bool socialApp = SocialApps.Any(k => ContainsWord(app, k, wholeWord: false));

        // A browser's own notifications (trending shows, "Chrome is up to date", …) have no
        // social site attached, so they are dropped; web notifications from IG etc. pass.
        if (IsBrowser(appName, appId))
        {
            // Installed web apps (PWA) carry their own name, e.g. "Instagram".
            if (SocialApps.Any(k => ContainsWord(appName.ToLowerInvariant(), k, wholeWord: false))) return true;

            string s = site.ToLowerInvariant();
            if (s.StartsWith("https://")) s = s[8..];
            else if (s.StartsWith("http://")) s = s[7..];
            s = s.TrimEnd('/');
            if (s.Length > 0) return IsSocialSite(s);

            // No site line: look for the URL anywhere in the text, then for the bare name.
            return SocialSiteIn(text) is not null || SocialBrandIn(text) is not null;
        }

        return socialApp;
    }

    private static bool IsSocialSite(string host) =>
        SocialSites.Any(d => host == d || host.EndsWith("." + d));

    private static readonly System.Text.RegularExpressions.Regex HostInText =
        new(@"(?:https?://)?((?:[a-z0-9-]+\.)+[a-z]{2,})", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    /// <summary>The IG / FB host mentioned anywhere in the text ("https://www.instagram.com/…"), or null.</summary>
    public static string? SocialSiteIn(string text)
    {
        foreach (System.Text.RegularExpressions.Match m in HostInText.Matches(text))
        {
            string host = m.Groups[1].Value.ToLowerInvariant();
            if (IsSocialSite(host)) return host;
        }
        return null;
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
