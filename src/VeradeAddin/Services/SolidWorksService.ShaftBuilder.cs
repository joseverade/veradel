using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using System;
using System.Collections.Generic;
using VeradeAddin.Logging;
using VeradeAddin.Models;

namespace VeradeAddin.Services
{
    /// <summary>
    /// "Eje personalizado" (fase 1: cuerpo): builds the stepped shaft body from scratch in an empty
    /// part, left → right. One 360° solid revolve of the whole stepped half-section, dimensioned per
    /// segment (Ø + L, like the reference console ChompCreator). Consecutive levels with the same
    /// diameter are merged into one revolve segment (a zero-height step is unsketchable) and each
    /// swallowed boundary is materialised afterwards as a SPLIT LINE: a vertical sketch line on the
    /// front plane projected both ways onto the cylindrical face
    /// (<see cref="IModelDoc2.InsertSplitLineProject"/>), which rings the shaft at that X and splits
    /// the face — verified by reflection on the interop DLL. Shares the sketch/dimension helpers of
    /// <c>SolidWorksService.BoltBuilder.cs</c> (same partial class).
    /// </summary>
    public sealed partial class SolidWorksService
    {
        public ShaftBuildResult CreateShaft(ShaftSpec spec)
        {
            var result = new ShaftBuildResult();

            string invalid = spec == null ? "Sin datos del eje." : spec.Validate();
            if (invalid != null)
            {
                result.Error = invalid;
                return result;
            }

            var model = _sw.IActiveDoc2 as IModelDoc2;
            if (model == null || model.GetType() != (int)swDocumentTypes_e.swDocPART)
            {
                result.Error = "No hay una pieza activa.";
                return result;
            }

            var segments = spec.GetMergedSegments();

            // Same application-level toggles as the bolt: no modal dimension boxes, no "make driven?"
            // prompt while the build adds dimensions. Always restored in the finally.
            bool drivenWas = _sw.GetUserPreferenceToggle((int)swUserPreferenceToggle_e.swAddDrivenDimensions);
            try
            {
                _sw.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swInputDimValOnCreate, false);
                _sw.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swAddDrivenDimensions, true);

                string err = RevolveShaftBody(model, segments);
                if (err != null) { result.Error = err; return result; }

                int splits = 0;
                double totalM = spec.TotalLengthMm * 0.001;
                foreach (var segment in segments)
                {
                    // Left → right, so each new split always lands on the RIGHTMOST sub-face so far:
                    // after splitting at x1, the face containing x2 (> x1) starts at x1. Selecting the
                    // face just LEFT of the cut (between the previous boundary and the cut) is always
                    // inside that yet-unsplit region.
                    double prev = segment.StartMm;
                    foreach (var cut in segment.SplitPositionsMm)
                    {
                        err = InsertShaftSplitLine(model,
                            cut * 0.001,
                            segment.DiameterMm * 0.001 / 2.0,
                            (prev + cut) / 2.0 * 0.001,
                            totalM);
                        if (err != null) { result.Error = err; return result; }
                        splits++;
                        prev = cut;
                    }
                }

                // Grooves BEFORE keyways: their position dims reference the boundary edge rings,
                // which an angled keyway cut could otherwise have carved away.
                var grooves = spec.Grooves ?? new List<ShaftGroove>();
                for (int g = 0; g < grooves.Count; g++)
                {
                    err = CutShaftGroove(model, spec, grooves[g]);
                    if (err != null)
                    {
                        result.Error = "Ranura " + (g + 1) + ": " + err;
                        return result;
                    }
                }

                // Undercuts AFTER grooves (a groove never touches a diameter-change boundary, so
                // every shoulder ring is still intact here) and BEFORE keyways (an overlapping
                // keyway cut is blocked by validation, but a straddling one could still eat the
                // shoulder ring the undercut anchors to).
                var undercuts = spec.Undercuts ?? new List<ShaftUndercut>();
                for (int u = 0; u < undercuts.Count; u++)
                {
                    err = CutShaftUndercut(model, spec, undercuts[u]);
                    if (err != null)
                    {
                        result.Error = "Entalladura " + (u + 1) + ": " + err;
                        return result;
                    }
                }

                Feature shaftAxis = null;
                var keyways = spec.Keyways ?? new List<ShaftKeyway>();
                for (int k = 0; k < keyways.Count; k++)
                {
                    err = CutKeyway(model, spec, keyways[k], ref shaftAxis);
                    if (err != null)
                    {
                        result.Error = "Chaveta " + (k + 1) + ": " + err;
                        return result;
                    }
                }

                model.ClearSelection2(true);
                model.EditRebuild3();
                model.ViewZoomtofit2();

