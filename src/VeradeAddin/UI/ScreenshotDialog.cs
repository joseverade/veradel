using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using VeradeAddin.Models;

namespace VeradeAddin.UI
{
    /// <summary>
    /// Modal preview of a captured image with three actions: "Guardar como", "Copiar al
    /// portapapeles" and "Cerrar". The image is loaded into an independent in-memory bitmap so
    /// the source file is never locked — the caller can delete the temp capture right after the
    /// dialog closes. Copying to the clipboard happens here (a UI concern); saving only collects
    /// the destination path and lets the caller perform the file copy.
    /// </summary>
    internal sealed class ScreenshotDialog : Form
    {
        private readonly Image _image;

        public ScreenshotAction Action { get; private set; }

        /// <summary>Destination chosen in "Guardar como"; set only when <see cref="Action"/> is Saved.</summary>
        public string SavedPath { get; private set; }

        public ScreenshotDialog(string title, string imagePath)
        {
            Action = ScreenshotAction.None;

            // Decouple the bitmap from the file so the file isn't locked by the PictureBox.
            using (var ms = new MemoryStream(File.ReadAllBytes(imagePath)))
            using (var loaded = Image.FromStream(ms))
            {
                _image = new Bitmap(loaded);
            }

            Text = title;
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox = false;
            MaximizeBox = true;
            ShowIcon = false;
            ShowInTaskbar = false;
            TopMost = true;
            ClientSize = new Size(820, 560);
            MinimumSize = new Size(440, 340);

            var preview = new PictureBox
            {
                Dock = DockStyle.Fill,
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.FromArgb(40, 40, 40),
                Image = _image
            };

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(8),
                Height = 54
            };

            // RightToLeft flow: first added sits rightmost, so add close -> copy -> save to get
            // the visual order [Guardar como] [Copiar] [Cerrar].
            var close = new Button
            {
                Text = "Cerrar",
                AutoSize = true,
                Margin = new Padding(6),
                DialogResult = DialogResult.Cancel
            };
            var copy = new Button { Text = "Copiar al portapapeles", AutoSize = true, Margin = new Padding(6) };
            var save = new Button { Text = "Guardar como...", AutoSize = true, Margin = new Padding(6) };

            copy.Click += OnCopy;
            save.Click += OnSave;

            buttons.Controls.Add(close);
            buttons.Controls.Add(copy);
            buttons.Controls.Add(save);

            Controls.Add(preview);
            Controls.Add(buttons);

            CancelButton = close;
        }

        private void OnCopy(object sender, EventArgs e)
        {
            try
            {
                Clipboard.SetImage(_image);
                Action = ScreenshotAction.Copied;
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "No se pudo copiar al portapapeles:\n" + ex.Message,
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void OnSave(object sender, EventArgs e)
        {
            using (var sfd = new SaveFileDialog())
            {
                sfd.Title = "Guardar captura";
                sfd.Filter = "Imagen PNG (*.png)|*.png";
                sfd.DefaultExt = "png";
                sfd.AddExtension = true;
                sfd.FileName = "captura.png";

                if (sfd.ShowDialog(this) != DialogResult.OK)
                {
                    return; // user cancelled the save dialog -> keep the preview open
                }

                SavedPath = sfd.FileName;
                Action = ScreenshotAction.Saved;
                DialogResult = DialogResult.OK;
                Close();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _image != null)
            {
                _image.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
