using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using VeradeAddin.Infrastructure;
using VeradeAddin.Logging;
using VeradeAddin.Models;

namespace VeradeAddin.Services
{
    /// <summary>
    /// "Colorear aristas": carries the appearance colours of a part's faces onto the corresponding
    /// edges of its drawing. Geometric idea: a planar face whose normal is perpendicular to the view
    /// normal is seen edge-on, so its edges are candidate lines in that view. Real views map 3D→2D
    /// via <c>IView.GetCorrespondingEntity</c>; synthetic views (sections, etc.) and silhouettes are
    /// matched by edge midpoint against a spatial hash of the coloured faces.
    ///
    /// Scope is ONE view: the command acts only on the single part view the user has selected, so it is
    /// fast (no traversal of every view) and stateless — there is no tracking and no persistence. To undo,
    /// the user runs "Líneas a negro" on the view, which blackens ALL of that view's edges.
    /// </summary>
    public sealed partial class SolidWorksService
    {
        private const double EdgeGridCellSize = 0.01;        // metres
        private const double PerpendicularTolerance = 0.002;
        private const double OnFaceTolerance = 1e-6;

        // ===================== public API =====================

        public EdgeColoringPlan InspectDrawingForEdgeColoring()
        {
            var plan = new EdgeColoringPlan();

            var model = _sw.IActiveDoc2 as IModelDoc2;
            if (model == null || model.GetType() != (int)swDocumentTypes_e.swDocDRAWING)
            {
                return plan; // IsDrawing = false
            }
            plan.IsDrawing = true;

            var selMgr = model.SelectionManager as ISelectionMgr;
            if (selMgr == null) { plan.Message = "No se pudo acceder al dibujo."; return plan; }

            string err;
            IView view = GetSingleSelectedPartView(selMgr, out err);
            try
            {
                if (view == null) { plan.Message = err; return plan; }

                var refDoc = view.ReferencedDocument as IModelDoc2;
                try
                {
                    if (refDoc == null)
                    {
                        plan.Message = "La vista seleccionada no referencia ninguna pieza.";
                        return plan;
                    }
                    string path = refDoc.GetPathName();
                    if (string.IsNullOrWhiteSpace(path))
                    {
                        plan.Message = "La pieza de la vista no está guardada.";
                        return plan;
                    }

                    plan.ViewName = view.GetName2();
                    var info = new EdgeColorPartInfo
                    {
                        PartPath = path,
                        PartName = Path.GetFileNameWithoutExtension(path)
                    };
                    DetectColors(refDoc, info.Colors);
                    info.FaceCount = CountPartFaces(refDoc);
                    plan.Parts.Add(info);

                    if (info.Colors.Count == 0)
                    {
                        plan.Message = "No se detectaron apariencias de color en la pieza de la vista.";
                    }
                }
                finally { ComRelease.Release(refDoc); }
            }
            finally
            {
                ComRelease.Release(view);
                ComRelease.Release(selMgr);
            }

            return plan;
        }

        public EdgeColoringResult ApplyEdgeColoring(EdgeColorRequest request)
        {
            var result = new EdgeColoringResult();
            if (request == null || request.Mappings.Count == 0)
            {
                result.Error = "No se seleccionó ningún color.";
                return result;
            }

            var model = _sw.IActiveDoc2 as IModelDoc2;
            if (model == null || model.GetType() != (int)swDocumentTypes_e.swDocDRAWING)
            {
                result.Error = "No hay un dibujo activo.";
                return result;
            }

            var drawing = model as IDrawingDoc;
            var selMgr = model.SelectionManager as ISelectionMgr;
            if (drawing == null || selMgr == null)
            {
                result.Error = "No se pudo acceder al dibujo.";
                return result;
            }

            // Resolve the target view by name (it was captured at inspect time): robust even if the SW
            // selection changed while the colour dialog was open.
            IView view = ResolveViewByName(drawing, request.ViewName);
            if (view == null)
            {
                result.Error = "No se encontró la vista seleccionada. Vuelve a seleccionarla y reinténtalo.";
                ComRelease.Release(selMgr);
                return result;
            }

            var selData = selMgr.CreateSelectData();
            try
            {
                foreach (var map in request.Mappings)
                {
                    try
                    {
                        ColorMappingInView(drawing, model, selMgr, selData, view, map, result);
                    }
                    catch (Exception ex)
                    {
                        result.Errors.Add("Color (" + map.SourceR + "," + map.SourceG + "," + map.SourceB + "): " + ex.Message);
                    }
                }
                result.ViewsProcessed = 1;
                result.Success = result.Errors.Count == 0;
            }
            finally
            {
                model.ClearSelection2(true);
                ComRelease.Release(selData);
                ComRelease.Release(view);
                ComRelease.Release(selMgr);
            }

            return result;
        }