                result.Success = true;
                result.LevelCount = spec.Levels.Count;
                result.TotalLengthMm = spec.TotalLengthMm;
                result.SplitLineCount = splits;
                result.KeywayCount = keyways.Count;
                result.GrooveCount = grooves.Count;
                result.UndercutCount = undercuts.Count;
                return result;
            }
            catch (Exception ex)
            {
                _log.Log("Eje personalizado", "Part", LogOutcome.Error, "CreateShaft failed", ex.ToString());
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

        // ---- 1) body: one stepped 360° revolve -----------------------------------------------------

        /// <summary>
        /// Sketches the whole stepped half-section on the front plane (left face at the part origin,
        /// growing +X) and revolves it 360°. Per merged segment it adds the driving Ø (against the
        /// construction centreline) and the horizontal length. Same AddToDB + ConstrainAll approach
        /// as the bolt. Returns null on success or a Spanish error.
        /// </summary>
        private string RevolveShaftBody(IModelDoc2 model, List<ShaftSegment> segments)
        {
            const double mmToM = 0.001;
            double total = 0, rMax = 0;
            foreach (var s in segments)
            {
                total += s.LengthMm * mmToM;
                rMax = Math.Max(rMax, s.DiameterMm * mmToM / 2.0);
            }

            var sketchMgr = model.SketchManager;
            bool addToDbWas = sketchMgr.AddToDB, displayWas = sketchMgr.DisplayWhenAdded;
            try
            {
                model.ClearSelection2(true);
                var frontPlane = FirstRefPlane(model);
                if (frontPlane == null) return "No se encontró el plano frontal de la pieza.";
                frontPlane.Select2(false, 0);
                sketchMgr.InsertSketch(true);

                sketchMgr.AddToDB = true;
                sketchMgr.DisplayWhenAdded = false;

                // Centreline OUTSIDE the profile (negative X, like the bolt) = axis of revolution.
                var segCl = sketchMgr.CreateLine(0, 0, 0, -total, 0, 0);
                segCl.ConstructionGeometry = true;

                // Closed contour: left face up, each segment top (+ step wall between different
                // diameters), right face down, back along the axis.
                var segLeft = Line(sketchMgr, 0, 0, 0, segments[0].DiameterMm * mmToM / 2.0);
                var topSegs = new List<SketchSegment>();
                double x = 0;
                for (int i = 0; i < segments.Count; i++)
                {
                    double r = segments[i].DiameterMm * mmToM / 2.0;
                    double x2 = x + segments[i].LengthMm * mmToM;
                    topSegs.Add(Line(sketchMgr, x, r, x2, r));
                    if (i < segments.Count - 1)
                    {
                        Line(sketchMgr, x2, r, x2, segments[i + 1].DiameterMm * mmToM / 2.0); // step wall
                    }
                    x = x2;
                }
                Line(sketchMgr, total, segments[segments.Count - 1].DiameterMm * mmToM / 2.0, total, 0);
                Line(sketchMgr, total, 0, 0, 0);

                ConstrainAll(sketchMgr);

                // Anchor to the part origin (ConstrainAll never relates outside the sketch).
                ((SketchPoint)((SketchLine)segLeft).GetStartPoint2()).Select4(true, null);
                model.Extension.SelectByID2("", "EXTSKETCHPOINT", 0, 0, 0, true, 0, null, (int)swSelectOption_e.swSelectOptionDefault);
                model.SketchAddConstraints("sgCOINCIDENT");

                // Driving dimensions per segment: Ø below the axis (stacked so the texts don't pile
                // up; clearly past −r so SolidWorks reads a DIAMETER, not a radius), L above.
                const double off = 0.012;
                for (int i = 0; i < segments.Count; i++)
                {
                    double xMid = (segments[i].StartMm + segments[i].LengthMm / 2.0) * mmToM;
                    DiameterDim(model, topSegs[i], segCl, xMid, -(rMax + off * (i + 1)));
                    LengthDim(model, topSegs[i], xMid, rMax + off * (i + 1));
                }

                model.ClearSelection2(true);
                sketchMgr.InsertSketch(true);

                var sketchFeature = LastProfileFeature(model);
                if (sketchFeature == null) return "No se pudo crear el croquis del eje.";
                model.ClearSelection2(true);
                sketchFeature.Select2(false, 0);

                var revolve = model.FeatureManager.FeatureRevolve2(
                    true, true, false, false, false, false,
                    (int)swEndConditions_e.swEndCondBlind, (int)swEndConditions_e.swEndCondBlind,
                    2.0 * Math.PI, 0.0, false, false, 0.0, 0.0,
                    (int)swThinWallType_e.swThinWallOneDirection, 0.0, 0.0,
                    true, false, false);
                if (revolve == null) return "SolidWorks no pudo crear la revolución del eje.";
                return null;
            }
            finally
            {
                RestoreSketch(sketchMgr, addToDbWas, displayWas);
            }
        }

        // ---- 2) split lines at merged equal-diameter boundaries ------------------------------------

        /// <summary>
        /// Rings the shaft with a split line at <paramref name="xCut"/> (metres). Recipe validated
        /// LIVE against SolidWorks (scratch SplitTest3, 2026-07-05):
        /// <list type="bullet">
        /// <item>Sketch on the front plane: construction centreline on the axis (coincident to the
        /// origin + horizontal) and ONE vertical line side to side exactly at the level's diameter.
        /// Manual relations only — ConstrainAll invented a spurious midpoint relation here. Fully
        /// defined via: vertical + endpoints SYMMETRIC about the centreline + top endpoint coincident
        /// with the upper silhouette + horizontal position dimension. (The lower silhouette is never
        /// selectable by point — symmetry replaces it.)</item>
        /// <item>Selection marks are REQUIRED: sketch with mark 4, face with mark 1 — with marks 0/0
        /// SolidWorks creates nothing.</item>
        /// <item>The face is picked at its FRONT point (x, 0, +r): the point (x, +r, 0) lies ON the
        /// silhouette edge and the FACE selection fails there.</item>
        /// <item><see cref="IModelDoc2.InsertSplitLineProject"/>(false, false) projects both ways →
        /// the full ring. The created feature's type name is <c>"PLine"</c> (localized name "Línea de
        /// partición"), NOT "SplitLine".</item>
        /// </list>
        /// Returns null on success or a Spanish error.
        /// </summary>
        private string InsertShaftSplitLine(IModelDoc2 model, double xCut, double radius, double faceX, double totalLength)
        {
            var sketchMgr = model.SketchManager;
            bool addToDbWas = sketchMgr.AddToDB, displayWas = sketchMgr.DisplayWhenAdded;
            try
            {
                model.ClearSelection2(true);
                var frontPlane = FirstRefPlane(model);
                if (frontPlane == null) return "No se encontró el plano frontal para la línea de división.";
                frontPlane.Select2(false, 0);
                sketchMgr.InsertSketch(true);

                sketchMgr.AddToDB = true;
                sketchMgr.DisplayWhenAdded = false;

                var segAxis = sketchMgr.CreateLine(0, 0, 0, totalLength, 0, 0);
                segAxis.ConstructionGeometry = true;
                var segCut = Line(sketchMgr, xCut, radius, xCut, -radius);

                model.ClearSelection2(true);
                segAxis.Select4(false, null);
                model.SketchAddConstraints("sgHORIZONTAL2D");
                CoincidentToOrigin(model, StartPt(segAxis));

                model.ClearSelection2(true);
                segCut.Select4(false, null);
                model.SketchAddConstraints("sgVERTICAL2D");

                model.ClearSelection2(true);
                StartPt(segCut).Select4(true, null);
                EndPt(segCut).Select4(true, null);
                segAxis.Select4(true, null);
                model.SketchAddConstraints("sgSYMMETRIC");

                CoincidentPointToSilhouetteEdge(model, StartPt(segCut), faceX, radius, 0);

                // Position dim origin → line (drives the cut's X).
                try
                {
                    model.ClearSelection2(true);
                    segCut.Select4(true, null);
                    model.Extension.SelectByID2("", "EXTSKETCHPOINT", 0, 0, 0, true, 0, null, (int)swSelectOption_e.swSelectOptionDefault);
                    model.AddHorizontalDimension2(xCut / 2.0, radius * 1.5, 0);
                }
                catch { }

                model.ClearSelection2(true);
                sketchMgr.InsertSketch(true);
            }
            finally
            {
                RestoreSketch(sketchMgr, addToDbWas, displayWas);
            }

            var sketchFeature = LastProfileFeature(model);
            if (sketchFeature == null) return "No se pudo crear el croquis de la línea de división.";

            model.ClearSelection2(true);
            sketchFeature.Select2(false, 4);                       // mark 4 = sketch to project
            bool gotFace = model.Extension.SelectByID2(
                "", "FACE", faceX, 0, radius, true, 1, null,       // mark 1 = faces to split, FRONT point
                (int)swSelectOption_e.swSelectOptionDefault);
            if (!gotFace) return "No se pudo seleccionar la cara cilíndrica para la línea de división.";

            model.InsertSplitLineProject(false, false); // void: verify via the tree below

            var last = model.Extension.GetLastFeatureAdded() as Feature;
            if (last == null || last.GetTypeName2() != "PLine")
            {
                return "SolidWorks no pudo crear la línea de división en x = " + (xCut * 1000.0) + " mm.";
            }
            model.ClearSelection2(true);
            return null;
        }

        // ---- 3) keyways (chavetas, DIN 6885 form A) ------------------------------------------------

        /// <summary>
        /// Cuts one keyway. Recipe validated LIVE (scratch KeyTest/KeyTest2, 2026-07-05):
        /// <list type="bullet">
        /// <item>Tangent plane = front plane offset by the reference RADIUS (goes to +Z unflipped).
        /// With a start angle: FIRST an angled plane — select front plane (append) + axis with MARK 1,
        /// <c>InsertRefPlane(Angle, θ, Coincident, 0, 0, 0)</c> (the axis-first pairing fails) — then
        /// offset that by the radius. Positive θ rotates +Z toward −Y.</item>
        /// <item>Slot sketch via <see cref="ISketchManager.CreateSketchSlot"/> (type line, FullLength,
        /// auto-dimensioned). Sketch X maps to model X on these planes (verified by floor-face probe).</item>
        /// <item>Cut = <c>FeatureCut4</c> with Sd=false (BOTH directions): dir1 Blind = depth into the
        /// shaft, dir2 ThroughAll = outward, so a key straddling a bigger level also removes the
        /// material above the reference surface.</item>
        /// <item>Pattern = axis mark 1 + seed cut mark 4 + <c>FeatureCircularPattern4(n, 2π, false,
        /// "NULL", false, true, false)</c>. The axis comes from the first two ref planes via
        /// <see cref="IModelDoc2.InsertAxis2"/> (created once, reused).</item>
        /// </list>
        /// Returns null on success or a Spanish error.
        /// </summary>
        private string CutKeyway(IModelDoc2 model, ShaftSpec spec, ShaftKeyway key, ref Feature shaftAxis)
        {
            const double mmToM = 0.001;
            var xs = spec.BoundariesMm();
            double x1 = key.StartXMm(xs[key.EdgeIndex]) * mmToM;
            double x2 = x1 + key.LengthMm * mmToM;
            double refRadius = key.RefDiameterMm * mmToM / 2.0;

            double angleRad = key.AngleDeg * Math.PI / 180.0;
            angleRad = angleRad % (2.0 * Math.PI);
            if (angleRad < 0) angleRad += 2.0 * Math.PI;
            bool hasAngle = Math.Abs(angleRad) > 1e-9;

            if ((hasAngle || key.Count > 1) && shaftAxis == null)
            {
                string axisErr = EnsureShaftAxis(model, out shaftAxis);
                if (axisErr != null) return axisErr;
            }

            // -- tangent plane --
            var frontPlane = FirstRefPlane(model);
            if (frontPlane == null) return "no se encontró el plano frontal.";
            Feature offsetBase = frontPlane;
            if (hasAngle)
            {
                model.ClearSelection2(true);
                ((Entity)frontPlane).Select4(true, null);
                shaftAxis.Select2(true, 1);
                var angled = model.FeatureManager.InsertRefPlane(
                    (int)swRefPlaneReferenceConstraints_e.swRefPlaneReferenceConstraint_Angle, angleRad,
                    (int)swRefPlaneReferenceConstraints_e.swRefPlaneReferenceConstraint_Coincident, 0, 0, 0) as Feature;
                if (angled == null) return "SolidWorks no pudo crear el plano con ángulo.";
                offsetBase = angled;
            }
            model.ClearSelection2(true);
            ((Entity)offsetBase).Select4(false, null);
            var tangent = model.FeatureManager.InsertRefPlane(
                (int)swRefPlaneReferenceConstraints_e.swRefPlaneReferenceConstraint_Distance, refRadius, 0, 0, 0, 0) as Feature;
            if (tangent == null) return "SolidWorks no pudo crear el plano tangente.";

            // -- slot sketch --
            var sketchMgr = model.SketchManager;
            bool addToDbWas = sketchMgr.AddToDB, displayWas = sketchMgr.DisplayWhenAdded;
            try
            {
                model.ClearSelection2(true);
                ((Entity)tangent).Select4(false, null);
                sketchMgr.InsertSketch(true);
                sketchMgr.AddToDB = true;
                sketchMgr.DisplayWhenAdded = false;

                var slot = sketchMgr.CreateSketchSlot(
                    (int)swSketchSlotCreationType_e.swSketchSlotCreationType_line,
                    (int)swSketchSlotLengthType_e.swSketchSlotLengthType_FullLength,
                    key.WidthMm * mmToM,
                    x1, 0, 0, x2, 0, 0, 0, 0, 0, 1, true);
                if (slot == null) return "no se pudo crear la ranura de la chaveta en el croquis.";

                // Fully define the slot (validated live, SlotDef 2026-07-05): the auto-dims cover
                // width/length but the slot floats. Its CENTRE point (GetCenterPointHandle) pins both:
                //  · Y — horizontal alignment centre ↔ sketch origin (the origin projects onto the
                //    axis on every tangent plane, straight or angled);
                //  · X — horizontal dim from the reference edge circle to the centre (mirrors the UI
                //    cota); if that edge pick fails, fall back to a dim from the origin.
                var slotCenter = slot.GetCenterPointHandle();
                if (slotCenter != null)
                {
                    model.ClearSelection2(true);
                    bool gotCenter = slotCenter.Select4(true, null);
                    bool gotOrigin = model.Extension.SelectByID2(
                        "", "EXTSKETCHPOINT", 0, 0, 0, true, 0, null, (int)swSelectOption_e.swSelectOptionDefault);
                    if (gotCenter && gotOrigin) model.SketchAddConstraints("sgHORIZONTALPOINTS2D");

                    // Reference edge circle: at an internal boundary the step ring has two circles;
                    // the SMALLER radius one always exists (also the split-line ring when Ø repeats).
                    double dLeft = key.EdgeIndex > 0 ? spec.Levels[key.EdgeIndex - 1].DiameterMm : spec.Levels[0].DiameterMm;
                    double dRight = key.EdgeIndex < spec.Levels.Count ? spec.Levels[key.EdgeIndex].DiameterMm : spec.Levels[spec.Levels.Count - 1].DiameterMm;
                    double rEdge = Math.Min(dLeft, dRight) * mmToM / 2.0;
                    double edgeX = xs[key.EdgeIndex] * mmToM;

                    model.ClearSelection2(true);
                    bool gotEdge = model.Extension.SelectByID2(
                        "", "EDGE", edgeX, rEdge, 0, true, 0, null, (int)swSelectOption_e.swSelectOptionDefault);
                    object posDim = null;
                    if (gotEdge && slotCenter.Select4(true, null))
                    {
                        posDim = model.AddHorizontalDimension2((x1 + x2) / 2.0, refRadius * 2.5, 0);
                    }
                    if (posDim == null)
                    {
                        model.ClearSelection2(true);
                        slotCenter.Select4(true, null);
                        model.Extension.SelectByID2(
                            "", "EXTSKETCHPOINT", 0, 0, 0, true, 0, null, (int)swSelectOption_e.swSelectOptionDefault);
                        model.AddHorizontalDimension2((x1 + x2) / 2.0, refRadius * 3.0, 0);
                    }
                }

                model.ClearSelection2(true);
                sketchMgr.InsertSketch(true);
            }
            finally
            {
                RestoreSketch(sketchMgr, addToDbWas, displayWas);
            }

            // -- cut: dir1 blind (depth, into the shaft) + dir2 through-all (outward) --
            var sketchFeature = LastProfileFeature(model);
            if (sketchFeature == null) return "no se encontró el croquis de la chaveta.";

            // CreateSketchSlot's auto-dims (width/length) are born DRIVEN while the
            // swAddDrivenDimensions toggle is on, leaving the slot under-defined.
            ForceDrivingDims(sketchFeature);

            model.ClearSelection2(true);
            sketchFeature.Select2(false, 0);
            var cut = model.FeatureManager.FeatureCut4(
                false, false, false,
                (int)swEndConditions_e.swEndCondBlind,
                (int)swEndConditions_e.swEndCondThroughAll,
                key.DepthMm * mmToM, 0,
                false, false, false, false, 0, 0,
                false, false, false, false,
                false, false, true, false, false, false,
                (int)swStartConditions_e.swStartSketchPlane, 0, false, false);
            if (cut == null) return "SolidWorks no pudo crear el corte de la chaveta.";

            // -- circular pattern --
            if (key.Count > 1)
            {
                model.ClearSelection2(true);
                shaftAxis.Select2(false, 1);
                cut.Select2(true, 4);
                var pattern = model.FeatureManager.FeatureCircularPattern4(
                    key.Count, 2.0 * Math.PI, false, "NULL", false, true, false);
                if (pattern == null) return "SolidWorks no pudo crear el patrón polar de la chaveta.";
            }

            model.ClearSelection2(true);
            return null;
        }

        // ---- 4) retaining-ring grooves (ranuras DIN 471) --------------------------------------------

        /// <summary>
        /// Cuts one retaining-ring groove: rectangular notch (level radius down to D3/2, width E1)
        /// sketched on the front plane and removed with a 360° cut-revolve — same recipe as the
        /// bolt's <c>CutGroove</c>. Position dim = reference edge ring → the wall the offset sign
        /// points at (mirrors the UI cota); falls back to a dim from the origin when the edge pick
        /// fails. Returns null on success or a Spanish error.
        /// </summary>
        private string CutShaftGroove(IModelDoc2 model, ShaftSpec spec, ShaftGroove groove)
        {
            const double mmToM = 0.001;
            var xs = spec.BoundariesMm();
            double x1Mm = groove.StartXMm(xs[groove.EdgeIndex]);
            double x2Mm = x1Mm + groove.WidthMm;
            double surfaceD = spec.GrooveSurfaceDiameterMm(groove);
            if (!(surfaceD > 0)) return "la ranura no cae en un único nivel.";

            double x1 = x1Mm * mmToM, x2 = x2Mm * mmToM;
            double rSurf = surfaceD * mmToM / 2.0;
            double r3 = groove.BottomDiameterMm * mmToM / 2.0;
            double total = spec.TotalLengthMm * mmToM;

            // Probe point ON the same level surface but OUTSIDE the notch (like the bolt, which
            // probes the shank before the groove): picking at the notch itself could grab the
            // sketch lines instead of the model silhouette. Widest gap wins.
            int levelIdx = 0;
            for (int i = 0; i < spec.Levels.Count; i++)
            {
                if (xs[i] < x1Mm + ShaftSpec.PositionToleranceMm) levelIdx = i;
            }
            double gapL = x1Mm - xs[levelIdx], gapR = xs[levelIdx + 1] - x2Mm;
            double probeX = (gapL >= gapR ? xs[levelIdx] + gapL / 2.0 : x2Mm + gapR / 2.0) * mmToM;

            var sketchMgr = model.SketchManager;
            bool addToDbWas = sketchMgr.AddToDB, displayWas = sketchMgr.DisplayWhenAdded;
            try
            {
                model.ClearSelection2(true);
                var frontPlane = FirstRefPlane(model);
                if (frontPlane == null) return "no se encontró el plano frontal.";
                frontPlane.Select2(false, 0);
                sketchMgr.InsertSketch(true);

                sketchMgr.AddToDB = true;
                sketchMgr.DisplayWhenAdded = false;

                // Construction axis (for the diameter dimension) + the closed rectangle of the notch.
                var segCl = sketchMgr.CreateLine(0, 0, 0, total, 0, 0);
                segCl.ConstructionGeometry = true;
                var segLeftWall = Line(sketchMgr, x1, rSurf, x1, r3);
                var segBottom = Line(sketchMgr, x1, r3, x2, r3);
                Line(sketchMgr, x2, r3, x2, rSurf);                   // right wall
                var segTop = Line(sketchMgr, x2, rSurf, x1, rSurf);

                // In-sketch relations first, then anchor to the existing body (same as the bolt groove).
                ConstrainAll(sketchMgr);
                CoincidentToOrigin(model, StartPt(segCl));
                CoincidentPointToEdge(model, EndPt(segCl), total, 0, 0);
                CoincidentPointToSilhouetteEdge(model, StartPt(segTop), probeX, rSurf, 0);
                CoincidentPointToEdge(model, EndPt(segTop), probeX, rSurf, 0);

                const double off = 0.012;
                double xMid = (x1 + x2) / 2.0;

                // Position: reference edge ring (smaller adjacent radius always exists, also on split
                // rings) → the LEFT wall, mirroring the UI cota.
                double dLeft = groove.EdgeIndex > 0 ? spec.Levels[groove.EdgeIndex - 1].DiameterMm : spec.Levels[0].DiameterMm;
                double dRight = groove.EdgeIndex < spec.Levels.Count ? spec.Levels[groove.EdgeIndex].DiameterMm : spec.Levels[spec.Levels.Count - 1].DiameterMm;
                double rEdge = Math.Min(dLeft, dRight) * mmToM / 2.0;
                double edgeX = xs[groove.EdgeIndex] * mmToM;

                model.ClearSelection2(true);
                bool gotEdge = model.Extension.SelectByID2(
                    "", "EDGE", edgeX, rEdge, 0, true, 0, null, (int)swSelectOption_e.swSelectOptionDefault);
                object posDim = null;
                if (gotEdge && segLeftWall.Select4(true, null))
                {
                    posDim = model.AddHorizontalDimension2((edgeX + xMid) / 2.0, rSurf + 2.0 * off, 0);
                }
                if (posDim == null)
                {
                    model.ClearSelection2(true);
                    segLeftWall.Select4(true, null);
                    model.Extension.SelectByID2("", "EXTSKETCHPOINT", 0, 0, 0, true, 0, null, (int)swSelectOption_e.swSelectOptionDefault);
                    model.AddHorizontalDimension2((edgeX + xMid) / 2.0, rSurf + 2.0 * off, 0);
                }

                LengthDim(model, segTop, xMid, rSurf + 3.0 * off);          // E1 (width)
                DiameterDim(model, segBottom, segCl, xMid, -r3);            // D3 (bottom Ø)

                model.ClearSelection2(true);
                sketchMgr.InsertSketch(true);

                var sketchFeature = LastProfileFeature(model);
                if (sketchFeature == null) return "no se pudo crear el croquis de la ranura.";
                ForceDrivingDims(sketchFeature);

                model.ClearSelection2(true);
                sketchFeature.Select2(false, 0);

                // 360° cut-revolve (IsCut = true), merged — same call as the bolt groove.
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

        // ---- 5) DIN 509-E undercuts (entalladuras) ---------------------------------------------------

        /// <summary>
        /// Cuts one DIN 509 form E undercut at a diameter-change shoulder: closed profile on the
        /// front plane removed with a 360° cut-revolve (same call as the grooves). Half-section,
        /// with the small level LEFT of the shoulder (mirrored otherwise): 15° ramp from the surface
        /// at f, flat bottom at depth t1, corner arc of radius r tangent to the bottom AND to the
        /// shoulder face — the tangency lands at r − t1 ABOVE the small surface, so the profile also
        /// eats the sharp corner (that relief is the whole point of the norm). Anchored to the model
        /// by the shoulder corner (its ring edge is intact: grooves cannot touch diameter changes and
        /// keyways are cut later) plus a silhouette coincidence on the free surface. Returns null on
        /// success or a Spanish error.
        /// </summary>
        private string CutShaftUndercut(IModelDoc2 model, ShaftSpec spec, ShaftUndercut undercut)
        {
            const double mmToM = 0.001;
            var xs = spec.BoundariesMm();
            bool smallLeft = spec.UndercutSmallSideIsLeft(undercut);
            double sign = smallLeft ? -1.0 : 1.0;

            double z1Mm, z2Mm, smallDMm, segStartMm, segEndMm;
            spec.UndercutZoneMm(undercut, out z1Mm, out z2Mm, out smallDMm);
            spec.UndercutSegmentMm(undercut, out segStartMm, out segEndMm);

            double xS = xs[undercut.BoundaryIndex] * mmToM;
            double rs = smallDMm * mmToM / 2.0;
            double r = undercut.RadiusMm * mmToM;
            double t1 = undercut.DepthMm * mmToM;
            double f = undercut.WidthMm * mmToM;
            double ramp = t1 / Math.Tan(ShaftSpec.UndercutRunOutDeg * Math.PI / 180.0);
            double total = spec.TotalLengthMm * mmToM;

            // Silhouette probe on the same continuous surface but OUTSIDE every notch already (or
            // about to be) cut into it: other undercut zones and groove spans. Widest gap wins.
            double probeX = UndercutProbeXMm(spec, segStartMm, segEndMm) * mmToM;

            var sketchMgr = model.SketchManager;
            bool addToDbWas = sketchMgr.AddToDB, displayWas = sketchMgr.DisplayWhenAdded;
            try
            {
                model.ClearSelection2(true);
                var frontPlane = FirstRefPlane(model);
                if (frontPlane == null) return "no se encontró el plano frontal.";
                frontPlane.Select2(false, 0);
                sketchMgr.InsertSketch(true);

                sketchMgr.AddToDB = true;
                sketchMgr.DisplayWhenAdded = false;

                var segCl = sketchMgr.CreateLine(0, 0, 0, total, 0, 0);
                segCl.ConstructionGeometry = true;

                // P5 shoulder corner → P1 surface → P2 ramp end → P3 arc start → (arc) → P4 on the
                // shoulder face → back down to P5.
                double x1 = xS + sign * f;                  // P1: run-out reaches the surface
                double x2 = xS + sign * (f - ramp);         // P2: ramp meets the flat bottom
                double x3 = xS + sign * r;                  // P3: bottom meets the corner arc
                double yBottom = rs - t1;
                double yTangent = rs - t1 + r;              // P4: tangency height on the face

                var segSurf = Line(sketchMgr, xS, rs, x1, rs);
                var segRamp = Line(sketchMgr, x1, rs, x2, yBottom);
                var segBottom = Line(sketchMgr, x2, yBottom, x3, yBottom);
                var arc = sketchMgr.CreateArc(
                    x3, yTangent, 0,                        // centre (above P3 by r)
                    x3, yBottom, 0,                         // start = P3
                    xS, yTangent, 0,                        // end   = P4
                    (short)(smallLeft ? 1 : -1));           // quarter bulging toward the corner
                if (arc == null) return "no se pudo crear el arco de la entalladura.";
                var segFace = Line(sketchMgr, xS, yTangent, xS, rs);

                ConstrainAll(sketchMgr);
                CoincidentToOrigin(model, StartPt(segCl));
                CoincidentPointToEdge(model, EndPt(segCl), total, 0, 0);
                // Shoulder corner = the smaller ring of the step edge, picked at its top point.
                CoincidentPointToEdge(model, EndPt(segFace), xS, rs, 0);
                CoincidentPointToSilhouetteEdge(model, EndPt(segSurf), probeX, rs, 0);

                const double off = 0.012;
                // f: shoulder corner ↔ run-out end, mirroring the norm's width dimension.
                model.ClearSelection2(true);
                StartPt(segSurf).Select4(true, null);
                EndPt(segSurf).Select4(true, null);
                model.AddHorizontalDimension2((xS + x1) / 2.0, rs + 2.0 * off, 0);
                DiameterDim(model, segBottom, segCl, (x2 + x3) / 2.0, -yBottom);   // bottom Ø = d − 2·t1

                model.ClearSelection2(true);
                sketchMgr.InsertSketch(true);

                var sketchFeature = LastProfileFeature(model);
                if (sketchFeature == null) return "no se pudo crear el croquis de la entalladura.";
                ForceDrivingDims(sketchFeature);

                model.ClearSelection2(true);
                sketchFeature.Select2(false, 0);

                // 360° cut-revolve, same call as the grooves.
                var cut = model.FeatureManager.FeatureRevolve2(
                    true, true, false, true, false, true,
                    (int)swEndConditions_e.swEndCondBlind, (int)swEndConditions_e.swEndCondBlind,
                    2.0 * Math.PI, 0.0, false, false, 0.0, 0.0,
                    (int)swThinWallType_e.swThinWallOneDirection, 0.0, 0.0,
                    true, false, true);
                if (cut == null) return "SolidWorks no pudo crear el corte de la entalladura.";
                return null;
            }
            finally
            {
                RestoreSketch(sketchMgr, addToDbWas, displayWas);
            }
        }

        /// <summary>
        /// X (mm) of the widest untouched stretch of the undercut's surface segment: the segment
        /// minus every undercut zone and groove span that intersects it. Falls back to the segment
        /// midpoint (cannot happen with a valid spec, which guarantees free surface).
        /// </summary>
        private static double UndercutProbeXMm(ShaftSpec spec, double segStart, double segEnd)
        {
            var walls = new List<double[]>();
            foreach (var other in spec.Undercuts)
            {
                if (other == null || other.BoundaryIndex < 1 || other.BoundaryIndex > spec.Levels.Count - 1) continue;
                double a, b, d;
                spec.UndercutZoneMm(other, out a, out b, out d);
                if (b > segStart && a < segEnd) walls.Add(new[] { a, b });
            }
            if (spec.Grooves != null)
            {
                var xs = spec.BoundariesMm();
                foreach (var groove in spec.Grooves)
                {
                    if (groove == null || groove.EdgeIndex < 0 || groove.EdgeIndex > spec.Levels.Count) continue;
                    double a = groove.StartXMm(xs[groove.EdgeIndex]);
                    double b = a + groove.WidthMm;
                    if (b > segStart && a < segEnd) walls.Add(new[] { a, b });
                }
            }
            walls.Sort((p, q) => p[0].CompareTo(q[0]));

            double bestX = (segStart + segEnd) / 2.0, bestGap = 0, cursor = segStart;
            foreach (var wall in walls)
            {
                double gap = Math.Min(wall[0], segEnd) - cursor;
                if (gap > bestGap) { bestGap = gap; bestX = cursor + gap / 2.0; }
                cursor = Math.Max(cursor, wall[1]);
            }
            double tail = segEnd - cursor;
            if (tail > bestGap) { bestX = cursor + tail / 2.0; }
            return bestX;
        }

        /// <summary>
        /// Flips every DRIVEN dimension of a sketch feature back to DRIVING. Dimensions added while
        /// the swAddDrivenDimensions toggle is on (e.g. CreateSketchSlot's auto-dims) are born driven
        /// and would leave the sketch under-defined.
        /// </summary>
        private static void ForceDrivingDims(Feature sketchFeature)
        {
            var dispDim = sketchFeature.GetFirstDisplayDimension() as DisplayDimension;
            while (dispDim != null)
            {
                var dim = dispDim.GetDimension2(0);
                if (dim != null && dim.DrivenState == (int)swDimensionDrivenState_e.swDimensionDriven)
                {
                    dim.DrivenState = (int)swDimensionDrivenState_e.swDimensionDriving;
                }
                dispDim = sketchFeature.GetNextDisplayDimension(dispDim) as DisplayDimension;
            }
        }

        /// <summary>
        /// Finds or creates the shaft axis (intersection of the first two ref planes, both containing
        /// the revolve axis). Created once per build and reused by every keyway.
        /// </summary>
        private string EnsureShaftAxis(IModelDoc2 model, out Feature axis)
        {
            axis = FindLastFeatureOfType(model, "RefAxis");
            if (axis != null) return null;

            Feature first = null, second = null;
            var feature = model.FirstFeature() as Feature;
            while (feature != null && second == null)
            {
                if (feature.GetTypeName2() == "RefPlane")
                {
                    if (first == null) first = feature;
                    else second = feature;
                }
                feature = feature.GetNextFeature() as Feature;
            }
            if (first == null || second == null) return "no se encontraron los planos para crear el eje de referencia.";

            model.ClearSelection2(true);
            ((Entity)first).Select4(true, null);
            ((Entity)second).Select4(true, null);
            model.InsertAxis2(true);
            axis = FindLastFeatureOfType(model, "RefAxis");
            return axis == null ? "SolidWorks no pudo crear el eje de referencia." : null;
        }

        private static Feature FindLastFeatureOfType(IModelDoc2 model, string typeName)
        {
            Feature found = null;
            var feature = model.FirstFeature() as Feature;
            while (feature != null)
            {
                if (feature.GetTypeName2() == typeName) found = feature;
                feature = feature.GetNextFeature() as Feature;
            }
            return found;
        }
    }
}
