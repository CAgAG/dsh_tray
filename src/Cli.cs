using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace DshTray
{
    internal sealed class CliOptions
    {
        public string Action = "tray";
        public bool Autostart;
        public bool Silent;
        public bool ShowHelp;
        public string DataDir = "";
        public int Port = -1;
        public string OutFile = "";
        public string Language = "";

        public static CliOptions Parse(string[] args)
        {
            CliOptions options = new CliOptions();
            List<string> tokens = new List<string>();
            foreach (string arg in args)
            {
                if (arg == null) continue;
                if (arg.StartsWith("--", StringComparison.Ordinal))
                {
                    int eq = arg.IndexOf('=');
                    if (eq > 2)
                    {
                        tokens.Add(arg.Substring(0, eq));
                        tokens.Add(arg.Substring(eq + 1));
                        continue;
                    }
                }
                tokens.Add(arg);
            }

            for (int i = 0; i < tokens.Count; i++)
            {
                string token = tokens[i].ToLowerInvariant();
                switch (token)
                {
                    case "--status": options.Action = "status"; break;
                    case "--diagnose": options.Action = "diagnose"; break;
                    case "--start": options.Action = "start"; break;
                    case "--stop": options.Action = "stop"; break;
                    case "--restart": options.Action = "restart"; break;
                    case "--open": options.Action = "open"; break;
                    case "--quit": options.Action = "quit"; break;
                    case "--autostart": options.Autostart = true; break;
                    case "--silent": options.Silent = true; break;
                    case "--help":
                    case "-h":
                    case "/?":
                        options.ShowHelp = true;
                        break;
                    case "--data":
                        options.DataDir = NextValue(tokens, ref i, "--data");
                        break;
                    case "--port":
                        options.Port = Util.ParseInt(NextValue(tokens, ref i, "--port"), -1);
                        break;
                    case "--out":
                        options.OutFile = NextValue(tokens, ref i, "--out");
                        break;
                    case "--language":
                        options.Action = "language";
                        options.Language = NextValue(tokens, ref i, "--language");
                        break;
                }
            }
            return options;
        }

        private static string NextValue(List<string> tokens, ref int index, string name)
        {
            if (index + 1 >= tokens.Count) throw new ArgumentException(Strings.T(name + " 后面需要一个参数", name + " requires a value"));
            index++;
            return tokens[index];
        }
    }

    internal static class CliRunner
    {
        public static void PrintHelp()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(Strings.T("DeepSeek Harness 托盘启动器 ", "DeepSeek Harness Tray ") + AppPaths.Version);
            sb.AppendLine();
            sb.AppendLine(Strings.T("用法：", "Usage:"));
            sb.AppendLine(Strings.T("  DeepSeekHarnessTray.exe                  启动托盘（并自动启动 DSH）",
                "  DeepSeekHarnessTray.exe                  Start the tray (and start DSH)"));
            sb.AppendLine(Strings.T("  DeepSeekHarnessTray.exe --autostart      开机自启模式（不自动弹浏览器）",
                "  DeepSeekHarnessTray.exe --autostart      Autostart mode (no automatic browser)"));
            sb.AppendLine(Strings.T("  DeepSeekHarnessTray.exe --status         输出当前状态（已运行时退出码 0）",
                "  DeepSeekHarnessTray.exe --status         Print current status (exit 0 when running)"));
            sb.AppendLine(Strings.T("  DeepSeekHarnessTray.exe --diagnose       输出诊断信息",
                "  DeepSeekHarnessTray.exe --diagnose       Print diagnostics"));
            sb.AppendLine(Strings.T("  DeepSeekHarnessTray.exe --start          确保 DSH 已启动",
                "  DeepSeekHarnessTray.exe --start          Ensure DSH is running"));
            sb.AppendLine(Strings.T("  DeepSeekHarnessTray.exe --stop           停止 DSH",
                "  DeepSeekHarnessTray.exe --stop           Stop DSH"));
            sb.AppendLine(Strings.T("  DeepSeekHarnessTray.exe --restart        重启 DSH",
                "  DeepSeekHarnessTray.exe --restart        Restart DSH"));
            sb.AppendLine(Strings.T("  DeepSeekHarnessTray.exe --open           打开 DSH 页面",
                "  DeepSeekHarnessTray.exe --open           Open the DSH page"));
            sb.AppendLine(Strings.T("  DeepSeekHarnessTray.exe --quit           退出托盘（DSH 保持运行）",
                "  DeepSeekHarnessTray.exe --quit           Quit the tray (DSH keeps running)"));
            sb.AppendLine(Strings.T("  DeepSeekHarnessTray.exe --language <auto|zh|en>  切换界面语言（托盘运行中立即生效）",
                "  DeepSeekHarnessTray.exe --language <auto|zh|en>  Switch UI language (live when the tray is running)"));
            sb.AppendLine();
            sb.AppendLine(Strings.T("选项：", "Options:"));
            sb.AppendLine(Strings.T("  --data <目录>   数据目录（默认 %LOCALAPPDATA%\\", "  --data <dir>    Data directory (default %LOCALAPPDATA%\\")
                + AppPaths.AppName + ")");
            sb.AppendLine(Strings.T("  --port <端口>   覆盖配置中的端口", "  --port <port>   Override the configured port"));
            sb.AppendLine(Strings.T("  --silent      不弹任何气泡提示，不自动打开浏览器",
                "  --silent        No balloon tips, no automatic browser"));
            sb.AppendLine(Strings.T("  --out <文件>   同时把输出写入指定文件", "  --out <file>    Also write the output to a file"));
            Console.Write(sb.ToString());
        }

        public static void SignalExisting(CliOptions options)
        {
            IntPtr window = FindTrayWindow(3000);
            if (window == IntPtr.Zero) return;
            int message = Native.WM_DSH_OPEN;
            IntPtr payload = IntPtr.Zero;
            switch (options.Action)
            {
                case "start": message = Native.WM_DSH_START; break;
                case "stop": message = Native.WM_DSH_STOP; break;
                case "quit": message = Native.WM_DSH_QUIT; break;
                case "open": message = Native.WM_DSH_OPEN; break;
            }
            if (message == Native.WM_DSH_OPEN && options.Silent) payload = new IntPtr(1);
            Native.PostMessage(window, message, payload, IntPtr.Zero);
        }

        public static IntPtr FindTrayWindow(int waitMilliseconds)
        {
            DateTime deadline = DateTime.Now.AddMilliseconds(Math.Max(0, waitMilliseconds));
            do
            {
                IntPtr handle = Native.FindWindow(null, AppPaths.WindowTitle);
                if (handle != IntPtr.Zero) return handle;
                Thread.Sleep(100);
            }
            while (DateTime.Now < deadline);
            return IntPtr.Zero;
        }

        public static int RunInspection(CliOptions options, Config config, string dataDir, Logger log)
        {
            StringBuilder sb = new StringBuilder();
            if (options.Action == "diagnose") Diagnose(sb, config, dataDir, log);
            else Status(sb, config, dataDir, log);

            Emit(options, dataDir, sb.ToString());
            if (options.Action == "diagnose") return 0;
            return IsRunningText(sb.ToString()) ? 0 : 1;
        }

        private static bool IsRunningText(string text)
        {
            return text.IndexOf("state=running", StringComparison.Ordinal) >= 0
                || text.IndexOf("state=external", StringComparison.Ordinal) >= 0;
        }

        private static void Status(StringBuilder sb, Config config, string dataDir, Logger log)
        {
            IntPtr window = FindTrayWindow(0);
            RuntimeStatus live = window != IntPtr.Zero ? RuntimeStatus.Load(AppPaths.StatusFile(dataDir)) : null;
            if (live != null)
            {
                sb.AppendLine("state=" + live.State);
                sb.AppendLine("managed=" + (live.Managed ? "true" : "false"));
                sb.AppendLine("pid=" + live.Pid.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("url=" + live.Url);
                sb.AppendLine("authUrl=" + live.AuthUrl);
                sb.AppendLine("message=" + live.Message);
                sb.AppendLine("source=status-file");
                sb.AppendLine("trayRunning=true");
                sb.AppendLine("dataDir=" + dataDir);
                sb.AppendLine("log=" + log.LogPath);
                return;
            }

            ProbeResult probe = new ProbeResult();
            if (config.Port > 0) probe = Probe.Run(config.CleanUrl, 1500);
            StateFile state = StateFile.Load(AppPaths.StateFile(dataDir));

            string name = "stopped";
            bool managed = false;
            int pid = 0;
            string authUrl = "";
            if (probe.IsDsh)
            {
                if (state != null
                    && !string.IsNullOrEmpty(state.AuthUrl)
                    && Probe.SameUrl(state.CleanUrl, config.CleanUrl)
                    && IsNodeAlive(state.Pid))
                {
                    name = "running";
                    managed = true;
                    pid = state.Pid;
                    authUrl = state.AuthUrl;
                }
                else
                {
                    name = "external";
                    pid = config.Port > 0 ? Probe.FindPortOwner(config.Port) : 0;
                }
            }
            else if (probe.PortInUse)
            {
                name = "port-conflict";
            }

            sb.AppendLine("state=" + name);
            sb.AppendLine("managed=" + (managed ? "true" : "false"));
            sb.AppendLine("pid=" + pid.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("url=" + config.CleanUrl);
            sb.AppendLine("authUrl=" + authUrl);
            sb.AppendLine("portInUse=" + (probe.PortInUse ? "true" : "false"));
            sb.AppendLine("httpStatus=" + probe.StatusCode.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("source=probe");
            sb.AppendLine("trayRunning=" + (window != IntPtr.Zero ? "true" : "false"));
            sb.AppendLine("dataDir=" + dataDir);
            sb.AppendLine("log=" + log.LogPath);
        }

        private static void Diagnose(StringBuilder sb, Config config, string dataDir, Logger log)
        {
            sb.AppendLine("DeepSeekHarnessTray " + AppPaths.Version);
            sb.AppendLine("exe=" + Environment.GetCommandLineArgs()[0]);
            sb.AppendLine("dataDir=" + dataDir);
            sb.AppendLine("config=" + config.ConfigPath);
            sb.AppendLine("port=" + config.Port.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("host=" + config.Host);
            sb.AppendLine("profile=" + config.Profile);
            sb.AppendLine("extraArgs=" + config.ExtraArgs);
            sb.AppendLine("openOnReady=" + (config.OpenOnReady ? "true" : "false"));
            sb.AppendLine("startOnLaunch=" + (config.StartOnLaunch ? "true" : "false"));
            sb.AppendLine("fallbackRandomPort=" + (config.FallbackRandomPort ? "true" : "false"));
            sb.AppendLine("trayWindow=" + (FindTrayWindow(0) != IntPtr.Zero ? "present" : "absent"));
            sb.AppendLine("dataWritable=" + (AppPaths.CanWriteDirectory(dataDir) ? "true" : "false"));
            sb.AppendLine("language=" + Strings.LanguageTag);
            sb.AppendLine("languageSetting=" + config.Language);
            sb.AppendLine("launcherLog=" + log.LogPath);
            sb.AppendLine("dshLog=" + log.DshLogPath);
            sb.AppendLine();

            Resolver resolver = new Resolver(config, log);
            try
            {
                string node = resolver.ResolveNode();
                sb.AppendLine("node=" + (node ?? Strings.T("(未找到)", "(not found)")));
            }
            catch (Exception ex)
            {
                sb.AppendLine("node=(error: " + ex.Message + ")");
            }

            try
            {
                string version;
                string source;
                string entry = resolver.ResolveDshEntry(out version, out source);
                sb.AppendLine("dshEntry=" + (entry ?? Strings.T("(未找到)", "(not found)")));
                sb.AppendLine("dshVersion=" + version);
                sb.AppendLine("dshSource=" + source);
            }
            catch (Exception ex)
            {
                sb.AppendLine("dshEntry=(error: " + ex.Message + ")");
            }

            try
            {
                LaunchCommand command = resolver.Build(config.Port);
                sb.AppendLine("launchFile=" + command.FileName);
                sb.AppendLine("launchArgs=" + command.Arguments);
                sb.AppendLine("launchDescription=" + command.Description);
            }
            catch (Exception ex)
            {
                sb.AppendLine("launch=(error: " + ex.Message + ")");
            }

            sb.AppendLine();
            if (config.Port > 0)
            {
                ProbeResult probe = Probe.Run(config.CleanUrl, 2000);
                sb.AppendLine("probe.url=" + config.CleanUrl);
                sb.AppendLine("probe.reachable=" + probe.Reachable);
                sb.AppendLine("probe.status=" + probe.StatusCode.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("probe.isDsh=" + probe.IsDsh);
                sb.AppendLine("probe.portInUse=" + probe.PortInUse);
                sb.AppendLine("probe.error=" + probe.Error);
            }
            else
            {
                sb.AppendLine("probe=(port 0: dynamic port)");
            }

            StateFile state = StateFile.Load(AppPaths.StateFile(dataDir));
            if (state == null)
            {
                sb.AppendLine("stateFile=(none)");
            }
            else
            {
                sb.AppendLine("stateFile.pid=" + state.Pid.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("stateFile.cleanUrl=" + state.CleanUrl);
                sb.AppendLine("stateFile.authUrl=" + state.AuthUrl);
                sb.AppendLine("stateFile.started=" + state.StartedAt.ToString("o", CultureInfo.InvariantCulture));
                sb.AppendLine("stateFile.alive=" + IsNodeAlive(state.Pid));
            }

            RuntimeStatus runtime = RuntimeStatus.Load(AppPaths.StatusFile(dataDir));
            if (runtime == null)
            {
                sb.AppendLine("statusFile=(none)");
            }
            else
            {
                sb.AppendLine("statusFile.state=" + runtime.State);
                sb.AppendLine("statusFile.managed=" + (runtime.Managed ? "true" : "false"));
                sb.AppendLine("statusFile.pid=" + runtime.Pid.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("statusFile.url=" + runtime.Url);
                sb.AppendLine("statusFile.authUrl=" + runtime.AuthUrl);
                sb.AppendLine("statusFile.message=" + runtime.Message);
                sb.AppendLine("statusFile.updated=" + runtime.Updated.ToString("o", CultureInfo.InvariantCulture));
            }
        }

        public static int RunControl(CliOptions options, Config config, string dataDir, Logger log)
        {
            IntPtr window = FindTrayWindow(1200);
            StringBuilder sb = new StringBuilder();

            switch (options.Action)
            {
                case "start":
                {
                    if (window != IntPtr.Zero) Native.PostMessage(window, Native.WM_DSH_START, IntPtr.Zero, IntPtr.Zero);
                    else LaunchTray(options, dataDir);
                    bool trayUp = window != IntPtr.Zero || FindTrayWindow(5000) != IntPtr.Zero;
                    bool up = WaitForServer(config, dataDir, true, 45000, trayUp);
                    sb.AppendLine("action=start");
                    sb.AppendLine("running=" + (up ? "true" : "false"));
                    sb.AppendLine("url=" + config.CleanUrl);
                    Emit(options, dataDir, sb.ToString());
                    return up ? 0 : 1;
                }
                case "stop":
                {
                    if (window != IntPtr.Zero) Native.PostMessage(window, Native.WM_DSH_STOP, IntPtr.Zero, IntPtr.Zero);
                    else StopDirect(config, dataDir, log);
                    bool down = WaitForServer(config, dataDir, false, 20000, window != IntPtr.Zero);
                    sb.AppendLine("action=stop");
                    sb.AppendLine("running=" + (down ? "false" : "true"));
                    Emit(options, dataDir, sb.ToString());
                    return down ? 0 : 1;
                }
                case "restart":
                {
                    bool up;
                    if (window != IntPtr.Zero)
                    {
                        RuntimeStatus before = RuntimeStatus.Load(AppPaths.StatusFile(dataDir));
                        string oldPid = before != null ? before.Pid.ToString(CultureInfo.InvariantCulture) : "";
                        Native.PostMessage(window, Native.WM_DSH_RESTART, IntPtr.Zero, IntPtr.Zero);
                        up = WaitForRestart(dataDir, 90000, oldPid);
                    }
                    else
                    {
                        LaunchTray(options, dataDir);
                        bool trayUp = FindTrayWindow(5000) != IntPtr.Zero;
                        up = WaitForServer(config, dataDir, true, 90000, trayUp);
                    }
                    sb.AppendLine("action=restart");
                    sb.AppendLine("running=" + (up ? "true" : "false"));
                    sb.AppendLine("url=" + config.CleanUrl);
                    Emit(options, dataDir, sb.ToString());
                    return up ? 0 : 1;
                }
                case "open":
                {
                    if (window != IntPtr.Zero)
                    {
                        Native.PostMessage(window, Native.WM_DSH_OPEN, IntPtr.Zero, IntPtr.Zero);
                        Emit(options, dataDir, "action=open\r\ndelegatedToTray=true\r\n");
                        return 0;
                    }
                    string url = ResolveOpenUrl(config, dataDir);
                    OpenUrl(url, log);
                    Emit(options, dataDir, "action=open\r\nurl=" + url + "\r\n");
                    return 0;
                }
                case "quit":
                {
                    if (window != IntPtr.Zero) Native.PostMessage(window, Native.WM_DSH_QUIT, IntPtr.Zero, IntPtr.Zero);
                    Emit(options, dataDir, "action=quit\r\ntrayRunning=" + (window != IntPtr.Zero ? "true" : "false") + "\r\n");
                    return 0;
                }
                case "language":
                {
                    string value = (options.Language ?? "").Trim().ToLowerInvariant();
                    if (value.Length == 0) value = "auto";
                    if (value != "auto" && value != "system" && value != "zh" && value != "en"
                        && !value.StartsWith("zh-", StringComparison.Ordinal)
                        && !value.StartsWith("en-", StringComparison.Ordinal))
                    {
                        Emit(options, dataDir, "action=language\r\nerror=invalid language (auto|zh|en)\r\n");
                        return 2;
                    }

                    string normalized = value == "auto" || value == "system"
                        ? "auto"
                        : (value.StartsWith("zh", StringComparison.Ordinal) ? "zh" : "en");
                    if (window != IntPtr.Zero)
                    {
                        int code = normalized == "auto" ? 0 : (normalized == "zh" ? 1 : 2);
                        if (Native.PostMessage(window, Native.WM_DSH_LANGUAGE, new IntPtr(code), IntPtr.Zero))
                        {
                            Emit(options, dataDir, "action=language\r\nlanguage=" + normalized + "\r\napplied=live\r\n");
                            return 0;
                        }
                    }

                    config.Language = normalized;
                    config.Save();
                    Strings.Initialize(normalized);
                    Emit(options, dataDir, "action=language\r\nlanguage=" + normalized + "\r\napplied=config\r\n");
                    return 0;
                }
            }
            Emit(options, dataDir, "action=unknown\r\n");
            return 2;
        }

        private static void LaunchTray(CliOptions options, string dataDir)
        {
            string exe = Environment.GetCommandLineArgs()[0];
            StringBuilder arguments = new StringBuilder();
            arguments.Append("--data ").Append(Util.Quote(dataDir));
            if (options.Silent) arguments.Append(" --silent");
            if (options.Port > 0) arguments.Append(" --port ").Append(options.Port.ToString(CultureInfo.InvariantCulture));

            ProcessStartInfo psi = new ProcessStartInfo(exe, arguments.ToString());
            // ShellExecute detaches the tray from this caller's stdio handles; otherwise a
            // caller that redirects our output would keep waiting for EOF forever.
            psi.UseShellExecute = true;
            psi.WorkingDirectory = Path.GetDirectoryName(exe);
            Process.Start(psi);
        }

        private static bool WaitForServer(Config config, string dataDir, bool wantRunning, int timeoutMilliseconds, bool preferStatusFile)
        {
            int wanted = wantRunning ? 1 : 0;
            DateTime deadline = DateTime.Now.AddMilliseconds(timeoutMilliseconds);
            while (DateTime.Now < deadline)
            {
                if (GetRunState(config, dataDir, preferStatusFile) == wanted) return true;
                Thread.Sleep(400);
            }
            return false;
        }

        private static bool WaitForRestart(string dataDir, int timeoutMilliseconds, string oldPid)
        {
            DateTime deadline = DateTime.Now.AddMilliseconds(timeoutMilliseconds);
            while (DateTime.Now < deadline)
            {
                RuntimeStatus status = RuntimeStatus.Load(AppPaths.StatusFile(dataDir));
                if (status != null)
                {
                    if (status.State == "external") return true;
                    if (status.State == "running")
                    {
                        string pid = status.Pid.ToString(CultureInfo.InvariantCulture);
                        if (string.IsNullOrEmpty(oldPid) || pid != oldPid) return true;
                    }
                }
                Thread.Sleep(250);
            }
            return false;
        }

        /// <summary>1 = serving, 0 = definitely stopped, -1 = transitioning.</summary>
        private static int GetRunState(Config config, string dataDir, bool preferStatusFile)
        {
            if (preferStatusFile)
            {
                RuntimeStatus status = RuntimeStatus.Load(AppPaths.StatusFile(dataDir));
                if (status != null)
                {
                    switch (status.State)
                    {
                        case "running":
                        case "external":
                            return 1;
                        case "stopped":
                        case "error":
                        case "conflict":
                            return 0;
                        default:
                            return -1;
                    }
                }
            }
            if (config.Port > 0) return Probe.Run(config.CleanUrl, 1000).IsDsh ? 1 : 0;
            StateFile state = StateFile.Load(AppPaths.StateFile(dataDir));
            if (state == null) return 0;
            if (IsNodeAlive(state.Pid)) return 1;
            return 0;
        }

        private static void StopDirect(Config config, string dataDir, Logger log)
        {
            string statePath = AppPaths.StateFile(dataDir);
            StateFile state = StateFile.Load(statePath);
            ProbeResult probe = config.Port > 0 ? Probe.Run(config.CleanUrl, 1500) : null;
            bool serving = probe != null && probe.IsDsh;

            int pid = 0;
            if (state != null && IsNodeAlive(state.Pid) && (probe == null || serving)) pid = state.Pid;
            if (pid == 0 && serving) pid = Probe.FindPortOwner(config.Port);
            if (pid > 0)
            {
                log.Info("cli stop: taskkill pid " + pid);
                KillTree(pid);
                for (int i = 0; i < 20 && IsProcessAlive(pid); i++) Thread.Sleep(100);
                if (IsProcessAlive(pid))
                {
                    try
                    {
                        using (Process target = Process.GetProcessById(pid))
                        {
                            target.Kill();
                        }
                        log.Info("cli stop: killed pid " + pid + " through its process handle");
                    }
                    catch (Exception ex)
                    {
                        log.Warn("cli stop: direct kill failed for pid " + pid + ": " + ex.Message);
                    }
                }
                if (IsProcessAlive(pid) && NativeProcess.Terminate(pid, 1))
                {
                    log.Info("cli stop: terminated pid " + pid + " through the Win32 API");
                }
            }
            else
            {
                log.Info("cli stop: nothing to stop");
            }
            Util.SafeDelete(statePath);
        }

        private static string ResolveOpenUrl(Config config, string dataDir)
        {
            StateFile state = StateFile.Load(AppPaths.StateFile(dataDir));
            if (state != null && !string.IsNullOrEmpty(state.AuthUrl) && IsNodeAlive(state.Pid)) return state.AuthUrl;
            return config.CleanUrl;
        }

        private static void OpenUrl(string url, Logger log)
        {
            if (string.IsNullOrEmpty(url)) return;
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(url);
                psi.UseShellExecute = true;
                Process.Start(psi);
                log.Info("cli open: " + url);
            }
            catch (Exception ex)
            {
                log.Error("cli open failed: " + ex.Message);
            }
        }

        private static void KillTree(int pid)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("taskkill.exe", "/PID " + pid.ToString(CultureInfo.InvariantCulture) + " /T /F");
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.WindowStyle = ProcessWindowStyle.Hidden;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                using (Process killer = Process.Start(psi))
                {
                    if (killer != null) killer.WaitForExit(6000);
                }
            }
            catch
            {
            }
        }

        private static bool IsProcessAlive(int pid)
        {
            return NativeProcess.IsAlive(pid);
        }

        private static bool IsNodeAlive(int pid)
        {
            if (pid <= 0) return false;
            try
            {
                Process process = Process.GetProcessById(pid);
                return !process.HasExited && string.Equals(process.ProcessName, "node", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static void Emit(CliOptions options, string dataDir, string text)
        {
            try { Console.Write(text); } catch { }
            try { File.WriteAllText(AppPaths.CliOutput(dataDir), text, Util.Utf8); } catch { }
            if (!string.IsNullOrEmpty(options.OutFile))
            {
                try { File.WriteAllText(options.OutFile, text, Util.Utf8); } catch { }
            }
        }
    }
}
