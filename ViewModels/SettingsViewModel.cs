using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using Islet.Services;

namespace Islet.ViewModels;

/// <summary>
/// Backs the settings window. Every setter writes straight into <see cref="SettingsService.Current"/>
/// and calls Update, so the islet changes live while the window is open — there is no "save" button.
/// </summary>
public sealed class SettingsViewModel : ViewModelBase
{
    private static AppSettings S => SettingsService.Current;

    public SettingsViewModel(MainViewModel main)
    {
        Main = main;
        Swatches = new ObservableCollection<SwatchItem>(SwatchColors.Select(c => new SwatchItem(c)));
        RebuildPages();
        SyncSwatches();
        _colorText = S.Home.Color;
    }

    /// <summary>The islet's own view model, for the live home-page preview and the picture buttons.</summary>
    public MainViewModel Main { get; }

    private static void Commit() => SettingsService.Update();

    // ---- General -----------------------------------------------------------

    private bool _launchAtStartup = StartupService.IsEnabled;

    public bool LaunchAtStartup
    {
        get => _launchAtStartup;
        set
        {
            if (value == _launchAtStartup) return;
            if (StartupService.Set(value)) _launchAtStartup = value;
            // Re-read afterwards so a refused registry write snaps the switch back.
            Application.Current.Dispatcher.BeginInvoke(new Action(() => OnPropertyChanged(nameof(LaunchAtStartup))));
        }
    }

    public double HoverDelayMs
    {
        get => S.General.HoverDelayMs;
        set
        {
            int ms = (int)Math.Round(value / 50) * 50;
            if (ms == S.General.HoverDelayMs) return;
            S.General.HoverDelayMs = ms;
            Commit();
            OnPropertyChanged();
            OnPropertyChanged(nameof(HoverDelayText));
        }
    }

    public string HoverDelayText => FormatMs(S.General.HoverDelayMs);

    public double CollapseDelayMs
    {
        get => S.General.CollapseDelayMs;
        set
        {
            int ms = (int)Math.Round(value / 50) * 50;
            if (ms == S.General.CollapseDelayMs) return;
            S.General.CollapseDelayMs = ms;
            Commit();
            OnPropertyChanged();
            OnPropertyChanged(nameof(CollapseDelayText));
        }
    }

    public string CollapseDelayText => FormatMs(S.General.CollapseDelayMs);

    public bool ResetPageOnExpand
    {
        get => S.General.ResetPageOnExpand;
        set
        {
            if (value == S.General.ResetPageOnExpand) return;
            S.General.ResetPageOnExpand = value;
            Commit();
            OnPropertyChanged();
        }
    }

    private static string FormatMs(int ms) => ms == 0 ? "立即" : ms < 1000 ? $"{ms} 毫秒" : $"{ms / 1000.0:0.##} 秒";

    // ---- Home background ---------------------------------------------------

    public bool IsBgImage { get => Kind == HomeBackgroundKind.Image; set { if (value) Kind = HomeBackgroundKind.Image; } }
    public bool IsBgColor { get => Kind == HomeBackgroundKind.Color; set { if (value) Kind = HomeBackgroundKind.Color; } }
    public bool IsBgCover { get => Kind == HomeBackgroundKind.MusicCover; set { if (value) Kind = HomeBackgroundKind.MusicCover; } }

    private HomeBackgroundKind Kind
    {
        get => S.Home.Background;
        set
        {
            if (value == S.Home.Background) return;
            S.Home.Background = value;
            Commit();
            OnPropertyChanged(nameof(IsBgImage));
            OnPropertyChanged(nameof(IsBgColor));
            OnPropertyChanged(nameof(IsBgCover));
        }
    }

    public bool IsFitFill { get => Fit == HomeImageFit.Fill; set { if (value) Fit = HomeImageFit.Fill; } }
    public bool IsFitFit { get => Fit == HomeImageFit.Fit; set { if (value) Fit = HomeImageFit.Fit; } }
    public bool IsFitStretch { get => Fit == HomeImageFit.Stretch; set { if (value) Fit = HomeImageFit.Stretch; } }

    private HomeImageFit Fit
    {
        get => S.Home.Fit;
        set
        {
            if (value == S.Home.Fit) return;
            S.Home.Fit = value;
            Commit();
            OnPropertyChanged(nameof(IsFitFill));
            OnPropertyChanged(nameof(IsFitFit));
            OnPropertyChanged(nameof(IsFitStretch));
        }
    }

    public double Dim
    {
        get => S.Home.Dim;
        set
        {
            int v = (int)Math.Round(value);
            if (v == S.Home.Dim) return;
            S.Home.Dim = v;
            Commit();
            OnPropertyChanged();
            OnPropertyChanged(nameof(DimText));
        }
    }

    public string DimText => S.Home.Dim == 0 ? "無" : $"{S.Home.Dim}%";

