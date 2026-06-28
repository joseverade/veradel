using System.Collections.Generic;
using VeradeAddin.Logging;
using VeradeAddin.Models;
using VeradeAddin.Services;

namespace VeradeAddin.Commands
{
    /// <summary>
    /// "Limpiar colores": resets the colour of every view edge in the active drawing back to black
    /// (the default). Companion to <see cref="ColorearAristasCommand"/>.
    ///
    /// Pure dispatch + logging; all SolidWorks work goes through <see cref="ISolidWorksService"/>.
    /// </summary>
    public sealed class LimpiarColoresCommand : ICommand
    {
        private readonly ISolidWorksService _sw;
        private readonly IDialogService _dialog;
        private readonly ILogger _log;

        public LimpiarColoresCommand(ISolidWorksService sw, IDialogService dialog, ILogger log)
        {
            _sw = sw;
            _dialog = dialog;
            _log = log;
        }

        public string Name { get { return "Líneas a negro"; } }
        public string Tooltip { get { return "Líneas a negro"; } }
        public string Hint { get { return "Devuelve a negro las aristas coloreadas de la VISTA seleccionada (o TODAS si no hay registro en memoria)"; } }
        public CommandIcon Icon { get { return CommandIcon.ClearColors; } }

        public IReadOnlyList<DocumentKind> DocumentTypes
        {
            get { return new[] { DocumentKind.Drawing }; }
        }

        public bool CanExecute()
        {
            var doc = _sw.GetActiveDocument();
            return doc != null && doc.Kind == DocumentKind.Drawing;
        }

        public void Execute()
        {
            const string docType = "Drawing";

            if (!_dialog.Confirm(Name,
                "Devuelve a NEGRO las aristas que coloreaste en la VISTA de pieza seleccionada.\n\n" +
                "Si no hay registro en memoria de esta sesión (p.ej. tras reiniciar), pondrá en negro TODAS " +
                "las aristas de la vista (incluidas las ocultas).\n\n" +
                "Selecciona una vista de pieza antes de continuar.\n\n¿Continuar?"))
            {
                _log.Log(Name, docType, LogOutcome.Cancel, "User declined");
                return;
            }

            EdgeColoringResult result;
            using (_dialog.ShowWait(Name, "Poniendo las aristas en negro…"))
            {
                result = _sw.ClearEdgeColors();
            }

            if (!string.IsNullOrEmpty(result.Error))
            {
                _log.Log(Name, docType, LogOutcome.Error, "Clear failed", result.Error);
                _dialog.ShowMessage(Name, "No se pudieron limpiar los colores:\n" + result.Error);
                return;
            }

            string scope = result.RestrictedToColored ? " (solo las coloreadas)" : " (toda la vista)";
            _log.Log(Name, docType, LogOutcome.Success, "Reset " + result.EdgesColored + " edge(s)" + scope);
            _dialog.ShowMessage(Name, "Aristas restauradas a negro: " + result.EdgesColored + scope);
        }
    }
}
