using System.Collections.Generic;
using VeradeAddin.Models;

namespace VeradeAddin.Services
{
    /// <summary>
    /// UI interactions behind an interface so command handlers carry no WinForms
    /// dependency. The concrete implementation lives in the UI layer.
    /// </summary>
    public interface IDialogService
    {
        /// <summary>
        /// Shows the component hierarchy in a modal tree dialog. Returns the node the
        /// user chose with Open, or null if they cancelled.
        /// </summary>
        ComponentNode ShowComponentTree(ComponentNode root, string title);

        void ShowMessage(string title, string message);

        /// <summary>Yes/No confirmation. Returns true only when the user confirms.</summary>
        bool Confirm(string title, string message);

        /// <summary>
        /// Shows the captured image (<paramref name="imagePath"/>) in a modal preview with
        /// "Guardar como" / "Copiar al portapapeles" / "Cerrar". Copying happens inside the dialog;
        /// when the user picks a save destination, returns <see cref="ScreenshotAction.Saved"/> and
        /// sets <paramref name="savedPath"/> to the chosen path so the caller can copy the file.
        /// </summary>
        ScreenshotAction ShowScreenshot(string title, string imagePath, out string savedPath);

        /// <summary>
        /// "Exportar" step 1: lets the user tick which formats to export (PDF/DWG/STEP). The STEP
        /// option is disabled when <see cref="DrawingExportInfo.StepAllowed"/> is false. Returns the
        /// chosen formats (affix not set yet), or null if cancelled.
        /// </summary>
        ExportRequest ShowExportOptions(DrawingExportInfo info);

        /// <summary>
        /// "Exportar" step 2: prompts for an optional prefix and/or suffix around the output file
        /// names (either, both or neither). <paramref name="baseName"/> is the real file name shown
        /// between the two textboxes and in the preview. Returns false if cancelled; otherwise sets
        /// the prefix and suffix (each empty when not used).
        /// </summary>
        bool ShowAffixPrompt(string baseName, out string prefix, out string suffix);

        /// <summary>
        /// "Colorear aristas": shows the detected part colours and lets the user pick which to carry
        /// to the edges and the target palette colour for each. Returns the request, or null if
        /// cancelled.
        /// </summary>
        EdgeColorRequest ShowEdgeColoring(EdgeColoringPlan plan);

        /// <summary>
        /// Single-choice modal picker (e.g. choosing a configuration). Returns the chosen option, or
        /// null if the user cancelled.
        /// </summary>
        string ChooseFromList(string title, string prompt, IReadOnlyList<string> options);

        /// <summary>
        /// "Configurar pieza": modern HTML/CSS catalog + configurator (WebView2 hosted in a WinForms
        /// frame). The user picks a part from the catalog and fills its dimensions next to a live
        /// dimensioned SVG. Returns the chosen part and its spec, or null if the user cancelled.
        /// </summary>
        PartConfiguratorSelection ShowPartConfigurator();
    }
}
