using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using VeradeAddin.Infrastructure;
using VeradeAddin.Logging;
using VeradeAddin.Models;

namespace VeradeAddin.Services
{
    /// <summary>
    /// Concrete SolidWorks access. The ONLY class (besides registration glue) that
    /// touches SolidWorks.Interop COM types. Everything it returns is a plain model.
    ///
    /// API verified against the SolidWorks API Help:
    ///   ISldWorks.IActiveDoc2, IModelDoc2.GetType/GetTitle/GetPathName,
    ///   IModelDoc2.ISelectionManager, ISelectionMgr.GetSelectedObjectCount2/
    ///   GetSelectedObjectType3/GetSelectedObjectsComponent4,
    ///   IComponent2.GetParent/Name2/GetPathName/IsVirtual/GetSuppression2,
    ///   IDrawingDoc.GetCurrentSheet/InsertModelAnnotations3, ISheet.GetViews,
    ///   IView.Name/ReferencedDocument.
    /// </summary>
    public sealed partial class SolidWorksService : ISolidWorksService
    {
        private readonly ISldWorks _sw;
        private readonly ILogger _log;

        public SolidWorksService(ISldWorks sw, ILogger log)
        {
            _sw = sw;
            _log = log;
        }

        public ActiveDocument GetActiveDocument()
        {
            var model = _sw.IActiveDoc2 as IModelDoc2;
            if (model == null)
            {
                return null;
            }

            return new ActiveDocument
            {
                Kind = MapKind(model.GetType()),
                Title = model.GetTitle(),
                FilePath = model.GetPathName() // empty string when never saved
            };
        }