        public EdgeColoringResult ClearEdgeColors()
        {
            var result = new EdgeColoringResult();

            var model = _sw.IActiveDoc2 as IModelDoc2;
            if (model == null || model.GetType() != (int)swDocumentTypes_e.swDocDRAWING)
            {
                result.Error = "No hay un dibujo activo.";
                return result;
            }

            var drawing = model as IDrawingDoc;
            var selMgr = model.SelectionManager as ISelectionMgr;
            if (drawing == null || selMgr == null)
            {
                result.Error = "No se pudo acceder al dibujo.";
                return result;
            }

            string err;
            IView view = GetSingleSelectedPartView(selMgr, out err);
            if (view == null)
            {
                result.Error = err;
                ComRelease.Release(selMgr);
                return result;
            }

            int black = RgbToSwInt(0, 0, 0);
            bool oldApply = _sw.GetApplySelectionFilter();
            object oldFilters = _sw.GetSelectionFilters();
            var selData = selMgr.CreateSelectData();

            // One view only, but still disable every refresh mechanism and do ONE redraw at the end — a
            // part with thousands of edges would otherwise repaint per edge (standard SW perf recipe):
            //   CommandInProgress / SuspendSelectionList / EnableGraphicsUpdate / EnableFeatureTree.
            // All restored in finally (CommandInProgress MUST be reset or the UI stays frozen).
            var mview = model.ActiveView as ModelView;
            bool hadGraphics = mview != null && mview.EnableGraphicsUpdate;
            var featMgr = model.FeatureManager;
            bool hadFeatureTree = featMgr != null && featMgr.EnableFeatureTree;
            bool suspended = false;
            bool commandInProgress = false;

            try
            {
                _sw.CommandInProgress = true;
                commandInProgress = true;
                if (mview != null) mview.EnableGraphicsUpdate = false;
                if (featMgr != null) featMgr.EnableFeatureTree = false;
                selMgr.SuspendSelectionList();
                suspended = true;

                result.EdgesColored = BlackenAllEdgesInView(drawing, model, selMgr, selData, view, black);
                result.ViewsProcessed = 1;
                result.Success = true;
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
            }
            finally
            {
                model.ClearSelection2(true);
                ClearActiveSelectionFilters();
                if (oldFilters != null) _sw.SetSelectionFilters(oldFilters, true);
                _sw.SetApplySelectionFilter(oldApply);
                if (suspended) selMgr.ResumeSelectionList2(false);
                if (featMgr != null)
                {
                    featMgr.EnableFeatureTree = hadFeatureTree;
                    ComRelease.Release(featMgr);
                }
                if (mview != null)
                {
                    mview.EnableGraphicsUpdate = hadGraphics;
                    ComRelease.Release(mview);
                }
                if (commandInProgress) _sw.CommandInProgress = false;
                model.GraphicsRedraw2();
                ComRelease.Release(view);
                ComRelease.Release(selData);
                ComRelease.Release(selMgr);
            }

            return result;
        }

        // ===================== selected-view resolution =====================

