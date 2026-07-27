using System.Collections.Generic;
using VeradeAddin.Licensing;
using VeradeAddin.Logging;
using VeradeAddin.Models;
using VeradeAddin.Services;

namespace VeradeAddin.Commands
{
    /// <summary>
    /// "Licencia": opens the activation dialog on demand, so a customer can enter a purchased
    /// key at any time (during the trial included) instead of waiting to be locked out.
    /// <see cref="Infrastructure.CompositionRoot.Execute"/> exempts this command from the
    /// license gate — it must stay reachable precisely when the add-in is blocked.
    /// </summary>
    public sealed class LicenciaCommand : ICommand
    {
        private readonly IDialogService _dialog;
        private readonly ILicenseService _license;
        private readonly ILogger _log;

        public LicenciaCommand(IDialogService dialog, ILicenseService license, ILogger log)
        {
            _dialog = dialog;
            _license = license;
            _log = log;
        }

        public string Name { get { return "Licencia"; } }
        public string Tooltip { get { return "Licencia de Veradel"; } }
        public string Hint { get { return "Ver el estado de la licencia o activar una clave comprada"; } }
        public CommandIcon Icon { get { return CommandIcon.Default; } }

        public IReadOnlyList<DocumentKind> DocumentTypes
        {
            get { return new[] { DocumentKind.Part, DocumentKind.Assembly, DocumentKind.Drawing }; }
        }

        public bool CanExecute()
        {
            return true;
        }

        public void Execute()
        {
            var before = _license.Status;
            _dialog.ShowLicense(_license);
            _log.Log(Name, "n/a", LogOutcome.Success,
                "Status " + before + " -> " + _license.Status);
        }
    }
}
