using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SolidWorks.Interop.swpublished;
using VeradeAddin.Infrastructure;
using VeradeAddin.Logging;

namespace VeradeAddin.Services
{
    /// <summary>
    /// "Identificar rosca / taladro": a TOGGLE pick-mode. When the user activates the ribbon button it
    /// stays pressed and the add-in listens to the active assembly's selection events; every time the
    /// user clicks a hole's circular edge (or cylindrical face) a Measure-style <see cref="ICallout"/>
    /// is shown in the graphics area with the thread designation and diameters. Clicking the button
    /// again (or closing the document) leaves the mode.
    ///
    /// Recognition per click (best-effort, never throws into SolidWorks):
    ///   1) the face's owning feature IS a Hole Wizard  -> full data (thread + both diameters),
    ///   2) the face's owning feature IS a cosmetic thread,
    ///   3) fall back to measuring the cylinder, and match a sibling cosmetic thread by nominal
    ///      diameter to recover the thread callout for manually-threaded plain cuts.
    /// COM stays isolated here.
    /// </summary>
    public sealed partial class SolidWorksService
    {
        // --- pick-mode session state ----------------------------------------------------------------
        private bool _pickActive;
        private IModelDoc2 _pickModel;
        private DAssemblyDocEvents_Event _pickEvents;
        private DAssemblyDocEvents_NewSelectionNotifyEventHandler _pickSelHandler;
        private DAssemblyDocEvents_DestroyNotifyEventHandler _pickDestroyHandler;
        private ICallout _currentCallout;
        // Strong ref: SolidWorks holds the handler through a CCW, but keeping it here guarantees the
        // managed object outlives the callout so a drag callback can never hit a collected handler.
        private HoleCalloutHandler _currentCalloutHandler;
        // The entity the current callout is attached to, so we can tell an actual selection change
        // apart from the spurious NewSelectionNotify events fired while dragging the callout.
        private IEntity _calloutEntity;
        // Auto-resolve of lightweight components: one attempt per component per session, and never
        // two in flight (resolving fires selection events of its own).
        private readonly HashSet<string> _resolveAttempted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private bool _resolvePending;
        // Advanced-hole element stacks that could only be read through AccessSelections (which rolls the
        // model back), keyed by part path + feature name: read at most once per pick-mode session.
        private readonly Dictionary<string, List<AdvElement>> _advElementCache =
            new Dictionary<string, List<AdvElement>>(StringComparer.OrdinalIgnoreCase);
        private bool _advReadPending;
        // Holes already reported to the log, so the diagnostic is written once per hole, not per click.
        private readonly HashSet<string> _advLogged = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // Same, for the cosmetic threads found under a cut feature.
        private readonly HashSet<string> _threadLogged = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public bool IsHolePickModeActive()
        {
            return _pickActive;
        }

        /// <summary>Flips pick-mode on/off. Returns the NEW active state (true = now listening).</summary>
        public bool ToggleHolePickMode()
        {
            if (_pickActive)
            {
                DeactivatePick();
                _log.Log("IdentificarRosca", "Assembly", LogOutcome.Success, "Pick-mode OFF");
                return false;
            }

            bool on = ActivatePick();
            _log.Log("IdentificarRosca", "Assembly",
                on ? LogOutcome.Success : LogOutcome.Cancel,
                on ? "Pick-mode ON" : "Pick-mode not started (no active assembly)");
            return on;
        }

