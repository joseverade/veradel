using System.Collections.Generic;

namespace VeradeAddin.Models
{
    /// <summary>
    /// What the "Despiece de calderería" command found in the active drawing before running: the
    /// referenced part, its configurations and whether a (destructive) weldment insertion is needed.
    /// COM-free; feeds validation, the configuration picker and the destructive warning.
    /// </summary>
    public sealed class BoilermakingPlan
    {
        public BoilermakingPlan()
        {
            Configurations = new List<string>();
        }

        public bool IsDrawing { get; set; }
        public bool HasModel { get; set; }
        public bool IsPart { get; set; }
        public bool IsMultiBody { get; set; }
        public bool IsWeldment { get; set; }

        public string ModelPath { get; set; }
        public string ModelTitle { get; set; }

        public List<string> Configurations { get; private set; }

        /// <summary>Reason the command cannot proceed (shown to the user) or extra info.</summary>
        public string Message { get; set; }

        /// <summary>Drawing referencing a part with at least the basics to attempt a breakdown.</summary>
        public bool CanProceed { get { return IsDrawing && HasModel && IsPart; } }

        /// <summary>
        /// True when the part is multibody but not yet a weldment, so the command must insert the
        /// weldment feature — a DESTRUCTIVE, irreversible edit that needs explicit confirmation.
        /// </summary>
        public bool NeedsWeldmentInsertion { get { return IsMultiBody && !IsWeldment; } }
    }
}
