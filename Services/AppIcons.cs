using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Islet.Services;

/// <summary>Where notification icons come from, other than AppDisplayInfo.</summary>
internal static class AppIcons
{
    private static readonly Dictionary<string, ImageSource?> BundledCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// File names for apps whose display name has extra words, e.g. "Telegram Desktop" → telegram.png,
    /// "LINE Corporation" → line.png. Matched as a prefix of the normalised name. ("x" is exact-only.)
    /// </summary>
    private static readonly string[] Brands =
    {
        "instagram", "facebook", "messenger", "discord", "line", "telegram", "whatsapp", "twitter",
    };

    /// <summary>
    /// An icon shipped with Islet: Assets\AppIcons\&lt;app&gt;.png next to Islet.exe, where &lt;app&gt;
    /// is the app name in lower case without spaces ("Instagram" → instagram.png). Null if none.
    /// </summary>
    public static ImageSource? Bundled(string appName)
    {
        string key = new string(appName.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        if (key.Length == 0) return null;
        if (BundledCache.TryGetValue(key, out var cached)) return cached;

        ImageSource? result = null;
        string dir = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcons");
        string path = Path.Combine(dir, key + ".png");
        if (!File.Exists(path) && Brands.FirstOrDefault(b => key.StartsWith(b)) is { } brand)
            path = Path.Combine(dir, (brand == "twitter" ? "x" : brand) + ".png");
        if (File.Exists(path))
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;   // don't keep the file locked
                bmp.DecodePixelWidth = 56;
                bmp.UriSource = new Uri(path);
                bmp.EndInit();
                bmp.Freeze();
                result = bmp;
            }
            catch (Exception)
            {
                result = null; // unreadable file → fall through to the other sources
            }
        }

        BundledCache[key] = result;
        return result;
    }

    /// <summary>
    /// The icon Explorer / the Start menu shows for an AppUserModelID ("shell:AppsFolder\&lt;AUMID&gt;").
    /// Works for classic desktop apps (Discord is "com.squirrel.Discord.Discord") and Store apps,
    /// as long as the app has a Start-menu entry.
    /// </summary>
    public static ImageSource? FromShell(string aumid, int size)
    {
        if (string.IsNullOrWhiteSpace(aumid)) return null;

        IntPtr hbmp = IntPtr.Zero;
        object? obj = null;
        try
        {
            var iid = typeof(IShellItemImageFactory).GUID;
            SHCreateItemFromParsingName(@"shell:AppsFolder\" + aumid, IntPtr.Zero, ref iid, out obj);
            int hr = ((IShellItemImageFactory)obj).GetImage(new SIZE { cx = size, cy = size },
                SIIGBF.ICONONLY | SIIGBF.BIGGERSIZEOK, out hbmp);
            return hr == 0 && hbmp != IntPtr.Zero ? ToBitmapSource(hbmp) : null;
        }
        catch (Exception)
        {
            return null; // no Start-menu entry for this AUMID, etc.
        }
        finally
        {
            if (hbmp != IntPtr.Zero) DeleteObject(hbmp);
            if (obj is not null) Marshal.ReleaseComObject(obj);
        }
    }

    /// <summary>
    /// Copies the HBITMAP's pixels keeping alpha (Imaging.CreateBitmapSourceFromHBitmap drops it
    /// and puts a black square behind the icon).
    /// </summary>
    private static BitmapSource? ToBitmapSource(IntPtr hbmp)
    {
        if (GetObject(hbmp, Marshal.SizeOf<BITMAP>(), out BITMAP bm) == 0) return null;
        int w = bm.bmWidth, h = Math.Abs(bm.bmHeight);
        if (w <= 0 || h <= 0) return null;

        var info = new BITMAPINFOHEADER
        {
            biSize = Marshal.SizeOf<BITMAPINFOHEADER>(),
            biWidth = w,
            biHeight = -h,      // negative = top-down rows
            biPlanes = 1,
            biBitCount = 32,
        };
        var pixels = new byte[w * h * 4];
        IntPtr dc = GetDC(IntPtr.Zero);
        try
        {
            if (GetDIBits(dc, hbmp, 0, (uint)h, pixels, ref info, 0) == 0) return null;
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, dc);
        }

        var src = BitmapSource.Create(w, h, 96, 96, PixelFormats.Pbgra32, null, pixels, w * 4);
        src.Freeze();
        return src;
    }

    // ---- Interop -------------------------------------------------------------------

    [Flags]
    private enum SIIGBF
    {
        BIGGERSIZEOK = 0x1,
        ICONONLY = 0x4,
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE { public int cx, cy; }

    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig] int GetImage(SIZE size, SIIGBF flags, out IntPtr phbm);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAP
    {
        public int bmType, bmWidth, bmHeight, bmWidthBytes;
        public ushort bmPlanes, bmBitsPixel;
        public IntPtr bmBits;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public int biSize, biWidth, biHeight;
        public ushort biPlanes, biBitCount;
        public uint biCompression, biSizeImage;
        public int biXPelsPerMeter, biYPelsPerMeter;
        public uint biClrUsed, biClrImportant;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(string pszPath, IntPtr pbc, ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out object ppv);

    [DllImport("gdi32.dll")] private static extern int GetObject(IntPtr h, int c, out BITMAP bm);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr h);
    [DllImport("gdi32.dll")]
    private static extern int GetDIBits(IntPtr hdc, IntPtr hbm, uint start, uint lines, byte[] bits,
        ref BITMAPINFOHEADER bmi, uint usage);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
}
