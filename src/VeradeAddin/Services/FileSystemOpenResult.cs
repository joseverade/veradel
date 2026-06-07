namespace VeradeAddin.Services
{
    /// <summary>Outcome of attempting to reveal a path in Windows Explorer.</summary>
    public enum FileSystemOpenResult
    {
        /// <summary>File exists and was selected inside its folder.</summary>
        FileSelected,

        /// <summary>File was missing but its containing folder existed and was opened.</summary>
        FolderOpened,

        /// <summary>Path was empty (e.g. unsaved document).</summary>
        PathEmpty,

        /// <summary>Neither the file nor its folder exist on disk.</summary>
        FolderMissing,

        /// <summary>Explorer failed to launch.</summary>
        Failed
    }
}
