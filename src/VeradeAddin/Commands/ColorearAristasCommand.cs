using System.Collections.Generic;
using System.Text;
using VeradeAddin.Logging;
using VeradeAddin.Models;
using VeradeAddin.Services;

namespace VeradeAddin.Commands
{
    /// <summary>
    /// "Colorear aristas": carries the appearance colours of a part's faces onto the corresponding
    /// edges of its drawing. Flow: inspect drawing → colour-selection dialog → apply. Best-effort
    /// (the 3D→2D edge mapping is inherently partial); "Limpiar colores" undoes it.
    ///
    /// Pure dispatch + logging; all SolidWorks work goes through <see cref="ISolidWorksService"/>.
    /// </summary>
    public sealed class ColorearAristasCommand : ICommand
    {
        private readonly ISolidWorksService _sw;
        private readonly IDialogService _dialog;
        private readonly ILogger _log;

        public ColorearAristasCommand(ISolidWorksService sw, IDialogService dialog, ILogger log)
        {
            _sw = sw;
            _dialog = dialog;
            _log = log;
        }

        public string Name { get { return "Colorear aristas"; } }
        public string Tooltip { get { return "Colorear aristas"; } }
        public string Hint { get { return "Colorea las aristas de la VISTA de pieza seleccionada según las apariencias de color de su pieza"; } }
        public CommandIcon Icon { get { return CommandIcon.ColorEdges; } }

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

            var plan = _sw.InspectDrawingForEdgeColoring();
            if (plan == null || !plan.IsDrawing)
            {
                _log.Log(Name, "None", LogOutcome.Cancel, "No active drawing");
                _dialog.ShowMessage(Name, "Debe haber un dibujo activo.");
                return;
            }
            if (plan.Parts.Count == 0 || !plan.HasAnyColor)
            {
                _log.Log(Name, docType, LogOutcome.Cancel, plan.Message ?? "Nothing to color");
                _dialog.ShowMessage(Name, plan.Message ?? "No hay colores que aplicar.");
                return;
            }

            var request = _dialog.ShowEdgeColoring(plan);
            if (request == null)
            {
                _log.Log(Name, docType, LogOutcome.Cancel, "Cancelled at color dialog");
                return;
            }
            request.ViewName = plan.ViewName;

            EdgeColoringResult result;
            using (_dialog.ShowWait(Name, "Coloreando las aristas de la vista…"))
            {
                result = _sw.ApplyEdgeColoring(request);
            }

            if (!string.IsNullOrEmpty(result.Error))
            {
                _log.Log(Name, docType, LogOutcome.Error, "Edge coloring failed", result.Error);
                _dialog.ShowMessage(Name, "No se pudo colorear:\n" + result.Error);
                return;
            }

            var msg = new StringBuilder();
            msg.AppendLine("Aristas coloreadas en la vista: " + result.EdgesColored);
            if (result.Errors.Count > 0)
            {
                msg.AppendLine();
                msg.AppendLine("Avisos:");
                foreach (var err in result.Errors) msg.AppendLine("• " + err);
            }

            if (result.Errors.Count == 0)
            {
                _log.Log(Name, docType, LogOutcome.Success,
                    "Colored " + result.EdgesColored + " edge(s) across " + result.ViewsProcessed + " view(s)");
            }
            else
            {
                _log.Log(Name, docType, LogOutcome.Error,
                    "Colored with warnings: edges=" + result.EdgesColored, string.Join(" | ", result.Errors));
            }

            _dialog.ShowMessage(Name, msg.ToString());
        }
    }
}