        // The single part-referencing drawing view the user has selected — directly (the view is selected)
        // or indirectly (a child entity is selected, whose owning view we resolve). Returns null plus a
        // user message when there is not exactly one. Caller releases the returned view.
        private IView GetSingleSelectedPartView(ISelectionMgr selMgr, out string error)
        {
            error = null;
            int n = selMgr.GetSelectedObjectCount2(-1);
            if (n == 0) { error = "Selecciona primero una vista de pieza."; return null; }

            // Collect the distinct views referenced by the selection (de-duplicated by name).
            var byName = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            for (int i = 1; i <= n; i++)
            {
                object vobj = selMgr.GetSelectedObjectType3(i, -1) == (int)swSelectType_e.swSelDRAWINGVIEWS
                    ? selMgr.GetSelectedObject6(i, -1)
                    : selMgr.GetSelectedObjectsDrawingView2(i, -1);

                var v = vobj as IView;
                if (v == null) { ComRelease.Release(vobj); continue; }
                string nm = v.GetName2();
                if (string.IsNullOrEmpty(nm) || byName.ContainsKey(nm)) { ComRelease.Release(vobj); continue; }
                byName[nm] = vobj;
            }

            // Keep only the part views.
            var partViews = new List<object>();
            foreach (var kv in byName)
            {
                var v = kv.Value as IView;
                var refDoc = v != null ? v.ReferencedDocument as IModelDoc2 : null;
                bool isPart = refDoc != null && refDoc.GetType() == (int)swDocumentTypes_e.swDocPART;
                ComRelease.Release(refDoc);
                if (isPart) partViews.Add(kv.Value);
                else ComRelease.Release(kv.Value);
            }

            if (partViews.Count == 0)
            {
                error = "Selecciona una vista que referencie una pieza.";
                return null;
            }
            if (partViews.Count > 1)
            {
                foreach (var o in partViews) ComRelease.Release(o);
                error = "Hay varias vistas seleccionadas. Selecciona solo UNA vista de pieza.";
                return null;
            }
            return partViews[0] as IView;
        }

        // The current sheet's view with this name (or null). Caller releases.
        private static IView ResolveViewByName(IDrawingDoc drawing, string viewName)
        {
            if (string.IsNullOrEmpty(viewName)) return null;

            var sheet = drawing.GetCurrentSheet() as ISheet;
            var views = sheet != null ? sheet.GetViews() as object[] : null;
            IView found = null;
            if (views != null)
            {
                foreach (var o in views)
                {
                    var v = o as IView;
                    if (found == null && v != null &&
                        string.Equals(v.GetName2(), viewName, StringComparison.OrdinalIgnoreCase))
                    {
                        found = v; // keep this COM object
                    }
                    else
                    {
                        ComRelease.Release(o);
                    }
                }
            }
            ComRelease.Release(sheet);
            return found;
        }

        // ===================== colour one view =====================

        private void ColorMappingInView(IDrawingDoc drawing, IModelDoc2 drawingModel, ISelectionMgr selMgr,
            SelectData selData, IView view, EdgeColorMapping map, EdgeColoringResult result)
        {
            int targetSw = RgbToSwInt(map.TargetR, map.TargetG, map.TargetB);

            if (!SameModel(view, map.PartPath))
            {
                result.Errors.Add("La vista no corresponde a la pieza " + Path.GetFileName(map.PartPath ?? ""));
                return;
            }

            var partModel = view.ReferencedDocument as IModelDoc2;
            if (partModel == null)
            {
                result.Errors.Add("No se pudo acceder a la pieza de la vista.");
                return;
            }

            var planar = new List<FaceData>();
            var cylinder = new List<FaceData>();
            var cone = new List<FaceData>();
            try
            {
                GatherColoredFaces(partModel, map, planar, cylinder, cone);

                var planarGrid = BuildGrid(planar);
                var cylinderGrid = BuildGrid(cylinder);
                var coneGrid = BuildGrid(cone);

                ColorOneView(drawing, drawingModel, selMgr, selData, view, partModel,
                    planar, cylinder, cone, planarGrid, cylinderGrid, coneGrid, targetSw, result);

                // Silhouette edges (cylinder/cone outlines) have no model edge — match them by midpoint,
                // scoped to THIS view so other views are untouched.
                try { selData.View = null; } catch { }
                result.EdgesColored += ColorSilhouettes(drawing, drawingModel, selMgr, selData,
                    cylinderGrid, coneGrid, targetSw, view.GetName2());
            }
            finally
            {
                ReleaseFaces(planar);
                ReleaseFaces(cylinder);
                ReleaseFaces(cone);
                ComRelease.Release(partModel);
            }
        }

