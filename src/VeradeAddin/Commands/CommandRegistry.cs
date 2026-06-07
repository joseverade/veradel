using System.Collections.Generic;

namespace VeradeAddin.Commands
{
    /// <summary>
    /// The single place where commands are registered. The registrar builds the ribbon
    /// from this ordered list, and the add-in dispatches callbacks by list index.
    ///
    /// To add a command: construct it in the composition root and Add() it here.
    /// </summary>
    public sealed class CommandRegistry
    {
        private readonly List<ICommand> _commands = new List<ICommand>();

        public CommandRegistry Add(ICommand command)
        {
            _commands.Add(command);
            return this;
        }

        public IReadOnlyList<ICommand> Commands
        {
            get { return _commands; }
        }

        public int Count
        {
            get { return _commands.Count; }
        }

        /// <summary>Dispatch by ribbon index (used by the SolidWorks callback).</summary>
        public ICommand this[int index]
        {
            get { return _commands[index]; }
        }
    }
}
