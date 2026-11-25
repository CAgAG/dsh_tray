using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace DshTray
{
    internal enum DshState
    {
        Stopped,
        Starting,
        Running,
        Stopping,
        Error,
        External,
        Conflict
    }

    internal sealed class StatusSnapshot
    {
        public DshState State = DshState.Stopped;
        public string Message = "\u5df2\u505c\u6b62";
        public string CleanUrl = "";
        public string AuthUrl = "";
        public string Error = "";
        public int Pid;
        public bool Managed;
        public DateTime StartedAt = DateTime.MinValue;
        public string NodeDetail = "";
    }

    /// <summary>
    /// Owns the DSH child process: spawning, readiness detection from its output,
    /// health polling, adoption of an already running instance, and stop.
    /// All state mutation happens on one worker thread; process callbacks only
    /// take the short lock.
    /// </summary>
    internal sealed class ServerController : IDisposable
    {
        private static readonly Regex UrlPattern = new Regex("dsh web:\\s*(https?://[^\\s\"']+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex AnsiPattern = new Regex("\\x1B\\[[0-9;?]*[A-Za-z]", RegexOptions.Compiled);

        private readonly Config _cfg;
        private readonly Logger _log;
        private readonly Resolver _resolver;
        private readonly string _statePath;
        private readonly string _statusPath;
        private readonly object _gate = new object();
        private readonly object _statusGate = new object();
        private readonly object _stateFileGate = new object();
        private readonly AutoResetEvent _wake = new AutoResetEvent(false);

        private Process _proc;
        private int _tailGeneration;
        private DshState _state = DshState.Stopped;
        private string _message = "\u5df2\u505c\u6b62";
        private string _cleanUrl;
        private string _authUrl;
        private string _error = "";
        private bool _managed;
        private int _pid;
        private DateTime _startedAt = DateTime.MinValue;
        private bool _intentionalStop;
        private bool _openWhenReady;
        private bool _opened;
        private int _probeFailures;
        private string _nodeDetail = "";
        private bool _disposed;

        private bool _startRequested;
        private bool _stopRequested;
        private bool _restartRequested;

        private Thread _worker;

        /// <summary>When true (autostart / --silent), never open a browser automatically.</summary>
        public bool SuppressAutoOpen;

        public event EventHandler Changed;

        public ServerController(Config cfg, Logger log, string dataDir)
        {
            _cfg = cfg;
            _log = log;
            _resolver = new Resolver(cfg, log);
            _statePath = AppPaths.StateFile(dataDir);
            _statusPath = AppPaths.StatusFile(dataDir);
            _cleanUrl = cfg.CleanUrl;
        }

        public string StateFilePath { get { return _statePath; } }

        public StatusSnapshot Snapshot
        {
            get
            {
                lock (_gate)
                {
                    StatusSnapshot snapshot = new StatusSnapshot();
                    snapshot.State = _state;
                    snapshot.Message = _message;
                    snapshot.CleanUrl = _cleanUrl ?? "";
                    snapshot.AuthUrl = _authUrl ?? "";
                    snapshot.Error = _error ?? "";
                    snapshot.Pid = _pid;
                    snapshot.Managed = _managed;
                    snapshot.StartedAt = _startedAt;
                    snapshot.NodeDetail = _nodeDetail;
                    if (snapshot.CleanUrl.Length == 0 && snapshot.AuthUrl.Length > 0) snapshot.CleanUrl = Probe.StripQuery(snapshot.AuthUrl);
                    return snapshot;
                }
            }
        }

        public void StartWorker()
        {
            if (_worker != null) return;
            _worker = new Thread(WorkerLoop);
            _worker.IsBackground = true;
            _worker.Name = "dsh-controller";
            _worker.Start();
        }

        public void Start()
        {
            lock (_gate)
            {
                _startRequested = true;
                if (_cfg.OpenOnReady) _openWhenReady = true;
            }
            _wake.Set();
        }

        public void StartAndOpen()
        {
            lock (_gate)
            {
                _startRequested = true;
                _openWhenReady = true;
            }
            _wake.Set();
        }

        public void Stop()
        {
            lock (_gate) _stopRequested = true;
            _wake.Set();
        }

        public void Restart()
        {
            lock (_gate) _restartRequested = true;
            _wake.Set();
        }

        public void Refresh()
        {
            _wake.Set();
        }

        public bool StopAndWait(int timeoutMs)
        {
            StatusSnapshot snapshot = Snapshot;
            if (snapshot.State == DshState.Stopped) return true;
            Stop();
            DateTime deadline = DateTime.Now.AddMilliseconds(Math.Max(500, timeoutMs));
            while (DateTime.Now < deadline)
            {
                snapshot = Snapshot;
                if (snapshot.State == DshState.Stopped || snapshot.State == DshState.Error) return true;
                Thread.Sleep(100);
            }
            return false;
        }

        private void WorkerLoop()
        {
            _log.Info("controller worker started");
            try
            {
                Initialize();
            }
            catch (Exception ex)
            {
                _log.Error("initialize failed: " + ex);
                SetError("初始化失败：" + ex.Message);
            }

            int tick = 0;
            while (true)
            {
                lock (_gate) { if (_disposed) break; }
                try
                {
                    ProcessPending();
                    tick++;
                    Tick(tick);
                }
                catch (Exception ex)
                {
                    _log.Error("worker loop error: " + ex.Message);
                }
                _wake.WaitOne(1500);
            }
            _log.Info("controller worker stopped");
        }

        private void ProcessPending()
        {
            bool start = false;
            bool stop = false;
            bool restart = false;
            lock (_gate)
            {
                stop = _stopRequested;
                start = _startRequested;
                restart = _restartRequested;
                _stopRequested = false;
                _startRequested = false;
                _restartRequested = false;
            }
            if (stop || restart) StopInternal();
            if (restart) start = true;
            if (start) StartInternal();
        }

        private void Initialize()
        {
            if (_cfg.Port > 0)
            {
                ProbeResult probe = Probe.Run(_cfg.CleanUrl, 2500);
                if (probe.IsDsh)
                {
                    _log.Info("found an already running DSH at " + _cfg.CleanUrl);
                    AttachRunning(probe);
                }
                else if (probe.PortInUse)
                {
                    _log.Warn("port " + _cfg.Port + " is occupied by another program");
                    SetConflict();
                }
            }

            DshState state = Snapshot.State;
            if (_cfg.StartOnLaunch && state != DshState.Running && state != DshState.External)
            {
                StartInternal();
            }
            else
            {
                RaiseChanged();
            }
        }

        private void StartInternal()
        {
            DshState state;
            lock (_gate) { state = _state; }
            if (state == DshState.Starting || state == DshState.Running || state == DshState.External || state == DshState.Stopping)
            {
                return;
            }

            lock (_gate)
            {
                _state = DshState.Starting;
                _message = "\u542f\u52a8\u4e2d\u2026";
                _error = "";
                _authUrl = null;
                _opened = false;
                _startedAt = DateTime.Now;
                _managed = false;
                _pid = 0;
                _probeFailures = 0;
                _cleanUrl = _cfg.Port > 0 ? _cfg.CleanUrl : "";
                if (_cfg.OpenOnReady) _openWhenReady = true;
            }
            RaiseChanged();
            _log.Info("starting DSH (configured port " + _cfg.Port.ToString(CultureInfo.InvariantCulture) + ")");

            int port = _cfg.Port;
            if (port > 0)
            {
                ProbeResult existing = Probe.Run(_cfg.CleanUrl, 1500);
                if (existing.IsDsh)
                {
                    _log.Info("DSH already answers on " + _cfg.CleanUrl + "; attaching instead of starting a second one");
                    AttachRunning(existing);
                    return;
                }
                if (existing.PortInUse)
                {
                    if (_cfg.FallbackRandomPort)
                    {
                        port = 0;
                        _log.Warn("port " + _cfg.Port + " is occupied by another program; starting on an OS-assigned port");
                    }
                    else
                    {
                        SetError("端口 " + _cfg.Port + " 已被其他程序占用，请在配置文件中修改 port。");
                        return;
                    }
                }
            }

            LaunchCommand command;
            try
            {
                command = _resolver.Build(port);
            }
            catch (Exception ex)
            {
                SetError(ex.Message);
                return;
            }

            Process process;
            try
            {
                process = Spawn(command, port);
            }
            catch (Exception ex)
            {
                SetError("无法启动 DSH：" + ex.Message);
                return;
            }

            bool readyAlready = false;
            bool lateDispose = false;
            lock (_gate)
            {
                if (_disposed)
                {
                    lateDispose = true;
                }
                else
                {
                    _proc = process;
                    _pid = process.Id;
                    _managed = true;
                    _nodeDetail = command.Description;
                    readyAlready = !string.IsNullOrEmpty(_authUrl);
                    if (readyAlready) _state = DshState.Running;
                }
            }
            if (lateDispose)
            {
                _log.Warn("launcher is shutting down; discarding freshly spawned DSH pid=" + process.Id);
                KillProcess(process, process.Id);
                return;
            }
            _log.Info("spawned DSH pid=" + process.Id + " via " + command.Description);
            if (readyAlready)
            {
                SaveState(process.Id, _cleanUrl, _authUrl, _startedAt);
                RaiseChanged();
            }
            else
            {
                RaiseChanged();
            }
        }

        private Process Spawn(LaunchCommand command, int port)
        {
            long offset = _log.PrepareDshLog();
            Process process = NativeProcess.Start(command.FileName, command.Arguments, _cfg.EffectiveWorkingDirectory, _log.DshLogPath);
            try
            {
                process.EnableRaisingEvents = true;
                process.Exited += OnProcessExited;
            }
            catch (Exception ex)
            {
                // Some restricted environments deny the SYNCHRONIZE handle needed for
                // exit events; Tick still polls liveness through the Win32 API.
                _log.Warn("exit events unavailable for pid " + process.Id + ": " + ex.Message + " (using polling)");
            }
            StartLogTail(offset);
            return process;
        }

        private void StartLogTail(long offset)
        {
            int generation;
            lock (_gate)
            {
                generation = ++_tailGeneration;
            }
            Thread thread = new Thread(delegate() { TailLog(generation, offset); });
            thread.IsBackground = true;
            thread.Name = "dsh-log-tail";
            thread.Start();
        }

        /// <summary>
        /// Reads newly appended DSH output until the launch URL appears. The child writes
        /// to a file (not a pipe), so it survives the tray exiting.
        /// </summary>
        private void TailLog(int generation, long offset)
        {
            StringBuilder partial = new StringBuilder();
            DateTime deadline = DateTime.Now.AddSeconds(_cfg.LaunchTimeoutSeconds + 60);
            while (!_disposed && DateTime.Now < deadline)
            {
                lock (_gate)
                {
                    if (generation != _tailGeneration) return;
                    if (!string.IsNullOrEmpty(_authUrl)) return;
                }
                try
                {
                    if (File.Exists(_log.DshLogPath))
                    {
                        using (FileStream stream = new FileStream(_log.DshLogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                        using (StreamReader reader = new StreamReader(stream, Util.Utf8))
                        {
                            if (offset > stream.Length) offset = 0;
                            stream.Seek(offset, SeekOrigin.Begin);
                            string chunk = reader.ReadToEnd();
                            offset = stream.Position;
                            if (chunk.Length > 0)
                            {
                                partial.Append(chunk);
                                string text = partial.ToString();
                                int lastNewline = text.LastIndexOf('\n');
                                if (lastNewline >= 0)
                                {
                                    string complete = text.Substring(0, lastNewline);
                                    partial.Length = 0;
                                    partial.Append(text.Substring(lastNewline + 1));
                                    string[] lines = complete.Split('\n');
                                    for (int i = 0; i < lines.Length; i++)
                                    {
                                        string url = ExtractUrl(lines[i].TrimEnd('\r'));
                                        if (url != null)
                                        {
                                            HandleReady(url);
                                            return;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                catch
                {
                }
                Thread.Sleep(150);
            }
        }

        private static string ExtractUrl(string line)
        {
            if (string.IsNullOrEmpty(line)) return null;
            string clean = AnsiPattern.Replace(line, "");
            Match match = UrlPattern.Match(clean);
            if (!match.Success) return null;
            string url = match.Groups[1].Value.Trim();
            return url.Length > 0 ? url : null;
        }

        private void HandleReady(string url)
        {
            bool becameReady = false;
            int pid;
            string clean;
            DateTime started;
            lock (_gate)
            {
                if (_disposed) return;
                if (!string.IsNullOrEmpty(_authUrl)) return;
                _authUrl = url;
                _cleanUrl = Probe.StripQuery(url);
                clean = _cleanUrl;
                started = _startedAt;
                pid = _pid;
                if (_state == DshState.Starting)
                {
                    _state = DshState.Running;
                    _message = "\u8fd0\u884c\u4e2d";
                    _error = "";
                    becameReady = true;
                }
            }
            if (!becameReady) return;
            _log.Info("DSH ready at " + clean);
            SaveState(pid, clean, url, started);
            RaiseChanged();
            TryAutoOpen(url);
        }

        private void TryAutoOpen(string url)
        {
            bool open = false;
            lock (_gate)
            {
                if (_openWhenReady && !_opened)
                {
                    _opened = true;
                    _openWhenReady = false;
                    open = !SuppressAutoOpen;
                }
            }
            if (open) OpenUrl(url);
        }

        public void OpenUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return;
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(url);
                psi.UseShellExecute = true;
                Process.Start(psi);
                _log.Info("opened browser: " + url);
            }
            catch (Exception ex)
            {
                _log.Error("could not open browser: " + ex.Message);
            }
        }

        private void Tick(int tick)
        {
            DshState state;
            Process process;
            string clean;
            string auth;
            DateTime started;
            lock (_gate)
            {
                state = _state;
                process = _proc;
                clean = _cleanUrl ?? "";
                auth = _authUrl ?? "";
                started = _startedAt;
            }

            if (process != null)
            {
                bool exited = false;
                try
                {
                    exited = process.HasExited;
                }
                catch
                {
                    exited = !NativeProcess.IsAlive(process.Id);
                }
                if (exited)
                {
                    HandleProcessExit(process);
                    return;
                }
            }

            ProbeResult probe = null;
            if (clean.Length > 0 && (tick % 2 == 0))
            {
                probe = Probe.Run(clean, 1200);
            }

            switch (state)
            {
                case DshState.Starting:
                    if (auth.Length > 0)
                    {
                        MarkRunning();
                    }
                    if ((DateTime.Now - started).TotalSeconds > _cfg.LaunchTimeoutSeconds)
                    {
                        _log.Warn("DSH did not become ready within " + _cfg.LaunchTimeoutSeconds + "s");
                        StopInternal();
                        SetError("DSH 启动超时（" + _cfg.LaunchTimeoutSeconds + " 秒），请查看日志排查。");
                    }
                    break;

                case DshState.Running:
                    if (probe != null)
                    {
                        if (probe.IsDsh)
                        {
                            _probeFailures = 0;
                        }
                        else
                        {
                            _probeFailures++;
                            if (process == null && _probeFailures >= 3)
                            {
                                MarkStopped("DSH \u5df2\u505c\u6b62");
                            }
                            else if (_probeFailures == 10)
                            {
                                _log.Warn("DSH health probe keeps failing while the process is alive");
                            }
                        }
                    }
                    break;

                case DshState.External:
                    if (probe != null)
                    {
                        if (probe.IsDsh)
                        {
                            _probeFailures = 0;
                        }
                        else
                        {
                            _probeFailures++;
                            if (_probeFailures >= 3) MarkStopped("\u5916\u90e8 DSH \u5df2\u505c\u6b62");
                        }
                    }
                    break;

                case DshState.Stopped:
                case DshState.Conflict:
                    if (tick % 3 == 0 && _cfg.Port > 0)
                    {
                        ProbeResult idle = Probe.Run(_cfg.CleanUrl, 900);
                        if (idle.IsDsh) AttachRunning(idle);
                        else if (idle.PortInUse) { if (state == DshState.Stopped) SetConflict(); }
                        else if (state == DshState.Conflict) MarkStopped("\u5df2\u505c\u6b62");
                    }
                    break;
            }
        }

        private void MarkRunning()
        {
            bool changed = false;
            lock (_gate)
            {
                if (_state != DshState.Running)
                {
                    _state = DshState.Running;
                    _message = "\u8fd0\u884c\u4e2d";
                    _error = "";
                    changed = true;
                }
            }
            if (changed) RaiseChanged();
        }

        private void MarkStopped(string message)
        {
            lock (_gate)
            {
                _tailGeneration++;
                _proc = null;
                _pid = 0;
                _authUrl = null;
                _managed = false;
                _state = DshState.Stopped;
                _message = string.IsNullOrEmpty(message) ? "\u5df2\u505c\u6b62" : message;
                _error = "";
                _probeFailures = 0;
                _opened = false;
                _openWhenReady = false;
            }
            DeleteStateFile();
            RaiseChanged();
        }

        private void HandleProcessExit(Process process)
        {
            int exitCode = -1;
            try { exitCode = process.ExitCode; } catch { }
            bool intentional;
            bool current;
            lock (_gate)
            {
                current = object.ReferenceEquals(_proc, process);
                intentional = _intentionalStop;
                if (current)
                {
                    _tailGeneration++;
                    _proc = null;
                    _pid = 0;
                    _authUrl = null;
                    _managed = false;
                    _opened = false;
                    _probeFailures = 0;
                }
            }
            if (!current) return;

            _log.Info("DSH process exited code=" + exitCode.ToString(CultureInfo.InvariantCulture));
            if (intentional)
            {
                lock (_gate)
                {
                    _state = DshState.Stopped;
                    _message = "\u5df2\u505c\u6b62";
                    _error = "";
                    _intentionalStop = false;
                }
                DeleteStateFile();
            }
            else
            {
                string tail = _log.TailDsh(8);
                string reason = LastMeaningfulLine(tail);
                string message = "DSH \u610f\u5916\u9000\u51fa\uff08\u4ee3\u7801 " + exitCode.ToString(CultureInfo.InvariantCulture) + "\uff09";
                if (!string.IsNullOrEmpty(reason)) message += "\uff1a" + reason;
                lock (_gate)
                {
                    _state = DshState.Error;
                    _message = "\u542f\u52a8\u5931\u8d25";
                    _error = message;
                }
                _log.Error(message + Environment.NewLine + tail);
                DeleteStateFile();
            }
            RaiseChanged();
        }

        private void OnProcessExited(object sender, EventArgs e)
        {
            Process process = sender as Process;
            if (process == null) return;
            try
            {
                HandleProcessExit(process);
            }
            catch (Exception ex)
            {
                _log.Error("exit handler failed: " + ex.Message);
            }
        }

        private static string LastMeaningfulLine(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            string[] lines = text.Split(new string[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = lines.Length - 1; i >= 0; i--)
            {
                string line = lines[i].Trim();
                if (line.Length == 0) continue;
                if (line.StartsWith("    at ", StringComparison.Ordinal)) continue;
                if (line.StartsWith("Node.js", StringComparison.Ordinal)) continue;
                return Util.SingleLine(line);
            }
            return "";
        }

        private void StopInternal()
        {
            Process process;
            int pid;
            DshState state;
            lock (_gate)
            {
                process = _proc;
                pid = _pid;
                state = _state;
                _tailGeneration++;
                _intentionalStop = true;
                _state = DshState.Stopping;
                _message = "\u6b63\u5728\u505c\u6b62\u2026";
                _authUrl = null;
                _openWhenReady = false;
            }
            _log.Info("stopping DSH (state=" + state + ")");
            RaiseChanged();

            int targetPid = 0;
            if (process != null)
            {
                try
                {
                    if (!process.HasExited) targetPid = process.Id;
                }
                catch
                {
                }
                if (targetPid > 0)
                {
                    KillProcess(process, targetPid);
                    try { process.WaitForExit(5000); } catch { }
                }
                try { process.Dispose(); } catch { }
            }
            else
            {
                targetPid = pid;
                if (targetPid <= 0 && _cfg.Port > 0 && state == DshState.External)
                {
                    targetPid = Probe.FindPortOwner(_cfg.Port);
                }
                if (targetPid > 0) KillProcess(null, targetPid);
            }

            bool stillAlive = targetPid > 0 && IsProcessAlive(targetPid);
            if (stillAlive)
            {
                string message = "\u65e0\u6cd5\u7ed3\u675f DSH \u8fdb\u7a0b\uff08PID " + targetPid.ToString(CultureInfo.InvariantCulture) + "\uff09\uff0c\u53ef\u80fd\u9700\u8981\u7ba1\u7406\u5458\u6743\u9650\u3002";
                lock (_gate)
                {
                    _proc = null;
                    _pid = targetPid;
                    _managed = false;
                    _state = DshState.Error;
                    _message = "\u505c\u6b62\u5931\u8d25";
                    _error = message;
                    _intentionalStop = false;
                    _probeFailures = 0;
                    _opened = false;
                }
                _log.Error(message);
                RaiseChanged();
                return;
            }

            lock (_gate)
            {
                _proc = null;
                _pid = 0;
                _managed = false;
                _state = DshState.Stopped;
                _message = "\u5df2\u505c\u6b62";
                _error = "";
                _probeFailures = 0;
                _intentionalStop = false;
                _opened = false;
                _cleanUrl = _cfg.CleanUrl;
            }
            DeleteStateFile();
            _log.Info("DSH stopped");
            RaiseChanged();
        }

        /// <summary>
        /// Ends a DSH process. taskkill handles the whole tree but can be denied in
        /// restricted environments, so fall back to terminating through a handle.
        /// </summary>
        private void KillProcess(Process known, int pid)
        {
            if (pid <= 0) return;
            KillTree(pid);
            if (!IsProcessAlive(pid))
            {
                _log.Info("DSH pid " + pid + " exited");
                return;
            }
            try
            {
                if (known != null && !known.HasExited)
                {
                    known.Kill();
                }
                else
                {
                    using (Process target = Process.GetProcessById(pid))
                    {
                        target.Kill();
                    }
                }
                _log.Info("killed pid " + pid + " through its process handle");
            }
            catch (Exception ex)
            {
                _log.Warn("direct kill failed for pid " + pid + ": " + ex.Message);
            }
            if (!IsProcessAlive(pid)) return;
            if (NativeProcess.Terminate(pid, 1))
            {
                _log.Info("terminated pid " + pid + " through the Win32 API");
            }
            else
            {
                _log.Warn("terminate failed for pid " + pid);
            }
        }

        private static bool IsProcessAlive(int pid)
        {
            return NativeProcess.IsAlive(pid);
        }

        private void KillTree(int pid)
        {
            if (pid <= 0) return;
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
                    if (killer == null) return;
                    killer.WaitForExit(6000);
                }
                _log.Info("taskkill sent to pid " + pid);
            }
            catch (Exception ex)
            {
                _log.Warn("taskkill failed for pid " + pid + ": " + ex.Message);
            }
            for (int i = 0; i < 20 && IsProcessAlive(pid); i++)
            {
                Thread.Sleep(100);
            }
        }

        private void AttachRunning(ProbeResult probe)
        {
            StateFile state = StateFile.Load(_statePath);
            if (state != null
                && !string.IsNullOrEmpty(state.AuthUrl)
                && Probe.SameUrl(state.CleanUrl, _cfg.CleanUrl)
                && IsAliveNode(state.Pid, state.StartedAt))
            {
                Process process = null;
                try
                {
                    process = Process.GetProcessById(state.Pid);
                    process.EnableRaisingEvents = true;
                    process.Exited += OnProcessExited;
                }
                catch (Exception ex)
                {
                    // Without a handle we can still manage it by pid and token; health
                    // polling covers the exit we can no longer observe.
                    _log.Warn("adopted pid=" + state.Pid + " without exit events: " + ex.Message);
                    try { if (process != null) process.Dispose(); } catch { }
                    process = null;
                }
                lock (_gate)
                {
                    _proc = process;
                    _pid = state.Pid;
                    _authUrl = state.AuthUrl;
                    _cleanUrl = state.CleanUrl;
                    _startedAt = state.StartedAt;
                    _managed = true;
                    _state = DshState.Running;
                    _message = "\u8fd0\u884c\u4e2d";
                    _error = "";
                }
                _log.Info("adopted previous DSH process pid=" + state.Pid);
                RaiseChanged();
                return;
            }

            int owner = 0;
            int port = 0;
            try { port = new Uri(_cfg.CleanUrl).Port; } catch { }
            if (port > 0) owner = Probe.FindPortOwner(port);
            lock (_gate)
            {
                _proc = null;
                _pid = owner;
                _authUrl = null;
                _managed = false;
                _cleanUrl = _cfg.CleanUrl;
                _state = DshState.External;
                _message = "\u8fd0\u884c\u4e2d\uff08\u5916\u90e8\u5b9e\u4f8b\uff09";
                _error = "";
            }
            _log.Info("attached to external DSH at " + _cfg.CleanUrl + " pid=" + owner + " probe=" + (probe != null ? probe.StatusCode.ToString(CultureInfo.InvariantCulture) : "?"));
            RaiseChanged();
        }

        private static bool IsAliveNode(int pid, DateTime startedAt)
        {
            if (pid <= 0) return false;
            try
            {
                Process process = Process.GetProcessById(pid);
                if (process.HasExited) return false;
                if (!string.Equals(process.ProcessName, "node", StringComparison.OrdinalIgnoreCase)) return false;
                if (startedAt != DateTime.MinValue)
                {
                    DateTime actual = process.StartTime;
                    if (Math.Abs((actual - startedAt.ToLocalTime()).TotalMinutes) > 2) return false;
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        private void SetError(string message)
        {
            _log.Error(message);
            lock (_gate)
            {
                _tailGeneration++;
                _proc = null;
                _pid = 0;
                _authUrl = null;
                _managed = false;
                _state = DshState.Error;
                _message = "\u542f\u52a8\u5931\u8d25";
                _error = message;
                _probeFailures = 0;
                _opened = false;
            }
            DeleteStateFile();
            RaiseChanged();
        }

        private void SetConflict()
        {
            lock (_gate)
            {
                _state = DshState.Conflict;
                _message = "\u7aef\u53e3\u88ab\u5360\u7528";
                _error = "端口 " + _cfg.Port.ToString(CultureInfo.InvariantCulture) + " 已被其他程序占用";
            }
            RaiseChanged();
        }

        private void SaveState(int pid, string cleanUrl, string authUrl, DateTime startedAt)
        {
            if (pid <= 0) return;
            lock (_stateFileGate)
            {
                StateFile.Save(_statePath, pid, cleanUrl, authUrl, startedAt);
            }
        }

        private void DeleteStateFile()
        {
            lock (_stateFileGate)
            {
                Util.SafeDelete(_statePath);
            }
        }

        private void RaiseChanged()
        {
            PersistStatus();
            EventHandler handler = Changed;
            if (handler == null) return;
            try { handler(this, EventArgs.Empty); } catch { }
        }

        private static string StateName(DshState state)
        {
            switch (state)
            {
                case DshState.Starting: return "starting";
                case DshState.Running: return "running";
                case DshState.Stopping: return "stopping";
                case DshState.Error: return "error";
                case DshState.External: return "external";
                case DshState.Conflict: return "conflict";
                default: return "stopped";
            }
        }

        /// <summary>Publishes the current state for `--status` and other external readers.</summary>
        private void PersistStatus()
        {
            StatusSnapshot snapshot = Snapshot;
            lock (_statusGate)
            {
                try
                {
                    StringBuilder sb = new StringBuilder();
                    sb.AppendLine("state=" + StateName(snapshot.State));
                    sb.AppendLine("managed=" + (snapshot.Managed ? "true" : "false"));
                    sb.AppendLine("pid=" + snapshot.Pid.ToString(CultureInfo.InvariantCulture));
                    sb.AppendLine("url=" + snapshot.CleanUrl);
                    sb.AppendLine("authUrl=" + snapshot.AuthUrl);
                    sb.AppendLine("message=" + Util.SingleLine(snapshot.Message));
                    sb.AppendLine("error=" + Util.SingleLine(snapshot.Error));
                    sb.AppendLine("updated=" + DateTime.Now.ToString("o", CultureInfo.InvariantCulture));
                    string temporary = _statusPath + ".tmp";
                    File.WriteAllText(temporary, sb.ToString(), Util.Utf8);
                    if (File.Exists(_statusPath)) File.Delete(_statusPath);
                    File.Move(temporary, _statusPath);
                }
                catch
                {
                }
            }
        }

        public void Dispose()
        {
            bool notReady;
            Process process;
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
                _tailGeneration++;
                notReady = string.IsNullOrEmpty(_authUrl);
                process = _proc;
                _proc = null;
            }
            _wake.Set();
            try
            {
                if (_worker != null) _worker.Join(12000);
            }
            catch
            {
            }

            if (process != null)
            {
                // A ready DSH is detached and keeps running; only a half-started one
                // is aborted so that quitting does not leave an untracked server.
                if (notReady)
                {
                    try
                    {
                        if (!process.HasExited) KillProcess(process, process.Id);
                    }
                    catch
                    {
                    }
                }
                try { process.Dispose(); } catch { }
            }
            Util.SafeDelete(_statusPath);
        }
    }
}
