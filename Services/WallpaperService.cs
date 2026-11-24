using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Islet.Services;

/// <summary>
/// The user's home-page picture. The chosen file is copied to %LOCALAPPDATA%\Islet\home-wallpaper,
/// so moving or deleting the original doesn't break it.
/// </summary>
public static class WallpaperService
{
    public static readonly string StorePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Islet", "home-wallpaper");

    /// <summary>File filter for the open dialog.</summary>
    public const string DialogFilter = "圖片|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.webp;*.tif;*.tiff|所有檔案|*.*";

    /// <summary>Decode width cap: the page is ~384 px wide, so this stays sharp at 200 % scale without
    /// keeping a full 4K photo in memory.</summary>
    private const int DecodeWidth = 1200;

    /// <summary>Loads the saved picture, or null when none is set (or it can't be read).</summary>
    public static ImageSource? Load()
    {
        if (!File.Exists(StorePath)) return null;
        try
        {
            return Decode(StorePath);
        }
        catch (Exception ex)
        {
            App.Log($"首頁圖片讀取失敗：{ex.Message}");
            return null;
        }
    }

    /// <summary>Validates <paramref name="sourcePath"/> as an image, copies it into the store and
    /// returns it. Throws when the file isn't a readable image.</summary>
    public static ImageSource Set(string sourcePath)
    {
        var image = Decode(sourcePath); // throws before we overwrite anything
        Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
        File.Copy(sourcePath, StorePath, overwrite: true);
        return image;
    }

    public static void Clear()
    {
        try
        {
            if (File.Exists(StorePath)) File.Delete(StorePath);
        }
        catch (Exception ex)
        {
            App.Log($"首頁圖片移除失敗：{ex.Message}");
        }
    }

    private static ImageSource Decode(string path)
    {
        // Read into memory first so the file isn't kept locked (lets us overwrite it later).
        var bytes = File.ReadAllBytes(path);
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
        bmp.DecodePixelWidth = DecodeWidth;
        bmp.StreamSource = new MemoryStream(bytes);
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }
}
