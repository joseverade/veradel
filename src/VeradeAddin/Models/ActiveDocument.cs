namespace VeradeAddin.Models
{
    /// <summary>
    /// Plain snapshot of the active document. Carries no COM references so command
    /// handlers and UI stay free of SolidWorks interop types.
    /// </summary>
    public sealed class ActiveDocument
    {
        public DocumentKind Kind { get; set; }

        public string Title { get; set; }

        /// <summary>Full path on disk. Empty when the document was never saved.</summary>
        public string FilePath { get; set; }

        public bool HasPath
        {
            get { return !string.IsNullOrWhiteSpace(FilePath); }
        }
    }
}
