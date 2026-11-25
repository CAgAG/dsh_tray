using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Microsoft.Win32;

namespace DshTray
{
    /// <summary>The tray icon, its menu, and all user-facing state presentation.</summary>
    internal sealed class TrayContext : ApplicationContext
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

        private readonly Config _cfg;
        private readonly Logger _log;
        private readonly ServerController _ctl;
        private readonly bool _silent;
        private readonly string _exePath;

        private readonly HiddenWindow _hidden;
        private readonly NotifyIcon _icon;
        private readonly ContextMenuStrip _menu;
        private readonly Dictionary<DshState, Icon> _icons = new Dictionary<DshState, Icon>();
        private readonly Timer _clickTimer;

        private readonly ToolStripMenuItem _statusItem;
        private readonly ToolStripMenuItem _openItem;
        private readonly ToolStripMenuItem _copyItem;
        private readonly ToolStripMenuItem _restartItem;
        private readonly ToolStripMenuItem _stopItem;
        private readonly ToolStripMenuItem _logsItem;
        private readonly ToolStripMenuItem _configItem;
        private readonly ToolStripMenuItem _folderItem;
        private readonly ToolStripMenuItem _autoItem;
        private readonly ToolStripMenuItem _exitItem;

        private DshState _lastState = DshState.Stopped;
        private bool _externalHintShown;
        private bool _exiting;

        public TrayContext(Config cfg, Logger log, ServerController controller, string windowTitle, bool silent, string exePath)
        {
            _cfg = cfg;
            _log = log;
            _ctl = controller;
            _silent = silent;
            _exePath = exePath;

            _hidden = new HiddenWindow(windowTitle);
            _hidden.MessageReceived += OnTrayMessage;
            IntPtr created = _hidden.Handle; // force the message window into existence

            _statusItem = new ToolStripMenuItem(Strings.T("DSH：正在检测…", "DSH: detecting..."));
            _statusItem.Enabled = false;
            _openItem = new ToolStripMenuItem(Strings.T("打开页面", "Open page"), null, delegate { HandlePrimaryAction(); });
            _copyItem = new ToolStripMenuItem(Strings.T("复制页面地址", "Copy page URL"), null, delegate { CopyUrl(); });
            _restartItem = new ToolStripMenuItem(Strings.T("重启 DSH", "Restart DSH"), null, delegate { RestartServer(); });
            _stopItem = new ToolStripMenuItem(Strings.T("停止 DSH", "Stop DSH"), null, delegate { StopServer(); });
            _logsItem = new ToolStripMenuItem(Strings.T("查看日志", "View logs"), null, delegate { OpenFile(_log.LogPath); });
            _configItem = new ToolStripMenuItem(Strings.T("编辑配置", "Edit config"), null, delegate { OpenFile(_cfg.ConfigPath); });
            _folderItem = new ToolStripMenuItem(Strings.T("打开数据文件夹", "Open data folder"), null, delegate { OpenFolder(Path.GetDirectoryName(_log.LogPath)); });
            _autoItem = new ToolStripMenuItem(Strings.T("开机自动启动", "Start with Windows"), null, delegate { ToggleAutoStart(); });
            _exitItem = new ToolStripMenuItem(Strings.T("退出（DSH 继续运行）", "Quit (keep DSH running)"), null, delegate { BeginQuit(false); });

            _menu = new ContextMenuStrip();
            _menu.Items.AddRange(new ToolStripItem[]
            {
                _statusItem,
                new ToolStripSeparator(),
                _openItem,
                _copyItem,
                new ToolStripSeparator(),
                _restartItem,
                _stopItem,
                new ToolStripSeparator(),
                _logsItem,
                _configItem,
                _folderItem,
                new ToolStripSeparator(),
                _autoItem,
                new ToolStripSeparator(),
                _exitItem
            });
            _menu.Opening += OnMenuOpening;

            _icon = new NotifyIcon();
            _icon.Icon = GetIcon(DshState.Stopped);
            _icon.Text = Strings.T("DSH 已停止", "DSH stopped");
            _icon.ContextMenuStrip = _menu;
            _icon.Visible = true;
            _icon.MouseClick += OnIconMouseClick;
            _icon.BalloonTipClicked += OnBalloonTipClicked;

            _clickTimer = new Timer();
            _clickTimer.Interval = 250;
            _clickTimer.Tick += OnClickTimerTick;

            _ctl.Changed += OnControllerChanged;
            try { SystemEvents.SessionEnding += OnSessionEnding; }
            catch { }

            _lastState = _ctl.Snapshot.State;
            UpdateUi();
            _ctl.StartWorker();
            _log.Info("tray context created (silent=" + _silent + ")");
            if (!_silent) ShowBalloon("DeepSeek Harness", Strings.T("托盘已就绪，正在检测 DSH…", "Tray ready, detecting DSH..."), ToolTipIcon.Info);
        }

