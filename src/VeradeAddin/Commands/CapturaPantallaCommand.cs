using System;
using System.Collections.Generic;
using VeradeAddin.Logging;
using VeradeAddin.Models;
using VeradeAddin.Services;

namespace VeradeAddin.Commands
{
    /// <summary>
    /// "Captura de pantalla": grabs a real screenshot of the SolidWorks graphics area (model space)
    /// — cropped to the active view's window, overlays drawn over it included (dimensions, Measure
    /// callouts, menus) — to a temporary PNG, then shows a preview dialog where the user can save it
    /// ("Guardar como") or copy it to the clipboard ("Copiar al portapapeles"). The temp file is
    /// always cleaned up afterwards.
    ///
    /// Pure dispatch + logging: the screen grab goes through <see cref="IScreenCaptureService"/>,
    /// the active-doc check through <see cref="ISolidWorksService"/>, the preview/clipboard UI
    /// through <see cref="IDialogService"/>, and file copy/cleanup through
    /// <see cref="IFileSystemService"/>.
    /// </summary>
    public sealed class CapturaPantallaCommand : ICommand
    {
        private readonly ISolidWorksService _sw;
        private readonly IScreenCaptureService _capture;
        private readonly IFileSystemService _fs;
        private readonly IDialogService _dialog;
        private readonly ILogger _log;

        public CapturaPantallaCommand(ISolidWorksService sw, IScreenCaptureService capture,
            IFileSystemService fs, IDialogService dialog, ILogger log)
        {
            _sw = sw;
            _capture = capture;
            _fs = fs;
            _dialog = dialog;
            _log = log;
        }

        public string Name { get { return "Captura de pantalla"; } }
        public string Tooltip { get { return "Captura la vista actual del modelo y permite guardarla o copiarla"; } }
        public string Hint { get { return "Toma una imagen de la vista actual de la pieza o ensamblaje y la muestra para guardar o copiar al portapapeles"; } }

        public IReadOnlyList<DocumentKind> DocumentTypes
        {
            get { return new[] { DocumentKind.Part, DocumentKind.Assembly }; }
        }

        public bool CanExecute()
        {
            var doc = _sw.GetActiveDocument();
            return doc != null && (doc.Kind == DocumentKind.Part || doc.Kind == DocumentKind.Assembly);
        }

        public void Execute()
        {
            var doc = _sw.GetActiveDocument();
            string docType = doc != null ? doc.Kind.ToString() : "None";

            // Crop to the graphics area (model space): grab only the active view's window.
            IntPtr viewHandle = _sw.GetActiveModelViewHandle();
            if (viewHandle == IntPtr.Zero)
            {
                _log.Log(Name, docType, LogOutcome.Error, "No active model view handle");
                _dialog.ShowMessage(Name, "No se pudo localizar el área gráfica (espacio modelo).");
                return;
            }

            // Trim the FeatureManager panel (left) so only the graphics area is captured.
            int featureManagerWidth = _sw.GetActiveFeatureManagerWidth();

            var capture = _capture.CaptureWindow(viewHandle, featureManagerWidth);
            if (capture == null || !capture.Success)
            {
                string err = capture != null ? capture.Error : "Error desconocido";
                _log.Log(Name, docType, LogOutcome.Error, "Capture failed", err);
                _dialog.ShowMessage(Name, "No se pudo capturar la pantalla:\n" + err);
                return;
            }

            try
            {
                string savedPath;
                var action = _dialog.ShowScreenshot(Name, capture.ImagePath, out savedPath);

                switch (action)
                {
                    case ScreenshotAction.Copied:
                        _log.Log(Name, docType, LogOutcome.Success, "Image copied to clipboard");
                        break;

                    case ScreenshotAction.Saved:
                        if (_fs.CopyFile(capture.ImagePath, savedPath))
                        {
                            _log.Log(Name, docType, LogOutcome.Success, "Image saved to '" + savedPath + "'");
                            _dialog.ShowMessage(Name, "Imagen guardada en:\n" + savedPath);
                        }
                        else
                        {
                            _log.Log(Name, docType, LogOutcome.Error, "Failed to save image to '" + savedPath + "'");
                            _dialog.ShowMessage(Name, "No se pudo guardar la imagen en:\n" + savedPath);
                        }
                        break;

                    default:
                        _log.Log(Name, docType, LogOutcome.Cancel, "Closed without saving or copying");
                        break;
                }
            }
            finally
            {
                // The capture lives in %TEMP%; remove it regardless of outcome.
                _fs.TryDeleteFile(capture.ImagePath);
            }
        }
    }
}
