using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using VeradeAddin.Infrastructure;
using VeradeAddin.Logging;
using VeradeAddin.Models;

namespace VeradeAddin.Services
{
    /// <summary>
    /// "Despiece de calderería": generates the boilermaking part breakdown of the part referenced by
    /// the active drawing, on NEW sheets. Per cut-list item a group of 3 isolated-body views
    /// (front + projected side/top) at one global scale, plus an isometric exploded view (own scale)
    /// with one balloon per item and the cut-list table, paginated across A0 sheets.
    ///
    /// DESTRUCTIVE: when the part is multibody and not yet a weldment, the weldment feature is inserted
    /// directly on the part (no copy, no rollback) and the part + drawing are saved at the end.
    ///
    /// All SolidWorks COM access stays in this partial; every signature was verified by reflecting the
    /// interop DLLs. Best-effort on the explode/flat-pattern/packing details (documented in PIPELINE.md).
    /// </summary>
    public sealed partial class SolidWorksService
    {
        private const string BmName = "Despiece de calderería";

        // A0 landscape and layout budget (metres; sheet origin = lower-left, IView.Position = view centre).
        private const double A0W = 1.189;
        private const double A0H = 0.841;
        private const double BmMargin = 0.02;       // sheet margin
        private const double BmTitleBand = 0.06;    // reserved bottom strip for the title block
        private const double BmViewGut = 0.018;     // gutter between the 3 views of a group
        private const double BmCellGut = 0.030;     // gutter between cells
        private const double BmUsableW = A0W - 2 * BmMargin;
        private const double BmUsableH = A0H - BmMargin - BmTitleBand;

        private static readonly int[] ScaleDens = { 1, 2, 5, 10, 20, 50, 100, 200, 500, 1000 };

        // ===================== public API =====================

        public BoilermakingPlan InspectDrawingForBreakdown()
        {
            var plan = new BoilermakingPlan();

            var model = _sw.IActiveDoc2 as IModelDoc2;
            if (model == null || model.GetType() != (int)swDocumentTypes_e.swDocDRAWING)
            {
                plan.Message = "Debe haber un dibujo activo.";
                return plan;
            }
            plan.IsDrawing = true;

            bool sawAssembly;
            var part = FindReferencedPart(model as IDrawingDoc, out sawAssembly);
            if (part == null)
            {
                plan.Message = sawAssembly
                    ? "El dibujo referencia un ENSAMBLAJE; este comando es solo para piezas."
                    : "El dibujo no referencia ninguna pieza.";
                return plan;
            }

            try
            {
                plan.HasModel = true;
                plan.IsPart = true;
                plan.ModelPath = part.GetPathName();
                plan.ModelTitle = part.GetTitle();

                var partDoc = part as IPartDoc;
                int bodyCount = CountBodies(partDoc);
                plan.IsMultiBody = bodyCount > 1;
                plan.IsWeldment = partDoc != null && partDoc.IsWeldment();

                var names = part.GetConfigurationNames() as string[];
                if (names != null) plan.Configurations.AddRange(names);

                if (bodyCount == 0) plan.Message = "La pieza no tiene cuerpos.";
            }
            finally
            {
                ComRelease.Release(part);
            }

            return plan;
        }