        private Icon GetIcon(DshState state)
        {
            Icon icon;
            if (!_icons.TryGetValue(state, out icon))
            {
                icon = Icons.ForState(state);
                _icons[state] = icon;
            }
            return icon;
        }

        private void OnControllerChanged(object sender, EventArgs e)
        {
            Post(UpdateUi);
        }

        private void Post(Action action)
        {
            try
            {
                if (_hidden == null || _hidden.IsDisposed) return;
                if (_hidden.InvokeRequired)
                {
                    _log.Debug("post: BeginInvoke");
                    _hidden.BeginInvoke(action);
                }
                else
                {
                    _log.Debug("post: direct");
                    action();
                }
            }
            catch (Exception ex)
            {
                _log.Debug("post failed: " + ex.Message);
            }
        }

        private void UpdateUi()
        {
            try
            {
                StatusSnapshot snapshot = _ctl.Snapshot;
                _log.Debug("ui begin state=" + snapshot.State + " last=" + _lastState);
                _icon.Icon = GetIcon(snapshot.State);
                _icon.Text = Util.Truncate(BuildTooltip(snapshot), 63);
                _statusItem.Text = BuildStatusText(snapshot);

                bool live = snapshot.State == DshState.Running || snapshot.State == DshState.External;
                bool busy = snapshot.State == DshState.Starting || snapshot.State == DshState.Stopping;
                _openItem.Text = live
                    ? Strings.T("打开页面", "Open page")
                    : (busy ? Strings.T("正在启动…", "Starting...") : Strings.T("启动 DSH", "Start DSH"));
                _openItem.Enabled = !busy;
                _copyItem.Enabled = live && (snapshot.AuthUrl.Length > 0 || snapshot.CleanUrl.Length > 0);
                _restartItem.Enabled = !busy;
                _stopItem.Enabled = live || busy;

                try { _autoItem.Checked = IsAutoStartEnabled(); }
                catch { }

                if (snapshot.State != _lastState)
                {
                    _log.Debug("ui transition " + _lastState + " -> " + snapshot.State);
                    NotifyTransition(_lastState, snapshot);
                    _lastState = snapshot.State;
                }
                _log.Debug("ui end");
            }
            catch (Exception ex)
            {
                try { _log.Warn("update UI failed: " + ex.Message); }
                catch { }
            }
        }

        private string BuildTooltip(StatusSnapshot snapshot)
        {
            switch (snapshot.State)
            {
                case DshState.Running:
                    return Strings.T("DSH 运行中 · ", "DSH running · ") + Util.DisplayUrl(snapshot.CleanUrl) + (snapshot.Pid > 0 ? " · PID " + snapshot.Pid : "");
                case DshState.External:
                    return Strings.T("DSH 运行中（外部启动）· ", "DSH running (external) · ") + Util.DisplayUrl(snapshot.CleanUrl);
                case DshState.Starting:
                    return Strings.T("DSH 启动中…", "DSH starting...");
                case DshState.Stopping:
                    return Strings.T("DSH 正在停止…", "DSH stopping...");
                case DshState.Error:
                    return Strings.T("DSH 启动失败：", "DSH failed to start: ") + Util.SingleLine(snapshot.Error);
                case DshState.Conflict:
                    return Strings.T("端口被占用：", "Port in use: ") + Util.SingleLine(snapshot.Error);
                default:
                    return Strings.T("DSH 已停止 — 点击启动", "DSH stopped — click to start");
            }
        }

