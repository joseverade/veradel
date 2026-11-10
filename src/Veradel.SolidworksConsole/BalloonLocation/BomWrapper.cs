using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using System.Collections.Generic;
using System.Linq;

namespace Veradel.SolidworksConsole.BalloonLocation
{
    // Copia de la LDM leida celda a celda (solo filas/columnas visibles, texto tal cual se ve)
    public class BomWrapper
    {
        public string Title;
        public int SheetIndex;
        public string SheetName;
        public List<string> Headers = new List<string>();
        public List<double> ColumnWidths = new List<double>();
        public List<string[]> Rows = new List<string[]>();
        public int ItemColumn;


        public BomWrapper(TableAnnotation table, int SheetIndex, string SheetName)
        {
            this.SheetIndex = SheetIndex;
            this.SheetName = SheetName;

            int rowCount = table.RowCount;
            int columnCount = table.ColumnCount;

            for (int c = 0; c < columnCount; c++)
                ColumnWidths.Add(table.GetColumnWidth(c));

            // Cabecera arriba o abajo (las LDM pegadas al cajetin suelen tenerla abajo)
            int headerCount = table.GetHeaderCount();
            bool headerAtBottom = table.GetHeaderStyle() == (int)swTableHeaderPosition_e.swTableHeader_Bottom;

            int headerRow = headerCount == 0 ? -1 : headerAtBottom ? rowCount - headerCount : headerCount - 1;
            int firstData = headerAtBottom ? 0 : headerCount;
            int lastData = headerAtBottom ? rowCount - headerCount - 1 : rowCount - 1;

            for (int c = 0; c < columnCount; c++)
                Headers.Add(headerRow >= 0 ? Clean(table.DisplayedText2[headerRow, c, false]) : table.GetColumnTitle2(c, false));

            for (int r = firstData; r <= lastData; r++)
            {
                string[] row = new string[columnCount];
                for (int c = 0; c < columnCount; c++)
                    row[c] = Clean(table.DisplayedText2[r, c, false]);
                Rows.Add(row);
            }

            // Con la cabecera abajo la posicion 1 esta en la ultima fila: se invierte para leer de arriba a abajo
            if (headerAtBottom) Rows.Reverse();

            ItemColumn = FindItemColumn(table, columnCount);

            Title = table.TitleVisible && !string.IsNullOrWhiteSpace(table.Title) ? table.Title : "LDM";
        }


        public string GetItem(string[] row)
        {
            return row[ItemColumn];
        }


        private int FindItemColumn(TableAnnotation table, int columnCount)
        {
            for (int c = 0; c < columnCount; c++)
                if (table.GetColumnType2(c, false) == (int)swTableColumnTypes_e.swBomTableColumnType_ItemNumber)
                    return c;

            // Columna de usuario: se busca por el titulo
            string[] names = { "POS", "ELEM", "ITEM", "Nº", "N.º", "NO." };
            for (int c = 0; c < Headers.Count; c++)
                if (names.Any(n => Headers[c].ToUpperInvariant().StartsWith(n)))
                    return c;

            return 0;
        }


        private static string Clean(string text)
        {
            return text == null ? "" : text.Replace("\r", "").Trim();
        }
    }
}
