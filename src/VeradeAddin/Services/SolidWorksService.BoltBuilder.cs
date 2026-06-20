using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using System;
using VeradeAddin.Logging;
using VeradeAddin.Models;

namespace VeradeAddin.Services
{
    /// <summary>
    /// "Bulón personalizado": builds a stepped bolt (head Ø1 × L1 + shank Ø2 × L2) from scratch in an
    /// empty part. Modelled the same way an engineer would in the feature tree (and mirroring the
    /// reference console <c>BoltCreation.cs</c>): THREE separate features instead of one fat profile —
    /// <list type="number">
    /// <item>a 360° solid <b>revolve</b> of the head+shank half-section,</item>
    /// <item>an optional 360° <b>cut-revolve</b> for the retaining-ring groove,</item>
    /// <item>an optional real <b>chamfer</b> feature on the free-end edge.</item>
    /// </list>
    /// Kept in its own partial so the configurator code is isolated from the rest of the service. The
    /// front plane is found language-independently (first <c>RefPlane</c>), so it works whatever the
    /// SolidWorks UI language is; the chamfer edge is picked by coordinate at the tip.
    /// </summary>
    public sealed partial class SolidWorksService
    {
        public bool IsActivePartEmpty()
        {
            var model = _sw.IActiveDoc2 as IModelDoc2;
            if (model == null || model.GetType() != (int)swDocumentTypes_e.swDocPART)
            {
                return false;
            }

            // "Empty" = no geometry yet. We test for the two things that mean the user already
            // started modelling: any body (solid/surface/wire/...) or any sketch in the tree. This is
            // robust across SolidWorks versions/languages, unlike whitelisting default feature names
            // (a fresh part carries many version-specific default features that would falsely count).
            var part = model as PartDoc;
            if (part != null)
            {
                var bodies = part.GetBodies2((int)swBodyType_e.swAllBodies, false) as object[];
                if (bodies != null && bodies.Length > 0)
                {
                    return false;
                }
            }

            var feature = model.FirstFeature() as Feature;
            while (feature != null)
            {
                if (feature.GetTypeName2() == "ProfileFeature") // a sketch
                {
                    return false;
                }
                feature = feature.GetNextFeature() as Feature;
            }
            return true;
        }

        public BoltBuildResult CreateCustomBolt(BoltSpec spec)
        {
            var result = new BoltBuildResult();

            if (spec == null || !spec.IsValid)
            {
                result.Error = "Medidas no válidas (Ø1 debe ser mayor que Ø2 y todas > 0).";
                return result;
            }

            var model = _sw.IActiveDoc2 as IModelDoc2;
            if (model == null || model.GetType() != (int)swDocumentTypes_e.swDocPART)
            {
                result.Error = "No hay una pieza activa.";
                return result;
            }

            // mm -> metres (SolidWorks API works in metres). Radii for the half-section profile.
            const double mmToM = 0.001;
            double r1 = spec.HeadDiameterMm * mmToM / 2.0;
            double r2 = spec.ShankDiameterMm * mmToM / 2.0;
            double r3 = spec.GrooveDiameterMm * mmToM / 2.0;
            double l1 = spec.HeadLengthMm * mmToM;
            double total = (spec.HeadLengthMm + spec.ShankLengthMm) * mmToM;
            double xg1 = l1 + spec.GroovePositionMm * mmToM;     // groove near (head-side) edge
            double xg2 = xg1 + spec.GrooveWidthMm * mmToM;       // groove far edge

            // The modal "input dimension value" box and the over-defining "make driven?" prompt are
            // APPLICATION-level options (ISldWorks, not the doc): with them on, every AddDimension2 would
            // pop a modal and freeze the macro. Turn them off for the whole build and ALWAYS restore them
            // (matching SolidWorks' normal defaults) in the finally, whatever happens.
            bool drivenWas = _sw.GetUserPreferenceToggle((int)swUserPreferenceToggle_e.swAddDrivenDimensions);
            try
            {
                _sw.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swInputDimValOnCreate, false);
                _sw.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swAddDrivenDimensions, true);

