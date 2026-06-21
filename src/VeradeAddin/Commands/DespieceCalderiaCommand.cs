using System.Collections.Generic;
using System.Text;
using VeradeAddin.Logging;
using VeradeAddin.Models;
using VeradeAddin.Services;

namespace VeradeAddin.Commands
{
    /// <summary>
    /// "Despiece de calderería": builds the boilermaking part breakdown of the part referenced by the
    /// active drawing — a 3-view group per cut-list item (body isolated, single global scale), an
    /// isometric exploded view with balloons and the cut-list table, paginated across A0 sheets.
    ///
    /// DESTRUCTIVE: a multibody non-weldment part has the weldment feature inserted (no rollback) and
    /// the part + drawing are saved. The command warns and requires confirmation first.
    ///
    /// Pure dispatch + logging; all SolidWorks work goes through <see cref="ISolidWorksService"/>.
    /// </summary>
    public sealed class DespieceCalderiaCommand : ICommand
    {
        private readonly ISolidWorksService _sw;
        private readonly IDialogService _dialog;
        private readonly ILogger _log;

        public DespieceCalderiaCommand(ISolidWorksService sw, IDialogService dialog, ILogger log)
        {
            _sw = sw;
            _dialog = dialog;
            _log = log;
        }

        public string Name { get { return "Despiece de calderería"; } }
        public string Tooltip { get { return "Genera el despiece de la pieza del dibujo en hojas nuevas (experimental, destructivo)"; } }
        public string Hint { get { return "Crea grupos de 3 vistas por elemento de la lista de corte, vista explosionada con globos y tabla, en hojas A0"; } }
        public CommandIcon Icon { get { return CommandIcon.Breakdown; } }

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

            var plan = _sw.InspectDrawingForBreakdown();
            if (plan == null || !plan.IsDrawing)
            {
                _log.Log(Name, "None", LogOutcome.Cancel, "No active drawing");
                _dialog.ShowMessage(Name, "Debe haber un dibujo activo.");
                return;
            }
            if (!plan.CanProceed)
            {
                _log.Log(Name, docType, LogOutcome.Cancel, plan.Message ?? "Cannot proceed");
                _dialog.ShowMessage(Name, plan.Message ?? "No se puede generar el despiece.");
                return;
            }

            // Configuration: mandatory pick when >1, silent use when exactly 1.
            string config = null;
            if (plan.Configurations.Count > 1)
            {
                config = _dialog.ChooseFromList(Name,
                    "La pieza '" + plan.ModelTitle + "' tiene varias configuraciones. Elige una:",
                    plan.Configurations);
                if (string.IsNullOrEmpty(config))
                {
                    _log.Log(Name, docType, LogOutcome.Cancel, "Cancelled at configuration picker");
                    return;
                }
            }
            else if (plan.Configurations.Count == 1)
            {
                config = plan.Configurations[0];
            }

            // Destructive / experimental warning before doing anything.
            var warn = new StringBuilder();
            warn.AppendLine("⚠ Comando EXPERIMENTAL y DESTRUCTIVO.");
            warn.AppendLine();
            if (plan.NeedsWeldmentInsertion)
            {
                warn.AppendLine("La pieza es multicuerpo y NO es soldadura: se insertará la función");
                warn.AppendLine("Soldadura directamente en la pieza. Es IRREVERSIBLE (sin copia, sin");
                warn.AppendLine("deshacer).");
                warn.AppendLine();
            }
            warn.AppendLine("Al terminar se GUARDARÁN la pieza y el dibujo.");
            warn.AppendLine("Puede tardar y crea hojas nuevas. Guarda una copia antes si lo necesitas.");
            warn.AppendLine();
            warn.AppendLine("¿Continuar?");

            if (!_dialog.Confirm(Name, warn.ToString()))
            {
                _log.Log(Name, docType, LogOutcome.Cancel, "User declined destructive warning");
                return;
            }

            var result = _sw.GenerateBoilermakingBreakdown(config, true);

            if (result.Aborted)
            {
                _dialog.ShowMessage(Name, "No se pudo generar el despiece:\n" + result.AbortReason);
                return;
            }
            if (!string.IsNullOrEmpty(result.Error))
            {
                _dialog.ShowMessage(Name, "Ocurrió un error:\n" + result.Error);
                return;
            }

            var msg = new StringBuilder();
            msg.AppendLine("Despiece generado.");
            msg.AppendLine();
            msg.AppendLine("Configuración: " + (result.ConfigUsed ?? "(activa)"));
            msg.AppendLine("Elementos de lista de corte: " + result.CutListItems);
            msg.AppendLine("Grupos de vistas: " + result.GroupsCreated);
            if (result.FlatPatternsCreated > 0) msg.AppendLine("Desarrollos (chapa): " + result.FlatPatternsCreated);
            msg.AppendLine("Vistas creadas: " + result.ViewsCreated);
            msg.AppendLine("Globos: " + result.BalloonsCreated);
            msg.AppendLine("Hojas nuevas: " + result.SheetsCreated);
            msg.AppendLine("Escala global: 1:" + result.ScaleDen);
            if (result.WeldmentInserted) msg.AppendLine("Soldadura insertada (irreversible).");
            if (result.Warnings.Count > 0)
            {
                msg.AppendLine();
                msg.AppendLine("Avisos:");
                foreach (var w in result.Warnings) msg.AppendLine("• " + w);
            }

            _dialog.ShowMessage(Name, msg.ToString());
        }
    }
}
