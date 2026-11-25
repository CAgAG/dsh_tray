using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace DshTray
{
    /// <summary>Last known managed DSH process, so a restarted tray can adopt it.</summary>
    internal sealed class StateFile
    {
        public int Pid;
        public string CleanUrl = "";
        public string AuthUrl = "";
        public DateTime StartedAt = DateTime.MinValue;

        public static StateFile Load(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                StateFile state = new StateFile();
                foreach (string raw in File.ReadAllLines(path, Util.Utf8))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";")) continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = line.Substring(0, eq).Trim();
                    string value = line.Substring(eq + 1).Trim();
                    switch (key.ToLowerInvariant())
                    {
                        case "pid": state.Pid = Util.ParseInt(value, 0); break;
                        case "cleanurl": state.CleanUrl = value; break;
                        case "authurl": state.AuthUrl = value; break;
                        case "started":
                            DateTime parsed;
                            if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed)) state.StartedAt = parsed;
                            break;
                    }
                }
                return state.Pid > 0 ? state : null;
            }
            catch
            {
                return null;
            }
        }

        public static void Save(string path, int pid, string cleanUrl, string authUrl, DateTime startedAt)
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("pid=" + pid.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("cleanUrl=" + cleanUrl);
                sb.AppendLine("authUrl=" + authUrl);
                sb.AppendLine("started=" + startedAt.ToString("o", CultureInfo.InvariantCulture));
                File.WriteAllText(path, sb.ToString(), Util.Utf8);
            }
            catch
            {
            }
        }
    }

    /// <summary>The tray's live view, written whenever its state changes; read by `--status`.</summary>
    internal sealed class RuntimeStatus
    {
        public string State = "";
        public bool Managed;
        public int Pid;
        public string Url = "";
        public string AuthUrl = "";
        public string Message = "";
        public string Error = "";
        public DateTime Updated = DateTime.MinValue;

        public static RuntimeStatus Load(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                RuntimeStatus status = new RuntimeStatus();
                foreach (string raw in File.ReadAllLines(path, Util.Utf8))
                {
                    string line = raw.Trim();
                    if (line.Length == 0) continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = line.Substring(0, eq).Trim();
                    string value = line.Substring(eq + 1).Trim();
                    switch (key.ToLowerInvariant())
                    {
                        case "state": status.State = value; break;
                        case "managed": status.Managed = Util.ParseBool(value, false); break;
                        case "pid": status.Pid = Util.ParseInt(value, 0); break;
                        case "url": status.Url = value; break;
                        case "authurl": status.AuthUrl = value; break;
                        case "message": status.Message = value; break;
                        case "error": status.Error = value; break;
                        case "updated":
                            DateTime parsed;
                            if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out parsed)) status.Updated = parsed;
                            break;
                    }
                }
                return status.State.Length > 0 ? status : null;
            }
            catch
            {
                return null;
            }
        }
    }
}
