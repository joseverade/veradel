using System.Drawing;
using System.Windows.Forms;
using VeradeAddin.Models;

namespace VeradeAddin.UI
{
    /// <summary>
    /// "Exportar" step 1: a modal dialog with one checkbox per format (PDF / DWG / STEP). STEP is
    /// disabled — with the reason shown — when the referenced model can't be exported safely
    /// (no model, or an assembly with more than 10 components).
    /// </summary>
    internal sealed class ExportOptionsDialog : Form
    {
        private readonly CheckBox _pdf;
        private readonly CheckBox _dwg;
        private readonly CheckBox _step;

        public bool Pdf { get { return _pdf.Checked; } }
        public bool Dwg { get { return _dwg.Checked; } }
        public bool Step { get { return _step.Checked && _step.Enabled; } }

        public ExportOptionsDialog(DrawingExportInfo info)
        {
            Text = "Exportar";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowIcon = false;
            ShowInTaskbar = false;
            TopMost = true;
            ClientSize = new Size(420, 250);

            var header = new Label
            {
                Text = "Seleccione los formatos a exportar:",
                AutoSize = true,
                Location = new Point(16, 16)
            };

            _pdf = new CheckBox { Text = "PDF (dibujo)", AutoSize = true, Checked = true, Location = new Point(24, 48) };
            _dwg = new CheckBox { Text = "DWG (dibujo)", AutoSize = true, Checked = true, Location = new Point(24, 76) };
            _step = new CheckBox { Text = "STEP (modelo 3D)", AutoSize = true, Checked = true, Location = new Point(24, 104) };

            string stepDetail;
            if (info != null && info.StepAllowed)
            {
                _step.Enabled = true;
                stepDetail = info.ModelKind == DocumentKind.Assembly
                    ? "Ensamblaje con " + info.ComponentCount + " componente(s)."
                    : "Pieza.";
            }
            else
            {
                _step.Enabled = false;
                _step.Checked = false;
                stepDetail = info != null && !string.IsNullOrEmpty(info.StepBlockedReason)
                    ? info.StepBlockedReason
                    : "STEP no disponible.";
            }

            var stepNote = new Label
            {
                Text = stepDetail,
                AutoSize = false,
                ForeColor = _step.Enabled ? Color.DimGray : Color.Firebrick,
                Location = new Point(44, 128),
                Size = new Size(360, 34)
            };

            var ok = new Button
            {
                Text = "Siguiente",
                DialogResult = DialogResult.OK,
                Location = new Point(228, 206),
                Size = new Size(85, 28)
            };
            var cancel = new Button
            {
                Text = "Cancelar",
                DialogResult = DialogResult.Cancel,
                Location = new Point(319, 206),
                Size = new Size(85, 28)
            };

            ok.Click += (s, e) =>
            {
                if (!Pdf && !Dwg && !Step)
                {
                    MessageBox.Show(this, "Seleccione al menos un formato.", Text,
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    DialogResult = DialogResult.None;
                }
            };

            Controls.Add(header);
            Controls.Add(_pdf);
            Controls.Add(_dwg);
            Controls.Add(_step);
            Controls.Add(stepNote);
            Controls.Add(ok);
            Controls.Add(cancel);

            AcceptButton = ok;
            CancelButton = cancel;
        }
    }
}
