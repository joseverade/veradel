namespace VeradeAddin.Logging
{
    /// <summary>
    /// Destination for log records. The current implementation writes JSON lines to
    /// disk; a future telemetry/analytics sink (HTTP, queue, etc.) implements this same
    /// interface and is registered in the composition root — nothing else changes.
    /// </summary>
    public interface ILogSink
    {
        void Write(LogEntry entry);
    }
}
