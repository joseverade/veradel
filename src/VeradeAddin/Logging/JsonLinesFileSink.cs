using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace VeradeAddin.Logging
{
    /// <summary>
    /// Local file sink. Appends one JSON object per line (JSON Lines / NDJSON) to a
    /// daily-rolled file under %APPDATA%\VeradelAddin\logs. Structured output keeps the
    /// records machine-readable for a later analytics pipeline. JSON is hand-written to
    /// avoid pulling in a serializer dependency for this fixed, tiny schema.
    /// </summary>
    public sealed class JsonLinesFileSink : ILogSink
    {
        private readonly string _directory;
        private readonly object _gate = new object();

        public JsonLinesFileSink(string directory = null)
        {
            _directory = directory ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "VeradelAddin", "logs");
        }

        public void Write(LogEntry entry)
        {
            Directory.CreateDirectory(_directory);
            var file = Path.Combine(_directory,
                "veradel-addin-" + DateTime.UtcNow.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".jsonl");

            var line = BuildJson(entry);

            lock (_gate)
            {
                File.AppendAllText(file, line + Environment.NewLine, Encoding.UTF8);
            }
        }

        private static string BuildJson(LogEntry e)
        {
            var sb = new StringBuilder(256);
            sb.Append('{');
            AppendField(sb, "timestamp", e.TimestampUtc.ToString("o", CultureInfo.InvariantCulture), true);
            AppendField(sb, "command", e.CommandName, true);
            AppendField(sb, "documentType", e.DocumentType, true);
            AppendField(sb, "outcome", e.Outcome.ToString(), true);
            AppendField(sb, "detail", e.Detail, true);
            AppendField(sb, "error", e.Error, false);
            sb.Append('}');
            return sb.ToString();
        }

        private static void AppendField(StringBuilder sb, string name, string value, bool trailingComma)
        {
            sb.Append('"').Append(name).Append("\":");
            if (value == null)
            {
                sb.Append("null");
            }
            else
            {
                sb.Append('"').Append(Escape(value)).Append('"');
            }
            if (trailingComma)
            {
                sb.Append(',');
            }
        }

        private static string Escape(string s)
        {
            var sb = new StringBuilder(s.Length + 8);
            foreach (var c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20)
                        {
                            sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            sb.Append(c);
                        }
                        break;
                }
            }
            return sb.ToString();
        }
    }
}