        private bool ActivatePick()
        {
            var model = _sw.IActiveDoc2 as IModelDoc2;
            if (model == null || model.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
            {
                ComRelease.Release(model);
                return false;
            }

            // Same wrapper seen through another interface, so this is ONE reference: it is held for the
            // whole pick session and given back by DeactivatePick.
            var ev = model as DAssemblyDocEvents_Event;
            if (ev == null)
            {
                ComRelease.Release(model);
                return false;
            }

            _pickModel = model;
            _pickEvents = ev;
            _pickSelHandler = OnPickNewSelection;
            _pickDestroyHandler = OnPickDestroy;

            try
            {
                ev.NewSelectionNotify += _pickSelHandler;
                ev.DestroyNotify += _pickDestroyHandler;
            }
            catch
            {
                DeactivatePick();
                return false;
            }

            _pickActive = true;

            // If something hole-like is already selected, show it right away.
            try { RefreshCalloutFromSelection(); } catch { }
            return true;
        }

        private void DeactivatePick()
        {
            try { if (_pickEvents != null && _pickSelHandler != null) _pickEvents.NewSelectionNotify -= _pickSelHandler; } catch { }
            try { if (_pickEvents != null && _pickDestroyHandler != null) _pickEvents.DestroyNotify -= _pickDestroyHandler; } catch { }

            HideCurrentCallout();

            // _pickEvents is the same wrapper as _pickModel: release once, after unhooking.
            ComRelease.Release(_pickModel);

            _pickEvents = null;
            _pickSelHandler = null;
            _pickDestroyHandler = null;
            _pickModel = null;
            _pickActive = false;
            _resolveAttempted.Clear();
            _advElementCache.Clear();
            _advLogged.Clear();
            _threadLogged.Clear();
            _advReadPending = false;
        }

        // NewSelectionNotify / DestroyNotify both return int (0 = proceed normally).
        private int OnPickNewSelection()
        {
            if (!_pickActive) return 0;
            try { RefreshCalloutFromSelection(); }
            catch (Exception ex)
            {
                _log.Log("IdentificarRosca", "Assembly", LogOutcome.Error, "Pick refresh failed", ex.ToString());
            }
            return 0;
        }

        private int OnPickDestroy()
        {
            DeactivatePick();
            return 0;
        }

        // --- read current selection -> show/update callout ------------------------------------------

        /// <summary>
        /// Runs on EVERY selection change while pick-mode is on, so everything it touches is released
        /// through <paramref name="com"/> — a click used to strand the selection manager, the picked
        /// geometry and, through the measuring helpers, a reference per face of the body.
        ///
        /// Two references deliberately outlive the scope: the entity carrying the callout (kept in
        /// <see cref="_calloutEntity"/> and released by <see cref="HideCurrentCallout"/>), and whatever
        /// the deferred timers need — those take ownership via <c>com.Keep</c>.
        /// </summary>
        private void RefreshCalloutFromSelection()
        {
            var model = _pickModel;
            if (model == null) return;

            using (var com = new ComScope())
            {
                var selMgr = com.Track(model.SelectionManager as ISelectionMgr);
                if (selMgr == null) return;

                IFace2 face = null;
                IEdge edge = null;
                Component2 comp = null;
                int selIndex = -1;

                int count = selMgr.GetSelectedObjectCount2(-1);
                for (int i = 1; i <= count; i++)
                {
                    int t = selMgr.GetSelectedObjectType3(i, -1);
                    if (t == (int)swSelectType_e.swSelFACES)
                    {
                        var f = com.Track(selMgr.GetSelectedObject6(i, -1) as IFace2);
                        // Cones count too: the conical face of a countersink (or of a countersunk screw
                        // head) is what the user naturally clicks on.
                        if (f != null && (IsCylindrical(f) || IsConical(f)))
                        {
                            face = f;
                            comp = com.Track(selMgr.GetSelectedObjectsComponent4(i, -1) as Component2);
                            selIndex = i;
                            break;
                        }
                    }
                    else if (t == (int)swSelectType_e.swSelEDGES)
                    {
                        var e = com.Track(selMgr.GetSelectedObject6(i, -1) as IEdge);
                        if (e != null && IsCircular(e))
                        {
                            edge = e;
                            comp = com.Track(selMgr.GetSelectedObjectsComponent4(i, -1) as Component2);
                            selIndex = i;
                            break;
                        }
                    }
                }

                if (face == null && edge == null)
                {
                    // Selection is not hole-like -> clear the last callout so nothing stale lingers.
                    HideCurrentCallout();
                    return;
                }

                // If the same entity is already annotated, leave the live callout untouched. Rebuilding
                // it on every NewSelectionNotify tears the callout down mid-drag, and SetCallout on the
                // torn-down object crashes SolidWorks with an AccessViolationException.
                //
                // Releasing this click's reference is safe even here: _calloutEntity holds one of its
                // own from the click that created it, and each acquisition is counted separately.
                // selOwner is the tracked reference behind selEnt (casting an RCW to another interface
                // hands back the same wrapper); keeping THAT is what stops the scope releasing it.
                object selOwner = (object)edge ?? face;
                IEntity selEnt = (edge as IEntity) ?? (face as IEntity);
                if (_currentCallout != null && _calloutEntity != null && selEnt != null
                    && SameCom(_calloutEntity, selEnt))
                {
                    return;
                }

                if (face == null)
                {
                    face = com.Track(HoleFaceOfEdge(edge));
                }

                // A lightweight component exposes geometry but no feature tree, so the thread can never
                // be read. Resolve it automatically (deferred — see ScheduleResolve) and annotate after.
                if (IsComponentLightweight(comp))
                {
                    HideCurrentCallout();
                    ScheduleResolve(com.Keep(comp), face); // ScheduleResolve owns comp from here on
                    return;
                }

                var desc = DescribeHole(com, face, edge, comp);
                if (desc == null || desc.Lines.Count == 0)
                {
                    HideCurrentCallout();
                    return;
                }

                // The entity outlives this scope: it is compared against the next selection, and
                // HideCurrentCallout gives the reference back when the callout is replaced.
                com.Keep(selOwner);
                ShowCallout(selMgr, selIndex, selEnt, desc);
            }
        }

        // ---- description ---------------------------------------------------------------------------

        /// <summary>Label/value rows to show, plus a short title. Value may be empty.</summary>
        private sealed class HoleDescription
        {
            public string Title;
            // Each entry is "Label|Value"; empty value => single-column row.
            public readonly List<string> Lines = new List<string>();

            public void Add(string label, string value)
            {
                Lines.Add(label + "|" + (value ?? string.Empty));
            }
        }

        private HoleDescription DescribeHole(ComScope com, IFace2 face, IEdge edge, Component2 comp)
        {
            var d = DescribeHoleCore(com, face, edge, comp);

            // A mirrored hole's face belongs to the mirror feature, not to the original Hole Wizard /
            // cosmetic thread, so everything we report about it is inferred from the source hole and may
            // not match reality. Flag it so the user does not trust the numbers blindly.
            if (d != null && d.Lines.Count > 0 && IsMirroredFeature(face))
            {
                d.Add("Simétrica", "datos no reales");
            }
            return d;
        }

        private HoleDescription DescribeHoleCore(ComScope com, IFace2 face, IEdge edge, Component2 comp)
        {
            var d = new HoleDescription();

            IFeature feat = com.Track(face != null ? face.GetFeature() as IFeature : null);
            object def = null;
            if (feat != null)
            {
                try { def = com.Track(feat.GetDefinition()); } catch { def = null; }
            }

            var wiz = def as IWizardHoleFeatureData2;
            if (wiz != null)
            {
                FillFromWizard(d, wiz);
                if (d.Lines.Count > 0) return d;
            }

            var adv = def as IAdvancedHoleFeatureData;
            if (adv != null)
            {
                FillFromAdvancedHole(com, d, feat, face, comp);
                if (d.Lines.Count > 0) return d;
            }

            var thread = def as ICosmeticThreadFeatureData;
            if (thread != null)
            {
                FillFromThread(d, thread);
                if (d.Lines.Count > 0) return d;
            }

            double boreDia = CylinderDiameter(face); // metres, 0 when not a cylinder

            // A cosmetic thread added by hand to a plain cut hangs UNDER that cut in the tree, so it is
            // looked up from the clicked face's OWN feature and tied back to that face. `plain` comes
            // back true when the feature's threads were all identified and NONE of them is this hole's:
            // the hole is then genuinely unthreaded, and the part-wide scan must not run and lend it the
            // thread of the hole next to it.
            bool plain;
            var scoped = FindThreadOnFace(feat, face, boreDia, out plain);

            // Fallback: measure the cylinder and try to recover a thread callout from a sibling
            // cosmetic-thread feature whose nominal diameter matches the bore.
            if (boreDia <= 0)
            {
                return DescribeCone(face, comp, scoped, plain);
            }

            var match = scoped ?? (plain ? null : FindCosmeticThreadByDiameter(comp, boreDia));
            if (match != null)
            {
                string label = ThreadLabel(match);
                d.Title = label;
                // A tapped bore is cut at the tap drill, always NARROWER than the nominal diameter; a
                // bore wider than nominal is clearance for a screw of that size, not the thread.
                bool tapped = match.NominalDia <= 0 || boreDia <= match.NominalDia * 1.02;
                if (label != null) d.Add(tapped ? "Rosca" : "Para rosca", label);
                if (match.NominalDia > 0) d.Add("Ø nominal", Mm(match.NominalDia));
                d.Add("Ø Agujero", Mm(boreDia));
                // Only the threaded bore has a thread depth; on a clearance hole it would be the depth
                // of somebody else's thread.
                if (tapped && match.Depth > 0) d.Add("Profundidad", Mm(match.Depth));
                return d;
            }

            // Plain (unthreaded) hole: just the measured diameter.
            d.Title = "Ø" + Mm(boreDia);
            d.Add("Ø Agujero", Mm(boreDia));
            return d;
        }

        /// <summary>
        /// Conical face with no feature data behind it: a countersink cut by hand, or the head of a
        /// countersunk screw. Both extreme diameters plus the included angle are reported, and the
        /// small end (= shank Ø on a screw) is used to look for a matching cosmetic thread — unless
        /// <paramref name="scoped"/> already carries the thread of the face's own feature, or
        /// <paramref name="plain"/> says that feature's threads belong to other holes of the same cut.
        /// </summary>
        private HoleDescription DescribeCone(IFace2 face, Component2 comp, ThreadMatch scoped, bool plain)
        {
            double dMin, dMax;
            if (!ConeDiameters(face, out dMin, out dMax))
            {
                return null;
            }

            var d = new HoleDescription();
            var match = scoped ?? (plain ? null : FindCosmeticThreadByDiameter(comp, dMin));
            if (match != null)
            {
                string label = ThreadLabel(match);
                d.Title = label;
                // A cone is an avellanado: clearance for the head of the screw, never the thread itself.
                if (label != null) d.Add("Para rosca", label);
                if (match.NominalDia > 0) d.Add("Ø nominal", Mm(match.NominalDia));
            }
            else
            {
                d.Title = "Avellanado Ø" + Mm(dMax);
            }

            d.Add("Ø avellanado (máx)", Mm(dMax));
            if (dMin > 0 && dMin < dMax) d.Add("Ø avellanado (mín)", Mm(dMin));

            double ang = ConeIncludedAngleDeg(face);
            if (ang > 0) d.Add("Ángulo", Deg(ang) + "°");
            return d;
        }

        private void FillFromThread(HoleDescription d, ICosmeticThreadFeatureData c)
        {
            string callout = Safe(() => c.ThreadCallout);
            string size = Safe(() => c.Size);
            double dia = SafeD(() => c.Diameter);        // metres
            double depth = SafeD(() => c.BlindDepth);     // metres, 0 when through

            d.Title = !string.IsNullOrWhiteSpace(callout) ? callout
                      : (!string.IsNullOrWhiteSpace(size) ? size : "Rosca");

            if (!string.IsNullOrWhiteSpace(callout)) d.Add("Rosca", callout);
            else if (!string.IsNullOrWhiteSpace(size)) d.Add("Rosca", size);
            if (dia > 0) d.Add("Ø nominal", Mm(dia));
            if (depth > 0) d.Add("Profundidad", Mm(depth));
        }

        private void FillFromWizard(HoleDescription d, IWizardHoleFeatureData2 w)
        {
            string fastener = Safe(() => w.FastenerSize);   // e.g. "M8x1.25" (includes the pitch)

            // The modelled bore (smaller) and the counter feature (larger), so both the minimum and
            // the maximum diameter of a countersink / counterbore hole are reported.
            double bore = FirstPositive(SafeD(() => w.HoleDiameter),
                                        SafeD(() => w.ThruHoleDiameter),
                                        SafeD(() => w.Diameter),
                                        SafeD(() => w.TapDrillDiameter));
            double cbore = SafeD(() => w.CounterBoreDiameter);
            double csink = SafeD(() => w.CounterSinkDiameter);
            double depth = FirstPositive(SafeD(() => w.HoleDepth), SafeD(() => w.Depth));

            // The hole TYPE decides, and only an unknown type falls back to the cosmetic-thread setting.
            bool? byType = ThreadedByHoleType(SafeI(() => w.Type));
            bool threaded = byType
                            ?? (SafeI(() => w.CosmeticThreadType) != (int)swWzdHoleCosmeticThreadTypes_e.swCosmeticThreadNone);

            d.Title = threaded && !string.IsNullOrWhiteSpace(fastener)
                      ? fastener
                      : (bore > 0 ? "Ø" + Mm(bore) : "Agujero");

            if (!string.IsNullOrWhiteSpace(fastener))
            {
                // Thread designation with its pitch (kept), without the tolerance class. A tapped hole
                // IS the thread ("Rosca"); a refrentado / avellanado / taladro / ranura is only sized
                // for a screw of that designation ("Para rosca").
                d.Add(threaded ? "Rosca" : "Para rosca", fastener);
            }
            if (bore > 0) d.Add("Ø agujero (mín)", Mm(bore));
            if (csink > 0) d.Add("Ø avellanado (máx)", Mm(csink));
            if (cbore > 0) d.Add("Ø caja (máx)", Mm(cbore));
            if (depth > 0) d.Add("Profundidad", Mm(depth));
        }

        /// <summary>
        /// Whether a Hole Wizard <see cref="swWzdHoleTypes_e"/> CUTS a thread. "Refrentado",
        /// "Avellanado", "Taladro" and "Ranura" are clearance for a screw, so they read "Para rosca";
        /// only the tap variants are the thread itself ("Rosca"). Matched by enum NAME because the tap
        /// variants are scattered across the enum. Null when the value is not a known member, so the
        /// caller can fall back to the cosmetic-thread setting.
        /// </summary>
        private static bool? ThreadedByHoleType(int wizardType)
        {
            string name = null;
            try { name = Enum.GetName(typeof(swWzdHoleTypes_e), wizardType); } catch { }
            if (string.IsNullOrEmpty(name)) return null;
            return ContainsTap(name);
        }

        /// <summary>
        /// "Tap" as a whole word: a "...TaperedTap" is a tap, a "...TaperedHole" is a conical DRILLED
        /// hole and must not be read as one.
        /// </summary>
        private static bool ContainsTap(string name)
        {
            int i = 0;
            while ((i = name.IndexOf("Tap", i, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                if (!name.Substring(i + 3).StartsWith("er", StringComparison.OrdinalIgnoreCase)) return true;
                i += 3;
            }
            return false;
        }

        // ---- "Taladro avanzado" --------------------------------------------------------------------

        /// <summary>
        /// One element of an advanced-hole stack. Snapshotted into plain data because the COM element
        /// objects stop reporting as soon as the feature releases selection access, and because the same
        /// list is reused on every click — nothing here is mutated by the matching.
        /// </summary>
        private sealed class AdvElement
        {
            public int Order;          // 0-based position inside its own side stack, outside -> inside
            public bool FarSide;
            public int Type;           // swAdvWzdGeneralHoleTypes_e
            public string Size;        // designation as SolidWorks writes it, e.g. "M8x1.25"
            public double Diameter;    // metres
            public double BlindDepth;  // metres, 0 when not blind
            public int EndCondition;   // swEndConditions_e

            public bool Tapped
            {
                get
                {
                    return Type == (int)swAdvWzdGeneralHoleTypes_e.swAdvWzdStraightTap
                        || Type == (int)swAdvWzdGeneralHoleTypes_e.swAdvWzdTaperTap;
                }
            }

            /// <summary>True when this element cuts a CONICAL face instead of a cylindrical one.</summary>
            public bool Conical
            {
                get
                {
                    return Type == (int)swAdvWzdGeneralHoleTypes_e.swAdvWzdCounterSink
                        || Type == (int)swAdvWzdGeneralHoleTypes_e.swAdvWzdTaperTap;
                }
            }
        }

        /// <summary>One cylindrical / conical stretch of a hole, measured from the model.</summary>
        private sealed class HoleSegment
        {
            public bool Cone;
            public double DiaMin;      // metres (== DiaMax on a cylinder)
            public double DiaMax;
            public double Angle;       // degrees, cones only
            public double TMin;        // axial extent, metres
            public double TMax;
            public bool Clicked;
            public AdvElement El;      // stack element that cut it, null when unmatched
            public ThreadMatch Thread; // cosmetic thread cutting this stretch, null when plain
            public string TypeEs;      // "Caja" / "Avellanado" / "Rosca" / "Agujero"

            public double Length { get { return TMax - TMin; } }
        }

        /// <summary>
        /// "Taladro avanzado": a STACK of elements (counterbore / countersink / straight bore / straight
        /// or tapered tap), each one cutting its own face.
        ///
        /// Two sources are combined. <see cref="IAdvancedHoleFeatureData.GetNearSideElements"/> /
        /// <c>GetFarSideElements</c> give WHAT was drilled: the designation of every element
        /// (<see cref="IAdvancedHoleElementData.Size"/> = "M8x1.25"), its type and its end condition,
        /// in stack order. The geometry gives WHERE: the coaxial faces the feature cut, measured and
        /// ordered along the axis, one per element, with the clicked one flagged. Position — which
        /// element the user actually clicked — comes from matching those two ORDERED lists
        /// (<see cref="MatchElementsByPosition"/>), not from guessing by diameter.
        ///
        /// When the elements cannot be read the stretches are still described from the model alone,
        /// with the thread designations recovered from the feature's own cosmetic threads.
        /// </summary>
        private void FillFromAdvancedHole(ComScope com, HoleDescription d, IFeature feat, IFace2 face, Component2 comp)
        {
            var segs = MeasureHoleStack(feat, face);
            if (segs.Count == 0) return;

            var els = GetAdvancedElements(com, feat, face, comp);
            if (els.Count > 0)
            {
                MatchElementsByPosition(segs, els);
            }
            else
            {
                OrderWidestFirst(segs);
            }

            MatchThreadsToSegments(segs, CollectThreads(feat));
            ClassifySegments(segs);

            HoleSegment hit = null;
            foreach (var s in segs)
            {
                if (s.Clicked) { hit = s; break; }
            }
            if (hit == null) hit = segs[0];

            DescribeSegment(d, hit);

            d.Add("Taladro avanzado", segs.Count + (segs.Count == 1 ? " tramo" : " tramos"));
            for (int i = 0; i < segs.Count; i++)
            {
                d.Add("Tramo " + (i + 1), SegmentValue(segs[i]) + (segs[i].Clicked ? "  ◄" : string.Empty));
            }
        }

        /// <summary>Detail rows for the stretch the user clicked on.</summary>
        private static void DescribeSegment(HoleDescription d, HoleSegment s)
        {
            var el = s.El;
            string size = el != null ? el.Size : null;
            // The element's own designation wins: it is what the hole was DEFINED with. The cosmetic
            // thread is the fallback for when the element stack could not be read.
            string thread = el != null
                ? (el.Tapped && !string.IsNullOrWhiteSpace(size) ? size : null)
                : ThreadLabel(s.Thread);

            if (thread != null)
            {
                d.Title = thread;
                d.Add("Rosca", thread);
                if (el == null && s.Thread.NominalDia > 0) d.Add("Ø nominal", Mm(s.Thread.NominalDia));
                else if (el != null && el.Diameter > 0) d.Add("Ø nominal", Mm(el.Diameter));
                d.Add(s.Cone ? "Ø broca (máx)" : "Ø broca", Mm(s.DiaMax));
                if (s.Cone && s.Angle > 0) d.Add("Ángulo", Deg(s.Angle) + "°");
            }
            else
            {
                d.Title = (s.Cone ? "Avellanado Ø" : "Ø") + Mm(s.DiaMax);
                d.Add("Tipo", s.TypeEs);
                // A clearance element carries the size of the screw it is meant for, not a thread.
                if (!string.IsNullOrWhiteSpace(size)) d.Add("Para rosca de", size);

                if (s.Cone)
                {
                    d.Add("Ø avellanado (máx)", Mm(s.DiaMax));
                    if (s.DiaMin > 0 && s.DiaMin < s.DiaMax) d.Add("Ø avellanado (mín)", Mm(s.DiaMin));
                    if (s.Angle > 0) d.Add("Ángulo", Deg(s.Angle) + "°");
                }
                else
                {
                    d.Add("Ø agujero", Mm(s.DiaMax));
                }
            }

            if (s.Length > 0) d.Add("Longitud tramo", Mm(s.Length));
            if (el != null) d.Add("Lado", el.FarSide ? "Lejano" : "Cercano");
        }

        private static string SegmentValue(HoleSegment s)
        {
            var el = s.El;
            string label = el != null && !string.IsNullOrWhiteSpace(el.Size) ? el.Size : ThreadLabel(s.Thread);
            string v;

            if (label != null)
            {
                v = s.TypeEs + " " + label;
            }
            else if (s.Cone)
            {
                v = "Avellanado Ø" + Mm(s.DiaMax);
                if (s.Angle > 0) v += " " + Deg(s.Angle) + "°";
            }
            else
            {
                v = s.TypeEs + " Ø" + Mm(s.DiaMax);
            }

            if (s.Length > 0) v += " × " + Mm(s.Length);
            return v;
        }

        private static string ThreadLabel(ThreadMatch t)
        {
            if (t == null) return null;
            if (!string.IsNullOrWhiteSpace(t.Callout)) return t.Callout;
            return !string.IsNullOrWhiteSpace(t.Size) ? t.Size : null;
        }

        /// <summary>
        /// Names every stretch. The element type is authoritative when it was read; otherwise the name
        /// comes from what the stretch IS in the model: a threaded one is a thread, a cone is a
        /// countersink, and a plain cylinder wider than the narrowest bore of the stack is the
        /// counterbore that leads to it (works from either end, so a far-side counterbore is named too).
        /// </summary>
        private static void ClassifySegments(List<HoleSegment> segs)
        {
            double minDia = double.MaxValue;
            foreach (var s in segs)
            {
                if (!s.Cone && s.DiaMax > 0 && s.DiaMax < minDia) minDia = s.DiaMax;
            }

            foreach (var s in segs)
            {
                if (s.El != null) s.TypeEs = TypeNameEs(s.El.Type);
                else if (s.Thread != null) s.TypeEs = s.Cone ? "Rosca cónica" : "Rosca";
                else if (s.Cone) s.TypeEs = "Avellanado";
                else if (minDia < double.MaxValue && s.DiaMax > minDia * 1.01) s.TypeEs = "Caja";
                else s.TypeEs = "Agujero";
            }
        }

        private static string TypeNameEs(int type)
        {
            switch (type)
            {
                case (int)swAdvWzdGeneralHoleTypes_e.swAdvWzdCounterBore: return "Caja";
                case (int)swAdvWzdGeneralHoleTypes_e.swAdvWzdCounterSink: return "Avellanado";
                case (int)swAdvWzdGeneralHoleTypes_e.swAdvWzdStraightTap: return "Rosca";
                case (int)swAdvWzdGeneralHoleTypes_e.swAdvWzdTaperTap: return "Rosca cónica";
                default: return "Agujero";
            }
        }

        /// <summary>
        /// Joins the measured stretches to the feature's own cosmetic threads by diameter, best fit
        /// first, one thread per stretch. The bore is cut at the tap-drill size, always SMALLER than the
        /// nominal diameter the thread declares, hence the one-sided tolerance. Only used for stretches
        /// no element claimed.
        /// </summary>
        private static void MatchThreadsToSegments(List<HoleSegment> segs, List<ThreadMatch> threads)
        {
            if (threads.Count == 0) return;

            var used = new HashSet<ThreadMatch>();
            while (true)
            {
                HoleSegment bestSeg = null;
                ThreadMatch bestTh = null;
                double bestCost = double.MaxValue;

                foreach (var s in segs)
                {
                    if (s.Thread != null || s.El != null || s.DiaMax <= 0) continue;
                    foreach (var t in threads)
                    {
                        if (t.NominalDia <= 0 || used.Contains(t)) continue;

                        double gap = t.NominalDia - s.DiaMax;                  // > 0 on a tapped bore
                        if (gap < -t.NominalDia * 0.02) continue;              // bore wider than nominal
                        double cost = Math.Abs(gap);
                        if (cost > t.NominalDia * 0.25 + 0.0005 || cost >= bestCost) continue;

                        bestCost = cost;
                        bestSeg = s;
                        bestTh = t;
                    }
                }

                if (bestSeg == null) return;
                bestSeg.Thread = bestTh;
                used.Add(bestTh);
            }
        }

        /// <summary>
        /// Every cosmetic thread the feature keeps underneath itself — an advanced hole (like a hole
        /// wizard) creates one per tapped element — read WITHOUT touching
        /// <see cref="IAdvancedHoleFeatureData"/>, so the model is never rolled back and the live
        /// selection and callout survive. The whole sub-tree is walked because the thread can hang one
        /// or two levels down, and each sub-feature is probed by CAST: they are not all named
        /// "CosmeticThread".
        /// </summary>
        private static List<ThreadMatch> CollectThreads(IFeature feat)
        {
            var list = new List<ThreadMatch>();
            CollectThreadsInto(list, feat, 0);
            return list;
        }

        private static void CollectThreadsInto(List<ThreadMatch> into, IFeature feat, int depth)
        {
            if (feat == null || depth > 4) return;

            var sub = Try(() => feat.GetFirstSubFeature()) as IFeature;
            while (sub != null)
            {
                var cur = sub;

                object def = null;
                try { def = cur.GetDefinition(); } catch { }
                var data = def as ICosmeticThreadFeatureData;
                if (data != null)
                {
                    double nominal = SafeD(() => data.Diameter);
                    if (nominal > 0)
                    {
                        into.Add(new ThreadMatch
                        {
                            Callout = Safe(() => data.ThreadCallout),
                            Size = Safe(() => data.Size),
                            NominalDia = nominal
                        });
                    }
                }
                else
                {
                    CollectThreadsInto(into, cur, depth + 1);
                }

                sub = Try(() => cur.GetNextSubFeature()) as IFeature;
                ComRelease.Release(def);
                ComRelease.Release(cur);
            }
        }

        // ---- advanced-hole elements ----------------------------------------------------------------

        /// <summary>
        /// The feature's element stacks. Tried FIRST without <c>AccessSelections</c>: when SolidWorks
        /// serves the arrays straight away nothing is disturbed and the data is fresh on every click.
        /// Only when that returns nothing is the read DEFERRED to
        /// <see cref="ScheduleAdvancedRead"/>, which pays for AccessSelections (it rolls the part back,
        /// drops the selection and tears the callout down) exactly once per hole and caches the result.
        /// </summary>
        private List<AdvElement> GetAdvancedElements(ComScope com, IFeature feat, IFace2 face, Component2 comp)
        {
            string key = AdvCacheKey(feat, comp);
            List<AdvElement> cached;
            if (key != null && _advElementCache.TryGetValue(key, out cached)) return cached;

            object def = Try(() => feat.GetDefinition());
            List<AdvElement> direct;
            try { direct = ReadElements(def as IAdvancedHoleFeatureData); }
            finally { ComRelease.Release(def); }

            if (direct.Count > 0)
            {
                LogElements(key, "lectura directa", direct);
                return direct;
            }

            ScheduleAdvancedRead(com, key, feat, face, comp);
            return direct;
        }

        /// <summary>Writes the stack to the log once per hole, so the source of the data is traceable.</summary>
        private void LogElements(string key, string how, List<AdvElement> els)
        {
            if (key == null || !_advLogged.Add(key)) return;

            var parts = new List<string>();
            foreach (var el in els)
            {
                parts.Add((el.FarSide ? "Lejano" : "Cercano") + "[" + el.Order + "] " +
                          TypeNameEs(el.Type) + " " + el.Size + " Ø" + Mm(el.Diameter));
            }
            _log.Log("IdentificarRosca", "Assembly", LogOutcome.Success,
                "Taladro avanzado (" + how + "): " + els.Count + " elementos", string.Join(" | ", parts));
        }

        /// <summary>Both stacks as plain data. Empty when the arrays are not being served.</summary>
        private static List<AdvElement> ReadElements(IAdvancedHoleFeatureData a)
        {
            var list = new List<AdvElement>();
            if (a == null) return list;
            Snapshot(list, Try(() => a.GetNearSideElements()), false);
            Snapshot(list, Try(() => a.GetFarSideElements()), true);
            return list;
        }

        private static void Snapshot(List<AdvElement> into, object raw, bool farSide)
        {
            var arr = raw as object[];
            if (arr == null) return;

            // The elements are snapshotted into plain data right here, so none of them outlives this
            // loop and every one is given back.
            using (var com = new ComScope())
            {
                com.TrackAll(arr);

                int i = 0;
                foreach (var o in arr)
                {
                    var el = o as IAdvancedHoleElementData;
                    if (el == null) continue;
                    into.Add(new AdvElement
                    {
                        Order = i++,
                        FarSide = farSide,
                        Type = SafeI(() => el.ElementType),
                        Size = Safe(() => el.Size),
                        Diameter = SafeD(() => el.Diameter),
                        BlindDepth = SafeD(() => el.BlindDepth),
                        EndCondition = SafeI(() => el.EndCondition)
                    });
                }
            }
        }

        /// <summary>
        /// Fallback read, outside the selection notification and on a one-shot timer, because
        /// AccessSelections re-enters SolidWorks and must never run inside NewSelectionNotify. The
        /// outcome is cached even when empty: one attempt per hole, so a feature that cannot be read
        /// never rolls the model back twice. The rollback DROPS THE SELECTION, so a point on the clicked
        /// face is captured beforehand and the face is selected again afterwards.
        /// </summary>
        private void ScheduleAdvancedRead(ComScope com, string key, IFeature feat, IFace2 face, Component2 comp)
        {
            if (key == null || feat == null || _advReadPending) return;

            double[] pt = FacePoint(face);
            _advReadPending = true;

            // The callback runs after the caller's scope is gone, so the two references it needs are
            // taken out of that scope and released here instead.
            com.Keep(feat);
            com.Keep(comp);

            var timer = new System.Windows.Forms.Timer { Interval = 1 };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                timer.Dispose();
                try
                {
                    _advElementCache[key] = ReadAdvancedElementsNow(feat, comp);

                    // Drop the live callout so the rebuild is not skipped as "same entity".
                    HideCurrentCallout();
                    if (!ReselectFace(comp, pt))
                    {
                        // Selection survived the rollback -> just describe it again.
                        RefreshCalloutFromSelection();
                    }
                    // Otherwise the re-selection fires NewSelectionNotify, which rebuilds the callout.
                }
                catch (Exception ex)
                {
                    _log.Log("IdentificarRosca", "Assembly", LogOutcome.Error,
                        "Lectura de taladro avanzado falló", ex.ToString());
                }
                finally
                {
                    _advReadPending = false;
                    ComRelease.Release(feat);
                    ComRelease.Release(comp);
                }
            };
            timer.Start();
        }

        /// <summary>
        /// The AccessSelections read. The definition is re-fetched here rather than carried over from
        /// the notification, and the access is ALWAYS released — a granted access left open keeps the
        /// part rolled back and breaks every following click. Graphics and feature tree are frozen so
        /// the rollback is not drawn.
        /// </summary>
        private List<AdvElement> ReadAdvancedElementsNow(IFeature feat, Component2 comp)
        {
            var list = new List<AdvElement>();
            object def = Try(() => feat.GetDefinition());
            var a = def as IAdvancedHoleFeatureData;
            if (a == null)
            {
                ComRelease.Release(def);
                return list;
            }

            // _pickModel is the session's active document: it is NOT ours to release, so `part` is only
            // given back when it is the component's own document.
            var part = comp != null ? comp.GetModelDoc2() as IModelDoc2 : _pickModel;
            bool ownsPart = !ReferenceEquals(part, _pickModel);
            var view = _pickModel != null ? _pickModel.ActiveView as IModelView : null;
            var featMgr = part != null ? part.FeatureManager : null;

            bool graphics = true, tree = true;
            try
            {
                if (view != null) { graphics = view.EnableGraphicsUpdate; view.EnableGraphicsUpdate = false; }
                if (featMgr != null) { tree = featMgr.EnableFeatureTree; featMgr.EnableFeatureTree = false; }

                // Part context first: rolling the part back does not drag the assembly into edit mode.
                ReadStack(list, a, part, null);
                if (list.Count == 0 && comp != null) ReadStack(list, a, _pickModel, comp);
            }
            catch (Exception ex)
            {
                _log.Log("IdentificarRosca", "Assembly", LogOutcome.Error,
                    "AccessSelections del taladro avanzado falló", ex.ToString());
            }
            finally
            {
                try { if (featMgr != null) featMgr.EnableFeatureTree = tree; } catch { }
                try { if (view != null) view.EnableGraphicsUpdate = graphics; } catch { }
                ComRelease.Release(featMgr);
                ComRelease.Release(view);
                if (ownsPart) ComRelease.Release(part);
                ComRelease.Release(def);
            }

            if (list.Count > 0)
            {
                LogElements(AdvCacheKey(feat, comp), "AccessSelections", list);
            }
            else
            {
                _log.Log("IdentificarRosca", "Assembly", LogOutcome.Cancel,
                    "Taladro avanzado " + Safe(() => feat.Name) + ": sin elementos legibles");
            }

            return list;
        }

        private static void ReadStack(List<AdvElement> into, IAdvancedHoleFeatureData a, object topDoc, object comp)
        {
            try { a.AccessSelections(topDoc, comp); } catch { }
            try { Snapshot(into, Try(() => a.GetNearSideElements()), false); Snapshot(into, Try(() => a.GetFarSideElements()), true); }
            finally { try { a.ReleaseSelectionAccess(); } catch { } }
        }

        private static string AdvCacheKey(IFeature feat, Component2 comp)
        {
            string f = feat != null ? Safe(() => feat.Name) : null;
            if (string.IsNullOrEmpty(f)) return null;
            string c = comp != null ? Safe(() => comp.GetPathName()) : string.Empty;
            return c + "|" + f;
        }

        // ---- position: which element cut which stretch ----------------------------------------------

        /// <summary>
        /// Assigns elements to stretches BY POSITION. Both lists are ordered: the near-side stack runs
        /// from the face the hole starts at inwards, the far-side stack from the opposite face inwards,
        /// and <paramref name="segs"/> is sorted along the axis. Which end of the axis is the near side
        /// is not known, so both orientations are laid out and the one whose diameters agree best wins.
        /// The winning layout is applied and, when the hole runs the other way, the stretch list is
        /// flipped so "Tramo 1" is always the near-side opening.
        /// </summary>
        private static void MatchElementsByPosition(List<HoleSegment> segs, List<AdvElement> els)
        {
            var near = SideStack(els, false);
            var far = SideStack(els, true);
            int last = segs.Count - 1;

            // Which end of the axis is the near side, and whether element 0 of a stack is the outermost
            // one, are both unstated by the API, so all four readings are laid out and scored. Ties keep
            // the natural one (near side at the low end of the axis, element 0 outermost).
            AdvElement[] best = null;
            double bestCost = double.MaxValue;
            bool bestFlip = false;

            for (int variant = 0; variant < 4; variant++)
            {
                bool flip = (variant & 1) != 0;          // near side sits at the high end of the axis
                bool inward = (variant & 2) != 0;        // element 0 is the innermost of its stack
                var n = inward ? Reversed(near) : near;
                var f = inward ? Reversed(far) : far;

                var map = new AdvElement[segs.Count];
                double cost = flip
                    ? Lay(segs, n, last, -1, map) + Lay(segs, f, 0, +1, map)
                    : Lay(segs, n, 0, +1, map) + Lay(segs, f, last, -1, map);

                if (cost >= bestCost) continue;
                bestCost = cost;
                best = map;
                bestFlip = flip;
            }

            if (best == null) return;
            for (int i = 0; i < segs.Count; i++) segs[i].El = best[i];

            // "Tramo 1" is always the near-side opening.
            if (bestFlip) segs.Reverse();
        }

        private static List<AdvElement> Reversed(List<AdvElement> src)
        {
            var copy = new List<AdvElement>(src);
            copy.Reverse();
            return copy;
        }

        /// <summary>One side's elements in stack order.</summary>
        private static List<AdvElement> SideStack(List<AdvElement> els, bool farSide)
        {
            var side = new List<AdvElement>();
            foreach (var el in els)
            {
                if (el.FarSide == farSide) side.Add(el);
            }
            side.Sort((x, y) => x.Order.CompareTo(y.Order));
            return side;
        }

        /// <summary>
        /// Walks one stack over the stretches from <paramref name="start"/> in direction
        /// <paramref name="step"/>, writing the assignment into <paramref name="map"/> and returning how
        /// badly the diameters disagree. Free stretches whose shape does not fit the element (a drill
        /// tip left at the bottom of a bore, say) are stepped over.
        /// </summary>
        private static double Lay(List<HoleSegment> segs, List<AdvElement> stack, int start, int step, AdvElement[] map)
        {
            const double Unmatched = 0.01; // 10 mm of "cost" for an element no stretch took
            double cost = 0;
            int i = start;

            foreach (var el in stack)
            {
                int probe = i, skipped = 0;
                while (probe >= 0 && probe < segs.Count && skipped < 3
                       && (map[probe] != null || segs[probe].Cone != el.Conical))
                {
                    probe += step;
                    skipped++;
                }

                if (probe < 0 || probe >= segs.Count || map[probe] != null || segs[probe].Cone != el.Conical)
                {
                    cost += Unmatched;
                    continue;
                }

                map[probe] = el;
                cost += PairCost(el, segs[probe]);
                i = probe + step;
            }
            return cost;
        }

        /// <summary>
        /// How far the element's declared diameter is from the measured one. A tap declares its NOMINAL
        /// diameter while the bore is cut at the tap-drill size (~0.75-1.0 x nominal), so anything inside
        /// that band costs nothing.
        /// </summary>
        private static double PairCost(AdvElement el, HoleSegment s)
        {
            if (el.Diameter <= 0 || s.DiaMax <= 0) return 0.001;
            if (el.Tapped)
            {
                double lo = el.Diameter * 0.72, hi = el.Diameter * 1.02;
                if (s.DiaMax < lo) return lo - s.DiaMax;
                return s.DiaMax > hi ? s.DiaMax - hi : 0;
            }
            return Math.Abs(el.Diameter - s.DiaMax);
        }

        /// <summary>
        /// Display order when there are no elements to say which end the hole starts at: the widest
        /// opening first, which is the end an advanced hole is normally drilled from.
        /// </summary>
        private static void OrderWidestFirst(List<HoleSegment> segs)
        {
            if (segs.Count > 1 && segs[segs.Count - 1].DiaMax > segs[0].DiaMax) segs.Reverse();
        }

        /// <summary>
        /// Every cylindrical / conical face cut by <paramref name="feat"/> that shares the clicked
        /// face's axis, measured and ordered along that axis (ascending). The clicked face is flagged.
        /// </summary>
        private static List<HoleSegment> MeasureHoleStack(IFeature feat, IFace2 face)
        {
            var segs = new List<HoleSegment>();
            double[] axis = HoleAxis(face);
            if (axis == null) return segs;

            var coaxial = CoaxialFaces(face, axis);
            try
            {
                foreach (var f in coaxial)
                {
                    // Same feature only: a different hole crossing this axis is not part of the stack.
                    var owner = Try(() => f.GetFeature());
                    bool sameFeature = feat == null || SameCom(feat, owner);
                    ComRelease.Release(owner);
                    if (!sameFeature) continue;

                    var s = MeasureSegment(f, axis);
                    if (s == null) continue;
                    s.Clicked = SameCom(face, f);
                    segs.Add(s);
                }
            }
            finally { ComRelease.ReleaseAll(coaxial); }

            segs.Sort((x, y) => x.TMin.CompareTo(y.TMin));
            return segs;
        }

        private static HoleSegment MeasureSegment(IFace2 f, double[] axis)
        {
            var s = new HoleSegment();
            if (IsConical(f))
            {
                double dMin, dMax;
                if (!ConeDiameters(f, out dMin, out dMax)) return null;
                s.Cone = true;
                s.DiaMin = dMin;
                s.DiaMax = dMax;
                s.Angle = ConeIncludedAngleDeg(f);
            }
            else
            {
                double dia = CylinderDiameter(f);
                if (dia <= 0) return null;
                s.DiaMin = s.DiaMax = dia;
            }

            double tMin, tMax;
            if (!FaceAxialExtent(f, axis, out tMin, out tMax))
            {
                tMin = tMax = AxialCoord(f, axis);
            }
            s.TMin = tMin;
            s.TMax = tMax;
            return s;
        }

        /// <summary>
        /// Cosmetic thread created BY this feature — an advanced hole keeps the thread of its tapped
        /// element underneath itself — whose nominal diameter matches the measured bore. The whole
        /// sub-tree is walked (the thread can hang one or two levels down) and, since this is a small
        /// scoped search, every sub-feature is probed by CAST instead of by type name: the sub-features
        /// an advanced hole creates are not all named "CosmeticThread".
        /// </summary>
        private static ThreadMatch FindThreadInFeature(IFeature feat, double boreDia)
        {
            if (feat == null || boreDia <= 0) return null;

            var sub = Try(() => feat.GetFirstSubFeature()) as IFeature;
            while (sub != null)
            {
                var cur = sub;
                var hit = ThreadOf(cur, boreDia) ?? FindThreadInFeature(cur, boreDia);
                if (hit != null)
                {
                    ComRelease.Release(cur);
                    return hit;
                }
                sub = Try(() => cur.GetNextSubFeature()) as IFeature;
                ComRelease.Release(cur);
            }
            return null;
        }

        /// <summary>
        /// The feature read as a cosmetic thread whose nominal diameter matches the bore, or null when
        /// it is not a cosmetic thread / does not match. The modelled bore is the tap drill, always
        /// smaller than the nominal diameter, hence the generous tolerance.
        /// </summary>
        private static ThreadMatch ThreadOf(IFeature f, double boreDia)
        {
            object def = null;
            try { def = f.GetDefinition(); } catch { }
            try
            {
                var data = def as ICosmeticThreadFeatureData;
                if (data == null) return null;

                double nominal = SafeD(() => data.Diameter);
                if (nominal <= 0 || Math.Abs(nominal - boreDia) > boreDia * 0.25 + 0.0005) return null;

                return new ThreadMatch
                {
                    Callout = Safe(() => data.ThreadCallout),
                    Size = Safe(() => data.Size),
                    NominalDia = nominal,
                    Depth = SafeD(() => data.BlindDepth)
                };
            }
            finally { ComRelease.Release(def); }
        }

        // ---- cosmetic thread of the clicked face's own feature -------------------------------------

        // How sure we are that a cosmetic thread is the CLICKED face's, best first.
        private const int RankOnFace = 0;     // its edge touches this face
        private const int RankCoaxial = 1;    // its edge is coaxial with this face
        private const int RankOtherHole = 2;  // its edge was read, and it is somewhere else
        private const int RankUnknown = 3;    // its edge could not be read: no evidence either way

        /// <summary>A cosmetic thread found under the clicked face's feature, with how sure we are.</summary>
        private sealed class ScopedThread
        {
            public ThreadMatch Match;
            public int Rank;
        }

        /// <summary>
        /// The cosmetic thread of the face the user clicked, read from that face's OWN feature instead
        /// of by scanning the part for a thread of a similar diameter. This is the case of a thread
        /// added by hand to a plain cut: SolidWorks nests it under the cut, so
        /// <see cref="IFeature.GetFirstSubFeature"/> from the face's owner finds it.
        ///
        /// One cut can bore SEVERAL holes and carry one thread per hole, so every thread is collected
        /// and tied back to the clicked face through <see cref="ICosmeticThreadFeatureData.Edge"/> (see
        /// <see cref="RankThreadEdge"/>) — the whole point being that the thread of the hole NEXT TO
        /// the clicked one is not reported on it.
        ///
        /// <paramref name="plain"/> comes back true when every thread of the feature was located and
        /// none of them is on this face: the hole is then unthreaded as a matter of fact, and the caller
        /// must NOT fall back to the part-wide diameter scan — that scan has no idea which hole is which
        /// and would hand back the sibling's thread again.
        /// </summary>
        private ThreadMatch FindThreadOnFace(IFeature feat, IFace2 face, double bore, out bool plain)
        {
            plain = false;
            if (feat == null || face == null) return null;

            var found = new List<ScopedThread>();
            CollectScopedThreads(found, feat, face, HoleAxis(face), 0);
            if (found.Count == 0) return null;

            LogScopedThreads(feat, found);

            // Best evidence wins; between equally-evidenced threads the nearest nominal diameter does.
            ScopedThread best = null;
            foreach (var t in found)
            {
                if (t.Rank > RankCoaxial) continue;
                if (best == null || t.Rank < best.Rank
                    || (t.Rank == best.Rank && DiaCost(t, bore) < DiaCost(best, bore)))
                {
                    best = t;
                }
            }
            if (best != null) return best.Match;

            // Nothing is on this face. Every thread accounted for -> the hole is plain, full stop.
            bool blind = false;
            foreach (var t in found)
            {
                if (t.Rank == RankUnknown) { blind = true; break; }
            }
            if (!blind)
            {
                plain = true;
                return null;
            }

            // Some edge could not be read, so absence of a match proves nothing: fall back to the
            // diameter, with the same tolerance the part-wide scan uses, but still restricted to the
            // threads of THIS feature. Anything wider than nominal is clearance, not a thread.
            foreach (var t in found)
            {
                if (t.Rank != RankUnknown || t.Match.NominalDia <= 0 || bore <= 0) continue;
                if (Math.Abs(t.Match.NominalDia - bore) > bore * 0.25 + 0.0005) continue;
                if (best == null || DiaCost(t, bore) < DiaCost(best, bore)) best = t;
            }
            return best != null ? best.Match : null;
        }

        /// <summary>
        /// Writes what the scoped search had to work with, once per feature: which threads hang under it
        /// and — the part that decides everything — whether their edge could be read at all. An
        /// unreadable edge is the one case where the search cannot tell a plain hole from a threaded one.
        /// </summary>
        private void LogScopedThreads(IFeature feat, List<ScopedThread> found)
        {
            string name = Safe(() => feat.Name);
            if (string.IsNullOrEmpty(name) || !_threadLogged.Add(name)) return;

            var parts = new List<string>();
            foreach (var t in found)
            {
                parts.Add((ThreadLabel(t.Match) ?? "?") + " Ø" + Mm(t.Match.NominalDia) +
                          (t.Rank == RankUnknown ? " (arista ilegible)" : " (arista legible)"));
            }
            _log.Log("IdentificarRosca", "Assembly", LogOutcome.Success,
                "Roscas cosméticas bajo " + name + ": " + found.Count, string.Join(" | ", parts));
        }

        private static double DiaCost(ScopedThread t, double bore)
        {
            if (bore <= 0 || t.Match.NominalDia <= 0) return 0;
            return Math.Abs(t.Match.NominalDia - bore);
        }

        /// <summary>
        /// Every cosmetic thread under <paramref name="feat"/>, ranked against the clicked face. The
        /// sub-tree is walked (the thread can hang one or two levels down) and each sub-feature is
        /// probed by CAST rather than by <c>GetTypeName2</c>: the name is not reliable, and the cast
        /// costs the same. Mirrors <see cref="CollectThreadsInto"/>, including its release order.
        /// </summary>
        private static void CollectScopedThreads(List<ScopedThread> into, IFeature feat, IFace2 face, double[] axis, int depth)
        {
            if (feat == null || depth > 4) return;

            var sub = Try(() => feat.GetFirstSubFeature()) as IFeature;
            while (sub != null)
            {
                var cur = sub;

                object def = Try(() => cur.GetDefinition());
                var data = def as ICosmeticThreadFeatureData;
                if (data != null)
                {
                    double nominal = SafeD(() => data.Diameter);
                    if (nominal > 0)
                    {
                        into.Add(new ScopedThread
                        {
                            Rank = RankThreadEdge(data, face, axis),
                            Match = new ThreadMatch
                            {
                                Callout = Safe(() => data.ThreadCallout),
                                Size = Safe(() => data.Size),
                                NominalDia = nominal,
                                Depth = SafeD(() => data.BlindDepth)
                            }
                        });
                    }
                }
                else
                {
                    CollectScopedThreads(into, cur, face, axis, depth + 1);
                }

                sub = Try(() => cur.GetNextSubFeature()) as IFeature;
                ComRelease.Release(def);
                ComRelease.Release(cur);
            }
        }

        /// <summary>
        /// How strongly a cosmetic thread belongs to the clicked face, from the edge it was applied to.
        ///
        /// The edge's two adjacent faces are the direct answer, but they only work when the thread sits
        /// on the mouth of the bore itself: chamfer a tapped hole and the thread's edge is shared by the
        /// top face and the CHAMFER, not by the bore, so the clicked cylinder is not adjacent to it.
        /// A non-match therefore never rejects the thread — it falls back to comparing the edge's own
        /// axis with the face's, which survives chamfers and still tells two holes of the same cut
        /// apart.
        ///
        /// <see cref="ICosmeticThreadFeatureData.Edge"/> may also come back null, because feature-data
        /// properties that return entities normally need <c>AccessSelections</c> first — which rolls the
        /// model back and drops the selection, so it is NEVER called here (see
        /// <see cref="ScheduleAdvancedRead"/> for what that costs). Unreadable edge = no evidence.
        /// </summary>
        private static int RankThreadEdge(ICosmeticThreadFeatureData data, IFace2 face, double[] axis)
        {
            using (var com = new ComScope())
            {
                var edge = com.Track(Try(() => data.Edge) as IEdge);
                if (edge == null) return RankUnknown;

                // Both faces are released even when one of them IS the clicked face: the array carries
                // its own reference count, separate from the caller's.
                var faces = com.TrackAll(Try(() => edge.GetTwoAdjacentFaces2()) as object[]);
                if (faces != null)
                {
                    foreach (var o in faces)
                    {
                        var f = o as IFace2;
                        if (f != null && SameFace(face, f)) return RankOnFace;
                    }
                }

                return AxisVerdict(edge, axis);
            }
        }

        /// <summary>
        /// Whether the thread's edge runs along the clicked face's own axis — the same test
        /// <see cref="CoaxialFaces"/> applies to faces. A NEGATIVE answer is what tells one hole of a
        /// multi-hole cut from another, so it is only given when the comparison could really be made:
        /// no axis, no circular edge and no usable parameters all report
        /// <see cref="RankUnknown"/> instead of pretending the thread is somebody else's.
        /// </summary>
        private static int AxisVerdict(IEdge edge, double[] axis)
        {
            if (axis == null) return RankUnknown;

            using (var com = new ComScope())
            {
                var curve = com.Track(Try(() => edge.GetCurve()) as ICurve);
                if (curve == null || !curve.IsCircle()) return RankUnknown;

                var cp = curve.CircleParams as double[]; // [cx,cy,cz, ax,ay,az, radius]
                if (cp == null || cp.Length < 6) return RankUnknown;

                double len = Math.Sqrt(cp[3] * cp[3] + cp[4] * cp[4] + cp[5] * cp[5]);
                if (len < 1e-12) return RankUnknown;

                var other = new[] { cp[0], cp[1], cp[2], cp[3] / len, cp[4] / len, cp[5] / len };
                bool coaxial = Math.Abs(other[3] * axis[3] + other[4] * axis[4] + other[5] * axis[5]) >= 0.999
                    && AxisOffset(other, axis) <= 1e-5;
                return coaxial ? RankCoaxial : RankOtherHole;
            }
        }

        /// <summary>
        /// Same face? <see cref="IFace2.IsSame"/> is the API's own answer, but it is asked SECOND: the
        /// pointer comparison already used across this file is free and cannot throw.
        /// </summary>
        private static bool SameFace(IFace2 a, IFace2 b)
        {
            if (a == null || b == null) return false;
            if (SameCom(a, b)) return true;
            try { return a.IsSame(b); } catch { return false; }
        }

        // ---- sibling cosmetic-thread lookup by diameter --------------------------------------------

        private sealed class ThreadMatch
        {
            public string Callout;
            public string Size;
            public double NominalDia; // metres
            public double Depth;      // metres, 0 when through / unknown
        }

        /// <summary>
        /// Scans the component's part for cosmetic-thread features and returns the one whose nominal
        /// diameter best matches the measured bore (nominal is >= the modelled tap-drill bore). Used
        /// to recover the thread callout when the thread is a standalone feature on a plain cut.
        /// </summary>
        private ThreadMatch FindCosmeticThreadByDiameter(Component2 comp, double boreDia)
        {
            var part = comp != null ? comp.GetModelDoc2() as IModelDoc2 : null;
            if (part == null)
            {
                return null;
            }

            ThreadMatch best = null;
            double bestDiff = double.MaxValue;

            try
            {
                // Every feature walked is a reference of its own: released as soon as we step past it,
                // otherwise a click on a hole in a big part strands the entire tree.
                var f = part.FirstFeature() as IFeature;
                while (f != null)
                {
                    var cur = f;
                    string type = null;
                    try { type = cur.GetTypeName2(); } catch { }

                    // Standalone cosmetic thread, or one buried inside a hole feature (a hole wizard and
                    // an advanced hole both keep the thread of their tapped element as a sub-feature).
                    ThreadMatch m = null;
                    if (!string.IsNullOrEmpty(type) && type.IndexOf("Thread", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        m = ThreadOf(cur, boreDia);
                    }
                    else if (!string.IsNullOrEmpty(type) && type.IndexOf("Hole", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        m = FindThreadInFeature(cur, boreDia);
                    }

                    if (m != null && m.NominalDia > 0)
                    {
                        double diff = Math.Abs(m.NominalDia - boreDia);
                        if (diff < bestDiff) { bestDiff = diff; best = m; }
                    }
                    f = cur.GetNextFeature() as IFeature;
                    ComRelease.Release(cur);
                }
            }
            finally { ComRelease.Release(part); }

            return best;
        }

        /// <summary>
        /// True when the face is produced by a mirror feature — either a mirrored body ("por sólido")
        /// or a mirrored feature ("por operación"). Both cases give the face a mirror-type owning
        /// feature ("Mirror...") instead of the real hole/thread feature.
        /// </summary>
        private static bool IsMirroredFeature(IFace2 face)
        {
            if (face == null) return false;
            var feat = face.GetFeature() as IFeature;
            if (feat == null) return false;
            try
            {
                string type = null;
                try { type = feat.GetTypeName2(); } catch { }
                return !string.IsNullOrEmpty(type)
                    && type.IndexOf("Mirror", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            finally { ComRelease.Release(feat); }
        }

        /// <summary>True when the component is loaded lightweight (feature/thread data unavailable).</summary>
        private static bool IsComponentLightweight(Component2 comp)
        {
            if (comp == null) return false;
            try
            {
                int s = comp.GetSuppression2();
                return s == (int)swComponentSuppressionState_e.swComponentLightweight
                    || s == (int)swComponentSuppressionState_e.swComponentFullyLightweight;
            }
            catch { return false; }
        }

        // ---- geometry ------------------------------------------------------------------------------

        private static bool IsCylindrical(IFace2 face)
        {
            if (face == null) return false;
            var surf = face.GetSurface() as ISurface;
            try { return surf != null && surf.IsCylinder(); }
            finally { ComRelease.Release(surf); }
        }

        private static bool IsConical(IFace2 face)
        {
            if (face == null) return false;
            var surf = face.GetSurface() as ISurface;
            try { return surf != null && surf.IsCone(); }
            finally { ComRelease.Release(surf); }
        }

        private static bool IsCircular(IEdge edge)
        {
            if (edge == null) return false;
            var curve = edge.GetCurve() as ICurve;
            try { return curve != null && curve.IsCircle(); }
            finally { ComRelease.Release(curve); }
        }

        /// <summary>
        /// Cylindrical (preferred) or conical face adjacent to a circular edge. The face that is
        /// returned is NOT released — the caller owns it — but the other one is.
        /// </summary>
        private static IFace2 HoleFaceOfEdge(IEdge edge)
        {
            if (edge == null) return null;
            var faces = edge.GetTwoAdjacentFaces2() as object[];
            if (faces == null) return null;

            using (var com = new ComScope())
            {
                com.TrackAll(faces);

                IFace2 cone = null;
                foreach (var o in faces)
                {
                    var f = o as IFace2;
                    if (f == null) continue;
                    if (IsCylindrical(f)) return com.Keep(f);
                    if (cone == null && IsConical(f)) cone = f;
                }
                return com.Keep(cone);
            }
        }

        /// <summary>Diameter (metres) of a cylindrical face, or 0 when the face is not a cylinder.</summary>
        private static double CylinderDiameter(IFace2 face)
        {
            if (face == null) return 0;
            var surf = face.GetSurface() as ISurface;
            try
            {
                if (surf == null || !surf.IsCylinder()) return 0;
                var p = surf.CylinderParams as double[]; // [ox,oy,oz, ax,ay,az, radius]
                if (p == null || p.Length < 7) return 0;
                return p[6] * 2.0;
            }
            finally { ComRelease.Release(surf); }
        }

        /// <summary>
        /// Smallest / largest diameter (metres) of a conical face, taken from its own circular edges
        /// (the exact cut extremes). Falls back to the cone's own radius when the face has no circular
        /// edge. Returns false when the face is not a cone.
        /// </summary>
        private static bool ConeDiameters(IFace2 face, out double dMin, out double dMax)
        {
            dMin = dMax = 0;
            if (!IsConical(face)) return false;

            double min = double.MaxValue, max = 0;
            using (var com = new ComScope())
            {
                var edges = com.TrackAll(face.GetEdges() as object[]);
                if (edges != null)
                {
                    foreach (var o in edges)
                    {
                        var e = o as IEdge;
                        if (e == null) continue;
                        var curve = com.Track(e.GetCurve() as ICurve);
                        if (curve == null || !curve.IsCircle()) continue;
                        var cp = curve.CircleParams as double[]; // [cx,cy,cz, ax,ay,az, radius]
                        if (cp == null || cp.Length < 7) continue;
                        double dia = cp[6] * 2.0;
                        if (dia <= 0) continue;
                        if (dia < min) min = dia;
                        if (dia > max) max = dia;
                    }
                }

                if (max <= 0)
                {
                    var surf = com.Track(face.GetSurface() as ISurface);
                    var p = surf != null ? surf.ConeParams as double[] : null; // [ox,oy,oz, ax,ay,az, radius, halfAngle]
                    if (p == null || p.Length < 7 || p[6] <= 0) return false;
                    min = max = p[6] * 2.0;
                }
            }

            dMin = min == double.MaxValue ? max : min;
            dMax = max;
            return true;
        }

        /// <summary>Included angle of a cone in degrees (90 for a standard metric countersink), or 0.</summary>
        private static double ConeIncludedAngleDeg(IFace2 face)
        {
            var surf = face != null ? face.GetSurface() as ISurface : null;
            try
            {
                var p = surf != null && surf.IsCone() ? surf.ConeParams as double[] : null;
                if (p == null || p.Length < 8) return 0;
                double deg = Math.Abs(p[7]) * 2.0 * 180.0 / Math.PI;
                return deg > 0 && deg < 180 ? deg : 0;
            }
            finally { ComRelease.Release(surf); }
        }

        /// <summary>
        /// Axis of a cylindrical / conical face as {ox,oy,oz, dx,dy,dz} with a unit direction, or null.
        /// Coordinates are the ones the face reports (part space), which is all the axial comparisons
        /// below need since they only ever mix faces of the same body.
        /// </summary>
        private static double[] HoleAxis(IFace2 face)
        {
            var surf = face != null ? face.GetSurface() as ISurface : null;
            if (surf == null) return null;
            try
            {
                double[] p = null;
                if (surf.IsCylinder()) p = surf.CylinderParams as double[];   // [ox,oy,oz, ax,ay,az, r]
                else if (surf.IsCone()) p = surf.ConeParams as double[];      // [ox,oy,oz, ax,ay,az, r, halfAngle]
                if (p == null || p.Length < 6) return null;

                double len = Math.Sqrt(p[3] * p[3] + p[4] * p[4] + p[5] * p[5]);
                if (len < 1e-12) return null;
                return new[] { p[0], p[1], p[2], p[3] / len, p[4] / len, p[5] / len };
            }
            finally { ComRelease.Release(surf); }
        }

        /// <summary>Position of the face's bounding-box centre along <paramref name="axis"/> (metres).</summary>
        private static double AxialCoord(IFace2 face, double[] axis)
        {
            var box = face != null ? face.GetBox() as double[] : null; // [x1,y1,z1, x2,y2,z2]
            if (box == null || box.Length < 6) return 0;
            double vx = (box[0] + box[3]) / 2 - axis[0];
            double vy = (box[1] + box[4]) / 2 - axis[1];
            double vz = (box[2] + box[5]) / 2 - axis[2];
            return vx * axis[3] + vy * axis[4] + vz * axis[5];
        }

        /// <summary>
        /// Axial span of a face, from the points of its edges projected on the hole axis: this is the
        /// LENGTH of that stretch of the hole. A circular edge (the end of a bore) is represented by its
        /// centre, which sits exactly at that end's axial position. False when the face has no edge.
        /// </summary>
        private static bool FaceAxialExtent(IFace2 face, double[] axis, out double tMin, out double tMax)
        {
            tMin = double.MaxValue;
            tMax = double.MinValue;

            using (var com = new ComScope())
            {
                var edges = com.TrackAll(face != null ? face.GetEdges() as object[] : null);
                if (edges == null) return false;

                foreach (var o in edges)
                {
                    var e = o as IEdge;
                    if (e == null) continue;

                    var curve = com.Track(Try(() => e.GetCurve()) as ICurve);
                    if (curve != null && curve.IsCircle())
                    {
                        Span(curve.CircleParams as double[], axis, ref tMin, ref tMax); // [cx,cy,cz, ...]
                        continue;
                    }

                    Span(VertexPoint(com.Track(Try(() => e.GetStartVertex()))), axis, ref tMin, ref tMax);
                    Span(VertexPoint(com.Track(Try(() => e.GetEndVertex()))), axis, ref tMin, ref tMax);
                }
            }
            return tMax >= tMin;
        }

        private static void Span(double[] pt, double[] axis, ref double tMin, ref double tMax)
        {
            if (pt == null || pt.Length < 3) return;
            double t = (pt[0] - axis[0]) * axis[3] + (pt[1] - axis[1]) * axis[4] + (pt[2] - axis[2]) * axis[5];
            if (t < tMin) tMin = t;
            if (t > tMax) tMax = t;
        }

        private static double[] VertexPoint(object vertex)
        {
            var v = vertex as IVertex;
            return v != null ? v.GetPoint() as double[] : null;
        }

        /// <summary>
        /// Every cylindrical / conical face of the same body sharing <paramref name="axis"/> — i.e. the
        /// faces cut by the same coaxial hole, used to measure how deep the clicked face sits.
        ///
        /// Returns a LIST the caller must release (see <see cref="MeasureHoleStack"/>); the body and the
        /// faces that did not match are released here. Walking a whole body means one reference per face,
        /// so on a part with thousands of faces this is where pick-mode used to bleed on every click.
        /// Releasing an element is always balanced: the array carries its own reference count, even for
        /// the clicked face the caller already holds.
        /// </summary>
        private static List<IFace2> CoaxialFaces(IFace2 face, double[] axis)
        {
            var hits = new List<IFace2>();

            var body = face != null ? face.GetBody() as IBody2 : null;
            if (body == null) return hits;

            try
            {
                var faces = body.GetFaces() as object[];
                if (faces == null) return hits;

                foreach (var o in faces)
                {
                    var f = o as IFace2;
                    if (f == null) { ComRelease.Release(o); continue; }

                    double[] fa = HoleAxis(f);
                    bool keep = fa != null
                        // parallel, and on the same line rather than merely parallel to it
                        && Math.Abs(fa[3] * axis[3] + fa[4] * axis[4] + fa[5] * axis[5]) >= 0.999
                        && AxisOffset(fa, axis) <= 1e-5;

                    if (keep) hits.Add(f);
                    else ComRelease.Release(f);
                }
                return hits;
            }
            finally { ComRelease.Release(body); }
        }

        /// <summary>Perpendicular distance from another axis' origin to the reference axis line.</summary>
        private static double AxisOffset(double[] other, double[] axis)
        {
            double vx = other[0] - axis[0], vy = other[1] - axis[1], vz = other[2] - axis[2];
            double d = vx * axis[3] + vy * axis[4] + vz * axis[5];
            double px = vx - d * axis[3], py = vy - d * axis[4], pz = vz - d * axis[5];
            return Math.Sqrt(px * px + py * py + pz * pz);
        }

        // ---- callout -------------------------------------------------------------------------------

        /// <summary>
        /// Builds the callout and attaches it to the selected entity via
        /// <see cref="ISelectionMgr.SetCallout"/>, which positions it on that object and keeps it
        /// visible until the object is deselected (the proven CodeStack pattern — no leader/position
        /// maths needed). The callout handler must be COM-visible for SolidWorks to accept it.
        /// </summary>
        private void ShowCallout(ISelectionMgr selMgr, int selIndex, IEntity entity, HoleDescription desc)
        {
            // Replace any previous callout so only the latest hole is annotated.
            HideCurrentCallout();

            var lines = desc.Lines;
            bool stored = false;
            try
            {
                var handler = new HoleCalloutHandler();
                var callout = selMgr.CreateCallout2(lines.Count, handler) as ICallout;
                if (callout == null)
                {
                    _log.Log("IdentificarRosca", "Assembly", LogOutcome.Error, "CreateCallout2 returned null");
                    return;
                }

                callout.SkipColon = false;
                for (int i = 0; i < lines.Count; i++)
                {
                    var parts = lines[i].Split(new[] { '|' }, 2);
                    string label = parts[0];
                    string value = parts.Length > 1 ? parts[1] : string.Empty;

                    callout.set_Label2(i, label);
                    callout.set_Value(i, value);
                    callout.set_ValueInactive(i, true); // every value is read-only informational
                }

                bool ok = selMgr.SetCallout(selIndex, callout as Callout);
                _currentCallout = callout;
                _currentCalloutHandler = handler;
                _calloutEntity = entity;
                stored = true;

                try { _pickModel?.GraphicsRedraw2(); } catch { }

                if (!ok)
                {
                    _log.Log("IdentificarRosca", "Assembly", LogOutcome.Error,
                        "SetCallout returned false (index=" + selIndex + ")");
                }
            }
            catch (Exception ex)
            {
                _log.Log("IdentificarRosca", "Assembly", LogOutcome.Error, "CreateCallout failed", ex.ToString());
            }
            finally
            {
                // The caller handed the entity over; if it did not end up in _calloutEntity nobody
                // else will ever release it.
                if (!stored) ComRelease.Release(entity);
            }
        }

        /// <summary>
        /// Resolves a lightweight component so its feature data (threads) becomes readable, then
        /// annotates the same face again. Deferred via a one-shot timer because we run inside
        /// NewSelectionNotify: resolving reloads the component and invalidates the selected face, so
        /// doing it mid-notification would re-enter SolidWorks and crash.
        ///
        /// <paramref name="face"/> is the face the user clicked; its position is captured BEFORE the
        /// reload so the face can be re-selected afterwards if SolidWorks drops the selection.
        /// </summary>
        private void ScheduleResolve(Component2 comp, IFace2 face)
        {
            // Ownership of comp was handed over by the caller: every path from here must give it back.
            if (comp == null) return;
            if (_resolvePending) { ComRelease.Release(comp); return; }

            // One attempt per component: if resolving fails (missing file, read-only assembly...) the
            // component stays lightweight and retrying on every click would loop forever.
            string key = Safe(() => comp.Name2);
            if (!string.IsNullOrEmpty(key) && !_resolveAttempted.Add(key))
            {
                ComRelease.Release(comp);
                return;
            }

            double[] pt = FacePoint(face);
            _resolvePending = true;

            var timer = new System.Windows.Forms.Timer { Interval = 1 };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                timer.Dispose();
                try
                {
                    comp.SetSuppression2((int)swComponentSuppressionState_e.swComponentFullyResolved);
                    if (!ReselectFace(comp, pt))
                    {
                        // Selection survived the reload -> describe it as usual.
                        RefreshCalloutFromSelection();
                    }
                    // When ReselectFace re-selected the face, its NewSelectionNotify does the rest.
                }
                catch (Exception ex)
                {
                    _log.Log("IdentificarRosca", "Assembly", LogOutcome.Error, "Resolver aligerado falló", ex.ToString());
                }
                finally
                {
                    _resolvePending = false;
                    ComRelease.Release(comp);
                }
            };
            timer.Start();
        }

        /// <summary>A point lying on the face (part space), used to find it again after a reload.</summary>
        private static double[] FacePoint(IFace2 face)
        {
            if (face == null) return null;
            try
            {
                var box = face.GetBox() as double[]; // [x1,y1,z1, x2,y2,z2]
                if (box == null || box.Length < 6) return null;
                var p = face.GetClosestPointOn((box[0] + box[3]) / 2, (box[1] + box[4]) / 2, (box[2] + box[5]) / 2) as double[];
                return p != null && p.Length >= 3 ? new[] { p[0], p[1], p[2] } : null;
            }
            catch { return null; }
        }

        /// <summary>
        /// Re-selects the face at <paramref name="pt"/> on a just-resolved component. Returns true when
        /// it selected something (the selection notification then rebuilds the callout), false when the
        /// caller should simply re-read the current selection.
        /// </summary>
        private bool ReselectFace(Component2 comp, double[] pt)
        {
            if (pt == null || _pickModel == null) return false;

            using (var com = new ComScope())
            {
                var selMgr = com.Track(_pickModel.SelectionManager as ISelectionMgr);
                if (selMgr == null) return false;

                // Selection survived the reload: nothing to do.
                if (selMgr.GetSelectedObjectCount2(-1) > 0) return false;

                var body = com.Track(comp.GetBody() as IBody2);
                if (body == null) return false;

                var faces = com.TrackAll(body.GetFaces() as object[]);
                if (faces == null) return false;

                const double tol = 1e-6; // 0.001 mm
                foreach (var o in faces)
                {
                    var f = o as IFace2;
                    if (f == null || !(IsCylindrical(f) || IsConical(f))) continue;

                    double[] q = null;
                    try { q = f.GetClosestPointOn(pt[0], pt[1], pt[2]) as double[]; } catch { }
                    if (q == null || q.Length < 3) continue;
                    double dx = q[0] - pt[0], dy = q[1] - pt[1], dz = q[2] - pt[2];
                    if (dx * dx + dy * dy + dz * dz > tol * tol) continue;

                    var ent = f as IEntity;
                    // Selecting fires NewSelectionNotify, which acquires its own reference to the face;
                    // this one still belongs to the scope.
                    if (ent != null && ent.Select4(false, null)) return true;
                }
                return false;
            }
        }

        private void HideCurrentCallout()
        {
            if (_currentCallout == null) return;
            try { _currentCallout.Display(false); } catch { }

            // Both were handed over by ShowCallout and are ours to give back.
            ComRelease.Release(_currentCallout);
            ComRelease.Release(_calloutEntity);

            _currentCallout = null;
            _currentCalloutHandler = null;
            _calloutEntity = null;
            try { _pickModel?.GraphicsRedraw2(); } catch { }
        }

        // ---- small helpers -------------------------------------------------------------------------

        private static string Mm(double metres)
        {
            return (metres * 1000.0).ToString("0.##", CultureInfo.CurrentCulture);
        }

        private static string Deg(double degrees)
        {
            return degrees.ToString("0.#", CultureInfo.CurrentCulture);
        }

        private static double FirstPositive(params double[] values)
        {
            foreach (var v in values)
            {
                if (v > 0) return v;
            }
            return 0;
        }

        /// <summary>True when both RCWs wrap the same underlying COM object (canonical IUnknown).</summary>
        private static bool SameCom(object a, object b)
        {
            if (a == null || b == null) return false;
            IntPtr pa = IntPtr.Zero, pb = IntPtr.Zero;
            try
            {
                pa = Marshal.GetIUnknownForObject(a);
                pb = Marshal.GetIUnknownForObject(b);
                return pa == pb;
            }
            catch { return false; }
            finally
            {
                if (pa != IntPtr.Zero) Marshal.Release(pa);
                if (pb != IntPtr.Zero) Marshal.Release(pb);
            }
        }

        private static string Safe(Func<string> get)
        {
            try { return get(); } catch { return null; }
        }

        private static double SafeD(Func<double> get)
        {
            try { return get(); } catch { return 0; }
        }

        private static int SafeI(Func<int> get)
        {
            try { return get(); } catch { return 0; }
        }

        private static object Try(Func<object> get)
        {
            try { return get(); } catch { return null; }
        }
    }

    /// <summary>
    /// Callout handler. Values are informational (read-only), so any attempted edit is rejected. Only
    /// required because <see cref="ISelectionMgr.CreateCallout2"/> needs a handler.
    ///
    /// MUST be public + COM-visible: the assembly is <c>[assembly: ComVisible(false)]</c>, so without
    /// it SolidWorks cannot marshal the handler and the callout never appears. The default class
    /// interface (AutoDispatch) is kept ON PURPOSE: CreateCallout2 takes the handler as <c>object</c>,
    /// which marshals as IDispatch, so the CCW must expose IDispatch — <c>ClassInterfaceType.None</c>
    /// removes it and makes CreateCallout2 throw InvalidCastException. The explicitly implemented
    /// <see cref="ISwCalloutHandler"/> is still reachable via QueryInterface for the callback.
    /// </summary>
    [ComVisible(true)]
    public sealed class HoleCalloutHandler : ISwCalloutHandler
    {
        public bool OnStringValueChanged(object pManipulator, int rowID, string text)
        {
            return false; // values are read-only
        }
    }
}
