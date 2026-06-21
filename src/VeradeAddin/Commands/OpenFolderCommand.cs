using System.Collections.Generic;
using VeradeAddin.Logging;
using VeradeAddin.Models;
using VeradeAddin.Services;

namespace VeradeAddin.Commands
{
    /// <summary>
    /// "Open Folder" command. Resolves the active document type and reveals the relevant
    /// file in Windows Explorer. For an assembly with a selected component, shows the
    /// selection hierarchy and reveals the chosen node's file.
    ///
    /// Pure dispatch + logging; all SolidWorks/file/UI work goes through injected services.
    /// </summary>
    public sealed class OpenFolderCommand : ICommand
    {
        private readonly ISolidWorksService _sw;
        private readonly IFileSystemService _fs;
        private readonly IDialogService _dialog;
        private readonly ILogger _log;

        public OpenFolderCommand(ISolidWorksService sw, IFileSystemService fs, IDialogService dialog, ILogger log)
        {
            _sw = sw;
            _fs = fs;
            _dialog = dialog;
            _log = log;
        }

        public string Name { get { return "Abrir carpeta"; } }
        public string Tooltip { get { return "Abre la carpeta que contiene el documento activo"; } }
        public string Hint { get { return "Abre la carpeta de la pieza, plano, ensamblaje o componente seleccionado"; } }
        public CommandIcon Icon { get { return CommandIcon.Folder; } }

        public IReadOnlyList<DocumentKind> DocumentTypes
        {
            get { return new[] { DocumentKind.Part, DocumentKind.Assembly, DocumentKind.Drawing }; }
        }

        public bool CanExecute()
        {
            var doc = _sw.GetActiveDocument();
            return doc != null && doc.Kind != DocumentKind.None;
        }

        public void Execute()
        {
            // No try/catch here: the dispatcher (CompositionRoot.Execute) provides the
            // centralised safety net + error logging for every command.
            string docType = "None";

            var doc = _sw.GetActiveDocument();
            if (doc == null || doc.Kind == DocumentKind.None)
            {
                _log.Log(Name, docType, LogOutcome.Cancel, "No active document");
                _dialog.ShowMessage(Name, "No hay ningún documento activo.");
                return;
            }

            docType = doc.Kind.ToString();

            switch (doc.Kind)
            {
                case DocumentKind.Part:
                case DocumentKind.Drawing:
                    RevealDocument(doc, docType);
                    break;

                case DocumentKind.Assembly:
                    ExecuteAssembly(doc, docType);
                    break;

                default:
                    _log.Log(Name, docType, LogOutcome.Cancel, "Unsupported document type");
                    _dialog.ShowMessage(Name, "Tipo de documento no admitido.");
                    break;
            }
        }

        private void ExecuteAssembly(ActiveDocument doc, string docType)
        {
            var root = _sw.GetSelectedComponentHierarchy();

            // No component selected -> reveal the assembly file itself.
            if (root == null)
            {
                RevealDocument(doc, docType);
                return;
            }

            var chosen = _dialog.ShowComponentTree(root, Name + " — Seleccionar componente");
            if (chosen == null)
            {
                _log.Log(Name, docType, LogOutcome.Cancel, "User cancelled component tree");
                return;
            }

            RevealNode(chosen, docType);
        }

        private void RevealDocument(ActiveDocument doc, string docType)
        {
            if (!doc.HasPath)
            {
                _log.Log(Name, docType, LogOutcome.Cancel, "Document not saved (no path on disk)");
                _dialog.ShowMessage(Name, "Este documento aún no se ha guardado, por lo que no tiene carpeta en disco.");
                return;
            }

            Reveal(doc.FilePath, docType, "active document");
        }

        private void RevealNode(ComponentNode node, string docType)
        {
            if (node.IsVirtual)
            {
                _log.Log(Name, docType, LogOutcome.Cancel,
                    "Selected component is virtual (stored in assembly, no external file): " + node.Name);
                _dialog.ShowMessage(Name, "'" + node.Name + "' es un componente virtual y no tiene archivo en disco.");
                return;
            }

            if (string.IsNullOrWhiteSpace(node.FilePath))
            {
                _log.Log(Name, docType, LogOutcome.Cancel, "Selected component has no path: " + node.Name);
                _dialog.ShowMessage(Name, "'" + node.Name + "' no tiene ruta de archivo.");
                return;
            }

            string suffix = node.IsSuppressed ? " (suppressed)" : node.IsLightweight ? " (lightweight)" : string.Empty;
            Reveal(node.FilePath, docType, "component '" + node.Name + "'" + suffix);
        }

        private void Reveal(string path, string docType, string what)
        {
            var result = _fs.RevealInExplorer(path);
            switch (result)
            {
                case FileSystemOpenResult.FileSelected:
                    _log.Log(Name, docType, LogOutcome.Success, "Selected " + what + " in Explorer: " + path);
                    break;
                case FileSystemOpenResult.FolderOpened:
                    _log.Log(Name, docType, LogOutcome.Success,
                        "File missing on disk; opened containing folder for " + what + ": " + path);
                    break;
                case FileSystemOpenResult.PathEmpty:
                    _log.Log(Name, docType, LogOutcome.Cancel, "Empty path for " + what);
                    _dialog.ShowMessage(Name, "No hay ninguna ruta para abrir.");
                    break;
                case FileSystemOpenResult.FolderMissing:
                    _log.Log(Name, docType, LogOutcome.Error, "Folder missing on disk for " + what + ": " + path);
                    _dialog.ShowMessage(Name, "La carpeta ya no existe en disco:\n" + path);
                    break;
                default:
                    _log.Log(Name, docType, LogOutcome.Error, "Explorer failed to launch for " + what + ": " + path);
                    _dialog.ShowMessage(Name, "No se pudo abrir el Explorador.");
                    break;
            }
        }
    }
}
