using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using System;
using VeradeAddin.Logging;
using VeradeAddin.Models;

namespace VeradeAddin.Services
{
    /// <summary>
    /// "Bulón personalizado": builds a stepped bolt (head Ø1 × L1 + shank Ø2 × L2) from scratch in
    /// an empty part using a single 360° revolve. All geometry is created programmatically; no base
    /// part is inserted. Kept in its own partial so the configurator code is isolated from the rest
    /// of the SolidWorks service.
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
            double l1 = spec.HeadLengthMm * mmToM;
            double total = (spec.HeadLengthMm + spec.ShankLengthMm) * mmToM;

            var sketchMgr = model.SketchManager;
            bool addToDbWas = sketchMgr.AddToDB;
            bool displayWas = sketchMgr.DisplayWhenAdded;

            try
            {
                model.ClearSelection2(true);

                // Sketch on the front plane (first RefPlane in the tree, language-independent).
                var frontPlane = FirstRefPlane(model);
                if (frontPlane == null)
                {
                    result.Error = "No se encontró el plano frontal de la pieza.";
                    return result;
                }
                frontPlane.Select2(false, 0);
                sketchMgr.InsertSketch(true);

                // Draw in NORMAL mode (AddToDB = false) so SolidWorks automatically adds the
                // coincident + horizontal/vertical relations as the contour is drawn. This both
                // closes the profile (a half-open contour would revolve into just a cylinder) and
                // does most of the constraining for free; we only add the four dimensions afterwards.
                // (AddToDB = true skips the solver, leaves the contour open and returns segment
                // objects that crash AddRelation with an AccessViolation — avoided here.)
                sketchMgr.AddToDB = false;

                // Optional shank features (groove / chamfer), pre-computed in metres.
                double r3 = spec.GrooveDiameterMm * mmToM / 2.0;
                double xg1 = l1 + spec.GroovePositionMm * mmToM;            // groove near (head-side) edge
                double xg2 = xg1 + spec.GrooveWidthMm * mmToM;              // groove far edge
                double aChf = spec.ChamferSizeMm * mmToM;                   // axial cathetus
                double bChf = aChf * Math.Tan(spec.ChamferAngleDeg * Math.PI / 180.0); // radial drop
                double xc = total - aChf;                                   // chamfer start (top edge)
                double rTip = r2 - bChf;                                    // end-face top radius after chamfer

                // Centreline along the X axis = axis of revolution. Closed half-section above the axis:
                // head (r1) stepping down to shank (r2), then the shank top edge runs to the free end
                // inserting the groove notch (down to r3) and/or the end chamfer. Capture the segments
                // we drive with dimensions.
                var segCl = sketchMgr.CreateCenterLine(0, 0, 0, total, 0, 0);   // axis
                sketchMgr.CreateLine(0, 0, 0, 0, r1, 0);                        // left face
                var segHeadTop = sketchMgr.CreateLine(0, r1, 0, l1, r1, 0);     // head top (L1, Ø1)
                sketchMgr.CreateLine(l1, r1, 0, l1, r2, 0);                     // step down to shank

                SketchSegment segShankFirst = null; // first r2 run after the step (Ø2; L2 or P1)
                SketchSegment segGrooveBottom = null;
                double xCursor = l1;

                if (spec.HasGroove)
                {
                    segShankFirst = sketchMgr.CreateLine(l1, r2, 0, xg1, r2, 0);        // P1 run
                    sketchMgr.CreateLine(xg1, r2, 0, xg1, r3, 0);                       // into groove
                    segGrooveBottom = sketchMgr.CreateLine(xg1, r3, 0, xg2, r3, 0);     // groove bottom (E1, D3)
                    sketchMgr.CreateLine(xg2, r3, 0, xg2, r2, 0);                       // out of groove
                    xCursor = xg2;
                }

                if (spec.HasChamfer)
                {
                    var seg = sketchMgr.CreateLine(xCursor, r2, 0, xc, r2, 0);          // shank up to chamfer
                    if (segShankFirst == null) segShankFirst = seg;
                    sketchMgr.CreateLine(xc, r2, 0, total, rTip, 0);                    // chamfer face
                    sketchMgr.CreateLine(total, rTip, 0, total, 0, 0);                  // (short) right face
                }
                else
                {
                    var seg = sketchMgr.CreateLine(xCursor, r2, 0, total, r2, 0);       // shank to tip
                    if (segShankFirst == null) segShankFirst = seg;
                    sketchMgr.CreateLine(total, r2, 0, total, 0, 0);                    // right face
                }

                sketchMgr.CreateLine(total, 0, 0, 0, 0, 0);                             // bottom, on axis

                // Driving dimensions (best-effort; never aborts the revolve).
                AddBoltDimensions(model, spec, segHeadTop, segShankFirst, segGrooveBottom, segCl,
                    r1, r2, r3, l1, total, xg1, xg2);

                // Close the sketch, then select it so the revolve consumes it.
                model.ClearSelection2(true);
                sketchMgr.InsertSketch(true);

                var sketchFeature = LastProfileFeature(model);
                if (sketchFeature == null)
                {
                    result.Error = "No se pudo crear el croquis del bulón.";
                    return result;
                }
                model.ClearSelection2(true);
                sketchFeature.Select2(false, 0);

                // 360° solid revolve, merged, single direction.
                var revolve = model.FeatureManager.FeatureRevolve2(
                    true,   // SingleDir
                    true,   // IsSolid
                    false,  // IsThin
                    false,  // IsCut
                    false,  // ReverseDir
                    false,  // BothDirectionUpToSameEntity
                    (int)swEndConditions_e.swEndCondBlind, // Dir1Type
                    (int)swEndConditions_e.swEndCondBlind, // Dir2Type
                    2.0 * Math.PI, // Dir1Angle (full revolution)
                    0.0,           // Dir2Angle
                    false, false,  // OffsetReverse1/2
                    0.0, 0.0,      // OffsetDistance1/2
                    0,             // ThinType (unused, IsThin=false)
                    0.0, 0.0,      // ThinThickness1/2
                    true,   // Merge
                    true,   // UseFeatScope
                    true);  // UseAutoSelect

                if (revolve == null)
                {
                    result.Error = "SolidWorks no pudo crear la revolución.";
                    return result;
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
                // Make sure the sketch is never left open and DB flags are restored.
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

        /// <summary>
        /// Adds the driving dimensions to the (already auto-related) bolt profile. Always: L1/Ø1 on the
        /// head top edge and Ø2 on the first shank run. L2 is only driven when the shank is a single
        /// run (no groove/chamfer split it). When a groove is present its P1 (position), E1 (width) and
        /// D3 (bottom Ø) are added too. Each edge dimensioned across the axis becomes a diameter
        /// dimension. No relations are added here (normal-mode drawing supplied coincident +
        /// horizontal/vertical). The "enter value" popup and the over-defining "make driven?" prompt
        /// are suppressed so AddDimension2 can never block the macro; every dimension is best-effort
        /// and never aborts the revolve.
        /// </summary>
        private void AddBoltDimensions(
            IModelDoc2 model, BoltSpec spec, SketchSegment segHeadTop, SketchSegment segShankFirst,
            SketchSegment segGrooveBottom, SketchSegment axis,
            double r1, double r2, double r3, double l1, double total, double xg1, double xg2)
        {
            // "Input dimension value" (the modal Modify box on dimension create) and the over-defining
            // "make driven?" prompt are APPLICATION-level options, so they MUST be toggled on ISldWorks
            // (_sw). Toggling them on IModelDoc2 silently does nothing — that is why the Modify box kept
            // popping up and freezing the macro. Disable the value popup while we add the dimensions and
            // ALWAYS re-enable it in the finally (even on error) so the user is never left with it stuck
            // off: re-enabling matches SolidWorks' normal default.
            bool drivenWas = _sw.GetUserPreferenceToggle((int)swUserPreferenceToggle_e.swAddDrivenDimensions);
            try
            {
                _sw.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swInputDimValOnCreate, false);
                _sw.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swAddDrivenDimensions, true);

                const double off = 0.012;
                LengthDim(model, segHeadTop, l1 / 2.0, r1 + off);                       // L1 (head length)
                DiameterDim(model, segHeadTop, axis, l1 / 2.0, -(r1 + off));            // Ø1 (head)
                DiameterDim(model, segShankFirst, axis, (l1 + total) / 2.0, -(r2 + off)); // Ø2 (shank)

                if (!spec.HasGroove)
                {
                    // segShankFirst spans the whole shank only when no groove splits it.
                    LengthDim(model, segShankFirst, (l1 + total) / 2.0, r1 + 2.0 * off); // L2 (shank length)
                }
                else
                {
                    LengthDim(model, segShankFirst, (l1 + xg1) / 2.0, r1 + 2.0 * off);   // P1 (head -> groove)
                    LengthDim(model, segGrooveBottom, (xg1 + xg2) / 2.0, r1 + 3.0 * off); // E1 (groove width)
                    DiameterDim(model, segGrooveBottom, axis, (xg1 + xg2) / 2.0, -(r3 + off)); // D3 (groove Ø)
                }
            }
            finally
            {
                // ALWAYS re-enable the value popup (and restore the driven-dim toggle), pase lo que pase.
                _sw.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swInputDimValOnCreate, true);
                _sw.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swAddDrivenDimensions, drivenWas);
                model.ClearSelection2(true);
            }
        }

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
                axis.Select4(true, null);
                model.AddDimension2(x, y, 0);
            }
            catch { }
            finally { model.ClearSelection2(true); }
        }
    }
}
