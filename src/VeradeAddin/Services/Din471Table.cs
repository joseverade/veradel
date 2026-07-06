using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace VeradeAddin.Services
{
    /// <summary>
    /// Provides the DIN 471 retaining-ring groove table injected into the configurator page as
    /// <c>window.DIN471</c>. The source of truth is the plain-text table
    /// <c>resources\data\din_471.txt</c> in the repo (whitespace-separated columns:
    /// <c>index d d3 m</c>, all in mm) — found by walking up from the add-in DLL folder. Rows that
    /// do not parse or are physically impossible (d3 ≥ d) are skipped. Fallbacks, in order: the
    /// same file next to the DLL (<c>data\din_471.txt</c>), the per-user copy under
    /// <c>%LOCALAPPDATA%\VeradeAddin</c>, and finally the legacy editable
    /// <c>%LOCALAPPDATA%\VeradeAddin\din471.json</c>.
    ///
    /// The host never needs a JSON library: the txt is converted to the JSON literal by string
    /// building, and the page's JS does the lookup (row where d == level Ø → fills E1/D3).
    /// </summary>
    internal static class Din471Table
    {
        private const string TxtFileName = "din_471.txt";
        private const string LegacyJsonFileName = "din471.json";

        /// <summary>Empty-but-valid table used if no source can be read.</summary>
        private const string EmptyJson = "{\"standard\":\"DIN 471\",\"rows\":[]}";

        /// <summary>Legacy per-user JSON path, kept as last-resort fallback.</summary>
        public static string FilePath
        {
            get
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "VeradeAddin");
                return Path.Combine(dir, LegacyJsonFileName);
            }
        }

        /// <summary>
        /// Returns the raw JSON text of the table (never throws): the parsed
        /// <c>resources\data\din_471.txt</c>, else the legacy user JSON, else an empty table.
        /// </summary>
        public static string LoadRawJson()
        {
            try
            {
                string txtPath = FindTxtPath();
                if (txtPath != null)
                {
                    string json = ParseTxtToJson(File.ReadAllLines(txtPath));
                    if (json != null) return json;
                }

                string legacy = FilePath;
                if (File.Exists(legacy))
                {
                    string text = File.ReadAllText(legacy);
                    if (!string.IsNullOrWhiteSpace(text)) return text;
                }
                return EmptyJson;
            }
            catch
            {
                return EmptyJson;
            }
        }

        /// <summary>
        /// Locates din_471.txt: next to the DLL (data\din_471.txt), then resources\data\din_471.txt
        /// walking up from the DLL folder (covers running from bin\ inside the repo), then the
        /// per-user app-data folder. Null when nowhere to be found.
        /// </summary>
        private static string FindTxtPath()
        {
            try
            {
                string dllDir = Path.GetDirectoryName(typeof(Din471Table).Assembly.Location);
                if (!string.IsNullOrEmpty(dllDir))
                {
                    string beside = Path.Combine(dllDir, "data", TxtFileName);
                    if (File.Exists(beside)) return beside;

                    string dir = dllDir;
                    for (int up = 0; up < 8 && dir != null; up++)
                    {
                        string candidate = Path.Combine(dir, "resources", "data", TxtFileName);
                        if (File.Exists(candidate)) return candidate;
                        dir = Path.GetDirectoryName(dir);
                    }
                }

                string userCopy = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "VeradeAddin", TxtFileName);
                if (File.Exists(userCopy)) return userCopy;
            }
            catch
            {
                // fall through to null
            }
            return null;
        }

        /// <summary>
        /// Converts the txt rows to the window.DIN471 JSON. Accepts 4 columns (index d d3 m) or
        /// 3 (d d3 m); skips blank/malformed lines and impossible rows (d3 ≥ d). Returns null when
        /// no row survives, so the caller can fall back.
        /// </summary>
        private static string ParseTxtToJson(string[] lines)
        {
            var sb = new StringBuilder("{\"standard\":\"DIN 471\",\"rows\":[");
            int count = 0;
            foreach (string line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                string[] cols = line.Split(new[] { '\t', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries);
                int first = cols.Length >= 4 ? 1 : cols.Length == 3 ? 0 : -1;
                if (first < 0) continue;

                double d, d3, m;
                if (!TryNum(cols[first], out d) || !TryNum(cols[first + 1], out d3) || !TryNum(cols[first + 2], out m))
                {
                    continue;
                }
                if (!(d > 0) || !(d3 > 0) || !(m > 0) || !(d3 < d)) continue;

                if (count > 0) sb.Append(',');
                sb.Append("{\"d\":").Append(Num(d))
                  .Append(",\"d3\":").Append(Num(d3))
                  .Append(",\"m\":").Append(Num(m)).Append('}');
                count++;
            }
            if (count == 0) return null;
            return sb.Append("]}").ToString();
        }

        private static bool TryNum(string s, out double value)
        {
            // The table uses '.' decimals, but tolerate a stray ',' from manual edits.
            return double.TryParse(s.Replace(',', '.'), NumberStyles.Float,
                CultureInfo.InvariantCulture, out value);
        }

        private static string Num(double v)
        {
            return v.ToString("R", CultureInfo.InvariantCulture);
        }
    }
}
