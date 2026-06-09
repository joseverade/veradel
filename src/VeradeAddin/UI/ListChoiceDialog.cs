using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace VeradeAddin.UI
{
    /// <summary>
    /// Minimal single-choice modal: a prompt label, a list box and OK/Cancel. Used for picking a
    /// configuration when the part has more than one. No business logic — just selection.
    /// </summary>
    public sealed class ListChoiceDialog : Form
    {
        private readonly ListBox _list;

        public string SelectedItem { get; private set; }

        public ListChoiceDialog(string title, string prompt, IReadOnlyList<string> options)
        {
            Text = title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(360, 300);

            var label = new Label
            {
                Text = prompt,
                AutoSize = false,
                Dock = DockStyle.Top,
                Height = 36,
                Padding = new Padding(10, 10, 10, 0)
            };

            _list = new ListBox
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(10),
                IntegralHeight = false
            };
            if (options != null)
            {
                foreach (var o in options) _list.Items.Add(o);
            }
            if (_list.Items.Count > 0) _list.SelectedIndex = 0;
            _list.DoubleClick += (s, e) => Accept();

            var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 0, 10, 0) };
            panel.Controls.Add(_list);

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                FlowDirection = FlowDirection.RightToLeft,
                Height = 44,
                Padding = new Padding(10, 6, 10, 6)
            };
            var ok = new Button { Text = "Aceptar", DialogResult = DialogResult.None, Width = 90 };
            var cancel = new Button { Text = "Cancelar", DialogResult = DialogResult.Cancel, Width = 90 };
            ok.Click += (s, e) => Accept();
            buttons.Controls.Add(ok);
            buttons.Controls.Add(cancel);

            Controls.Add(panel);
            Controls.Add(buttons);
            Controls.Add(label);

            AcceptButton = ok;
            CancelButton = cancel;
        }

        private void Accept()
        {
            SelectedItem = _list.SelectedItem as string;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
