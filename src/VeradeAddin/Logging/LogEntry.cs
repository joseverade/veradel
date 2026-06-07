using System;

namespace VeradeAddin.Logging
{
    /// <summary>
    /// Structured log record. This is the contract handed to every <see cref="ILogSink"/>,
    /// so swapping the file sink for a telemetry/analytics sink later requires no change
    /// to the data captured here.
    /// </summary>
    public sealed class LogEntry
    {
        public DateTime TimestampUtc { get; set; }

        public string CommandName { get; set; }

        public string DocumentType { get; set; }

        public LogOutcome Outcome { get; set; }

        /// <summary>Human-readable detail of what happened (e.g. "Opened folder").</summary>
        public string Detail { get; set; }

        /// <summary>Exception/error text. Null unless <see cref="Outcome"/> is Error.</summary>
        public string Error { get; set; }
    }
}
