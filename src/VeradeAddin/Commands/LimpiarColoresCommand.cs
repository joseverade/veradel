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
        public string Hint { get { return "Restaura el color por defecto (negro) de las aristas de todas las vistas del dibujo activo"; } }
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
                "Esto pondrá en NEGRO el color de todas las aristas de las vistas del dibujo.\n\n¿Continuar?"))
            {
                _log.Log(Name, docType, LogOutcome.Cancel, "User declined");
                return;
            }

            var result = _sw.ClearEdgeColors();

            if (!string.IsNullOrEmpty(result.Error))
            {
                _log.Log(Name, docType, LogOutcome.Error, "Clear failed", result.Error);
                _dialog.ShowMessage(Name, "No se pudieron limpiar los colores:\n" + result.Error);
                return;
            }

            _log.Log(Name, docType, LogOutcome.Success, "Reset " + result.EdgesColored + " edge(s)");
            _dialog.ShowMessage(Name, "Aristas restauradas a negro: " + result.EdgesColored);
        }
    }
}
