using System.Drawing;
using System.Windows.Forms;

namespace VeradeAddin.UI
{
    /// <summary>
    /// "Exportar" step 2: builds the output file name as [prefix] + name + [suffix]. The real base
    /// name (e.g. "1720.020.000") is shown fixed in the middle, with a textbox on each side. Either
    /// side may be left empty, so the user can add a prefix, a suffix, both, or nothing. Includes a
    /// live preview using the actual name.
    /// </summary>
    internal sealed class AffixDialog : Form
    {
        private readonly string _baseName;
        private readonly TextBox _prefix;
        private readonly TextBox _suffix;
        private readonly Label _preview;

        public string PrefixText { get { return _prefix.Text.Trim(); } }
        public string SuffixText { get { return _suffix.Text.Trim(); } }

        public AffixDialog(string baseName)
        {
            _baseName = string.IsNullOrEmpty(baseName) ? "nombredelarchivo" : baseName;

            Text = "Prefijo / Sufijo";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowIcon = false;
            ShowInTaskbar = false;
            TopMost = true;
            ClientSize = new Size(500, 230);

            var header = new Label
            {
                Text = "Añada un prefijo (izquierda) y/o un sufijo (derecha) al nombre del archivo. " +
                       "Deje un lado vacío para no añadir nada.",
                AutoSize = false,
                Location = new Point(16, 14),
                Size = new Size(468, 36)
            };

            var prefixCaption = new Label { Text = "Prefijo", AutoSize = true, Location = new Point(20, 58) };
            var suffixCaption = new Label { Text = "Sufijo", AutoSize = true, Location = new Point(330, 58) };

            _prefix = new TextBox { Location = new Point(20, 80), Size = new Size(150, 24) };
            _suffix = new TextBox { Location = new Point(330, 80), Size = new Size(150, 24) };

            var nameLabel = new Label
            {
                Text = _baseName,
                AutoSize = false,
                AutoEllipsis = true,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                ForeColor = Color.DimGray,
                Location = new Point(174, 80),
                Size = new Size(152, 24)
            };

            _preview = new Label
            {
                AutoSize = false,
                AutoEllipsis = true,
                ForeColor = Color.SteelBlue,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point(20, 120),
                Size = new Size(460, 24)
            };

            _prefix.TextChanged += (s, e) => UpdatePreview();
            _suffix.TextChanged += (s, e) => UpdatePreview();

            var ok = new Button
            {
                Text = "Exportar",
                DialogResult = DialogResult.OK,
                Location = new Point(308, 186),
                Size = new Size(85, 28)
            };
            var cancel = new Button
            {
                Text = "Cancelar",
                DialogResult = DialogResult.Cancel,
                Location = new Point(399, 186),
                Size = new Size(85, 28)
            };

            Controls.Add(header);
            Controls.Add(prefixCaption);
            Controls.Add(suffixCaption);
            Controls.Add(_prefix);
            Controls.Add(nameLabel);
            Controls.Add(_suffix);
            Controls.Add(_preview);
            Controls.Add(ok);
            Controls.Add(cancel);

            AcceptButton = ok;
            CancelButton = cancel;

            UpdatePreview();
        }

        private void UpdatePreview()
        {
            _preview.Text = "Ejemplo: " + _prefix.Text.Trim() + _baseName + _suffix.Text.Trim() + ".pdf";
        }
    }
}
