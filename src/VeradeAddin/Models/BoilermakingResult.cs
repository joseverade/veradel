using System.Collections.Generic;

namespace VeradeAddin.Models
{
    /// <summary>
    /// COM-free outcome of "Despiece de calderería": what was created (sheets, view groups, balloons),
    /// the scale chosen, whether a weldment was inserted (irreversible) and any non-fatal warnings.
    /// </summary>
    public sealed class BoilermakingResult
    {
        public BoilermakingResult()
        {
            Warnings = new List<string>();
        }

        public bool Success { get; set; }
        public bool Aborted { get; set; }
        public string AbortReason { get; set; }
        public string Error { get; set; }

        /// <summary>True if the weldment feature was inserted on the part (destructive, not undone).</summary>
        public bool WeldmentInserted { get; set; }

        public string ConfigUsed { get; set; }
        public int CutListItems { get; set; }
        public int GroupsCreated { get; set; }
        public int FlatPatternsCreated { get; set; }
        public int ViewsCreated { get; set; }
        public int BalloonsCreated { get; set; }
        public int SheetsCreated { get; set; }
        public bool Exploded { get; set; }

        /// <summary>Global scale of the view groups, as numerator:denominator (e.g. 1:10).</summary>
        public int ScaleNum { get; set; }
        public int ScaleDen { get; set; }

        public List<string> Warnings { get; private set; }
    }
}
