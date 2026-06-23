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
    /// This is a refactor of the original EdgeColoring code into the COM-isolated service layer; all
    /// SolidWorks access stays here. Best-effort: it will not colour 100% of edges (the 3D→2D mapping
    /// is inherently partial) — "Limpiar colores" resets them.
    /// </summary>
    public sealed partial class SolidWorksService
    {
        private const double EdgeGridCellSize = 0.01;        // metres
        private const double PerpendicularTolerance = 0.002;
        private const double OnFaceTolerance = 1e-6;

        // ---- coloured-edge tracking (in-memory flag, for a fast + EXACT reset) ----
        // Every edge we colour is recorded straight from the selection list, keyed by the drawing it
        // belongs to, so "Líneas a negro" resets EXACTLY those edges in THAT drawing — hidden/obscured
        // ones included, blocks and untouched geometry never. Keyed by drawing so several open drawings
        // don't cross-contaminate. Presence of an entry == "this drawing is currently coloured" (the flag).
        // Held alive as COM objects for the session; released when the reset consumes them. NOT persisted:
        // after the drawing is reopened the reset falls back to re-deriving the geometry instead.
        private sealed class ColoredEdgeRecord
        {
            public string ViewName;   // drawing view to re-select in (null = silhouettes / no view context)
            public object Edge;        // the coloured drawing entity (kept alive until the reset)
        }

        private readonly Dictionary<string, List<ColoredEdgeRecord>> _coloredByDrawing =
            new Dictionary<string, List<ColoredEdgeRecord>>(StringComparer.OrdinalIgnoreCase);

        // The drawing currently being coloured, so RecordColoredSelection appends to the right bucket.
        private string _activeDrawingKey;

        // Distinct views actually processed by a colouring run (the same view is revisited once per colour
        // mapping; counting raw iterations is what inflated "Vistas procesadas" to e.g. 17 for 7 views).
        private readonly HashSet<string> _processedViews = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Stable per-session key for a drawing: its path, or its title when unsaved.
        private static string DrawingKey(IModelDoc2 model)
        {
            string p = model.GetPathName();
            return string.IsNullOrEmpty(p) ? "(unsaved):" + model.GetTitle() : p;
        }

        private void RecordColoredSelection(ISelectionMgr selMgr, string viewName)
        {
            List<ColoredEdgeRecord> list;
            if (_activeDrawingKey == null || !_coloredByDrawing.TryGetValue(_activeDrawingKey, out list)) return;

            int n = selMgr.GetSelectedObjectCount2(-1);
            for (int i = 1; i <= n; i++)
            {
                var ent = selMgr.GetSelectedObject6(i, -1);
                if (ent != null) list.Add(new ColoredEdgeRecord { ViewName = viewName, Edge = ent });
            }
        }

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

            var drawing = model as IDrawingDoc;
            var sheet = drawing != null ? drawing.GetCurrentSheet() as ISheet : null;
            var views = sheet != null ? sheet.GetViews() as object[] : null;
            if (views == null)
            {
                plan.Message = "El dibujo no tiene vistas.";
                ComRelease.Release(sheet);
                return plan;
            }

            var byPath = new Dictionary<string, EdgeColorPartInfo>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var o in views)
                {
                    var view = o as IView;
                    if (view == null) continue;

                    var refDoc = view.ReferencedDocument as IModelDoc2;
                    if (refDoc == null) continue;
                    try
                    {
                        if (refDoc.GetType() != (int)swDocumentTypes_e.swDocPART) continue;
                        string path = refDoc.GetPathName();
                        if (string.IsNullOrWhiteSpace(path) || byPath.ContainsKey(path)) continue;

                        var info = new EdgeColorPartInfo
                        {
                            PartPath = path,
                            PartName = Path.GetFileNameWithoutExtension(path)
                        };
                        DetectColors(refDoc, info.Colors);
                        info.FaceCount = CountPartFaces(refDoc);
                        byPath[path] = info;
                    }
                    finally
                    {
                        ComRelease.Release(refDoc);
                    }
                }
            }
            finally
            {
                foreach (var o in views) ComRelease.Release(o);
                ComRelease.Release(sheet);
            }

            plan.Parts.AddRange(byPath.Values);
            if (plan.Parts.Count == 0)
            {
                plan.Message = "El dibujo no referencia ninguna pieza.";
            }
            else if (!plan.HasAnyColor)
            {
                plan.Message = "No se detectaron apariencias de color en la(s) pieza(s).";
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

            var selData = selMgr.CreateSelectData();
            _processedViews.Clear();

            // Track what we colour under this drawing's key. Don't clear an existing bucket: a second
            // colouring (e.g. add another colour) should ACCUMULATE so the reset still blacks everything.
            _activeDrawingKey = DrawingKey(model);
            if (!_coloredByDrawing.ContainsKey(_activeDrawingKey))
            {
                _coloredByDrawing[_activeDrawingKey] = new List<ColoredEdgeRecord>();
            }

            try
            {
                foreach (var map in request.Mappings)
                {
                    try
                    {
                        ColorMapping(drawing, model, selMgr, selData, map, result);
                    }
                    catch (Exception ex)
                    {
                        result.Errors.Add("Color (" + map.SourceR + "," + map.SourceG + "," + map.SourceB + "): " + ex.Message);
                    }
                }
                result.ViewsProcessed = _processedViews.Count;
                result.Success = result.Errors.Count == 0;
            }
            finally
            {
                model.ClearSelection2(true);
                ComRelease.Release(selData);
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

            int black = RgbToSwInt(0, 0, 0);
            bool oldApply = _sw.GetApplySelectionFilter();
            object oldFilters = _sw.GetSelectionFilters();
            var selData = selMgr.CreateSelectData();

            // The slow part is NOT the number of edges but the UI/refresh work around them: SelectAll
            // highlights (renders) every selected edge, and SetLineColor then forces a full repaint
            // and tree update. On big assembly drawings that froze for seconds. Going view by view does
            // not help — the edge count is identical. Instead we keep the single bulk pass but disable
            // every refresh mechanism, then do ONE redraw at the end (the standard SW perf recipe):
            //   1) CommandInProgress = true       -> the single biggest lever (suppresses UI churn)
            //   2) SuspendSelectionList()         -> skip the per-edge highlight rendering
            //   3) ModelView.EnableGraphicsUpdate -> defer the repaint to one GraphicsRedraw2 at the end
            //   4) FeatureManager.EnableFeatureTree-> skip design-tree updates
            // All four are restored in finally (CommandInProgress MUST be reset or the UI stays frozen).
            var view = model.ActiveView as ModelView;
            bool hadGraphics = view != null && view.EnableGraphicsUpdate;
            var featMgr = model.FeatureManager;
            bool hadFeatureTree = featMgr != null && featMgr.EnableFeatureTree;
            bool suspended = false;
            bool commandInProgress = false;

            try
            {
                _sw.CommandInProgress = true;
                commandInProgress = true;
                if (view != null) view.EnableGraphicsUpdate = false;
                if (featMgr != null) featMgr.EnableFeatureTree = false;
                selMgr.SuspendSelectionList();
                suspended = true;

                string key = DrawingKey(model);
                List<ColoredEdgeRecord> tracked;
                bool hasTracking = _coloredByDrawing.TryGetValue(key, out tracked) && tracked.Count > 0;

                if (hasTracking)
                {
                    // Fast + exact path (coloured this session): reset ONLY the edges we recorded for THIS
                    // drawing. Includes hidden/obscured edges; never touches blocks or untouched geometry.
                    result.EdgesColored = ResetTrackedEdges(drawing, model, selMgr, selData, black, tracked);
                    _coloredByDrawing.Remove(key); // consumed -> flag back to "not coloured"
                }
                else
                {
                    // Fallback (no tracking — e.g. the drawing was reopened): re-derive the geometry and
                    // blacken every model edge per part view. Slower, but reaches hidden/obscured edges
                    // too (which SelectAll cannot). Accepted trade-off for the cross-session case.
                    result.EdgesColored = ReanalyzeAndBlacken(drawing, model, selMgr, selData, black);
                }

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
                if (view != null)
                {
                    view.EnableGraphicsUpdate = hadGraphics;
                    ComRelease.Release(view);
                }
                // MUST run even on exception: leaving CommandInProgress = true freezes the UI until
                // SOLIDWORKS is restarted.
                if (commandInProgress) _sw.CommandInProgress = false;
                model.GraphicsRedraw2();
                ComRelease.Release(selData);
                ComRelease.Release(selMgr);
            }

            return result;
        }

        // Reset exactly the edges recorded for this drawing's colouring. Each was captured straight from
        // the selection list (so hidden/obscured edges are included), tagged with its view. We re-select
        // them all — restoring each one's view context — and issue ONE SetLineColor(black). The records'
        // COM objects are released here (the caller removes the bucket).
        private int ResetTrackedEdges(IDrawingDoc drawing, IModelDoc2 drawingModel,
            ISelectionMgr selMgr, SelectData selData, int black, List<ColoredEdgeRecord> tracked)
        {
            int reset = 0;

            // Resolve the current views by name so a recorded edge can be re-selected in its view context
            // (the View COM objects captured at colour time are long gone — names are stable).
            var sheet = drawing.GetCurrentSheet() as ISheet;
            var views = sheet != null ? sheet.GetViews() as object[] : null;
            var viewByName = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            if (views != null)
            {
                foreach (var o in views)
                {
                    var v = o as IView;
                    if (v == null) continue;
                    string nm = v.GetName2();
                    if (!string.IsNullOrEmpty(nm) && !viewByName.ContainsKey(nm)) viewByName[nm] = o;
                }
            }

            try
            {
                drawingModel.ClearSelection2(true);
                foreach (var rec in tracked)
                {
                    object viewObj = null;
                    if (rec.ViewName != null) viewByName.TryGetValue(rec.ViewName, out viewObj);
                    try { selData.View = viewObj as View; } catch { }
                    try { selMgr.AddSelectionListObject(rec.Edge, selData); } catch { }
                }

                // AddSelectionListObject reports false for view-resolved edges even when added, so colour
                // off the real selection count (same caveat as ColorMapping).
                if (selMgr.GetSelectedObjectCount2(-1) > 0)
                {
                    drawing.SetLineColor(black);
                    reset = selMgr.GetSelectedObjectCount2(-1);
                }

                drawingModel.ClearSelection2(true);
                try { selData.View = null; } catch { }
            }
            finally
            {
                foreach (var rec in tracked) ComRelease.Release(rec.Edge);
                tracked.Clear();
                if (views != null) foreach (var o in views) ComRelease.Release(o);
                ComRelease.Release(sheet);
            }

            return reset;
        }

        // Fallback reset for a reopened drawing (nothing tracked this session). Re-derives the geometry:
        // for every view that references a PART, every model edge is mapped to its 2D view entity via
        // IView.GetCorrespondingEntity (visibility-independent, so hidden/obscured edges are reached too)
        // and blackened. Slower than the tracked path, but correct across sessions.
        private int ReanalyzeAndBlacken(IDrawingDoc drawing, IModelDoc2 drawingModel,
            ISelectionMgr selMgr, SelectData selData, int black)
        {
            int reset = 0;
            var sheet = drawing.GetCurrentSheet() as ISheet;
            var views = sheet != null ? sheet.GetViews() as object[] : null;
            if (views == null) { ComRelease.Release(sheet); return 0; }

            try
            {
                foreach (var o in views)
                {
                    var view = o as IView;
                    if (view == null) continue;

                    var refDoc = view.ReferencedDocument as IModelDoc2;
                    if (refDoc == null) continue;
                    try
                    {
                        if (refDoc.GetType() != (int)swDocumentTypes_e.swDocPART) continue;
                        var partDoc = refDoc as IPartDoc;
                        if (partDoc == null) continue;

                        drawingModel.ClearSelection2(true);
                        var viewT = o as View;
                        if (viewT != null) { try { selData.View = viewT; } catch { } }

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

                        int c = selMgr.GetSelectedObjectCount2(-1);
                        if (c > 0)
                        {
                            drawing.SetLineColor(black);
                            reset += c;
                        }
                        drawingModel.ClearSelection2(true);
                    }
                    finally
                    {
                        ComRelease.Release(refDoc);
                    }
                }

                try { selData.View = null; } catch { }
            }
            finally
            {
                foreach (var o in views) ComRelease.Release(o);
                ComRelease.Release(sheet);
            }

            return reset;
        }

        // ===================== per-mapping core =====================

        private void ColorMapping(IDrawingDoc drawing, IModelDoc2 drawingModel, ISelectionMgr selMgr,
            SelectData selData, EdgeColorMapping map, EdgeColoringResult result)
        {
            int targetSw = RgbToSwInt(map.TargetR, map.TargetG, map.TargetB);

            var sheet = drawing.GetCurrentSheet() as ISheet;
            var views = sheet != null ? sheet.GetViews() as object[] : null;
            if (views == null) { ComRelease.Release(sheet); return; }

            IModelDoc2 partModel = FindReferencedModel(views, map.PartPath);
            if (partModel == null)
            {
                result.Errors.Add("No se encontró la pieza referenciada: " + Path.GetFileName(map.PartPath ?? ""));
                foreach (var o in views) ComRelease.Release(o);
                ComRelease.Release(sheet);
                return;
            }

            var planar = new List<FaceData>();
            var cylinder = new List<FaceData>();
            var cone = new List<FaceData>();
            GatherColoredFaces(partModel, map, planar, cylinder, cone);

            var planarGrid = BuildGrid(planar);
            var cylinderGrid = BuildGrid(cylinder);
            var coneGrid = BuildGrid(cone);

            try
            {
                foreach (var o in views)
                {
                    var view = o as IView;
                    if (view == null) continue;
                    if (!SameModel(view, map.PartPath)) continue;

                    Vec3 viewNormal = ViewNormal(view);
                    var planarPerp = planar.Where(f => ArePerpendicular(viewNormal, f.Normal)).ToList();

                    bool real = IsRealView(view, partModel);

                    // Selecting an entity inside a drawing view needs the view context, otherwise
                    // AddSelectionListObject can't resolve a raw model edge to the view and fails
                    // (this is why section views ended up with 0 coloured edges).
                    drawingModel.ClearSelection2(true);
                    var viewT = o as View;
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

                    // Colour whatever actually ended up selected (do NOT gate on the
                    // AddSelectionListObject return value — it reports false for view-resolved
                    // model edges even when they are added).
                    int coloredInView = selMgr.GetSelectedObjectCount2(-1);
                    if (coloredInView > 0)
                    {
                        drawing.SetLineColor(targetSw);
                        result.EdgesColored += coloredInView;
                        // Record EXACTLY what got coloured (incl. hidden edges) so the reset is fast + exact.
                        RecordColoredSelection(selMgr, view.GetName2());
                    }

                    drawingModel.ClearSelection2(true);
                    _processedViews.Add(view.GetName2());
                }

                // Silhouette edges (cylinder outlines) are not returned by the loops above.
                try { selData.View = null; } catch { }
                result.EdgesColored += ColorSilhouettes(drawing, drawingModel, selMgr, selData, cylinderGrid, coneGrid, targetSw);
            }
            finally
            {
                ReleaseFaces(planar);
                ReleaseFaces(cylinder);
                ReleaseFaces(cone);
                ComRelease.Release(partModel);
                foreach (var o in views) ComRelease.Release(o);
                ComRelease.Release(sheet);
            }
        }

        private int ColorSilhouettes(IDrawingDoc drawing, IModelDoc2 drawingModel, ISelectionMgr selMgr,
            SelectData selData, Dictionary<(int, int, int), List<FaceData>> cylinderGrid,
            Dictionary<(int, int, int), List<FaceData>> coneGrid, int targetSw)
        {
            int colored = 0;
            bool oldApply = _sw.GetApplySelectionFilter();
            object oldFilters = _sw.GetSelectionFilters();
            bool suspended = false;
            var silhouettes = new List<object>();

            try
            {
                selMgr.SuspendSelectionList();
                suspended = true;

                ClearActiveSelectionFilters();
                _sw.SetSelectionFilter((int)swSelectType_e.swSelSILHOUETTES, true);
                _sw.SetApplySelectionFilter(true);

                drawingModel.Extension.SelectAll();

                int count = selMgr.GetSelectedObjectCount2(-1);
                for (int i = 1; i <= count; i++)
                {
                    if (selMgr.GetSelectedObjectType3(i, -1) != (int)swSelectType_e.swSelSILHOUETTES) continue;
                    var sil = selMgr.GetSelectedObject6(i, -1) as SilhouetteEdge;
                    if (sil != null) silhouettes.Add(sil);
                }

                drawingModel.ClearSelection2(true);

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
                    RecordColoredSelection(selMgr, null);
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
                foreach (var os in silhouettes) ComRelease.Release(os);
            }

            return colored;
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

        private static IModelDoc2 FindReferencedModel(object[] views, string path)
        {
            foreach (var o in views)
            {
                var view = o as IView;
                if (view == null) continue;

                var refDoc = view.ReferencedDocument as IModelDoc2;
                if (refDoc == null) continue;
                if (string.Equals(refDoc.GetPathName(), path, StringComparison.OrdinalIgnoreCase)) return refDoc;
                ComRelease.Release(refDoc);
            }
            return null;
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
