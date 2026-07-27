using System;
using SolidWorks.Interop.sldworks;
using VeradeAddin.Commands;
using VeradeAddin.Licensing;
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
        private readonly ILicenseService _license;
        private bool _trialReminderShown;

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

            // --- Licensing -----------------------------------------------------------
            _license = new LicenseService();
            Logger.Log("License", "n/a", LogOutcome.Success,
                "Status=" + _license.Status + " TrialDaysLeft=" + _license.TrialDaysLeft);

            // --- Services ------------------------------------------------------------
            ISolidWorksService swService = new SolidWorksService(sw, Logger);
            IFileSystemService fsService = new FileSystemService();
            IScreenCaptureService captureService = new ScreenCaptureService();
            IPartRegistry partRegistry = new JsonPartRegistry();
            _dialog = new WinFormsDialogService();

            // --- Commands (register new commands here, nothing else) -----------------
            _registry = new CommandRegistry()
                .Add(new OpenFolderCommand(swService, fsService, _dialog, Logger))
                .Add(new MostrarRoscaCommand(swService, _dialog, Logger))
                .Add(new ExtraerPiezaCommand(swService, _dialog, Logger))
                .Add(new IdentificarRoscaCommand(swService, _dialog, Logger))
                .Add(new CapturaPantallaCommand(swService, captureService, fsService, _dialog, Logger))
                .Add(new ExportarCommand(swService, _dialog, Logger))
                .Add(new ColorearAristasCommand(swService, _dialog, Logger))
                .Add(new LimpiarColoresCommand(swService, _dialog, Logger))
                // Despiece de calderería oculto del ribbon (código intacto): descomentar para reactivar.
                //.Add(new DespieceCalderiaCommand(swService, _dialog, Logger))
                .Add(new ConfigurarPiezaCommand(swService, _dialog, partRegistry, Logger))
                .Add(new LicenciaCommand(_dialog, _license, Logger));

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

            // The license command is exempt from the gate: it opens the activation dialog
            // itself and must stay reachable while the add-in is blocked.
            if (!(command is LicenciaCommand))
            {
                // License gate: expired/tampered -> activation dialog; still blocked -> no run.
                if (!_license.CanRun)
                {
                    _dialog.ShowLicense(_license);
                    if (!_license.CanRun)
                    {
                        return;
                    }
                }
                else if (_license.Status == LicenseStatus.Trial && _license.TrialDaysLeft <= 7 && !_trialReminderShown)
                {
                    _trialReminderShown = true;
                    _dialog.ShowMessage("Veradel",
                        "Quedan " + _license.TrialDaysLeft + " día(s) de prueba.\n" +
                        "Puedes activar una licencia con el botón \"Licencia\" del ribbon.");
                }
            }

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

        /// <summary>
        /// Enable-state query for a ribbon callback, using SolidWorks' 4-state convention:
        /// 0 = deselected+disabled, 1 = deselected+enabled, 2 = selected+disabled, 3 = selected+enabled.
        /// Toggle commands report their pressed state so the button stays highlighted while active.
        /// </summary>
        public int CanExecute(int commandIndex)
        {
            if (commandIndex < 0 || commandIndex >= _registry.Count)
            {
                return 0;
            }

            var command = _registry[commandIndex];
            bool enabled = command.CanExecute();

            var toggle = command as IToggleCommand;
            if (toggle != null)
            {
                bool pressed = toggle.IsActive;
                return enabled ? (pressed ? 3 : 1) : (pressed ? 2 : 0);
            }

            return enabled ? 1 : 0;
        }
    }
}
