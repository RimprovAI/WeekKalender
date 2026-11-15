using System;
using System.Runtime.InteropServices;
using WeekKalender.Core;

namespace WeekKalender.App
{
    internal static class NativeMethods
    {
        public const int WM_NULL = 0x0000;
        public const int WM_SETTINGCHANGE = 0x001A;
        public const int WM_TIMECHANGE = 0x001E;
        public const int WM_CONTEXTMENU = 0x007B;
        public const int WM_DISPLAYCHANGE = 0x007E;
        public const int WM_POWERBROADCAST = 0x0218;
        public const int WM_DPICHANGED = 0x02E0;
        public const int WM_APP = 0x8000;

        public const int NIN_SELECT = 0x0400;
        public const int NIN_KEYSELECT = 0x0401;
        public const int NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2, NIM_SETVERSION = 4;
        public const int NIF_MESSAGE = 0x01, NIF_ICON = 0x02, NIF_TIP = 0x04, NIF_SHOWTIP = 0x80;
        public const int NOTIFYICON_VERSION_4 = 4;

        public const int PBT_APMRESUMESUSPEND = 0x07;
        public const int PBT_APMRESUMEAUTOMATIC = 0x12;

        public const int MF_STRING = 0x0000, MF_CHECKED = 0x0008, MF_POPUP = 0x0010, MF_SEPARATOR = 0x0800;
        public const int TPM_LEFTALIGN = 0x0000, TPM_RIGHTALIGN = 0x0008, TPM_BOTTOMALIGN = 0x0020, TPM_TOPALIGN = 0x0000;
        public const int TPM_RIGHTBUTTON = 0x0002, TPM_NONOTIFY = 0x0080, TPM_RETURNCMD = 0x0100, TPM_WORKAREA = 0x10000;

        public const int MONITOR_DEFAULTTOPRIMARY = 1, MONITOR_DEFAULTTONEAREST = 2;
        public const int SM_CXSMICON = 49;
        public const int ASFW_ANY = -1;
        public const int ABM_GETTASKBARPOS = 5;

        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        public const int SWP_NOACTIVATE = 0x0010;

        public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        public const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        public const int DWMWCP_ROUND = 2;

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X, Y;

