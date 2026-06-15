using System.Collections.Generic;
using VeradeAddin.Logging;
using VeradeAddin.Models;
using VeradeAddin.Services;

namespace VeradeAddin.Commands
{
    /// <summary>
    /// "Configurar pieza": part-tab entry point to the parametric part generator. Only enabled on
    /// an empty part (no features in the tree). It opens a catalog of parts the add-in can build
    /// from scratch; the first one is "Bulón" (a stepped revolve). The chosen part's dimensions are
    /// gathered in a modern HTML/CSS configurator (SVG with dimensions on the left, inputs on the
    /// right) and the geometry is created with no base part.
    ///
    /// Pure dispatch + logging; geometry goes through <see cref="ISolidWorksService"/>, UI through
    /// <see cref="IDialogService"/>.
    /// </summary>
    public sealed class ConfigurarPiezaCommand : ICommand
    {
        private readonly ISolidWorksService _sw;
        private readonly IDialogService _dialog;
        private readonly ILogger _log;

        public ConfigurarPiezaCommand(ISolidWorksService sw, IDialogService dialog, ILogger log)
        {
            _sw = sw;
            _dialog = dialog;
            _log = log;
        }

        public string Name { get { return "Configurar pieza"; } }
        public string Tooltip { get { return "Genera una pieza paramétrica desde cero (bulón, ...)"; } }
        public string Hint { get { return "Abre el catálogo de piezas generables y crea la elegida en la pieza vacía actual"; } }

        public IReadOnlyList<DocumentKind> DocumentTypes
        {
            get { return new[] { DocumentKind.Part }; }
        }

        public bool CanExecute()
        {
            return _sw.IsActivePartEmpty();
        }

        public void Execute()
        {
            const string docType = "Part";

            if (!_sw.IsActivePartEmpty())
            {
                _log.Log(Name, docType, LogOutcome.Cancel, "Active part is not empty (or no part active)");
                _dialog.ShowMessage(Name,
                    "Este comando genera la pieza desde cero en un documento de pieza vacío.\n\n" +
                    "Abra una pieza nueva (sin operaciones en el árbol) e inténtelo de nuevo.");
                return;
            }

            var selection = _dialog.ShowPartConfigurator();
            if (selection == null)
            {
                _log.Log(Name, docType, LogOutcome.Cancel, "User cancelled the configurator");
                return;
            }

            // Catalog currently has one part. Dispatch by kind so adding parts here stays trivial.
            switch (selection.Part)
            {
                case ConfigurablePart.Bolt:
                    BuildBolt(selection.Bolt, docType);
                    break;
                default:
                    _log.Log(Name, docType, LogOutcome.Error, "Unknown part kind: " + selection.Part);
                    _dialog.ShowMessage(Name, "Pieza no reconocida.");
                    break;
            }
        }

        private void BuildBolt(BoltSpec spec, string docType)
        {
            var result = _sw.CreateCustomBolt(spec);
            if (result.Success)
            {
                string groove = spec.HasGroove
                    ? ", ranura D3=" + spec.GrooveDiameterMm + " E1=" + spec.GrooveWidthMm + " P1=" + spec.GroovePositionMm
                    : "";
                string chamfer = spec.HasChamfer
                    ? ", chaflán " + spec.ChamferSizeMm + "×" + spec.ChamferAngleDeg + "°"
                    : "";
                _log.Log(Name, docType, LogOutcome.Success,
                    "Bolt created (Ø1=" + spec.HeadDiameterMm + ", L1=" + spec.HeadLengthMm +
                    ", Ø2=" + spec.ShankDiameterMm + ", L2=" + spec.ShankLengthMm + groove + chamfer + ")");
                _dialog.ShowMessage(Name,
                    "Bulón creado.\n" +
                    "Cabeza: Ø" + spec.HeadDiameterMm + " × " + spec.HeadLengthMm + " mm\n" +
                    "Vástago: Ø" + spec.ShankDiameterMm + " × " + spec.ShankLengthMm + " mm\n" +
                    (spec.HasGroove ? "Ranura: D3 " + spec.GrooveDiameterMm + " · E1 " + spec.GrooveWidthMm + " · P1 " + spec.GroovePositionMm + " mm\n" : "") +
                    (spec.HasChamfer ? "Chaflán: " + spec.ChamferSizeMm + " mm × " + spec.ChamferAngleDeg + "°\n" : "") +
                    "Longitud total: " + result.TotalLengthMm + " mm");
            }
            else
            {
                _log.Log(Name, docType, LogOutcome.Error, "Bolt creation failed", result.Error);
                _dialog.ShowMessage(Name, "No se pudo crear el bulón:\n" + result.Error);
            }
        }
    }
}