        public BoilermakingResult GenerateBoilermakingBreakdown(string configName, bool allowWeldmentInsertion)
        {
            var result = new BoilermakingResult { ConfigUsed = configName };

            var model = _sw.IActiveDoc2 as IModelDoc2;
            if (model == null || model.GetType() != (int)swDocumentTypes_e.swDocDRAWING)
                return Abort(result, "No hay un dibujo activo.");

            var drawing = model as IDrawingDoc;
            bool sawAssembly;
            var part = FindReferencedPart(drawing, out sawAssembly);
            if (part == null) return Abort(result, "El dibujo no referencia una pieza.");

            var partDoc = part as IPartDoc;
            var mv = model.IActiveView as ModelView;
            bool prevCmd = _sw.CommandInProgress;
            var items = new List<CutItem>();

            try
            {
                Decide("Inicio. Config solicitada='" + (configName ?? "(activa)") + "'.");

                if (!string.IsNullOrEmpty(configName) && !part.ShowConfiguration2(configName))
                    Warn(result, "No se pudo activar la configuración '" + configName + "'.");

                // ---- Weldment / cut list (DESTRUCTIVE) ----
                int bodyCount = CountBodies(partDoc);
                if (bodyCount == 0) return Abort(result, "La pieza no tiene cuerpos.");

                bool isWeldment = partDoc != null && partDoc.IsWeldment();
                if (bodyCount > 1 && !isWeldment)
                {
                    if (!allowWeldmentInsertion)
                        return Abort(result, "La pieza es multicuerpo y no es soldadura: hace falta confirmar la inserción de la soldadura (destructivo).");

                    _log.Log(BmName, "Drawing", LogOutcome.Error,
                        "IRREVERSIBLE: insertando función Soldadura en la pieza (sin copia, sin rollback).");
                    var wf = part.FeatureManager.InsertWeldmentFeature();
                    part.ForceRebuild3(false);
                    result.WeldmentInserted = wf != null;
                    if (wf == null) Warn(result, "No se pudo insertar la función Soldadura.");
                    else Decide("Función Soldadura insertada.");
                }

                // ---- Read cut list ----
                items = ReadCutList(part);
                if (items.Count == 0)
                {
                    var fb = WholePartItem(partDoc);
                    if (fb != null) { items.Add(fb); Warn(result, "Sin lista de corte: se usa la pieza completa como un único elemento."); }
                }
                if (items.Count == 0) return Abort(result, "No se encontró lista de corte ni cuerpos.");
                result.CutListItems = items.Count;
                Decide("Elementos de lista de corte: " + items.Count + ".");

                ComputeBoxes(items);

                // ---- Global scale ----
                int den = ChooseGlobalScale(items);
                result.ScaleNum = 1;
                result.ScaleDen = den;
                double s = 1.0 / den;
                Decide("Escala global de los grupos: 1:" + den + ".");

                // ---- Silence graphics during bulk insertion ----
                if (mv != null) { try { mv.EnableGraphicsUpdate = false; } catch { } }
                _sw.CommandInProgress = true;

                string modelName = ModelName(part);
                string template = FirstSheetTemplate(drawing);

                // ---- Summary sheet: exploded isometric + cut-list table + balloons ----
                if (!NewBreakdownSheet(drawing, template))
                    return Abort(result, "No se pudo crear la hoja nueva.");
                result.SheetsCreated++;
                Decide("Hoja de resumen creada: '" + CurrentSheetName(drawing) + "'.");

                EnsurePartExplode(part, result);
                CreateExplodedSummary(drawing, model, modelName, configName, result);

                // ---- Group sheets (paginated) ----
                LayoutGroups(drawing, model, modelName, configName, items, s, den, result);

                // ---- Save (destructive run) ----
                int e = 0, w = 0;
                part.Save3((int)swSaveAsOptions_e.swSaveAsOptions_Silent, ref e, ref w);
                model.Save3((int)swSaveAsOptions_e.swSaveAsOptions_Silent, ref e, ref w);

                result.Success = true;
                _log.Log(BmName, "Drawing", LogOutcome.Success,
                    "OK: items=" + result.CutListItems + " grupos=" + result.GroupsCreated +
                    " desarrollos=" + result.FlatPatternsCreated + " vistas=" + result.ViewsCreated +
                    " globos=" + result.BalloonsCreated + " hojas=" + result.SheetsCreated +
                    " escala=1:" + result.ScaleDen + " soldadura=" + result.WeldmentInserted);
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
                _log.Log(BmName, "Drawing", LogOutcome.Error, "Excepción no controlada", ex.ToString());
            }
            finally
            {
                if (mv != null) { try { mv.EnableGraphicsUpdate = true; } catch { } }
                _sw.CommandInProgress = prevCmd;
                foreach (var it in items) foreach (var b in it.Bodies) ComRelease.Release(b);
                ComRelease.Release(part);
            }

            return result;
        }