        // Select and colour the candidate edges of ONE view (the body of the original per-view loop).
        private void ColorOneView(IDrawingDoc drawing, IModelDoc2 drawingModel, ISelectionMgr selMgr,
            SelectData selData, IView view, IModelDoc2 partModel,
            List<FaceData> planar, List<FaceData> cylinder, List<FaceData> cone,
            Dictionary<(int, int, int), List<FaceData>> planarGrid,
            Dictionary<(int, int, int), List<FaceData>> cylinderGrid,
            Dictionary<(int, int, int), List<FaceData>> coneGrid,
            int targetSw, EdgeColoringResult result)
        {
            Vec3 viewNormal = ViewNormal(view);
            var planarPerp = planar.Where(f => ArePerpendicular(viewNormal, f.Normal)).ToList();

            bool real = IsRealView(view, partModel);

            // Selecting an entity inside a drawing view needs the view context, otherwise
            // AddSelectionListObject can't resolve a raw model edge to the view and fails.
            drawingModel.ClearSelection2(true);
            var viewT = view as View;
            if (viewT != null) { try { selData.View = viewT; } catch { } }

            if (real)
            {
                var faces3d = IsIsometricView(view)
                    ? planar.Concat(cylinder).Concat(cone)
                    : planarPerp.Concat(cylinder).Concat(cone);

                foreach (var fd in faces3d)
                {
                    foreach (var edge3d in FaceEdges(fd.Face))
                    {
                        var edge2d = view.GetCorrespondingEntity(edge3d);
                        if (edge2d != null)
                        {
                            selMgr.AddSelectionListObject(edge2d, selData);
                        }
                        ComRelease.Release(edge3d);
                    }
                }
            }
            else
            {
                foreach (var oedge in VisibleEdges(view))
                {
                    int kind = EdgeColorClass(oedge, viewNormal);
                    if (kind < 0) { ComRelease.Release(oedge); continue; }

                    double mx, my, mz;
                    if (!EdgeMidpoint(oedge, out mx, out my, out mz)) { ComRelease.Release(oedge); continue; }

                    var grid = kind == 0 ? planarGrid : (kind == 1 ? cylinderGrid : coneGrid);
                    if (PointLiesOnGridFace(mx, my, mz, grid))
                    {
                        selMgr.AddSelectionListObject(oedge, selData);
                    }
                    ComRelease.Release(oedge);
                }
            }

            // Colour whatever ended up selected (do NOT gate on the AddSelectionListObject return value —
            // it reports false for view-resolved model edges even when they are added).
            int coloredInView = selMgr.GetSelectedObjectCount2(-1);
            if (coloredInView > 0)
            {
                drawing.SetLineColor(targetSw);
                result.EdgesColored += coloredInView;
            }

            drawingModel.ClearSelection2(true);
        }

        private int ColorSilhouettes(IDrawingDoc drawing, IModelDoc2 drawingModel, ISelectionMgr selMgr,
            SelectData selData, Dictionary<(int, int, int), List<FaceData>> cylinderGrid,
            Dictionary<(int, int, int), List<FaceData>> coneGrid, int targetSw, string viewName)
        {
            int colored = 0;
            bool oldApply = _sw.GetApplySelectionFilter();
            object oldFilters = _sw.GetSelectionFilters();
            bool suspended = false;
            List<object> silhouettes = null;

            try
            {
                selMgr.SuspendSelectionList();
                suspended = true;

                silhouettes = CollectViewSilhouettes(drawingModel, selMgr, viewName);

                foreach (var os in silhouettes)
                {
                    var sil = os as SilhouetteEdge;
                    if (sil == null) continue;

                    double x, y, z;
                    if (!SilhouetteMidpoint(sil, out x, out y, out z)) continue;
                    if (!PointLiesOnGridFace(x, y, z, cylinderGrid) && !PointLiesOnGridFace(x, y, z, coneGrid)) continue;

                    if (selMgr.AddSelectionListObject(sil, selData)) colored++;
                }

                if (colored > 0)
                {
                    drawing.SetLineColor(targetSw);
                }
                drawingModel.ClearSelection2(true);
            }
            finally
            {
                drawingModel.ClearSelection2(true);
                ClearActiveSelectionFilters();
                if (oldFilters != null) _sw.SetSelectionFilters(oldFilters, true);
                _sw.SetApplySelectionFilter(oldApply);
                if (suspended) selMgr.ResumeSelectionList2(false);
                if (silhouettes != null) foreach (var os in silhouettes) ComRelease.Release(os);
            }

            return colored;
        }

        // ===================== reset one view (Líneas a negro) =====================

