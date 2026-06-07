using System.Collections.Generic;

namespace VeradeAddin.Models
{
    /// <summary>Per-format outcome of the "Exportar" command. COM-free.</summary>
    public sealed class ExportResult
    {
        public ExportResult()
        {
            Items = new List<ExportItem>();
        }

        public List<ExportItem> Items { get; private set; }
    }

    /// <summary>One exported (or attempted) format.</summary>
    public sealed class ExportItem
    {
        /// <summary>"PDF" / "DWG" / "STEP" (or "General" for a whole-command failure).</summary>
        public string Format { get; set; }

        public bool Success { get; set; }

        /// <summary>Output path when successful.</summary>
        public string Path { get; set; }

        /// <summary>Failure reason when not successful.</summary>
        public string Error { get; set; }
    }
}