        // ===================== referenced model / cut list =====================

        private static IModelDoc2 FindReferencedPart(IDrawingDoc drawing, out bool sawAssembly)
        {
            sawAssembly = false;
            if (drawing == null) return null;

            var view = drawing.GetFirstView() as IView; // first "view" is the sheet itself
            while (view != null)
            {
                var refDoc = view.ReferencedDocument as IModelDoc2;
                if (refDoc != null)
                {
                    int t = refDoc.GetType();
                    if (t == (int)swDocumentTypes_e.swDocPART) return refDoc; // caller releases
                    if (t == (int)swDocumentTypes_e.swDocASSEMBLY) sawAssembly = true;
                    ComRelease.Release(refDoc);
                }
                view = view.GetNextView() as IView;
            }
            return null;
        }

        private List<CutItem> ReadCutList(IModelDoc2 part)
        {
            var items = new List<CutItem>();
            var feat = part.FirstFeature() as Feature;
            int idx = 0;

            while (feat != null)
            {
                var bf = feat.GetSpecificFeature2() as BodyFolder;
                if (bf != null && IsCutListFolder(bf))
                {
                    // The cut-list container holds one sub-feature per item (group of identical bodies).
                    var sub = feat.GetFirstSubFeature() as Feature;
                    bool any = false;
                    while (sub != null)
                    {
                        var sbf = sub.GetSpecificFeature2() as BodyFolder;
                        if (sbf != null && sbf.GetBodyCount() > 0)
                        {
                            any = true;
                            items.Add(MakeItem(++idx, sub, sbf));
                        }
                        sub = sub.GetNextSubFeature() as Feature;
                    }
                    if (!any && bf.GetBodyCount() > 0) items.Add(MakeItem(++idx, feat, bf));
                }
                feat = feat.GetNextFeature() as Feature;
            }
            return items;
        }

        private static bool IsCutListFolder(BodyFolder bf)
        {
            try
            {
                int t = bf.GetCutListType();
                return t == (int)swCutListType_e.swWeldmentCutlist
                    || t == (int)swCutListType_e.swSheetmetalCutlist
                    || t == (int)swCutListType_e.swSolidBodyCutList;
            }
            catch { return false; }
        }

        private static CutItem MakeItem(int idx, Feature feat, BodyFolder bf)
        {
            var item = new CutItem { Index = idx, Quantity = bf.GetBodyCount() };

            var bodies = bf.GetBodies() as object[];
            if (bodies != null)
            {
                foreach (var ob in bodies)
                {
                    var b = ob as Body2;
                    if (b == null) continue;
                    item.Bodies.Add(b);
                    try { if (b.IsSheetMetal()) item.SheetMetal = true; } catch { }
                }
            }

            string mark = ReadProp(feat, "Mark");
            item.Mark = string.IsNullOrWhiteSpace(mark) ? idx.ToString() : mark.Trim();
            item.Description = ReadProp(feat, "Description");
            return item;
        }

        private static string ReadProp(Feature feat, string name)
        {
            try
            {
                var cpm = feat.CustomPropertyManager;
                if (cpm == null) return null;
                string val, resolved; bool wasResolved;
                cpm.Get5(name, false, out val, out resolved, out wasResolved);
                return string.IsNullOrEmpty(resolved) ? val : resolved;
            }
            catch { return null; }
        }

        private static int CountBodies(IPartDoc partDoc)
        {
            if (partDoc == null) return 0;
            var bodies = partDoc.GetBodies2((int)swBodyType_e.swAllBodies, true) as object[];
            int n = bodies != null ? bodies.Length : 0;
            if (bodies != null) foreach (var ob in bodies) ComRelease.Release(ob);
            return n;
        }