            public POINT(int x, int y)
            {
                X = x;
                Y = y;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left, Top, Right, Bottom;

            public RectI ToRectI()
            {
                return new RectI(Left, Top, Right, Bottom);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public int dwFlags;

            public static MONITORINFO Create()
            {
                return new MONITORINFO { cbSize = Marshal.SizeOf(typeof(MONITORINFO)) };
            }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct NOTIFYICONDATA
        {
            public int cbSize;
            public IntPtr hWnd;
            public int uID;
            public int uFlags;
            public int uCallbackMessage;
            public IntPtr hIcon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
            public int dwState;
            public int dwStateMask;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
            public int uTimeoutOrVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
            public int dwInfoFlags;
            public Guid guidItem;
            public IntPtr hBalloonIcon;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct NOTIFYICONIDENTIFIER
        {
            public int cbSize;
            public IntPtr hWnd;
            public int uID;
            public Guid guidItem;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct APPBARDATA
        {
            public int cbSize;
            public IntPtr hWnd;
            public int uCallbackMessage;
            public int uEdge;
            public RECT rc;
            public IntPtr lParam;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        public static extern bool Shell_NotifyIcon(int message, ref NOTIFYICONDATA data);

        [DllImport("shell32.dll")]
        public static extern int Shell_NotifyIconGetRect(ref NOTIFYICONIDENTIFIER identifier, out RECT location);

        [DllImport("shell32.dll")]
        public static extern IntPtr SHAppBarMessage(int message, ref APPBARDATA data);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int RegisterWindowMessage(string name);

        [DllImport("user32.dll")]
        public static extern bool DestroyIcon(IntPtr icon);

        [DllImport("user32.dll")]
        public static extern IntPtr CreatePopupMenu();

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern bool AppendMenu(IntPtr menu, int flags, IntPtr id, string text);

        [DllImport("user32.dll")]
        public static extern int TrackPopupMenuEx(IntPtr menu, int flags, int x, int y, IntPtr hwnd, IntPtr parameters);

        [DllImport("user32.dll")]
        public static extern bool DestroyMenu(IntPtr menu);

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hwnd);

        [DllImport("user32.dll")]
        public static extern bool AllowSetForegroundWindow(int processId);

        [DllImport("user32.dll")]
        public static extern bool PostMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern bool GetCursorPos(out POINT point);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr FindWindow(string className, string windowName);

        [DllImport("user32.dll")]
        public static extern IntPtr MonitorFromPoint(POINT point, int flags);

        [DllImport("user32.dll")]
        public static extern IntPtr MonitorFromWindow(IntPtr hwnd, int flags);

        [DllImport("user32.dll")]
        public static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);

        [DllImport("user32.dll")]
        public static extern int GetSystemMetricsForDpi(int index, uint dpi);

        [DllImport("shcore.dll")]
        public static extern int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);

        [DllImport("user32.dll")]
        public static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, int flags);

        [DllImport("kernel32.dll")]
        static extern IntPtr GetCurrentProcess();

        [DllImport("kernel32.dll")]
        static extern bool SetProcessWorkingSetSize(IntPtr process, IntPtr minimum, IntPtr maximum);

        /// <summary>Geeft ongebruikt werkgeheugen terug aan Windows; pagina's komen bij gebruik direct terug.</summary>
        public static void TrimWorkingSet()
        {
            try { SetProcessWorkingSetSize(GetCurrentProcess(), new IntPtr(-1), new IntPtr(-1)); }
            catch (Exception) { }
        }

        public const int GWL_STYLE = -16;
        public const int WS_SYSMENU = 0x00080000;

        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
        public static extern int GetWindowLong(IntPtr hwnd, int index);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
        public static extern int SetWindowLong(IntPtr hwnd, int index, int value);

        [DllImport("dwmapi.dll")]
        public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool SetDllDirectory(string path);

        /// <summary>
        /// Haalt de werkmap uit het zoekpad voor DLL's, zodat een bestand dat toevallig (of kwaadwillig) in de map staat
        /// van waaruit het programma wordt gestart nooit als systeembibliotheek wordt geladen.
        /// </summary>
        public static void HardenDllSearch()
        {
            try { SetDllDirectory(""); }
            catch (Exception) { }
        }

        public static uint GetMonitorDpi(IntPtr monitor)
        {
            try
            {
                uint x, y;
                if (GetDpiForMonitor(monitor, 0, out x, out y) == 0 && x > 0) return x;
            }
            catch (EntryPointNotFoundException) { }
            catch (DllNotFoundException) { }
            return 96;
        }

        /// <summary>Pixelgrootte van een systeemvakicoon op het scherm met de taakbalk (16 bij 100%, 24 bij 150%, 32 bij 200%).</summary>
        public static int GetTrayIconSize()
        {
            IntPtr tray = FindWindow("Shell_TrayWnd", null);
            IntPtr monitor = tray != IntPtr.Zero ? MonitorFromWindow(tray, MONITOR_DEFAULTTOPRIMARY) : MonitorFromPoint(new POINT(0, 0), MONITOR_DEFAULTTOPRIMARY);
            uint dpi = GetMonitorDpi(monitor);
            try
            {
                int size = GetSystemMetricsForDpi(SM_CXSMICON, dpi);
                if (size > 0) return size;
            }
            catch (EntryPointNotFoundException) { }
            return (int)Math.Round(16 * dpi / 96.0);
        }

        public static RectI? GetTaskbarRect()
        {
            var data = new APPBARDATA { cbSize = Marshal.SizeOf(typeof(APPBARDATA)) };
            if (SHAppBarMessage(ABM_GETTASKBARPOS, ref data) == IntPtr.Zero) return null;
            return data.rc.ToRectI();
        }
    }
}
