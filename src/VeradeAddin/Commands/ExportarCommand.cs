using System.Collections.Generic;
using System.IO;
using System.Text;
using VeradeAddin.Logging;
using VeradeAddin.Models;
using VeradeAddin.Services;

namespace VeradeAddin.Commands
{
    /// <summary>
    /// "Exportar": from a drawing, exports PDF and/or DWG (of the drawing) and/or STEP (of the
    /// referenced 3D model). Flow: format checkboxes → prefix/suffix prompt → export. STEP is
    /// blocked when the model is an assembly with more than 10 components (crash risk), and the
    /// model is activated before its STEP export as SolidWorks requires.
    ///
    /// Pure dispatch + logging; all SolidWorks work goes through <see cref="ISolidWorksService"/>
    /// and the two dialogs through <see cref="IDialogService"/>.
    /// </summary>
    public sealed class ExportarCommand : ICommand
    {
        private readonly ISolidWorksService _sw;
        private readonly IDialogService _dialog;
        private readonly ILogger _log;

        public ExportarCommand(ISolidWorksService sw, IDialogService dialog, ILogger log)
        {
            _sw = sw;
            _dialog = dialog;
            _log = log;
        }

        public string Name { get { return "Exportar"; } }
        public string Tooltip { get { return "Exporta el dibujo a PDF/DWG y el modelo a STEP"; } }
        public string Hint { get { return "Exporta el dibujo activo a PDF y/o DWG, y el modelo referenciado a STEP, con prefijo o sufijo opcional"; } }

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

            var info = _sw.InspectActiveDrawingForExport();
            if (info == null || !info.IsDrawing)
            {
                _log.Log(Name, "None", LogOutcome.Cancel, "No active drawing");
                _dialog.ShowMessage(Name, "Debe haber un dibujo activo.");
                return;
            }
            if (string.IsNullOrEmpty(info.DrawingPath))
            {
                _log.Log(Name, docType, LogOutcome.Cancel, "Drawing not saved");
                _dialog.ShowMessage(Name, "Guarde el dibujo antes de exportar.");
                return;
            }

            // Step 1: choose formats.
            var request = _dialog.ShowExportOptions(info);
            if (request == null)
            {
                _log.Log(Name, docType, LogOutcome.Cancel, "Cancelled at format dialog");
                return;
            }

            // Step 2: prefix / suffix (either, both or neither). Show the real drawing name.
            string baseName = Path.GetFileNameWithoutExtension(info.DrawingPath);
            string prefix;
            string suffix;
            if (!_dialog.ShowAffixPrompt(baseName, out prefix, out suffix))
            {
                _log.Log(Name, docType, LogOutcome.Cancel, "Cancelled at affix dialog");
                return;
            }
            request.Prefix = prefix;
            request.Suffix = suffix;

            var result = _sw.ExportActiveDrawing(request);

            var sb = new StringBuilder();
            var errorDetail = new StringBuilder();
            int ok = 0, fail = 0;
            foreach (var item in result.Items)
            {
                if (item.Success)
                {
                    ok++;
                    sb.AppendLine("[OK] " + item.Format + " → " + item.Path);
                }
                else
                {
                    fail++;
                    sb.AppendLine("[ERROR] " + item.Format + ": " + item.Error);
                    if (errorDetail.Length > 0) errorDetail.Append(" | ");
                    errorDetail.Append(item.Format + ": " + item.Error);
                }
            }

            if (fail == 0)
            {
                _log.Log(Name, docType, LogOutcome.Success, "Exported " + ok + " file(s)");
            }
            else
            {
                _log.Log(Name, docType, LogOutcome.Error,
                    "Exported " + ok + ", failed " + fail, errorDetail.ToString());
            }

            _dialog.ShowMessage(Name, sb.ToString());
        }
    }
}
