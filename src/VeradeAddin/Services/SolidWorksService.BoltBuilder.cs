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

                // Centreline along the X axis = axis of revolution. Closed half-section above the
                // axis: head (r1) stepping down to shank (r2). Capture the two top edges + the axis
                // for dimensioning.
                var segCl = sketchMgr.CreateCenterLine(0, 0, 0, total, 0, 0);   // axis
                sketchMgr.CreateLine(0, 0, 0, 0, r1, 0);                        // P0 -> P1 (left face)
                var segHeadTop = sketchMgr.CreateLine(0, r1, 0, l1, r1, 0);     // P1 -> P2 (head top)
                sketchMgr.CreateLine(l1, r1, 0, l1, r2, 0);                     // P2 -> P3 (step)
                var segShankTop = sketchMgr.CreateLine(l1, r2, 0, total, r2, 0);// P3 -> P4 (shank top)
                sketchMgr.CreateLine(total, r2, 0, total, 0, 0);               // P4 -> P5 (right face)
                sketchMgr.CreateLine(total, 0, 0, 0, 0, 0);                    // P5 -> P0 (bottom, on axis)

                // Driving dimensions (best-effort; never aborts the revolve).
                AddBoltDimensions(model, segHeadTop, segShankTop, segCl, r1, r2, l1, total);

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
        /// Adds the four driving dimensions to the (already auto-related) bolt profile: the two top
        /// edges give L1/L2 (length) and Ø1/Ø2 (each edge dimensioned to the centreline, placed
        /// across the axis so SolidWorks makes them diameter dimensions). No relations are added here
        /// (normal-mode drawing supplied coincident + horizontal/vertical). The "enter value" popup
        /// and the over-defining "make driven?" prompt are suppressed so AddDimension2 can never
        /// block the macro with a modal dialog; each dimension is best-effort and never aborts.
        /// </summary>
        private void AddBoltDimensions(
            IModelDoc2 model, SketchSegment segHeadTop, SketchSegment segShankTop, SketchSegment axis,
            double r1, double r2, double l1, double total)
        {
            bool inputWas = model.GetUserPreferenceToggle((int)swUserPreferenceToggle_e.swInputDimValOnCreate);
            bool drivenWas = model.GetUserPreferenceToggle((int)swUserPreferenceToggle_e.swAddDrivenDimensions);
            model.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swInputDimValOnCreate, false);
            model.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swAddDrivenDimensions, true);
            try
            {
                const double off = 0.012;
                LengthDim(model, segHeadTop, l1 / 2.0, r1 + off);                       // L1 (head length)
                LengthDim(model, segShankTop, (l1 + total) / 2.0, r1 + 2.0 * off);      // L2 (shank length)
                DiameterDim(model, segHeadTop, axis, l1 / 2.0, -(r1 + off));            // Ø1 (head)
                DiameterDim(model, segShankTop, axis, (l1 + total) / 2.0, -(r2 + off)); // Ø2 (shank)
            }
            finally
            {
                model.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swInputDimValOnCreate, inputWas);
                model.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swAddDrivenDimensions, drivenWas);
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
