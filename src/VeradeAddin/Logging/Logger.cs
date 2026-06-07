using System;
using System.Collections.Generic;

namespace VeradeAddin.Logging
{
    /// <summary>
    /// Default logger. Stamps each call with the UTC timestamp and forwards the record
    /// to every configured sink. Sink failures are swallowed so logging can never break
    /// a command. Multiple sinks are supported (e.g. file + telemetry simultaneously).
    /// </summary>
    public sealed class Logger : ILogger
    {
        private readonly IReadOnlyList<ILogSink> _sinks;

        public Logger(IEnumerable<ILogSink> sinks)
        {
            _sinks = new List<ILogSink>(sinks ?? new ILogSink[0]);
        }

        public void Log(string commandName, string documentType, LogOutcome outcome, string detail = null, string error = null)
        {
            var entry = new LogEntry
            {
                TimestampUtc = DateTime.UtcNow,
                CommandName = commandName,
                DocumentType = documentType,
                Outcome = outcome,
                Detail = detail,
                Error = error
            };

            foreach (var sink in _sinks)
            {
                try
                {
                    sink.Write(entry);
                }
                catch
                {
                    // Logging must never throw into a command. Drop on failure.
                }
            }
        }
    }
}