        private static CutItem WholePartItem(IPartDoc partDoc)
        {
            if (partDoc == null) return null;
            var bodies = partDoc.GetBodies2((int)swBodyType_e.swAllBodies, true) as object[];
            if (bodies == null || bodies.Length == 0) return null;

            var item = new CutItem { Index = 1, Mark = "1", Quantity = bodies.Length };
            foreach (var ob in bodies)
            {
                var b = ob as Body2;
                if (b == null) continue;
                item.Bodies.Add(b);
                try { if (b.IsSheetMetal()) item.SheetMetal = true; } catch { }
            }
            return item;
        }

        // ===================== scale & layout =====================

        private static void ComputeBoxes(List<CutItem> items)
        {
            foreach (var it in items)
            {
                double[] u = null;
                foreach (var b in it.Bodies)
                {
                    var box = b.GetBodyBox() as double[];
                    if (box == null || box.Length < 6) continue;
                    if (u == null) { u = (double[])box.Clone(); continue; }
                    for (int k = 0; k < 3; k++) if (box[k] < u[k]) u[k] = box[k];
                    for (int k = 3; k < 6; k++) if (box[k] > u[k]) u[k] = box[k];
                }
                it.Box = u ?? new double[] { 0, 0, 0, 0.1, 0.1, 0.1 };
            }
        }

        private static int ChooseGlobalScale(List<CutItem> items)
        {
            foreach (int d in ScaleDens)
            {
                double s = 1.0 / d;
                bool allFit = true;
                foreach (var it in items)
                {
                    if (GroupW(it, s) > BmUsableW * 0.9 || GroupH(it, s) > BmUsableH * 0.9) { allFit = false; break; }
                }
                if (allFit) return d;
            }
            return ScaleDens[ScaleDens.Length - 1];
        }

        private static double GroupW(CutItem it, double s) { return it.Dx * s + BmViewGut + it.Dy * s; }
        private static double GroupH(CutItem it, double s) { return it.Dz * s + BmViewGut + it.Dy * s; }

        private void LayoutGroups(IDrawingDoc drawing, IModelDoc2 model, string modelName, string config,
            List<CutItem> items, double s, int den, BoilermakingResult result)
        {
            // Uniform cells sized to the largest group → no overlaps; flat panels (≤ a group) fit too.
            double maxGW = 0, maxGH = 0;
            foreach (var it in items)
            {
                maxGW = Math.Max(maxGW, GroupW(it, s));
                maxGH = Math.Max(maxGH, GroupH(it, s));
            }
            double cellW = maxGW + BmCellGut;
            double cellH = maxGH + BmCellGut;
            int cols = Math.Max(1, (int)Math.Floor(BmUsableW / cellW));
            int rows = Math.Max(1, (int)Math.Floor(BmUsableH / cellH));
            int perSheet = cols * rows;

            // Panels: every item gets a 3-view group; sheet-metal items also get a flat pattern panel.
            var panels = new List<Panel>();
            foreach (var it in items)
            {
                panels.Add(new Panel { Item = it, IsFlat = false });
                if (it.SheetMetal) panels.Add(new Panel { Item = it, IsFlat = true });
            }

            string template = FirstSheetTemplate(drawing);
            int placed = 0;
            foreach (var panel in panels)
            {
                int li = placed % perSheet;
                if (li == 0)
                {
                    if (!NewBreakdownSheet(drawing, template)) { Warn(result, "No se pudo crear una hoja adicional; se detiene la paginación."); return; }
                    result.SheetsCreated++;
                    Decide("Hoja de grupos creada: '" + CurrentSheetName(drawing) + "'.");
                }

                int col = li % cols;
                int row = li / cols;
                double cellLeft = BmMargin + col * cellW;
                double cellTop = (A0H - BmMargin) - row * cellH;
                double ccx = cellLeft + cellW / 2.0;
                double ccy = cellTop - cellH / 2.0;
                double labelY = (cellTop - cellH) + 0.010;

                if (panel.IsFlat) CreateFlatPanel(drawing, model, modelName, config, panel.Item, ccx, ccy, labelY, den, result);
                else CreateGroup(drawing, model, modelName, panel.Item, ccx, ccy, labelY, s, den, result);

                placed++;
            }
        }

