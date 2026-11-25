using System;
using System.Runtime.InteropServices;

namespace DshTray
{
    /// <summary>Small Win32 surface: finding/posting to the running tray window and icon cleanup.</summary>
    internal static class Native
    {
        public const int WM_APP = 0x8000;
        public const int WM_DSH_OPEN = WM_APP + 1;
        public const int WM_DSH_START = WM_APP + 2;
        public const int WM_DSH_STOP = WM_APP + 3;
        public const int WM_DSH_QUIT = WM_APP + 4;
        public const int WM_DSH_REFRESH = WM_APP + 5;
        public const int WM_DSH_RESTART = WM_APP + 6;
        public const int WM_DSH_LANGUAGE = WM_APP + 7;

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool PostMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DestroyIcon(IntPtr hIcon);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetProcessDPIAware();

        [DllImport("kernel32.dll")]
        public static extern IntPtr GetStdHandle(int nStdHandle);

        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);
    }
}