        private string BuildStatusText(StatusSnapshot snapshot)
        {
            switch (snapshot.State)
            {
                case DshState.Running:
                    return Strings.T("DSH：运行中 · ", "DSH: running · ") + SafeUrl(snapshot.CleanUrl);
                case DshState.External:
                    return Strings.T("DSH：运行中（外部实例）· ", "DSH: running (external) · ") + SafeUrl(snapshot.CleanUrl);
                case DshState.Starting:
                    return Strings.T("DSH：启动中…", "DSH: starting...");
                case DshState.Stopping:
                    return Strings.T("DSH：正在停止…", "DSH: stopping...");
                case DshState.Error:
                    return Strings.T("DSH：启动失败", "DSH: failed to start");
                case DshState.Conflict:
                    return Strings.Format("DSH：端口 {0} 被占用", "DSH: port {0} in use", _cfg.Port);
                default:
                    return Strings.T("DSH：已停止", "DSH: stopped");
            }
        }

        private static string SafeUrl(string url)
        {
            return string.IsNullOrEmpty(url) ? Strings.T("随机端口", "random port") : url.TrimEnd('/');
        }

        private void NotifyTransition(DshState previous, StatusSnapshot snapshot)
        {
            if (_silent || _exiting) return;
            switch (snapshot.State)
            {
                case DshState.Running:
                    ShowBalloon(Strings.T("DSH 已启动", "DSH started"),
                        Strings.T("页面地址：", "Page: ") + SafeUrl(snapshot.CleanUrl)
                            + Strings.T("\r\n双击托盘图标即可打开", "\r\nDouble-click the tray icon to open it"),
                        ToolTipIcon.Info);
                    break;
                case DshState.External:
                    if (previous != DshState.External && !_externalHintShown)
                    {
                        _externalHintShown = true;
                        ShowBalloon(Strings.T("检测到已运行的 DSH", "Detected a running DSH"),
                            Strings.T(
                                "这个实例由命令行启动。若页面要求重新验证，请用菜单里的「重启 DSH」由托盘接管。",
                                "This instance was started from a command line. If the page asks you to re-authenticate, use \"Restart DSH\" in the tray menu to take it over."),
                            ToolTipIcon.Info);
                    }
                    break;
                case DshState.Error:
                    ShowBalloon(Strings.T("DSH 启动失败", "DSH failed to start"), Util.Truncate(Util.SingleLine(snapshot.Error), 220), ToolTipIcon.Error);
                    break;
                case DshState.Stopped:
                    if (previous == DshState.Running || previous == DshState.External || previous == DshState.Starting)
                    {
                        ShowBalloon(Strings.T("DSH 已停止", "DSH stopped"),
                            Strings.T("点击托盘图标可重新启动。", "Click the tray icon to start it again."),
                            ToolTipIcon.Info);
                    }
                    break;
            }
        }

        private void ShowBalloon(string title, string text, ToolTipIcon icon)
        {
            try
            {
                _log.Debug("balloon show: " + title);
                _icon.ShowBalloonTip(4000, title, Util.Truncate(text, 250), icon);
                _log.Debug("balloon shown: " + title);
            }
            catch (Exception ex)
            {
                _log.Debug("balloon failed: " + ex.Message);
            }
        }