        private void CreateGroup(IDrawingDoc drawing, IModelDoc2 model, string modelName, CutItem item,
            double ccx, double ccy, double labelY, double s, int den, BoilermakingResult result)
        {
            double fw = item.Dx * s, fh = item.Dz * s, sw = item.Dy * s, th = item.Dy * s;

            // Centre the group bbox in the cell. Side to the right of front, top below front (first angle).
            double cx = (BmViewGut + sw) / 2.0;
            double cy = -(BmViewGut + th) / 2.0;
            double shiftX = ccx - cx, shiftY = ccy - cy;

            double fx = shiftX, fy = shiftY;
            double sx = (fw / 2 + BmViewGut + sw / 2) + shiftX, sy = shiftY;
            double tx = shiftX, ty = -(fh / 2 + BmViewGut + th / 2) + shiftY;

            var arr = ToBodyArray(item.Bodies);

            var front = drawing.CreateDrawViewFromModelView3(modelName, "*Front", fx, fy, 0) as View;
            if (front == null) { Warn(result, "No se pudo crear la vista frontal de la marca " + item.Mark + "."); return; }
            result.ViewsCreated++;
            try { front.ScaleRatio = new double[] { 1, den }; } catch { }
            SetBodies(front, arr, item, result);

            string fn = front.GetName2();

            try
            {
                drawing.ActivateView(fn);
                var side = drawing.CreateUnfoldedViewAt3(sx, sy, 0, false) as View;
                if (side != null) { result.ViewsCreated++; SetBodies(side, arr, item, result); }
            }
            catch (Exception ex) { Warn(result, "Vista lateral marca " + item.Mark + ": " + ex.Message); }

            try
            {
                drawing.ActivateView(fn);
                var top = drawing.CreateUnfoldedViewAt3(tx, ty, 0, false) as View;
                if (top != null) { result.ViewsCreated++; SetBodies(top, arr, item, result); }
            }
            catch (Exception ex) { Warn(result, "Vista superior marca " + item.Mark + ": " + ex.Message); }

            result.GroupsCreated++;
            PlaceLabel(model, ccx, labelY, "Marca " + item.Mark + "   Cant.: " + item.Quantity);
        }

        private void CreateFlatPanel(IDrawingDoc drawing, IModelDoc2 model, string modelName, string config,
            CutItem item, double ccx, double ccy, double labelY, int den, BoilermakingResult result)
        {
            View fp = null;
            try { fp = drawing.CreateFlatPatternViewFromModelView3(modelName, config ?? "", ccx, ccy, 0, false, false) as View; }
            catch (Exception ex) { Warn(result, "Desarrollo marca " + item.Mark + ": " + ex.Message); return; }

            if (fp == null) { Warn(result, "No se pudo crear el desarrollo de la marca " + item.Mark + "."); return; }
            result.ViewsCreated++;
            result.FlatPatternsCreated++;
            try { fp.ScaleRatio = new double[] { 1, den }; } catch { }
            try { fp.Position = new double[] { ccx, ccy }; } catch { }
            SetBodies(fp, ToBodyArray(item.Bodies), item, result); // isolate to this item (best-effort)
            PlaceLabel(model, ccx, labelY, "Marca " + item.Mark + " - Desarrollo");
        }

        // ===================== exploded summary =====================

