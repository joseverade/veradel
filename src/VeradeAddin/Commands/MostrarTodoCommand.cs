using System.Collections.Generic;
using VeradeAddin.Logging;
using VeradeAddin.Models;
using VeradeAddin.Services;

namespace VeradeAddin.Commands
{
    /// <summary>
    /// "Mostrar todo" (eye): shows every hidden component of the active assembly by running SolidWorks'
    /// own "Mostrar con dependientes" on the top-level assembly. Always enabled in an assembly, whatever
    /// is selected. Lives in the ribbon ("Veradel Assembly") AND in the context toolbar (right-click on a
    /// component or on empty space); undoes <see cref="LocalizarEnVistaCommand"/>.
    ///
    /// Pure dispatch + logging; all SolidWorks work goes through <see cref="ISolidWorksService"/>.
    /// </summary>
    public sealed class MostrarTodoCommand : IContextToolbarCommand
    {
        private readonly ISolidWorksService _sw;
        private readonly IDialogService _dialog;
        private readonly ILogger _log;

        public MostrarTodoCommand(ISolidWorksService sw, IDialogService dialog, ILogger log)
        {
            _sw = sw;
            _dialog = dialog;
            _log = log;
        }

        public string Name { get { return "Mostrar todo"; } }
        public string Tooltip { get { return "Muestra todos los componentes ocultos"; } }
        public string Hint { get { return "Muestra todos los componentes ocultos del ensamblaje (Mostrar con dependientes)"; } }
        public CommandIcon Icon { get { return CommandIcon.ShowAll; } }

        public IReadOnlyList<DocumentKind> DocumentTypes
        {
            get { return new[] { DocumentKind.Assembly }; }
        }

        public IReadOnlyList<ContextToolbarTarget> ContextTargets
        {
            get { return new[] { ContextToolbarTarget.Component, ContextToolbarTarget.EmptySpace }; }
        }

        public bool CanExecute()
        {
            var doc = _sw.GetActiveDocument();
            return doc != null && doc.Kind == DocumentKind.Assembly;
        }

        public void Execute()
        {
            const string docType = "Assembly";

            var result = _sw.ShowAllComponents();
            if (!result.Success)
            {
                _log.Log(Name, docType, LogOutcome.Error, "Show all failed", result.Error);
                _dialog.ShowMessage(Name, "No se pudieron mostrar los componentes:\n" + result.Error);
                return;
            }

            _log.Log(Name, docType, LogOutcome.Success,
                "ShowDependents=" + result.RanShowDependents + " shownByFallback=" + result.ShownByFallback);
        }
    }
}
