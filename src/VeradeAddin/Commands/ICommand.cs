using System.Collections.Generic;
using VeradeAddin.Models;

namespace VeradeAddin.Commands
{
    /// <summary>
    /// A single ribbon command. Add a new command by implementing this interface and
    /// registering the class in <see cref="CommandRegistry"/> — nothing else changes.
    /// Services are supplied via constructor injection, so <see cref="Execute"/> takes
    /// no SolidWorks/COM arguments.
    /// </summary>
    public interface ICommand
    {
        /// <summary>Button caption shown on the ribbon.</summary>
        string Name { get; }

        /// <summary>Tooltip text.</summary>
        string Tooltip { get; }

        /// <summary>Status-bar hint text.</summary>
        string Hint { get; }

        /// <summary>Which glyph the ribbon button draws (rendered at runtime by <c>IconStripFactory</c>).</summary>
        CommandIcon Icon { get; }

        /// <summary>
        /// Document types the command appears in. The add-in shows one ribbon tab per document
        /// type ("Veradel Pieza/Assembly/Dibujo"); the command gets a button in each matching tab.
        /// </summary>
        IReadOnlyList<DocumentKind> DocumentTypes { get; }

        /// <summary>Whether the command is currently enabled (drives ribbon greying).</summary>
        bool CanExecute();

        /// <summary>Run the command. Implementations must log their own outcome.</summary>
        void Execute();
    }

    /// <summary>
    /// A command that behaves as a toggle: its ribbon button stays visually pressed while
    /// <see cref="IsActive"/> is true. The registrar reports the pressed state to SolidWorks
    /// (selected + enabled) instead of the plain enabled/disabled state of a normal command.
    /// </summary>
    public interface IToggleCommand : ICommand
    {
        /// <summary>True when the toggle is currently on (button drawn pressed).</summary>
        bool IsActive { get; }
    }

    /// <summary>
    /// A command with an icon in the right-click CONTEXT TOOLBAR (the small icon box that pops up over
    /// the shortcut menu in the FeatureManager tree and the graphics area). Registered from the
    /// composition root's context registry by <c>ContextToolbarRegistrar</c>; it only gets a ribbon
    /// button as well when it is ALSO added to the ribbon registry.
    /// </summary>
    public interface IContextToolbarCommand : ICommand
    {
        /// <summary>What the user right-clicks for the icon to appear (in each of <see cref="ICommand.DocumentTypes"/>).</summary>
        IReadOnlyList<ContextToolbarTarget> ContextTargets { get; }
    }
}