        // Blacken EVERY edge of the view: walk the part's body edges and map each to the view via
        // GetCorrespondingEntity (so hidden/obscured edges are reached too — SelectAll would skip them),
        // add the view's silhouettes, then ONE SetLineColor(black). View-scoped and stateless.
        private int BlackenAllEdgesInView(IDrawingDoc drawing, IModelDoc2 drawingModel, ISelectionMgr selMgr,
            SelectData selData, IView view, int black)
        {
            // Silhouettes are gathered first because collecting them uses SelectAll, which would otherwise
            // wipe a selection list we had already started building.
            var silhouettes = CollectViewSilhouettes(drawingModel, selMgr, view.GetName2());

            var partModel = view.ReferencedDocument as IModelDoc2;
            var partDoc = partModel as IPartDoc;
            try
            {
                drawingModel.ClearSelection2(true);

                // Model edges (visible + hidden) need the view context on selData.
                var viewT = view as View;
                if (viewT != null) { try { selData.View = viewT; } catch { } }

                if (partDoc != null)
                {
                    var bodies = partDoc.GetBodies2((int)swBodyType_e.swAllBodies, true) as object[];
                    if (bodies != null)
                    {
                        foreach (var ob in bodies)
                        {
                            var body = ob as Body2;
                            var edges = body != null ? body.GetEdges() as object[] : null;
                            if (edges != null)
                            {
                                foreach (var oe in edges)
                                {
                                    var edge2d = view.GetCorrespondingEntity(oe);
                                    if (edge2d != null) selMgr.AddSelectionListObject(edge2d, selData);
                                    ComRelease.Release(oe);
                                }
                            }
                            ComRelease.Release(ob);
                        }
                    }
                }

                // Silhouettes are added without a view context (they are already view entities).
                try { selData.View = null; } catch { }
                foreach (var os in silhouettes)
                {
                    if (os != null) selMgr.AddSelectionListObject(os, selData);
                }

                int n = selMgr.GetSelectedObjectCount2(-1);
                if (n > 0) drawing.SetLineColor(black);

                drawingModel.ClearSelection2(true);
                return n;
            }
            finally
            {
                foreach (var os in silhouettes) ComRelease.Release(os);
                ComRelease.Release(partModel);
            }
        }

        // Silhouette entities of one view (by name; null = any view). Uses a SILHOUETTES-filtered SelectAll
        // and filters by the owning drawing view. Clears the selection before returning; caller releases.
        private List<object> CollectViewSilhouettes(IModelDoc2 drawingModel, ISelectionMgr selMgr, string viewName)
        {
            var list = new List<object>();

            ClearActiveSelectionFilters();
            _sw.SetSelectionFilter((int)swSelectType_e.swSelSILHOUETTES, true);
            _sw.SetApplySelectionFilter(true);

            drawingModel.ClearSelection2(true);
            drawingModel.Extension.SelectAll();

            int count = selMgr.GetSelectedObjectCount2(-1);
            for (int i = 1; i <= count; i++)
            {
                if (selMgr.GetSelectedObjectType3(i, -1) != (int)swSelectType_e.swSelSILHOUETTES) continue;

                if (viewName != null)
                {
                    var ov = selMgr.GetSelectedObjectsDrawingView2(i, -1) as IView;
                    string nm = ov != null ? ov.GetName2() : null;
                    ComRelease.Release(ov);
                    if (!string.Equals(nm, viewName, StringComparison.OrdinalIgnoreCase)) continue;
                }

                var sil = selMgr.GetSelectedObject6(i, -1);
                if (sil != null) list.Add(sil);
            }

            drawingModel.ClearSelection2(true);
            return list;
        }

        // ===================== detection / classification =====================

        private static void DetectColors(IModelDoc2 part, List<DetectedColor> into)
        {
            var ext = part.Extension;
            if (ext == null) return;

            var rms = ext.GetRenderMaterials2((int)swDisplayStateOpts_e.swAllDisplayState, string.Empty) as object[];
            if (rms == null) { ComRelease.Release(ext); return; }

            var seen = new HashSet<int>();
            try
            {
                foreach (var o in rms)
                {
                    var rm = o as RenderMaterial;
                    if (rm == null) continue;
                    try
                    {
                        int primary = rm.PrimaryColor;
                        if (!seen.Add(primary)) continue;

                        int r, g, b;
                        SwColorToRgb(primary, out r, out g, out b);
                        into.Add(new DetectedColor { R = r, G = g, B = b, Name = SafeFileName(rm.FileName) });
                    }
                    finally { ComRelease.Release(rm); }
                }
            }
            finally
            {
                ComRelease.Release(ext);
            }
        }

