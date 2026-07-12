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
    /// normally. The DIN 471 table is injected as <c>window.DIN471</c> before the page scripts run;
    /// when re-editing a registered part, the stored message is injected as <c>window.PRELOAD</c>
    /// so the page reopens the wizard with the saved values. On submit the page posts a
    /// pipe-delimited message parsed into a <see cref="PartConfiguratorSelection"/>:
    /// <c>create|shaft|wiz|...</c> where wiz is the wizard mode ('S' = shaft, 'B' = bolt — a
    /// 2-level shaft). Cancel: <c>cancel</c>.
    /// </summary>
    internal sealed class PartConfiguratorDialog : Form
    {
        private readonly WebView2 _web;
        private readonly string _preloadMessage;

        public PartConfiguratorSelection Selection { get; private set; }

        public PartConfiguratorDialog(string preloadMessage = null)
        {
            _preloadMessage = preloadMessage;
            Text = "Configurar pieza";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox = false;
            MaximizeBox = true;
            ShowIcon = false;
            ShowInTaskbar = false;
            TopMost = true;
            // Casi toda el área de trabajo del monitor actual: los pasos del asistente deben verse
            // completos sin barras de desplazamiento internas.
            var work = Screen.FromPoint(Cursor.Position).WorkingArea;
            ClientSize = new Size(
                Math.Min(1600, work.Width - 60),
                Math.Min(1000, work.Height - 80));
            MinimumSize = new Size(1000, 640);
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

                if (!string.IsNullOrEmpty(_preloadMessage))
                {
                    await _web.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(
                        "window.PRELOAD = '" + JsEscape(_preloadMessage) + "';");
                }

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

            if (parts[1] == "shaft")
            {
                HandleShaft(parts, message);
            }
        }

        // create|shaft|wiz|n|d1|l1|...|dn|ln|K|<keyways×9>|G|<grooves×4>|U|<undercuts×6>|C|<centres×15>|T|<threads×4>
        //             |F|<fillets, variable>|H|<chamfers, variable>
        // (wiz = wizard mode: 'S' full shaft wizard, 'B' bolt — a fixed 2-level shaft.)
        // (K keyways × 9 values — ctr: 0 = position by off, 1/2 = LEFT/RIGHT arc CENTRE on the edge
        //  (off ignored, l = centre→opposite extreme) —, G retaining-ring grooves × 4 values,
        //  U DIN 509 undercuts × 6 values — form: "E" | "F", t2 only meaningful for F,
        //  C DIN 332 centre points × 15 values: end|form|d1|d2|d3|d4|d5|b|R|t|t1|t2|t3|t4|t5 —
        //  form: "A"|"B"|"C"|"R"|"D"|"DR"|"DS" —,
        //  T cosmetic threads × 4 values: level(0-based)|side(0 left/1 right)|pitch|depth — depth
        //  0 = the whole level —,
        //  F fillet groups, VARIABLE length: radius|m|corner1|...|cornerm (m ≥ 1 corner ids,
        //  corner = 2·levelIndex + side with side 0 = the level's left vertex, 1 = right),
        //  H 45° chamfer groups, VARIABLE length: length|m|corner1|...|cornerm)
        private void HandleShaft(string[] parts, string rawMessage)
        {
            if (parts.Length < 6) return;
            string wiz = parts[2];
            if (wiz != "S" && wiz != "B") return;
            if (!int.TryParse(parts[3], out int n) || n < 1) return;
            if (wiz == "B" && n != 2) return;
            int keyBase = 4 + 2 * n;
            if (parts.Length < keyBase + 1) return;

            var spec = new ShaftSpec();
            for (int i = 0; i < n; i++)
            {
                if (!TryParse(parts[4 + 2 * i], out double d) || !TryParse(parts[5 + 2 * i], out double l))
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

            // C center points × 15 values:
            //   end|form|d1|d2|d3|d4|d5|b|R|t|t1|t2|t3|t4|t5   (form: A|B|C|R|D|DR|DS).
            if (!int.TryParse(parts[chBase], out int chCount) || chCount < 0) return;
            int thBase = chBase + 1 + 15 * chCount;
            if (parts.Length < thBase + 1) return;
            for (int c = 0; c < chCount; c++)
            {
                int p = chBase + 1 + 15 * c;
                string form = parts[p + 1];
                if (!int.TryParse(parts[p], out int end) || (end != 0 && end != 1) ||
                    (form != "A" && form != "B" && form != "C" && form != "R" &&
                     form != "D" && form != "DR" && form != "DS") ||
                    !TryParse(parts[p + 2], out double cd1) || !TryParse(parts[p + 3], out double cd2) ||
                    !TryParse(parts[p + 4], out double cd3) || !TryParse(parts[p + 5], out double cd4) ||
                    !TryParse(parts[p + 6], out double cd5) || !TryParse(parts[p + 7], out double cb) ||
                    !TryParse(parts[p + 8], out double crr) || !TryParse(parts[p + 9], out double ct) ||
                    !TryParse(parts[p + 10], out double ct1) || !TryParse(parts[p + 11], out double ct2) ||
                    !TryParse(parts[p + 12], out double ct3) || !TryParse(parts[p + 13], out double ct4) ||
                    !TryParse(parts[p + 14], out double ct5))
                {
                    return;
                }
                spec.CenterHoles.Add(new ShaftCenterHole
                {
                    End = end,
                    Form = form,
                    D1Mm = cd1,
                    D2Mm = cd2,
                    D3Mm = cd3,
                    D4Mm = cd4,
                    D5Mm = cd5,
                    BMm = cb,
                    RadiusMm = crr,
                    TMm = ct,
                    T1Mm = ct1,
                    T2Mm = ct2,
                    T3Mm = ct3,
                    T4Mm = ct4,
                    T5Mm = ct5
                });
            }

            // T cosmetic threads × 4 values: level|side|pitch|depth (depth 0 = whole level).
            if (!int.TryParse(parts[thBase], out int thCount) || thCount < 0) return;
            int pos = thBase + 1 + 4 * thCount;
            if (parts.Length < pos + 1) return;
            for (int t = 0; t < thCount; t++)
            {
                int p = thBase + 1 + 4 * t;
                if (!int.TryParse(parts[p], out int tLvl) ||
                    !int.TryParse(parts[p + 1], out int tSide) || (tSide != 0 && tSide != 1) ||
                    !TryParse(parts[p + 2], out double tPitch) || !TryParse(parts[p + 3], out double tDepth))
                {
                    return;
                }
                spec.Threads.Add(new ShaftThread
                {
                    LevelIndex = tLvl,
                    FromRight = tSide,
                    PitchMm = tPitch,
                    DepthMm = tDepth
                });
            }

            // F fillet groups, VARIABLE length each: radius|m|corner1|...|cornerm.
            if (!int.TryParse(parts[pos], out int filCount) || filCount < 0) return;
            pos++;
            for (int f = 0; f < filCount; f++)
            {
                if (parts.Length < pos + 2) return;
                if (!TryParse(parts[pos], out double fr) ||
                    !int.TryParse(parts[pos + 1], out int fm) || fm < 1)
                {
                    return;
                }
                pos += 2;
                if (parts.Length < pos + fm) return;
                var fillet = new ShaftFillet { RadiusMm = fr };
                for (int e = 0; e < fm; e++)
                {
                    if (!int.TryParse(parts[pos + e], out int fb)) return;
                    fillet.Corners.Add(fb);
                }
                pos += fm;
                spec.Fillets.Add(fillet);
            }

            // H 45° chamfer groups, VARIABLE length each: length|m|corner1|...|cornerm.
            if (parts.Length < pos + 1) return;
            if (!int.TryParse(parts[pos], out int chmCount) || chmCount < 0) return;
            pos++;
            for (int c = 0; c < chmCount; c++)
            {
                if (parts.Length < pos + 2) return;
                if (!TryParse(parts[pos], out double cl) ||
                    !int.TryParse(parts[pos + 1], out int cm) || cm < 1)
                {
                    return;
                }
                pos += 2;
                if (parts.Length < pos + cm) return;
                var chamfer = new ShaftChamfer { LengthMm = cl };
                for (int e = 0; e < cm; e++)
                {
                    if (!int.TryParse(parts[pos + e], out int cb)) return;
                    chamfer.Corners.Add(cb);
                }
                pos += cm;
                spec.Chamfers.Add(chamfer);
            }
            if (parts.Length != pos) return;

            if (!spec.IsValid)
            {
                return; // page should not have allowed this
            }

            Selection = new PartConfiguratorSelection
            {
                Part = wiz == "B" ? ConfigurablePart.Bolt : ConfigurablePart.Shaft,
                Shaft = spec,
                RawMessage = rawMessage
            };
            DialogResult = DialogResult.OK;
            Close();
        }

        private static bool TryParse(string s, out double value)
        {
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        // Escapes a string for embedding inside a single-quoted JS literal.
        private static string JsEscape(string s)
        {
            return s.Replace("\\", "\\\\").Replace("'", "\\'")
                    .Replace("\r", "\\r").Replace("\n", "\\n");
        }
    }
}
