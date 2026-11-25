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

            _statusItem = new ToolStripMenuItem("DSH\uff1a\u6b63\u5728\u68c0\u6d4b\u2026");
            _statusItem.Enabled = false;
            _openItem = new ToolStripMenuItem("\u6253\u5f00\u9875\u9762", null, delegate { HandlePrimaryAction(); });
            _copyItem = new ToolStripMenuItem("\u590d\u5236\u9875\u9762\u5730\u5740", null, delegate { CopyUrl(); });
            _restartItem = new ToolStripMenuItem("\u91cd\u542f DSH", null, delegate { RestartServer(); });
            _stopItem = new ToolStripMenuItem("\u505c\u6b62 DSH", null, delegate { StopServer(); });
            _logsItem = new ToolStripMenuItem("\u67e5\u770b\u65e5\u5fd7", null, delegate { OpenFile(_log.LogPath); });
            _configItem = new ToolStripMenuItem("\u7f16\u8f91\u914d\u7f6e", null, delegate { OpenFile(_cfg.ConfigPath); });
            _folderItem = new ToolStripMenuItem("\u6253\u5f00\u6570\u636e\u6587\u4ef6\u5939", null, delegate { OpenFolder(Path.GetDirectoryName(_log.LogPath)); });
            _autoItem = new ToolStripMenuItem("\u5f00\u673a\u81ea\u52a8\u542f\u52a8", null, delegate { ToggleAutoStart(); });
            _exitItem = new ToolStripMenuItem("\u9000\u51fa\uff08DSH \u7ee7\u7eed\u8fd0\u884c\uff09", null, delegate { BeginQuit(false); });

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
            _icon.Text = "DSH \u5df2\u505c\u6b62";
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
            if (!_silent) ShowBalloon("DeepSeek Harness", "\u6258\u76d8\u5df2\u5c31\u7eea\uff0c\u6b63\u5728\u68c0\u6d4b DSH\u2026", ToolTipIcon.Info);
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
                _openItem.Text = live ? "\u6253\u5f00\u9875\u9762" : (busy ? "\u6b63\u5728\u542f\u52a8\u2026" : "\u542f\u52a8 DSH");
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
                    return "DSH \u8fd0\u884c\u4e2d \u00b7 " + Util.DisplayUrl(snapshot.CleanUrl) + (snapshot.Pid > 0 ? " \u00b7 PID " + snapshot.Pid : "");
                case DshState.External:
                    return "DSH \u8fd0\u884c\u4e2d\uff08\u5916\u90e8\u542f\u52a8\uff09\u00b7 " + Util.DisplayUrl(snapshot.CleanUrl);
                case DshState.Starting:
                    return "DSH \u542f\u52a8\u4e2d\u2026";
                case DshState.Stopping:
                    return "DSH \u6b63\u5728\u505c\u6b62\u2026";
                case DshState.Error:
                    return "DSH \u542f\u52a8\u5931\u8d25\uff1a" + Util.SingleLine(snapshot.Error);
                case DshState.Conflict:
                    return "\u7aef\u53e3\u88ab\u5360\u7528\uff1a" + Util.SingleLine(snapshot.Error);
                default:
                    return "DSH \u5df2\u505c\u6b62 \u2014 \u70b9\u51fb\u542f\u52a8";
            }
        }

        private string BuildStatusText(StatusSnapshot snapshot)
        {
            switch (snapshot.State)
            {
                case DshState.Running:
                    return "DSH\uff1a\u8fd0\u884c\u4e2d \u00b7 " + SafeUrl(snapshot.CleanUrl);
                case DshState.External:
                    return "DSH\uff1a\u8fd0\u884c\u4e2d\uff08\u5916\u90e8\u5b9e\u4f8b\uff09\u00b7 " + SafeUrl(snapshot.CleanUrl);
                case DshState.Starting:
                    return "DSH\uff1a\u542f\u52a8\u4e2d\u2026";
                case DshState.Stopping:
                    return "DSH\uff1a\u6b63\u5728\u505c\u6b62\u2026";
                case DshState.Error:
                    return "DSH\uff1a\u542f\u52a8\u5931\u8d25";
                case DshState.Conflict:
                    return "DSH\uff1a\u7aef\u53e3 " + _cfg.Port + " \u88ab\u5360\u7528";
                default:
                    return "DSH\uff1a\u5df2\u505c\u6b62";
            }
        }

        private static string SafeUrl(string url)
        {
            return string.IsNullOrEmpty(url) ? "\u968f\u673a\u7aef\u53e3" : url.TrimEnd('/');
        }

        private void NotifyTransition(DshState previous, StatusSnapshot snapshot)
        {
            if (_silent || _exiting) return;
            switch (snapshot.State)
            {
                case DshState.Running:
                    ShowBalloon("DSH \u5df2\u542f\u52a8", "\u9875\u9762\u5730\u5740\uff1a" + SafeUrl(snapshot.CleanUrl) + "\r\n\u53cc\u51fb\u6258\u76d8\u56fe\u6807\u5373\u53ef\u6253\u5f00", ToolTipIcon.Info);
                    break;
                case DshState.External:
                    if (previous != DshState.External && !_externalHintShown)
                    {
                        _externalHintShown = true;
                        ShowBalloon("\u68c0\u6d4b\u5230\u5df2\u8fd0\u884c\u7684 DSH", "\u8fd9\u4e2a\u5b9e\u4f8b\u7531\u547d\u4ee4\u884c\u542f\u52a8\u3002\u82e5\u9875\u9762\u8981\u6c42\u91cd\u65b0\u9a8c\u8bc1\uff0c\u8bf7\u7528\u83dc\u5355\u91cc\u7684\u300c\u91cd\u542f DSH\u300d\u7531\u6258\u76d8\u63a5\u7ba1\u3002", ToolTipIcon.Info);
                    }
                    break;
                case DshState.Error:
                    ShowBalloon("DSH \u542f\u52a8\u5931\u8d25", Util.Truncate(Util.SingleLine(snapshot.Error), 220), ToolTipIcon.Error);
                    break;
                case DshState.Stopped:
                    if (previous == DshState.Running || previous == DshState.External || previous == DshState.Starting)
                    {
                        ShowBalloon("DSH \u5df2\u505c\u6b62", "\u70b9\u51fb\u6258\u76d8\u56fe\u6807\u53ef\u91cd\u65b0\u542f\u52a8\u3002", ToolTipIcon.Info);
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
                    ShowBalloon("DSH \u6b63\u5728\u542f\u52a8", "\u5c31\u7eea\u540e\u4f1a\u81ea\u52a8\u6253\u5f00\u9875\u9762\u3002", ToolTipIcon.Info);
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
                ShowBalloon("\u9875\u9762\u5c1a\u672a\u5c31\u7eea", "\u8bf7\u7a0d\u7b49 DSH \u542f\u52a8\u5b8c\u6210\u540e\u518d\u8bd5\u3002", ToolTipIcon.Info);
                return;
            }
            _ctl.OpenUrl(url);
            if (snapshot.State == DshState.External && string.IsNullOrEmpty(snapshot.AuthUrl) && !_externalHintShown)
            {
                _externalHintShown = true;
                ShowBalloon("\u5916\u90e8\u5b9e\u4f8b", "\u82e5\u9875\u9762\u63d0\u793a\u9700\u8981\u91cd\u65b0\u9a8c\u8bc1\uff0c\u8bf7\u7528\u83dc\u5355\u91cc\u7684\u300c\u91cd\u542f DSH\u300d\u7531\u6258\u76d8\u63a5\u7ba1\u3002", ToolTipIcon.Info);
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
                ShowBalloon("\u5df2\u590d\u5236", url, ToolTipIcon.Info);
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
                    "\u5f53\u524d DSH \u7531\u547d\u4ee4\u884c\u542f\u52a8\u3002\r\n\u91cd\u542f\u4f1a\u7ed3\u675f\u8be5\u8fdb\u7a0b\uff0c\u5e76\u7531\u6258\u76d8\u91cd\u65b0\u542f\u52a8\u4e00\u4e2a\uff08\u9875\u9762\u5730\u5740\u4f1a\u53d8\u66f4\uff09\u3002\r\n\r\n\u786e\u5b9a\u7ee7\u7eed\u5417\uff1f",
                    "\u91cd\u542f DSH",
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
                "\u505c\u6b62 DSH \u4f1a\u4e2d\u65ad\u6b63\u5728\u8fdb\u884c\u7684\u5bf9\u8bdd\u4efb\u52a1\u3002\r\n\r\n\u786e\u5b9a\u8981\u505c\u6b62\u5417\uff1f",
                "\u505c\u6b62 DSH",
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
                ShowBalloon("\u5f00\u673a\u81ea\u52a8\u542f\u52a8", enable ? "\u5df2\u5f00\u542f\uff1a\u767b\u5f55 Windows \u540e\u6258\u76d8\u4f1a\u81ea\u52a8\u542f\u52a8 DSH\u3002" : "\u5df2\u5173\u95ed\u3002", ToolTipIcon.Info);
            }
            catch (Exception ex)
            {
                ShowBalloon("\u8bbe\u7f6e\u5931\u8d25", ex.Message, ToolTipIcon.Error);
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
                if (key == null) throw new InvalidOperationException("\u65e0\u6cd5\u6253\u5f00\u6ce8\u518c\u8868\u542f\u52a8\u9879");
                if (enabled) key.SetValue(AppPaths.AppName, Util.Quote(_exePath) + " --autostart");
                else key.DeleteValue(AppPaths.AppName, false);
            }
            _log.Info("autostart set to " + enabled);
        }

        private void OpenFile(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                ShowBalloon("\u6587\u4ef6\u4e0d\u5b58\u5728", path ?? "", ToolTipIcon.Warning);
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
                ShowBalloon("\u6253\u5f00\u5931\u8d25", ex.Message, ToolTipIcon.Error);
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
                ShowBalloon("\u6253\u5f00\u5931\u8d25", ex.Message, ToolTipIcon.Error);
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
                            ShowBalloon("DSH " + (current.State == DshState.Running || current.State == DshState.External ? "\u5df2\u5728\u8fd0\u884c" : "\u72b6\u6001"),
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
