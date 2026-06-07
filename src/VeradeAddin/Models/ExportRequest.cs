namespace VeradeAddin.Models
{
    /// <summary>
    /// What the user chose to export in the "Exportar" command. COM-free. PDF/DWG are produced from
    /// the active drawing; STEP from the model the drawing references. <see cref="Prefix"/> and
    /// <see cref="Suffix"/> are added around the base file name (either, both or neither).
    /// </summary>
    public sealed class ExportRequest
    {
        public bool Pdf { get; set; }

        public bool Dwg { get; set; }

        public bool Step { get; set; }

        /// <summary>Text placed before the base file name (empty = none).</summary>
        public string Prefix { get; set; }

        /// <summary>Text placed after the base file name (empty = none).</summary>
        public string Suffix { get; set; }
    }
}