        private static void GatherColoredFaces(IModelDoc2 part, EdgeColorMapping map,
            List<FaceData> planar, List<FaceData> cylinder, List<FaceData> cone)
        {
            var ext = part.Extension;
            if (ext == null) return;

            var rms = ext.GetRenderMaterials2((int)swDisplayStateOpts_e.swAllDisplayState, string.Empty) as object[];
            if (rms == null) { ComRelease.Release(ext); return; }

            try
            {
                foreach (var o in rms)
                {
                    var rm = o as RenderMaterial;
                    if (rm == null) continue;
                    try
                    {
                        int r, g, b;
                        SwColorToRgb(rm.PrimaryColor, out r, out g, out b);
                        if (r != map.SourceR || g != map.SourceG || b != map.SourceB) continue;

                        int before = planar.Count + cylinder.Count + cone.Count;

                        var ents = rm.GetEntities() as object[];
                        if (ents != null)
                        {
                            foreach (var e in ents) AddEntityFaces(e, planar, cylinder, cone);
                        }

                        // Nothing concrete to attach to => part-level appearance; apply to all faces.
                        if (planar.Count + cylinder.Count + cone.Count == before)
                        {
                            AddAllPartFaces(part, planar, cylinder, cone);
                        }
                    }
                    finally { ComRelease.Release(rm); }
                }
            }
            finally
            {
                ComRelease.Release(ext);
            }
        }

        private static void AddEntityFaces(object oEntity, List<FaceData> planar, List<FaceData> cylinder, List<FaceData> cone)
        {
            var entity = oEntity as Entity;
            if (entity == null) return;

            switch (entity.GetType()) // swSelectType_e (interop method, returns int)
            {
                case (int)swSelectType_e.swSelFACES:
                    AddFace(oEntity as Face2, planar, cylinder, cone);
                    break;

                case (int)swSelectType_e.swSelBODYFEATURES:
                    var feat = oEntity as Feature;
                    var ffaces = feat != null ? feat.GetFaces() as object[] : null;
                    if (ffaces != null) foreach (var f in ffaces) AddFace(f as Face2, planar, cylinder, cone);
                    break;

                // Appearance applied to a whole body — carry it to all of that body's faces.
                case (int)swSelectType_e.swSelSOLIDBODIES:
                case (int)swSelectType_e.swSelSURFACEBODIES:
                    AddBodyFaces(oEntity as Body2, planar, cylinder, cone);
                    break;

                // Appearance applied to a component (assembly context) — use its body.
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

        // Part/document-level appearance lists no specific faces, so the colour covers the whole part.
        // Gather every visible body's faces. Fixes parts coloured at the part level (not per-face),
        // which previously yielded zero edges because GetEntities returned nothing usable.
        private static void AddAllPartFaces(IModelDoc2 part, List<FaceData> planar, List<FaceData> cylinder, List<FaceData> cone)
        {
            var partDoc = part as IPartDoc;
            if (partDoc == null) return;
            var bodies = partDoc.GetBodies2((int)swBodyType_e.swAllBodies, true) as object[];
            if (bodies == null) return;
            foreach (var ob in bodies)
            {
                AddBodyFaces(ob as Body2, planar, cylinder, cone);
                ComRelease.Release(ob);
            }
        }

        // Total faces of a part (all visible bodies). Cheap proxy for how long colouring will take,
        // shown in the pre-run warning so the user knows a 400-face part is far slower than a 30-face one.
        private static int CountPartFaces(IModelDoc2 part)
        {
            var partDoc = part as IPartDoc;
            if (partDoc == null) return 0;
            var bodies = partDoc.GetBodies2((int)swBodyType_e.swAllBodies, true) as object[];
            if (bodies == null) return 0;
            int n = 0;
            foreach (var ob in bodies)
            {
                var body = ob as Body2;
                if (body != null) n += body.GetFaceCount();
                ComRelease.Release(ob);
            }
            return n;
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
                default: ComRelease.Release(face); break; // not a surface we handle
            }
        }

        /// <summary>
        /// Which face list to search for the edge: -1 skip, 0 planar, 1 cylinder, 2 cone.
        /// A planar/planar edge is planar; any cone-adjacent edge is conical; a plane next to a
        /// curved face is planar when the plane is edge-on to the view, else it belongs to the curve.
        /// </summary>
        private static int EdgeColorClass(object oEdge, Vec3 viewNormal)
        {
            var edge = oEdge as Edge;
            if (edge == null) return -1;

            var adj = edge.GetTwoAdjacentFaces2() as object[];
            if (adj == null) return -1;

            var f0 = adj.Length > 0 ? adj[0] as Face2 : null;
            var f1 = adj.Length > 1 ? adj[1] as Face2 : null;
            if (f0 == null && f1 == null) return -1;

            try
            {
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
                return 1; // both curved (cylindrical) — validated later against the grid
            }
            finally
            {
                ComRelease.Release(f0);
                ComRelease.Release(f1);
            }
        }

        // ===================== geometry helpers =====================

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
            try
            {
                if (d == null || d.Length < 9) return new Vec3 { X = 0, Y = 0, Z = 1 };
                return new Vec3 { X = Math.Round(d[2], 6), Y = Math.Round(d[5], 6), Z = Math.Round(d[8], 6) };
            }
            finally { ComRelease.Release(xf); }
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
            try { return surf.Identity(); }
            finally { ComRelease.Release(surf); }
        }

