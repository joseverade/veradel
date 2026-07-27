using System.Collections.Generic;
using VeradeAddin.Logging;
using VeradeAddin.Models;
using VeradeAddin.Services;

namespace VeradeAddin.Commands
{
    /// <summary>
    /// "Identificar rosca / taladro": a TOGGLE button (assembly level). Activating it enters a
    /// pick-mode — the button stays pressed and, every time the user clicks a hole's circular edge
    /// (or cylindrical face), a Measure-style callout appears in the graphics area with the thread
    /// designation and diameters (including the min/max of countersink/counterbore holes). Clicking
    /// the button again leaves the mode.
    ///
    /// Pure dispatch + logging; the pick-mode session and all SolidWorks work live in
    /// <see cref="ISolidWorksService"/>.
    /// </summary>
    public sealed class IdentificarRoscaCommand : IToggleCommand
    {
        private readonly ISolidWorksService _sw;
        private readonly IDialogService _dialog;
        private readonly ILogger _log;

        public IdentificarRoscaCommand(ISolidWorksService sw, IDialogService dialog, ILogger log)
        {
            _sw = sw;
            _dialog = dialog;
            _log = log;
        }

        public string Name { get { return "Identificar rosca"; } }
        public string Tooltip { get { return "Identificar rosca / taladro (Ensamblaje)"; } }
        public string Hint { get { return "Actívalo y haz clic en el borde de un taladro para ver su rosca y diámetros; púlsalo de nuevo para salir"; } }
        public CommandIcon Icon { get { return CommandIcon.Measure; } }

        public IReadOnlyList<DocumentKind> DocumentTypes
        {
            get { return new[] { DocumentKind.Assembly }; }
        }

        /// <summary>Always available in the assembly tab (it's a mode toggle, not a one-shot action).</summary>
        public bool CanExecute()
        {
            return true;
        }

        /// <summary>Drives the pressed state of the ribbon button.</summary>
        public bool IsActive
        {
            get { return _sw.IsHolePickModeActive(); }
        }

        public void Execute()
        {
            bool active = _sw.ToggleHolePickMode();

            if (active)
            {
                // Started: nothing else to do — the callout appears as the user clicks holes.
                return;
            }

            // If it did NOT turn on and is not active either, it never started (no active assembly).
            if (!_sw.IsHolePickModeActive())
            {
                // Distinguish "just turned off" from "could not start": only warn on the latter.
                // ToggleHolePickMode returns false in both cases, so re-check is not enough; the
                // service logs the reason. Show a hint only when there is no assembly to work on.
                var doc = _sw.GetActiveDocument();
                if (doc == null || doc.Kind != DocumentKind.Assembly)
                {
                    _dialog.ShowMessage(Name, "Abra un ensamblaje para usar esta herramienta.");
                }
            }
        }
    }
}
