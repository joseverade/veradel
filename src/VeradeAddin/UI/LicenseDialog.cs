using System;
using System.Drawing;
using System.Windows.Forms;
using VeradeAddin.Licensing;

namespace VeradeAddin.UI
{
    /// <summary>
    /// Activation dialog shown when the trial is over (or on demand): current license status,
    /// a box to paste a license key and a purchase contact link. Activation itself happens in
    /// <see cref="ILicenseService.TryActivate"/>; this form only presents the result.
    /// </summary>
    public sealed class LicenseDialog : Form
    {
        private const string PurchaseEmail = "josemverade@gmail.com";

        private readonly ILicenseService _license;
        private readonly Label _status;
        private readonly TextBox _key;

        public LicenseDialog(ILicenseService license)
        {
            _license = license;

            Text = "Veradel — Licencia";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(440, 260);

            _status = new Label
            {
                AutoSize = false,
                Dock = DockStyle.Top,
                Height = 56,
                Padding = new Padding(12, 12, 12, 0)
            };

            var keyLabel = new Label
            {
                Text = "Clave de licencia:",
                AutoSize = false,
                Dock = DockStyle.Top,
                Height = 22,
                Padding = new Padding(12, 4, 12, 0)
            };

            _key = new TextBox
            {
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                Dock = DockStyle.Fill,
                Font = new Font(FontFamily.GenericMonospace, 8f)
            };
            var keyPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 4, 12, 4) };
            keyPanel.Controls.Add(_key);

            var contact = new LinkLabel
            {
                Text = "Comprar una licencia: " + PurchaseEmail,
                AutoSize = false,
                Dock = DockStyle.Bottom,
                Height = 24,
                Padding = new Padding(12, 4, 12, 0)
            };
            contact.LinkClicked += (s, e) =>
            {
                try
                {
                    System.Diagnostics.Process.Start(
                        "mailto:" + PurchaseEmail + "?subject=Licencia%20Veradel");
                }
                catch { }
            };

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                FlowDirection = FlowDirection.RightToLeft,
                Height = 44,
                Padding = new Padding(12, 6, 12, 6)
            };
            var activate = new Button { Text = "Activar", Width = 90 };
            var close = new Button { Text = "Cerrar", DialogResult = DialogResult.Cancel, Width = 90 };
            activate.Click += (s, e) => ActivateLicense();
            buttons.Controls.Add(close);
            buttons.Controls.Add(activate);

            Controls.Add(keyPanel);
            Controls.Add(keyLabel);
            Controls.Add(_status);
            Controls.Add(contact);
            Controls.Add(buttons);

            AcceptButton = activate;
            CancelButton = close;

            RefreshStatus();
        }

        private void RefreshStatus()
        {
            switch (_license.Status)
            {
                case LicenseStatus.Licensed:
                    _status.Text = "Licencia activada a nombre de " + _license.LicensedTo + "." +
                        (_license.ExpiresUtc.HasValue
                            ? "\nVálida hasta el " +
                              _license.ExpiresUtc.Value.ToLocalTime().ToString("dd/MM/yyyy") + "."
                            : "") + " ¡Gracias!";
                    break;
                case LicenseStatus.Trial:
                    _status.Text = "Periodo de prueba: quedan " + _license.TrialDaysLeft +
                        " día(s).\nPuedes activar una licencia cuando quieras.";
                    break;
                case LicenseStatus.TrialExpired:
                    _status.Text = "El periodo de prueba ha terminado.\n" +
                        "Introduce una clave de licencia para seguir usando Veradel.";
                    break;
                case LicenseStatus.LicenseExpired:
                    _status.Text = "Tu licencia" +
                        (_license.ExpiresUtc.HasValue
                            ? " caducó el " +
                              _license.ExpiresUtc.Value.ToLocalTime().ToString("dd/MM/yyyy") + "."
                            : " ha caducado.") +
                        "\nRenueva e introduce la nueva clave para seguir usando Veradel.";
                    break;
                default: // Tampered
                    _status.Text = "Los datos de licencia no son válidos o el reloj del sistema " +
                        "se ha atrasado.\nCorrige la fecha del equipo o introduce una clave de licencia.";
                    break;
            }
        }

        private void ActivateLicense()
        {
            string error;
            if (_license.TryActivate(_key.Text, out error))
            {
                MessageBox.Show(this,
                    "Licencia activada a nombre de " + _license.LicensedTo + ". ¡Gracias!",
                    "Veradel", MessageBoxButtons.OK, MessageBoxIcon.Information);
                DialogResult = DialogResult.OK;
                Close();
            }
            else
            {
                MessageBox.Show(this, error, "Veradel",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }
}
