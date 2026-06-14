using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using VeradeAddin.Models;

namespace VeradeAddin.UI
{
    /// <summary>
    /// "Configurar pieza" catalog + configurator. A plain WinForms <see cref="Form"/> is only the
    /// window frame; the whole UI is modern HTML/CSS/JS rendered by an embedded WebView2 (Evergreen
    /// runtime). The page shows a catalog of generable parts; picking one opens its configurator
    /// (a live dimensioned SVG on the left, manual dimension inputs on the right). On submit it posts
    /// a pipe-delimited message back to the host, parsed into a <see cref="PartConfiguratorSelection"/>.
    /// Bolt message: <c>create|bolt|d1|l1|d2|l2</c>. Cancel: <c>cancel</c>.
    /// </summary>
    internal sealed class PartConfiguratorDialog : Form
    {
        private readonly WebView2 _web;

        public PartConfiguratorSelection Selection { get; private set; }

        public PartConfiguratorDialog()
        {
            Text = "Configurar pieza";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowIcon = false;
            ShowInTaskbar = false;
            TopMost = true;
            ClientSize = new Size(780, 480);
            BackColor = Color.FromArgb(0xDA, 0xDD, 0xD8);

            _web = new WebView2 { Dock = DockStyle.Fill };
            Controls.Add(_web);

            Load += OnLoadAsync;
        }

        private async void OnLoadAsync(object sender, EventArgs e)
        {
            try
            {
                string userData = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "VeradeAddin", "WebView2");
                Directory.CreateDirectory(userData);

                var env = await CoreWebView2Environment.CreateAsync(null, userData);
                await _web.EnsureCoreWebView2Async(env);

                var settings = _web.CoreWebView2.Settings;
                settings.AreDefaultContextMenusEnabled = false;
                settings.AreDevToolsEnabled = false;
                settings.IsStatusBarEnabled = false;
                settings.AreBrowserAcceleratorKeysEnabled = false;

                _web.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
                _web.CoreWebView2.NavigateToString(PartConfiguratorHtml.Page);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this,
                    "No se pudo iniciar la interfaz (WebView2).\n\n" +
                    "Asegúrese de tener instalado el runtime de Microsoft Edge WebView2.\n\n" +
                    ex.Message,
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                DialogResult = DialogResult.Cancel;
                Close();
            }
        }

        private void OnWebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            string message;
            try { message = e.TryGetWebMessageAsString(); }
            catch { message = null; }

            if (string.IsNullOrEmpty(message))
            {
                return;
            }

            if (message == "cancel")
            {
                DialogResult = DialogResult.Cancel;
                Close();
                return;
            }

            var parts = message.Split('|');
            if (parts.Length < 2 || parts[0] != "create")
            {
                return;
            }

            if (parts[1] == "bolt")
            {
                HandleBolt(parts);
            }
        }

        // create|bolt|d1|l1|d2|l2  (decimals use '.', produced by JS)
        private void HandleBolt(string[] parts)
        {
            if (parts.Length != 6) return;

            if (TryParse(parts[2], out double d1) && TryParse(parts[3], out double l1) &&
                TryParse(parts[4], out double d2) && TryParse(parts[5], out double l2))
            {
                var spec = new BoltSpec
                {
                    HeadDiameterMm = d1,
                    HeadLengthMm = l1,
                    ShankDiameterMm = d2,
                    ShankLengthMm = l2
                };

                if (!spec.IsValid)
                {
                    return; // page should not have allowed this
                }

                Selection = new PartConfiguratorSelection { Part = ConfigurablePart.Bolt, Bolt = spec };
                DialogResult = DialogResult.OK;
                Close();
            }
        }

        private static bool TryParse(string s, out double value)
        {
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }
    }
}
