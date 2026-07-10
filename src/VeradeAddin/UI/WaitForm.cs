using System;
using System.Drawing;
using System.Windows.Forms;

namespace VeradeAddin.UI
{
    /// <summary>
    /// Minimal modeless "please wait" overlay for long SolidWorks operations that run synchronously on the
    /// UI thread. Constructed = shown (and painted once via <see cref="Application.DoEvents"/> before the
    /// blocking COM work begins); disposed = closed. There is no progress bar because the COM call blocks
    /// this thread — it is a reassurance overlay, not animated. Use with <c>using (...) { work }</c>.
    /// </summary>
    internal sealed class WaitForm : Form
    {
        public WaitForm(string title, string message)
        {
            FormBorderStyle = FormBorderStyle.FixedDialog;
            ControlBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterScreen;
            TopMost = true;
            MinimizeBox = false;
            MaximizeBox = false;
            Text = title;
            ClientSize = new Size(360, 96);

            var label = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Text = message,
                Font = new Font("Segoe UI", 10F),
                Padding = new Padding(16)
            };
            Controls.Add(label);

            Show();
            Update();
            Application.DoEvents(); // force a paint before the thread is blocked by the SW call
        }

        protected override void Dispose(bool disposing)
        {
            // NOTE: do NOT call Close() here. For a modeless form shown with Show(), Close() disposes the
            // form, which re-enters Dispose() -> Close() -> ... causing an InvalidOperationException that
            // takes down SolidWorks. Just hide it and let base.Dispose release the window handle.
            if (disposing && !IsDisposed && IsHandleCreated)
            {
                try { Hide(); } catch { /* best-effort hide */ }
            }
            base.Dispose(disposing);
        }
    }
}
