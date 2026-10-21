using System;
using System.Runtime.InteropServices;
using System.Text;

namespace TableManager
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;

        public int Width { get { return Right - Left; } }
        public int Height { get { return Bottom - Top; } }

        public override string ToString()
        {
            return string.Format("{0},{1} {2}x{3}", Left, Top, Width, Height);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X, Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct WINDOWPLACEMENT
    {
        public int length;
        public int flags;
        public int showCmd;
        public POINT ptMinPosition;
        public POINT ptMaxPosition;
        public RECT rcNormalPosition;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    /// <summary>Вызовы Win32. Окна клиентов только перечисляются, перемещаются
    /// и разворачиваются; ввод в клиенты не отправляется.</summary>
    public static class Native
    {
        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
        public delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, ref RECT rect, IntPtr data);

        [DllImport("user32.dll")]
        public static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int max);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetClassName(IntPtr hWnd, StringBuilder text, int max);

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

        [DllImport("dwmapi.dll")]
        public static extern int DwmGetWindowAttribute(IntPtr hWnd, int attribute, out RECT rect, int size);

        [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")]
        public static extern int DwmGetWindowAttributeInt(IntPtr hWnd, int attribute, out int value, int size);

        [DllImport("user32.dll")]
        public static extern bool GetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT placement);

        [DllImport("user32.dll")]
        public static extern bool GetCursorPos(out POINT point);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

        /// <summary>Без ожидания: зависший клиент не задержит расстановщик.</summary>
        [DllImport("user32.dll")]
        public static extern bool ShowWindowAsync(IntPtr hWnd, int command);

        /// <summary>Развернуть свёрнутое окно в прежний размер, не делая его активным.</summary>
        public const int SW_SHOWNOACTIVATE = 4;

        [DllImport("user32.dll")]
        public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc callback, IntPtr data);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX info);

        [DllImport("user32.dll")]
        public static extern IntPtr SetProcessDpiAwarenessContext(IntPtr value);

        [DllImport("user32.dll")]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint vk);

        [DllImport("user32.dll")]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll")]
        public static extern short GetAsyncKeyState(int vk);

        [DllImport("user32.dll")]
        public static extern int GetWindowLong(IntPtr hWnd, int index);

        [DllImport("user32.dll")]
        public static extern int SetWindowLong(IntPtr hWnd, int index, int value);

        public const uint SWP_NOZORDER = 0x0004;
        public const uint SWP_NOACTIVATE = 0x0010;
        public const uint SWP_SHOWWINDOW = 0x0040;
        public const uint SWP_NOOWNERZORDER = 0x0200;
        public const uint SWP_ASYNCWINDOWPOS = 0x4000;
        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

        public const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
        public const int DWMWA_CLOAKED = 14;
        public static readonly IntPtr DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = new IntPtr(-4);

        public const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, MOD_WIN = 0x8, MOD_NOREPEAT = 0x4000;
        public const int WM_HOTKEY = 0x0312;
        public const int VK_LBUTTON = 0x01;
        public const uint MONITORINFOF_PRIMARY = 0x1;

        public const int GWL_EXSTYLE = -20;
        public const int WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_LAYERED = 0x80000, WS_EX_NOACTIVATE = 0x8000000;

        /// <summary>Видимая рамка окна (без невидимых краёв Windows 10/11).</summary>
        public static RECT FrameOf(IntPtr hWnd)
        {
            RECT frame;
            if (DwmGetWindowAttribute(hWnd, DWMWA_EXTENDED_FRAME_BOUNDS, out frame, Marshal.SizeOf(typeof(RECT))) == 0
                && frame.Width > 0 && frame.Height > 0)
            {
                return frame;
            }
            GetWindowRect(hWnd, out frame);
            return frame;
        }

        /// <summary>Окно скрыто системой (другой рабочий стол, свёрнутое UWP).</summary>
        public static bool IsCloaked(IntPtr hWnd)
        {
            int cloaked;
            return DwmGetWindowAttributeInt(hWnd, DWMWA_CLOAKED, out cloaked, sizeof(int)) == 0 && cloaked != 0;
        }

        /// <summary>Размер окна в обычном состоянии — и у свёрнутого.</summary>
        public static RECT NormalRect(IntPtr hWnd)
        {
            var placement = new WINDOWPLACEMENT();
            placement.length = Marshal.SizeOf(typeof(WINDOWPLACEMENT));
            if (IsIconic(hWnd) && GetWindowPlacement(hWnd, ref placement)) return placement.rcNormalPosition;
            return FrameOf(hWnd);
        }

        public static bool LeftButtonDown()
        {
            return (GetAsyncKeyState(VK_LBUTTON) & 0x8000) != 0;
        }
    }
}
