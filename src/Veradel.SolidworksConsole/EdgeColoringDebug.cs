using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace Veradel.SolidworksConsole
{
    /// <summary>
    /// Copia SIMPLIFICADA y autocontenida de la lógica de "Colorear aristas" del addin
    /// (VeradeAddin.Services.SolidWorksService.EdgeColoring.cs), para depurar los falsos positivos paso a
    /// paso sin el ruido del addin (sin toggles de rendimiento, sin suspend de selección, sin release COM,
    /// sin silhouettes, sin memoria de aristas). Los métodos conservan los MISMOS nombres que el addin para
    /// que el mapeo 1:1 sea evidente al depurar.
    ///
    /// Flujo (Run):
    ///   1. Seleccionas la arista culpable en la vista de pieza dentro del dibujo.
    ///   2. El programa detecta los colores de la pieza y te PIDE cuál usar (por índice).
    ///   3. Ejecuta ColorOneView sobre esa vista con ese color -> pon breakpoints y observa dónde se
    ///      selecciona una arista que no debería (el falso positivo).
    /// </summary>
    internal sealed class EdgeColoringDebug
    {
        private const double EdgeGridCellSize = 0.01;        // metros
        private const double PerpendicularTolerance = 0.002;
        private const double OnFaceTolerance = 1e-6;
        private const double MidpointKeyScale = 1e6;

        private readonly SldWorks _sw;

        private static (long, long, long) MidpointKey(double x, double y, double z)
        {
            return ((long)Math.Round(x * MidpointKeyScale),
                    (long)Math.Round(y * MidpointKeyScale),
                    (long)Math.Round(z * MidpointKeyScale));
        }

        public EdgeColoringDebug(SldWorks sw)
        {
            _sw = sw;
        }

        // ===================== orquestación =====================

        public void Run()
        {
            var model = _sw.IActiveDoc2 as IModelDoc2;
            if (model == null || model.GetType() != (int)swDocumentTypes_e.swDocDRAWING)
            {
                Console.WriteLine("No hay un dibujo activo. Abre el dibujo y selecciona una arista.");
                return;
            }

            var drawing = model as IDrawingDoc;
            var selMgr = model.SelectionManager as ISelectionMgr;
            if (drawing == null || selMgr == null)
            {
                Console.WriteLine("No se pudo acceder al dibujo.");
                return;
            }

            // 1) Vista de pieza a partir de la arista seleccionada.
            string err;
            IView view = GetSingleSelectedPartView(selMgr, out err);
            if (view == null)
            {
                Console.WriteLine("Vista no resuelta: " + err);
                return;
            }

            var partModel = view.ReferencedDocument as IModelDoc2;
            if (partModel == null)
            {
                Console.WriteLine("La vista no referencia ninguna pieza.");
                return;
            }

            Vec3 viewNormal = ViewNormal(view);
            bool realView = IsRealView(view, partModel);
            bool isoView = realView && IsIsometricView(view);
            Console.WriteLine(string.Format("Vista '{0}'  type={1}  realView={2}  iso={3}  viewNormal=({4:0.###},{5:0.###},{6:0.###})",
                view.GetName2(), view.Type, realView, isoView, viewNormal.X, viewNormal.Y, viewNormal.Z));

            // Aristas concretas seleccionadas (solo estas se comprobarán/colorearán).
            var selectedEdges = CollectSelectedEdges(selMgr);
            if (selectedEdges.Count == 0)
            {
                Console.WriteLine("No hay ninguna ARISTA en la selección. Selecciona la arista a comprobar.");
                return;
            }
            Console.WriteLine("Aristas seleccionadas a comprobar: " + selectedEdges.Count);

            // 2) Detectar colores y PEDIR cuál usar.
            var colors = new List<DetectedColor>();
            DetectColors(partModel, colors);
            if (colors.Count == 0)
            {
                Console.WriteLine("No se detectaron apariencias de color en la pieza.");
                return;
            }

            Console.WriteLine();
            Console.WriteLine("Colores detectados en la pieza:");
            for (int i = 0; i < colors.Count; i++)
            {
                var c = colors[i];
                Console.WriteLine(string.Format("  [{0}] ({1},{2},{3})  {4}", i, c.R, c.G, c.B, c.Name));
            }
            Console.Write("Elige el índice del color a depurar: ");
            var source = colors[ReadIndex(colors.Count)];
            Console.WriteLine(string.Format("Color origen elegido: ({0},{1},{2})", source.R, source.G, source.B));

            var map = new EdgeColorMapping
            {
                PartPath = partModel.GetPathName(),
                SourceR = source.R, SourceG = source.G, SourceB = source.B,
                // Destino: un color visible para localizar las aristas pintadas (verde si el origen es rojo).
                TargetR = (source.R == 255 && source.G == 0 && source.B == 0) ? 0 : 255, TargetG = source.R == 255 ? 200 : 0, TargetB = 0
            };

            // 3) Comprobar SOLO las aristas seleccionadas y colorear únicamente las que deban.
            int edges = ColorMappingInView(drawing, model, selMgr, view, partModel, selectedEdges,
                viewNormal, realView, isoView, map);
            model.GraphicsRedraw2();
            Console.WriteLine();
            Console.WriteLine(string.Format("Aristas coloreadas: {0} de {1} seleccionadas", edges, selectedEdges.Count));
            Console.WriteLine("(pon breakpoints en ShouldColorReal / ShouldColorSynthetic / PointLiesOnGridFace para ver la decisión)");
        }

        private static int ReadIndex(int count)
        {
            while (true)
            {
                var s = Console.ReadLine();
                int idx;
                if (int.TryParse(s, out idx) && idx >= 0 && idx < count) return idx;
                Console.Write("Índice inválido, reintenta: ");
            }
        }

        // ===================== resolución de la vista seleccionada =====================

        private IView GetSingleSelectedPartView(ISelectionMgr selMgr, out string error)
        {
            error = null;
            int n = selMgr.GetSelectedObjectCount2(-1);
            if (n == 0) { error = "Selecciona primero una arista de una vista de pieza."; return null; }

            var byName = new Dictionary<string, IView>(StringComparer.OrdinalIgnoreCase);
            for (int i = 1; i <= n; i++)
            {
                var vobj = selMgr.GetSelectedObjectType3(i, -1) == (int)swSelectType_e.swSelDRAWINGVIEWS
                    ? selMgr.GetSelectedObject6(i, -1)
                    : selMgr.GetSelectedObjectsDrawingView2(i, -1);

                var v = vobj as IView;
                if (v == null) continue;
                string nm = v.GetName2();
                if (!string.IsNullOrEmpty(nm) && !byName.ContainsKey(nm)) byName[nm] = v;
            }

            var partViews = byName.Values
                .Where(v => (v.ReferencedDocument as IModelDoc2)?.GetType() == (int)swDocumentTypes_e.swDocPART)
                .ToList();

            if (partViews.Count == 0) { error = "Selecciona una arista de una vista que referencie una pieza."; return null; }
            if (partViews.Count > 1) { error = "Hay varias vistas en la selección. Selecciona aristas de UNA sola vista."; return null; }
            return partViews[0];
        }

        // ===================== colorear la vista =====================

        private int ColorMappingInView(IDrawingDoc drawing, IModelDoc2 drawingModel, ISelectionMgr selMgr,
            IView view, IModelDoc2 partModel, List<object> selectedEdges, Vec3 viewNormal,
            bool realView, bool isoView, EdgeColorMapping map)
        {
            var planar = new List<FaceData>();
            var cylinder = new List<FaceData>();
            var cone = new List<FaceData>();
            GatherColoredFaces(partModel, map, planar, cylinder, cone);
            Console.WriteLine(string.Format("Caras coloreadas: planas={0} cilindros={1} conos={2}", planar.Count, cylinder.Count, cone.Count));

            var planarGrid = realView ? null : BuildGrid(planar);
            var cylinderGrid = BuildGrid(cylinder);
            var coneGrid = BuildGrid(cone);

            int targetSw = RgbToSwInt(map.TargetR, map.TargetG, map.TargetB);
            return ColorOneView(drawing, drawingModel, selMgr, view, selectedEdges, planar, cylinder, cone,
                planarGrid, cylinderGrid, coneGrid, targetSw, viewNormal, realView, isoView);
        }

        // Comprueba SOLO las aristas seleccionadas y colorea únicamente las que deban, replicando la
        // decisión del addin: vista real -> la arista pertenece a una cara coloreada que cualifica;
        // vista sintética -> el midpoint de la arista cae sobre una cara coloreada del grid de su clase.
        private int ColorOneView(IDrawingDoc drawing, IModelDoc2 drawingModel, ISelectionMgr selMgr, IView view,
            List<object> selectedEdges,
            List<FaceData> planar, List<FaceData> cylinder, List<FaceData> cone,
            Dictionary<(int, int, int), List<FaceData>> planarGrid,
            Dictionary<(int, int, int), List<FaceData>> cylinderGrid,
            Dictionary<(int, int, int), List<FaceData>> coneGrid,
            int targetSw, Vec3 viewNormal, bool real, bool iso)
        {
            drawingModel.ClearSelection2(true);
            var selData = selMgr.CreateSelectData();
            var viewT = view as View;
            if (viewT != null) selData.View = viewT;

            int colored = 0;
            for (int i = 0; i < selectedEdges.Count; i++)
            {
                var oedge = selectedEdges[i];
                bool should = real
                    ? ShouldColorReal(oedge, planar, cylinder, cone, viewNormal, iso)                    // <-- breakpoint decisión (vista real)
                    : ShouldColorSynthetic(oedge, planarGrid, cylinderGrid, coneGrid, viewNormal);        // <-- breakpoint decisión (vista sintética)

                double mx, my, mz;
                string mid = EdgeMidpoint(oedge, out mx, out my, out mz)
                    ? string.Format("({0:0.#####},{1:0.#####},{2:0.#####})", mx, my, mz) : "(sin midpoint)";
                Console.WriteLine(string.Format("  arista[{0}] midpoint={1} class={2} => {3}",
                    i, mid, EdgeColorClass(oedge, viewNormal), should ? "COLOREA" : "no colorea"));

                if (should)
                {
                    selMgr.AddSelectionListObject(oedge, selData);
                    colored++;
                }
            }

            if (colored > 0) drawing.SetLineColor(targetSw);
            drawingModel.ClearSelection2(true);
            return colored;
        }

        // Vista real: la arista se colorea si es arista de alguna cara coloreada que cualifica (plano
        // perpendicular a la vista -salvo isométrica-, o cilindro/cono). Se empareja por clave de midpoint,
        // igual que el addin mapea las aristas de cada cara coloreada.
        private bool ShouldColorReal(object selEdge, List<FaceData> planar, List<FaceData> cylinder,
            List<FaceData> cone, Vec3 viewNormal, bool iso)
        {
            double sx, sy, sz;
            if (!EdgeMidpoint(selEdge, out sx, out sy, out sz)) return false;
            var selKey = MidpointKey(sx, sy, sz);

            var faces3d = iso
                ? planar.Concat(cylinder).Concat(cone)
                : planar.Where(f => ArePerpendicular(viewNormal, f.Normal)).Concat(cylinder).Concat(cone);

            foreach (var fd in faces3d)
                foreach (var edge3d in FaceEdges(fd.Face))
                {
                    double ex, ey, ez;
                    if (EdgeMidpoint(edge3d, out ex, out ey, out ez) && MidpointKey(ex, ey, ez).Equals(selKey))
                        return true;
                }
            return false;
        }

        // Vista sintética: la arista se colorea si su clase es válida y su midpoint cae sobre una cara
        // coloreada del grid correspondiente.
        private bool ShouldColorSynthetic(object selEdge, Dictionary<(int, int, int), List<FaceData>> planarGrid,
            Dictionary<(int, int, int), List<FaceData>> cylinderGrid,
            Dictionary<(int, int, int), List<FaceData>> coneGrid, Vec3 viewNormal)
        {
            int kind = EdgeColorClass(selEdge, viewNormal);
            if (kind < 0) return false;
            double mx, my, mz;
            if (!EdgeMidpoint(selEdge, out mx, out my, out mz)) return false;
            var grid = kind == 0 ? planarGrid : (kind == 1 ? cylinderGrid : coneGrid);
            return PointLiesOnGridFace(mx, my, mz, grid);
        }

        private static List<object> CollectSelectedEdges(ISelectionMgr selMgr)
        {
            var list = new List<object>();
            int n = selMgr.GetSelectedObjectCount2(-1);
            for (int i = 1; i <= n; i++)
            {
                if (selMgr.GetSelectedObjectType3(i, -1) != (int)swSelectType_e.swSelEDGES) continue;
                var o = selMgr.GetSelectedObject6(i, -1);
                if (o != null) list.Add(o);
            }
            return list;
        }

        // ===================== detección / clasificación =====================

        private static void DetectColors(IModelDoc2 part, List<DetectedColor> into)
        {
            var ext = part.Extension;
            if (ext == null) return;
            var rms = ext.GetRenderMaterials2((int)swDisplayStateOpts_e.swAllDisplayState, string.Empty) as object[];
            if (rms == null) return;

            var seen = new HashSet<int>();
            foreach (var o in rms)
            {
                var rm = o as RenderMaterial;
                if (rm == null) continue;
                int primary = rm.PrimaryColor;
                if (!seen.Add(primary)) continue;
                int r, g, b;
                SwColorToRgb(primary, out r, out g, out b);
                into.Add(new DetectedColor { R = r, G = g, B = b, Name = SafeFileName(rm.FileName) });
            }
        }

        private static void GatherColoredFaces(IModelDoc2 part, EdgeColorMapping map,
            List<FaceData> planar, List<FaceData> cylinder, List<FaceData> cone)
        {
            var ext = part.Extension;
            var rms = ext != null ? ext.GetRenderMaterials2((int)swDisplayStateOpts_e.swAllDisplayState, string.Empty) as object[] : null;
            if (rms == null) return;

            foreach (var o in rms)
            {
                var rm = o as RenderMaterial;
                if (rm == null) continue;

                int r, g, b;
                SwColorToRgb(rm.PrimaryColor, out r, out g, out b);
                if (r != map.SourceR || g != map.SourceG || b != map.SourceB) continue;

                int before = planar.Count + cylinder.Count + cone.Count;

                var ents = rm.GetEntities() as object[];
                if (ents != null) foreach (var e in ents) AddEntityFaces(e, planar, cylinder, cone);

                // Nada concreto -> apariencia a nivel de pieza: aplica a todas las caras.
                if (planar.Count + cylinder.Count + cone.Count == before)
                    AddAllPartFaces(part, planar, cylinder, cone);
            }
        }

        private static void AddEntityFaces(object oEntity, List<FaceData> planar, List<FaceData> cylinder, List<FaceData> cone)
        {
            var entity = oEntity as Entity;
            if (entity == null) return;

            switch (entity.GetType())
            {
                case (int)swSelectType_e.swSelFACES:
                    AddFace(oEntity as Face2, planar, cylinder, cone);
                    break;
                case (int)swSelectType_e.swSelBODYFEATURES:
                    var feat = oEntity as Feature;
                    var ffaces = feat != null ? feat.GetFaces() as object[] : null;
                    if (ffaces != null) foreach (var f in ffaces) AddFace(f as Face2, planar, cylinder, cone);
                    break;
                case (int)swSelectType_e.swSelSOLIDBODIES:
                case (int)swSelectType_e.swSelSURFACEBODIES:
                    AddBodyFaces(oEntity as Body2, planar, cylinder, cone);
                    break;
                case (int)swSelectType_e.swSelCOMPONENTS:
                    var comp = oEntity as Component2;
                    AddBodyFaces(comp != null ? comp.GetBody() as Body2 : null, planar, cylinder, cone);
                    break;
            }
        }

        private static void AddBodyFaces(Body2 body, List<FaceData> planar, List<FaceData> cylinder, List<FaceData> cone)
        {
            if (body == null) return;
            var faces = body.GetFaces() as object[];
            if (faces == null) return;
            foreach (var f in faces) AddFace(f as Face2, planar, cylinder, cone);
        }

        private static void AddAllPartFaces(IModelDoc2 part, List<FaceData> planar, List<FaceData> cylinder, List<FaceData> cone)
        {
            var partDoc = part as IPartDoc;
            if (partDoc == null) return;
            var bodies = partDoc.GetBodies2((int)swBodyType_e.swAllBodies, true) as object[];
            if (bodies == null) return;
            foreach (var ob in bodies) AddBodyFaces(ob as Body2, planar, cylinder, cone);
        }

        private static void AddFace(Face2 face, List<FaceData> planar, List<FaceData> cylinder, List<FaceData> cone)
        {
            if (face == null) return;
            int id = SurfaceType(face);
            var fd = new FaceData { Face = face, Normal = FaceNormal(face), Box = face.GetBox() as double[] };
            switch (id)
            {
                case (int)swSurfaceTypes_e.PLANE_TYPE: planar.Add(fd); break;
                case (int)swSurfaceTypes_e.CYLINDER_TYPE: cylinder.Add(fd); break;
                case (int)swSurfaceTypes_e.CONE_TYPE: cone.Add(fd); break;
            }
        }

        /// <summary>-1 descartar, 0 planar, 1 cilindro, 2 cono.</summary>
        private static int EdgeColorClass(object oEdge, Vec3 viewNormal)
        {
            var edge = oEdge as Edge;
            if (edge == null) return -1;

            var adj = edge.GetTwoAdjacentFaces2() as object[];
            if (adj == null) return -1;

            var f0 = adj.Length > 0 ? adj[0] as Face2 : null;
            var f1 = adj.Length > 1 ? adj[1] as Face2 : null;
            if (f0 == null && f1 == null) return -1;

            int t0 = SurfaceType(f0);
            int t1 = SurfaceType(f1);

            bool plane0 = t0 == (int)swSurfaceTypes_e.PLANE_TYPE;
            bool plane1 = t1 == (int)swSurfaceTypes_e.PLANE_TYPE;
            bool cone0 = t0 == (int)swSurfaceTypes_e.CONE_TYPE;
            bool cone1 = t1 == (int)swSurfaceTypes_e.CONE_TYPE;

            if (plane0 && plane1) return 0;
            if (cone0 || cone1) return 2;
            if (plane0) return ArePerpendicular(FaceNormal(f0), viewNormal) ? 0 : 1;
            if (plane1) return ArePerpendicular(FaceNormal(f1), viewNormal) ? 0 : 1;
            return 1;
        }

        // ===================== geometría =====================

        private sealed class FaceData
        {
            public Face2 Face;
            public Vec3 Normal;
            public double[] Box;
        }

        private struct Vec3
        {
            public double X;
            public double Y;
            public double Z;
        }

        private static Vec3 FaceNormal(Face2 face)
        {
            if (face == null) return new Vec3();
            var n = face.Normal as double[];
            if (n == null || n.Length < 3) return new Vec3();
            return new Vec3 { X = n[0], Y = n[1], Z = n[2] };
        }

        private static Vec3 ViewNormal(IView view)
        {
            var xf = view.ModelToViewTransform;
            var d = xf != null ? xf.ArrayData as double[] : null;
            if (d == null || d.Length < 9) return new Vec3 { X = 0, Y = 0, Z = 1 };
            return new Vec3 { X = Math.Round(d[2], 6), Y = Math.Round(d[5], 6), Z = Math.Round(d[8], 6) };
        }

        private static bool ArePerpendicular(Vec3 a, Vec3 b)
        {
            double dot = Math.Round(a.X * b.X + a.Y * b.Y + a.Z * b.Z, 6);
            return Math.Abs(dot) < PerpendicularTolerance;
        }

        private static int SurfaceType(Face2 face)
        {
            if (face == null) return -1;
            var surf = face.GetSurface() as Surface;
            if (surf == null) return -1;
            return surf.Identity();
        }

        private static Dictionary<(int, int, int), List<FaceData>> BuildGrid(List<FaceData> faces)
        {
            var grid = new Dictionary<(int, int, int), List<FaceData>>();
            foreach (var f in faces)
            {
                if (f.Box == null || f.Box.Length < 6) continue;
                int ix0 = Cell(f.Box[0]) - 1, ix1 = Cell(f.Box[3]) + 1;
                int iy0 = Cell(f.Box[1]) - 1, iy1 = Cell(f.Box[4]) + 1;
                int iz0 = Cell(f.Box[2]) - 1, iz1 = Cell(f.Box[5]) + 1;
                for (int x = ix0; x <= ix1; x++)
                    for (int y = iy0; y <= iy1; y++)
                        for (int z = iz0; z <= iz1; z++)
                        {
                            var key = (x, y, z);
                            List<FaceData> list;
                            if (!grid.TryGetValue(key, out list)) grid[key] = list = new List<FaceData>();
                            list.Add(f);
                        }
            }
            return grid;
        }

        private static int Cell(double v)
        {
            return (int)Math.Floor(v / EdgeGridCellSize);
        }

        private static bool PointLiesOnGridFace(double x, double y, double z, Dictionary<(int, int, int), List<FaceData>> grid)
        {
            if (grid == null) return false;
            var key = (Cell(x), Cell(y), Cell(z));
            List<FaceData> faces;
            if (!grid.TryGetValue(key, out faces)) { Console.WriteLine("      grid: celda vacía"); return false; }

            foreach (var f in faces)
            {
                // Rechazo por bounding box ANTES del closest-point: GetClosestPointOn evalúa la superficie
                // SIN recortar (plano infinito / cilindro completo) -> una arista coplanaria/coaxial hasta
                // una celda fuera de la cara real daba distancia 0 y se coloreaba (el falso positivo).
                bool inBox = PointInBox(x, y, z, f.Box);

                var cp = f.Face.GetClosestPointOn(x, y, z) as double[];
                double dist = double.NaN;
                if (cp != null && cp.Length >= 3)
                {
                    double dx = cp[0] - x, dy = cp[1] - y, dz = cp[2] - z;
                    dist = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                }

                bool surfHit = !double.IsNaN(dist) && dist < OnFaceTolerance;
                Console.WriteLine(string.Format("      cara {0} box=[{1}] dist={2}  inBox={3}{4}",
                    SurfaceType(f.Face) == (int)swSurfaceTypes_e.PLANE_TYPE ? "PLANO" :
                    SurfaceType(f.Face) == (int)swSurfaceTypes_e.CYLINDER_TYPE ? "CILINDRO" : "CONO",
                    f.Box != null && f.Box.Length >= 6
                        ? string.Format("{0:0.####},{1:0.####},{2:0.####} .. {3:0.####},{4:0.####},{5:0.####}",
                            f.Box[0], f.Box[1], f.Box[2], f.Box[3], f.Box[4], f.Box[5]) : "?",
                    double.IsNaN(dist) ? "n/a" : dist.ToString("0.0e+0"),
                    inBox,
                    surfHit && !inBox ? "  <== FALSO POSITIVO evitado (superficie sin recortar)" : ""));

                if (inBox && surfHit) return true;
            }
            return false;
        }

        private static bool PointInBox(double x, double y, double z, double[] box)
        {
            const double BoxTolerance = 1e-5;
            if (box == null || box.Length < 6) return false;
            return x >= box[0] - BoxTolerance && x <= box[3] + BoxTolerance &&
                   y >= box[1] - BoxTolerance && y <= box[4] + BoxTolerance &&
                   z >= box[2] - BoxTolerance && z <= box[5] + BoxTolerance;
        }

        private static IEnumerable<object> FaceEdges(Face2 face)
        {
            var edges = face.GetEdges() as object[];
            if (edges == null) yield break;
            foreach (var e in edges) if (e != null) yield return e;
        }

        private static IEnumerable<object> VisibleEdges(IView view)
        {
            var comps = view.GetVisibleComponents() as object[];
            if (comps == null || comps.Length == 0)
            {
                var ents = view.GetVisibleEntities2(null, (int)swViewEntityType_e.swViewEntityType_Edge) as object[];
                if (ents != null) foreach (var e in ents) if (e != null) yield return e;
                yield break;
            }

            foreach (var oc in comps)
            {
                var comp = oc as Component2;
                var ents = view.GetVisibleEntities2(comp, (int)swViewEntityType_e.swViewEntityType_Edge) as object[];
                if (ents != null) foreach (var e in ents) if (e != null) yield return e;
            }
        }

        private static bool EdgeMidpoint(object oEdge, out double x, out double y, out double z)
        {
            x = y = z = 0;
            var edge = oEdge as Edge;
            if (edge == null) return false;

            var curve = edge.GetCurve() as Curve;
            if (curve == null) return false;

            var sv = edge.GetStartVertex() as Vertex;
            var ev = edge.GetEndVertex() as Vertex;

            double tMid;
            if (sv != null && ev != null)
            {
                var sp = sv.GetPoint() as double[];
                var ep = ev.GetPoint() as double[];
                if (sp == null || ep == null || sp.Length < 3 || ep.Length < 3) return false;
                double ts = curve.ReverseEvaluate(sp[0], sp[1], sp[2]);
                double te = curve.ReverseEvaluate(ep[0], ep[1], ep[2]);
                tMid = (ts + te) / 2.0;
            }
            else
            {
                double t0, t1; bool isClosed, isPeriodic;
                curve.GetEndParams(out t0, out t1, out isClosed, out isPeriodic);
                tMid = (t0 + t1) / 2.0;
            }

            var eval = curve.Evaluate2(tMid, 0) as double[];
            if (eval == null || eval.Length < 3) return false;
            x = Math.Round(eval[0], 6);
            y = Math.Round(eval[1], 6);
            z = Math.Round(eval[2], 6);
            return true;
        }

        private static bool IsRealView(IView view, IModelDoc2 partModel)
        {
            int type = view.Type;
            if (type == (int)swDrawingViewTypes_e.swDrawingSectionView) return false;
            if (view.GetBreakOutSectionCount() > 0) return false;

            switch ((swDrawingViewTypes_e)type)
            {
                case swDrawingViewTypes_e.swDrawingNamedView:
                case swDrawingViewTypes_e.swDrawingRelativeView:
                    return true;
            }

            var ext = partModel.Extension;
            int sampled = 0, mapped = 0;
            foreach (var e in VisibleEdges(view))
            {
                var corr = ext != null ? ext.GetCorrespondingEntity2(e) : null;
                if (corr != null) mapped++;
                if (++sampled >= 5) break;
            }

            if (sampled == 0) return true;
            return mapped == sampled;
        }

        private static bool IsIsometricView(IView view)
        {
            switch ((swDrawingViewTypes_e)view.Type)
            {
                case swDrawingViewTypes_e.swDrawingProjectedView:
                case swDrawingViewTypes_e.swDrawingAuxiliaryView:
                case swDrawingViewTypes_e.swDrawingSectionView:
                case swDrawingViewTypes_e.swDrawingDetailView:
                case swDrawingViewTypes_e.swDrawingAlternatePositionView:
                    return false;
            }

            switch (view.GetOrientationName() ?? string.Empty)
            {
                case "*Isometric":
                case "*Dimetric":
                case "*Trimetric":
                    return true;
                case "*Front":
                case "*Back":
                case "*Top":
                case "*Bottom":
                case "*Left":
                case "*Right":
                case "*Current":
                case "*Normal To":
                    return false;
            }

            var xf = view.ModelToViewTransform;
            var m = xf != null ? xf.ArrayData as double[] : null;
            if (m == null || m.Length < 9) return false;

            double[] px = { m[0], m[3] };
            double[] py = { m[1], m[4] };
            double[] pz = { m[2], m[5] };
            const double epsLen = 1e-3, epsCross = 1e-3;

            if (Len2(px) < epsLen || Len2(py) < epsLen || Len2(pz) < epsLen) return false;
            if (Cross2(px, py) < epsCross) return false;
            if (Cross2(py, pz) < epsCross) return false;
            if (Cross2(px, pz) < epsCross) return false;
            return true;
        }

        private static double Len2(double[] v)
        {
            return Math.Sqrt(v[0] * v[0] + v[1] * v[1]);
        }

        private static double Cross2(double[] a, double[] b)
        {
            return Math.Abs(a[0] * b[1] - a[1] * b[0]);
        }

        private static void SwColorToRgb(int color, out int r, out int g, out int b)
        {
            r = color & 0xFF;
            g = (color >> 8) & 0xFF;
            b = (color >> 16) & 0xFF;
        }

        private static int RgbToSwInt(int r, int g, int b)
        {
            return (b << 16) | (g << 8) | r;
        }

        private static string SafeFileName(string path)
        {
            return string.IsNullOrEmpty(path) ? string.Empty : System.IO.Path.GetFileNameWithoutExtension(path);
        }

        // ===================== tipos locales (equivalentes a los modelos del addin) =====================

        private sealed class DetectedColor
        {
            public int R, G, B;
            public string Name;
        }

        private sealed class EdgeColorMapping
        {
            public string PartPath;
            public int SourceR, SourceG, SourceB;
            public int TargetR, TargetG, TargetB;
        }
    }
}
