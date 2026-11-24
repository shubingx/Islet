using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace Islet;

public partial class App : Application
{
    /// <summary>%LOCALAPPDATA%\Islet\islet.log — crashes and other errors worth looking at.</summary>
    public static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Islet", "islet.log");

    private static readonly object LogLock = new();

    public static void Log(string message)
    {
        try
        {
            lock (LogLock)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                File.AppendAllText(LogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Logging must never take the app down.
        }
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        // Exceptions on the UI thread: log, show, and keep running when possible.
        DispatcherUnhandledException += (_, args) =>
        {
            Report("UI 執行緒", args.Exception);
            args.Handled = true;
        };

        // Exceptions on background threads can't be recovered; at least leave a record.
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Report("背景執行緒", args.ExceptionObject as Exception);

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log($"未處理的 Task 例外：{args.Exception}");
            args.SetObserved();
        };

        base.OnStartup(e);
    }

    private static int _dialogShown;
    private static string _lastLogged = "";

    private static void Report(string where, Exception? ex)
    {
        string text = ex?.ToString() ?? "(未知錯誤)";
        // A broken binding can throw on every render; log each distinct error once.
        if (text != _lastLogged)
        {
            _lastLogged = text;
            Log($"未處理的例外（{where}）：{text}");
            Console.Error.WriteLine($"[Islet] 未處理的例外（{where}）：{text}");
        }

        // Only one error dialog per run, so a repeating error can't bury the screen.
        if (Interlocked.Exchange(ref _dialogShown, 1) == 1) return;
        try
        {
            MessageBox.Show($"{ex?.GetType().Name}: {ex?.Message}\n\n詳細記錄：{LogPath}",
                "Islet 發生錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch
        {
        }
    }
}
