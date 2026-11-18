using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using VeradeAddin.Infrastructure;
using VeradeAddin.Models;

namespace VeradeAddin.Services
{
    /// <summary>
    /// "Localizar en vista" and "Mostrar todo" (assembly context toolbar).
    ///
    /// Coordinate spaces: Transform2, GetBox and the view orientation are in top-level ASSEMBLY space, but
    /// component geometry (bodies, faces, ray hits on them) reports PART space. The rays are built in
    /// assembly space and moved into each candidate's part space, then fired at temporary copies of its
    /// solids (a copy carries plain part-space geometry with no component context), so the test does not
    /// depend on how SolidWorks would treat a component body handed to RayIntersections.
    ///
    /// API: ISelectionMgr.GetSelectedObjectCount2/GetSelectedObjectsComponent4,
    ///   IComponent2.Transform2/GetBox/GetBodies3/GetChildren/IsHidden/IsSuppressed/Select4,
    ///   IModelDoc2.HideComponent2/ShowComponent2, IModelView.Orientation3/HasPerspective/GetEyePoint,
    ///   IMathUtility.CreatePoint/CreateVector, IMathTransform.Inverse, IMathPoint/IMathVector.MultiplyTransform,
    ///   IBody2.Copy2, IModelDocExtension.RayIntersections/SelectByID2,
    ///   IModelDoc2.GetRayIntersectionsPoints/ViewZoomToSelection, ISldWorks.RunCommand.
    /// </summary>
    public sealed partial class SolidWorksService
    {
        // swCommands_e.swCommands_Show_Dependents ("Mostrar con dependientes"). Hard-coded so the add-in
        // needs no reference to SolidWorks.Interop.swcommands.dll.
        private const int ShowDependentsCommandId = 1046;

        // Ray starts at the box corners are pulled this far from the centre (0 = centre, 1 = corner) so they
        // fall inside the component rather than inside a neighbour that touches its box.
        private const double CornerInset = 0.5;

        // Metres. Hits closer than this to the ray start are ignored, and boxes are padded by it.
        private const double RayTolerance = 1e-5;

