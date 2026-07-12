using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using System.Collections.Generic;
using VeradeAddin.Models;

namespace VeradeAddin.Services
{
    /// <summary>
    /// Shared plumbing for the "Generar pieza" builders (the shaft engine builds both catalog
    /// kinds): the empty-part gate, the destructive tree clear used when regenerating a registered
    /// part, and the sketch/dimension helpers the builders share. Kept in its own partial so the
    /// configurator code is isolated from the rest of the service. The front plane is found
    /// language-independently (first <c>RefPlane</c>), so it works whatever the SolidWorks UI
    /// language is.
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

        public string ClearActivePart()
        {
            var model = _sw.IActiveDoc2 as IModelDoc2;
            if (model == null || model.GetType() != (int)swDocumentTypes_e.swDocPART)
            {
                return "No hay una pieza activa.";
            }

            // In a part tree every default feature (folders, planes, origin) comes BEFORE the origin
            // or is the origin itself; all user modelling comes after it. So "clear" = delete every
            // top-level feature past the origin, letting the delete absorb children (sketches inside
            // revolves, etc.). Type-based, so it works whatever the UI language is.
            var toDelete = new List<Feature>();
            bool pastOrigin = false;
            var feature = model.FirstFeature() as Feature;
            while (feature != null)
            {
                if (pastOrigin)
                {
                    toDelete.Add(feature);
                }
                else if (feature.GetTypeName2() == "OriginProfileFeature")
                {
                    pastOrigin = true;
                }
                feature = feature.GetNextFeature() as Feature;
            }
            if (!pastOrigin)
            {
                return "No se encontró el origen de la pieza.";
            }
            if (toDelete.Count == 0)
            {
                return null; // already empty
            }

            model.ClearSelection2(true);
            bool anySelected = false;
            foreach (var f in toDelete)
            {
                anySelected |= f.Select2(true, 0);
            }
            if (!anySelected)
            {
                model.ClearSelection2(true);
                return "No se pudieron seleccionar las operaciones a eliminar.";
            }

            bool deleted = model.Extension.DeleteSelection2(
                (int)(swDeleteSelectionOptions_e.swDelete_Absorbed | swDeleteSelectionOptions_e.swDelete_Children));
            model.ClearSelection2(true);
            model.EditRebuild3();

            if (!deleted)
            {
                return "SolidWorks no pudo eliminar las operaciones existentes.";
            }
            return IsActivePartEmpty()
                ? null
                : "La pieza no quedó vacía tras eliminar las operaciones.";
        }

        // ---- shared sketch/dimension helpers (used by the shaft builder) ---------------------------

        // Creates a sketch line on the front plane (z=0) with exact coordinates. No relation work here:
        // with AddToDB=true the point goes straight to the database (no snapping) and ConstrainAll adds
        // every relation afterwards. Returns the segment so callers can dimension it.
        private static SketchSegment Line(SketchManager sketchMgr, double x1, double y1, double x2, double y2)
        {
            return sketchMgr.CreateLine(x1, y1, 0, x2, y2, 0);
        }

        // Infers every geometric relation (horizontal/vertical/coincident/...) on the active sketch in
        // one shot — the documented partner of AddToDB=true. Best-effort: never aborts the build.
        private static void ConstrainAll(SketchManager sketchMgr)
        {
            try
            {
                var sketch = sketchMgr.ActiveSketch;
                if (sketch != null) sketch.ConstrainAll();
            }
            catch { }
        }

        private static SketchPoint StartPt(SketchSegment seg)
        {
            var line = seg as SketchLine;
            return line == null ? null : line.GetStartPoint2() as SketchPoint;
        }

        private static SketchPoint EndPt(SketchSegment seg)
        {
            var line = seg as SketchLine;
            return line == null ? null : line.GetEndPoint2() as SketchPoint;
        }

        // Coincident between a sketch point and the part origin. The origin is found language-
        // INDEPENDENTLY by its feature type ("OriginProfileFeature"), never by its localized name.
        private static void CoincidentToOrigin(IModelDoc2 model, SketchPoint pt)
        {
            if (pt == null) return;
            try
            {
                model.ClearSelection2(true);
                var startpoint = pt.Select4(false, null);
                var origin = model.Extension.SelectByID2("", "EXTSKETCHPOINT", 0, 0, 0, true, 0, null, 0);
                if (origin && startpoint)
                {
                    model.SketchAddConstraints("sgCOINCIDENT");
                }
            }
            catch { }
            finally { model.ClearSelection2(true); }
        }

        // Coincident (point-on-face) between a sketch point and a model face picked by coordinate.
        // Anchors a cut sketch to the already-built body (end face, cylindrical face).
        private static void CoincidentPointToEdge(IModelDoc2 model, SketchPoint pt, double fx, double fy, double fz)
        {
            if (pt == null) return;
            try
            {
                model.ClearSelection2(true);
                pt.Select4(false, null);
                bool gotEdge = model.Extension.SelectByID2(
                    "", "EDGE", fx, fy, fz, true, 0, null, (int)swSelectOption_e.swSelectOptionDefault);
                if (gotEdge)
                {
                    model.SketchAddConstraints("sgCOINCIDENT");
                }
            }
            catch { }
            finally { model.ClearSelection2(true); }
        }

        private static void CoincidentPointToSilhouetteEdge(IModelDoc2 model, SketchPoint pt, double fx, double fy, double fz)
        {
            if (pt == null) return;
            try
            {
                model.ClearSelection2(true);
                pt.Select4(false, null);
                bool gotEdge = model.Extension.SelectByID2(
                    "", "SILHOUETTE", fx, fy, fz, true, 0, null, (int)swSelectOption_e.swSelectOptionDefault);
                if (gotEdge)
                {
                    model.SketchAddConstraints("sgCOINCIDENT");
                }
            }
            catch { }
            finally { model.ClearSelection2(true); }
        }

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
                model.AddHorizontalDimension2(x, y, 0); // horizontal length, like the reference console
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
                // Order matters: select the LINE first, then the centreline (matches the working
                // reference console). Reversed, AddDiameterDimension2 falls back to a radius.
                line.Select4(true, null);
                axis.Select4(true, null);    // line + centreline ⇒ AddDiameterDimension2 makes it a Ø dim
                DisplayDimension display = (DisplayDimension)model.AddDiameterDimension2(x, y, 0); // was AddDimension2, which produced a radius
                display.Diametric = true;
            }
            catch { }
            finally { model.ClearSelection2(true); }
        }

        // Horizontal distance from a body edge (selected by coordinate) to a sketch segment — used
        // for cut positions measured from an existing body edge.
        private void EdgeToSegDim(IModelDoc2 model, double edgeX, double edgeY, SketchSegment seg, double x, double y)
        {
            if (seg == null) return;
            try
            {
                model.ClearSelection2(true);
                model.Extension.SelectByID2(
                    "", "EDGE", edgeX, edgeY, 0, true, 0, null, (int)swSelectOption_e.swSelectOptionDefault);
                seg.Select4(true, null);
                model.AddHorizontalDimension2(x, y, 0); // horizontal position from the step edge
            }
            catch { }
            finally { model.ClearSelection2(true); }
        }
    }
}