        private void OnIconMouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            if (_clickTimer.Enabled)
            {
                _clickTimer.Stop();
                HandlePrimaryAction();
                return;
            }
            _clickTimer.Start();
        }

        private void OnClickTimerTick(object sender, EventArgs e)
        {
            _clickTimer.Stop();
            HandlePrimaryAction();
        }

        private void OnBalloonTipClicked(object sender, EventArgs e)
        {
            StatusSnapshot snapshot = _ctl.Snapshot;
            if (snapshot.State == DshState.Running || snapshot.State == DshState.External) OpenPage();
        }

        private void HandlePrimaryAction()
        {
            StatusSnapshot snapshot = _ctl.Snapshot;
            switch (snapshot.State)
            {
                case DshState.Running:
                case DshState.External:
                    OpenPage();
                    break;
                case DshState.Starting:
                    ShowBalloon(Strings.T("DSH 正在启动", "DSH is starting"),
                        Strings.T("就绪后会自动打开页面。", "The page will open automatically when it is ready."),
                        ToolTipIcon.Info);
                    break;
                case DshState.Stopping:
                    break;
                default:
                    _ctl.StartAndOpen();
                    break;
            }
        }

        private void OpenPage()
        {
            StatusSnapshot snapshot = _ctl.Snapshot;
            string url = !string.IsNullOrEmpty(snapshot.AuthUrl) ? snapshot.AuthUrl : snapshot.CleanUrl;
            if (string.IsNullOrEmpty(url))
            {
                ShowBalloon(Strings.T("页面尚未就绪", "The page is not ready yet"),
                    Strings.T("请稍等 DSH 启动完成后再试。", "Please wait for DSH to finish starting."),
                    ToolTipIcon.Info);
                return;
            }
            _ctl.OpenUrl(url);
            if (snapshot.State == DshState.External && string.IsNullOrEmpty(snapshot.AuthUrl) && !_externalHintShown)
            {
                _externalHintShown = true;
                ShowBalloon(Strings.T("外部实例", "External instance"),
                    Strings.T("若页面提示需要重新验证，请用菜单里的「重启 DSH」由托盘接管。",
                        "If the page asks you to re-authenticate, use \"Restart DSH\" in the tray menu to take it over."),
                    ToolTipIcon.Info);
            }
        }

        private void CopyUrl()
        {
            StatusSnapshot snapshot = _ctl.Snapshot;
            string url = !string.IsNullOrEmpty(snapshot.AuthUrl) ? snapshot.AuthUrl : snapshot.CleanUrl;
            if (string.IsNullOrEmpty(url)) return;
            try
            {
                Clipboard.SetText(url);
                ShowBalloon(Strings.T("已复制", "Copied"), url, ToolTipIcon.Info);
            }
            catch (Exception ex)
            {
                _log.Warn("clipboard failed: " + ex.Message);
            }
        }

        private void RestartServer()
        {
            StatusSnapshot snapshot = _ctl.Snapshot;
            if (snapshot.State == DshState.External)
            {
                DialogResult result = MessageBox.Show(
                    Strings.T(
                        "当前 DSH 由命令行启动。\r\n重启会结束该进程，并由托盘重新启动一个（页面地址会变更）。\r\n\r\n确定继续吗？",
                        "The current DSH instance was started from a command line.\r\nRestarting will stop that process and start a new one under the tray (the page URL will change).\r\n\r\nContinue?"),
                    Strings.T("重启 DSH", "Restart DSH"),
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question,
                    MessageBoxDefaultButton.Button2);
                if (result != DialogResult.Yes) return;
            }
            _ctl.Restart();
        }

        private void StopServer()
        {
            StatusSnapshot snapshot = _ctl.Snapshot;
            if (snapshot.State != DshState.Running && snapshot.State != DshState.External && snapshot.State != DshState.Starting) return;
            DialogResult result = MessageBox.Show(
                Strings.T(
                    "停止 DSH 会中断正在进行的对话任务。\r\n\r\n确定要停止吗？",
                    "Stopping DSH will interrupt running conversations and tasks.\r\n\r\nStop it?"),
                Strings.T("停止 DSH", "Stop DSH"),
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            if (result == DialogResult.Yes) _ctl.Stop();
        }

        private void ToggleAutoStart()
        {
            try
            {
                bool enable = !IsAutoStartEnabled();
                SetAutoStart(enable);
                _autoItem.Checked = enable;
                ShowBalloon(Strings.T("开机自动启动", "Start with Windows"),
                    enable
                        ? Strings.T("已开启：登录 Windows 后托盘会自动启动 DSH。", "Enabled: the tray will start DSH after you sign in to Windows.")
                        : Strings.T("已关闭。", "Disabled."),
                    ToolTipIcon.Info);
            }
            catch (Exception ex)
            {
                ShowBalloon(Strings.T("设置失败", "Settings failed"), ex.Message, ToolTipIcon.Error);
            }
        }

        private bool IsAutoStartEnabled()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false))
            {
                if (key == null) return false;
                string value = key.GetValue(AppPaths.AppName) as string;
                return value != null && value.IndexOf(_exePath, StringComparison.OrdinalIgnoreCase) >= 0;
            }
        }

        private void SetAutoStart(bool enabled)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath))
            {
                if (key == null) throw new InvalidOperationException(Strings.T("无法打开注册表启动项", "Cannot open the registry Run key"));
                if (enabled) key.SetValue(AppPaths.AppName, Util.Quote(_exePath) + " --autostart");
                else key.DeleteValue(AppPaths.AppName, false);
            }
            _log.Info("autostart set to " + enabled);
        }

        private void OpenFile(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                ShowBalloon(Strings.T("文件不存在", "File not found"), path ?? "", ToolTipIcon.Warning);
                return;
            }
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(path);
                psi.UseShellExecute = true;
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                ShowBalloon(Strings.T("打开失败", "Open failed"), ex.Message, ToolTipIcon.Error);
            }
        }

        private void OpenFolder(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("explorer.exe", Util.Quote(path));
                psi.UseShellExecute = true;
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                ShowBalloon(Strings.T("打开失败", "Open failed"), ex.Message, ToolTipIcon.Error);
            }
        }

        private void OnMenuOpening(object sender, CancelEventArgs e)
        {
            UpdateUi();
        }

        private void OnTrayMessage(int message, IntPtr wParam)
        {
            switch (message)
            {
                case Native.WM_DSH_OPEN:
                    if (wParam != IntPtr.Zero)
                    {
                        _log.Info("second launch asked to open the page in silent mode");
                        StatusSnapshot current = _ctl.Snapshot;
                        if (!_silent)
                        {
                            ShowBalloon("DSH " + (current.State == DshState.Running || current.State == DshState.External
                                    ? Strings.T("已在运行", "already running")
                                    : Strings.T("状态", "status")),
                                BuildTooltip(current), ToolTipIcon.Info);
                        }
                    }
                    else
                    {
                        HandlePrimaryAction();
                    }
                    break;
                case Native.WM_DSH_START:
                    _ctl.StartAndOpen();
                    break;
                case Native.WM_DSH_STOP:
                    _ctl.Stop();
                    break;
                case Native.WM_DSH_RESTART:
                    _ctl.Restart();
                    break;
                case Native.WM_DSH_QUIT:
                    BeginQuit(true);
                    break;
                case Native.WM_DSH_REFRESH:
                    _ctl.Refresh();
                    break;
            }
        }

        private void OnSessionEnding(object sender, SessionEndingEventArgs e)
        {
            try
            {
                _log.Info("session ending; DSH follows the OS session");
            }
            catch
            {
            }
        }

        private void BeginQuit(bool fromMessage)
        {
            if (_exiting) return;
            _exiting = true;
            StatusSnapshot snapshot = _ctl.Snapshot;
            if (snapshot.State == DshState.Running || snapshot.State == DshState.External || snapshot.State == DshState.Starting)
            {
                _log.Info("exit: DSH keeps running (state=" + snapshot.State + ", pid=" + snapshot.Pid + ")");
            }
            else
            {
                _log.Info("exit: no running DSH (fromMessage=" + fromMessage + ")");
            }
            ExitThread();
        }

        protected override void ExitThreadCore()
        {
            try { _log.Info("tray shutting down"); }
            catch { }
            try { _clickTimer.Stop(); _clickTimer.Dispose(); } catch { }
            try { _icon.Visible = false; _icon.Dispose(); } catch { }
            try { _menu.Dispose(); } catch { }
            foreach (Icon icon in _icons.Values)
            {
                try { icon.Dispose(); } catch { }
            }
            _icons.Clear();
            try { _hidden.Dispose(); } catch { }
            try { SystemEvents.SessionEnding -= OnSessionEnding; } catch { }
            try { _ctl.Dispose(); } catch { }
            base.ExitThreadCore();
        }
    }
}
