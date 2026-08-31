using System.Runtime.InteropServices;

namespace WindowsAppRestarter.Interop;

internal static class NativeMethods
{
    public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    public const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    public const int DWMWA_BORDER_COLOR = 34;
    public const int DWMWA_SYSTEMBACKDROP_TYPE = 38;

    public const int DWMWCP_ROUND = 2;

    public const int DWMSBT_NONE = 1;
    public const int DWMSBT_TRANSIENTWINDOW = 3;

    public const int WS_EX_TOOLWINDOW = 0x00000080;
    public const int WS_EX_TOPMOST = 0x00000008;
    public const int WS_EX_NOACTIVATE = 0x08000000;
    public const int GWL_EXSTYLE = -20;

    public const int WM_SETTINGCHANGE = 0x001A;
    public const int WM_DWMCOLORIZATIONCOLORCHANGED = 0x0320;


    [StructLayout(LayoutKind.Sequential)]
    public struct MARGINS
    {
        public int Left;
        public int Right;
        public int Top;
        public int Bottom;
    }

    [DllImport("dwmapi.dll", ExactSpelling = true)]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);

    [DllImport("dwmapi.dll", ExactSpelling = true)]
    private static extern int DwmExtendFrameIntoClientArea(nint hwnd, ref MARGINS margins);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetForegroundWindow(nint hwnd);

    [DllImport("user32.dll", ExactSpelling = true)]
    public static extern nint GetForegroundWindow();

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", ExactSpelling = true)]
    private static extern nint GetWindowLongPtr(nint hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", ExactSpelling = true)]
    private static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);

    /// <summary>Adds or removes WS_EX_NOACTIVATE so the window can be shown without ever taking focus.</summary>
    public static void SetNoActivate(nint hwnd, bool noActivate)
    {
        var style = GetWindowLongPtr(hwnd, GWL_EXSTYLE);
        var updated = noActivate ? style | WS_EX_NOACTIVATE : style & ~(nint)WS_EX_NOACTIVATE;
        if (updated != style)
        {
            SetWindowLongPtr(hwnd, GWL_EXSTYLE, updated);
        }
    }

    public static bool HasNoActivate(nint hwnd) => (GetWindowLongPtr(hwnd, GWL_EXSTYLE) & WS_EX_NOACTIVATE) != 0;

    [DllImport("user32.dll", ExactSpelling = true)]
    public static extern short GetAsyncKeyState(int virtualKey);

    public const int VK_LBUTTON = 0x01;
    public const int VK_RBUTTON = 0x02;
    public const int VK_MBUTTON = 0x04;

    public static bool IsAnyMouseButtonDown() =>
        (GetAsyncKeyState(VK_LBUTTON) & 0x8000) != 0
        || (GetAsyncKeyState(VK_RBUTTON) & 0x8000) != 0
        || (GetAsyncKeyState(VK_MBUTTON) & 0x8000) != 0;

    public static bool TrySetWindowAttribute(nint hwnd, int attribute, int value)
    {
        try
        {
            return DwmSetWindowAttribute(hwnd, attribute, ref value, sizeof(int)) == 0;
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }

    public static bool TryExtendFrameIntoClientArea(nint hwnd)
    {
        try
        {
            var margins = new MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };
            return DwmExtendFrameIntoClientArea(hwnd, ref margins) == 0;
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }
}
