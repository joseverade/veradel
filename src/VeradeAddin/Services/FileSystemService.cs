using System.Diagnostics;
using System.IO;

namespace VeradeAddin.Services
{
    public sealed class FileSystemService : IFileSystemService
    {
        public FileSystemOpenResult RevealInExplorer(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return FileSystemOpenResult.PathEmpty;
            }

            try
            {
                if (File.Exists(filePath))
                {
                    // /select, highlights the file inside its folder.
                    Process.Start("explorer.exe", "/select,\"" + filePath + "\"");
                    return FileSystemOpenResult.FileSelected;
                }

                var folder = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
                {
                    Process.Start("explorer.exe", "\"" + folder + "\"");
                    return FileSystemOpenResult.FolderOpened;
                }

                return FileSystemOpenResult.FolderMissing;
            }
            catch
            {
                return FileSystemOpenResult.Failed;
            }
        }

        public bool CopyFile(string source, string destination)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(source) ||
                    string.IsNullOrWhiteSpace(destination) ||
                    !File.Exists(source))
                {
                    return false;
                }

                File.Copy(source, destination, true);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public void TryDeleteFile(string path)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // best effort — temp files get cleaned by the OS eventually
            }
        }
    }
}
