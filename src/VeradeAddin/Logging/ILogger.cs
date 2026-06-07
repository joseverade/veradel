namespace VeradeAddin.Logging
{
    /// <summary>
    /// Logging abstraction every command depends on. Implementations stamp the
    /// timestamp and fan the record out to one or more <see cref="ILogSink"/>s.
    /// </summary>
    public interface ILogger
    {
        void Log(string commandName, string documentType, LogOutcome outcome, string detail = null, string error = null);
    }
}
