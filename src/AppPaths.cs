using System;
using System.IO;
using System.Text;

namespace DshTray
{
    internal static class AppPaths
    {
        public const string AppName = "DeepSeekHarnessTray";
        public const string DisplayName = "DeepSeek Harness \u6258\u76d8";
        public const string ProductName = "DeepSeek Harness Tray";
        public const string Version = "1.3.0";

        /// <summary>
        /// Optional isolated instance (tests, or several profiles side by side): set
        /// DSHTRAY_INSTANCE to give this launcher its own mutex and message window.
        /// </summary>
        private static string InstanceSuffix()
        {
            string value = Environment.GetEnvironmentVariable("DSHTRAY_INSTANCE");
            if (string.IsNullOrEmpty(value)) return "";
            StringBuilder sb = new StringBuilder("-");
            foreach (char c in value)
            {
                if (char.IsLetterOrDigit(c) || c == '_' || c == '-') sb.Append(c);
            }
            return sb.Length > 1 ? sb.ToString() : "";
        }

        public static readonly string WindowTitle = "DSHTrayLauncher-" + Environment.UserName + "-v1" + InstanceSuffix();
        public static readonly string MutexName = "Local\\DeepSeekHarnessTrayLauncher-v1" + InstanceSuffix();

        public static string ResolveDataDir(string overrideDir)
        {
            if (!string.IsNullOrEmpty(overrideDir)) return Path.GetFullPath(overrideDir);
            string env = Environment.GetEnvironmentVariable("DSHTRAY_DATA");
            if (!string.IsNullOrEmpty(env)) return Path.GetFullPath(env);
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(local, AppName);
        }

        public static void EnsureDirectory(string dir)
        {
            if (string.IsNullOrEmpty(dir)) return;
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        }

        /// <summary>Checks that the data directory accepts writes (sandboxes and read-only profiles do not).</summary>
        public static bool CanWriteDirectory(string dir)
        {
            try
            {
                string probe = Path.Combine(dir, ".write-test-" + Guid.NewGuid().ToString("N") + ".tmp");
                File.WriteAllText(probe, "ok", new UTF8Encoding(false));
                File.Delete(probe);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static string ConfigFile(string dataDir) { return Path.Combine(dataDir, "config.ini"); }
        public static string StateFile(string dataDir) { return Path.Combine(dataDir, "state.ini"); }
        public static string StatusFile(string dataDir) { return Path.Combine(dataDir, "status.ini"); }
        public static string LauncherLog(string dataDir) { return Path.Combine(dataDir, "launcher.log"); }
        public static string DshLog(string dataDir) { return Path.Combine(dataDir, "dsh-web.log"); }
        public static string CliOutput(string dataDir) { return Path.Combine(dataDir, "cli-output.txt"); }
        public static string DiagnoseOutput(string dataDir) { return Path.Combine(dataDir, "diagnose.txt"); }
    }
}
