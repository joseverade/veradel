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

                // A failed feature operation does NOT abort the build: it is skipped, the rest is
                // still cut, and every skip is reported at the end via result.Warning.
                var skipped = new List<string>();

                // Grooves BEFORE keyways: their position dims reference the boundary edge rings,
                // which an angled keyway cut could otherwise have carved away.
                var grooves = spec.Grooves ?? new List<ShaftGroove>();
                int groovesOk = 0;
                for (int g = 0; g < grooves.Count; g++)
                {
                    err = CutShaftGroove(model, spec, grooves[g]);
                    if (err != null) skipped.Add("Ranura " + (g + 1) + ": " + err);
                    else groovesOk++;
                }

                // Undercuts AFTER grooves (a groove never touches a diameter-change boundary, so
                // every shoulder ring is still intact here) and BEFORE keyways (an overlapping
                // keyway cut is blocked by validation, but a straddling one could still eat the
                // shoulder ring the undercut anchors to).
                var undercuts = spec.Undercuts ?? new List<ShaftUndercut>();
                int undercutsOk = 0;
                for (int u = 0; u < undercuts.Count; u++)
                {
                    err = CutShaftUndercut(model, spec, undercuts[u]);
                    if (err != null) skipped.Add("Entalladura " + (u + 1) + ": " + err);
                    else undercutsOk++;
                }

                Feature shaftAxis = null;
                var keyways = spec.Keyways ?? new List<ShaftKeyway>();
                int keywaysOk = 0;
                for (int k = 0; k < keyways.Count; k++)
                {
                    err = CutKeyway(model, spec, keyways[k], ref shaftAxis);
                    if (err != null) skipped.Add("Chaveta " + (k + 1) + ": " + err);
                    else keywaysOk++;
                }

                // Centre points last: coaxial cut-revolves on the end faces, independent of every
                // other feature (the faces are untouched by keyways/grooves/undercuts).
                var centerHoles = spec.CenterHoles ?? new List<ShaftCenterHole>();
                int centerHolesOk = 0;
                for (int c = 0; c < centerHoles.Count; c++)
                {
                    err = CutShaftCenterHole(model, spec, centerHoles[c]);
                    if (err != null) skipped.Add("Punto de centrado " + (c + 1) + ": " + err);
                    else centerHolesOk++;
                }

                model.ClearSelection2(true);
                model.EditRebuild3();
                model.ViewZoomtofit2();

                result.Success = true;
                result.LevelCount = spec.Levels.Count;
                result.TotalLengthMm = spec.TotalLengthMm;
                result.SplitLineCount = splits;
                result.KeywayCount = keywaysOk;
                result.GrooveCount = groovesOk;
                result.UndercutCount = undercutsOk;
                result.CenterHoleCount = centerHolesOk;
                result.Warning = skipped.Count > 0 ? string.Join("\n", skipped.ToArray()) : null;
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
        /// <item>Slot sketch via <see cref="ISketchManager.CreateSketchSlot"/> (type line). The two
        /// points are the ARC CENTRES regardless of the length-type flag (the old code passed the
        /// extremes and every key came out b too long), so pass x1 + b/2 and x2 − b/2 and add the
        /// dimensions manually (no auto-dims): width = the two slot lines, length = the two arcs with
        /// <c>SetArcEndCondition(Max, Max)</c> → measured extreme to extreme (tangency to tangency).
        /// Sketch X maps to model X on these planes (verified by floor-face probe).</item>
        /// <item>Position: normally a dim from the reference edge ring to the LEFT arc with the arc
        /// condition at its LEFT extreme (Min when the key is right of the edge, Max otherwise) —
        /// mirrors the UI cota. With <see cref="ShaftKeyway.CenterArc"/> 1/2 the LEFT/RIGHT arc
        /// CENTRE is made coincident with the edge ring instead (the ring projects onto the sketch
        /// as the vertical line x = edge), no position dim, and the length dim runs anchored
        /// centre → opposite extreme.</item>
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
            double x2 = x1 + key.SpanMm * mmToM;
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

                // The two slot points are the ARC CENTRES (SolidWorks ignores the length-type flag
                // for the geometry): overall extreme-to-extreme length = key.LengthMm. No auto-dims —
                // they are anchored centre-to-centre; ours below measure what the user entered.
                double halfB = key.WidthMm * mmToM / 2.0;
                var slot = sketchMgr.CreateSketchSlot(
                    (int)swSketchSlotCreationType_e.swSketchSlotCreationType_line,
                    (int)swSketchSlotLengthType_e.swSketchSlotLengthType_FullLength,
                    key.WidthMm * mmToM,
                    x1 + halfB, 0, 0, x2 - halfB, 0, 0, 0, 0, 0, 1, false);
                if (slot == null) return "no se pudo crear la ranura de la chaveta en el croquis.";

                // The slot is exactly 2 lines + 2 arcs and it is alone in this sketch: enumerate the
                // segments to reach them (ISketchSlot exposes no segment accessor).
                SketchSegment lineA = null, lineB = null, arcLeft = null, arcRight = null;
                SketchPoint arcLeftCenter = null, arcRightCenter = null;
                var sketchObjs = sketchMgr.ActiveSketch == null ? null : sketchMgr.ActiveSketch.GetSketchSegments() as object[];
                double bestLeft = double.MaxValue, bestRight = double.MinValue;
                if (sketchObjs != null)
                {
                    foreach (var rawSeg in sketchObjs)
                    {
                        var seg = rawSeg as SketchSegment;
                        if (seg == null) continue;
                        if (seg.GetType() == (int)swSketchSegments_e.swSketchLINE)
                        {
                            if (seg.ConstructionGeometry) continue;
                            if (lineA == null) lineA = seg; else lineB = seg;
                        }
                        else if (seg.GetType() == (int)swSketchSegments_e.swSketchARC)
                        {
                            var center = ((SketchArc)seg).GetCenterPoint2() as SketchPoint;
                            if (center == null) continue;
                            if (center.X < bestLeft) { bestLeft = center.X; arcLeft = seg; arcLeftCenter = center; }
                            if (center.X > bestRight) { bestRight = center.X; arcRight = seg; arcRightCenter = center; }
                        }
                    }
                }

                // COTAS PRIMERO, driving en el acto (MakeDriving): only then are the in-sketch
                // fully-defined/broken checks below meaningful, and a later relation that fights a
                // dimension shows up as invalid instead of silently moving the geometry.
                // Width b: distance between the two slot lines.
                if (lineA != null && lineB != null)
                {
                    model.ClearSelection2(true);
                    lineA.Select4(true, null);
                    lineB.Select4(true, null);
                    MakeDriving(model.AddVerticalDimension2(x1 - 0.008, 0, 0));
                }
                // Length l — arc conditions per anchor mode: cota mode measures the outer extremes
                // (Max/Max, tangency to tangency); a centre-anchored mode measures from the anchored
                // arc CENTRE to the opposite EXTREME (that is the l the user typed).
                if (arcLeft != null && arcRight != null)
                {
                    model.ClearSelection2(true);
                    arcLeft.Select4(true, null);
                    arcRight.Select4(true, null);
                    var lenDim = model.AddHorizontalDimension2((x1 + x2) / 2.0, -(key.WidthMm * mmToM + 0.008), 0) as DisplayDimension;
                    var len = lenDim == null ? null : lenDim.GetDimension2(0);
                    if (len != null)
                    {
                        len.SetArcEndCondition(0, key.CenterArc == 1
                            ? (int)swArcEndCondition_e.swArcEndConditionCenter
                            : (int)swArcEndCondition_e.swArcEndConditionMax);
                        len.SetArcEndCondition(1, key.CenterArc == 2
                            ? (int)swArcEndCondition_e.swArcEndConditionCenter
                            : (int)swArcEndCondition_e.swArcEndConditionMax);
                    }
                    MakeDriving(lenDim);
                }

                // Fully define the slot (validated live, SlotDef 2026-07-05): with width/length
                // dimensioned the slot still floats. Its CENTRE point (GetCenterPointHandle) pins Y —
                // horizontal alignment centre ↔ sketch origin (the origin projects onto the axis on
                // every tangent plane, straight or angled). X is pinned below per position mode.
                var slotCenter = slot.GetCenterPointHandle();
                if (slotCenter != null && !SketchFullyDefined(sketchMgr))
                {
                    model.ClearSelection2(true);
                    bool gotCenter = slotCenter.Select4(true, null);
                    bool gotOrigin = model.Extension.SelectByID2(
                        "", "EXTSKETCHPOINT", 0, 0, 0, true, 0, null, (int)swSelectOption_e.swSelectOptionDefault);
                    if (gotCenter && gotOrigin) AddConstraintGuarded(model, sketchMgr, "sgHORIZONTALPOINTS2D");
                }

                // Reference edge circle: at an internal boundary the step ring has two circles;
                // the SMALLER radius one always exists (also the split-line ring when Ø repeats).
                double dLeft = key.EdgeIndex > 0 ? spec.Levels[key.EdgeIndex - 1].DiameterMm : spec.Levels[0].DiameterMm;
                double dRight = key.EdgeIndex < spec.Levels.Count ? spec.Levels[key.EdgeIndex].DiameterMm : spec.Levels[spec.Levels.Count - 1].DiameterMm;
                double rEdge = Math.Min(dLeft, dRight) * mmToM / 2.0;
                double edgeX = xs[key.EdgeIndex] * mmToM;

                bool positioned = false;
                if (key.CenterArc != 0)
                {
                    // Locating key: the anchored arc CENTRE sits ON the step ring. The ring projects
                    // onto the tangent plane as the vertical line x = edge, so a coincident relation
                    // pins X exactly there (Y is already tied to the axis). No position dim. Guarded:
                    // if the relation twists the slot into invalid geometry it is rolled back and the
                    // fallback dimension below positions the slot instead.
                    var anchor = key.CenterArc == 1 ? arcLeftCenter : arcRightCenter;
                    positioned = CoincidentPointToEdgeGuarded(model, sketchMgr, anchor, edgeX, rEdge, 0);
                }
                else
                {
                    // Position dim edge ring → LEFT arc at its LEFT extreme (tangency), mirroring the
                    // UI cota (edge → left extreme). Min when the key is right of the edge, Max when
                    // it is left of (or straddles) it — both resolve to the LEFT extreme.
                    model.ClearSelection2(true);
                    bool gotEdge = model.Extension.SelectByID2(
                        "", "EDGE", edgeX, rEdge, 0, true, 0, null, (int)swSelectOption_e.swSelectOptionDefault);
                    if (gotEdge && arcLeft != null && arcLeft.Select4(true, null))
                    {
                        var posDim = model.AddHorizontalDimension2((edgeX + x1) / 2.0, refRadius * 2.5, 0) as DisplayDimension;
                        var pos = posDim == null ? null : posDim.GetDimension2(0);
                        if (pos != null)
                        {
                            pos.SetArcEndCondition(0, (int)swArcEndCondition_e.swArcEndConditionCenter);
                            pos.SetArcEndCondition(1, key.OffsetMm > 0
                                ? (int)swArcEndCondition_e.swArcEndConditionMin
                                : (int)swArcEndCondition_e.swArcEndConditionMax);
                        }
                        MakeDriving(posDim);
                        positioned = posDim != null;
                    }
                }
                // Stop condition: no fallback when the sketch is already fully defined (adding more
                // would only over-define it).
                if (!positioned && slotCenter != null && !SketchFullyDefined(sketchMgr))
                {
                    // Fallback: dim origin → slot centre (value from the exact geometry).
                    model.ClearSelection2(true);
                    slotCenter.Select4(true, null);
                    model.Extension.SelectByID2(
                        "", "EXTSKETCHPOINT", 0, 0, 0, true, 0, null, (int)swSelectOption_e.swSelectOptionDefault);
                    MakeDriving(model.AddHorizontalDimension2((x1 + x2) / 2.0, refRadius * 3.0, 0));
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

            // The width/length/position dims above are born DRIVEN while the
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
        /// bolt's <c>CutGroove</c>. The rectangle TOP is coincident with the level's silhouette
        /// (outer Ø = the level Ø, no extra diameter dim), so it follows the surface if the level
        /// Ø is edited later. Position dim = reference edge ring → the wall the offset sign points
        /// at (positive = left wall, negative = right wall, mirroring the UI cota); falls back to
        /// a dim from the origin when the edge pick fails. Returns null on success or a Spanish
        /// error.
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
                var segRightWall = Line(sketchMgr, x2, r3, x2, rSurf);
                var segTop = Line(sketchMgr, x2, rSurf, x1, rSurf);

                // In-sketch relations first, then anchor to the existing body: the rectangle TOP
                // sits ON the level silhouette (outer Ø = level Ø), same as the bolt groove.
                ConstrainAll(sketchMgr);
                CoincidentToOrigin(model, StartPt(segCl));
                CoincidentPointToEdge(model, EndPt(segCl), total, 0, 0);
                CoincidentPointToSilhouetteEdge(model, StartPt(segTop), probeX, rSurf, 0);
                CoincidentPointToEdge(model, EndPt(segTop), probeX, rSurf, 0);

                const double off = 0.012;
                double xMid = (x1 + x2) / 2.0;

                // Position: reference edge ring (smaller adjacent radius always exists, also on split
                // rings) → the wall the offset sign points at (positive = LEFT wall, negative =
                // RIGHT wall), mirroring the UI cota.
                double dLeft = groove.EdgeIndex > 0 ? spec.Levels[groove.EdgeIndex - 1].DiameterMm : spec.Levels[0].DiameterMm;
                double dRight = groove.EdgeIndex < spec.Levels.Count ? spec.Levels[groove.EdgeIndex].DiameterMm : spec.Levels[spec.Levels.Count - 1].DiameterMm;
                double rEdge = Math.Min(dLeft, dRight) * mmToM / 2.0;
                double edgeX = xs[groove.EdgeIndex] * mmToM;
                var posWall = groove.OffsetMm < 0 ? segRightWall : segLeftWall;

                model.ClearSelection2(true);
                bool gotEdge = model.Extension.SelectByID2(
                    "", "EDGE", edgeX, rEdge, 0, true, 0, null, (int)swSelectOption_e.swSelectOptionDefault);
                object posDim = null;
                if (gotEdge && posWall.Select4(true, null))
                {
                    posDim = model.AddHorizontalDimension2((edgeX + xMid) / 2.0, rSurf + 2.0 * off, 0);
                }
                if (posDim == null)
                {
                    model.ClearSelection2(true);
                    posWall.Select4(true, null);
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

        // ---- 5) DIN 509 undercuts (entalladuras, forms E and F) --------------------------------------

        /// <summary>
        /// Cuts one DIN 509 undercut (form E or F) at a diameter-change shoulder: STRAIGHT-LINE
        /// closed profile on the front plane removed with a 360° cut-revolve (same call as the
        /// grooves) — NO rounds in the sketch — then the corner radii r applied as a single FILLET
        /// FEATURE on the resulting BOTTOM FACE (the smallest-diameter cylindrical face of the
        /// relief), which rounds every ring edge bounding it. Half-section, with the small level
        /// LEFT of the shoulder (mirrored otherwise). Form E: 15° ramp from the surface at f, flat
        /// bottom at depth t1 running to the shoulder face, back up the face. Form F: the flat
        /// bottom continues t2 PAST the shoulder face and runs out at 8° up the face (DIN 509 face
        /// relief), exiting on the face at t2/tan 8° − t1 above the small surface. Anchored to the
        /// model by the shoulder corner (its ring edge is intact: grooves cannot touch diameter
        /// changes and keyways are cut later) plus a silhouette coincidence on the free surface.
        /// Returns null on success or a Spanish error.
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
            double t2 = undercut.IsFormF ? undercut.Depth2Mm * mmToM : 0.0;
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

                // Straight lines only (rounds come later as a fillet feature on the bottom face):
                // shoulder corner → P1 surface → P2 ramp end → flat bottom. Form E: bottom stops
                // at the shoulder face and closes straight up it. Form F: bottom continues t2 past
                // the face, runs out at 8° up the face to yExit, and closes up the face from there.
                double x1 = xS + sign * f;                  // P1: run-out reaches the surface
                double x2 = xS + sign * (f - ramp);         // P2: ramp meets the flat bottom
                double yBottom = rs - t1;
                bool isF = undercut.IsFormF;
                double xD = xS - sign * t2;                 // bottom end (past the face when F)
                double yExit = yBottom + t2 / Math.Tan(ShaftSpec.UndercutFaceRunOutDeg * Math.PI / 180.0);

                var segSurf = Line(sketchMgr, xS, rs, x1, rs);
                var segRamp = Line(sketchMgr, x1, rs, x2, yBottom);
                var segBottom = Line(sketchMgr, x2, yBottom, xD, yBottom);
                var segRamp8 = isF ? Line(sketchMgr, xD, yBottom, xS, yExit) : null;
                var segFace = isF
                    ? Line(sketchMgr, xS, yExit, xS, rs)
                    : Line(sketchMgr, xS, yBottom, xS, rs);

                ConstrainAll(sketchMgr);
                CoincidentToOrigin(model, StartPt(segCl));
                CoincidentPointToEdge(model, EndPt(segCl), total, 0, 0);
                // Shoulder corner = the smaller ring of the step edge, picked at its top point.
                // This is the ONLY relation pinning the profile axially (the silhouette below fixes
                // Y only), so pick it GUARDED: when SelectByID2 misses the ring the coincidence
                // silently never sticks and the profile is left free to slide in X. If it did not
                // stick, fall back to a horizontal position dimension origin → shoulder corner
                // (value taken from the exact geometry) so X is pinned either way.
                bool shoulderPinned = CoincidentPointToEdgeGuarded(model, sketchMgr, EndPt(segFace), xS, rs, 0);
                CoincidentPointToSilhouetteEdge(model, EndPt(segSurf), probeX, rs, 0);
                if (!shoulderPinned)
                {
                    model.ClearSelection2(true);
                    EndPt(segFace).Select4(true, null);
                    model.Extension.SelectByID2("", "EXTSKETCHPOINT", 0, 0, 0, true, 0, null,
                        (int)swSelectOption_e.swSelectOptionDefault);
                    MakeDriving(model.AddHorizontalDimension2(xS / 2.0, rs * 3.0, 0));
                    model.ClearSelection2(true);
                }

                const double off = 0.012;
                // f: shoulder corner ↔ run-out end, mirroring the norm's width dimension.
                model.ClearSelection2(true);
                StartPt(segSurf).Select4(true, null);
                EndPt(segSurf).Select4(true, null);
                model.AddHorizontalDimension2((xS + x1) / 2.0, rs + 2.0 * off, 0);
                DiameterDim(model, segBottom, segCl, (xS + x2) / 2.0, -yBottom);   // bottom Ø = d − 2·t1
                // 15° run-out: angular dim ramp ↔ surface, text inside the acute wedge so
                // SolidWorks measures the 15°, not the 165° sector. Best-effort like every dim.
                try
                {
                    model.ClearSelection2(true);
                    segRamp.Select4(true, null);
                    segSurf.Select4(true, null);
                    model.AddDimension2((x1 + x2) / 2.0, rs - t1 * 0.25, 0);
                }
                catch { }
                finally { model.ClearSelection2(true); }
                if (isF)
                {
                    // t2: bottom end ↔ face exit, and the 8° face run-out (text inside the acute
                    // wedge between the ramp and the face line). Best-effort like every dim.
                    try
                    {
                        model.ClearSelection2(true);
                        EndPt(segBottom).Select4(true, null);
                        StartPt(segFace).Select4(true, null);
                        model.AddHorizontalDimension2((xS + xD) / 2.0, yBottom - 2.0 * off, 0);
                        model.ClearSelection2(true);
                        segRamp8.Select4(true, null);
                        segFace.Select4(true, null);
                        model.AddDimension2(xS - sign * t2 * 0.3, yBottom + (yExit - yBottom) * 0.5, 0);
                    }
                    catch { }
                    finally { model.ClearSelection2(true); }
                }

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

                // Rounds r as ONE fillet feature on the BOTTOM FACE of the relief — the smallest-
                // diameter cylindrical face the cut just created, picked at its front-plane
                // midpoint. Filleting the face rounds every ring edge bounding it (ramp ↔ bottom
                // and face-side corner) in a single feature, without hunting individual edges.
                // The cut itself is already done, so a failure here is reported but leaves a
                // square-cornered undercut behind.
                double xMidBottom = (x2 + xD) / 2.0;
                string filletErr = FilletFace(model, r, xMidBottom, yBottom);
                if (filletErr != null) return "corte creado pero sin redondeo r: " + filletErr;
                return null;
            }
            finally
            {
                RestoreSketch(sketchMgr, addToDbWas, displayWas);
            }
        }

        /// <summary>
        /// Constant-radius fillet on the FACE picked by coordinate (mark 1) — SolidWorks rounds
        /// every edge bounding that face. Options 195 = Propagate | UniformRadius | AttachEdges |
        /// KeepFeatures, the canonical SW help recipe; signature of <c>FeatureFillet3</c> verified
        /// by reflection on the interop DLL. Returns null on success or a Spanish error.
        /// </summary>
        private static string FilletFace(IModelDoc2 model, double radius, double x, double y)
        {
            model.ClearSelection2(true);

            /* 
            if (!model.Extension.SelectByID2("", "FACE", x, y, 0.0, false, 1, null,
                (int)swSelectOption_e.swSelectOptionDefault))
            {
                model.ClearSelection2(true);
                return "no se pudo seleccionar la cara del fondo para el redondeo.";
            }
             */

            // SelectByRay

            model.Extension.SelectByRay(x, 0, 0, 0, 0, 1, 1,
                (int)swSelectType_e.swSelFACES, false, 1, (int)swSelectOption_e.swSelectOptionDefault);

            var fillet = model.FeatureManager.FeatureFillet3(
                (int)(swFeatureFilletOptions_e.swFeatureFilletPropagate
                    | swFeatureFilletOptions_e.swFeatureFilletUniformRadius
                    | swFeatureFilletOptions_e.swFeatureFilletAttachEdges
                    | swFeatureFilletOptions_e.swFeatureFilletKeepFeatures),
                radius, 0, 0,
                (int)swFeatureFilletType_e.swFeatureFilletType_Simple, 0, 0,
                null, null, null, null, null, null, null) as Feature;
            model.ClearSelection2(true);
            return fillet == null ? "SolidWorks no pudo crear el redondeo." : null;
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

        // ---- 6) DIN 332 centre points (puntos de centrado) -----------------------------------------

        /// <summary>
        /// Cuts one DIN 332 centre point into a shaft end face: a coaxial half-section (drawn on the
        /// front plane, one edge on the revolve axis) removed with a 360° cut-revolve — same call as
        /// the grooves. The profile goes mouth → flank → pilot/core bore → 120° drill tip → back
        /// along the axis to the face centre. Form A: 60° countersink; B: 120° protective countersink
        /// then the 60° cone; R: a radius arc flank instead of the 60° cone; D (threaded): 120°
        /// protection + relief counterbore + tap-drill core bore. Left end drills toward +X, right end
        /// toward −X. Pinned in X by the face-centre point (origin for the left end, the axis far
        /// endpoint for the right). Returns null on success or a Spanish error.
        /// </summary>
        private string CutShaftCenterHole(IModelDoc2 model, ShaftSpec spec, ShaftCenterHole hole)
        {
            const double mmToM = 0.001;
            double total = spec.TotalLengthMm * mmToM;
            double xf = hole.End == 0 ? 0.0 : total;
            double sign = hole.End == 0 ? 1.0 : -1.0;

            // Half-section {depth-from-face, radius} from the single source of truth, in metres. The
            // face segment (face centre → mouth) and the axis segment (tip → face centre) close the
            // loop. For the radius forms (R, DR) segment pts[arcSeg−1]→pts[arcSeg] is a tangent arc.
            var profileMm = hole.ProfileMm(out int arcSeg, out double arcRadiusMm);
            if (profileMm.Count < 3) return "el perfil del punto de centrado es incompleto.";
            var pts = new List<double[]>();
            foreach (var p in profileMm) pts.Add(new[] { p[0] * mmToM, p[1] * mmToM });
            double arcRadius = arcRadiusMm * mmToM;

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

                var segCl = sketchMgr.CreateLine(0, 0, 0, total, 0, 0);   // revolve axis
                segCl.ConstructionGeometry = true;

                // Face segment: face centre (xf, 0) → mouth M.
                double mx = xf + sign * pts[0][0];
                var faceSeg = Line(sketchMgr, xf, 0, mx, pts[0][1]);

                // Inner segments mouth → tip. Cylinder segments (equal, non-zero radius) get a driving
                // Ø; the arc flank (R, DR) is a 3-point arc tangent to the seat/pilot cylinder.
                var cylinders = new List<SketchSegment>();       // horizontal bores → Ø dims
                var cylRadius = new List<double>();
                for (int i = 1; i < pts.Count; i++)
                {
                    double xa = xf + sign * pts[i - 1][0], ya = pts[i - 1][1];
                    double xb = xf + sign * pts[i][0], yb = pts[i][1];
                    if (i == arcSeg && arcRadius > 0)
                    {
                        // Centre r above the flank end (horizontal tangent on the seat/pilot cylinder).
                        // Three-point arc through the minor-arc midpoint so the bulge is unambiguous.
                        double cx = xb, cy = yb + arcRadius;
                        double ux = xa - cx, uy = ya - cy, vx = xb - cx, vy = yb - cy;
                        double un = Math.Sqrt(ux * ux + uy * uy), vn = Math.Sqrt(vx * vx + vy * vy);
                        double sx = ux / un + vx / vn, sy = uy / un + vy / vn;
                        double sn = Math.Sqrt(sx * sx + sy * sy);
                        double midX = cx + arcRadius * sx / sn, midY = cy + arcRadius * sy / sn;
                        sketchMgr.Create3PointArc(xa, ya, 0, xb, yb, 0, midX, midY, 0);
                    }
                    else
                    {
                        var seg = Line(sketchMgr, xa, ya, xb, yb);
                        if (Math.Abs(ya - yb) < 1e-9 && ya > 1e-9) { cylinders.Add(seg); cylRadius.Add(ya); }
                    }
                }
                // Axis segment: tip T → face centre, on the revolve axis.
                double tx = xf + sign * pts[pts.Count - 1][0];
                Line(sketchMgr, tx, pts[pts.Count - 1][1], xf, 0);

                ConstrainAll(sketchMgr);
                CoincidentToOrigin(model, StartPt(segCl));
                CoincidentPointToEdge(model, EndPt(segCl), total, 0, 0);
                // Pin the hole's face-centre point in X: the origin for the left end, the axis far
                // endpoint (on the x = total face) for the right end.
                if (hole.End == 0)
                {
                    CoincidentToOrigin(model, StartPt(faceSeg));
                }
                else
                {
                    model.ClearSelection2(true);
                    var oPt = StartPt(faceSeg);
                    if (oPt != null && oPt.Select4(true, null) && EndPt(segCl).Select4(true, null))
                    {
                        model.SketchAddConstraints("sgCOINCIDENT");
                    }
                    model.ClearSelection2(true);
                }

                // Driving Ø on every straight bore (pilot/core, seat), guarded so a redundant one
                // never breaks the profile. The cones/arc are then located by their endpoints.
                for (int i = 0; i < cylinders.Count; i++)
                {
                    if (SketchBroken(sketchMgr)) break;
                    var cs = StartPt(cylinders[i]); var ce = EndPt(cylinders[i]);
                    double xMid = cs != null && ce != null ? 0.5 * (cs.X + ce.X) : xf;
                    DiameterDim(model, cylinders[i], segCl, xMid, -cylRadius[i]);
                    if (SketchBroken(sketchMgr)) { try { model.EditUndo2(1); } catch { } break; }
                }

                model.ClearSelection2(true);
                sketchMgr.InsertSketch(true);

                var sketchFeature = LastProfileFeature(model);
                if (sketchFeature == null) return "no se pudo crear el croquis del punto de centrado.";
                ForceDrivingDims(sketchFeature);

                model.ClearSelection2(true);
                sketchFeature.Select2(false, 0);

                var cut = model.FeatureManager.FeatureRevolve2(
                    true, true, false, true, false, true,
                    (int)swEndConditions_e.swEndCondBlind, (int)swEndConditions_e.swEndCondBlind,
                    2.0 * Math.PI, 0.0, false, false, 0.0, 0.0,
                    (int)swThinWallType_e.swThinWallOneDirection, 0.0, 0.0,
                    true, false, true);
                if (cut == null) return "SolidWorks no pudo crear el corte del punto de centrado.";
                return null;
            }
            finally
            {
                RestoreSketch(sketchMgr, addToDbWas, displayWas);
            }
        }

        // ---- sketch solve status + guarded relations -----------------------------------------------

        /// <summary>
        /// Solve status of the ACTIVE sketch (<c>ISketch.GetConstrainedStatus</c>, verified by
        /// reflection on the interop DLL), as <c>swConstrainedStatus_e</c>: 2 = under-defined,
        /// 3 = fully defined, 4 = over-defined, 5 = no solution, 6 = invalid geometry. 5/6 mean the
        /// last dimension/relation broke the sketch and should be rolled back.
        /// </summary>
        private static int SketchStatus(SketchManager sketchMgr)
        {
            try
            {
                var sketch = sketchMgr.ActiveSketch;
                return sketch == null
                    ? (int)swConstrainedStatus_e.swUnknownConstraint
                    : sketch.GetConstrainedStatus();
            }
            catch { return (int)swConstrainedStatus_e.swUnknownConstraint; }
        }

        private static bool SketchFullyDefined(SketchManager sketchMgr)
        {
            return SketchStatus(sketchMgr) == (int)swConstrainedStatus_e.swFullyConstrained;
        }

        private static bool SketchBroken(SketchManager sketchMgr)
        {
            int status = SketchStatus(sketchMgr);
            return status == (int)swConstrainedStatus_e.swNoSolution
                || status == (int)swConstrainedStatus_e.swInvalidSolution;
        }

        /// <summary>
        /// <c>SketchAddConstraints</c> with rollback: entities must already be selected. If the new
        /// relation leaves the sketch with NO/INVALID solution, the relations added by this call are
        /// deleted again via <c>ISketchRelationManager</c> (new relations are appended at the end of
        /// <c>GetRelations(swAll)</c>) and false is returned so the caller can fall back to a
        /// dimension instead.
        /// </summary>
        private static bool AddConstraintGuarded(IModelDoc2 model, SketchManager sketchMgr, string constraint)
        {
            var sketch = sketchMgr.ActiveSketch;
            var relMgr = sketch == null ? null : sketch.RelationManager;
            int before = -1;
            try { if (relMgr != null) before = relMgr.GetRelationsCount(0); } catch { }

            model.SketchAddConstraints(constraint);
            if (!SketchBroken(sketchMgr)) return true;

            try
            {
                var rels = relMgr == null ? null : relMgr.GetRelations(0) as object[];
                if (rels != null && before >= 0)
                {
                    for (int i = rels.Length - 1; i >= before; i--)
                    {
                        relMgr.DeleteRelation(rels[i] as SketchRelation);
                    }
                }
            }
            catch { }
            return false;
        }

        /// <summary>
        /// Guarded variant of <c>CoincidentPointToEdge</c>: reports whether the relation stuck and
        /// rolls it back when it produced invalid geometry, instead of silently leaving a broken
        /// sketch behind.
        /// </summary>
        private static bool CoincidentPointToEdgeGuarded(
            IModelDoc2 model, SketchManager sketchMgr, SketchPoint pt, double fx, double fy, double fz)
        {
            if (pt == null) return false;
            try
            {
                model.ClearSelection2(true);
                if (!pt.Select4(false, null)) return false;
                if (!model.Extension.SelectByID2("", "EDGE", fx, fy, fz, true, 0, null,
                    (int)swSelectOption_e.swSelectOptionDefault))
                {
                    return false;
                }
                return AddConstraintGuarded(model, sketchMgr, "sgCOINCIDENT");
            }
            catch { return false; }
            finally { model.ClearSelection2(true); }
        }

        /// <summary>
        /// Flips ONE display dimension to DRIVING right after creation (dims are born driven while
        /// the swAddDrivenDimensions toggle is on). Dims first, driving immediately — only then do
        /// the in-sketch fully-defined/broken checks mean anything.
        /// </summary>
        private static void MakeDriving(object displayDimension)
        {
            try
            {
                var dispDim = displayDimension as DisplayDimension;
                var dim = dispDim == null ? null : dispDim.GetDimension2(0);
                if (dim != null && dim.DrivenState == (int)swDimensionDrivenState_e.swDimensionDriven)
                {
                    dim.DrivenState = (int)swDimensionDrivenState_e.swDimensionDriving;
                }
            }
            catch { }
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
