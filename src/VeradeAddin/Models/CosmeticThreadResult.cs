using System.Collections.Generic;

namespace VeradeAddin.Models
{
    /// <summary>
    /// Outcome of inserting cosmetic thread annotations into the assembly views of a drawing.
    /// COM-free so the command layer can report/log without touching SolidWorks types.
    /// </summary>
    public sealed class CosmeticThreadResult
    {
        public CosmeticThreadResult()
        {
            Errors = new List<string>();
        }

        /// <summary>True only when the active document was a drawing.</summary>
        public bool WasDrawing { get; set; }

        /// <summary>Number of views on the active sheet that reference an assembly.</summary>
        public int AssemblyViewsFound { get; set; }

        /// <summary>Views where annotations were inserted successfully.</summary>
        public int Processed { get; set; }

        /// <summary>Views that failed.</summary>
        public int Failed { get; set; }

        public List<string> Errors { get; private set; }
    }
}