                // 1) Base body: head + shank, single 360° solid revolve.
                string err = RevolveBody(model, r1, r2, l1, total);
                if (err != null) { result.Error = err; return result; }

                // 2) Groove: separate 360° cut-revolve (rectangular notch down to r3).
                if (spec.HasGroove)
                {
                    err = CutGroove(model, r2, r3, l1, total, xg1, xg2);
                    if (err != null) { result.Error = err; return result; }
                }

                // 3) Chamfer: real chamfer feature on the free-end edge.
                if (spec.HasChamfer)
                {
                    err = ApplyChamfer(model, spec, r2, total);
                    if (err != null) { result.Error = err; return result; }
                }

                model.ClearSelection2(true);
                model.EditRebuild3();
                model.ViewZoomtofit2();

                result.Success = true;
                result.TotalLengthMm = spec.HeadLengthMm + spec.ShankLengthMm;
                return result;
            }
            catch (Exception ex)
            {
                _log.Log("Bulón personalizado", "Part", LogOutcome.Error, "CreateCustomBolt failed", ex.ToString());
                result.Error = ex.Message;
                return result;
            }
            finally
            {
                _sw.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swInputDimValOnCreate, true);
                _sw.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swAddDrivenDimensions, drivenWas);
                model.ClearSelection2(true);
            }
        }

        // ---- 1) base body --------------------------------------------------------------------------

        /// <summary>
        /// Sketches the closed head+shank half-section on the front plane and revolves it 360° into a
        /// solid. Returns null on success or a Spanish error string. Drawn in NORMAL mode
        /// (<c>AddToDB = false</c>) so SolidWorks auto-adds the coincident/horizontal/vertical relations
        /// and closes the contour; we only add the four driving dimensions afterwards.
        /// </summary>
        private string RevolveBody(IModelDoc2 model, double r1, double r2, double l1, double total)
        {
            var sketchMgr = model.SketchManager;
            bool addToDbWas = sketchMgr.AddToDB, displayWas = sketchMgr.DisplayWhenAdded;
            try
            {
                model.ClearSelection2(true);
                var frontPlane = FirstRefPlane(model);
                if (frontPlane == null) return "No se encontró el plano frontal de la pieza.";
                frontPlane.Select2(false, 0);
                sketchMgr.InsertSketch(true);
                sketchMgr.AddToDB = false;

                // Centreline on the X axis = axis of revolution. Closed half-section above it:
                // left face → head top (Ø1/L1) → step down → shank top (Ø2/L2) → right face → bottom.
                var segCl = sketchMgr.CreateCenterLine(0, 0, 0, total, 0, 0);
                sketchMgr.CreateLine(0, 0, 0, 0, r1, 0);                       // left face
                var segHeadTop = sketchMgr.CreateLine(0, r1, 0, l1, r1, 0);   // head top (L1, Ø1)
                sketchMgr.CreateLine(l1, r1, 0, l1, r2, 0);                   // step down to shank
                var segShankTop = sketchMgr.CreateLine(l1, r2, 0, total, r2, 0); // shank top (L2, Ø2)
                sketchMgr.CreateLine(total, r2, 0, total, 0, 0);             // right (free-end) face
                sketchMgr.CreateLine(total, 0, 0, 0, 0, 0);                  // bottom, on the axis

                const double off = 0.012;
                LengthDim(model, segHeadTop, l1 / 2.0, r1 + off);                        // L1
                DiameterDim(model, segHeadTop, segCl, l1 / 2.0, -(r1 + off));            // Ø1
                LengthDim(model, segShankTop, (l1 + total) / 2.0, r1 + 2.0 * off);       // L2
                DiameterDim(model, segShankTop, segCl, (l1 + total) / 2.0, -(r2 + off)); // Ø2

                model.ClearSelection2(true);
                sketchMgr.InsertSketch(true);

                var sketchFeature = LastProfileFeature(model);
                if (sketchFeature == null) return "No se pudo crear el croquis del bulón.";
                model.ClearSelection2(true);
                sketchFeature.Select2(false, 0);

                // 360° solid revolve, merged, single direction.
                var revolve = model.FeatureManager.FeatureRevolve2(
                    true, true, false, false, false, false,
                    (int)swEndConditions_e.swEndCondBlind, (int)swEndConditions_e.swEndCondBlind,
                    2.0 * Math.PI, 0.0, false, false, 0.0, 0.0,
                    (int)swThinWallType_e.swThinWallOneDirection, 0.0, 0.0,
                    true, false, false);
                if (revolve == null) return "SolidWorks no pudo crear la revolución.";
                return null;
            }
            finally
            {
                RestoreSketch(sketchMgr, addToDbWas, displayWas);
            }
        }

        // ---- 2) groove (cut-revolve) ---------------------------------------------------------------

        /// <summary>
        /// Sketches the rectangular groove notch (shank radius r2 down to bottom radius r3, from xg1 to
        /// xg2) on the front plane and removes it with a 360° cut-revolve. Returns null on success.
        /// </summary>
        private string CutGroove(IModelDoc2 model, double r2, double r3, double l1, double total, double xg1, double xg2)
        {
            var sketchMgr = model.SketchManager;
            bool addToDbWas = sketchMgr.AddToDB, displayWas = sketchMgr.DisplayWhenAdded;
            try
            {
                model.ClearSelection2(true);
                var frontPlane = FirstRefPlane(model);
                if (frontPlane == null) return "No se encontró el plano frontal para la ranura.";
                frontPlane.Select2(false, 0);
                sketchMgr.InsertSketch(true);
                sketchMgr.AddToDB = false;

                // Construction axis (for the diameter dimension) + the closed rectangle of the notch.
                var segCl = sketchMgr.CreateCenterLine(0, 0, 0, total, 0, 0);
                var segNearWall = sketchMgr.CreateLine(xg1, r2, 0, xg1, r3, 0);  // into groove (near wall)
                var segBottom = sketchMgr.CreateLine(xg1, r3, 0, xg2, r3, 0);    // groove bottom (E1, D3)
                sketchMgr.CreateLine(xg2, r3, 0, xg2, r2, 0);                    // out of groove (far wall)
                var segTop = sketchMgr.CreateLine(xg2, r2, 0, xg1, r2, 0);       // top, on the shank surface

                const double off = 0.012;
                // P1: from the head/shank step edge (selected on the body) to the groove's near wall.
                EdgeToSegDim(model, l1, r2, segNearWall, (l1 + xg1) / 2.0, r2 + 2.0 * off); // P1 (position)
                LengthDim(model, segTop, (xg1 + xg2) / 2.0, r2 + 3.0 * off);               // E1 (width)
                DiameterDim(model, segBottom, segCl, (xg1 + xg2) / 2.0, -(r3 + off));   // D3 (bottom Ø)

                model.ClearSelection2(true);
                sketchMgr.InsertSketch(true);

                var sketchFeature = LastProfileFeature(model);
                if (sketchFeature == null) return "No se pudo crear el croquis de la ranura.";
                model.ClearSelection2(true);
                sketchFeature.Select2(false, 0);

                // 360° cut-revolve (IsCut = true), merged.
                var cut = model.FeatureManager.FeatureRevolve2(
                    true, true, false, true, false, true,
                    (int)swEndConditions_e.swEndCondBlind, (int)swEndConditions_e.swEndCondBlind,
                    2.0 * Math.PI, 0.0, false, false, 0.0, 0.0,
                    (int)swThinWallType_e.swThinWallOneDirection, 0.0, 0.0,
                    true, false, true);
                if (cut == null) return "SolidWorks no pudo crear el corte de la ranura.";
                return null;
            }
            finally
            {
                RestoreSketch(sketchMgr, addToDbWas, displayWas);
            }
        }

        // ---- 3) chamfer ----------------------------------------------------------------------------

        /// <summary>
        /// Adds a real angle-distance chamfer feature on the free-end circular edge (picked by
        /// coordinate at the tip). The angle is passed straight through as entered, like the reference
        /// console. Returns null on success.
        /// </summary>
        private string ApplyChamfer(IModelDoc2 model, BoltSpec spec, double r2, double total)
        {
            model.ClearSelection2(true);
            // The tip edge is the circle at x = total; (total, r2, 0) is a point on it.
            bool ok = model.Extension.SelectByID2(
                "", "EDGE", total, r2, 0, false, 0, null, (int)swSelectOption_e.swSelectOptionDefault);
            if (!ok) return "No se pudo seleccionar la arista del extremo para el chaflán.";

            var chamfer = model.FeatureManager.InsertFeatureChamfer(
                4, (int)swChamferType_e.swChamferAngleDistance,
                spec.ChamferSizeMm * 0.001, spec.ChamferAngleDeg * Math.PI / 180.0,
                0, 0, 0, 0);
            if (chamfer == null) return "SolidWorks no pudo crear el chaflán.";
            model.ClearSelection2(true);
            return null;
        }

        // ---- shared helpers ------------------------------------------------------------------------

        private static void RestoreSketch(SketchManager sketchMgr, bool addToDbWas, bool displayWas)
        {
            // Never leave a sketch open and always restore the DB flags, even on error.
            try
            {
                sketchMgr.AddToDB = addToDbWas;
                sketchMgr.DisplayWhenAdded = displayWas;
                if (sketchMgr.ActiveSketch != null)
                {
                    sketchMgr.InsertSketch(true);
                }
            }
            catch { }
        }

        private static Feature FirstRefPlane(IModelDoc2 model)
        {
            var feature = model.FirstFeature() as Feature;
            while (feature != null)
            {
                if (feature.GetTypeName2() == "RefPlane")
                {
                    return feature;
                }
                feature = feature.GetNextFeature() as Feature;
            }
            return null;
        }

        private static Feature LastProfileFeature(IModelDoc2 model)
        {
            Feature found = null;
            var feature = model.FirstFeature() as Feature;
            while (feature != null)
            {
                if (feature.GetTypeName2() == "ProfileFeature")
                {
                    found = feature; // keep walking; we want the most recently added sketch
                }
                feature = feature.GetNextFeature() as Feature;
            }
            return found;
        }

        // Each *Dim is best-effort: a failed dimension never aborts the feature. Selection is always
        // cleared afterwards so the next dimension starts clean.
        private static void LengthDim(IModelDoc2 model, SketchSegment seg, double x, double y)
        {
            if (seg == null) return;
            try
            {
                model.ClearSelection2(true);
                seg.Select4(false, null);
                model.AddDimension2(x, y, 0);
            }
            catch { }
            finally { model.ClearSelection2(true); }
        }

        private static void DiameterDim(IModelDoc2 model, SketchSegment line, SketchSegment axis, double x, double y)
        {
            if (line == null || axis == null) return;
            try
            {
                model.ClearSelection2(true);
                line.Select4(false, null);
                axis.Select4(true, null);   // line + centreline ⇒ AddDimension2 makes it a diameter dim
                model.AddDimension2(x, y, 0);
            }
            catch { }
            finally { model.ClearSelection2(true); }
        }

        // Horizontal distance from a body edge (selected by coordinate) to a sketch segment — used for
        // the groove position P1, measured from the head/shank step edge.
        private void EdgeToSegDim(IModelDoc2 model, double edgeX, double edgeY, SketchSegment seg, double x, double y)
        {
            if (seg == null) return;
            try
            {
                model.ClearSelection2(true);
                model.Extension.SelectByID2(
                    "", "EDGE", edgeX, edgeY, 0, true, 0, null, (int)swSelectOption_e.swSelectOptionDefault);
                seg.Select4(true, null);
                model.AddDimension2(x, y, 0);
            }
            catch { }
            finally { model.ClearSelection2(true); }
        }
    }
}