        private void EnsurePartExplode(IModelDoc2 part, BoilermakingResult result)
        {
            try
            {
                var cfg = part.GetActiveConfiguration() as Configuration;
                if (cfg == null) { Warn(result, "No se pudo obtener la configuración activa para explosionar."); return; }
                if (cfg.GetNumberOfPartExplodeSteps() > 0) { result.Exploded = true; return; }

                var partDoc = part as IPartDoc;
                var bodies = partDoc != null ? partDoc.GetBodies2((int)swBodyType_e.swAllBodies, true) as object[] : null;
                if (bodies == null || bodies.Length < 2) { if (bodies != null) foreach (var o in bodies) ComRelease.Release(o); return; }

                part.ClearSelection2(true);
                foreach (var ob in bodies)
                {
                    var ent = ob as Entity;
                    if (ent != null) { try { ent.Select4(true, null); } catch { } }
                    ComRelease.Release(ob);
                }

                int err;
                var step = cfg.AddPartExplodeStep("Despiece", 0.15, 0, false, true, out err);
                part.ClearSelection2(true);
                part.ForceRebuild3(false);

                result.Exploded = step != null && err == 0;
                if (!result.Exploded) Warn(result, "No se pudo crear la explosión (la vista isométrica irá sin explosionar).");
                else Decide("Explosión de pieza creada (auto-espaciado).");
            }
            catch (Exception ex) { Warn(result, "Explosión: " + ex.Message); }
        }

        private void CreateExplodedSummary(IDrawingDoc drawing, IModelDoc2 model, string modelName, string config,
            BoilermakingResult result)
        {
            double regionCx = BmMargin + BmUsableW * 0.34;
            double regionCy = BmMargin + BmTitleBand + BmUsableH * 0.5;

            var iso = drawing.CreateDrawViewFromModelView3(modelName, "*Isometric", regionCx, regionCy, 0) as View;
            if (iso == null) { Warn(result, "No se pudo crear la vista isométrica explosionada."); return; }
            result.ViewsCreated++;

            try { if (iso.ShowExploded(true)) Decide("Vista isométrica mostrada explosionada."); } catch { }

            int den = FitScaleDen(iso, BmUsableW * 0.60, BmUsableH * 0.80);
            try { iso.ScaleRatio = new double[] { 1, den }; } catch { }
            try { iso.Position = new double[] { regionCx, regionCy }; } catch { }

            // Balloons (one per cut-list item) against the cut-list table, numbered from the table.
            try
            {
                model.ClearSelection2(true);
                model.Extension.SelectByID2(iso.GetName2(), "DRAWINGVIEW", 0, 0, 0, false, 0, null, 0);
                var opts = drawing.CreateAutoBalloonOptions();
                if (opts != null)
                {
                    opts.Layout = (int)swBalloonLayoutType_e.swDetailingBalloonLayout_Circle;
                    opts.Style = (int)swBalloonStyle_e.swBS_Circular;
                    opts.IgnoreMultiple = false;
                    opts.ItemNumberStart = 1;
                    var balloons = drawing.AutoBalloon5(opts) as object[];
                    if (balloons != null) result.BalloonsCreated += balloons.Length;
                }
                model.ClearSelection2(true);
            }
            catch (Exception ex) { Warn(result, "Globos automáticos: " + ex.Message); }

            // Cut-list table, anchored above the title block (bottom-right of the usable area).
            try
            {
                double tableX = BmMargin + BmUsableW;
                double tableY = BmMargin + BmTitleBand;
                iso.InsertWeldmentTable(false, tableX, tableY,
                    (int)swBOMConfigurationAnchorType_e.swBOMConfigurationAnchor_BottomRight, "", config ?? "");
                Decide("Tabla de lista de corte insertada sobre el cajetín.");
            }
            catch (Exception ex) { Warn(result, "Tabla de lista de corte: " + ex.Message); }
        }

        private static int FitScaleDen(View v, double maxW, double maxH)
        {
            double[] ol = null;
            try { ol = v.GetOutline() as double[]; } catch { }
            if (ol == null || ol.Length < 4) return 10;

            double curr = 1;
            try { curr = v.ScaleDecimal; } catch { }
            if (curr <= 0) curr = 1;

            double mw = (ol[2] - ol[0]) / curr;   // model size (strip current view scale)
            double mh = (ol[3] - ol[1]) / curr;
            foreach (int d in ScaleDens) if (mw / d <= maxW && mh / d <= maxH) return d;
            return ScaleDens[ScaleDens.Length - 1];
        }

        // ===================== sheets / notes / helpers =====================

