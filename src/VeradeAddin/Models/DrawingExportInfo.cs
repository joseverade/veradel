namespace VeradeAddin.Models
{
    /// <summary>
    /// Read-only snapshot of the active drawing for the "Exportar" command: tells the dialog which
    /// formats are possible (in particular whether STEP is allowed). COM-free.
    /// </summary>
    public sealed class DrawingExportInfo
    {
        public bool IsDrawing { get; set; }

        public string DrawingPath { get; set; }

        public string DrawingTitle { get; set; }

        /// <summary>True when the drawing references a model (needed for STEP).</summary>
        public bool HasModel { get; set; }

        public string ModelPath { get; set; }

        /// <summary>Kind of the referenced model (Part/Assembly).</summary>
        public DocumentKind ModelKind { get; set; }

        /// <summary>Component count when the model is an assembly; -1 when unknown/not applicable.</summary>
        public int ComponentCount { get; set; }

        /// <summary>
        /// True when STEP export is allowed: a part, or an assembly with at most 10 components.
        /// Assemblies above the threshold are blocked because exporting them can crash SolidWorks.
        /// </summary>
        public bool StepAllowed { get; set; }

        /// <summary>Why STEP is disabled (shown next to the greyed-out checkbox).</summary>
        public string StepBlockedReason { get; set; }

        public DrawingExportInfo()
        {
            ComponentCount = -1;
        }
    }
}
