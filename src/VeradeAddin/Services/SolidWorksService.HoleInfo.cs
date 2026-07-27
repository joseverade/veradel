using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SolidWorks.Interop.swpublished;
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
                return false;
            }

            var ev = model as DAssemblyDocEvents_Event;
            if (ev == null)
            {
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

            _pickEvents = null;
            _pickSelHandler = null;
            _pickDestroyHandler = null;
            _pickModel = null;
            _pickActive = false;
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

        private void RefreshCalloutFromSelection()
        {
            var model = _pickModel;
            if (model == null) return;

            var selMgr = model.SelectionManager as ISelectionMgr;
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
                    var f = selMgr.GetSelectedObject6(i, -1) as IFace2;
                    if (f != null && IsCylindrical(f))
                    {
                        face = f;
                        comp = selMgr.GetSelectedObjectsComponent4(i, -1) as Component2;
                        selIndex = i;
                        break;
                    }
                }
                else if (t == (int)swSelectType_e.swSelEDGES)
                {
                    var e = selMgr.GetSelectedObject6(i, -1) as IEdge;
                    if (e != null && IsCircular(e))
                    {
                        edge = e;
                        comp = selMgr.GetSelectedObjectsComponent4(i, -1) as Component2;
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

            if (face == null)
            {
                face = CylindricalFaceOfEdge(edge);
            }

            var desc = DescribeHole(face, edge, comp);
            if (desc == null || desc.Lines.Count == 0)
            {
                HideCurrentCallout();
                return;
            }

            ShowCallout(selMgr, selIndex, desc.Lines);
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

        private HoleDescription DescribeHole(IFace2 face, IEdge edge, Component2 comp)
        {
            var d = new HoleDescription();

            IFeature feat = face != null ? face.GetFeature() as IFeature : null;
            object def = null;
            if (feat != null)
            {
                try { def = feat.GetDefinition(); } catch { def = null; }
            }

            var wiz = def as IWizardHoleFeatureData2;
            if (wiz != null)
            {
                FillFromWizard(d, wiz);
                if (d.Lines.Count > 0) return d;
            }

            var thread = def as ICosmeticThreadFeatureData;
            if (thread != null)
            {
                FillFromThread(d, thread);
                if (d.Lines.Count > 0) return d;
            }

            // Fallback: measure the cylinder and try to recover a thread callout from a sibling
            // cosmetic-thread feature whose nominal diameter matches the bore.
            double boreDia = CylinderDiameter(face); // metres, 0 when not a cylinder
            if (boreDia <= 0)
            {
                return null;
            }

            var match = FindCosmeticThreadByDiameter(comp, boreDia);
            if (match != null)
            {
                d.Title = !string.IsNullOrWhiteSpace(match.Callout) ? match.Callout : match.Size;
                if (!string.IsNullOrWhiteSpace(match.Callout)) d.Add("Rosca", match.Callout);
                else if (!string.IsNullOrWhiteSpace(match.Size)) d.Add("Rosca", match.Size);
                if (match.NominalDia > 0) d.Add("Ø nominal", Mm(match.NominalDia));
                d.Add("Ø taladro", Mm(boreDia));
                return d;
            }

            // Plain (unthreaded) hole: just the measured diameter.
            d.Title = "Ø" + Mm(boreDia);
            d.Add("Ø taladro", Mm(boreDia));
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
            string fastener = Safe(() => w.FastenerSize);   // e.g. "M8"
            string threadClass = Safe(() => w.ThreadClass);

            // The modelled bore (smaller) and the counter feature (larger), so both the minimum and
            // the maximum diameter of a countersink / counterbore hole are reported.
            double bore = FirstPositive(SafeD(() => w.HoleDiameter),
                                        SafeD(() => w.ThruHoleDiameter),
                                        SafeD(() => w.Diameter),
                                        SafeD(() => w.TapDrillDiameter));
            double cbore = SafeD(() => w.CounterBoreDiameter);
            double csink = SafeD(() => w.CounterSinkDiameter);
            double depth = FirstPositive(SafeD(() => w.HoleDepth), SafeD(() => w.Depth));

            bool threaded = !string.IsNullOrWhiteSpace(fastener) &&
                            (SafeD(() => w.ThreadDiameter) > 0 || SafeD(() => w.MajorDiameter) > 0
                             || SafeI(() => w.TapType) != 0);

            d.Title = threaded && !string.IsNullOrWhiteSpace(fastener)
                      ? fastener
                      : (bore > 0 ? "Ø" + Mm(bore) : "Taladro");

            if (!string.IsNullOrWhiteSpace(fastener))
            {
                d.Add(threaded ? "Rosca" : "Tamaño",
                      fastener + (threaded && !string.IsNullOrWhiteSpace(threadClass) ? " " + threadClass : ""));
            }
            if (bore > 0) d.Add("Ø taladro (mín)", Mm(bore));
            if (csink > 0) d.Add("Ø avellanado (máx)", Mm(csink));
            if (cbore > 0) d.Add("Ø caja (máx)", Mm(cbore));
            if (depth > 0) d.Add("Profundidad", Mm(depth));
        }

        // ---- sibling cosmetic-thread lookup by diameter --------------------------------------------

        private sealed class ThreadMatch
        {
            public string Callout;
            public string Size;
            public double NominalDia; // metres
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

            double tol = boreDia * 0.25 + 0.0005; // generous: tap-drill bore is smaller than nominal
            ThreadMatch best = null;
            double bestDiff = double.MaxValue;

            var f = part.FirstFeature() as IFeature;
            while (f != null)
            {
                string type = null;
                try { type = f.GetTypeName2(); } catch { }
                if (type == "CosmeticThread")
                {
                    ICosmeticThreadFeatureData data = null;
                    try { data = f.GetDefinition() as ICosmeticThreadFeatureData; } catch { }
                    if (data != null)
                    {
                        double nominal = SafeD(() => data.Diameter);
                        double diff = Math.Abs(nominal - boreDia);
                        if (nominal > 0 && diff <= tol && diff < bestDiff)
                        {
                            bestDiff = diff;
                            best = new ThreadMatch
                            {
                                Callout = Safe(() => data.ThreadCallout),
                                Size = Safe(() => data.Size),
                                NominalDia = nominal
                            };
                        }
                    }
                }
                f = f.GetNextFeature() as IFeature;
            }
            return best;
        }

        // ---- geometry ------------------------------------------------------------------------------

        private static bool IsCylindrical(IFace2 face)
        {
            if (face == null) return false;
            var surf = face.GetSurface() as ISurface;
            return surf != null && surf.IsCylinder();
        }

        private static bool IsCircular(IEdge edge)
        {
            if (edge == null) return false;
            var curve = edge.GetCurve() as ICurve;
            return curve != null && curve.IsCircle();
        }

        private static IFace2 CylindricalFaceOfEdge(IEdge edge)
        {
            if (edge == null) return null;
            var faces = edge.GetTwoAdjacentFaces2() as object[];
            if (faces == null) return null;
            foreach (var o in faces)
            {
                var f = o as IFace2;
                if (f != null && IsCylindrical(f))
                {
                    return f;
                }
            }
            return null;
        }

        /// <summary>Diameter (metres) of a cylindrical face, or 0 when the face is not a cylinder.</summary>
        private static double CylinderDiameter(IFace2 face)
        {
            if (face == null) return 0;
            var surf = face.GetSurface() as ISurface;
            if (surf == null || !surf.IsCylinder()) return 0;
            var p = surf.CylinderParams as double[]; // [ox,oy,oz, ax,ay,az, radius]
            if (p == null || p.Length < 7) return 0;
            return p[6] * 2.0;
        }

        // ---- callout -------------------------------------------------------------------------------

        /// <summary>
        /// Builds the callout and attaches it to the selected entity via
        /// <see cref="ISelectionMgr.SetCallout"/>, which positions it on that object and keeps it
        /// visible until the object is deselected (the proven CodeStack pattern — no leader/position
        /// maths needed). The callout handler must be COM-visible for SolidWorks to accept it.
        /// </summary>
        private void ShowCallout(ISelectionMgr selMgr, int selIndex, List<string> lines)
        {
            // Replace any previous callout so only the latest hole is annotated.
            HideCurrentCallout();

            try
            {
                var callout = selMgr.CreateCallout2(lines.Count, new HoleCalloutHandler()) as ICallout;
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
                    callout.set_ValueInactive(i, true); // read-only, informational
                }

                bool ok = selMgr.SetCallout(selIndex, callout as Callout);
                _currentCallout = callout;

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
        }

        private void HideCurrentCallout()
        {
            if (_currentCallout == null) return;
            try { _currentCallout.Display(false); } catch { }
            _currentCallout = null;
            try { _pickModel?.GraphicsRedraw2(); } catch { }
        }

        // ---- small helpers -------------------------------------------------------------------------

        private static string Mm(double metres)
        {
            return (metres * 1000.0).ToString("0.##", CultureInfo.CurrentCulture);
        }

        private static double FirstPositive(params double[] values)
        {
            foreach (var v in values)
            {
                if (v > 0) return v;
            }
            return 0;
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
    }

    /// <summary>
    /// Minimal callout handler: our callouts are read-only (informational), so any attempted value
    /// edit is rejected. Required because <see cref="ISelectionMgr.CreateCallout2"/> needs a handler.
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
            return false; // reject edits
        }
    }
}
