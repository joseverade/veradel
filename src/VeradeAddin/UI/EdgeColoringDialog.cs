using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using VeradeAddin.Models;

namespace VeradeAddin.UI
{
    /// <summary>
    /// "Colorear aristas" dialog (WinForms reimplementation of the old WPF window). Lists each
    /// referenced part and the appearance colours found on it; the user ticks which source colours
    /// to carry to the edges and picks the target palette colour (pre-suggested by hue) for each.
    /// </summary>
    internal sealed class EdgeColoringDialog : Form
    {
        private static readonly PaletteColor[] Palette =
        {
            new PaletteColor("Rojo", 255, 0, 0),
            new PaletteColor("Naranja", 255, 128, 0),
            new PaletteColor("Amarillo", 255, 255, 0),
            new PaletteColor("Verde", 0, 255, 0),
            new PaletteColor("Azul", 0, 0, 255),
            new PaletteColor("Púrpura", 128, 0, 128)
        };

        private readonly List<RowControls> _rows = new List<RowControls>();

        public EdgeColorRequest Request { get; private set; }

        public EdgeColoringDialog(EdgeColoringPlan plan)
        {
            Text = "Colorear aristas";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowIcon = false;
            ShowInTaskbar = false;
            TopMost = true;
            ClientSize = new Size(560, 460);
            MinimumSize = new Size(520, 360);

            var header = new Label
            {
                Text = "Marque el color de cara a llevar a las aristas y elija el color destino:",
                Dock = DockStyle.Top,
                Padding = new Padding(10, 8, 10, 4),
                Height = 34
            };

            var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
            var flow = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Top,
                Padding = new Padding(8)
            };

            foreach (var part in plan.Parts)
            {
                flow.Controls.Add(new Label
                {
                    Text = "Pieza: " + part.PartName,
                    Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                    AutoSize = true,
                    Margin = new Padding(2, 10, 2, 4)
                });

                if (part.Colors.Count == 0)
                {
                    flow.Controls.Add(new Label { Text = "   (sin colores de apariencia)", AutoSize = true, ForeColor = Color.DimGray });
                    continue;
                }

                foreach (var color in part.Colors)
                {
                    flow.Controls.Add(BuildRow(part.PartPath, color));
                }
            }

            scroll.Controls.Add(flow);

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(8),
                Height = 50
            };
            var cancel = new Button { Text = "Cancelar", AutoSize = true, Margin = new Padding(6), DialogResult = DialogResult.Cancel };
            var ok = new Button { Text = "Aplicar", AutoSize = true, Margin = new Padding(6) };
            ok.Click += OnApply;
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(ok);

            Controls.Add(scroll);
            Controls.Add(buttons);
            Controls.Add(header);

            AcceptButton = ok;
            CancelButton = cancel;
        }

        private Panel BuildRow(string partPath, DetectedColor color)
        {
            var row = new Panel { Size = new Size(510, 30), Margin = new Padding(2) };

            var swatch = new Panel
            {
                Location = new Point(6, 4),
                Size = new Size(22, 22),
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.FromArgb(Clamp(color.R), Clamp(color.G), Clamp(color.B))
            };

            string label = string.IsNullOrEmpty(color.Name)
                ? "RGB(" + color.R + "," + color.G + "," + color.B + ")"
                : color.Name + "  (" + color.R + "," + color.G + "," + color.B + ")";

            var name = new Label { Location = new Point(36, 6), AutoSize = false, Size = new Size(230, 20), Text = label };

            var check = new CheckBox { Location = new Point(276, 6), AutoSize = true, Text = "Aplicar" };

            var combo = new ComboBox
            {
                Location = new Point(370, 4),
                Size = new Size(130, 24),
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            foreach (var p in Palette) combo.Items.Add(p.Name);
            combo.SelectedIndex = SuggestIndex(color.R, color.G, color.B);

            row.Controls.Add(swatch);
            row.Controls.Add(name);
            row.Controls.Add(check);
            row.Controls.Add(combo);

            _rows.Add(new RowControls
            {
                PartPath = partPath,
                R = color.R,
                G = color.G,
                B = color.B,
                Check = check,
                Combo = combo
            });

            return row;
        }

        private void OnApply(object sender, EventArgs e)
        {
            var request = new EdgeColorRequest();
            foreach (var r in _rows)
            {
                if (!r.Check.Checked) continue;
                int idx = r.Combo.SelectedIndex;
                if (idx < 0 || idx >= Palette.Length) continue;
                var target = Palette[idx];

                request.Mappings.Add(new EdgeColorMapping
                {
                    PartPath = r.PartPath,
                    SourceR = r.R,
                    SourceG = r.G,
                    SourceB = r.B,
                    TargetR = target.R,
                    TargetG = target.G,
                    TargetB = target.B
                });
            }

            if (request.Mappings.Count == 0)
            {
                MessageBox.Show(this, "Marque al menos un color para aplicar.", Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            Request = request;
            DialogResult = DialogResult.OK;
            Close();
        }

        private static int Clamp(int v)
        {
            return v < 0 ? 0 : (v > 255 ? 255 : v);
        }

        // Maps an RGB colour to the nearest palette bucket by hue (ported from the old SetAverage).
        private static int SuggestIndex(int r, int g, int b)
        {
            double h = Hue(r, g, b);
            if (h >= 345 || h < 15) return 0;   // Rojo
            if (h < 45) return 1;               // Naranja
            if (h < 75) return 2;               // Amarillo
            if (h < 170) return 3;              // Verde
            if (h < 285) return 4;              // Azul
            return 5;                           // Púrpura
        }

        private static double Hue(int r, int g, int b)
        {
            double rr = r / 255.0, gg = g / 255.0, bb = b / 255.0;
            double max = Math.Max(rr, Math.Max(gg, bb));
            double min = Math.Min(rr, Math.Min(gg, bb));
            double d = max - min;
            if (d <= 0) return 0;

            double h;
            if (max == rr) h = 60.0 * ((((gg - bb) / d) % 6) + 6) % 360;
            else if (max == gg) h = 60.0 * (((bb - rr) / d) + 2);
            else h = 60.0 * (((rr - gg) / d) + 4);

            if (h < 0) h += 360;
            return h;
        }

        private sealed class RowControls
        {
            public string PartPath;
            public int R;
            public int G;
            public int B;
            public CheckBox Check;
            public ComboBox Combo;
        }

        private struct PaletteColor
        {
            public readonly string Name;
            public readonly int R;
            public readonly int G;
            public readonly int B;

            public PaletteColor(string name, int r, int g, int b)
            {
                Name = name;
                R = r;
                G = g;
                B = b;
            }
        }
    }
}
