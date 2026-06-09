using System.Collections.Generic;

namespace VeradeAddin.Models
{
    /// <summary>Outcome of "Colorear aristas" / "Limpiar colores". COM-free.</summary>
    public sealed class EdgeColoringResult
    {
        public EdgeColoringResult()
        {
            Errors = new List<string>();
        }

        public bool Success { get; set; }

        /// <summary>Edges whose colour was set.</summary>
        public int EdgesColored { get; set; }

        /// <summary>Drawing views that were processed.</summary>
        public int ViewsProcessed { get; set; }

        public List<string> Errors { get; private set; }

        /// <summary>Fatal reason when the whole operation could not run.</summary>
        public string Error { get; set; }
    }
}
