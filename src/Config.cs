using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace DshTray
{
    /// <summary>Persistent launcher settings, stored as a small INI file.</summary>
    internal sealed class Config
    {
        public int Port = 3080;
        public string Host = "127.0.0.1";
        public string Profile = "web";
        public string ExtraArgs = "";
        public string NodePath = "";
        public string DshEntry = "";
        public string WorkingDirectory = "";
        public bool OpenOnReady = true;
        public bool StartOnLaunch = true;
        public bool FallbackRandomPort = true;
        public int LaunchTimeoutSeconds = 240;

        public string ConfigPath = "";

        /// <summary>The unauthenticated root URL used for status probes.</summary>
        public string CleanUrl
        {
            get
            {
                if (Port <= 0 || string.IsNullOrEmpty(Host)) return "";
                return "http://" + Host + ":" + Port.ToString(CultureInfo.InvariantCulture) + "/";
            }
        }

        public string EffectiveWorkingDirectory
        {
            get
            {
                if (!string.IsNullOrEmpty(WorkingDirectory) && Directory.Exists(WorkingDirectory)) return WorkingDirectory;
                string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (!string.IsNullOrEmpty(home) && Directory.Exists(home)) return home;
                return Environment.CurrentDirectory;
            }
        }

        public static Config Load(string path)
        {
            Config config = new Config();
            config.ConfigPath = path;
            if (!File.Exists(path))
            {
                try
                {
                    config.Save();
                }
                catch
                {
                }
                return config;
            }

            try
            {
                foreach (string raw in File.ReadAllLines(path, Util.Utf8))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";") || line.StartsWith("[")) continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = line.Substring(0, eq).Trim();
                    string value = line.Substring(eq + 1).Trim();
                    switch (key.ToLowerInvariant())
                    {
                        case "port": config.Port = Util.ParseInt(value, config.Port); break;
                        case "host": config.Host = value; break;
                        case "profile": config.Profile = string.IsNullOrEmpty(value) ? "web" : value; break;
                        case "extraargs": config.ExtraArgs = value; break;
                        case "nodepath": config.NodePath = value; break;
                        case "dshentry": config.DshEntry = value; break;
                        case "workingdir": config.WorkingDirectory = value; break;
                        case "openonready": config.OpenOnReady = Util.ParseBool(value, config.OpenOnReady); break;
                        case "startonlaunch": config.StartOnLaunch = Util.ParseBool(value, config.StartOnLaunch); break;
                        case "fallbackrandomport": config.FallbackRandomPort = Util.ParseBool(value, config.FallbackRandomPort); break;
                        case "launchtimeoutseconds": config.LaunchTimeoutSeconds = Util.ParseInt(value, config.LaunchTimeoutSeconds); break;
                    }
                }
            }
            catch
            {
            }

            if (config.Port < 0 || config.Port > 65535) config.Port = 3080;
            if (string.IsNullOrEmpty(config.Host)) config.Host = "127.0.0.1";
            if (config.LaunchTimeoutSeconds < 30) config.LaunchTimeoutSeconds = 30;
            return config;
        }

        public void Save()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("# DeepSeek Harness \u6258\u76d8\u542f\u52a8\u5668\u914d\u7f6e");
            sb.AppendLine("# \u4fee\u6539\u540e\u8bf7\u9000\u51fa\u6258\u76d8\u518d\u91cd\u65b0\u6253\u5f00\uff0c\u65b0\u914d\u7f6e\u624d\u4f1a\u751f\u6548");
            sb.AppendLine();
            sb.AppendLine("# \u76d1\u542c\u7aef\u53e3\uff1b\u586b 0 \u8868\u793a\u7531\u7cfb\u7edf\u5206\u914d\u968f\u673a\u7aef\u53e3");
            sb.AppendLine("port=" + Port.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("# \u76d1\u542c\u5730\u5740\uff0c\u9ed8\u8ba4\u4ec5\u672c\u673a 127.0.0.1");
            sb.AppendLine("host=" + Host);
            sb.AppendLine("# DSH profile \u540d\u79f0\uff0c\u5bf9\u5e94 dsh web \u7684 web profile");
            sb.AppendLine("profile=" + Profile);
            sb.AppendLine("# \u8ffd\u52a0\u538b\u7ed9 dsh web \u7684\u53c2\u6570\uff0c\u4f8b\u5982 --public-url https://dsh.example.com/");
            sb.AppendLine("extraArgs=" + ExtraArgs);
            sb.AppendLine("# \u624b\u52a8\u6307\u5b9a node.exe\uff1b\u7559\u7a7a\u81ea\u52a8\u67e5\u627e");
            sb.AppendLine("nodePath=" + NodePath);
            sb.AppendLine("# \u624b\u52a8\u6307\u5b9a @deepseek-ai/dsh \u5305\u76ee\u5f55\u6216 lib\\bin.js\uff1b\u7559\u7a7a\u81ea\u52a8\u67e5\u627e");
            sb.AppendLine("dshEntry=" + DshEntry);
            sb.AppendLine("# DSH \u8fdb\u7a0b\u5de5\u4f5c\u76ee\u5f55\uff1b\u7559\u7a7a\u7528\u7528\u6237\u76ee\u5f55");
            sb.AppendLine("workingDir=" + WorkingDirectory);
            sb.AppendLine("# DSH \u5c31\u7eea\u540e\u81ea\u52a8\u6253\u5f00\u9875\u9762");
            sb.AppendLine("openOnReady=" + (OpenOnReady ? "true" : "false"));
            sb.AppendLine("# \u542f\u52a8\u6258\u76d8\u65f6\u81ea\u52a8\u542f\u52a8 DSH");
            sb.AppendLine("startOnLaunch=" + (StartOnLaunch ? "true" : "false"));
            sb.AppendLine("# \u76ee\u6807\u7aef\u53e3\u88ab\u5176\u4ed6\u7a0b\u5e8f\u5360\u7528\u65f6\uff0c\u81ea\u52a8\u6539\u7528\u968f\u673a\u7aef\u53e3");
            sb.AppendLine("fallbackRandomPort=" + (FallbackRandomPort ? "true" : "false"));
            sb.AppendLine("# \u542f\u52a8\u8d85\u65f6\u79d2\u6570");
            sb.AppendLine("launchTimeoutSeconds=" + LaunchTimeoutSeconds.ToString(CultureInfo.InvariantCulture));
            try
            {
                File.WriteAllText(ConfigPath, sb.ToString(), Util.Utf8);
            }
            catch
            {
            }
        }
    }
}
