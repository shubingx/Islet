using System.Runtime.InteropServices;

namespace Islet.Shell;

internal static class NativeMethods
{
    public const int GWL_EXSTYLE = -20;

    // Keeps the window out of Alt+Tab and the taskbar.
    public const int WS_EX_TOOLWINDOW = 0x00000080;

    // Mouse clicks fall through to whatever window is underneath.
    public const int WS_EX_TRANSPARENT = 0x00000020;

    public const int VK_LBUTTON = 0x01;
    public const int VK_RBUTTON = 0x02;

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X; public int Y; }

    [DllImport("user32.dll")]
    public static extern bool GetCursorPos(out POINT point);

    [DllImport("user32.dll")]
    public static extern short GetAsyncKeyState(int vKey);

    public static bool IsKeyDown(int vKey) => (GetAsyncKeyState(vKey) & 0x8000) != 0;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
}
