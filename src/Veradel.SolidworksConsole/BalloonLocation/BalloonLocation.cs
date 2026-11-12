using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Veradel.SolidworksConsole.BalloonLocation
{
    public class BalloonLocation
    {


        /*
         * PROBLEMA: Nativamente en solidoworks no hay forma de identificar la posicion de los globos en base a la LDM
         * Solucion: Identificar todos los globos y sus posiciones (HOJA-ZONA) y generar un PDF para montadores:
         *   - Hojas del dibujo con las lineas de zona (0.1 mm, punteadas) y la etiqueta de cada zona
         *   - Paginas A4 con la LDM + columna POSICION ("1-A4, 2-C3")
         * El dibujo NO se modifica: las lineas se dibujan sobre el PDF exportado (PdfSharp).
         */



        private SldWorks swApp;
        private ModelDoc2 modelDoc2;
        private DrawingDoc drawingDoc;


        public BalloonLocation(SldWorks swApp)
        {
            this.swApp = swApp;
            modelDoc2 = swApp.ActiveDoc as ModelDoc2;
            drawingDoc = modelDoc2 as DrawingDoc;
        }

        public void Execute()
        {
            if (drawingDoc == null)
            {
                Console.WriteLine("El documento activo no es un dibujo.");
                return;
            }

            string activeSheet = ((Sheet)drawingDoc.GetCurrentSheet()).GetName();

            List<SheetWrapper> sheets = SetSheetWrappers();
            List<BomWrapper> boms = new List<BomWrapper>();

            foreach (SheetWrapper sheetWrapper in sheets)
            {
                // seteamos las zonas
                sheetWrapper.SetZones();

                // seteamos los globos y las LDM de la hoja
                sheetWrapper.SetBalloons(drawingDoc);
                boms.AddRange(sheetWrapper.GetBoms(drawingDoc));
            }

            drawingDoc.ActivateSheet(activeSheet);

            PrintDebug(sheets);

            BomWrapper bom = SelectBom(boms);
            if (bom == null) return;

            Dictionary<string, List<BalloonWrapper>> positions = GroupByItem(sheets);
            ReportUnmatched(bom, positions);

            string outPdf = GetOutputPath();
            string basePdf = Path.Combine(Path.GetTempPath(), "veradel_montaje_" + Guid.NewGuid().ToString("N") + ".pdf");

            try
            {
                if (!ExportPdf(basePdf, sheets)) return;

                MontadoresPdf.Build(basePdf, outPdf, sheets, bom, positions);
                Console.WriteLine("PDF generado: " + outPdf);
            }
            finally
            {
                if (File.Exists(basePdf)) File.Delete(basePdf);
            }
        }


        private List<SheetWrapper> SetSheetWrappers()
        {

            List<SheetWrapper> sheets = new List<SheetWrapper>();

            string[] names = (string[])drawingDoc.GetSheetNames();

            for (int i = 0; i < names.Length; i++)
            {
                Sheet sheet = drawingDoc.Sheet[names[i]];
                sheets.Add(new SheetWrapper(sheet, i + 1));
            }


            return sheets;
        }


        // Si hay varias LDM se pregunta cual usar
        private BomWrapper SelectBom(List<BomWrapper> boms)
        {
            if (boms.Count == 0)
            {
                Console.WriteLine("El dibujo no tiene ninguna LDM.");
                return null;
            }

            if (boms.Count == 1) return boms[0];

            Console.WriteLine("Hay varias LDM:");
            for (int i = 0; i < boms.Count; i++)
                Console.WriteLine("  " + (i + 1) + ") " + boms[i].Title + "  (hoja " + boms[i].SheetIndex + " - " + boms[i].SheetName + ", " + boms[i].Rows.Count + " filas)");

            while (true)
            {
                Console.Write("Elige LDM [1-" + boms.Count + "]: ");
                int choice;
                if (int.TryParse(Console.ReadLine(), out choice) && choice >= 1 && choice <= boms.Count)
                    return boms[choice - 1];
            }
        }


        // Nº de posicion -> globos que lo referencian
        private Dictionary<string, List<BalloonWrapper>> GroupByItem(List<SheetWrapper> sheets)
        {
            Dictionary<string, List<BalloonWrapper>> positions = new Dictionary<string, List<BalloonWrapper>>();

            foreach (BalloonWrapper balloon in sheets.SelectMany(s => s.Balloons))
            {
                if (string.IsNullOrEmpty(balloon.Text)) continue;

                List<BalloonWrapper> list;
                if (!positions.TryGetValue(balloon.Text, out list))
                {
                    list = new List<BalloonWrapper>();
                    positions.Add(balloon.Text, list);
                }
                list.Add(balloon);
            }

            return positions;
        }


        private void ReportUnmatched(BomWrapper bom, Dictionary<string, List<BalloonWrapper>> positions)
        {
            HashSet<string> items = new HashSet<string>(bom.Rows.Select(r => bom.GetItem(r)));

            List<string> noBalloon = items.Where(i => !positions.ContainsKey(i)).ToList();
            List<string> noBomRow = positions.Keys.Where(k => !items.Contains(k)).ToList();

            if (noBalloon.Count > 0)
                Console.WriteLine("Posiciones SIN GLOBO: " + string.Join(", ", noBalloon));
            if (noBomRow.Count > 0)
                Console.WriteLine("Globos que no estan en la LDM: " + string.Join(", ", noBomRow));
        }


        private bool ExportPdf(string path, List<SheetWrapper> sheets)
        {
            ExportPdfData pdfData = (ExportPdfData)swApp.GetExportFileData((int)swExportDataFileType_e.swExportPdfData);
            pdfData.SetSheets((int)swExportDataSheetsToExport_e.swExportData_ExportAllSheets, sheets.Select(s => s.SheetName).ToArray());

            int errors = 0, warnings = 0;
            bool ok = modelDoc2.Extension.SaveAs3(path, (int)swSaveAsVersion_e.swSaveAsCurrentVersion,
                (int)swSaveAsOptions_e.swSaveAsOptions_Silent, pdfData, null, ref errors, ref warnings);

            if (!ok || !File.Exists(path))
            {
                Console.WriteLine("Error exportando PDF. errors=" + errors + " warnings=" + warnings);
                return false;
            }
            return true;
        }


        // Junto al dibujo: <nombre>_montaje.pdf (si no esta guardado, al escritorio)
        private string GetOutputPath()
        {
            string drawingPath = modelDoc2.GetPathName();
            string dir = string.IsNullOrEmpty(drawingPath)
                ? System.Environment.GetFolderPath(System.Environment.SpecialFolder.Desktop)
                : Path.GetDirectoryName(drawingPath);
            string name = string.IsNullOrEmpty(drawingPath)
                ? modelDoc2.GetTitle()
                : Path.GetFileNameWithoutExtension(drawingPath);

            return Path.Combine(dir, name + "_montaje.pdf");
        }


        private void PrintDebug(List<SheetWrapper> sheets)
        {
            foreach (SheetWrapper s in sheets)
            {
                Console.WriteLine(string.Format("Hoja {0} '{1}': {2:0.#}x{3:0.#} mm, marco X[{4:0.##}..{5:0.##}] Y[{6:0.##}..{7:0.##}] mm",
                    s.SheetIndex, s.SheetName, s.Width * 1000, s.Height * 1000,
                    s.FrameLeft * 1000, s.FrameRight * 1000, s.FrameBottom * 1000, s.FrameTop * 1000));
                Console.WriteLine("  Lineas X (mm): " + string.Join(", ", s.LineX.Select(x => (x * 1000).ToString("0.##"))));
                Console.WriteLine("  Lineas Y (mm): " + string.Join(", ", s.LineY.Select(y => (y * 1000).ToString("0.##"))));
                foreach (BalloonWrapper b in s.Balloons)
                    Console.WriteLine(string.Format("  Globo '{0}' en ({1:0.#}, {2:0.#}) mm -> {3}  [vista {4}]",
                        b.Text, b.PosX * 1000, b.PosY * 1000, b.Position, b.ViewName));
            }
        }


    }


    public class BalloonWrapper
    {

        public int Page;
        public double PosX;
        public double PosY;
        public string Text;
        public string ViewName;
        public string Zone;

        // Formato PAGINA-REGION, ej: 1-A4
        public string Position { get { return Page + "-" + Zone; } }


        public BalloonWrapper(int Page, double PosX, double PosY, string Text, string Zone, string ViewName)
        {
            this.Page = Page;
            this.PosX = PosX;
            this.PosY = PosY;
            this.Text = Text;
            this.Zone = Zone;
            this.ViewName = ViewName;
        }


    }


    // Celda de zona (coordenadas de hoja, metros)
    public class ZoneCell
    {
        public double X1, Y1, X2, Y2;
        public string Label;
    }


    public class SheetWrapper
    {
        public List<BalloonWrapper> Balloons;
        public List<double> LineY;
        public List<double> LineX;
        public List<ZoneCell> Cells;
        public string SheetName;
        public int SheetIndex;

        // metros
        public double Width = 0;
        public double Height = 0;
        public double FrameLeft, FrameRight, FrameBottom, FrameTop;


        private Sheet SheetSw;

        // Paso del barrido para encontrar cambios de zona y precision de la biseccion (metros)
        private const double ScanStep = 0.002;
        private const double Tolerance = 0.00001;


        public SheetWrapper(Sheet SheetSw, int SheetIndex)
        {
            this.SheetSw = SheetSw;

            // Se llena el nombre automaticamente
            SheetName = SheetSw.GetName();
            this.SheetIndex = SheetIndex;

        }


        public void SetBalloons(DrawingDoc doc)
        {
            Balloons = new List<BalloonWrapper>();
            doc.ActivateSheet(SheetName);

            // La primera "vista" es la propia hoja: asi tambien salen los globos puestos sobre la hoja
            View view = (View)doc.GetFirstView();
            while (view != null)
            {
                object[] oNotes = (object[])view.GetNotes();
                if (oNotes != null)
                {
                    foreach (object o in oNotes)
                    {
                        Note note = o as Note;
                        if (note == null) continue;

                        if (note.IsStackedBalloon())
                        {
                            // El maestro trae toda la pila; los repetidos dan igual (se agrupa por HOJA-ZONA)
                            AddBalloon(note, view);
                            if (note.IsStackedBalloonMaster())
                            {
                                BalloonStack stack = note.GetBalloonStack();
                                object[] members = stack == null ? null : (object[])stack.Stack;
                                if (members != null)
                                    foreach (object m in members)
                                        if (m is Note) AddBalloon((Note)m, view);
                            }
                            continue;
                        }

                        if (!note.HasBalloon() && !note.IsBomBalloon()) continue;

                        AddBalloon(note, view);
                    }
                }

                view = (View)view.GetNextView();
            }
        }


        private void AddBalloon(Note note, View view)
        {
            // Centro del circulo (coordenadas de hoja, metros). La posicion de la anotacion es el punto
            // del texto y, si el globo esta junto a un limite de zona, cae en la zona vecina
            double[] pos = (double[])note.GetBalloonInfo();
            if (pos == null || pos.Length < 2)
            {
                Annotation annotation = (Annotation)note.GetAnnotation();
                pos = annotation == null ? null : (double[])annotation.GetPosition();
            }
            if (pos == null) return;

            string text = GetBalloonText(note);
            Balloons.Add(new BalloonWrapper(SheetIndex, pos[0], pos[1], text, SheetSw.GetDrawingZone(pos[0], pos[1]), view.GetName2()));
        }


        private static string GetBalloonText(Note note)
        {
            string text = null;

            // Globo de LDM: texto superior = nº de posicion
            if (note.IsBomBalloon())
                text = note.GetBomBalloonText(true);

            if (string.IsNullOrWhiteSpace(text))
                text = note.GetText();

            return text == null ? "" : text.Trim();
        }


        public List<BomWrapper> GetBoms(DrawingDoc doc)
        {
            List<BomWrapper> boms = new List<BomWrapper>();
            doc.ActivateSheet(SheetName);

            View view = (View)doc.GetFirstView();
            while (view != null)
            {
                object[] tables = (object[])view.GetTableAnnotations();
                if (tables != null)
                {
                    foreach (object o in tables)
                    {
                        TableAnnotation table = o as TableAnnotation;
                        if (table != null && table.Type == (int)swTableAnnotationType_e.swTableAnnotation_BillOfMaterials)
                            boms.Add(new BomWrapper(table, SheetIndex, SheetName));
                    }
                }

                view = (View)view.GetNextView();
            }

            return boms;
        }


        public void SetZones()
        {
            double w = 0, h = 0;
            SheetSw.GetSize(ref w, ref h);
            Width = w;
            Height = h;

            // Marco de zonas = hoja menos margenes del editor de zonas
            if (!SetFrameFromMargins(w, h) || FrameRight <= FrameLeft || FrameTop <= FrameBottom)
            {
                FrameLeft = 0; FrameRight = w;
                FrameBottom = 0; FrameTop = h;
            }

            double xMid = (FrameLeft + FrameRight) / 2;
            double yMid = (FrameBottom + FrameTop) / 2;

            // En horizontal solo cambia la columna, en vertical solo la fila
            Func<double, string> zoneAtX = x => SheetSw.GetDrawingZone(x, yMid);
            Func<double, string> zoneAtY = y => SheetSw.GetDrawingZone(xMid, y);

            LineX = FindBoundaries(FrameLeft, FrameRight, zoneAtX);
            LineY = FindBoundaries(FrameBottom, FrameTop, zoneAtY);

            TrimFrame(LineX, zoneAtX, ref FrameLeft, ref FrameRight);
            TrimFrame(LineY, zoneAtY, ref FrameBottom, ref FrameTop);

            SetCells();
        }


        // GetZoneMargin no existe en la API de SW 2021 (si desde 2022): se llama por IDispatch
        // para que compile con cualquier interop y en 2021 se cae al barrido de la hoja entera
        private bool SetFrameFromMargins(double w, double h)
        {
            try
            {
                FrameLeft = GetZoneMargin(3);       // swZoneLeftMargin
                FrameRight = w - GetZoneMargin(2);  // swZoneRightMargin
                FrameBottom = GetZoneMargin(1);     // swZoneBottomMargin
                FrameTop = h - GetZoneMargin(0);    // swZoneTopMargin
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }


        private double GetZoneMargin(int margin)
        {
            return Convert.ToDouble(SheetSw.GetType().InvokeMember("GetZoneMargin", BindingFlags.InvokeMethod, null, SheetSw, new object[] { margin }));
        }


        // Si el barrido empezo en el borde de la hoja y ahi no hay zona (margen), el primer/ultimo
        // cambio es el borde del marco y no una linea de zona
        private void TrimFrame(List<double> lines, Func<double, string> zoneAt, ref double start, ref double end)
        {
            if (lines.Count > 0 && string.IsNullOrEmpty(zoneAt(start + Tolerance)))
            {
                start = lines[0];
                lines.RemoveAt(0);
            }

            if (lines.Count > 0 && string.IsNullOrEmpty(zoneAt(end - Tolerance)))
            {
                end = lines[lines.Count - 1];
                lines.RemoveAt(lines.Count - 1);
            }
        }


        // Barrido + biseccion sobre GetDrawingZone: sirve para zonas uniformes y "50 mm desde el centro"
        private static List<double> FindBoundaries(double from, double to, Func<double, string> zoneAt)
        {
            List<double> boundaries = new List<double>();

            double prevPos = from + Tolerance;
            string prevZone = zoneAt(prevPos);

            for (double pos = from + ScanStep; pos < to - Tolerance; pos += ScanStep)
            {
                string zone = zoneAt(pos);

                if (zone != prevZone)
                {
                    double lowerLimit = prevPos;
                    double upperLimit = pos;

                    while (upperLimit - lowerLimit > Tolerance)
                    {
                        double mean = (lowerLimit + upperLimit) / 2;

                        if (zoneAt(mean) == prevZone)
                            lowerLimit = mean;
                        else
                            upperLimit = mean;
                    }

                    boundaries.Add((lowerLimit + upperLimit) / 2);
                    prevZone = zone;
                }

                prevPos = pos;
            }

            return boundaries;
        }


        private void SetCells()
        {
            Cells = new List<ZoneCell>();

            List<double> xs = new List<double> { FrameLeft };
            xs.AddRange(LineX);
            xs.Add(FrameRight);

            List<double> ys = new List<double> { FrameBottom };
            ys.AddRange(LineY);
            ys.Add(FrameTop);

            for (int i = 0; i < xs.Count - 1; i++)
            {
                for (int j = 0; j < ys.Count - 1; j++)
                {
                    string label = SheetSw.GetDrawingZone((xs[i] + xs[i + 1]) / 2, (ys[j] + ys[j + 1]) / 2);
                    if (string.IsNullOrEmpty(label)) continue;

                    Cells.Add(new ZoneCell { X1 = xs[i], X2 = xs[i + 1], Y1 = ys[j], Y2 = ys[j + 1], Label = label });
                }
            }
        }

    }


}