    public double Blur
    {
        get => S.Home.Blur;
        set
        {
            int v = (int)Math.Round(value);
            if (v == S.Home.Blur) return;
            S.Home.Blur = v;
            Commit();
            OnPropertyChanged();
            OnPropertyChanged(nameof(BlurText));
        }
    }

    public string BlurText => S.Home.Blur == 0 ? "無" : S.Home.Blur.ToString();

    // ---- Solid colour ------------------------------------------------------

    private static readonly string[] SwatchColors =
    {
        "#FF0A0A0A", "#FF1F3A5F", "#FF2D4A3E", "#FF4A2D5F",
        "#FF5F2D3A", "#FF6B4A1F", "#FF2F4F6F", "#FF3A3A3A",
    };

    public ObservableCollection<SwatchItem> Swatches { get; }

    private string _colorText;
    private bool _colorTextValid = true;

    /// <summary>Hex box under the swatches; only valid colours are applied.</summary>
    public string ColorText
    {
        get => _colorText;
        set
        {
            if (!Set(ref _colorText, value)) return;
            var parsed = TryParseColor(value);
            ColorTextValid = parsed is not null;
            if (parsed is { } c) ApplyColor(c.ToString());
        }
    }

    public bool ColorTextValid
    {
        get => _colorTextValid;
        private set
        {
            if (Set(ref _colorTextValid, value)) OnPropertyChanged(nameof(ColorTextInvalid));
        }
    }

    public bool ColorTextInvalid => !_colorTextValid;

    public void PickSwatch(SwatchItem swatch)
    {
        ApplyColor(swatch.Hex);
        _colorText = swatch.Hex;
        ColorTextValid = true;
        OnPropertyChanged(nameof(ColorText));
    }

    private void ApplyColor(string hex)
    {
        if (string.Equals(hex, S.Home.Color, StringComparison.OrdinalIgnoreCase)) return;
        S.Home.Color = hex;
        Commit();
        SyncSwatches();
    }

    private void SyncSwatches()
    {
        foreach (var s in Swatches) s.IsSelected = string.Equals(s.Hex, S.Home.Color, StringComparison.OrdinalIgnoreCase);
    }

    public static Color? TryParseColor(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        text = text.Trim();
        if (!text.StartsWith('#')) text = "#" + text;
        if (text.Length is not (7 or 9)) return null;   // #RRGGBB or #AARRGGBB
        try
        {
            return (Color)ColorConverter.ConvertFromString(text);
        }
        catch
        {
            return null;
        }
    }

    // ---- Pages -------------------------------------------------------------

    public ObservableCollection<PageItem> Pages { get; } = new();

    private void RebuildPages()
    {
        Pages.Clear();
        var list = S.Pages;
        for (int i = 0; i < list.Count; i++)
        {
            var info = PageCatalog.Find(list[i].Id)!;
            Pages.Add(new PageItem(this, list[i], info, canUp: i > 0, canDown: i < list.Count - 1));
        }
    }

    public void Move(PageItem item, int delta)
    {
        var list = S.Pages;
        int from = list.IndexOf(item.Setting);
        int to = from + delta;
        if (from < 0 || to < 0 || to >= list.Count) return;
        list.RemoveAt(from);
        list.Insert(to, item.Setting);
        Commit();
        RebuildPages();
    }

    public void ResetPages()
    {
        SettingsService.ResetPages();
        RebuildPages();
    }

    /// <summary>Called by a page row when its switch flips. The last shown page can't be turned off.</summary>
    internal bool TrySetEnabled(PageItem item, bool enabled)
    {
        if (!enabled && S.Pages.Count(p => p.Enabled) <= 1) return false;
        item.Setting.Enabled = enabled;
        Commit();
        return true;
    }
}

/// <summary>One row on the pages list.</summary>
public sealed class PageItem : ViewModelBase
{
    private readonly SettingsViewModel _owner;

    public PageItem(SettingsViewModel owner, PageSetting setting, PageCatalog.Info info, bool canUp, bool canDown)
    {
        _owner = owner;
        Setting = setting;
        Name = info.Name;
        Glyph = info.Glyph;
        CanMoveUp = canUp;
        CanMoveDown = canDown;
    }

    public PageSetting Setting { get; }
    public string Name { get; }
    public string Glyph { get; }
    public bool CanMoveUp { get; }
    public bool CanMoveDown { get; }

    public bool Enabled
    {
        get => Setting.Enabled;
        set
        {
            if (value == Setting.Enabled) return;
            if (!_owner.TrySetEnabled(this, value))
            {
                // Refused (it's the last page): flip the switch back once WPF has finished this update.
                Application.Current.Dispatcher.BeginInvoke(new Action(() => OnPropertyChanged(nameof(Enabled))));
                return;
            }
            OnPropertyChanged();
        }
    }
}

/// <summary>A colour button under "純色".</summary>
public sealed class SwatchItem : ViewModelBase
{
    private bool _isSelected;

    public SwatchItem(string hex)
    {
        Hex = hex;
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        Brush = brush;
    }

    public string Hex { get; }
    public Brush Brush { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set => Set(ref _isSelected, value);
    }
}
