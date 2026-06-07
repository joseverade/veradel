namespace VeradeAddin.Services
{
    /// <summary>File system / shell operations, isolated for testability.</summary>
    public interface IFileSystemService
    {
        /// <summary>
        /// Opens the containing folder of <paramref name="filePath"/> in Explorer,
        /// selecting the file when it exists. Falls back to opening the folder if the
        /// file is missing. Never throws; returns a result describing what happened.
        /// </summary>
        FileSystemOpenResult RevealInExplorer(string filePath);

        /// <summary>Copies a file, overwriting the destination. Returns false on failure (never throws).</summary>
        bool CopyFile(string source, string destination);

        /// <summary>Best-effort delete; ignores errors. Used to clean up temporary captures.</summary>
        void TryDeleteFile(string path);
    }
}