        public ComponentNode GetSelectedComponentHierarchy()
        {
            var model = _sw.IActiveDoc2 as IModelDoc2;
            if (model == null || model.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
            {
                return null;
            }

            var selMgr = model.SelectionManager as ISelectionMgr;
            if (selMgr == null)
            {
                return null;
            }

            // COM objects we touch are released in the finally; the active doc (model) is not.
            var chain = new List<Component2>();
            try
            {
                int count = selMgr.GetSelectedObjectCount2(-1);
                if (count <= 0)
                {
                    _log.Log("OpenFolder", "Assembly", LogOutcome.Cancel, "No selection in assembly");
                    return null;
                }

                // Resolve the owning component of each selection regardless of the selected
                // entity type. GetSelectedObjectsComponent4 returns the component a face/edge/
                // vertex/component belongs to, so selecting in the graphics area works too.
                Component2 selected = null;
                for (int i = 1; i <= count; i++)
                {
                    var comp = selMgr.GetSelectedObjectsComponent4(i, -1) as Component2;
                    if (comp != null)
                    {
                        selected = comp;
                        break;
                    }
                }

                if (selected == null)
                {
                    _log.Log("OpenFolder", "Assembly", LogOutcome.Cancel,
                        "Selection (" + count + " item(s)) has no owning component; opening assembly folder");
                    return null;
                }

                // Climb from the selected component up to the top-level component.
                var cursor = selected;
                while (cursor != null)
                {
                    chain.Add(cursor);
                    cursor = cursor.GetParent() as Component2;
                }
                chain.Reverse(); // now ordered top-level -> ... -> selected

                // Root node = the assembly document itself.
                var root = new ComponentNode
                {
                    Name = model.GetTitle(),
                    FilePath = model.GetPathName(),
                    IsVirtual = false,
                    FileExists = FileExistsSafe(model.GetPathName())
                };

                var parentNode = root;
                foreach (var comp in chain)
                {
                    var node = MapComponent(comp);
                    node.IsSelectedTarget = ReferenceEquals(comp, selected);
                    parentNode.Children.Add(node);
                    parentNode = node;
                }

                return root;
            }
            finally
            {
                foreach (var comp in chain)
                {
                    ComRelease.Release(comp);
                }
                ComRelease.Release(selMgr);
            }
        }

        private static ComponentNode MapComponent(Component2 comp)
        {
            string path = comp.GetPathName();
            int suppression = comp.GetSuppression2();

            return new ComponentNode
            {
                Name = comp.Name2,
                FilePath = path,
                IsVirtual = comp.IsVirtual,
                IsSuppressed = suppression == (int)swComponentSuppressionState_e.swComponentSuppressed,
                IsLightweight = suppression == (int)swComponentSuppressionState_e.swComponentLightweight
                                || suppression == (int)swComponentSuppressionState_e.swComponentFullyLightweight,
                FileExists = FileExistsSafe(path)
            };
        }

        public bool ActiveDrawingReferencesAssembly()
        {
            var model = _sw.IActiveDoc2 as IModelDoc2;
            if (model == null || model.GetType() != (int)swDocumentTypes_e.swDocDRAWING)
            {
                return false;
            }

            var drawing = model as IDrawingDoc;
            var sheet = drawing?.GetCurrentSheet() as ISheet;
            if (sheet == null)
            {
                return false;
            }

            var views = sheet.GetViews() as object[];
            if (views == null)
            {
                return false;
            }

            try
            {
                foreach (var obj in views)
                {
                    if (IsRealAssemblyView(obj as IView))
                    {
                        return true;
                    }
                }
                return false;
            }
            finally
            {
                foreach (var obj in views) ComRelease.Release(obj);
                ComRelease.Release(sheet);
            }
        }

        public CosmeticThreadResult InsertCosmeticThreadsInAssemblyViews()
        {
            var result = new CosmeticThreadResult();

            var model = _sw.IActiveDoc2 as IModelDoc2;
            if (model == null || model.GetType() != (int)swDocumentTypes_e.swDocDRAWING)
            {
                return result; // WasDrawing stays false
            }
            result.WasDrawing = true;

            var drawing = model as IDrawingDoc;
            var sheet = drawing?.GetCurrentSheet() as ISheet;
            if (sheet == null)
            {
                result.Errors.Add("No hay hoja activa en el dibujo.");
                return result;
            }

            // Count the real assembly views on the active sheet (for the report and to skip when
            // there is nothing to annotate). Template/orientation views ("*Front", ...) excluded.
            var views = sheet.GetViews() as object[];
            if (views != null)
            {
                try
                {
                    foreach (var obj in views)
                    {
                        if (IsRealAssemblyView(obj as IView))
                        {
                            result.AssemblyViewsFound++;
                        }
                    }
                }
                finally
                {
                    foreach (var obj in views) ComRelease.Release(obj);
                    ComRelease.Release(sheet);
                }
            }

            if (result.AssemblyViewsFound == 0)
            {
                return result; // no assembly views -> nothing to do
            }

            // A single call inserts cosmetic threads from the entire model into ALL views
            // (AllViews = true), so no per-view selection/loop is needed. Parameter values
            // verified working on SW 2026: Source = whole model, Types = cosmetic threads.
            try
            {
                 var ob = drawing.InsertModelAnnotations3(
                    (int)swImportModelItemsSource_e.swImportModelItemsFromEntireModel,
                    (int)swInsertAnnotation_e.swInsertCThreads,
                    true, false, false, false);

                bool ok = ob != null;

                if (ok)
                {
                    result.Processed = result.AssemblyViewsFound;
                }
                else
                {
                    result.Failed = result.AssemblyViewsFound;
                    result.Errors.Add("InsertModelAnnotations3 devolvió false.");
                }
            }
            catch (Exception ex)
            {
                result.Failed = result.AssemblyViewsFound;
                result.Errors.Add(ex.Message);
            }

            return result;
        }

        /// <summary>
        /// True only for a real placed drawing view that references an assembly. Excludes the
        /// standard orientation/template views ("*Front", "*Top", "*Isometric", ...) that
        /// <c>GetViews</c> also returns and which cannot be annotated.
        /// </summary>
        private static bool IsRealAssemblyView(IView view)
        {
            if (view == null) return false;
            string name = view.Name;
            if (string.IsNullOrEmpty(name) || name.StartsWith("*")) return false;
            return view.ReferencedDocument is IAssemblyDoc;
        }

        public bool IsComponentSelectedInActiveAssembly()
        {
            var model = _sw.IActiveDoc2 as IModelDoc2;
            if (model == null || model.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
            {
                return false;
            }
            return ResolveSelectedComponent(model) != null;
        }

        public ComponentExtractionPlan InspectSelectedComponentForExtraction()
        {
            var model = _sw.IActiveDoc2 as IModelDoc2;
            if (model == null || model.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
            {
                return null;
            }

            var comp = ResolveSelectedComponent(model);
            if (comp == null)
            {
                return null;
            }

            var mates = comp.GetMates() as object[];
            return new ComponentExtractionPlan
            {
                ComponentName = comp.Name2,
                IsFixed = comp.IsFixed(),
                MateCount = mates?.Length ?? 0,
                IsPatternInstance = comp.IsPatternInstance() || comp.IsMirrored()
            };
        }

        public ExtractComponentResult ExtractSelectedComponent()
        {
            var result = new ExtractComponentResult();

            var model = _sw.IActiveDoc2 as IModelDoc2;
            if (model == null || model.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
            {
                result.Error = "No hay un ensamblaje activo.";
                return result;
            }

            var asm = model as IAssemblyDoc;
            var comp = ResolveSelectedComponent(model);
            if (asm == null || comp == null)
            {
                result.Error = "Seleccione un componente del ensamblaje.";
                return result;
            }

            result.ComponentName = comp.Name2;

            // Safety net: never move a pattern/mirror instance — it would break the pattern.
            if (comp.IsPatternInstance() || comp.IsMirrored())
            {
                result.Error = "El componente es una instancia de matriz o simetría.";
                return result;
            }

            try
            {
                // 1) Remove the Fixed state (UnfixComponent acts on the current selection).
                if (comp.IsFixed())
                {
                    model.ClearSelection2(true);
                    comp.Select4(false, null, false);
                    asm.UnfixComponent();
                    model.ClearSelection2(true);
                    result.WasFixed = true;
                }

                // 2) Suppress the position mates that reference this component.
                result.MatesSuppressed = SuppressComponentMates(model, comp.Name2);

                // 3) Move the component past the assembly bounding box (+X).
                result.Moved = MoveBeyondAssemblyBox(asm, comp);

                // 4) Zoom to the extracted component only (leave it selected so the user sees it).
                model.ClearSelection2(true);
                comp.Select4(false, null, false);
                model.ViewZoomToSelection();

                result.Success = true;
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
            }

            return result;
        }

        public IntPtr GetActiveModelViewHandle()
        {
            var model = _sw.IActiveDoc2 as IModelDoc2;
            if (model == null)
            {
                return IntPtr.Zero;
            }

            // IActiveView -> ModelView; GetViewHWndx64 is the graphics-area (model space) window.
            var view = model.IActiveView;
            if (view == null)
            {
                return IntPtr.Zero;
            }

            long hwnd = view.GetViewHWndx64();
            return new IntPtr(hwnd);
        }

        public int GetActiveFeatureManagerWidth()
        {
            var model = _sw.IActiveDoc2 as IModelDoc2;
            if (model == null)
            {
                return 0;
            }

            try
            {
                int width = model.GetFeatureManagerWidth();
                return width > 0 ? width : 0;
            }
            catch
            {
                return 0;
            }
        }

        // ---- Exportar (PDF / DWG / STEP) ------------------------------------------------

        /// <summary>Assemblies above this component count are blocked from STEP export (crash risk).</summary>
        private const int MaxStepAssemblyComponents = 10;

        public DrawingExportInfo InspectActiveDrawingForExport()
        {
            var info = new DrawingExportInfo();

            var drawing = _sw.IActiveDoc2 as IModelDoc2;
            if (drawing == null || drawing.GetType() != (int)swDocumentTypes_e.swDocDRAWING)
            {
                return info; // IsDrawing = false
            }

            info.IsDrawing = true;
            info.DrawingPath = drawing.GetPathName();
            info.DrawingTitle = drawing.GetTitle();

            string modelPath;
            var model = ResolveReferencedModel(drawing, out modelPath);
            info.ModelPath = !string.IsNullOrEmpty(modelPath)
                ? modelPath
                : (model != null ? model.GetPathName() : null);

            if (model == null && string.IsNullOrEmpty(info.ModelPath))
            {
                info.StepAllowed = false;
                info.StepBlockedReason = "El dibujo no referencia ningún modelo.";
                return info;
            }

            info.HasModel = true;

            int kind = model != null ? model.GetType() : ModelTypeFromExt(info.ModelPath);
            info.ModelKind = MapKind(kind);

            if (kind == (int)swDocumentTypes_e.swDocASSEMBLY)
            {
                var asm = model as IAssemblyDoc;
                int count = asm != null ? asm.GetComponentCount(false) : -1;
                info.ComponentCount = count;

                if (count < 0)
                {
                    info.StepAllowed = false;
                    info.StepBlockedReason = "No se pudo contar los componentes del ensamblaje (no está cargado).";
                }
                else if (count > MaxStepAssemblyComponents)
                {
                    info.StepAllowed = false;
                    info.StepBlockedReason =
                        "El ensamblaje tiene " + count + " componentes (más de " + MaxStepAssemblyComponents +
                        "). STEP no disponible por seguridad.";
                }
                else
                {
                    info.StepAllowed = true;
                }
            }
            else
            {
                info.StepAllowed = true; // a part is always fine
            }

            return info;
        }

        public ExportResult ExportActiveDrawing(ExportRequest request)
        {
            var result = new ExportResult();
            if (request == null)
            {
                return result;
            }

            var drawing = _sw.IActiveDoc2 as IModelDoc2;
            if (drawing == null || drawing.GetType() != (int)swDocumentTypes_e.swDocDRAWING)
            {
                result.Items.Add(Fail("General", "No hay un dibujo activo."));
                return result;
            }

            string drawingPath = drawing.GetPathName();
            if (string.IsNullOrEmpty(drawingPath))
            {
                result.Items.Add(Fail("General", "El dibujo no está guardado."));
                return result;
            }

            string dir = Path.GetDirectoryName(drawingPath);
            string outBase = ApplyPrefixSuffix(Path.GetFileNameWithoutExtension(drawingPath), request.Prefix, request.Suffix);

            // PDF / DWG come from the active drawing.
            if (request.Pdf)
            {
                result.Items.Add(SaveActiveAs(drawing, Path.Combine(dir, outBase + ".pdf"), "PDF"));
            }
            if (request.Dwg)
            {
                result.Items.Add(SaveActiveAs(drawing, Path.Combine(dir, outBase + ".dwg"), "DWG"));
            }

            // STEP comes from the referenced model (which must be the active document).
            if (request.Step)
            {
                result.Items.Add(ExportReferencedModelAsStep(drawing, request));
            }

            return result;
        }

        private ExportItem ExportReferencedModelAsStep(IModelDoc2 drawing, ExportRequest request)
        {
            const string fmt = "STEP";

            string modelPath;
            var model = ResolveReferencedModel(drawing, out modelPath);
            bool weOpened = false;

            try
            {
                if (model == null)
                {
                    if (string.IsNullOrEmpty(modelPath) || !FileExistsSafe(modelPath))
                    {
                        return Fail(fmt, "No se encontró el modelo referenciado por el dibujo.");
                    }

                    int oerr = 0, owarn = 0;
                    model = _sw.OpenDoc6(modelPath, ModelTypeFromExt(modelPath),
                        (int)swOpenDocOptions_e.swOpenDocOptions_Silent, "", ref oerr, ref owarn) as IModelDoc2;
                    if (model == null)
                    {
                        return Fail(fmt, "No se pudo abrir el modelo (errors=" + oerr + ").");
                    }
                    weOpened = true;
                }

                // Safety net: never STEP a big assembly.
                if (model.GetType() == (int)swDocumentTypes_e.swDocASSEMBLY)
                {
                    var asm = model as IAssemblyDoc;
                    int count = asm != null ? asm.GetComponentCount(false) : -1;
                    if (count < 0 || count > MaxStepAssemblyComponents)
                    {
                        return Fail(fmt, "El ensamblaje tiene demasiados componentes (" + count +
                            ") para exportar a STEP con seguridad.");
                    }
                }

                string mPath = model.GetPathName();
                if (string.IsNullOrEmpty(mPath))
                {
                    return Fail(fmt, "El modelo no está guardado.");
                }

                // All exports go in the drawing's folder; the STEP keeps the model's own name.
                string drawingDir = Path.GetDirectoryName(drawing.GetPathName());
                string outPath = Path.Combine(
                    drawingDir,
                    ApplyPrefixSuffix(Path.GetFileNameWithoutExtension(mPath), request.Prefix, request.Suffix) + ".step");

                // SolidWorks requires the document to be active to export it to STEP.
                string drawingTitle = drawing.GetTitle();
                string modelTitle = model.GetTitle();
                int aerr = 0;
                _sw.ActivateDoc3(modelTitle, false, (int)swRebuildOnActivation_e.swDontRebuildActiveDoc, ref aerr);

                var item = SaveActiveAs(model, outPath, fmt);

                // Restore the drawing as the active document.
                int derr = 0;
                _sw.ActivateDoc3(drawingTitle, false, (int)swRebuildOnActivation_e.swDontRebuildActiveDoc, ref derr);

                return item;
            }
            catch (Exception ex)
            {
                return Fail(fmt, ex.Message);
            }
            finally
            {
                // Only close what we opened ourselves; leave pre-loaded references alone.
                if (weOpened && model != null)
                {
                    try { _sw.CloseDoc(model.GetTitle()); } catch { }
                }
            }
        }

        private static ExportItem SaveActiveAs(IModelDoc2 doc, string outPath, string fmt)
        {
            try
            {
                int err = 0, warn = 0;
                bool ok = doc.Extension.SaveAs3(
                    outPath,
                    (int)swSaveAsVersion_e.swSaveAsCurrentVersion,
                    (int)swSaveAsOptions_e.swSaveAsOptions_Silent,
                    null, null, ref err, ref warn);

                return new ExportItem
                {
                    Format = fmt,
                    Success = ok,
                    Path = ok ? outPath : null,
                    Error = ok ? null : ("SolidWorks no pudo guardar (errors=" + err + ", warnings=" + warn + ").")
                };
            }
            catch (Exception ex)
            {
                return Fail(fmt, ex.Message);
            }
        }

        private IModelDoc2 ResolveReferencedModel(IModelDoc2 drawing, out string modelPath)
        {
            modelPath = null;

            var dwg = drawing as IDrawingDoc;
            if (dwg == null)
            {
                return null;
            }

            // GetFirstView returns the sheet view; subsequent views (across all sheets) hold the model.
            var view = dwg.GetFirstView() as IView;
            while (view != null)
            {
                var refDoc = view.ReferencedDocument as IModelDoc2;
                if (refDoc != null)
                {
                    modelPath = refDoc.GetPathName();
                    return refDoc;
                }

                if (modelPath == null)
                {
                    try
                    {
                        string name = view.GetReferencedModelName();
                        if (!string.IsNullOrEmpty(name)) modelPath = name;
                    }
                    catch { }
                }

                view = view.GetNextView() as IView;
            }

            return null;
        }

        private static string ApplyPrefixSuffix(string baseName, string prefix, string suffix)
        {
            return SanitizeAffix(prefix) + baseName + SanitizeAffix(suffix);
        }

        private static string SanitizeAffix(string affix)
        {
            if (string.IsNullOrEmpty(affix))
            {
                return string.Empty;
            }
            foreach (var ch in Path.GetInvalidFileNameChars())
            {
                affix = affix.Replace(ch.ToString(), string.Empty);
            }
            return affix;
        }

        private static int ModelTypeFromExt(string path)
        {
            string ext = (Path.GetExtension(path) ?? string.Empty).ToLowerInvariant();
            if (ext == ".sldasm") return (int)swDocumentTypes_e.swDocASSEMBLY;
            if (ext == ".slddrw") return (int)swDocumentTypes_e.swDocDRAWING;
            return (int)swDocumentTypes_e.swDocPART;
        }

        private static ExportItem Fail(string fmt, string error)
        {
            return new ExportItem { Format = fmt, Success = false, Error = error };
        }

        private static Component2 ResolveSelectedComponent(IModelDoc2 model)
        {
            var selMgr = model.SelectionManager as ISelectionMgr;
            if (selMgr == null)
            {
                return null;
            }

            int count = selMgr.GetSelectedObjectCount2(-1);
            for (int i = 1; i <= count; i++)
            {
                var comp = selMgr.GetSelectedObjectsComponent4(i, -1) as Component2;
                if (comp != null)
                {
                    return comp;
                }
            }
            return null;
        }

        private int SuppressComponentMates(IModelDoc2 model, string componentName)
        {
            int suppressed = 0;
            var feature = model.FirstFeature() as Feature;
            while (feature != null)
            {
                suppressed += SuppressIfComponentMate(feature, componentName);

                // Mates usually live as sub-features of the "MateGroup" feature.
                var sub = feature.GetFirstSubFeature() as Feature;
                while (sub != null)
                {
                    suppressed += SuppressIfComponentMate(sub, componentName);
                    sub = sub.GetNextSubFeature() as Feature;
                }

                feature = feature.GetNextFeature() as Feature;
            }
            return suppressed;
        }

        private static int SuppressIfComponentMate(Feature feature, string componentName)
        {
            var mate = feature.GetSpecificFeature2() as IMate2;
            if (mate == null)
            {
                return 0;
            }

            int entityCount = mate.GetMateEntityCount();
            for (int i = 0; i < entityCount; i++)
            {
                var entity = mate.MateEntity(i);
                var refComp = entity != null ? entity.ReferenceComponent : null;
                if (refComp != null &&
                    string.Equals(refComp.Name2, componentName, StringComparison.OrdinalIgnoreCase))
                {
                    // swSuppressFeature = 0, swThisConfiguration = 1
                    feature.SetSuppression2(0, 1, null);
                    return 1;
                }
            }
            return 0;
        }

        private bool MoveBeyondAssemblyBox(IAssemblyDoc asm, Component2 comp)
        {
            var asmBox = UnionTopLevelBoxes(asm);
            var compBox = comp.GetBox(false, false) as double[];
            if (asmBox == null || compBox == null || compBox.Length < 6)
            {
                return false;
            }

            double assemblyWidth = asmBox[3] - asmBox[0];        // xMax - xMin
            double gap = Math.Max(assemblyWidth * 0.2, 0.05);    // 20% of width, min 50 mm
            double dx = (asmBox[3] + gap) - compBox[0];          // place comp.xMin just past the box
            if (dx <= 0)
            {
                return false;
            }

            var xform = comp.Transform2;
            var data = xform?.ArrayData as double[];
            if (data == null || data.Length < 12)
            {
                return false;
            }

            data[9] += dx; // translation X component of the 4x4 transform

            var math = _sw.GetMathUtility() as MathUtility;
            var moved = math?.CreateTransform(data) as MathTransform;
            if (moved == null)
            {
                return false;
            }

            comp.SetTransformAndSolve2(moved);
            return true;
        }

        private static double[] UnionTopLevelBoxes(IAssemblyDoc asm)
        {
            var comps = asm.GetComponents(true) as object[];
            if (comps == null || comps.Length == 0)
            {
                return null;
            }

            double xMin = double.MaxValue, yMin = double.MaxValue, zMin = double.MaxValue;
            double xMax = double.MinValue, yMax = double.MinValue, zMax = double.MinValue;
            bool any = false;

            foreach (var obj in comps)
            {
                var c = obj as Component2;
                if (c == null || c.GetSuppression2() == (int)swComponentSuppressionState_e.swComponentSuppressed)
                {
                    continue;
                }

                var b = c.GetBox(false, false) as double[];
                if (b == null || b.Length < 6)
                {
                    continue;
                }

                if (b[0] < xMin) xMin = b[0];
                if (b[1] < yMin) yMin = b[1];
                if (b[2] < zMin) zMin = b[2];
                if (b[3] > xMax) xMax = b[3];
                if (b[4] > yMax) yMax = b[4];
                if (b[5] > zMax) zMax = b[5];
                any = true;
            }

            return any ? new[] { xMin, yMin, zMin, xMax, yMax, zMax } : null;
        }

        private static bool FileExistsSafe(string path)
        {
            return !string.IsNullOrWhiteSpace(path) && File.Exists(path);
        }

        private static DocumentKind MapKind(int swDocType)
        {
            switch (swDocType)
            {
                case (int)swDocumentTypes_e.swDocPART: return DocumentKind.Part;
                case (int)swDocumentTypes_e.swDocASSEMBLY: return DocumentKind.Assembly;
                case (int)swDocumentTypes_e.swDocDRAWING: return DocumentKind.Drawing;
                case (int)swDocumentTypes_e.swDocNONE: return DocumentKind.None;
                default: return DocumentKind.Unknown;
            }
        }
    }
}
