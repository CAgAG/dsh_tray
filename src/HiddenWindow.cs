using System;
using System.Drawing;
using System.Windows.Forms;

namespace DshTray
{
    /// <summary>
    /// Invisible window whose only job is receiving posted control messages from a
    /// second launcher instance or from command-line control commands.
    /// </summary>
    internal sealed class HiddenWindow : Form
    {
        public event Action<int, IntPtr> MessageReceived;

        public HiddenWindow(string title)
        {
            Text = title;
            ShowInTaskbar = false;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Location = new Point(-32000, -32000);
            Size = new Size(1, 1);
            ShowIcon = false;
            MinimizeBox = false;
            MaximizeBox = false;
            Opacity = 0;
            Visible = false;
        }

        protected override void SetVisibleCore(bool value)
        {
            // Keep the window alive and handle-owning, but never show it.
            base.SetVisibleCore(false);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg >= Native.WM_DSH_OPEN && m.Msg <= Native.WM_DSH_RESTART)
            {
                Action<int, IntPtr> handler = MessageReceived;
                if (handler != null)
                {
                    try { handler(m.Msg, m.WParam); } catch { }
                }
                return;
            }
            base.WndProc(ref m);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            e.Cancel = true;
            base.OnFormClosing(e);
        }
    }
}
