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
            ClientSize = new Size(1200, 740);
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
            else if (parts[1] == "shaft")
            {
                HandleShaft(parts);
            }
        }

        // create|shaft|n|d1|l1|...|dn|ln|K|<keyways×9>|G|<grooves×4>|U|<undercuts×6>|C|<centres×9>
        // (K keyways × 9 values — ctr: 0 = position by off, 1/2 = LEFT/RIGHT arc CENTRE on the edge
        //  (off ignored, l = centre→opposite extreme) —, G retaining-ring grooves × 4 values,
        //  U DIN 509 undercuts × 6 values — form: "E" | "F", t2 only meaningful for F,
        //  C DIN 332 centre points × 9 values: end|form|d1|d2|d3|lp|rarc|lc|thread — form:
        //  "A"|"B"|"R"|"D"; d3 only for B/D, rarc only for R, lc/thread only for D)
        private void HandleShaft(string[] parts)
        {
            if (parts.Length < 5) return;
            if (!int.TryParse(parts[2], out int n) || n < 1) return;
            int keyBase = 3 + 2 * n;
            if (parts.Length < keyBase + 1) return;

            var spec = new ShaftSpec();
            for (int i = 0; i < n; i++)
            {
                if (!TryParse(parts[3 + 2 * i], out double d) || !TryParse(parts[4 + 2 * i], out double l))
                {
                    return;
                }
                spec.Levels.Add(new ShaftLevel { DiameterMm = d, LengthMm = l });
            }

            if (!int.TryParse(parts[keyBase], out int keyCount) || keyCount < 0) return;
            int grooveBase = keyBase + 1 + 9 * keyCount;
            if (parts.Length < grooveBase + 1) return;
            for (int k = 0; k < keyCount; k++)
            {
                int p = keyBase + 1 + 9 * k;
                if (!TryParse(parts[p], out double b) || !TryParse(parts[p + 1], out double kl) ||
                    !int.TryParse(parts[p + 2], out int edge) || !TryParse(parts[p + 3], out double off) ||
                    !TryParse(parts[p + 4], out double depth) || !TryParse(parts[p + 5], out double refd) ||
                    !TryParse(parts[p + 6], out double ang) || !int.TryParse(parts[p + 7], out int cnt) ||
                    !int.TryParse(parts[p + 8], out int ctr) || ctr < 0 || ctr > 2)
                {
                    return;
                }
                spec.Keyways.Add(new ShaftKeyway
                {
                    WidthMm = b,
                    LengthMm = kl,
                    EdgeIndex = edge,
                    OffsetMm = off,
                    DepthMm = depth,
                    RefDiameterMm = refd,
                    AngleDeg = ang,
                    Count = cnt,
                    CenterArc = ctr
                });
            }

            if (!int.TryParse(parts[grooveBase], out int grooveCount) || grooveCount < 0) return;
            int ucBase = grooveBase + 1 + 4 * grooveCount;
            if (parts.Length < ucBase + 1) return;
            for (int g = 0; g < grooveCount; g++)
            {
                int p = grooveBase + 1 + 4 * g;
                if (!TryParse(parts[p], out double e1) || !TryParse(parts[p + 1], out double d3) ||
                    !int.TryParse(parts[p + 2], out int gEdge) || !TryParse(parts[p + 3], out double gOff))
                {
                    return;
                }
                spec.Grooves.Add(new ShaftGroove
                {
                    WidthMm = e1,
                    BottomDiameterMm = d3,
                    EdgeIndex = gEdge,
                    OffsetMm = gOff
                });
            }

            if (!int.TryParse(parts[ucBase], out int ucCount) || ucCount < 0) return;
            int chBase = ucBase + 1 + 6 * ucCount;
            if (parts.Length < chBase + 1) return;
            for (int u = 0; u < ucCount; u++)
            {
                int p = ucBase + 1 + 6 * u;
                string form = parts[p + 1];
                if (!int.TryParse(parts[p], out int bnd) || (form != "E" && form != "F") ||
                    !TryParse(parts[p + 2], out double ur) || !TryParse(parts[p + 3], out double ut1) ||
                    !TryParse(parts[p + 4], out double uf) || !TryParse(parts[p + 5], out double ut2))
                {
                    return;
                }
                spec.Undercuts.Add(new ShaftUndercut
                {
                    BoundaryIndex = bnd,
                    Form = form,
                    RadiusMm = ur,
                    DepthMm = ut1,
                    WidthMm = uf,
                    Depth2Mm = ut2
                });
            }

            // C center points × 9 values: end|form|d1|d2|d3|lp|rarc|lc|thread (form: "A"|"B"|"R"|"D").
            if (!int.TryParse(parts[chBase], out int chCount) || chCount < 0) return;
            if (parts.Length != chBase + 1 + 9 * chCount) return;
            for (int c = 0; c < chCount; c++)
            {
                int p = chBase + 1 + 9 * c;
                string form = parts[p + 1];
                if (!int.TryParse(parts[p], out int end) || (end != 0 && end != 1) ||
                    (form != "A" && form != "B" && form != "R" && form != "D") ||
                    !TryParse(parts[p + 2], out double d1) || !TryParse(parts[p + 3], out double d2) ||
                    !TryParse(parts[p + 4], out double d3) || !TryParse(parts[p + 5], out double clp) ||
                    !TryParse(parts[p + 6], out double crArc) || !TryParse(parts[p + 7], out double clc) ||
                    !TryParse(parts[p + 8], out double cthr))
                {
                    return;
                }
                spec.CenterHoles.Add(new ShaftCenterHole
                {
                    End = end,
                    Form = form,
                    PilotDiameterMm = d1,
                    CountersinkDiameterMm = d2,
                    ProtectDiameterMm = d3,
                    PilotDepthMm = clp,
                    ArcRadiusMm = crArc,
                    CounterboreDepthMm = clc,
                    ThreadDiameterMm = cthr
                });
            }

            if (!spec.IsValid)
            {
                return; // page should not have allowed this
            }

            Selection = new PartConfiguratorSelection { Part = ConfigurablePart.Shaft, Shaft = spec };
            DialogResult = DialogResult.OK;
            Close();
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
