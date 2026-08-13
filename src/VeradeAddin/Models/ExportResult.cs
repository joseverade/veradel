using System.Collections.Generic;

namespace VeradeAddin.Models
{
    /// <summary>How one format ended up in the "Exportar" command.</summary>
    public enum ExportStatus
    {
        /// <summary>The user did not tick this format.</summary>
        NotSelected = 0,

        /// <summary>File written.</summary>
        Ok = 1,

        /// <summary>The file already existed and "reemplazar" was off, so nothing was written.</summary>
        AlreadyExists = 2,

        /// <summary>The file already existed, replacing was allowed, but it could not be replaced
        /// (open in another program, read-only, no permissions...).</summary>
        ReplaceFailed = 3,

        /// <summary>Anything else that went wrong while exporting.</summary>
        Error = 4
    }

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

        public ExportStatus Status { get; set; }

        /// <summary>Output path when successful.</summary>
        public string Path { get; set; }

        /// <summary>Reason detail when not successful (empty when it worked).</summary>
        public string Error { get; set; }

        public bool Success { get { return Status == ExportStatus.Ok; } }
    }
}
