using System;
using System.Collections.Generic;
using System.Diagnostics;
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
    /// Localizar zooms to the component, then looks for a sightline from which it is seen without hiding
    /// anything. Rays leave the component (origin, box centre, inset box corners) towards the camera of the
    /// current view first, then along the 26 standard directions (6 views, 12 between two of them, 8 isometric)
    /// from the nearest to the farthest. The first sightline with every ray free wins; failing that, the first
    /// with the centre ray and most rays free. The camera turns to it and zooms again. When no sightline
    /// qualifies, the parts covering the component in the current view are hidden instead (method 1).
    ///
    /// Coordinate spaces: Transform2, GetBox and the view orientation are in top-level ASSEMBLY space, but
    /// component geometry (bodies, their boxes, ray hits on them) reports PART space. The rays are built in
    /// assembly space and moved into each candidate's part space (plain arithmetic on the inverse Transform2),
    /// then fired at temporary copies of its solids (a copy carries plain part-space geometry with no component
    /// context), so the test does not depend on how SolidWorks would treat a component body handed to
    /// RayIntersections.
    ///
    /// API: ISelectionMgr.GetSelectedObjectCount2/GetSelectedObjectsComponent4,
    ///   IComponent2.Transform2/GetBox/GetBodies3/GetChildren/IGetChildrenCount/IsHidden/GetSuppression2/Select4,
    ///   IAssemblyDoc.GetComponents, IModelDoc2.HideComponent2/ShowComponent2/ViewZoomToSelection,
    ///   IModelView.Orientation3 (get/set)/HasPerspective/GetEyePoint,
    ///   IMathUtility.CreateTransform, IMathTransform.Inverse/ArrayData,
    ///   IBody2.Copy2/GetBodyBox, IModelDocExtension.RayIntersections/SelectByID2,
    ///   IModelDoc2.GetRayIntersectionsPoints, ISldWorks.RunCommand.
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

        // A standard direction within ~1° of the current view is the current view itself.
        private const double SameDirectionDot = 0.9998;

        // Direction (part space) of the ray that tells whether a start point lies inside the component's own
        // solid. Skewed so it does not run along the faces and edges of prismatic parts.
        private static readonly double[] InsideProbe = Normalize(new[] { 0.5373, 0.6211, 0.5702 });

        public bool IsSingleComponentSelectedInActiveAssembly()
        {
            using (var com = new ComScope())
            {
                var model = ActiveAssembly(com);
                return model != null && com.Track(SingleSelectedComponent(model)) != null;
            }
        }

        public LocateComponentResult LocateSelectedComponentInView()
        {
            var result = new LocateComponentResult();
            var clock = Stopwatch.StartNew();

            using (var com = new ComScope())
            {
                var model = ActiveAssembly(com);
                if (model == null)
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
                var ext = com.Track(model.Extension);
                if (view == null || math == null || ext == null)
                {
                    result.Error = "No hay una vista activa.";
                    return result;
                }

                // 1) Zoom first: the user sees the component at once, and in perspective the eye the current-view
                //    rays aim at is the one after the zoom.
                ZoomTo(model, target);

                // 2) Ray start points on the component, then the sightlines to try (current view first).
                var starts = RayStartPoints(target);
                var toViewer = ViewerDirection(view);
                if (starts.Points.Count == 0 || toViewer == null)
                {
                    result.Error = "No se pudo calcular la posición del componente o la dirección de la cámara.";
                    return result;
                }
                KeepStartsInsideOwnSolid(model, ext, com, target, starts);
                result.RayCount = starts.Points.Count;
                result.StartPointsDiscarded = starts.Discarded;

                var eye = view.HasPerspective() ? view.GetEyePoint() as double[] : null;
                var sightlines = Sightlines(starts.Points, toViewer, eye);

                // 3) Parts that may stand in the way of some sightline: box test only, and a subassembly whose box
                //    no ray crosses is skipped whole.
                var obstacles = new List<Obstacle>();
                var roots = com.TrackAll(((IAssemblyDoc)model).GetComponents(true) as object[]);
                CollectObstacles(com, roots, target.Name2, starts.Points, sightlines, obstacles);
                result.Candidates = obstacles.Count;

                // 4) The current view is tested in full: if no sightline is good enough, its blockers are what
                //    gets hidden. Then the others until one has every ray free.
                var current = sightlines[0];
                FindBlockers(model, ext, com, starts.Points, current, obstacles, true, false);
                result.SightlinesTested = 1;

                Sightline free = AllRaysFree(current) ? current : null;
                Sightline mostlyFree = free == null && CentreAndMostRaysFree(current, starts.Centre) ? current : null;
                for (int i = 1; free == null && i < sightlines.Count; i++)
                {
                    var s = sightlines[i];
                    // With a mostly free sightline in hand only a fully free one can beat it.
                    FindBlockers(model, ext, com, starts.Points, s, obstacles, false, mostlyFree != null);
                    result.SightlinesTested++;

                    if (AllRaysFree(s))
                    {
                        free = s;
                    }
                    else if (mostlyFree == null && CentreAndMostRaysFree(s, starts.Centre))
                    {
                        mostlyFree = s;
                    }
                }

                // 5) Look from there (already zoomed when it is the current view), or hide what covers it now.
                var chosen = free ?? mostlyFree;
                if (chosen != null && chosen != current)
                {
                    if (SetViewDirection(view, math, chosen.ToViewer))
                    {
                        result.ViewRotated = true;
                        ZoomTo(model, target);
                    }
                    else
                    {
                        chosen = null;
                    }
                }

                if (chosen == null)
                {
                    chosen = current;
                    HideComponents(model, current.Blockers);
                    foreach (var o in current.Blockers)
                    {
                        result.HiddenComponents.Add(o.Component.Name2);
                        if (o.Solids == null)
                        {
                            result.HiddenByBoxOnly++;
                        }
                    }
                    ZoomTo(model, target); // hiding cleared the selection: selected again so the user sees it
                }

                result.ViewDirection = chosen.ToViewer;
                result.FreeRays = chosen.FreeCount;
                model.GraphicsRedraw2();

                result.Success = true;
                result.ElapsedMs = clock.ElapsedMilliseconds;
                return result;
            }
        }

        public ShowAllResult ShowAllComponents()
        {
            var result = new ShowAllResult();

            using (var com = new ComScope())
            {
                var model = ActiveAssembly(com);
                if (model == null)
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

        /// <summary>The active document when it is an assembly, else null. Tracked in <paramref name="com"/>.</summary>
        private IModelDoc2 ActiveAssembly(ComScope com)
        {
            var model = com.Track(_sw.IActiveDoc2 as IModelDoc2);
            return model != null && model.GetType() == (int)swDocumentTypes_e.swDocASSEMBLY ? model : null;
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
        /// Selects the component itself (not the face the user may have picked, which would frame only that
        /// face) and zooms to it.
        /// </summary>
        private static void ZoomTo(IModelDoc2 model, Component2 target)
        {
            target.Select4(false, null, false);
            model.ViewZoomToSelection();
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
        private static RayStarts RayStartPoints(Component2 target)
        {
            var starts = new RayStarts();

            var xform = target.Transform2;
            try
            {
                var d = xform != null ? xform.ArrayData as double[] : null;
                if (d != null && d.Length >= 12)
                {
                    starts.Points.Add(new[] { d[9], d[10], d[11] }); // translation = component origin
                }
            }
            finally { ComRelease.Release(xform); }

            var box = target.GetBox(false, false) as double[]; // [xMin,yMin,zMin, xMax,yMax,zMax]
            if (box != null && box.Length >= 6)
            {
                starts.Box = box;
                double cx = (box[0] + box[3]) / 2, cy = (box[1] + box[4]) / 2, cz = (box[2] + box[5]) / 2;
                starts.Centre = starts.Points.Count;
                starts.Points.Add(new[] { cx, cy, cz });

                for (int k = 0; k < 8; k++)
                {
                    double x = (k & 1) == 0 ? box[0] : box[3];
                    double y = (k & 2) == 0 ? box[1] : box[4];
                    double z = (k & 4) == 0 ? box[2] : box[5];
                    starts.Points.Add(new[]
                    {
                        cx + (x - cx) * CornerInset,
                        cy + (y - cy) * CornerInset,
                        cz + (z - cz) * CornerInset
                    });
                }
            }

            return starts;
        }

        /// <summary>
        /// Drops the start points that fall outside the component's own solid (box corners of an L profile, the
        /// hollow of a tube, an origin off the part): such a point may lie inside a touching neighbour, and its
        /// ray would then be blocked in every direction. One ray per point at the component's own solids: the
        /// point is inside when the nearest hit is an exit. Left as they are when the solids are not available
        /// (subassembly, SpeedPak) or no point turns out inside.
        /// </summary>
        private static void KeepStartsInsideOwnSolid(IModelDoc2 model, IModelDocExtension ext, ComScope com,
            Component2 target, RayStarts starts)
        {
            if (target.IGetChildrenCount() > 0)
            {
                return;
            }
            var solids = LoadSolids(com, target, starts.Box);
            if (solids == null)
            {
                return;
            }

            int n = starts.Points.Count;
            var partStarts = new double[n][];
            var partDirs = new double[n][];
            var lengths = new double[n];
            for (int k = 0; k < n; k++)
            {
                partStarts[k] = TransformPoint(solids.ToPart, starts.Points[k]);
                partDirs[k] = InsideProbe;
                lengths[k] = double.PositiveInfinity;
            }

            var hits = FireRays(model, ext, com, solids, partStarts, partDirs, lengths);
            if (hits == null)
            {
                return;
            }

            var nearest = new RayHit?[n];
            foreach (var h in hits)
            {
                if (nearest[h.Ray] == null || h.T < nearest[h.Ray].Value.T)
                {
                    nearest[h.Ray] = h;
                }
            }

            var kept = new List<double[]>();
            int centre = -1;
            for (int k = 0; k < n; k++)
            {
                if (nearest[k] != null && (nearest[k].Value.Type & (int)swRayPtsResults_e.swRayPtsResultsEXIT) != 0)
                {
                    if (k == starts.Centre)
                    {
                        centre = kept.Count;
                    }
                    kept.Add(starts.Points[k]);
                }
            }
            if (kept.Count == 0)
            {
                return;
            }

            starts.Discarded = n - kept.Count;
            starts.Points = kept;
            starts.Centre = centre;
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
        /// Turns the camera so it looks along -<paramref name="toViewer"/>, model +Y up (or -Z/+Z when looking
        /// straight down/up, like the standard Top/Bottom views). Same layout as <see cref="ViewerDirection"/>:
        /// the view axes are the COLUMNS of the rotation. Translation and scale are kept.
        /// </summary>
        private static bool SetViewDirection(IModelView view, MathUtility math, double[] toViewer)
        {
            var z = toViewer;
            var up = Math.Abs(z[1]) > 0.99 ? new[] { 0.0, 0.0, z[1] > 0 ? -1.0 : 1.0 } : new[] { 0.0, 1.0, 0.0 };
            var x = Normalize(Cross(up, z));
            if (x == null)
            {
                return false;
            }
            var y = Cross(z, x);

            var orient = view.Orientation3;
            MathTransform turned = null;
            try
            {
                var d = orient != null ? orient.ArrayData as double[] : null;
                if (d == null || d.Length < 13)
                {
                    return false;
                }

                d = (double[])d.Clone();
                d[0] = x[0]; d[1] = y[0]; d[2] = z[0];
                d[3] = x[1]; d[4] = y[1]; d[5] = z[1];
                d[6] = x[2]; d[7] = y[2]; d[8] = z[2];
                turned = math.CreateTransform(d) as MathTransform;
                if (turned == null)
                {
                    return false;
                }
                view.Orientation3 = turned;
                return true;
            }
            finally
            {
                ComRelease.Release(turned);
                ComRelease.Release(orient);
            }
        }

        /// <summary>
        /// The sightlines to try, in order: the current view (in perspective each ray aims at the eye and ends
        /// there; the eye is ignored if it would send the ray away from the viewer), then the 26 directions of a
        /// 3×3×3 grid around the component from the nearest to the current view to the farthest. Those are
        /// parallel and unbounded: nothing beyond the assembly can block, so "up to the camera" needs no length.
        /// </summary>
        private static List<Sightline> Sightlines(List<double[]> starts, double[] toViewer, double[] eye)
        {
            var current = new Sightline(toViewer, starts.Count);
            for (int i = 0; i < starts.Count; i++)
            {
                current.Dirs[i] = toViewer;
                current.Lengths[i] = double.PositiveInfinity;
                if (eye != null && eye.Length >= 3)
                {
                    var d = new[] { eye[0] - starts[i][0], eye[1] - starts[i][1], eye[2] - starts[i][2] };
                    double len = Math.Sqrt(Dot(d, d));
                    if (len > 1e-12 && Dot(d, toViewer) > 0)
                    {
                        current.Dirs[i] = new[] { d[0] / len, d[1] / len, d[2] / len };
                        current.Lengths[i] = len;
                    }
                }
            }

            var standard = new List<double[]>();
            for (int x = -1; x <= 1; x++)
            {
                for (int y = -1; y <= 1; y++)
                {
                    for (int z = -1; z <= 1; z++)
                    {
                        if (x == 0 && y == 0 && z == 0)
                        {
                            continue;
                        }
                        var d = Normalize(new double[] { x, y, z });
                        if (Dot(d, toViewer) < SameDirectionDot)
                        {
                            standard.Add(d);
                        }
                    }
                }
            }
            standard.Sort((a, b) => Dot(b, toViewer).CompareTo(Dot(a, toViewer)));

            var sightlines = new List<Sightline> { current };
            foreach (var d in standard)
            {
                var s = new Sightline(d, starts.Count);
                for (int i = 0; i < starts.Count; i++)
                {
                    s.Dirs[i] = d;
                    s.Lengths[i] = double.PositiveInfinity;
                }
                sightlines.Add(s);
            }
            return sightlines;
        }

        /// <summary>
        /// Visible parts whose box some ray of some sightline crosses, walking the tree from
        /// <paramref name="components"/>. The box goes first (plain arithmetic) so most components cost one COM
        /// call, and a subassembly no ray reaches is skipped with all its parts. Never the component itself, nor
        /// (when it is a subassembly) its own parts. Subassemblies are never hidden as a whole, since that could
        /// hide the very component being located: their parts are collected instead.
        /// </summary>
        private static void CollectObstacles(ComScope com, object[] components, string targetName,
            List<double[]> starts, List<Sightline> sightlines, List<Obstacle> into)
        {
            foreach (var obj in components ?? new object[0])
            {
                var c = obj as Component2;
                if (c == null)
                {
                    continue;
                }

                var box = c.GetBox(false, false) as double[];
                bool hasBox = box != null && box.Length >= 6;
                if (hasBox && !AnySightlineCrossesBox(starts, sightlines, box))
                {
                    continue;
                }

                // A hidden or suppressed subassembly takes all its parts with it.
                if (IsReallySuppressed(c) || c.IsHidden(false) ||
                    string.Equals(c.Name2, targetName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (c.IGetChildrenCount() > 0)
                {
                    CollectObstacles(com, com.TrackAll(c.GetChildren() as object[]), targetName, starts, sightlines, into);
                }
                else if (hasBox)
                {
                    into.Add(new Obstacle { Component = c, Box = box });
                }
            }
        }

        /// <summary>
        /// Marks the rays of <paramref name="sight"/> that a part blocks. With <paramref name="allBlockers"/> every
        /// part in the way is recorded in <see cref="Sightline.Blockers"/> (what method 1 hides); otherwise a ray
        /// already known to be blocked is not tested again. <paramref name="stopAtFirstBlock"/> gives up at the
        /// first blocked ray, for when only a fully free sightline matters.
        /// </summary>
        private static void FindBlockers(IModelDoc2 model, IModelDocExtension ext, ComScope com, List<double[]> starts,
            Sightline sight, List<Obstacle> obstacles, bool allBlockers, bool stopAtFirstBlock)
        {
            foreach (var o in obstacles)
            {
                var rays = new List<int>();
                for (int i = 0; i < starts.Count; i++)
                {
                    if ((allBlockers || !sight.Blocked[i]) && RayHitsBox(starts[i], sight.Dirs[i], sight.Lengths[i], o.Box))
                    {
                        rays.Add(i);
                    }
                }
                if (rays.Count == 0)
                {
                    continue;
                }

                var blocked = RaysBlockedBy(model, ext, com, o, starts, sight, rays);
                if (blocked.Count == 0)
                {
                    continue;
                }
                foreach (int i in blocked)
                {
                    sight.Blocked[i] = true;
                }
                if (allBlockers)
                {
                    sight.Blockers.Add(o);
                }
                else if (stopAtFirstBlock || sight.FreeCount == 0)
                {
                    return;
                }
            }
        }

        /// <summary>
        /// Which of <paramref name="rays"/> (indices into the sightline) cross a solid body of the part in front
        /// of their start. All of them when its solids are not available (SpeedPak or graphics-only component) or
        /// the hits cannot be read: the box test decides.
        /// </summary>
        private static List<int> RaysBlockedBy(IModelDoc2 model, IModelDocExtension ext, ComScope com, Obstacle o,
            List<double[]> starts, Sightline sight, List<int> rays)
        {
            if (!o.SolidsLoaded)
            {
                o.Solids = LoadSolids(com, o.Component, o.Box);
                o.SolidsLoaded = true;
            }
            if (o.Solids == null)
            {
                return rays;
            }

            var partStarts = new double[rays.Count][];
            var partDirs = new double[rays.Count][];
            var lengths = new double[rays.Count];
            for (int k = 0; k < rays.Count; k++)
            {
                partStarts[k] = TransformPoint(o.Solids.ToPart, starts[rays[k]]);
                partDirs[k] = TransformDirection(o.Solids.ToPart, sight.Dirs[rays[k]]);
                lengths[k] = sight.Lengths[rays[k]];
            }

            var hits = FireRays(model, ext, com, o.Solids, partStarts, partDirs, lengths);
            if (hits == null)
            {
                return rays;
            }

            var blocked = new List<int>();
            foreach (var h in hits)
            {
                if (!blocked.Contains(rays[h.Ray]))
                {
                    blocked.Add(rays[h.Ray]);
                }
            }
            return blocked;
        }

        /// <summary>
        /// The component's solid bodies with their part-space boxes and the inverse of its transform, or null
        /// when it has none (subassembly, SpeedPak, graphics-only). The body copies the rays are fired at are
        /// made later, only for the bodies a ray reaches.
        /// </summary>
        private static SolidSet LoadSolids(ComScope com, Component2 comp, double[] componentBox)
        {
            object bodiesInfo;
            var bodies = com.TrackAll(comp.GetBodies3((int)swBodyType_e.swSolidBody, out bodiesInfo) as object[]);
            if (bodies == null || bodies.Length == 0)
            {
                return null;
            }

            var xform = com.Track(comp.Transform2);
            var inverse = xform != null ? com.Track(xform.Inverse() as MathTransform) : null;
            var toAssembly = xform != null ? xform.ArrayData as double[] : null;
            var toPart = inverse != null ? inverse.ArrayData as double[] : null;
            if (toPart == null || toPart.Length < 13)
            {
                return null;
            }

            var set = new SolidSet { ToPart = toPart };
            foreach (var obj in bodies)
            {
                var body = obj as IBody2;
                if (body != null)
                {
                    set.Bodies.Add(new SolidBody { Body = body, Box = body.GetBodyBox() as double[] });
                }
            }
            if (set.Bodies.Count == 0)
            {
                return null;
            }

            if (!BodyBoxesInPartSpace(set.Bodies, toAssembly, componentBox))
            {
                foreach (var b in set.Bodies)
                {
                    b.Box = null;
                }
            }
            return set;
        }

        /// <summary>
        /// GetBodyBox is expected in part space like the rest of a component body's geometry. Checked rather than
        /// assumed: mapped to assembly space, the union of the body boxes must enclose the component box (with a
        /// margin, since both boxes may be loose). When it does not, the boxes are not used and every body is
        /// fired at: slower, never wrong.
        /// </summary>
        private static bool BodyBoxesInPartSpace(List<SolidBody> bodies, double[] toAssembly, double[] componentBox)
        {
            if (toAssembly == null || toAssembly.Length < 13 || componentBox == null || componentBox.Length < 6)
            {
                return false;
            }

            var union = new[] { double.MaxValue, double.MaxValue, double.MaxValue, double.MinValue, double.MinValue, double.MinValue };
            foreach (var b in bodies)
            {
                if (b.Box == null || b.Box.Length < 6)
                {
                    return false;
                }
                for (int a = 0; a < 3; a++)
                {
                    union[a] = Math.Min(union[a], b.Box[a]);
                    union[a + 3] = Math.Max(union[a + 3], b.Box[a + 3]);
                }
            }

            var mapped = new[] { double.MaxValue, double.MaxValue, double.MaxValue, double.MinValue, double.MinValue, double.MinValue };
            for (int k = 0; k < 8; k++)
            {
                var p = TransformPoint(toAssembly, new[]
                {
                    (k & 1) == 0 ? union[0] : union[3],
                    (k & 2) == 0 ? union[1] : union[4],
                    (k & 4) == 0 ? union[2] : union[5]
                });
                for (int a = 0; a < 3; a++)
                {
                    mapped[a] = Math.Min(mapped[a], p[a]);
                    mapped[a + 3] = Math.Max(mapped[a + 3], p[a]);
                }
            }

            double dx = componentBox[3] - componentBox[0], dy = componentBox[4] - componentBox[1], dz = componentBox[5] - componentBox[2];
            double margin = 0.05 * Math.Sqrt(dx * dx + dy * dy + dz * dz) + 1e-4;
            for (int a = 0; a < 3; a++)
            {
                if (componentBox[a] < mapped[a] - margin || componentBox[a + 3] > mapped[a + 3] + margin)
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Fires part-space rays at temporary copies of the bodies whose box one of them crosses (a body without
        /// a usable box is always fired at). Copies are made on first use and kept in the body for the next
        /// sightline. Returns the hits in front of each start and within its length (<see cref="RayHit.Ray"/>
        /// indexes the arrays given), or null when a copy fails or SolidWorks reports hits it does not let us read.
        /// </summary>
        private static List<RayHit> FireRays(IModelDoc2 model, IModelDocExtension ext, ComScope com, SolidSet solids,
            double[][] starts, double[][] dirs, double[] lengths)
        {
            // Wrapped in DispatchWrapper because RayIntersections reads BodiesIn as an array of IDispatch
            // pointers: a plain object[] marshals as an array of VARIANTs, which SolidWorks dereferences as
            // pointers (AccessViolationException).
            var copies = new List<DispatchWrapper>();
            foreach (var b in solids.Bodies)
            {
                if (b.Box != null && !AnyRayHitsBox(starts, dirs, lengths, b.Box))
                {
                    continue;
                }
                if (b.Copy == null)
                {
                    var copy = com.Track(b.Body.Copy2(false) as Body2);
                    if (copy == null)
                    {
                        return null;
                    }
                    b.Copy = new DispatchWrapper(copy);
                }
                copies.Add(b.Copy);
            }

            var hits = new List<RayHit>();
            if (copies.Count == 0)
            {
                return hits;
            }

            var points = new double[starts.Length * 3];
            var vectors = new double[starts.Length * 3];
            for (int k = 0; k < starts.Length; k++)
            {
                Array.Copy(starts[k], 0, points, k * 3, 3);
                Array.Copy(dirs[k], 0, vectors, k * 3, 3);
            }

            int count = ext.RayIntersections(copies.ToArray(), points, vectors,
                (int)(swRayPtsOpts_e.swRayPtsOptsENTRY_EXIT | swRayPtsOpts_e.swRayPtsOptsUNBLOCK), 0, 0, false);
            if (count <= 0)
            {
                return hits;
            }

            // Per hit: body index, ray index, hit type, x, y, z [, normal].
            var data = model.GetRayIntersectionsPoints() as double[];
            int stride = data != null ? data.Length / count : 0;
            if (stride < 6)
            {
                return null;
            }

            for (int h = 0; h < count; h++)
            {
                int i = h * stride;
                int ray = (int)data[i + 1];
                if (ray < 0 || ray >= starts.Length)
                {
                    continue;
                }

                var s = starts[ray];
                var d = dirs[ray];
                double t = (data[i + 3] - s[0]) * d[0] + (data[i + 4] - s[1]) * d[1] + (data[i + 5] - s[2]) * d[2];
                if (t > RayTolerance && t <= lengths[ray])
                {
                    hits.Add(new RayHit { Ray = ray, Type = (int)data[i + 2], T = t });
                }
            }
            return hits;
        }

        /// <summary>
        /// Hides the parts with SolidWorks' own "Ocultar componentes" on the selection (setting
        /// IComponent2.Visible changes the state but only shows after a manual rebuild). Graphics and
        /// FeatureManager updates are deliberately left on: with them frozen around the hide, the component
        /// selected afterwards did not show as selected.
        /// </summary>
        private static void HideComponents(IModelDoc2 model, List<Obstacle> parts)
        {
            model.ClearSelection2(true);
            if (parts.Count == 0)
            {
                return;
            }

            foreach (var o in parts)
            {
                o.Component.Select4(true, null, false);
            }
            model.HideComponent2();
            model.ClearSelection2(true);
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

        private static bool AllRaysFree(Sightline s)
        {
            return s.FreeCount == s.Blocked.Length;
        }

        /// <summary>The box-centre ray free (when it is one of the rays) and more than half of the rays free.</summary>
        private static bool CentreAndMostRaysFree(Sightline s, int centre)
        {
            return (centre < 0 || !s.Blocked[centre]) && s.FreeCount * 2 > s.Blocked.Length;
        }

        private static bool AnySightlineCrossesBox(List<double[]> starts, List<Sightline> sightlines, double[] box)
        {
            foreach (var s in sightlines)
            {
                if (AnyRayHitsBox(starts, s.Dirs, s.Lengths, box))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool AnyRayHitsBox(IList<double[]> starts, IList<double[]> dirs, IList<double> lengths, double[] box)
        {
            for (int i = 0; i < starts.Count; i++)
            {
                if (RayHitsBox(starts[i], dirs[i], lengths[i], box))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Slab test: does the ray segment 0 &lt; t ≤ <paramref name="length"/> cross the axis-aligned box
        /// [min xyz, max xyz]?
        /// </summary>
        private static bool RayHitsBox(double[] origin, double[] dir, double length, double[] box)
        {
            double tMin = 0, tMax = length;
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
        /// p·R·s + T with a MathTransform's ArrayData: rotation 0-8 (row-wise, as in <see cref="ViewerDirection"/>),
        /// translation 9-11, scale 12. What IMathPoint.MultiplyTransform does, without two COM objects per point.
        /// </summary>
        private static double[] TransformPoint(double[] m, double[] p)
        {
            double s = m[12] != 0 ? m[12] : 1;
            return new[]
            {
                (p[0] * m[0] + p[1] * m[3] + p[2] * m[6]) * s + m[9],
                (p[0] * m[1] + p[1] * m[4] + p[2] * m[7]) * s + m[10],
                (p[0] * m[2] + p[1] * m[5] + p[2] * m[8]) * s + m[11]
            };
        }

        /// <summary>Direction v·R, renormalised (translation and scale do not apply to a direction).</summary>
        private static double[] TransformDirection(double[] m, double[] v)
        {
            return Normalize(new[]
            {
                v[0] * m[0] + v[1] * m[3] + v[2] * m[6],
                v[0] * m[1] + v[1] * m[4] + v[2] * m[7],
                v[0] * m[2] + v[1] * m[5] + v[2] * m[8]
            });
        }

        private static double Dot(double[] a, double[] b)
        {
            return a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
        }

        private static double[] Cross(double[] a, double[] b)
        {
            return new[] { a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0] };
        }

        private static double[] Normalize(double[] v)
        {
            double len = Math.Sqrt(Dot(v, v));
            return len < 1e-12 ? null : new[] { v[0] / len, v[1] / len, v[2] / len };
        }

        /// <summary>Ray start points in assembly space.</summary>
        private sealed class RayStarts
        {
            public List<double[]> Points = new List<double[]>();

            /// <summary>Index of the box centre in <see cref="Points"/>, -1 when it is not one of them.</summary>
            public int Centre = -1;

            /// <summary>The component's box (assembly space), null if SolidWorks gave none.</summary>
            public double[] Box;

            /// <summary>Points dropped for lying outside the component's own solid.</summary>
            public int Discarded;
        }

        /// <summary>One way of looking at the component: a ray from every start point towards the viewer.</summary>
        private sealed class Sightline
        {
            public Sightline(double[] toViewer, int rays)
            {
                ToViewer = toViewer;
                Dirs = new double[rays][];
                Lengths = new double[rays];
                Blocked = new bool[rays];
            }

            /// <summary>Unit vector towards the viewer, assembly space.</summary>
            public readonly double[] ToViewer;

            /// <summary>Per ray: unit direction (assembly space) and how far it reaches.</summary>
            public readonly double[][] Dirs;
            public readonly double[] Lengths;

            public readonly bool[] Blocked;

            /// <summary>Every part in the way; filled only when the sightline is tested for all blockers.</summary>
            public readonly List<Obstacle> Blockers = new List<Obstacle>();

            public int FreeCount
            {
                get
                {
                    int free = 0;
                    foreach (bool b in Blocked)
                    {
                        if (!b) free++;
                    }
                    return free;
                }
            }
        }

        /// <summary>A visible part whose box some ray crosses. Its solids are loaded the first time a ray needs them.</summary>
        private sealed class Obstacle
        {
            public Component2 Component;
            public double[] Box;
            public bool SolidsLoaded;

            /// <summary>Null when no solids are available: the box test decides.</summary>
            public SolidSet Solids;
        }

        private sealed class SolidSet
        {
            /// <summary>Inverse of the component's Transform2 (ArrayData): assembly space to part space.</summary>
            public double[] ToPart;

            public List<SolidBody> Bodies = new List<SolidBody>();
        }

        private sealed class SolidBody
        {
            public IBody2 Body;

            /// <summary>Part-space box, null when unusable (the body is then always fired at).</summary>
            public double[] Box;

            /// <summary>Temporary copy the rays are fired at, made on first use.</summary>
            public DispatchWrapper Copy;
        }

        private struct RayHit
        {
            public int Ray;      // index into the rays fired
            public int Type;     // swRayPtsResults_e flags
            public double T;     // distance from the ray start
        }
    }
}