        public bool IsSingleComponentSelectedInActiveAssembly()
        {
            using (var com = new ComScope())
            {
                var model = com.Track(_sw.IActiveDoc2 as IModelDoc2);
                if (model == null || model.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
                {
                    return false;
                }
                return com.Track(SingleSelectedComponent(model)) != null;
            }
        }

        public LocateComponentResult LocateSelectedComponentInView()
        {
            var result = new LocateComponentResult();

            using (var com = new ComScope())
            {
                var model = com.Track(_sw.IActiveDoc2 as IModelDoc2);
                if (model == null || model.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
                {
                    result.Error = "No hay un ensamblaje activo.";
                    return result;
                }

                var target = com.Track(SingleSelectedComponent(model));
                if (target == null)
                {
                    result.Error = "Seleccione un único componente del ensamblaje.";
                    return result;
                }
                result.ComponentName = target.Name2;

                var view = com.Track(model.IActiveView);
                var math = com.Track(_sw.GetMathUtility() as MathUtility);
                if (view == null || math == null)
                {
                    result.Error = "No hay una vista activa.";
                    return result;
                }

                // 1) Ray bundle in assembly space: start points on the component, directions to the camera.
                var starts = RayStartPoints(target);
                var toViewer = ViewerDirection(view);
                if (starts.Count == 0 || toViewer == null)
                {
                    result.Error = "No se pudo calcular la posición del componente o la dirección de la cámara.";
                    return result;
                }

                var eye = view.HasPerspective() ? view.GetEyePoint() as double[] : null;
                var dirs = new List<double[]>();
                foreach (var start in starts)
                {
                    dirs.Add(RayDirection(start, toViewer, eye));
                }
                result.RayCount = starts.Count;
                result.ViewDirection = toViewer;

                // 2) Parts the rays cross: cheap box test first, then the real solids.
                var asm = (IAssemblyDoc)model;
                var ext = com.Track(model.Extension);
                var all = com.TrackAll(asm.GetComponents(false) as object[]);
                var toHide = new List<Component2>();
                string targetName = target.Name2;

                foreach (var obj in all ?? new object[0])
                {
                    var c = obj as Component2;
                    if (c == null || !IsShownPart(c))
                    {
                        continue;
                    }

                    // Never the component itself, nor (when it is a subassembly) its own parts.
                    string name = c.Name2;
                    if (string.Equals(name, targetName, StringComparison.OrdinalIgnoreCase) ||
                        name.StartsWith(targetName + "/", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var box = c.GetBox(false, false) as double[];
                    if (box == null || box.Length < 6)
                    {
                        continue;
                    }

                    var crossing = new List<int>();
                    for (int i = 0; i < starts.Count; i++)
                    {
                        if (RayHitsBox(starts[i], dirs[i], box))
                        {
                            crossing.Add(i);
                        }
                    }
                    if (crossing.Count == 0)
                    {
                        continue;
                    }

                    result.Candidates++;
                    bool? hit = RaysHitSolids(model, ext, math, c, starts, dirs, crossing);
                    if (hit == false)
                    {
                        continue;
                    }
                    if (hit == null)
                    {
                        result.HiddenByBoxOnly++;
                    }
                    toHide.Add(c);
                    result.HiddenComponents.Add(name);
                }

                // 3) Hide them with SolidWorks' own "Ocultar componentes" on the selection: setting
                //    IComponent2.Visible changes the state but only shows after a manual rebuild.
                model.ClearSelection2(true);
                if (toHide.Count > 0)
                {
                    foreach (var c in toHide)
                    {
                        c.Select4(true, null, false);
                    }
                    model.HideComponent2();
                    model.ClearSelection2(true);
                }

                // 4) Select the component and zoom to it (left selected so the user sees it).
                target.Select4(false, null, false);
                model.ViewZoomToSelection();
                model.GraphicsRedraw2();

                result.Success = true;
                return result;
            }
        }

        public ShowAllResult ShowAllComponents()
        {
            var result = new ShowAllResult();

            using (var com = new ComScope())
            {
                var model = com.Track(_sw.IActiveDoc2 as IModelDoc2);
                if (model == null || model.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
                {
                    result.Error = "No hay un ensamblaje activo.";
                    return result;
                }

                try
                {
                    // 1) SolidWorks' own "Mostrar con dependientes" on the top-level assembly node.
                    model.ClearSelection2(true);
                    if (SelectTopLevelNode(model, com.Track(model.Extension)))
                    {
                        result.RanShowDependents = _sw.RunCommand(ShowDependentsCommandId, "");
                    }
                    model.ClearSelection2(true);

                    // 2) Safety net: anything still hidden (command unavailable, root not selectable) is
                    //    selected and shown with "Mostrar componentes" (setting Visible needs a rebuild to show).
                    var asm = (IAssemblyDoc)model;
                    var all = com.TrackAll(asm.GetComponents(false) as object[]);
                    foreach (var obj in all ?? new object[0])
                    {
                        var c = obj as Component2;
                        if (c == null || IsReallySuppressed(c) || !c.IsHidden(false))
                        {
                            continue;
                        }
                        c.Select4(true, null, false);
                        result.ShownByFallback++;
                    }
                    if (result.ShownByFallback > 0)
                    {
                        model.ShowComponent2();
                        model.ClearSelection2(true);
                    }

                    model.GraphicsRedraw2();
                    result.Success = true;
                }
                catch (Exception ex)
                {
                    result.Error = ex.Message;
                }

                return result;
            }
        }

        /// <summary>The owning component when EXACTLY one object is selected, else null. Caller owns it.</summary>
        private static Component2 SingleSelectedComponent(IModelDoc2 model)
        {
            var selMgr = model.SelectionManager as ISelectionMgr;
            if (selMgr == null)
            {
                return null;
            }

            try
            {
                return selMgr.GetSelectedObjectCount2(-1) == 1
                    ? selMgr.GetSelectedObjectsComponent4(1, -1) as Component2
                    : null;
            }
            finally { ComRelease.Release(selMgr); }
        }

        /// <summary>
        /// A part currently on screen: not a subassembly, not suppressed, not already hidden.
        /// Subassemblies are never hidden as a whole, since that could hide the very component being located.
        /// </summary>
        private static bool IsShownPart(Component2 c)
        {
            if (IsReallySuppressed(c) || c.IsHidden(false))
            {
                return false;
            }

            var children = c.GetChildren() as object[];
            try { return children == null || children.Length == 0; }
            finally { ComRelease.ReleaseAll(children); }
        }

        /// <summary>
        /// True only for a component that is actually suppressed. IComponent2.IsSuppressed() also answers true
        /// for LIGHTWEIGHT components (measured on SW 2021: 1859 of 1859 lightweight parts), and large assemblies
        /// open lightweight, so it would skip almost every part there. Lightweight parts still give their box
        /// and solids, which is all the ray test needs.
        /// </summary>
        private static bool IsReallySuppressed(Component2 c)
        {
            return c.GetSuppression2() == (int)swComponentSuppressionState_e.swComponentSuppressed;
        }

        /// <summary>
        /// Where the rays start, in assembly space: the component origin, its box centre and the 8 box corners
        /// pulled towards the centre (<see cref="CornerInset"/>). Several rays catch parts that cover only part
        /// of the component, or the whole of it when its origin lies outside its solid.
        /// </summary>
        private static List<double[]> RayStartPoints(Component2 target)
        {
            var points = new List<double[]>();

            var xform = target.Transform2;
            try
            {
                var d = xform != null ? xform.ArrayData as double[] : null;
                if (d != null && d.Length >= 12)
                {
                    points.Add(new[] { d[9], d[10], d[11] }); // translation = component origin
                }
            }
            finally { ComRelease.Release(xform); }

            var box = target.GetBox(false, false) as double[]; // [xMin,yMin,zMin, xMax,yMax,zMax]
            if (box != null && box.Length >= 6)
            {
                double cx = (box[0] + box[3]) / 2, cy = (box[1] + box[4]) / 2, cz = (box[2] + box[5]) / 2;
                points.Add(new[] { cx, cy, cz });

                for (int k = 0; k < 8; k++)
                {
                    double x = (k & 1) == 0 ? box[0] : box[3];
                    double y = (k & 2) == 0 ? box[1] : box[4];
                    double z = (k & 4) == 0 ? box[2] : box[5];
                    points.Add(new[]
                    {
                        cx + (x - cx) * CornerInset,
                        cy + (y - cy) * CornerInset,
                        cz + (z - cz) * CornerInset
                    });
                }
            }

            return points;
        }

        /// <summary>
        /// Unit vector from the model towards the viewer, in assembly space: the Z axis of the view.
        /// Orientation3 maps model to view space and SolidWorks stores the rotation row-wise (p' = p·R), so the
        /// view's Z axis expressed in model space is the third COLUMN of R: elements 2, 5, 8.
        /// </summary>
        private static double[] ViewerDirection(IModelView view)
        {
            var orient = view.Orientation3;
            try
            {
                var d = orient != null ? orient.ArrayData as double[] : null;
                return d != null && d.Length >= 9 ? Normalize(new[] { d[2], d[5], d[8] }) : null;
            }
            finally { ComRelease.Release(orient); }
        }

        /// <summary>
        /// Direction of one ray: along the view axis or, in perspective, straight at the eye point so each ray
        /// really ends at the camera. The eye is ignored if it would send the ray away from the viewer.
        /// </summary>
        private static double[] RayDirection(double[] start, double[] toViewer, double[] eye)
        {
            if (eye != null && eye.Length >= 3)
            {
                var d = Normalize(new[] { eye[0] - start[0], eye[1] - start[1], eye[2] - start[2] });
                if (d != null && Dot(d, toViewer) > 0)
                {
                    return d;
                }
            }
            return toViewer;
        }

        /// <summary>Slab test: does the ray (t &gt; 0) cross the axis-aligned box [min xyz, max xyz]?</summary>
        private static bool RayHitsBox(double[] origin, double[] dir, double[] box)
        {
            double tMin = 0, tMax = double.MaxValue;
            for (int a = 0; a < 3; a++)
            {
                double lo = box[a] - RayTolerance, hi = box[a + 3] + RayTolerance;
                if (Math.Abs(dir[a]) < 1e-12)
                {
                    if (origin[a] < lo || origin[a] > hi)
                    {
                        return false;
                    }
                    continue;
                }

                double t1 = (lo - origin[a]) / dir[a];
                double t2 = (hi - origin[a]) / dir[a];
                if (t1 > t2)
                {
                    double tmp = t1; t1 = t2; t2 = tmp;
                }
                if (t1 > tMin) tMin = t1;
                if (t2 < tMax) tMax = t2;
                if (tMin > tMax)
                {
                    return false;
                }
            }
            return tMax > RayTolerance;
        }

        /// <summary>
        /// Exact test: does any of the <paramref name="rays"/> (indices into starts/dirs) cross a solid body of
        /// <paramref name="comp"/> in front of its start? Null when the solids are not available (lightweight,
        /// SpeedPak or graphics-only component), so the caller falls back to the box test.
        /// </summary>
        private static bool? RaysHitSolids(IModelDoc2 model, IModelDocExtension ext, MathUtility math, Component2 comp,
            List<double[]> starts, List<double[]> dirs, List<int> rays)
        {
            using (var com = new ComScope())
            {
                object bodiesInfo;
                var bodies = com.TrackAll(comp.GetBodies3((int)swBodyType_e.swSolidBody, out bodiesInfo) as object[]);
                if (bodies == null || bodies.Length == 0)
                {
                    return null;
                }

                // Temporary copies: plain part-space geometry, no component context. Wrapped in DispatchWrapper
                // because RayIntersections reads BodiesIn as an array of IDispatch pointers: a plain object[]
                // marshals as an array of VARIANTs, which SolidWorks dereferences as pointers
                // (AccessViolationException).
                var copies = new List<DispatchWrapper>();
                foreach (var obj in bodies)
                {
                    var body = obj as IBody2;
                    var copy = body != null ? com.Track(body.Copy2(false) as Body2) : null;
                    if (copy != null)
                    {
                        copies.Add(new DispatchWrapper(copy));
                    }
                }
                if (copies.Count == 0)
                {
                    return null;
                }

                // Rays: assembly space -> this component's part space.
                var xform = com.Track(comp.Transform2);
                var toPart = xform != null ? com.Track(xform.Inverse() as MathTransform) : null;
                if (toPart == null)
                {
                    return null;
                }

                var partStarts = new double[rays.Count][];
                var partDirs = new double[rays.Count][];
                var points = new double[rays.Count * 3];
                var vectors = new double[rays.Count * 3];
                for (int k = 0; k < rays.Count; k++)
                {
                    var p = com.Track(math.CreatePoint(starts[rays[k]]) as MathPoint);
                    var v = com.Track(math.CreateVector(dirs[rays[k]]) as MathVector);
                    var pp = com.Track(p != null ? p.MultiplyTransform(toPart) as MathPoint : null);
                    var pv = com.Track(v != null ? v.MultiplyTransform(toPart) as MathVector : null);
                    partStarts[k] = pp != null ? pp.ArrayData as double[] : null;
                    partDirs[k] = pv != null ? pv.ArrayData as double[] : null;
                    if (partStarts[k] == null || partDirs[k] == null)
                    {
                        return null;
                    }

                    Array.Copy(partStarts[k], 0, points, k * 3, 3);
                    Array.Copy(partDirs[k], 0, vectors, k * 3, 3);
                }

                int hits = ext.RayIntersections(copies.ToArray(), points, vectors,
                    (int)(swRayPtsOpts_e.swRayPtsOptsENTRY_EXIT | swRayPtsOpts_e.swRayPtsOptsUNBLOCK), 0, 0, false);
                if (hits <= 0)
                {
                    return false;
                }

                // Per hit: body index, ray index, hit type, x, y, z [, normal]. Only hits in front of the start count.
                var data = model.GetRayIntersectionsPoints() as double[];
                int stride = data != null ? data.Length / hits : 0;
                if (stride < 6)
                {
                    return true; // hits reported but not readable: trust them
                }

                for (int h = 0; h < hits; h++)
                {
                    int i = h * stride;
                    int ray = (int)data[i + 1];
                    if (ray < 0 || ray >= rays.Count)
                    {
                        continue;
                    }

                    var s = partStarts[ray];
                    var d = partDirs[ray];
                    double t = (data[i + 3] - s[0]) * d[0] + (data[i + 4] - s[1]) * d[1] + (data[i + 5] - s[2]) * d[2];
                    if (t > RayTolerance)
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        /// <summary>
        /// Selects the top-level assembly node of the FeatureManager tree (what "Mostrar con dependientes" acts
        /// on), by name as a COMPONENT. The title may or may not carry the extension depending on Windows
        /// settings, so every spelling is tried.
        /// </summary>
        private static bool SelectTopLevelNode(IModelDoc2 model, IModelDocExtension ext)
        {
            if (ext == null)
            {
                return false;
            }

            var names = new List<string>();
            string path = model.GetPathName();
            if (!string.IsNullOrEmpty(path))
            {
                names.Add(Path.GetFileNameWithoutExtension(path));
                names.Add(Path.GetFileName(path));
            }
            string title = model.GetTitle();
            if (!string.IsNullOrEmpty(title))
            {
                names.Add(title);
                names.Add(Path.GetFileNameWithoutExtension(title));
            }

            foreach (var name in names)
            {
                if (ext.SelectByID2(name, "COMPONENT", 0, 0, 0, false, 0, null, 0))
                {
                    return true;
                }
            }
            return false;
        }

        private static double Dot(double[] a, double[] b)
        {
            return a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
        }

        private static double[] Normalize(double[] v)
        {
            double len = Math.Sqrt(Dot(v, v));
            return len < 1e-12 ? null : new[] { v[0] / len, v[1] / len, v[2] / len };
        }
    }
}
