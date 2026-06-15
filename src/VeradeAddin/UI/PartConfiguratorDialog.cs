using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using VeradeAddin.Models;
using VeradeAddin.Services;

namespace VeradeAddin.UI
{
    /// <summary>
    /// "Configurar pieza" catalog + configurator. A plain WinForms <see cref="Form"/> is only the
    /// window frame; the whole UI is modern HTML/CSS/JS rendered by an embedded WebView2 (Evergreen
    /// runtime). The page lives as real editable assets under <c>web\</c> next to the add-in DLL
    /// (configurator.html/.css/.js, copied at build time); the dialog serves that folder through a
    /// WebView2 virtual host (<c>https://veradeapp.local/</c>) so the HTML can reference its CSS/JS
    /// normally. The DIN 471 table is injected as <c>window.DIN471</c> before the page scripts run.
    /// On submit the page posts a pipe-delimited message parsed into a
    /// <see cref="PartConfiguratorSelection"/>: <c>create|bolt|d1|l1|d2|l2|g|p1|e1|d3|c|cang|csize</c>
    /// (g/c = groove/chamfer 1/0 flags). Cancel: <c>cancel</c>.
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

                // Serve the editable web assets (web\ next to the DLL) through a virtual host so the
                // page's relative <link>/<script> resolve, and inject the DIN 471 table before any
                // page script runs.
                string webDir = Path.Combine(
                    Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty, "web");
                _web.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    "veradeapp.local", webDir, CoreWebView2HostResourceAccessKind.Allow);

                string dinJson = Din471Table.LoadRawJson();
                await _web.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(
                    "window.DIN471 = " + dinJson + ";");

                _web.CoreWebView2.Navigate("https://veradeapp.local/configurator.html");
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

        // create|bolt|d1|l1|d2|l2|g|p1|e1|d3|c|cang|csize  (decimals use '.', produced by JS)
        private void HandleBolt(string[] parts)
        {
            if (parts.Length != 13) return;

            if (TryParse(parts[2], out double d1) && TryParse(parts[3], out double l1) &&
                TryParse(parts[4], out double d2) && TryParse(parts[5], out double l2) &&
                TryParse(parts[7], out double p1) && TryParse(parts[8], out double e1) &&
                TryParse(parts[9], out double d3) && TryParse(parts[11], out double cang) &&
                TryParse(parts[12], out double csize))
            {
                var spec = new BoltSpec
                {
                    HeadDiameterMm = d1,
                    HeadLengthMm = l1,
                    ShankDiameterMm = d2,
                    ShankLengthMm = l2,
                    HasGroove = parts[6] == "1",
                    GroovePositionMm = p1,
                    GrooveWidthMm = e1,
                    GrooveDiameterMm = d3,
                    HasChamfer = parts[10] == "1",
                    ChamferAngleDeg = cang,
                    ChamferSizeMm = csize
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
