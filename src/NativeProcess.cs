using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace DshTray
{
    /// <summary>
    /// Starts DSH with its stdout/stderr attached to the log file instead of pipes.
    /// That way the server keeps running (and keeps its log) after the tray exits.
    /// </summary>
    internal static class NativeProcess
    {
        private const uint GENERIC_READ = 0x80000000;
        private const uint FILE_APPEND_DATA = 0x0004;
        private const uint FILE_SHARE_READ = 0x00000001;
        private const uint FILE_SHARE_WRITE = 0x00000002;
        private const uint FILE_SHARE_DELETE = 0x00000004;
        private const uint OPEN_EXISTING = 3;
        private const uint OPEN_ALWAYS = 4;
        private const uint FILE_ATTRIBUTE_NORMAL = 0x80;
        private const int STARTF_USESTDHANDLES = 0x00000100;
        private const uint CREATE_NO_WINDOW = 0x08000000;
        private const uint PROCESS_TERMINATE = 0x0001;
        private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
        private const uint STILL_ACTIVE = 259;
        private static readonly IntPtr InvalidHandle = new IntPtr(-1);

        [StructLayout(LayoutKind.Sequential)]
        private struct SECURITY_ATTRIBUTES
        {
            public int nLength;
            public IntPtr lpSecurityDescriptor;
            [MarshalAs(UnmanagedType.Bool)] public bool bInheritHandle;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct STARTUPINFO
        {
            public int cb;
            public string lpReserved;
            public string lpDesktop;
            public string lpTitle;
            public int dwX;
            public int dwY;
            public int dwXSize;
            public int dwYSize;
            public int dwXCountChars;
            public int dwYCountChars;
            public int dwFillAttribute;
            public int dwFlags;
            public short wShowWindow;
            public short cbReserved2;
            public IntPtr lpReserved2;
            public IntPtr hStdInput;
            public IntPtr hStdOutput;
            public IntPtr hStdError;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PROCESS_INFORMATION
        {
            public IntPtr hProcess;
            public IntPtr hThread;
            public int dwProcessId;
            public int dwThreadId;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CreateProcess(
            string lpApplicationName,
            string lpCommandLine,
            IntPtr lpProcessAttributes,
            IntPtr lpThreadAttributes,
            [MarshalAs(UnmanagedType.Bool)] bool bInheritHandles,
            uint dwCreationFlags,
            IntPtr lpEnvironment,
            string lpCurrentDirectory,
            ref STARTUPINFO lpStartupInfo,
            out PROCESS_INFORMATION lpProcessInformation);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateFile(
            string lpFileName,
            uint dwDesiredAccess,
            uint dwShareMode,
            ref SECURITY_ATTRIBUTES lpSecurityAttributes,
            uint dwCreationDisposition,
            uint dwFlagsAndAttributes,
            IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint dwDesiredAccess, [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle, int dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetExitCodeProcess(IntPtr hProcess, out uint lpExitCode);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool TerminateProcess(IntPtr hProcess, uint uExitCode);

        /// <summary>
        /// Liveness check through the Win32 API. Managed Process objects can be
        /// unavailable or access-restricted; this only needs limited query rights.
        /// </summary>
        public static bool IsAlive(int pid)
        {
            if (pid <= 0) return false;
            IntPtr handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (handle == IntPtr.Zero) return false;
            try
            {
                uint exitCode;
                if (!GetExitCodeProcess(handle, out exitCode)) return false;
                return exitCode == STILL_ACTIVE;
            }
            finally
            {
                CloseHandle(handle);
            }
        }

        /// <summary>Terminates a process directly (fallback when taskkill is denied).</summary>
        public static bool Terminate(int pid, uint exitCode)
        {
            if (pid <= 0) return false;
            IntPtr handle = OpenProcess(PROCESS_TERMINATE, false, pid);
            if (handle == IntPtr.Zero) return false;
            try
            {
                return TerminateProcess(handle, exitCode);
            }
            finally
            {
                CloseHandle(handle);
            }
        }

        public static Process Start(string fileName, string arguments, string workingDirectory, string logPath)
        {
            SECURITY_ATTRIBUTES security = new SECURITY_ATTRIBUTES();
            security.nLength = Marshal.SizeOf(typeof(SECURITY_ATTRIBUTES));
            security.bInheritHandle = true;

            uint share = FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE;
            IntPtr logHandle = CreateFile(logPath, FILE_APPEND_DATA, share, ref security, OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, IntPtr.Zero);
            if (logHandle == InvalidHandle || logHandle == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "\u65e0\u6cd5\u6253\u5f00 DSH \u65e5\u5fd7\u6587\u4ef6");
            }

            IntPtr nullHandle = CreateFile("NUL", GENERIC_READ, share, ref security, OPEN_EXISTING, 0, IntPtr.Zero);
            if (nullHandle == InvalidHandle || nullHandle == IntPtr.Zero)
            {
                CloseHandle(logHandle);
                throw new Win32Exception(Marshal.GetLastWin32Error(), "\u65e0\u6cd5\u6253\u5f00 NUL \u8bbe\u5907");
            }

            STARTUPINFO startup = new STARTUPINFO();
            startup.cb = Marshal.SizeOf(typeof(STARTUPINFO));
            startup.dwFlags = STARTF_USESTDHANDLES;
            startup.hStdInput = nullHandle;
            startup.hStdOutput = logHandle;
            startup.hStdError = logHandle;

            string commandLine = Util.Quote(fileName) + " " + arguments;
            PROCESS_INFORMATION info;
            bool started = false;
            try
            {
                started = CreateProcess(
                    fileName,
                    commandLine,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    true,
                    CREATE_NO_WINDOW,
                    IntPtr.Zero,
                    workingDirectory,
                    ref startup,
                    out info);
                if (!started)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "\u65e0\u6cd5\u542f\u52a8 DSH \u8fdb\u7a0b");
                }
            }
            finally
            {
                // The child owns duplicates of both handles now.
                CloseHandle(logHandle);
                CloseHandle(nullHandle);
            }

            try
            {
                CloseHandle(info.hThread);
                CloseHandle(info.hProcess);
            }
            catch
            {
            }

            try
            {
                return Process.GetProcessById(info.dwProcessId);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("\u65e0\u6cd5\u8ddf\u8e2a DSH \u8fdb\u7a0b\uff1a" + ex.Message + "\uff08\u8bf7\u67e5\u770b\u65e5\u5fd7\uff09", ex);
            }
        }
    }
}
