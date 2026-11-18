using System.Collections.Generic;
using System.Globalization;
using VeradeAddin.Logging;
using VeradeAddin.Models;
using VeradeAddin.Services;

namespace VeradeAddin.Commands
{
    /// <summary>
    /// "Localizar en vista" (assembly context toolbar only): shows where the selected component is.
    /// Fires a bundle of rays from the component (origin, box centre and inset box corners) towards the
    /// camera, hides every other part they cross, then selects the component and zooms to it.
    /// Greyed out unless EXACTLY one component is selected. <see cref="MostrarTodoCommand"/> undoes it.
    ///
    /// Pure dispatch + logging; all SolidWorks work goes through <see cref="ISolidWorksService"/>.
    /// </summary>
    public sealed class LocalizarEnVistaCommand : IContextToolbarCommand
    {
        private readonly ISolidWorksService _sw;
        private readonly IDialogService _dialog;
        private readonly ILogger _log;

        public LocalizarEnVistaCommand(ISolidWorksService sw, IDialogService dialog, ILogger log)
        {
            _sw = sw;
            _dialog = dialog;
            _log = log;
        }

        public string Name { get { return "Localizar en vista"; } }
        public string Tooltip { get { return "Oculta lo que tapa el componente seleccionado y amplía hacia él"; } }
        public string Hint { get { return "Oculta las piezas que hay entre el componente seleccionado y la cámara y amplía la vista hacia él"; } }
        public CommandIcon Icon { get { return CommandIcon.Locate; } }

        public IReadOnlyList<DocumentKind> DocumentTypes
        {
            get { return new[] { DocumentKind.Assembly }; }
        }

        public IReadOnlyList<ContextToolbarTarget> ContextTargets
        {
            get { return new[] { ContextToolbarTarget.Component }; }
        }

        public bool CanExecute()
        {
            return _sw.IsSingleComponentSelectedInActiveAssembly();
        }

        public void Execute()
        {
            const string docType = "Assembly";

            var result = _sw.LocateSelectedComponentInView();
            if (!result.Success)
            {
                _log.Log(Name, docType, LogOutcome.Error, "Locate failed", result.Error);
                _dialog.ShowMessage(Name, "No se pudo localizar el componente:\n" + result.Error);
                return;
            }

            // Quick command: no dialog on success, the view itself is the feedback.
            var v = result.ViewDirection;
            string view = v != null
                ? string.Format(CultureInfo.InvariantCulture, "({0:0.###}, {1:0.###}, {2:0.###})", v[0], v[1], v[2])
                : "(?)";
            _log.Log(Name, docType, LogOutcome.Success,
                "Located '" + result.ComponentName + "': rays=" + result.RayCount + " view=" + view +
                " candidates=" + result.Candidates +
                " hidden=" + result.HiddenComponents.Count + " (boxOnly=" + result.HiddenByBoxOnly + ")" +
                (result.HiddenComponents.Count > 0 ? ": " + string.Join(", ", result.HiddenComponents) : ""));
        }
    }
}
