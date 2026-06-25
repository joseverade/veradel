using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace VeradeAddin.Services
{
    public sealed class FileSystemService : IFileSystemService
    {
        // ShowWindow commands.
        private const int SW_RESTORE = 9;
        private const int SW_MAXIMIZE = 3;

        // IShellFolderViewDual.SelectItem flags (SVSI_*).
        private const int SVSI_SELECT = 0x0001;
        private const int SVSI_DESELECTOTHERS = 0x0004;
        private const int SVSI_ENSUREVISIBLE = 0x0008;
        private const int SVSI_FOCUSED = 0x0010;
        private const int SelectFlags = SVSI_SELECT | SVSI_DESELECTOTHERS | SVSI_ENSUREVISIBLE | SVSI_FOCUSED;

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
                    string folder = Path.GetDirectoryName(filePath);
                    string fileName = Path.GetFileName(filePath);

                    // 1) Reuse an already-open Explorer window of the same folder.
                    if (TryActivateExistingWindow(folder, fileName))
                    {
                        return FileSystemOpenResult.FileSelected;
                    }

                    // 2) Spawn a fresh window with the file selected, then maximize + focus it.
                    Process.Start("explorer.exe", "/select,\"" + filePath + "\"");
                    PollAndActivate(folder, fileName);
                    return FileSystemOpenResult.FileSelected;
                }

                var dir = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                {
                    if (TryActivateExistingWindow(dir, null))
                    {
                        return FileSystemOpenResult.FolderOpened;
                    }

                    Process.Start("explorer.exe", "\"" + dir + "\"");
                    PollAndActivate(dir, null);
                    return FileSystemOpenResult.FolderOpened;
                }

                return FileSystemOpenResult.FolderMissing;
            }
            catch
            {
                return FileSystemOpenResult.Failed;
            }
        }

        // ---- Shell COM automation (late-bound; no SHDocVw reference) --------------------------

        /// <summary>Polls for a freshly-spawned Explorer window of <paramref name="folder"/>,
        /// then selects the file, maximizes and brings it to the foreground.</summary>
        private static void PollAndActivate(string folder, string fileName)
        {
            for (int i = 0; i < 25; i++) // ~2.5 s max
            {
                if (TryActivateExistingWindow(folder, fileName))
                {
                    return;
                }
                Thread.Sleep(100);
            }
        }

        /// <summary>Finds an open Explorer window showing <paramref name="folder"/>. If found,
        /// selects <paramref name="fileName"/> (when given), maximizes the window and forces it
        /// to the foreground. Returns false if no matching window exists.</summary>
        private static bool TryActivateExistingWindow(string folder, string fileName)
        {
            if (string.IsNullOrEmpty(folder)) return false;

            object shell = null;
            object windows = null;
            try
            {
                Type shellType = Type.GetTypeFromProgID("Shell.Application");
                if (shellType == null) return false;

                shell = Activator.CreateInstance(shellType);
                dynamic dynShell = shell;
                windows = dynShell.Windows();
                dynamic dynWindows = windows;

                int count = dynWindows.Count;
                for (int i = 0; i < count; i++)
                {
                    dynamic w = null;
                    try
                    {
                        w = dynWindows.Item(i);
                        if (w == null) continue;

                        // Skip Internet Explorer windows: only the file Explorer host matches.
                        string exe = SafeStr(() => (string)w.FullName);
                        if (exe == null || !exe.EndsWith("explorer.exe", StringComparison.OrdinalIgnoreCase))
                            continue;

                        dynamic doc = w.Document;
                        if (doc == null) continue;

                        string winPath = SafeStr(() => (string)doc.Folder.Self.Path);
                        if (winPath == null) continue;
                        if (!PathsEqual(winPath, folder)) continue;

                        // Match. Select the file (if any).
                        if (!string.IsNullOrEmpty(fileName))
                        {
                            try
                            {
                                dynamic item = doc.Folder.ParseName(fileName);
                                if (item != null)
                                {
                                    doc.SelectItem(item, SelectFlags);
                                }
                            }
                            catch { /* selection best-effort */ }
                        }

                        IntPtr hwnd = new IntPtr((long)w.HWND);
                        Maximize(hwnd);
                        ForceForeground(hwnd);
                        return true;
                    }
                    catch
                    {
                        // ignore this window, try next
                    }
                    finally
                    {
                        if (w != null) TryRelease(w);
                    }
                }

                return false;
            }
            catch
            {
                return false;
            }
            finally
            {
                if (windows != null) TryRelease(windows);
                if (shell != null) TryRelease(shell);
            }
        }

        private static void Maximize(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return;
            if (IsIconic(hwnd)) ShowWindow(hwnd, SW_RESTORE);
            ShowWindow(hwnd, SW_MAXIMIZE);
        }

        /// <summary>Brings a window to the foreground, working around Windows' focus-steal
        /// prevention by briefly attaching to the current foreground thread's input queue.</summary>
        private static void ForceForeground(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return;

            IntPtr fore = GetForegroundWindow();
            uint foreThread = GetWindowThreadProcessId(fore, out _);
            uint thisThread = GetCurrentThreadId();

            if (foreThread != thisThread)
            {
                AttachThreadInput(thisThread, foreThread, true);
                SetForegroundWindow(hwnd);
                AttachThreadInput(thisThread, foreThread, false);
            }
            else
            {
                SetForegroundWindow(hwnd);
            }
        }

        private static bool PathsEqual(string a, string b)
        {
            return string.Equals(
                a.TrimEnd('\\', '/'),
                b.TrimEnd('\\', '/'),
                StringComparison.OrdinalIgnoreCase);
        }

        private static string SafeStr(Func<string> get)
        {
            try { return get(); } catch { return null; }
        }

        private static void TryRelease(object o)
        {
            try { if (o != null && Marshal.IsComObject(o)) Marshal.ReleaseComObject(o); }
            catch { }
        }

        // ---- Win32 ----------------------------------------------------------------------------

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        // ---- unchanged ------------------------------------------------------------------------

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
