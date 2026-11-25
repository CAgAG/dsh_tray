using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace DshTray
{
    /// <summary>Launcher log plus helpers around the DSH output file.</summary>
    internal sealed class Logger : IDisposable
    {
        private const long MaxLauncherBytes = 4L * 1024 * 1024;
        private const long MaxDshBytes = 16L * 1024 * 1024;

        private readonly object _gate = new object();
        private readonly string _logPath;
        private readonly string _dshPath;
        private readonly bool _verbose;
        private bool _disposed;

        public Logger(string dataDir)
        {
            _logPath = AppPaths.LauncherLog(dataDir);
            _dshPath = AppPaths.DshLog(dataDir);
            _verbose = string.Equals(Environment.GetEnvironmentVariable("DSHTRAY_DEBUG"), "1", StringComparison.Ordinal);
            Rotate(_logPath, MaxLauncherBytes);
        }

        public string LogPath { get { return _logPath; } }
        public string DshLogPath { get { return _dshPath; } }
        public bool Verbose { get { return _verbose; } }

        public void Info(string message) { Write("INFO ", message); }
        public void Warn(string message) { Write("WARN ", message); }
        public void Error(string message) { Write("ERROR", message); }
        public void Debug(string message) { if (_verbose) Write("DEBUG", message); }

        /// <summary>
        /// Rotates the DSH log when it gets large, writes a session banner, and returns
        /// the file offset where this session's output will begin.
        /// </summary>
        public long PrepareDshLog()
        {
            lock (_gate)
            {
                try
                {
                    if (_disposed) return 0;
                    Rotate(_dshPath, MaxDshBytes);
                    string banner = "=== " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " dsh web ===\r\n";
                    File.AppendAllText(_dshPath, banner, Util.Utf8);
                    return new FileInfo(_dshPath).Length;
                }
                catch
                {
                    return 0;
                }
            }
        }

        /// <summary>Returns the last <paramref name="count"/> non-empty DSH output lines.</summary>
        public string TailDsh(int count)
        {
            lock (_gate)
            {
                try
                {
                    if (!File.Exists(_dshPath)) return "";
                    string text;
                    using (FileStream stream = new FileStream(_dshPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    using (StreamReader reader = new StreamReader(stream, Util.Utf8))
                    {
                        text = reader.ReadToEnd();
                    }
                    string[] lines = text.Replace("\r\n", "\n").Split('\n');
                    List<string> tail = new List<string>();
                    for (int i = lines.Length - 1; i >= 0 && tail.Count < count; i--)
                    {
                        if (lines[i].Trim().Length > 0) tail.Insert(0, lines[i]);
                    }
                    return string.Join(Environment.NewLine, tail.ToArray());
                }
                catch
                {
                    return "";
                }
            }
        }

        private void Write(string level, string message)
        {
            lock (_gate)
            {
                if (_disposed) return;
                try
                {
                    Rotate(_logPath, MaxLauncherBytes);
                    string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " [" + level + "] " + message;
                    File.AppendAllText(_logPath, line + Environment.NewLine, Util.Utf8);
                }
                catch
                {
                }
            }
        }

        private static void Rotate(string path, long maxBytes)
        {
            try
            {
                FileInfo info = new FileInfo(path);
                if (!info.Exists || info.Length <= maxBytes) return;
                string backup = path + ".1";
                if (File.Exists(backup)) File.Delete(backup);
                File.Move(path, backup);
            }
            catch
            {
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                _disposed = true;
            }
        }
    }
}
