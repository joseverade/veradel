using System;
using VeradeAddin.Models;

namespace VeradeAddin.Services
{
    /// <summary>
    /// All SolidWorks API access lives behind this interface so command handlers,
    /// UI and tests never reference COM interop types. Returns plain models only.
    /// </summary>
    public interface ISolidWorksService
    {
        /// <summary>Snapshot of the active document, or null when none is open.</summary>
        ActiveDocument GetActiveDocument();

        /// <summary>
        /// When the active document is an assembly with a component selected, returns the
        /// selection hierarchy as a tree rooted at the top-level assembly, with the
        /// selected component flagged (<see cref="ComponentNode.IsSelectedTarget"/>).
        /// Returns null when the active doc is not an assembly or no component is selected.
        /// </summary>
        ComponentNode GetSelectedComponentHierarchy();

        /// <summary>
        /// True when the active document is a drawing whose current sheet contains at least
        /// one view that references an assembly. Used to enable the cosmetic-thread command.
        /// </summary>
        bool ActiveDrawingReferencesAssembly();

        /// <summary>
        /// Inserts cosmetic thread annotations into every assembly view of the active
        /// drawing's current sheet (SolidWorks does not show them by default). Returns a
        /// COM-free summary. Does nothing meaningful if the active doc is not a drawing.
        /// </summary>
        CosmeticThreadResult InsertCosmeticThreadsInAssemblyViews();

        /// <summary>True when the active document is an assembly with a component selected.</summary>
        bool IsComponentSelectedInActiveAssembly();

        /// <summary>
        /// Describes what extracting the selected component would change (fixed? how many mates?),
        /// so the caller can warn the user first. Null when the active doc is not an assembly or
        /// no component is selected.
        /// </summary>
        ComponentExtractionPlan InspectSelectedComponentForExtraction();

        /// <summary>
        /// Frees the selected component (removes Fixed, suppresses its position mates), moves it
        /// past the assembly bounding box and zooms to the extracted component. Returns a COM-free
        /// summary.
        /// </summary>
        ExtractComponentResult ExtractSelectedComponent();

        /// <summary>
        /// Window handle of the active document's graphics area (model space), for cropping a
        /// screen capture to just the viewport. Returns <see cref="IntPtr.Zero"/> when there is no
        /// active view.
        /// </summary>
        IntPtr GetActiveModelViewHandle();

        /// <summary>
        /// Width in pixels of the FeatureManager panel docked on the left of the active document's
        /// window, so a screen capture can trim it off. Returns 0 when unavailable/collapsed.
        /// </summary>
        int GetActiveFeatureManagerWidth();

        /// <summary>
        /// Inspects the active drawing for the "Exportar" command: whether it references a model and
        /// whether STEP is allowed (a part, or an assembly with ≤ 10 components). Returns a result
        /// with <see cref="DrawingExportInfo.IsDrawing"/> false when the active doc is not a drawing.
        /// </summary>
        DrawingExportInfo InspectActiveDrawingForExport();

        /// <summary>
        /// Exports the active drawing to the requested formats: PDF/DWG from the drawing itself, and
        /// STEP from the referenced model (which is activated first, as SolidWorks requires, then the
        /// drawing is reactivated). Returns a COM-free per-format result.
        /// </summary>
        ExportResult ExportActiveDrawing(ExportRequest request);

        /// <summary>
        /// Inspects the SINGLE part view the user has selected for "Colorear aristas": its part and the
        /// appearance colours found on it (<see cref="EdgeColoringPlan.ViewName"/> identifies the view).
        /// <see cref="EdgeColoringPlan.IsDrawing"/> is false when the active doc is not a drawing;
        /// <see cref="EdgeColoringPlan.Message"/> explains when there is not exactly one part view selected.
        /// </summary>
        EdgeColoringPlan InspectDrawingForEdgeColoring();

        /// <summary>
        /// Carries the chosen face colours onto the corresponding edges of the selected view only
        /// (<see cref="EdgeColorRequest.ViewName"/>). Best-effort (the 3D→2D mapping is inherently
        /// partial). Returns a COM-free per-run summary.
        /// </summary>
        EdgeColoringResult ApplyEdgeColoring(EdgeColorRequest request);

        /// <summary>
        /// Puts EVERY edge of the selected part view (visible, hidden and silhouettes) back to black.
        /// Stateless undo for "Colorear aristas"; acts only on the single selected view.
        /// </summary>
        EdgeColoringResult ClearEdgeColors();

        /// <summary>
        /// Inspects the active drawing for "Despiece de calderería": the referenced part, its
        /// configurations and whether a destructive weldment insertion would be required. Returns a
        /// result with <see cref="BoilermakingPlan.IsDrawing"/> false when the active doc is not a drawing.
        /// </summary>
        BoilermakingPlan InspectDrawingForBreakdown();

        /// <summary>
        /// Generates the boilermaking part breakdown of the referenced part on NEW sheets: a group of
        /// 3 views (front/side/top) per cut-list item with the body isolated (all groups share one
        /// global scale), plus an isometric exploded view with one balloon per item and the cut-list
        /// table, paginated across A0 sheets. DESTRUCTIVE: if the part is multibody and not a weldment
        /// it inserts the weldment feature (only when <paramref name="allowWeldmentInsertion"/> is true)
        /// and saves the part and drawing at the end. Returns a COM-free summary.
        /// </summary>
        BoilermakingResult GenerateBoilermakingBreakdown(string configName, bool allowWeldmentInsertion);

        /// <summary>
        /// True when the active document is a part whose feature tree is empty (only the default
        /// planes/origin/folders, no bodies or user features). Gates the "Bulón personalizado"
        /// command, which builds geometry from scratch and must not run on a part that already
        /// contains modelling.
        /// </summary>
        bool IsActivePartEmpty();

        /// <summary>
        /// Builds the custom bolt in the active (empty) part: sketches the stepped half-section on
        /// the front plane with a centreline axis and creates a 360° solid revolve. Returns a
        /// COM-free summary. Assumes <see cref="IsActivePartEmpty"/> and <see cref="BoltSpec.IsValid"/>.
        /// </summary>
        BoltBuildResult CreateCustomBolt(BoltSpec spec);
    }
}
