using System.Collections.Generic;
using VeradeAddin.Logging;
using VeradeAddin.Models;
using VeradeAddin.Services;

namespace VeradeAddin.Commands
{
    /// <summary>
    /// "Mostrar rosca": inserts cosmetic thread annotations into every assembly view of the
    /// active drawing's current sheet. SolidWorks does not display cosmetic threads in
    /// assembly views by default, so they must be imported. Lives in a drawing-only tab and
    /// is only enabled when the active drawing references an assembly.
    ///
    /// Pure dispatch + logging; all SolidWorks work goes through <see cref="ISolidWorksService"/>.
    /// </summary>
    public sealed class MostrarRoscaCommand : ICommand
    {
        private readonly ISolidWorksService _sw;
        private readonly IDialogService _dialog;
        private readonly ILogger _log;

        public MostrarRoscaCommand(ISolidWorksService sw, IDialogService dialog, ILogger log)
        {
            _sw = sw;
            _dialog = dialog;
            _log = log;
        }

        public string Name { get { return "Mostrar roscas cosméticas"; } }
        public string Tooltip { get { return "Mostrar roscas cosméticas (Ensamblaje)"; } }
        public string Hint { get { return "Inserta anotaciones de rosca cosmética en todas las vistas de ensamblaje de la hoja activa"; } }
        public CommandIcon Icon { get { return CommandIcon.Thread; } }

        public IReadOnlyList<DocumentKind> DocumentTypes
        {
            get { return new[] { DocumentKind.Drawing }; }
        }

        public bool CanExecute()
        {
            // Enabled only for a drawing whose current sheet references an assembly.
            return _sw.ActiveDrawingReferencesAssembly();
        }

        public void Execute()
        {
            const string docType = "Drawing";

            var result = _sw.InsertCosmeticThreadsInAssemblyViews();

            if (!result.WasDrawing)
            {
                _log.Log(Name, "None", LogOutcome.Cancel, "No active drawing");
                _dialog.ShowMessage(Name, "Debe haber un dibujo activo.");
                return;
            }

            if (result.AssemblyViewsFound == 0)
            {
                _log.Log(Name, docType, LogOutcome.Cancel, "No assembly views on active sheet");
                _dialog.ShowMessage(Name, "La hoja activa no contiene vistas de ensamblaje.");
                return;
            }

            if (result.Failed == 0)
            {
                _log.Log(Name, docType, LogOutcome.Success,
                    "Cosmetic threads inserted in " + result.Processed + " assembly view(s)");
                _dialog.ShowMessage(Name, "Roscas cosméticas añadidas en " + result.Processed + " vista(s).");
            }
            else
            {
                _log.Log(Name, docType, LogOutcome.Error,
                    "Completed with errors: processed=" + result.Processed + " failed=" + result.Failed,
                    string.Join(" | ", result.Errors));
                _dialog.ShowMessage(Name,
                    "Completado con " + result.Failed + " error(es):\n" + string.Join("\n", result.Errors));
            }
        }
    }
}