        private bool NewBreakdownSheet(IDrawingDoc drawing, string template)
        {
            // Name "" → SolidWorks assigns the sequential name automatically (not forced manually).
            // FirstAngle = true (European). Custom template uses the first sheet's sheet-format path.
            try
            {
                if (string.IsNullOrEmpty(template))
                {
                    return drawing.NewSheet4("", (int)swDwgPaperSizes_e.swDwgPaperA0size,
                        (int)swDwgTemplates_e.swDwgTemplateA0size, 1.0, 1.0, true, "", A0W, A0H, "", 0, 0, 0, 0, 0, 0);
                }
                return drawing.NewSheet4("", (int)swDwgPaperSizes_e.swDwgPaperA0size,
                    (int)swDwgTemplates_e.swDwgTemplateCustom, 1.0, 1.0, true, template, A0W, A0H, "", 0, 0, 0, 0, 0, 0);
            }
            catch { return false; }
        }

        private string FirstSheetTemplate(IDrawingDoc drawing)
        {
            try
            {
                var names = drawing.GetSheetNames() as string[];
                if (names == null || names.Length == 0) return null;
                drawing.ActivateSheet(names[0]);
                var sheet = drawing.GetCurrentSheet() as Sheet;
                return sheet != null ? sheet.GetTemplateName() : null;
            }
            catch { return null; }
        }

        private static string CurrentSheetName(IDrawingDoc drawing)
        {
            try
            {
                var sheet = drawing.GetCurrentSheet() as Sheet;
                return sheet != null ? sheet.GetName() : "?";
            }
            catch { return "?"; }
        }

        private void PlaceLabel(IModelDoc2 model, double x, double y, string text)
        {
            try
            {
                var note = model.InsertNote(text) as Note;
                if (note != null)
                {
                    note.SetTextPoint(x, y, 0);
                    var ann = note.GetAnnotation() as Annotation;
                    if (ann != null) ann.SetPosition(x, y, 0);
                }
                model.ClearSelection2(true);
            }
            catch { }
        }

        private void SetBodies(View view, object[] bodies, CutItem item, BoilermakingResult result)
        {
            if (view == null || bodies == null || bodies.Length == 0) return;
            try { view.Bodies = bodies; }
            catch (Exception ex) { Warn(result, "Aislar cuerpos de la marca " + item.Mark + ": " + ex.Message); }
        }

        private static object[] ToBodyArray(List<Body2> bodies)
        {
            var arr = new object[bodies.Count];
            for (int i = 0; i < bodies.Count; i++) arr[i] = bodies[i];
            return arr;
        }

        private static string ModelName(IModelDoc2 part)
        {
            string path = part.GetPathName();
            return string.IsNullOrEmpty(path) ? part.GetTitle() : path;
        }

        private BoilermakingResult Abort(BoilermakingResult result, string reason)
        {
            result.Aborted = true;
            result.AbortReason = reason;
            _log.Log(BmName, "Drawing", LogOutcome.Error, "ABORTA: " + reason);
            return result;
        }

        private void Decide(string message)
        {
            _log.Log(BmName, "Drawing", LogOutcome.Success, message);
        }

        private void Warn(BoilermakingResult result, string message)
        {
            result.Warnings.Add(message);
            _log.Log(BmName, "Drawing", LogOutcome.Success, "AVISO: " + message);
        }

        // ===================== private types =====================

        private sealed class CutItem
        {
            public int Index;
            public string Mark;
            public string Description;
            public int Quantity;
            public bool SheetMetal;
            public readonly List<Body2> Bodies = new List<Body2>();
            public double[] Box;

            public double Dx { get { return Box != null ? Box[3] - Box[0] : 0; } }
            public double Dy { get { return Box != null ? Box[4] - Box[1] : 0; } }
            public double Dz { get { return Box != null ? Box[5] - Box[2] : 0; } }
        }

        private struct Panel
        {
            public CutItem Item;
            public bool IsFlat;
        }
    }
}
