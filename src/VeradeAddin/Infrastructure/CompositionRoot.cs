using System;
using SolidWorks.Interop.sldworks;
using VeradeAddin.Commands;
using VeradeAddin.Logging;
using VeradeAddin.Services;
using VeradeAddin.UI;

namespace VeradeAddin.Infrastructure
{
    /// <summary>
    /// Manual DI container / composition root. Builds the entire object graph once at
    /// add-in connect time using constructor injection. This is the single place to:
    ///   - choose the logging sink(s) (swap file -> telemetry here),
    ///   - register commands (add to the CommandRegistry here).
    /// No other class news-up its dependencies.
    /// </summary>
    public sealed class CompositionRoot
    {
        private readonly CommandRegistry _registry;
        private readonly IDialogService _dialog;

        public ILogger Logger { get; private set; }

        public CommandRegistrar CommandRegistrar { get; private set; }

        public CompositionRoot(ISldWorks sw, int cookie)
        {
            // --- Logging pipeline ----------------------------------------------------
            // Swap or add sinks here (e.g. add an HttpTelemetrySink alongside the file sink).
            Logger = new Logger(new ILogSink[]
            {
                new JsonLinesFileSink()
            });

            // --- Services ------------------------------------------------------------
            ISolidWorksService swService = new SolidWorksService(sw, Logger);
            IFileSystemService fsService = new FileSystemService();
            IScreenCaptureService captureService = new ScreenCaptureService();
            _dialog = new WinFormsDialogService();

            // --- Commands (register new commands here, nothing else) -----------------
            _registry = new CommandRegistry()
                .Add(new OpenFolderCommand(swService, fsService, _dialog, Logger))
                .Add(new MostrarRoscaCommand(swService, _dialog, Logger))
                .Add(new ExtraerPiezaCommand(swService, _dialog, Logger))
                .Add(new CapturaPantallaCommand(swService, captureService, fsService, _dialog, Logger))
                .Add(new ExportarCommand(swService, _dialog, Logger))
                .Add(new ColorearAristasCommand(swService, _dialog, Logger))
                .Add(new LimpiarColoresCommand(swService, _dialog, Logger))
                .Add(new DespieceCalderiaCommand(swService, _dialog, Logger));

            // --- Ribbon registrar ----------------------------------------------------
            CommandRegistrar = new CommandRegistrar(sw, cookie, _registry, Logger);
        }

        /// <summary>
        /// Dispatch a ribbon callback to its command, with a centralised safety net so a
        /// command can never crash SolidWorks and every unhandled failure is logged. New
        /// commands get this protection automatically — they don't need their own try/catch.
        /// </summary>
        public void Execute(int commandIndex)
        {
            if (commandIndex < 0 || commandIndex >= _registry.Count)
            {
                return;
            }

            var command = _registry[commandIndex];
            try
            {
                command.Execute();
            }
            catch (Exception ex)
            {
                Logger.Log(command.Name, "n/a", LogOutcome.Error, "Unhandled exception in command", ex.ToString());
                _dialog.ShowMessage(command.Name, "Ocurrió un error inesperado:\n" + ex.Message);
            }
        }

        /// <summary>Enable-state query for a ribbon callback. 1 = enabled, 0 = disabled.</summary>
        public int CanExecute(int commandIndex)
        {
            if (commandIndex < 0 || commandIndex >= _registry.Count)
            {
                return 0;
            }
            return _registry[commandIndex].CanExecute() ? 1 : 0;
        }
    }
}
