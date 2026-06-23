using System.Collections.Generic;
using VeradeAddin.Logging;
using VeradeAddin.Models;
using VeradeAddin.Services;

namespace VeradeAddin.Commands
{
    /// <summary>
    /// "Extraer pieza": frees the selected component that got buried inside the assembly and
    /// moves it out. If the component is fixed it removes the fix; if it has position mates it
    /// suppresses them; in both cases the user is warned first. Then it translates the component
    /// past the assembly bounding box and zooms to fit.
    ///
    /// Pure dispatch + logging; all SolidWorks work goes through <see cref="ISolidWorksService"/>.
    /// </summary>
    public sealed class ExtraerPiezaCommand : ICommand
    {
        private readonly ISolidWorksService _sw;
        private readonly IDialogService _dialog;
        private readonly ILogger _log;

        public ExtraerPiezaCommand(ISolidWorksService sw, IDialogService dialog, ILogger log)
        {
            _sw = sw;
            _dialog = dialog;
            _log = log;
        }

        public string Name { get { return "Liberar componente"; } }
        public string Tooltip { get { return "Libera y mueve el componente seleccionado fuera del ensamblaje"; } }
        public string Hint { get { return "Quita el fijo y suprime las relaciones del componente seleccionado, lo traslada fuera del ensamblaje y ajusta el zoom"; } }
        public CommandIcon Icon { get { return CommandIcon.Extract; } }

        public IReadOnlyList<DocumentKind> DocumentTypes
        {
            get { return new[] { DocumentKind.Assembly }; }
        }

        public bool CanExecute()
        {
            return _sw.IsComponentSelectedInActiveAssembly();
        }

        public void Execute()
        {
            const string docType = "Assembly";

            var plan = _sw.InspectSelectedComponentForExtraction();
            if (plan == null)
            {
                _log.Log(Name, docType, LogOutcome.Cancel, "No component selected");
                _dialog.ShowMessage(Name, "Seleccione un componente dentro del ensamblaje.");
                return;
            }

            // Only top-level components can be extracted; a nested one must come out of its subassembly first.
            if (plan.IsInsideSubassembly)
            {
                _log.Log(Name, docType, LogOutcome.Cancel,
                    "Component is inside a subassembly: " + plan.ComponentName);
                _dialog.ShowMessage(Name,
                    "'" + plan.ComponentName + "' está dentro de un subensamblaje.\n\n" +
                    "Solo se pueden extraer componentes de primer nivel, así que la operación se ha cancelado.");
                return;
            }

            // A pattern/mirror instance cannot be moved without breaking the pattern -> warn, abort.
            if (plan.IsPatternInstance)
            {
                _log.Log(Name, docType, LogOutcome.Cancel,
                    "Component is a pattern/mirror instance: " + plan.ComponentName);
                _dialog.ShowMessage(Name,
                    "'" + plan.ComponentName + "' es una instancia de una matriz o simetría.\n\n" +
                    "Moverla rompería la matriz, así que la operación se ha cancelado.");
                return;
            }

            var message = "Se va a extraer '" + plan.ComponentName + "':\n\n";
            if (plan.IsFixed)
            {
                message += "• Está FIJA → se quitará el fijo.\n";
            }
            if (plan.MateCount > 0)
            {
                message += "• Tiene " + plan.MateCount + " relación(es) de posición → se suprimirán.\n";
            }
            if (!plan.IsFixed && plan.MateCount == 0)
            {
                message += "• Sin fijo ni relaciones de posición.\n";
            }
            message += "\nLuego se moverá fuera del ensamblaje y se ajustará el zoom.\n\n¿Continuar?";

            if (!_dialog.Confirm(Name, message))
            {
                _log.Log(Name, docType, LogOutcome.Cancel, "User declined extraction of '" + plan.ComponentName + "'");
                return;
            }

            var result = _sw.ExtractSelectedComponent();
            if (result.Success)
            {
                _log.Log(Name, docType, LogOutcome.Success,
                    "Extracted '" + result.ComponentName + "' (wasFixed=" + result.WasFixed +
                    ", matesSuppressed=" + result.MatesSuppressed + ", moved=" + result.Moved + ")");
                _dialog.ShowMessage(Name,
                    "'" + result.ComponentName + "' extraída.\n" +
                    "Fijo quitado: " + (result.WasFixed ? "sí" : "no") + "\n" +
                    "Relaciones suprimidas: " + result.MatesSuppressed);
            }
            else
            {
                _log.Log(Name, docType, LogOutcome.Error, "Extraction failed", result.Error);
                _dialog.ShowMessage(Name, "No se pudo extraer el componente:\n" + result.Error);
            }
        }
    }
}
