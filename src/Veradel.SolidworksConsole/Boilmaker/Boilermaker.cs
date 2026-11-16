using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Veradel.SolidworksConsole.Boilmaker
{
    public class Boilermaker
    {
        // Tolerancias en unidades del API (metros / adimensional para productos escalares)
        private const double AreaTol = 1e-9;          // dos caras "de igual area" (chapa: frente y dorso)
        private const double PerpendicularTol = 1e-3; // |n1 . n2| por debajo de esto => 90 grados
        private const double ParallelTol = 1e-3;      // 1 - |d1 . d2| por debajo de esto => misma direccion
        private const double MinEdgeLength = 1e-6;    // aristas degeneradas
        private const double AxisAlignedTol = 1e-2;   // margen para dar un valor por 0 o por +-1

        // true  => la arista dominante de la cara principal queda horizontal (apaisado)
        // false => se gira lo minimo posible hasta el angulo recto mas cercano
        private static readonly bool AlignDominantEdgeHorizontal = true;

        // Si el giro sale al reves (el error se duplica en vez de anularse), cambiar a 1.0
        private static readonly double AngleSign = -1.0;

        // Distribucion de las vistas en la hoja
        private const double ViewGap = 0.015;

        // Area util de la hoja: offset para quedar dentro del marco por los cuatro lados, mas la
        // banda del cajetin medida desde el marco inferior.
        private const double FrameOffset = 0.020;
        private const double TitleBlockBand = 0.060;

        // Limites de la escala de hoja. Por encima de MaxScale no se amplia aunque sobre papel;
        // por debajo de MinScale se prefiere un papel mayor y, agotado el A0, mas hojas.
        private const double MaxScale = 1.0;
        private const double MinScale = 1.0 / 10.0;

        private const double Eps = 1e-9;

        // Formatos apaisados A4..A0: { swDwgPaperSizes_e (mismo valor en swDwgTemplates_e), ancho, alto }
        private static readonly double[][] StandardPapers =
        {
            new[] { 6.0, 0.297, 0.210 },
            new[] { 8.0, 0.420, 0.297 },
            new[] { 9.0, 0.594, 0.420 },
            new[] { 10.0, 0.841, 0.594 },
            new[] { 11.0, 1.189, 0.841 }
        };

        // Aire entre el bloque de vistas y el borde de su region
        private const double RegionPadding = 0.008;

        // Corte de seguridad: un despiece de cientos de piezas no debe llenar el disco de hojas
        private const int MaxSheets = 50;

        // Hoja nueva donde cae todo el despiece; si ya existe se numera para no chocar
        private const string DespieceSheetName = "despiece - veradel";

        /// <summary>
        /// Serie normalizada ISO 5455, de mayor a menor. Una escala medida en crudo (1:6.37) no se
        /// lee ni se acota; se baja siempre al escalon normalizado inmediatamente inferior.
        /// </summary>
        private static readonly int[][] NormalizedScales =
        {
            new[] { 50, 1 }, new[] { 20, 1 }, new[] { 10, 1 }, new[] { 5, 1 }, new[] { 2, 1 },
            new[] { 1, 1 },
            new[] { 1, 2 }, new[] { 1, 5 }, new[] { 1, 10 }, new[] { 1, 20 }, new[] { 1, 50 },
            new[] { 1, 100 }, new[] { 1, 200 }, new[] { 1, 500 }, new[] { 1, 1000 }
        };

        private SldWorks _swApp;
        private MathUtility _math;

        // Convenio de proyeccion leido de la hoja: decide de que lado cae cada vista proyectada
        private bool _firstAngleProjection = true;

        // El perfil va siempre a la DERECHA del alzado, aunque la hoja sea de primer diedro.
        // Ojo: en primer diedro soltar a la derecha proyecta la vista POR LA IZQUIERDA.
        // A false manda el diedro de la hoja (primer diedro => perfil a la izquierda).
        private static readonly bool ProfileOnRight = true;

        public Boilermaker(SldWorks swApp)
        {
            _swApp = swApp;
            _math = (MathUtility)swApp.GetMathUtility();
        }

        public void Run()
        {
            // setters

            ModelDoc2 modelDrawing = null;
            modelDrawing = _swApp.ActiveDoc;

            string drawingPathName = modelDrawing.GetPathName();

            DrawingDoc drawing = null;
            drawing = modelDrawing as DrawingDoc;

            if (drawing == null) return;

            // We open the referenced drawing, the button must show in parts only just like de color edge

            Sheet activeSheet = null;
            activeSheet = drawing.GetCurrentSheet();

            double width = 0, height = 0;
            activeSheet.GetSize(ref width, ref height);

            _firstAngleProjection = IsFirstAngleProjection(activeSheet);
            Console.WriteLine($"proyeccion de la hoja: {(_firstAngleProjection ? "primer diedro (europeo)" : "tercer diedro (americano)")}");
            Console.WriteLine($"perfil {(ProfileSign > 0 ? "a la derecha" : "a la izquierda")}, planta {(PlanSign > 0 ? "encima" : "debajo")}");

            if (ProfileOnRight && _firstAngleProjection)
                Console.WriteLine("aviso: hoja en primer diedro con el perfil forzado a la derecha, la proyeccion es la vista por la izquierda");

            // select the first view
            object[] oViews = activeSheet.GetViews() as object[];
            View firstView = (View)oViews[0];

            ModelDoc2 referencedPartInDrawing = firstView.ReferencedDocument;
            string referencedDrawingPathName = referencedPartInDrawing.GetPathName();

            // opendoc
            ModelDoc2 modelPart = OpenReferencedDoc(referencedPartInDrawing.GetPathName());
            PartDoc partDoc = modelPart as PartDoc;

            // Se verifica la operacion is weldment
            FeatureManager featureManager = modelPart.FeatureManager;
            if (!partDoc.IsWeldment())
            {
                featureManager.InsertWeldmentFeature();

                // La lista de cortes no existe hasta que la pieza se reconstruye
                modelPart.ForceRebuild3(false);
            }

            // una vez verificada que sea weldment, se recorre todas las carpetas y se extrae una pieza

            // El objetivo es obtener todas las cut-list, para esto a partir de la version 2024: https://help.solidworks.com/2024/english/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IConfiguration~GetCutListItems.html

            Feature weldmentFeature = null;
            weldmentFeature = partDoc.FirstFeature();

            // Ahora se hace el array:
            List<Body2> bodies = GetBodiesCutList(weldmentFeature);

            // La orientacion se resuelve con la pieza abierta, antes de volver al dibujo:
            // asi las caras y las aristas se consultan una sola vez por cuerpo.
            List<BodyOrientation> orientations = new List<BodyOrientation>();
            for (int i = 0; i < bodies.Count; i++)
                orientations.Add(SolveOrientation(bodies[i], i));

            // Fase 1: medir. El bloque de cada cuerpo (alzado + perfil + planta) se mide sobre la
            // geometria, sin vistas: la escala y el reparto en hojas hay que decidirlos ANTES de
            // crear nada, porque una vista no se puede mover de hoja. Los cuerpos sin par de caras
            // valido se descartan aqui para que no reserven region.
            List<BodyRegion> regions = new List<BodyRegion>();
            for (int i = 0; i < bodies.Count; i++)
            {
                if (orientations[i].FrontFace != null && orientations[i].RightFace != null)
                    regions.Add(MeasureRegion(bodies[i], orientations[i], i));
                else
                    Console.WriteLine($"[cuerpo {i}] sin par de caras valido, se omite");
            }

            // regresamos al dibujo

            int errors = 0;
            _swApp.ActivateDoc3(drawingPathName, true, (int)swRebuildOnActivation_e.swDontRebuildActiveDoc, ref errors);

            SelectionMgr sel = modelPart.SelectionManager;

            if (regions.Count == 0)
            {
                Console.WriteLine("ningun cuerpo imprimible, no se crea el despiece");
                return;
            }

            // Fase 2: papel y escala DE HOJA, unica para todo el despiece. Se decide solo con
            // numeros, antes de crear ninguna hoja ni vista.
            SheetSetup setup = ChooseSheetSetup(regions, activeSheet);

            // Fase 3: una region por cuerpo, sin solapes, en las hojas que hagan falta.
            int sheetCount = PackRegions(regions, setup.Scale, setup.Width, setup.Height);

            if (sheetCount > MaxSheets)
            {
                Console.WriteLine($"{regions.Count} cuerpos necesitan {sheetCount} hojas, el tope es {MaxSheets}: se recorta el despiece");
                sheetCount = MaxSheets;
            }

            Console.WriteLine($"{regions.Count} cuerpos, papel {setup.Width * 1000.0:F0} x {setup.Height * 1000.0:F0} mm, " +
                $"escala de hoja {setup.Scale[0]}:{setup.Scale[1]}, {sheetCount} hoja(s)");

            // Fase 4: crear las vistas de cada hoja y centrarlas en su region. Nacen ya a la
            // escala de la hoja, no hay que escalar vista por vista.
            for (int sheetIndex = 0; sheetIndex < sheetCount; sheetIndex++)
            {
                // Todo el despiece va a hojas nuevas: el dibujo original no se toca y las vistas
                // no se mezclan con las que ya habia.
                if (CreateDespieceSheet(drawing, setup, DespieceSheetName) == null)
                {
                    Console.WriteLine("no se pudo crear la hoja del despiece, se aborta");
                    return;
                }

                List<BodyViewSet> sets = new List<BodyViewSet>();
                for (int r = 0; r < regions.Count; r++)
                {
                    if (regions[r].Sheet != sheetIndex) continue;

                    int i = regions[r].Index;

                    BodyViewSet views = CreateBodyViews(drawing, modelDrawing, modelPart, sel,
                        referencedDrawingPathName, bodies[i], orientations[i], i);

                    if (views == null) continue;

                    views.Region = regions[r];
                    sets.Add(views);
                }

                // Hasta que el dibujo no se reconstruye, GetOutline sigue dando el contorno de la
                // pieza ENTERA y no el del cuerpo filtrado: centrar con ese contorno es lo que
                // sacaba las vistas de su region. Una reconstruccion por hoja, no por cuerpo.
                modelDrawing.ForceRebuild3(false);

                for (int s = 0; s < sets.Count; s++)
                    PlaceInRegion(modelDrawing, sets[s]);
            }

            modelDrawing.GraphicsRedraw2();
        }

        /// <summary>
        /// Tamano del bloque de vistas de un cuerpo a 1:1, medido sobre la geometria en los ejes
        /// que tendra el alzado: horizontal = arista dominante, profundidad = normal de la cara
        /// principal. El perfil suma la profundidad al ancho y la planta la suma al alto.
        /// </summary>
        private BodyRegion MeasureRegion(Body2 body, BodyOrientation orientation, int index)
        {
            double[] normal = orientation.FrontNormal;
            double[] right = VeraDelMathClass.Normalize(GetNormal(orientation.RightFace));

            // Sin arista recta no hay giro posterior: el eje horizontal es el de la cara secundaria.
            double[] horizontal = orientation.DominantDirection ?? right;

            if (!AlignDominantEdgeHorizontal && orientation.DominantDirection != null)
            {
                // Giro minimo: queda horizontal el eje de la cara que ya estaba mas cerca de serlo.
                double[] across = VeraDelMathClass.CrossProduct(normal, horizontal);
                if (Math.Abs(VeraDelMathClass.DotProduct(across, right)) > Math.Abs(VeraDelMathClass.DotProduct(horizontal, right)))
                    horizontal = across;
            }

            double[] vertical = VeraDelMathClass.CrossProduct(normal, horizontal);

            double frontWidth = Extent(body, horizontal);
            double frontHeight = Extent(body, vertical);
            double depth = Extent(body, normal);

            BodyRegion region = new BodyRegion();
            region.Index = index;
            region.ModelWidth = frontWidth + depth;
            region.ModelHeight = frontHeight + depth;

            Console.WriteLine($"[cuerpo {index}] bloque 1:1 = {region.ModelWidth * 1000.0:F1} x {region.ModelHeight * 1000.0:F1} mm");

            return region;
        }

        /// <summary>
        /// Longitud del cuerpo a lo largo de una direccion unitaria. GetExtremePoint da el extent
        /// exacto en cualquier direccion; si falla se proyecta la caja del cuerpo, que nunca
        /// queda corta.
        /// </summary>
        private static double Extent(Body2 body, double[] direction)
        {
            double x1, y1, z1, x2, y2, z2;

            if (body.GetExtremePoint(direction[0], direction[1], direction[2], out x1, out y1, out z1) &&
                body.GetExtremePoint(-direction[0], -direction[1], -direction[2], out x2, out y2, out z2))
            {
                return Math.Abs((x1 - x2) * direction[0] + (y1 - y2) * direction[1] + (z1 - z2) * direction[2]);
            }

            double[] box = (double[])body.GetBodyBox();
            if (box == null) return 0.0;

            return Math.Abs(direction[0]) * (box[3] - box[0])
                 + Math.Abs(direction[1]) * (box[4] - box[1])
                 + Math.Abs(direction[2]) * (box[5] - box[2]);
        }

        /// <summary>
        /// Papel y escala de hoja del despiece. Objetivo: TODO en una sola hoja.
        ///
        ///   1. Del papel de partida hacia el A0, el primer papel en el que todo cabe en una hoja
        ///      sin bajar de MinScale; dentro de ese papel, la mayor escala normalizada.
        ///   2. Si ni el A0 lo consigue: A0 a MinScale y las hojas que hagan falta. Solo se baja
        ///      de MinScale si algun cuerpo no cabe el solo en la hoja.
        /// </summary>
        private SheetSetup ChooseSheetSetup(List<BodyRegion> regions, Sheet source)
        {
            List<SheetSetup> papers = CandidatePapers(source);

            for (int p = 0; p < papers.Count; p++)
            {
                for (int s = 0; s < NormalizedScales.Length; s++)
                {
                    double value = (double)NormalizedScales[s][0] / NormalizedScales[s][1];
                    if (value > MaxScale + Eps) continue;
                    if (value < MinScale - Eps) break;

                    if (PackRegions(regions, NormalizedScales[s], papers[p].Width, papers[p].Height) == 1)
                    {
                        papers[p].Scale = NormalizedScales[s];
                        return papers[p];
                    }
                }
            }

            SheetSetup largest = papers[papers.Count - 1];
            largest.Scale = NormalizedScales[NormalizedScales.Length - 1];

            for (int s = 0; s < NormalizedScales.Length; s++)
            {
                double value = (double)NormalizedScales[s][0] / NormalizedScales[s][1];
                if (value > MinScale + Eps) continue;

                if (PackRegions(regions, NormalizedScales[s], largest.Width, largest.Height) > 0)
                {
                    largest.Scale = NormalizedScales[s];
                    break;
                }
            }

            Console.WriteLine("no cabe todo en una hoja ni en el papel mayor: se reparte en varias");
            return largest;
        }

        /// <summary>
        /// Papeles a probar, de menor a mayor: el de la hoja de partida (con su propio formato) y
        /// los normalizados mayores que el. Un papel distinto del de partida no puede reutilizar
        /// su formato de hoja (el marco no encajaria), asi que lleva el estandar de su tamano.
        /// </summary>
        private List<SheetSetup> CandidatePapers(Sheet source)
        {
            SheetSetup original = new SheetSetup();
            original.PaperSize = (int)swDwgPaperSizes_e.swDwgPaperA3size;
            original.Template = (int)swDwgTemplates_e.swDwgTemplateNone;
            original.TemplateName = source.GetTemplateName();

            // Mismo orden que ISheet::SetProperties: papel, plantilla, escala1, escala2, diedro.
            Array properties = SheetProperties(source);
            if (properties != null && properties.Length >= 2)
            {
                original.PaperSize = Convert.ToInt32(properties.GetValue(0));
                original.Template = Convert.ToInt32(properties.GetValue(1));
            }
            else
            {
                Console.WriteLine("no se pudieron leer las propiedades de la hoja de partida, se usan valores por defecto");
            }

            // El ancho y el alto se leen de GetSize: en papel a medida no vienen en las propiedades.
            source.GetSize(ref original.Width, ref original.Height);

            List<SheetSetup> papers = new List<SheetSetup>();
            papers.Add(original);

            for (int i = 0; i < StandardPapers.Length; i++)
            {
                if (StandardPapers[i][1] <= original.Width + 1e-6) continue;

                SheetSetup paper = new SheetSetup();
                paper.PaperSize = (int)StandardPapers[i][0];
                paper.Template = (int)StandardPapers[i][0];
                paper.TemplateName = string.Empty;
                paper.Width = StandardPapers[i][1];
                paper.Height = StandardPapers[i][2];
                papers.Add(paper);
            }

            return papers;
        }

        /// <summary>
        /// Reparte las regiones en hojas sin que se pisen: estantes de izquierda a derecha, con
        /// los bloques ordenados de mas alto a mas bajo (first-fit decreasing height), que es lo
        /// que mejor llena la hoja. El papel que sobra se reparte entre regiones y entre estantes,
        /// asi el despiece queda centrado y los solidos lo mas separados posible.
        ///
        /// Devuelve el numero de hojas, o 0 si algun bloque no cabe el solo en la hoja. No toca
        /// SolidWorks: se llama tantas veces como haga falta para tantear papel y escala.
        /// </summary>
        private static int PackRegions(List<BodyRegion> regions, int[] scale, double sheetWidth, double sheetHeight)
        {
            double factor = (double)scale[0] / scale[1];
            double usableWidth = UsableWidth(sheetWidth);
            double usableHeight = UsableHeight(sheetHeight);

            // Los huecos entre vistas y el aire de la region van en unidades de papel: no escalan.
            for (int i = 0; i < regions.Count; i++)
            {
                regions[i].Width = regions[i].ModelWidth * factor + ViewGap + 2.0 * RegionPadding;
                regions[i].Height = regions[i].ModelHeight * factor + ViewGap + 2.0 * RegionPadding;

                if (regions[i].Width > usableWidth + Eps || regions[i].Height > usableHeight + Eps) return 0;
            }

            List<BodyRegion> order = new List<BodyRegion>(regions);
            order.Sort((a, b) =>
            {
                int byHeight = b.Height.CompareTo(a.Height);
                return byHeight != 0 ? byHeight : a.Index.CompareTo(b.Index);
            });

            List<Shelf> shelves = new List<Shelf>();
            List<double> usedHeight = new List<double>();  // alto ocupado de cada hoja
            List<int> shelfCount = new List<int>();        // estantes de cada hoja

            for (int i = 0; i < order.Count; i++)
            {
                BodyRegion region = order[i];

                // Al ir de mas alto a mas bajo, en un estante abierto solo hay que mirar el ancho.
                Shelf shelf = null;
                for (int s = 0; s < shelves.Count && shelf == null; s++)
                {
                    if (shelves[s].Width + region.Width <= usableWidth + Eps) shelf = shelves[s];
                }

                if (shelf == null)
                {
                    int sheet = 0;
                    while (sheet < usedHeight.Count && usedHeight[sheet] + region.Height > usableHeight + Eps) sheet++;

                    if (sheet == usedHeight.Count)
                    {
                        usedHeight.Add(0.0);
                        shelfCount.Add(0);
                    }

                    shelf = new Shelf();
                    shelf.Sheet = sheet;
                    shelf.Row = shelfCount[sheet]++;
                    shelf.Top = usedHeight[sheet];
                    shelf.Height = region.Height;
                    shelves.Add(shelf);

                    usedHeight[sheet] += region.Height;
                }

                shelf.Regions.Add(region);
                shelf.Width += region.Width;
            }

            for (int s = 0; s < shelves.Count; s++)
            {
                Shelf shelf = shelves[s];

                double extraY = (usableHeight - usedHeight[shelf.Sheet]) / shelfCount[shelf.Sheet];
                double extraX = (usableWidth - shelf.Width) / shelf.Regions.Count;

                double slotHeight = shelf.Height + extraY;
                double top = shelf.Top + shelf.Row * extraY;
                double x = 0.0;

                for (int i = 0; i < shelf.Regions.Count; i++)
                {
                    BodyRegion region = shelf.Regions[i];
                    double slotWidth = region.Width + extraX;

                    // La Y de la hoja crece hacia arriba y los estantes se llenan desde el marco superior.
                    region.Sheet = shelf.Sheet;
                    region.CenterX = FrameOffset + x + slotWidth / 2.0;
                    region.CenterY = sheetHeight - FrameOffset - top - slotHeight / 2.0;

                    x += slotWidth;
                }
            }

            return usedHeight.Count;
        }

        /// <summary>Fila de regiones dentro de una hoja, durante el reparto.</summary>
        private sealed class Shelf
        {
            public int Sheet;
            public int Row;        // posicion del estante dentro de su hoja, 0 = arriba
            public double Top;     // distancia desde el borde superior del area util
            public double Height;
            public double Width;   // ancho ya ocupado
            public readonly List<BodyRegion> Regions = new List<BodyRegion>();
        }

        /// <summary>
        /// Alzado (vista relativa girada al angulo recto) mas perfil y planta, en la hoja activa.
        /// </summary>
        private BodyViewSet CreateBodyViews(DrawingDoc drawing, ModelDoc2 modelDrawing, ModelDoc2 modelPart,
            SelectionMgr sel, string partPathName, Body2 body, BodyOrientation orientation, int index)
        {
            modelPart.ClearSelection2(true);

            SelectData frontData = sel.CreateSelectData();
            frontData.Mark = 1;
            sel.AddSelectionListObject(orientation.FrontFace, frontData);

            SelectData rightData = sel.CreateSelectData();
            rightData.Mark = 2;
            sel.AddSelectionListObject(orientation.RightFace, rightData);

            View vista = drawing.CreateRelativeView(
                partPathName, 0, 0,
                (int)swRelativeViewCreationDirection_e.swRelativeViewCreationDirection_FRONT,
                (int)swRelativeViewCreationDirection_e.swRelativeViewCreationDirection_RIGHT);

            modelPart.ClearSelection2(true);

            if (vista == null)
            {
                Console.WriteLine($"[cuerpo {index}] CreateRelativeView devolvio null");
                return null;
            }

            ApplyBodyFilter(vista, body, index, "alzado");

            AlignViewToRightAngle(vista, orientation, index);

            modelDrawing.GraphicsRedraw2();

            // Perfil y planta: vistas proyectadas hijas del alzado, asi heredan giro y escala
            // y quedan alineadas con el.
            BodyViewSet views = CreateProjectedViews(drawing, modelDrawing, vista, body, index);
            views.Index = index;

            UseSheetScale(views);

            return views;
        }

        private List<Body2> GetBodiesCutList(Feature weldmentFeature)
        {

            List<Body2> bodies = new List<Body2>();

            bool exit = false;
            bool entro = false;

            do
            {
                switch (weldmentFeature.GetTypeName2())
                {
                    case "CutListFolder":
                        entro = true;
                        BodyFolder folder = null;
                        folder = weldmentFeature.GetSpecificFeature2();

                        Console.WriteLine(weldmentFeature.Name);

                        object[] oBodies = folder.GetBodies();

                        if (oBodies != null && oBodies.Length > 0)
                            bodies.Add((Body2)oBodies[0]);

                        break;

                    default:
                        if (entro)
                            exit = true;
                        break;
                }

                if (exit) break;

                weldmentFeature = weldmentFeature.GetNextFeature();
            } while (weldmentFeature != null);

            return bodies;
        }

        /// <summary>
        /// Hoja nueva para el despiece con el papel y la ESCALA DE HOJA ya decididos; el diedro
        /// es el de la hoja de partida. Se activa por nombre porque tanto CreateRelativeView como
        /// CreateUnfoldedViewAt3 crean siempre en la hoja activa.
        /// </summary>
        private Sheet CreateDespieceSheet(DrawingDoc drawing, SheetSetup setup, string baseName)
        {
            string name = UniqueSheetName(drawing, baseName);

            if (!drawing.NewSheet3(name, setup.PaperSize, setup.Template, setup.Scale[0], setup.Scale[1],
                    _firstAngleProjection, setup.TemplateName, setup.Width, setup.Height, string.Empty))
            {
                Console.WriteLine($"NewSheet3 fallo para la hoja '{name}'");
                return null;
            }

            if (!drawing.ActivateSheet(name))
                Console.WriteLine($"no se pudo activar la hoja '{name}'");

            Console.WriteLine($"hoja del despiece: '{name}'");

            return drawing.GetCurrentSheet() as Sheet;
        }

        private static Array SheetProperties(Sheet sheet)
        {
            object properties = sheet.GetProperties2();
            if (properties == null) properties = sheet.GetProperties();

            return properties as Array;
        }

        /// <summary>
        /// El nombre de hoja tiene que ser unico dentro del dibujo: si ya hay un despiece se numera.
        /// </summary>
        private static string UniqueSheetName(DrawingDoc drawing, string baseName)
        {
            List<string> existing = new List<string>();

            Array names = drawing.GetSheetNames() as Array;
            if (names != null)
            {
                foreach (object name in names)
                    existing.Add(Convert.ToString(name));
            }

            if (!ContainsName(existing, baseName)) return baseName;

            for (int i = 2; ; i++)
            {
                string candidate = $"{baseName} {i}";
                if (!ContainsName(existing, candidate)) return candidate;
            }
        }

        private static bool ContainsName(List<string> names, string candidate)
        {
            for (int i = 0; i < names.Count; i++)
            {
                if (string.Equals(names[i], candidate, StringComparison.OrdinalIgnoreCase)) return true;
            }

            return false;
        }

        /// <summary>
        /// Resuelve las dos caras que necesita CreateRelativeView y la direccion dominante
        /// de la cara principal, que es la que despues se lleva al angulo recto.
        /// </summary>
        private BodyOrientation SolveOrientation(Body2 body, int index)
        {
            BodyOrientation result = new BodyOrientation();

            result.FrontFace = GetLargestPlanarFace(body);
            if (result.FrontFace == null)
            {
                Console.WriteLine($"[cuerpo {index}] sin caras planas");
                return result;
            }

            result.FrontNormal = VeraDelMathClass.Normalize(GetNormal(result.FrontFace));

            bool rightAngleCorner;
            result.DominantDirection = GetDominantEdgeDirection(result.FrontFace, result.FrontNormal, out rightAngleCorner);
            result.HasRightAngleCorner = rightAngleCorner;

            result.RightFace = GetPerpendicularFace(body, result.FrontFace, result.FrontNormal, result.DominantDirection);

            Console.WriteLine($"[cuerpo {index}] area principal = {result.FrontFace.GetArea():F6} m2");
            BoilermakerDebug.PrintVector("  normal principal", result.FrontNormal);
            BoilermakerDebug.PrintVector("  arista dominante", result.DominantDirection);
            Console.WriteLine($"  esquina en angulo recto: {result.HasRightAngleCorner}");

            if (result.RightFace != null)
            {
                double[] rightNormal = VeraDelMathClass.Normalize(GetNormal(result.RightFace));
                BoilermakerDebug.PrintVector("  normal secundaria", rightNormal);
                Console.WriteLine($"  |n1.n2| = {Math.Abs(VeraDelMathClass.DotProduct(result.FrontNormal, rightNormal)):E3} (0 = perpendicular)");
            }
            else
            {
                Console.WriteLine("  sin cara perpendicular plana");
            }

            return result;
        }

        /// <summary>
        /// Cara de mayor area entre las planas de la cascara exterior. En una chapa hay dos caras
        /// con la misma area (frente y dorso): el desempate es fijo para que la vista no salga
        /// espejada de una ejecucion a otra.
        /// </summary>
        private Face2 GetLargestPlanarFace(Body2 body)
        {
            object[] oFaces = (object[])body.GetFaces();
            if (oFaces == null) return null;

            Face2 best = null;
            double bestArea = 0.0;
            double bestTieScore = 0.0;

            for (int i = 0; i < oFaces.Length; i++)
            {
                Face2 face = oFaces[i] as Face2;
                if (!IsUsableFace(face)) continue;

                double area = face.GetArea();
                double tieScore = VeraDelMathClass.DotProduct(
                    VeraDelMathClass.Normalize(GetNormal(face)), VeraDelMathClass.TieBreakDirection);

                if (best == null || area > bestArea + AreaTol)
                {
                    best = face;
                    bestArea = area;
                    bestTieScore = tieScore;
                }
                else if (Math.Abs(area - bestArea) <= AreaTol && tieScore > bestTieScore)
                {
                    best = face;
                    bestTieScore = tieScore;
                }
            }

            if (best == null)
                BoilermakerDebug.PrintFaceTypes(oFaces);

            return best;
        }

        /// <summary>
        /// Direccion recta con mas longitud acumulada dentro de la cara. Devuelve tambien si existe
        /// otra direccion perpendicular a ella, es decir, si la cara tiene una esquina de 90 grados.
        /// </summary>
        private double[] GetDominantEdgeDirection(Face2 face, double[] normal, out bool rightAngleCorner)
        {
            rightAngleCorner = false;

            List<double[]> directions = new List<double[]>();
            List<double> lengths = new List<double>();

            object[] oEdges = (object[])face.GetEdges();
            if (oEdges == null) return null;

            for (int i = 0; i < oEdges.Length; i++)
            {
                Edge edge = oEdges[i] as Edge;
                if (edge == null) continue;

                Curve curve = edge.GetCurve() as Curve;
                if (curve == null || curve.Identity() != (int)swCurveTypes_e.LINE_TYPE) continue;

                Vertex startVertex = edge.GetStartVertex() as Vertex;
                Vertex endVertex = edge.GetEndVertex() as Vertex;
                if (startVertex == null || endVertex == null) continue;

                double[] start = (double[])startVertex.GetPoint();
                double[] end = (double[])endVertex.GetPoint();

                double[] direction = VeraDelMathClass.Subtract(end, start);
                double length = VeraDelMathClass.Length(direction);
                if (length < MinEdgeLength) continue;

                direction = VeraDelMathClass.Scale(direction, 1.0 / length);

                // La arista pertenece a la cara, asi que ya esta en su plano; se limpia el residuo
                // numerico para que el producto escalar contra la normal sea exactamente cero.
                direction = VeraDelMathClass.ProjectOntoPlane(direction, normal);
                if (direction == null) continue;

                // Mismo eje con sentido opuesto es la misma direccion: se acumula la longitud.
                int existing = -1;
                for (int j = 0; j < directions.Count; j++)
                {
                    if (Math.Abs(VeraDelMathClass.DotProduct(directions[j], direction)) > 1.0 - ParallelTol)
                    {
                        existing = j;
                        break;
                    }
                }

                if (existing >= 0)
                {
                    lengths[existing] += length;
                }
                else
                {
                    directions.Add(direction);
                    lengths.Add(length);
                }
            }

            if (directions.Count == 0) return null;

            int dominant = 0;
            for (int i = 1; i < directions.Count; i++)
            {
                if (lengths[i] > lengths[dominant])
                    dominant = i;
            }

            for (int i = 0; i < directions.Count; i++)
            {
                if (i == dominant) continue;
                if (Math.Abs(VeraDelMathClass.DotProduct(directions[dominant], directions[i])) < PerpendicularTol)
                {
                    rightAngleCorner = true;
                    break;
                }
            }

            return directions[dominant];
        }

        /// <summary>
        /// Cara plana perpendicular a la principal. GetNextFace no sirve aqui: recorre la lista
        /// enlazada de todas las caras del cuerpo, no las vecinas. Las vecinas reales salen de las
        /// aristas de la cara principal.
        /// </summary>
        private Face2 GetPerpendicularFace(Body2 body, Face2 frontFace, double[] frontNormal, double[] dominantDirection)
        {
            List<Face2> candidates = new List<Face2>();

            object[] oEdges = (object[])frontFace.GetEdges();
            if (oEdges != null)
            {
                for (int i = 0; i < oEdges.Length; i++)
                {
                    Edge edge = oEdges[i] as Edge;
                    if (edge == null) continue;

                    object[] adjacent = (object[])edge.GetTwoAdjacentFaces2();
                    if (adjacent == null) continue;

                    for (int j = 0; j < adjacent.Length; j++)
                    {
                        Face2 neighbour = adjacent[j] as Face2;
                        if (neighbour == null || IsSameFace(neighbour, frontFace)) continue;
                        candidates.Add(neighbour);
                    }
                }
            }

            Face2 best = PickPerpendicular(candidates, frontFace, frontNormal, dominantDirection);
            if (best != null) return best;

            // Sin vecina valida (la cara toca solo redondeos o cilindros): se busca en todo el cuerpo.
            candidates.Clear();
            object[] oFaces = (object[])body.GetFaces();
            if (oFaces != null)
            {
                for (int i = 0; i < oFaces.Length; i++)
                {
                    Face2 face = oFaces[i] as Face2;
                    if (face == null || IsSameFace(face, frontFace)) continue;
                    candidates.Add(face);
                }
            }

            return PickPerpendicular(candidates, frontFace, frontNormal, dominantDirection);
        }

        /// <summary>
        /// De las candidatas perpendiculares se queda con la que apunta mas hacia la direccion
        /// dominante: asi el eje derecho de la vista ya nace alineado con la esquina recta y el
        /// giro posterior es minimo.
        /// </summary>
        private Face2 PickPerpendicular(List<Face2> candidates, Face2 frontFace, double[] frontNormal, double[] dominantDirection)
        {
            Face2 best = null;
            double bestScore = double.NegativeInfinity;

            for (int i = 0; i < candidates.Count; i++)
            {
                Face2 face = candidates[i];
                if (!IsUsableFace(face)) continue;

                double[] normal = VeraDelMathClass.Normalize(GetNormal(face));
                if (normal == null) continue;

                if (Math.Abs(VeraDelMathClass.DotProduct(frontNormal, normal)) > PerpendicularTol) continue;

                double score = dominantDirection == null
                    ? VeraDelMathClass.DotProduct(normal, VeraDelMathClass.TieBreakDirection)
                    : VeraDelMathClass.DotProduct(normal, dominantDirection);

                if (score > bestScore)
                {
                    bestScore = score;
                    best = face;
                }
            }

            return best;
        }

        /// <summary>
        /// Gira la vista hasta que la arista dominante queda en angulo recto con la hoja.
        /// CreateRelativeView solo admite dos caras, asi que la cara secundaria fija un eje derecho
        /// aproximado y este giro corrige lo que falte.
        /// </summary>
        private void AlignViewToRightAngle(View view, BodyOrientation orientation, int index)
        {
            if (orientation.DominantDirection == null)
            {
                Console.WriteLine($"[cuerpo {index}] sin arista recta, no se corrige el giro");
                return;
            }

            MathTransform modelToView = view.ModelToViewTransform;
            if (modelToView == null) return;

            MathVector vector = (MathVector)_math.CreateVector(orientation.DominantDirection);
            MathVector inView = (MathVector)vector.MultiplyTransform(modelToView);
            double[] data = (double[])inView.ArrayData;

            double angle = Math.Atan2(data[1], data[0]);

            if (!AlignDominantEdgeHorizontal)
            {
                // Giro minimo: se lleva al multiplo de 90 grados mas cercano.
                while (angle > Math.PI / 4.0) angle -= Math.PI / 2.0;
                while (angle < -Math.PI / 4.0) angle += Math.PI / 2.0;
            }

            Console.WriteLine($"[cuerpo {index}] correccion de giro = {angle * 180.0 / Math.PI:F3} grados");

            if (Math.Abs(angle) < 1e-9) return;

            view.Angle = view.Angle + AngleSign * angle;
        }

        /// <summary>+1 = perfil a la derecha del alzado, -1 = a la izquierda.</summary>
        private double ProfileSign
        {
            get { return ProfileOnRight ? 1.0 : (_firstAngleProjection ? -1.0 : 1.0); }
        }

        /// <summary>+1 = planta encima del alzado, -1 = debajo.</summary>
        private double PlanSign
        {
            get { return _firstAngleProjection ? -1.0 : 1.0; }
        }

        /// <summary>
        /// Perfil y planta a partir del alzado. CreateUnfoldedViewAt3 deduce que vista es por la
        /// zona en que se suelta respecto al recuadro del padre, y ese lado depende del convenio
        /// de proyeccion de la hoja:
        ///
        ///   primer diedro  -> perfil (vista por la derecha) a la IZQUIERDA, planta ABAJO
        ///   tercer diedro  -> perfil a la DERECHA,                          planta ARRIBA
        ///
        /// Si el punto cae en una zona diagonal, SolidWorks crea una axonometrica en vez de la
        /// vista ortogonal. La distancia no importa, solo la direccion; la posicion definitiva la
        /// fija PlaceViewGroup cuando ya se conoce el tamano real de cada vista.
        /// </summary>
        private BodyViewSet CreateProjectedViews(DrawingDoc drawing, ModelDoc2 modelDrawing, View front, Body2 body, int index)
        {
            BodyViewSet set = new BodyViewSet();
            set.Front = front;

            set.Profile = CreateProjection(drawing, modelDrawing, front, ProfileSign, 0.0,
                (int)swAlignViewTypes_e.swAlignViewHorizontalCenter, index, "perfil");

            set.Plan = CreateProjection(drawing, modelDrawing, front, 0.0, PlanSign,
                (int)swAlignViewTypes_e.swAlignViewVerticalCenter, index, "planta");

            ApplyBodyFilter(set.Profile, body, index, "perfil");
            ApplyBodyFilter(set.Plan, body, index, "planta");

            return set;
        }

        /// <summary>
        /// Crea una vista proyectada en la direccion (dirX, dirY) y la ata al padre por CENTRO,
        /// no por origen.
        ///
        /// El punto de suelta se mide desde el CENTRO DEL OUTLINE del padre, no desde
        /// IView::Position: Position es el origen del modelo proyectado y en un despiece cada
        /// cuerpo cae lejos de el, asi que el punto se iba a una zona diagonal y salia una
        /// axonometrica. Si aun asi la vista no sale ortogonal, se borra y se reintenta midiendo
        /// desde Position.
        /// </summary>
        private View CreateProjection(DrawingDoc drawing, ModelDoc2 modelDrawing, View parent,
            double dirX, double dirY, int alignType, int index, string label)
        {
            double[] outline = (double[])parent.GetOutline();
            double[] position = (double[])parent.Position;
            if (outline == null || position == null) return null;

            double parentWidth = outline[2] - outline[0];
            double parentHeight = outline[3] - outline[1];
            double step = (Math.Abs(dirX) > 0.0 ? parentWidth : parentHeight) + ViewGap;

            double[][] origins = new double[][]
            {
                new double[] { (outline[0] + outline[2]) / 2.0, (outline[1] + outline[3]) / 2.0 },
                new double[] { position[0], position[1] }
            };

            for (int attempt = 0; attempt < origins.Length; attempt++)
            {
                View child = CreateUnfolded(drawing, modelDrawing, parent,
                    origins[attempt][0] + dirX * step,
                    origins[attempt][1] + dirY * step,
                    index, label);

                if (child == null) continue;

                if (!IsOrthogonalProjection(parent, child))
                {
                    Console.WriteLine($"[cuerpo {index}] la {label} salio axonometrica, se descarta y se reintenta");
                    DeleteView(modelDrawing, child);
                    continue;
                }

                if (!child.AlignWithView(alignType, parent))
                    Console.WriteLine($"[cuerpo {index}] no se pudo alinear la {label} por centro");

                return child;
            }

            Console.WriteLine($"[cuerpo {index}] sin {label}");
            return null;
        }

        /// <summary>
        /// Una vista proyectada exige que la vista padre este activa y seleccionada.
        /// </summary>
        private View CreateUnfolded(DrawingDoc drawing, ModelDoc2 modelDrawing, View parent, double x, double y, int index, string label)
        {
            string parentName = parent.GetName2();

            modelDrawing.ClearSelection2(true);
            drawing.ActivateView(parentName);

            bool selected = modelDrawing.Extension.SelectByID2(
                parentName, "DRAWINGVIEW", 0.0, 0.0, 0.0, false, 0, null, 0);

            if (!selected)
            {
                Console.WriteLine($"[cuerpo {index}] no se pudo seleccionar '{parentName}' para la {label}");
                return null;
            }

            View child = drawing.CreateUnfoldedViewAt3(x, y, 0.0, false);
            modelDrawing.ClearSelection2(true);

            if (child == null)
                Console.WriteLine($"[cuerpo {index}] CreateUnfoldedViewAt3 devolvio null para la {label}");

            return child;
        }

        /// <summary>
        /// Distingue una proyeccion ortogonal (perfil, planta) de una axonometrica sin depender de
        /// nombres: en la ortogonal los ejes de la vista hija son los del padre permutados y con
        /// signo, asi que C * P^t solo tiene 0 y +-1. En una isometrica salen 0.408, 0.577, 0.707.
        /// </summary>
        private static bool IsOrthogonalProjection(View parent, View child)
        {
            double[,] p = GetRotation(parent.ModelToViewTransform);
            double[,] c = GetRotation(child.ModelToViewTransform);

            // Sin transformadas no se puede juzgar: se acepta la vista antes que borrar una buena.
            if (p == null || c == null) return true;

            for (int i = 0; i < 3; i++)
            {
                for (int j = 0; j < 3; j++)
                {
                    double dot = 0.0;
                    for (int k = 0; k < 3; k++) dot += c[i, k] * p[j, k];

                    double magnitude = Math.Abs(dot);
                    if (magnitude > AxisAlignedTol && magnitude < 1.0 - AxisAlignedTol) return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Los 9 primeros valores de MathTransform::ArrayData son la matriz de giro.
        /// </summary>
        private static double[,] GetRotation(MathTransform transform)
        {
            if (transform == null) return null;

            double[] data = (double[])transform.ArrayData;
            if (data == null || data.Length < 9) return null;

            double[,] rotation = new double[3, 3];
            for (int row = 0; row < 3; row++)
                for (int column = 0; column < 3; column++)
                    rotation[row, column] = data[row * 3 + column];

            return rotation;
        }

        private void DeleteView(ModelDoc2 modelDrawing, View view)
        {
            modelDrawing.ClearSelection2(true);

            if (modelDrawing.Extension.SelectByID2(view.GetName2(), "DRAWINGVIEW", 0.0, 0.0, 0.0, false, 0, null, 0))
                modelDrawing.Extension.DeleteSelection2((int)swDeleteSelectionOptions_e.swDelete_Children);

            modelDrawing.ClearSelection2(true);
        }

        /// <summary>
        /// Deja en la vista un unico cuerpo del despiece.
        /// </summary>
        private void ApplyBodyFilter(View view, Body2 body, int index, string label)
        {
            if (view == null) return;

            try
            {
                DispatchWrapper[] wrappers = new DispatchWrapper[1];
                wrappers[0] = new DispatchWrapper(body);
                view.Bodies = wrappers;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[cuerpo {index}] {label}: no se pudo filtrar el cuerpo ({ex.Message})");
            }
        }

        /// <summary>
        /// La escala manda desde la HOJA: el alzado sigue la de la hoja y el perfil y la planta
        /// la del alzado. Ninguna vista lleva escala propia.
        /// </summary>
        private static void UseSheetScale(BodyViewSet views)
        {
            try
            {
                views.Front.UseSheetScale = 1;
                if (views.Profile != null) views.Profile.UseParentScale = true;
                if (views.Plan != null) views.Plan.UseParentScale = true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[cuerpo {views.Index}] no se pudo ligar la escala a la hoja ({ex.Message})");
            }
        }

        private static double UsableWidth(double sheetWidth)
        {
            return sheetWidth - 2.0 * FrameOffset;
        }

        private static double UsableHeight(double sheetHeight)
        {
            return sheetHeight - 2.0 * FrameOffset - TitleBlockBand;
        }

        /// <summary>
        /// Centra el bloque de vistas en su region. El alzado no va al centro de la region: se
        /// descuenta el hueco que ocupan perfil y planta a su lado, que es lo que centra el
        /// conjunto y no la vista principal.
        /// </summary>
        private void PlaceInRegion(ModelDoc2 modelDrawing, BodyViewSet views)
        {
            BodyRegion region = views.Region;

            double[] frontOutline = (double[])views.Front.GetOutline();
            if (frontOutline == null) return;

            double frontWidth = frontOutline[2] - frontOutline[0];
            double frontHeight = frontOutline[3] - frontOutline[1];

            double profileWidth = 0.0, planHeight = 0.0;

            double[] profileOutline = views.Profile == null ? null : (double[])views.Profile.GetOutline();
            if (profileOutline != null) profileWidth = profileOutline[2] - profileOutline[0];

            double[] planOutline = views.Plan == null ? null : (double[])views.Plan.GetOutline();
            if (planOutline != null) planHeight = planOutline[3] - planOutline[1];

            double sideSpace = profileOutline == null ? 0.0 : profileWidth + ViewGap;
            double stackSpace = planOutline == null ? 0.0 : planHeight + ViewGap;

            // Reparto del hueco del bloque alrededor del alzado, segun el lado de cada hija.
            double leftPad = ProfileSign < 0.0 ? sideSpace : 0.0;
            double rightPad = ProfileSign < 0.0 ? 0.0 : sideSpace;
            double topPad = PlanSign < 0.0 ? 0.0 : stackSpace;
            double bottomPad = PlanSign < 0.0 ? stackSpace : 0.0;

            double frontCenterX = region.CenterX - (rightPad - leftPad) / 2.0;
            double frontCenterY = region.CenterY - (topPad - bottomPad) / 2.0;

            MoveViewCenterTo(views.Front, frontCenterX, frontCenterY);

            // Las hijas siguen al padre por la alineacion, pero se fija su posicion de forma
            // explicita: sobre el eje alineado manda SolidWorks y sobre el libre manda este valor.
            modelDrawing.GraphicsRedraw2();

            if (profileOutline != null)
                MoveViewCenterTo(views.Profile, frontCenterX + ProfileSign * (frontWidth / 2.0 + ViewGap + profileWidth / 2.0), frontCenterY);

            if (planOutline != null)
                MoveViewCenterTo(views.Plan, frontCenterX, frontCenterY + PlanSign * (frontHeight / 2.0 + ViewGap + planHeight / 2.0));

            double blockWidth = leftPad + frontWidth + rightPad;
            double blockHeight = topPad + frontHeight + bottomPad;

            if (blockWidth > region.Width + 1e-9 || blockHeight > region.Height + 1e-9)
                Console.WriteLine($"[cuerpo {views.Index}] el bloque real ({blockWidth * 1000.0:F1} x {blockHeight * 1000.0:F1} mm) " +
                    $"no cabe en su region ({region.Width * 1000.0:F1} x {region.Height * 1000.0:F1} mm)");
        }

        /// <summary>
        /// IView::Position es el centro de la vista, no la esquina del contorno: el desplazamiento
        /// se calcula contra el centro del outline actual.
        /// </summary>
        private static void MoveViewCenterTo(View view, double centerX, double centerY)
        {
            double[] outline = (double[])view.GetOutline();
            if (outline == null) return;

            double currentCenterX = (outline[0] + outline[2]) / 2.0;
            double currentCenterY = (outline[1] + outline[3]) / 2.0;

            double[] position = (double[])view.Position;
            if (position == null) return;

            view.Position = new double[]
            {
                position[0] + (centerX - currentCenterX),
                position[1] + (centerY - currentCenterY)
            };
        }

        /// <summary>
        /// Indice 4 de las propiedades de la hoja: true = primer diedro (europeo).
        /// </summary>
        private static bool IsFirstAngleProjection(Sheet sheet)
        {
            Array values = SheetProperties(sheet);
            if (values == null || values.Length < 5)
            {
                Console.WriteLine("no se pudo leer el diedro de la hoja, se asume primer diedro");
                return true;
            }

            return Convert.ToDouble(values.GetValue(4)) != 0.0;
        }

        private bool IsUsableFace(Face2 face)
        {
            if (face == null) return false;

            Surface surface = face.GetSurface() as Surface;

            // Sin filtro de cascara: GetShellType no distingue lo que se esperaba y descartaba todas
            // las caras. No hace falta, la cara de mayor area siempre es exterior.
            return surface != null && surface.Identity() == (int)swSurfaceTypes_e.PLANE_TYPE;
        }

        private static bool IsSameFace(Face2 a, Face2 b)
        {
            if (a == null || b == null) return false;
            return a.IsSame(b);
        }

        private static double[] GetNormal(Face2 face)
        {
            return (double[])face.Normal;
        }

        private ModelDoc2 OpenReferencedDoc(string path)
        {

            int errors = 0, warnings = 0;
            ModelDoc2 referenced = _swApp.OpenDoc6(path, (int)swDocumentTypes_e.swDocPART, (int)swOpenDocOptions_e.swOpenDocOptions_Silent, string.Empty, ref errors, ref warnings);

            if (referenced == null)
                throw new Exception("The file cannot be accessed");


            if (warnings == (int)swFileLoadWarning_e.swFileLoadWarning_AlreadyOpen)
            {
                _swApp.ActivateDoc3(path, true, (int)swRebuildOnActivation_e.swUserDecision, errors);
            }


            return referenced;
        }

    }


    /// <summary>
    /// Papel y escala de las hojas del despiece.
    /// </summary>
    public class SheetSetup
    {
        public int PaperSize;        // swDwgPaperSizes_e
        public int Template;         // swDwgTemplates_e
        public string TemplateName;  // formato de hoja; vacio = el estandar del tamano
        public double Width;
        public double Height;
        public int[] Scale;          // escala de hoja: {numerador, denominador}
    }


    /// <summary>
    /// Region de la hoja reservada a un cuerpo del despiece: su bloque de vistas mas el aire.
    /// </summary>
    public class BodyRegion
    {
        public int Index;           // cuerpo de la lista de cortes al que pertenece

        // Tamano del bloque a 1:1, en metros de modelo (sin huecos entre vistas)
        public double ModelWidth;
        public double ModelHeight;

        // Hoja del despiece (0 = la primera) y recuadro dentro de ella, en metros de papel
        public int Sheet;
        public double CenterX;
        public double CenterY;
        public double Width;
        public double Height;
    }


    /// <summary>
    /// Las tres vistas de un cuerpo del despiece. Perfil y planta son hijas proyectadas del alzado:
    /// el perfil alineado horizontalmente por centro y la planta verticalmente por centro.
    /// </summary>
    public class BodyViewSet
    {
        public View Front;    // alzado: la vista relativa, cara de mayor area al usuario
        public View Profile;  // perfil
        public View Plan;     // planta

        public int Index;     // cuerpo de la lista de cortes al que pertenece

        public BodyRegion Region;  // recuadro de la hoja reservado a este bloque
    }


    public class BodyOrientation
    {
        public Face2 FrontFace;
        public Face2 RightFace;
        public double[] FrontNormal;
        public double[] DominantDirection;
        public bool HasRightAngleCorner;
    }


    public static class VeraDelMathClass
    {
        // Direccion sin simetria con los ejes: desempata caras de igual area de forma repetible.
        public static readonly double[] TieBreakDirection = { 0.2672612419124244, 0.5345224838248488, 0.8017837257372732 };

        public static double DotProduct(double[] v1, double[] v2)
        {
            if (v1 == null || v2 == null) throw new ArgumentNullException();
            if (v1.Length != 3 || v2.Length != 3) throw new Exception("Vector length not equal");

            double x = v1[0] * v2[0];
            double y = v1[1] * v2[1];
            double z = v1[2] * v2[2];
            return x + y + z;

        }


        public static double[] CrossProduct(double[] v1, double[] v2)
        {

            double x = v1[1] * v2[2] - v1[2] * v2[1];
            double y = v1[2] * v2[0] - v1[0] * v2[2];
            double z = v1[0] * v2[1] - v1[1] * v2[0];

            return new double[] { x, y, z };
        }

        public static double[] Subtract(double[] v1, double[] v2)
        {
            return new double[] { v1[0] - v2[0], v1[1] - v2[1], v1[2] - v2[2] };
        }

        public static double[] Scale(double[] v, double factor)
        {
            return new double[] { v[0] * factor, v[1] * factor, v[2] * factor };
        }

        public static double Length(double[] v)
        {
            return Math.Sqrt(v[0] * v[0] + v[1] * v[1] + v[2] * v[2]);
        }

        public static double[] Normalize(double[] v)
        {
            if (v == null) return null;

            double length = Length(v);
            if (length < 1e-12) return null;

            return Scale(v, 1.0 / length);
        }

        /// <summary>
        /// Quita a v su componente sobre la normal y lo vuelve unitario: el resultado queda dentro del plano.
        /// </summary>
        public static double[] ProjectOntoPlane(double[] v, double[] normal)
        {
            double projection = DotProduct(v, normal);
            double[] inPlane = Subtract(v, Scale(normal, projection));
            return Normalize(inPlane);
        }

    }


    public static class BoilermakerDebug
    {

        public static void PrintNormalValues(Face2 face)
        {
            double[] normal = face.Normal;

            Console.WriteLine($"Valores de la normal: ({normal?[0]},{normal?[1]},{normal?[2]})");

        }

        /// <summary>
        /// Solo se llama cuando no se encontro ninguna cara plana: dice por que se descarto cada una.
        /// PLANE_TYPE = 4001.
        /// </summary>
        public static void PrintFaceTypes(object[] oFaces)
        {
            Console.WriteLine("  ninguna cara plana, volcado de caras:");

            for (int i = 0; i < oFaces.Length; i++)
            {
                Face2 face = oFaces[i] as Face2;
                if (face == null)
                {
                    Console.WriteLine($"    [{i}] no es Face2");
                    continue;
                }

                Surface surface = face.GetSurface() as Surface;
                string identity = surface == null ? "sin superficie" : surface.Identity().ToString();

                Console.WriteLine($"    [{i}] identity = {identity}, shellType = {face.GetShellType()}, area = {face.GetArea():F6}");
            }
        }

        public static void PrintVector(string label, double[] v)
        {
            if (v == null)
            {
                Console.WriteLine($"{label}: null");
                return;
            }

            Console.WriteLine($"{label}: ({v[0]:F6}, {v[1]:F6}, {v[2]:F6})");
        }

    }

}
