using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Islet.Services;
using Islet.ViewModels;

namespace Islet.Shell;

/// <summary>
/// The full settings window (gear on the islet, or 設定… in the right-click menu).
/// Everything applies immediately; closing the window is all the "save" there is.
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _vm;

    public SettingsWindow(MainViewModel main)
    {
        InitializeComponent();
        _vm = new SettingsViewModel(main);
        DataContext = _vm;

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = version is null ? "" : $"版本 {version.Major}.{version.Minor}.{version.Build}";
        DataFolderText.Text = Path.GetDirectoryName(SettingsService.FilePath);

        ShowSection("general");
    }

    /// <summary>Jumps to a section: "general", "home", "pages" or "about".</summary>
    public void ShowSection(string tag)
    {
        // Checked fires while the XAML is still loading, before the sections exist.
        if (SectionGeneral is null || SectionHome is null || SectionPages is null || SectionAbout is null) return;

        SectionGeneral.Visibility = tag == "general" ? Visibility.Visible : Visibility.Collapsed;
        SectionHome.Visibility = tag == "home" ? Visibility.Visible : Visibility.Collapsed;
        SectionPages.Visibility = tag == "pages" ? Visibility.Visible : Visibility.Collapsed;
        SectionAbout.Visibility = tag == "about" ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string tag }) ShowSection(tag);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        UseDarkTitleBar(new WindowInteropHelper(this).Handle);
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        SettingsService.Flush();
    }

    // ---- Home background ---------------------------------------------------

    private void PickPicture_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "選擇首頁圖片",
            Filter = WallpaperService.DialogFilter,
            Multiselect = false,
        };
        if (dialog.ShowDialog(this) != true) return;

        if (!_vm.Main.SetHomeWallpaper(dialog.FileName))
            MessageBox.Show(this, "這個檔案無法當作圖片開啟，請換一張試試。", "Islet",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void RemovePicture_Click(object sender, RoutedEventArgs e) => _vm.Main.ClearHomeWallpaper();

    private void Swatch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: SwatchItem swatch }) _vm.PickSwatch(swatch);
    }

    // ---- Pages -------------------------------------------------------------

    private void MoveUp_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: PageItem item }) _vm.Move(item, -1);
    }

    private void MoveDown_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: PageItem item }) _vm.Move(item, +1);
    }

    private void ResetPages_Click(object sender, RoutedEventArgs e) => _vm.ResetPages();

    // ---- About -------------------------------------------------------------

    private void OpenDataFolder_Click(object sender, RoutedEventArgs e)
    {
        string folder = Path.GetDirectoryName(SettingsService.FilePath)!;
        try
        {
            Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            App.Log($"無法開啟資料夾：{ex.Message}");
        }
    }

    private void OpenLog_Click(object sender, RoutedEventArgs e)
    {
        if (!File.Exists(App.LogPath))
        {
            MessageBox.Show(this, "目前沒有任何錯誤記錄。", "Islet", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo(App.LogPath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            App.Log($"無法開啟記錄檔：{ex.Message}");
        }
    }

    // ---- Dark title bar ----------------------------------------------------

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    private static void UseDarkTitleBar(IntPtr hwnd)
    {
        int on = 1;
        // 20 = DWMWA_USE_IMMERSIVE_DARK_MODE (Windows 10 20H1+); older builds used 19.
        if (DwmSetWindowAttribute(hwnd, 20, ref on, sizeof(int)) != 0)
            DwmSetWindowAttribute(hwnd, 19, ref on, sizeof(int));
    }
}
