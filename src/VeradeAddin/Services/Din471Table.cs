using System;
using System.IO;

namespace VeradeAddin.Services
{
    /// <summary>
    /// Provides the DIN 471 retaining-ring groove table that backs the "usar DIN 471" button in the
    /// bolt configurator. The table is user-editable data, so it lives <b>outside the repo / install
    /// dir</b> in the per-user app-data folder
    /// <c>%LOCALAPPDATA%\VeradeAddin\din471.json</c> (the same root WebView2 already uses). On first
    /// run the file is seeded from the embedded <see cref="DefaultJson"/> template; afterwards the
    /// user edits that file and it persists across rebuilds/reinstalls.
    ///
    /// The host never parses it: it hands the raw JSON to the WebView2 page, whose JS does the lookup
    /// (row where <c>d == Ø2</c> → fills D3 and E1). This keeps the add-in free of any JSON library on
    /// .NET Framework 4.8.
    /// </summary>
    internal static class Din471Table
    {
        private const string FileName = "din471.json";

        /// <summary>Seed written on first run; rows empty until the user fills the table.</summary>
        private const string DefaultJson =
            "{\n" +
            "  \"standard\": \"DIN 471\",\n" +
            "  \"_comment\": \"Anillo de retencion para ejes. Rellena 'rows'. d = diametro nominal del eje (= O2/D2 del vastago), d3 = diametro de fondo de ranura (= D3), m = ancho de ranura (= E1). Todo en mm. El boton 'usar DIN 471 = Si' busca la fila d == D2 y rellena D3 y E1.\",\n" +
            "  \"_example\": { \"d\": 20, \"d3\": 19.0, \"m\": 1.3 },\n" +
            "  \"rows\": []\n" +
            "}\n";

        /// <summary>Empty-but-valid table used if the file cannot be read or written.</summary>
        private const string EmptyJson = "{\"standard\":\"DIN 471\",\"rows\":[]}";

        /// <summary>Full path to the user's editable table, under %LOCALAPPDATA%\VeradeAddin.</summary>
        public static string FilePath
        {
            get
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "VeradeAddin");
                return Path.Combine(dir, FileName);
            }
        }

        /// <summary>
        /// Returns the raw JSON text of the table. Seeds the file from the embedded default on first
        /// run; falls back to a valid empty table if the folder is unreadable/unwritable.
        /// </summary>
        public static string LoadRawJson()
        {
            try
            {
                string path = FilePath;
                if (!File.Exists(path))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    File.WriteAllText(path, DefaultJson);
                    return DefaultJson;
                }
                string text = File.ReadAllText(path);
                return string.IsNullOrWhiteSpace(text) ? EmptyJson : text;
            }
            catch
            {
                return EmptyJson;
            }
        }
    }
}