        private static bool SameModel(IView view, string path)
        {
            var refDoc = view.ReferencedDocument as IModelDoc2;
            if (refDoc == null) return false;
            try { return string.Equals(refDoc.GetPathName(), path, StringComparison.OrdinalIgnoreCase); }
            finally { ComRelease.Release(refDoc); }
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
            var key = (Cell(x), Cell(y), Cell(z));
            List<FaceData> faces;
            if (!grid.TryGetValue(key, out faces)) return false;

            foreach (var f in faces)
            {
                var cp = f.Face.GetClosestPointOn(x, y, z) as double[];
                if (cp == null || cp.Length < 3) continue;
                double dx = cp[0] - x, dy = cp[1] - y, dz = cp[2] - z;
                if (dx * dx + dy * dy + dz * dz < OnFaceTolerance * OnFaceTolerance) return true;
            }
            return false;
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
                // Part drawing: no components -> ask for the view's edges directly.
                var ents = view.GetVisibleEntities2(null, (int)swViewEntityType_e.swViewEntityType_Edge) as object[];
                if (ents != null) foreach (var e in ents) if (e != null) yield return e;
                yield break;
            }

            foreach (var oc in comps)
            {
                var comp = oc as Component2;
                var ents = view.GetVisibleEntities2(comp, (int)swViewEntityType_e.swViewEntityType_Edge) as object[];
                if (ents != null) foreach (var e in ents) if (e != null) yield return e;
                ComRelease.Release(comp);
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
            try
            {
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
            finally
            {
                ComRelease.Release(sv);
                ComRelease.Release(ev);
                ComRelease.Release(curve);
            }
        }

        private static bool SilhouetteMidpoint(SilhouetteEdge sil, out double x, out double y, out double z)
        {
            x = y = z = 0;

            var sp = sil.GetStartPoint();
            var ep = sil.GetEndPoint();
            var s = sp != null ? sp.ArrayData as double[] : null;
            var e = ep != null ? ep.ArrayData as double[] : null;
            try
            {
                if (s == null || e == null || s.Length < 3 || e.Length < 3) return false;
                x = Math.Round((s[0] + e[0]) / 2.0, 6);
                y = Math.Round((s[1] + e[1]) / 2.0, 6);
                z = Math.Round((s[2] + e[2]) / 2.0, 6);
                return true;
            }
            finally
            {
                ComRelease.Release(sp);
                ComRelease.Release(ep);
            }
        }

        // Bug fix vs. original: sample up to the number of available edges (the original looped a
        // fixed 0..4 and crashed when a view had fewer than 5 visible edges).
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
                if (corr != null) { mapped++; ComRelease.Release(corr); }
                ComRelease.Release(e);
                if (++sampled >= 5) break;
            }
            ComRelease.Release(ext);

            if (sampled == 0) return true;     // nothing to sample -> the real path simply yields nothing
            return mapped == sampled;          // every sampled edge mapped back to 3D -> real view
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
            ComRelease.Release(xf);
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

        private void ClearActiveSelectionFilters()
        {
            object active = _sw.GetSelectionFilters();
            if (active == null) return;
            _sw.SetSelectionFilters(active, false);
        }

        private static void ReleaseFaces(List<FaceData> faces)
        {
            foreach (var f in faces) ComRelease.Release(f.Face);
            faces.Clear();
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
            return string.IsNullOrEmpty(path) ? string.Empty : Path.GetFileNameWithoutExtension(path);
        }
    }
}
