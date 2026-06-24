using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using VeradeAddin.Models;

namespace VeradeAddin.Services
{
    /// <summary>
    /// Persists, per drawing (keyed by its file path), the colour mapping last applied by "Colorear
    /// aristas", so "Líneas a negro" can re-derive and blacken EXACTLY those edges after the drawing has
    /// been closed and reopened (when the in-memory tracking is gone). Only RGB values + part paths are
    /// stored — nothing fragile (no persistent entity references). Plain text under
    /// <c>%AppData%\VeradelAddin\edgecolors\&lt;hash&gt;.txt</c>; no external dependencies.
    ///
    /// Best-effort: every operation swallows IO errors. Unsaved drawings (no path) are not persisted.
    /// </summary>
    public sealed class EdgeColorStore
    {
        private static string Dir
        {
            get
            {
                var d = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "VeradelAddin", "edgecolors");
                Directory.CreateDirectory(d);
                return d;
            }
        }

        // Filename is a hash of the drawing path; the real path is stored as line 1 to guard collisions.
        private static string FileFor(string drawingPath)
        {
            using (var md5 = MD5.Create())
            {
                var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(drawingPath.ToLowerInvariant()));
                var sb = new StringBuilder();
                foreach (var b in hash) sb.Append(b.ToString("x2"));
                return Path.Combine(Dir, sb.ToString() + ".txt");
            }
        }

        public void Save(string drawingPath, IEnumerable<EdgeColorMapping> mappings)
        {
            if (string.IsNullOrEmpty(drawingPath) || mappings == null) return;
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine(drawingPath);
                foreach (var m in mappings)
                {
                    sb.AppendLine(string.Join("\t", new[]
                    {
                        m.PartPath ?? string.Empty,
                        m.SourceR.ToString(), m.SourceG.ToString(), m.SourceB.ToString(),
                        m.TargetR.ToString(), m.TargetG.ToString(), m.TargetB.ToString()
                    }));
                }
                File.WriteAllText(FileFor(drawingPath), sb.ToString(), Encoding.UTF8);
            }
            catch { /* persistence is best-effort */ }
        }

        public List<EdgeColorMapping> Load(string drawingPath)
        {
            if (string.IsNullOrEmpty(drawingPath)) return null;
            try
            {
                var file = FileFor(drawingPath);
                if (!File.Exists(file)) return null;

                var lines = File.ReadAllLines(file, Encoding.UTF8);
                if (lines.Length < 2) return null;
                // Guard against an (extremely unlikely) hash collision pointing at another drawing.
                if (!string.Equals(lines[0].Trim(), drawingPath, StringComparison.OrdinalIgnoreCase)) return null;

                var list = new List<EdgeColorMapping>();
                for (int i = 1; i < lines.Length; i++)
                {
                    var p = lines[i].Split('\t');
                    if (p.Length < 7) continue;
                    list.Add(new EdgeColorMapping
                    {
                        PartPath = p[0],
                        SourceR = ParseInt(p[1]), SourceG = ParseInt(p[2]), SourceB = ParseInt(p[3]),
                        TargetR = ParseInt(p[4]), TargetG = ParseInt(p[5]), TargetB = ParseInt(p[6])
                    });
                }
                return list.Count > 0 ? list : null;
            }
            catch { return null; }
        }

        public void Delete(string drawingPath)
        {
            if (string.IsNullOrEmpty(drawingPath)) return;
            try
            {
                var f = FileFor(drawingPath);
                if (File.Exists(f)) File.Delete(f);
            }
            catch { /* best-effort */ }
        }

        private static int ParseInt(string s)
        {
            int v;
            return int.TryParse(s, out v) ? v : 0;
        }
    }
}
