using PdfSharp;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Annotations;
using PdfSharp.Pdf.IO;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Veradel.SolidworksConsole.BalloonLocation
{
    // Post-proceso del PDF exportado por SolidWorks:
    //   1) lineas de zona punteadas de 0.1 mm + etiqueta en cada zona sobre cada hoja
    //   2) paginas A4 al final con la LDM + columna POSICION (con enlace a la hoja)
    public static class MontadoresPdf
    {
        private const double PointsPerMeter = 72.0 / 0.0254;

        private const double ZoneLineWidthMm = 0.1;
        private static readonly XColor ZoneLineColor = XColor.FromArgb(110, 110, 110);
        private static readonly XColor ZoneLabelColor = XColor.FromArgb(150, 150, 150);
        private const double ZoneLabelHeightMm = 2.5;

        private const double A4MarginMm = 12;
        private const double CellPaddingPt = 2;
        private const double FontSize = 8;
        private const string PositionHeader = "POSICIÓN";
        private const string NoBalloon = "SIN GLOBO";

        // Al pinchar una posicion el visor salta con zoom fijo al globo. El visor no dice el tamaño de su
        // ventana: se supone un panel de ~380x200 mm al 100 % (pantalla normal) para centrar el globo
        private const double FocusZoom = 2.0;
        private const double ViewportWidthMm = 380;
        private const double ViewportHeightMm = 200;
        private const double FocusMarginMm = 20;


        public static void Build(string basePdf, string outPdf, List<SheetWrapper> sheets, BomWrapper bom,
            Dictionary<string, List<BalloonWrapper>> positions)
        {
            using (PdfDocument doc = PdfReader.Open(basePdf, PdfDocumentOpenMode.Modify))
            {
                if (doc.PageCount != sheets.Count)
                    Console.WriteLine("Aviso: el PDF tiene " + doc.PageCount + " paginas y el dibujo " + sheets.Count + " hojas.");

                for (int i = 0; i < Math.Min(doc.PageCount, sheets.Count); i++)
                    DrawZones(doc.Pages[i], sheets[i]);

                AddBomPages(doc, sheets, bom, positions);

                doc.Save(outPdf);
            }
        }


        private static double Mm(double mm)
        {
            return mm * 72.0 / 25.4;
        }


        private static void DrawZones(PdfPage page, SheetWrapper sheet)
        {
            if (sheet.Width <= 0 || sheet.Height <= 0) return;

            // El PDF sale a tamaño real; se escala por si la pagina difiere un poco
            double sx = page.Width.Point / sheet.Width;
            double sy = page.Height.Point / sheet.Height;
            if (Math.Abs(sx / PointsPerMeter - 1) > 0.05 || Math.Abs(sy / PointsPerMeter - 1) > 0.05)
                Console.WriteLine("Aviso: la pagina de la hoja " + sheet.SheetIndex + " no coincide con el tamaño de hoja.");

            Func<double, double> X = x => x * sx;
            Func<double, double> Y = y => (sheet.Height - y) * sy;

            using (XGraphics gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append))
            {
                // Punteado: el patron va en multiplos del grosor (0.3 mm trazo / 0.8 mm hueco)
                XPen pen = new XPen(ZoneLineColor, Mm(ZoneLineWidthMm));
                pen.DashStyle = XDashStyle.Custom;
                pen.DashPattern = new double[] { 3, 8 };

                foreach (double x in sheet.LineX)
                    gfx.DrawLine(pen, X(x), Y(sheet.FrameBottom), X(x), Y(sheet.FrameTop));

                foreach (double y in sheet.LineY)
                    gfx.DrawLine(pen, X(sheet.FrameLeft), Y(y), X(sheet.FrameRight), Y(y));

                // Etiqueta en la esquina superior izquierda de cada zona: en A0 el borde queda lejos
                XFont labelFont = new XFont("Arial", Mm(ZoneLabelHeightMm), XFontStyle.Regular, new XPdfFontOptions(PdfFontEncoding.Unicode));
                XBrush labelBrush = new XSolidBrush(ZoneLabelColor);
                foreach (ZoneCell cell in sheet.Cells)
                    gfx.DrawString(cell.Label, labelFont, labelBrush, X(cell.X1) + Mm(1), Y(cell.Y2) + Mm(1), XStringFormats.TopLeft);
            }
        }


        // ---------------------------------------------------------------- LDM en A4

        private class BomLayout
        {
            public PdfDocument Doc;
            public PdfPage Page;
            public XGraphics Gfx;
            public double Y;
            public double Left, Top, Bottom, Width;
            public double[] ColumnWidths;
            public List<PdfPage> Pages = new List<PdfPage>();
            public List<SheetWrapper> Sheets;
        }


        // Enlace a un punto de la hoja con zoom (/XYZ izquierda arriba zoom). AddDocumentLink de PdfSharp
        // escribe /XYZ null null 0 (conserva zoom y scroll de la LDM) y /FitR lo ignoran Edge/Chrome
        private class ZoomLink : PdfAnnotation
        {
            public ZoomLink(PdfDocument doc, PdfRectangle rect, PdfPage target, double left, double top, double zoom)
                : base(doc)
            {
                Elements.SetName("/Subtype", "/Link");
                Rectangle = rect;
                Elements["/Border"] = new PdfArray(doc, new PdfInteger(0), new PdfInteger(0), new PdfInteger(0));
                Elements["/Dest"] = new PdfArray(doc, target.Reference, new PdfName("/XYZ"),
                    new PdfReal(left), new PdfReal(top), new PdfReal(zoom));
            }
        }


        private static void AddBomPages(PdfDocument doc, List<SheetWrapper> sheets, BomWrapper bom, Dictionary<string, List<BalloonWrapper>> positions)
        {
            XPdfFontOptions unicode = new XPdfFontOptions(PdfFontEncoding.Unicode);
            XFont font = new XFont("Arial", FontSize, XFontStyle.Regular, unicode);
            XFont bold = new XFont("Arial", FontSize, XFontStyle.Bold, unicode);

            List<string> headers = new List<string>(bom.Headers) { PositionHeader };

            BomLayout layout = new BomLayout { Doc = doc, Sheets = sheets };
            layout.ColumnWidths = GetColumnWidths(bom, A4Width() - 2 * Mm(A4MarginMm));

            NewPage(layout, headers, bold);

            foreach (string[] row in bom.Rows)
            {
                List<string> cells = new List<string>(row);
                List<BalloonWrapper> balloons;
                List<IGrouping<string, BalloonWrapper>> zones = positions.TryGetValue(bom.GetItem(row), out balloons)
                    ? GroupPositions(balloons)
                    : new List<IGrouping<string, BalloonWrapper>>();
                List<string> tokens = zones.Select(z => z.Key).ToList();

                double height = MeasureRow(layout.Gfx, font, cells, tokens, layout.ColumnWidths);
                if (layout.Y + height > layout.Bottom)
                    NewPage(layout, headers, bold);

                DrawRow(layout, font, cells, zones, height);
            }

            layout.Gfx.Dispose();

            // Pie: pagina n/m de la LDM
            for (int i = 0; i < layout.Pages.Count; i++)
            {
                using (XGraphics gfx = XGraphics.FromPdfPage(layout.Pages[i], XGraphicsPdfPageOptions.Append))
                {
                    string footer = (i + 1) + " / " + layout.Pages.Count;
                    gfx.DrawString(footer, font, XBrushes.Gray,
                        new XRect(layout.Left, layout.Bottom + Mm(3), layout.Width, FontSize), XStringFormats.TopRight);
                }
            }
        }


        private static double A4Width() { return Mm(210); }
        private static double A4Height() { return Mm(297); }


        // Columnas de la LDM proporcionales a SolidWorks; POSICION se lleva un 25 %
        private static double[] GetColumnWidths(BomWrapper bom, double total)
        {
            int n = bom.Headers.Count;
            double positionWidth = total * 0.25;
            double rest = total - positionWidth;

            double swTotal = bom.ColumnWidths.Where(w => w > 0).Sum();
            double[] widths = new double[n + 1];
            for (int c = 0; c < n; c++)
            {
                double w = c < bom.ColumnWidths.Count ? bom.ColumnWidths[c] : 0;
                widths[c] = swTotal > 0 && w > 0 ? rest * w / swTotal : rest / n;
            }
            widths[n] = positionWidth;
            return widths;
        }


        private static void NewPage(BomLayout layout, List<string> headers, XFont bold)
        {
            if (layout.Gfx != null) layout.Gfx.Dispose();

            layout.Page = layout.Doc.AddPage();
            layout.Page.Size = PageSize.A4;
            layout.Page.Orientation = PageOrientation.Portrait;
            layout.Pages.Add(layout.Page);
            layout.Gfx = XGraphics.FromPdfPage(layout.Page);

            layout.Left = Mm(A4MarginMm);
            layout.Top = Mm(A4MarginMm);
            layout.Width = A4Width() - 2 * Mm(A4MarginMm);
            layout.Bottom = A4Height() - Mm(A4MarginMm) - Mm(6);

            layout.Y = layout.Top;

            // Cabecera repetida en cada pagina
            double height = MeasureRow(layout.Gfx, bold, headers, null, layout.ColumnWidths);
            layout.Gfx.DrawRectangle(new XSolidBrush(XColor.FromArgb(225, 225, 225)), layout.Left, layout.Y, layout.Width, height);
            DrawCells(layout, bold, headers, height);
            layout.Y += height;
        }


        private static double MeasureRow(XGraphics gfx, XFont font, List<string> cells, List<string> tokens, double[] widths)
        {
            int lines = 1;
            for (int c = 0; c < cells.Count; c++)
                lines = Math.Max(lines, WrapText(gfx, font, cells[c], widths[c] - 2 * CellPaddingPt).Count);

            if (tokens != null)
                lines = Math.Max(lines, WrapTokens(gfx, font, PositionTokens(tokens), widths[widths.Length - 1] - 2 * CellPaddingPt).Count);

            return lines * font.GetHeight() + 2 * CellPaddingPt;
        }


        private static void DrawRow(BomLayout layout, XFont font, List<string> cells, List<IGrouping<string, BalloonWrapper>> zones, double height)
        {
            DrawCells(layout, font, cells, height);

            // Columna POSICION: cada "1-A4" enlaza a sus globos en la hoja
            double x = layout.Left + layout.ColumnWidths.Take(cells.Count).Sum();
            double width = layout.ColumnWidths[cells.Count];
            XGraphics gfx = layout.Gfx;

            if (zones.Count == 0)
            {
                gfx.DrawString(NoBalloon, font, new XSolidBrush(XColor.FromArgb(180, 0, 0)), x + CellPaddingPt, layout.Y + CellPaddingPt, XStringFormats.TopLeft);
            }
            else
            {
                Dictionary<string, List<BalloonWrapper>> byPosition = zones.ToDictionary(z => z.Key, z => z.ToList());
                double lineHeight = font.GetHeight();
                List<List<string>> lines = WrapTokens(gfx, font, PositionTokens(zones.Select(z => z.Key).ToList()), width - 2 * CellPaddingPt);
                for (int l = 0; l < lines.Count; l++)
                {
                    double tx = x + CellPaddingPt;
                    double ty = layout.Y + CellPaddingPt + l * lineHeight;
                    foreach (string token in lines[l])
                    {
                        gfx.DrawString(token, font, XBrushes.Black, tx, ty, XStringFormats.TopLeft);

                        // El enlace cubre solo "1-A4", sin el ", "
                        string position = token.Trim().TrimEnd(',');
                        List<BalloonWrapper> balloons;
                        if (byPosition.TryGetValue(position, out balloons))
                            AddZoomLink(layout, new XRect(tx, ty, gfx.MeasureString(position, font).Width, lineHeight), balloons);

                        tx += gfx.MeasureString(token, font).Width;
                    }
                }
            }

            XPen grid = new XPen(XColors.Gray, 0.3);
            gfx.DrawRectangle(grid, x, layout.Y, width, height);

            layout.Y += height;
        }


        private static void DrawCells(BomLayout layout, XFont font, List<string> cells, double height)
        {
            XGraphics gfx = layout.Gfx;
            XPen grid = new XPen(XColors.Gray, 0.3);
            double x = layout.Left;
            double lineHeight = font.GetHeight();

            for (int c = 0; c < cells.Count; c++)
            {
                double w = layout.ColumnWidths[c];
                List<string> lines = WrapText(gfx, font, cells[c], w - 2 * CellPaddingPt);
                for (int l = 0; l < lines.Count; l++)
                    gfx.DrawString(lines[l], font, XBrushes.Black, x + CellPaddingPt, layout.Y + CellPaddingPt + l * lineHeight, XStringFormats.TopLeft);

                gfx.DrawRectangle(grid, x, layout.Y, w, height);
                x += w;
            }
        }


        // Globos agrupados por "1-A4", ordenados por hoja, letra y numero (F7, F16, L4...)
        private static List<IGrouping<string, BalloonWrapper>> GroupPositions(List<BalloonWrapper> balloons)
        {
            return balloons
                .Where(b => !string.IsNullOrEmpty(b.Zone))
                .GroupBy(b => b.Position)
                .OrderBy(g => g.First().Page)
                .ThenBy(g => ZoneLetters(g.First().Zone), StringComparer.Ordinal)
                .ThenBy(g => ZoneNumber(g.First().Zone))
                .ToList();
        }


        private static string ZoneLetters(string zone)
        {
            return new string(zone.Where(char.IsLetter).ToArray());
        }


        private static int ZoneNumber(string zone)
        {
            int number;
            return int.TryParse(new string(zone.Where(char.IsDigit).ToArray()), out number) ? number : 0;
        }


        // Enlace desde el texto de la LDM (coordenadas de dibujo de la pagina A4) al encuadre de los globos
        private static void AddZoomLink(BomLayout layout, XRect source, List<BalloonWrapper> balloons)
        {
            int index = balloons[0].Page - 1;
            if (index < 0 || index >= layout.Sheets.Count || index >= layout.Doc.PageCount) return;

            SheetWrapper sheet = layout.Sheets[index];
            PdfPage target = layout.Doc.Pages[index];
            if (sheet.Width <= 0 || sheet.Height <= 0) return;

            // Hoja (metros, origen abajo a la izquierda) -> espacio PDF de la pagina destino (mismo origen)
            double sx = target.Width.Point / sheet.Width;
            double sy = target.Height.Point / sheet.Height;
            double ox = target.MediaBox.X1, oy = target.MediaBox.Y1;

            // El zoom del PDF es sobre el tamaño de pagina: si la pagina no sale a tamaño real
            // (A0 = 1189 mm) se corrige para que el globo se vea igual en cualquier formato de hoja
            double pageScale = sx / PointsPerMeter;

            // Globos de la posicion en esa zona + margen; si no caben en la ventana se baja el zoom
            double margin = FocusMarginMm / 1000.0;
            double minX = balloons.Min(b => b.PosX) - margin, maxX = balloons.Max(b => b.PosX) + margin;
            double minY = balloons.Min(b => b.PosY) - margin, maxY = balloons.Max(b => b.PosY) + margin;
            double zoom = Math.Min(FocusZoom, Math.Min(ViewportWidthMm / 1000.0 / (maxX - minX), ViewportHeightMm / 1000.0 / (maxY - minY)));

            // Trozo de hoja que se ve con ese zoom (metros de hoja)
            double visibleWidth = ViewportWidthMm / 1000.0 / zoom;
            double visibleHeight = ViewportHeightMm / 1000.0 / zoom;

            // /XYZ pone (izquierda, arriba) en la esquina superior izquierda de la ventana:
            // se retrocede media ventana para que el globo quede centrado, sin salirse de la hoja
            double cx = (minX + maxX) / 2, cy = (minY + maxY) / 2;
            double left = Math.Max(0, Math.Min(sheet.Width - visibleWidth, cx - visibleWidth / 2));
            double top = Math.Min(sheet.Height, Math.Max(visibleHeight, cy + visibleHeight / 2));

            // Rectangulo del enlace en coordenadas PDF de la pagina A4 (origen abajo a la izquierda)
            double pageHeight = layout.Page.Height.Point;
            PdfRectangle rect = new PdfRectangle(new XRect(source.X, pageHeight - source.Y - source.Height, source.Width, source.Height));

            layout.Page.Annotations.Add(new ZoomLink(layout.Doc, rect, target,
                ox + left * sx, oy + top * sy, zoom / pageScale));
        }


        private static List<string> PositionTokens(List<string> positions)
        {
            return positions.Select((p, i) => i < positions.Count - 1 ? p + ", " : p).ToList();
        }


        private static List<string> WrapText(XGraphics gfx, XFont font, string text, double maxWidth)
        {
            List<string> result = new List<string>();
            foreach (string paragraph in (text ?? "").Split('\n'))
            {
                List<string> words = paragraph.Split(' ').Select((w, i) => i == 0 ? w : " " + w).ToList();
                foreach (List<string> line in WrapTokens(gfx, font, words, maxWidth))
                    result.Add(string.Concat(line).Trim());
            }
            return result;
        }


        // Reparte piezas de texto en lineas; una pieza mas ancha que la celda se corta por caracteres
        private static List<List<string>> WrapTokens(XGraphics gfx, XFont font, List<string> tokens, double maxWidth)
        {
            List<List<string>> lines = new List<List<string>> { new List<string>() };
            double x = 0;

            foreach (string original in tokens)
            {
                string token = original;
                double w = gfx.MeasureString(token, font).Width;

                if (x > 0 && x + w > maxWidth)
                {
                    lines.Add(new List<string>());
                    x = 0;
                    token = token.TrimStart();
                    w = gfx.MeasureString(token, font).Width;
                }

                while (w > maxWidth && token.Length > 1)
                {
                    int n = token.Length - 1;
                    while (n > 1 && gfx.MeasureString(token.Substring(0, n), font).Width > maxWidth) n--;

                    lines[lines.Count - 1].Add(token.Substring(0, n));
                    lines.Add(new List<string>());
                    token = token.Substring(n);
                    w = gfx.MeasureString(token, font).Width;
                }

                lines[lines.Count - 1].Add(token);
                x += w;
            }

            return lines;
        }
    }
}
