using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Threading;

namespace Islet.Services;

/// <summary>What the home page shows behind the header.</summary>
public enum HomeBackgroundKind
{
    Image,       // the user's own picture
    Color,       // a solid colour
    MusicCover,  // cover art of what's playing (falls back to a hint when nothing plays)
}

/// <summary>How the home picture fills the islet.</summary>
public enum HomeImageFit
{
    Fill,     // cover the islet, crop the overflow
    Fit,      // whole picture visible, letterboxed
    Stretch,  // stretch to the islet's shape
}

public sealed class GeneralSettings
{
    /// <summary>How long the cursor rests on the collapsed islet before it expands.</summary>
    public int HoverDelayMs { get; set; } = 400;

    /// <summary>How long after the cursor leaves before the expanded islet collapses.</summary>
    public int CollapseDelayMs { get; set; } = 150;

    /// <summary>Each time the islet expands, start on the first page instead of the last one shown.</summary>
    public bool ResetPageOnExpand { get; set; }
}

public sealed class HomeSettings
{
    public HomeBackgroundKind Background { get; set; } = HomeBackgroundKind.Image;
    public HomeImageFit Fit { get; set; } = HomeImageFit.Fill;

    /// <summary>#AARRGGBB or #RRGGBB, used when <see cref="Background"/> is Color.</summary>
    public string Color { get; set; } = "#FF1F3A5F";

    /// <summary>Darkening over the picture, 0–80 (%).</summary>
    public int Dim { get; set; }

    /// <summary>Blur radius, 0–30.</summary>
    public int Blur { get; set; }
}

public sealed class PageSetting
{
    public string Id { get; set; } = "";
    public bool Enabled { get; set; } = true;
}

public sealed class AppSettings
{
    public GeneralSettings General { get; set; } = new();
    public HomeSettings Home { get; set; } = new();

    /// <summary>Pages in the order they appear; disabled ones are left out of the islet.</summary>
    public List<PageSetting> Pages { get; set; } = new();
}

/// <summary>The islet's pages, in their default order.</summary>
public static class PageCatalog
{
    public sealed record Info(string Id, string Name, string Glyph);

    public static readonly IReadOnlyList<Info> All = new Info[]
    {
        new("home", "首頁", ""),
        new("music", "音樂", ""),
        new("notif", "通知", ""),
        new("todo", "待辦", ""),
        new("timer", "計時器", ""),
        new("perf", "效能", ""),
    };

    public static Info? Find(string id) => All.FirstOrDefault(p => p.Id == id);
}

/// <summary>
/// Loads and saves %LOCALAPPDATA%\Islet\settings.json. Change <see cref="Current"/>, then call
/// <see cref="Update"/>: listeners hear about it right away and the file is written shortly after
/// (so dragging a slider doesn't write the file dozens of times).
/// </summary>
public static class SettingsService
{
    public static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Islet", "settings.json");

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private static DispatcherTimer? _saveTimer;
    private static bool _dirty;

    public static AppSettings Current { get; } = Load();

    /// <summary>Raised on the UI thread after any setting changes.</summary>
    public static event Action? Changed;

    public static void Update()
    {
        Normalize(Current);
        Changed?.Invoke();

        _dirty = true;
        if (_saveTimer is null)
        {
            _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            _saveTimer.Tick += (_, _) =>
            {
                _saveTimer!.Stop();
                Flush();
            };
        }
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    /// <summary>Writes pending changes now (called on exit).</summary>
    public static void Flush()
    {
        if (!_dirty) return;
        _dirty = false;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            string tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(Current, Json));
            File.Move(tmp, FilePath, overwrite: true);   // never leave a half-written file behind
        }
        catch (Exception ex)
        {
            App.Log($"設定儲存失敗：{ex.Message}");
        }
    }

    /// <summary>Puts the pages back in their default order, all shown.</summary>
    public static void ResetPages()
    {
        Current.Pages = PageCatalog.All.Select(p => new PageSetting { Id = p.Id, Enabled = true }).ToList();
        Update();
    }

    private static AppSettings Load()
    {
        AppSettings settings;
        try
        {
            settings = File.Exists(FilePath)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Json) ?? new AppSettings()
                : new AppSettings();
        }
        catch (Exception ex)
        {
            // A broken file shouldn't stop the islet; start from defaults (the file is rewritten on the next change).
            App.Log($"設定讀取失敗，改用預設值：{ex.Message}");
            settings = new AppSettings();
        }
        Normalize(settings);
        return settings;
    }

    /// <summary>Fills gaps and clamps values, so the rest of the app can trust what it reads.</summary>
    private static void Normalize(AppSettings s)
    {
        s.General ??= new GeneralSettings();
        s.Home ??= new HomeSettings();
        s.Pages ??= new List<PageSetting>();

        s.General.HoverDelayMs = Math.Clamp(s.General.HoverDelayMs, 0, 2000);
        s.General.CollapseDelayMs = Math.Clamp(s.General.CollapseDelayMs, 0, 3000);
        s.Home.Dim = Math.Clamp(s.Home.Dim, 0, 80);
        s.Home.Blur = Math.Clamp(s.Home.Blur, 0, 30);
        if (string.IsNullOrWhiteSpace(s.Home.Color)) s.Home.Color = new HomeSettings().Color;

        // Known pages only, each once, in the saved order; pages added in a newer version go at the end.
        var pages = new List<PageSetting>();
        foreach (var p in s.Pages)
        {
            if (p?.Id is null || PageCatalog.Find(p.Id) is null || pages.Any(x => x.Id == p.Id)) continue;
            pages.Add(p);
        }
        foreach (var info in PageCatalog.All)
            if (pages.All(x => x.Id != info.Id)) pages.Add(new PageSetting { Id = info.Id, Enabled = true });
        if (!pages.Any(p => p.Enabled)) pages[0].Enabled = true;
        s.Pages = pages;
    }
}
