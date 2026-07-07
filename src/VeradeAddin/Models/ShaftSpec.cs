using System.Collections.Generic;

namespace VeradeAddin.Models
{
    /// <summary>One shaft level as entered by the user: a diameter × length pair, in millimetres.</summary>
    public sealed class ShaftLevel
    {
        public double DiameterMm { get; set; }
        public double LengthMm { get; set; }
    }

    /// <summary>
    /// A contiguous run of levels sharing the same diameter, merged into ONE revolve segment.
    /// When the user enters two (or more) consecutive levels with equal diameter, the profile
    /// cannot hold a zero-height step, so those levels are summed into a single segment and the
    /// internal boundaries are materialised afterwards as split lines on the cylindrical face
    /// (<see cref="SplitPositionsMm"/>, measured from the shaft's left face).
    /// </summary>
    public sealed class ShaftSegment
    {
        public ShaftSegment()
        {
            SplitPositionsMm = new List<double>();
        }

        /// <summary>Distance from the shaft's left face to this segment's left edge.</summary>
        public double StartMm { get; set; }
        public double DiameterMm { get; set; }
        /// <summary>Summed length of every merged level.</summary>
        public double LengthMm { get; set; }
        /// <summary>Absolute X (from the left face) of each internal level boundary → one split line each.</summary>
        public List<double> SplitPositionsMm { get; set; }
    }

    /// <summary>
    /// One keyway (chaveta, DIN 6885 form A: slot with rounded ends) on the shaft. Modelled as a
    /// plane tangent to the shaft (parallel to the front plane, or rotated <see cref="AngleDeg"/>
    /// about the axis) + slot extrude-cut + optional circular pattern. May overhang a shaft end
    /// PARTIALLY (open keyway) — some stretch must remain on the shaft.
    /// </summary>
    public sealed class ShaftKeyway
    {
        /// <summary>b: slot width (also the end-arc diameter).</summary>
        public double WidthMm { get; set; }
        /// <summary>
        /// l: with <see cref="CenterArc"/> = 0 the TOTAL slot length (arc ends included, l &gt; b).
        /// With CenterArc = 1/2 it is the cota the user sees: anchored arc CENTRE → opposite arc
        /// EXTREME (l &gt; b/2; the total span is then l + b/2, see <see cref="SpanMm"/>).
        /// </summary>
        public double LengthMm { get; set; }
        /// <summary>
        /// Reference edge the position is measured from: 0 = shaft left face, i = boundary after
        /// level i (1-based levels), levels.Count = right face.
        /// </summary>
        public int EdgeIndex { get; set; }
        /// <summary>
        /// Signed distance, never 0, ALWAYS from the edge to the LEFT arc extreme:
        /// x1 = edge + value. Negative puts the left extreme left of the edge (the key may
        /// straddle it). Ignored unless <see cref="CenterArc"/> is 0.
        /// </summary>
        public double OffsetMm { get; set; }
        /// <summary>
        /// Anchor mode: 0 = position by <see cref="OffsetMm"/> (cota edge → left extreme);
        /// 1 = LEFT arc CENTRE exactly on the reference edge; 2 = RIGHT arc CENTRE on it.
        /// With 1/2 there is no position dim (the centre is related to the edge) and
        /// <see cref="LengthMm"/> is measured anchored-centre → opposite extreme.
        /// </summary>
        public int CenterArc { get; set; }
        /// <summary>t: radial depth measured from the reference-diameter surface toward the axis.</summary>
        public double DepthMm { get; set; }
        /// <summary>
        /// Diameter whose surface the depth is measured from. Must be the diameter of one of the
        /// levels the key spans (the combobox choice when the key sits between two levels).
        /// </summary>
        public double RefDiameterMm { get; set; }
        /// <summary>Start angle in degrees. 0 = front (+Z); positive rotates +Z toward −Y (validated live).</summary>
        public double AngleDeg { get; set; }
        /// <summary>Number of keys equally spaced around the axis (circular pattern). ≥ 1.</summary>
        public int Count { get; set; }

        /// <summary>TOTAL extreme-to-extreme span (mm): l, or l + b/2 in the centre-anchored modes.</summary>
        public double SpanMm
        {
            get { return CenterArc == 0 ? LengthMm : LengthMm + WidthMm / 2.0; }
        }

        /// <summary>Left arc extreme X (mm from the shaft left face), given the edge position.</summary>
        public double StartXMm(double edgeX)
        {
            // LEFT centre on edge: the left extreme is b/2 left of it.
            if (CenterArc == 1) return edgeX - WidthMm / 2.0;
            // RIGHT centre on edge: l runs from that centre to the LEFT extreme.
            if (CenterArc == 2) return edgeX - LengthMm;
            // The cota always points at the LEFT arc extreme: x1 = edge + offset.
            return edgeX + OffsetMm;
        }
    }

    /// <summary>
    /// One retaining-ring groove (DIN 471 style): rectangular 360° cut-revolve of width E1 down to
    /// bottom diameter D3. Must sit entirely on ONE diameter (split lines are not walls and may be
    /// crossed). Positioned from a level boundary exactly like a keyway.
    /// </summary>
    public sealed class ShaftGroove
    {
        /// <summary>E1: groove width along the axis.</summary>
        public double WidthMm { get; set; }
        /// <summary>D3: groove bottom diameter. Must be smaller than the level's diameter.</summary>
        public double BottomDiameterMm { get; set; }
        /// <summary>Reference edge, same indexing as <see cref="ShaftKeyway.EdgeIndex"/>.</summary>
        public int EdgeIndex { get; set; }
        /// <summary>
        /// Signed distance, never 0, from the edge to the NEAR wall of the groove: positive →
        /// LEFT wall right of the edge (x1 = edge + value); negative → RIGHT wall left of the
        /// edge (x2 = edge + value), so flipping the sign mirrors the cota to the other wall.
        /// </summary>
        public double OffsetMm { get; set; }

        /// <summary>Left wall X (mm from the shaft left face), given the edge position.</summary>
        public double StartXMm(double edgeX)
        {
            // Positive cota → LEFT wall (x1 = edge + offset); negative cota → RIGHT wall
            // (x2 = edge + offset, so x1 = x2 − E1). Mirrors grvX1 in configurator.js.
            return OffsetMm < 0 ? edgeX + OffsetMm - WidthMm : edgeX + OffsetMm;
        }
    }

    /// <summary>
    /// One DIN 509 undercut (entalladura), form E or F, at a diameter-change shoulder. The relief
    /// is machined into the SMALLER-diameter cylinder against the shoulder face: width
    /// <see cref="WidthMm"/> (f) along the axis, depth <see cref="DepthMm"/> (t1) below the small
    /// surface, corner radius <see cref="RadiusMm"/> (r), 15° run-out back to the surface. Form F
    /// additionally cuts INTO the shoulder face to axial depth <see cref="Depth2Mm"/> (t2) with an
    /// 8° run-out up the face. Sizes come from the DIN 509 table (UI, keyed by small diameter and
    /// series usual/fatiga); the host revalidates the geometry but not norm membership.
    /// </summary>
    public sealed class ShaftUndercut
    {
        /// <summary>Boundary index of the shoulder (1 … Levels.Count − 1). The two adjacent levels
        /// must have DIFFERENT diameters (an equal-diameter split boundary is not a shoulder).</summary>
        public int BoundaryIndex { get; set; }
        /// <summary>"E" or "F" (DIN 509 form).</summary>
        public string Form { get; set; }
        /// <summary>r: tool/corner radius at both ends of the flat bottom.</summary>
        public double RadiusMm { get; set; }
        /// <summary>t1: depth below the small-diameter surface.</summary>
        public double DepthMm { get; set; }
        /// <summary>f: total axial width measured from the shoulder face.</summary>
        public double WidthMm { get; set; }
        /// <summary>t2: axial depth of the relief into the shoulder face. Form F only (0 for E).</summary>
        public double Depth2Mm { get; set; }

        /// <summary>True when this undercut is DIN 509 form F (relief also into the shoulder face).</summary>
        public bool IsFormF { get { return Form == "F"; } }
    }

    /// <summary>
    /// One DIN 332 centre point (punto de centrado) machined coaxially into a shaft END FACE, for
    /// turning/grinding between centres. Modelled as a single 360° cut-revolve of the drilled
    /// half-section on the axis. Four forms:
    /// <list type="bullet">
    /// <item><b>A</b> (DIN 332-1): 60° countersink (Ø <see cref="CountersinkDiameterMm"/>) + pilot
    /// bore (Ø <see cref="PilotDiameterMm"/>) + 120° drill tip. No protective chamfer.</item>
    /// <item><b>B</b> (DIN 332-1): form A preceded by a 120° protective countersink at the mouth
    /// (Ø <see cref="ProtectDiameterMm"/>), which shields the 60° cone.</item>
    /// <item><b>R</b> (DIN 332-1): the 60° flank is replaced by a radius arc
    /// (<see cref="ArcRadiusMm"/>) from the mouth Ø down to the pilot bore.</item>
    /// <item><b>D</b> (DIN 332-2, threaded): 120° protective countersink + relief counterbore
    /// (Ø <see cref="CountersinkDiameterMm"/>, length <see cref="CounterboreDepthMm"/>) + tap-drill
    /// core bore (Ø <see cref="PilotDiameterMm"/>) for a metric thread Ø
    /// <see cref="ThreadDiameterMm"/> + 120° drill tip.</item>
    /// </list>
    /// The mouth Ø must fit on the end face (leave a rim) and the total depth must stay within the
    /// end level. At most one centre point per end. Sizes come from the DIN 332 tables in the UI;
    /// the host revalidates the geometry.
    /// </summary>
    public sealed class ShaftCenterHole
    {
        /// <summary>0 = left face (x = 0, drills toward +X); 1 = right face (x = total, toward −X).</summary>
        public int End { get; set; }
        /// <summary>"A", "B", "R" (DIN 332-1) or "D" (DIN 332-2, threaded).</summary>
        public string Form { get; set; }
        /// <summary>d1: pilot bore Ø (A/B/R) or tap-drill core bore Ø (D).</summary>
        public double PilotDiameterMm { get; set; }
        /// <summary>d2: 60° countersink Ø (A/B/R) or relief counterbore Ø (D).</summary>
        public double CountersinkDiameterMm { get; set; }
        /// <summary>d3: 120° protective countersink Ø. 0 for forms A and R.</summary>
        public double ProtectDiameterMm { get; set; }
        /// <summary>Straight length of the pilot/core cylinder (below the cones, before the tip).</summary>
        public double PilotDepthMm { get; set; }
        /// <summary>r: form R flank arc radius. 0 for the other forms.</summary>
        public double ArcRadiusMm { get; set; }
        /// <summary>Form D relief counterbore length (between the protective cone and the core bore). 0 otherwise.</summary>
        public double CounterboreDepthMm { get; set; }
        /// <summary>Form D nominal metric thread Ø (M size). 0 otherwise.</summary>
        public double ThreadDiameterMm { get; set; }

        /// <summary>DIN 332-2 threaded form.</summary>
        public bool IsThreaded { get { return Form == "D"; } }
        /// <summary>Form has a 120° protective countersink at the mouth (B and D).</summary>
        public bool HasProtect { get { return ProtectDiameterMm > 0; } }
        /// <summary>Largest Ø at the face = what must fit on the end face with a rim.</summary>
        public double MouthDiameterMm { get { return System.Math.Max(CountersinkDiameterMm, ProtectDiameterMm); } }

        /// <summary>
        /// Axial depth (mm) of the form R radiused flank: the arc runs from the mouth (Ø d2) to a
        /// horizontal tangent on the pilot cylinder (Ø d1). Its centre is r above the pilot top, so
        /// the flank length along the axis is √(r² − (r + d1/2 − d2/2)²). 0 when unsolvable.
        /// </summary>
        public double ArcFlankDepthMm()
        {
            double r1 = PilotDiameterMm / 2.0, r2 = CountersinkDiameterMm / 2.0, r = ArcRadiusMm;
            double a = r + r1 - r2;
            double disc = r * r - a * a;
            return disc > 0 ? System.Math.Sqrt(disc) : 0.0;
        }

        /// <summary>Total drilled depth (mm) from the end face to the tip, per form.</summary>
        public double TotalDepthMm()
        {
            double tanCs = System.Math.Tan(ShaftSpec.CenterHoleCountersinkHalfDeg * System.Math.PI / 180.0);
            double tanTp = System.Math.Tan(ShaftSpec.CenterHoleTaperHalfDeg * System.Math.PI / 180.0);
            double r1 = PilotDiameterMm / 2.0, r2 = CountersinkDiameterMm / 2.0, r3 = ProtectDiameterMm / 2.0;
            double tip = r1 / tanTp;                                  // 120° drill point
            if (Form == "D")
            {
                double hp = r3 > r2 ? (r3 - r2) / tanTp : 0.0;        // 120° protective
                return hp + CounterboreDepthMm + PilotDepthMm + tip;
            }
            if (Form == "R")
            {
                return ArcFlankDepthMm() + PilotDepthMm + tip;
            }
            double hpB = (Form == "B" && r3 > r2) ? (r3 - r2) / tanTp : 0.0;
            double hc = (r2 - r1) / tanCs;                           // 60° countersink
            return hpB + hc + PilotDepthMm + tip;
        }
    }

    /// <summary>
    /// User-entered geometry for the "Eje personalizado" configurator. All values in millimetres.
    /// The body is a stepped shaft built left → right: n levels, each a diameter × length, revolved
    /// 360° as a single profile. Consecutive levels with the SAME diameter are merged into one
    /// segment and the internal boundary becomes a split line (see <see cref="ShaftSegment"/>).
    /// Keyways (<see cref="ShaftKeyway"/>) are cut afterwards, each from its own tangent plane.
    /// </summary>
    public sealed class ShaftSpec
    {
        /// <summary>Two adjacent diameters closer than this (mm) count as "the same diameter".</summary>
        public const double DiameterToleranceMm = 1e-9;

        /// <summary>Tolerance (mm) for "an arc extreme sits exactly on an edge" checks.</summary>
        public const double PositionToleranceMm = 1e-6;

        /// <summary>Run-out angle of the relief back to the cylindrical surface (DIN 509, forms E and F).</summary>
        public const double UndercutRunOutDeg = 15.0;

        /// <summary>Run-out angle of the form F relief up the shoulder FACE (DIN 509).</summary>
        public const double UndercutFaceRunOutDeg = 8.0;

        /// <summary>Half-angle (from the axis) of the DIN 332 60° countersink flank (included 60°).</summary>
        public const double CenterHoleCountersinkHalfDeg = 30.0;

        /// <summary>Half-angle (from the axis) of the 120° protective countersink and the 120° drill tip.</summary>
        public const double CenterHoleTaperHalfDeg = 60.0;

        public ShaftSpec()
        {
            Levels = new List<ShaftLevel>();
            Keyways = new List<ShaftKeyway>();
            Grooves = new List<ShaftGroove>();
            Undercuts = new List<ShaftUndercut>();
            CenterHoles = new List<ShaftCenterHole>();
        }

        /// <summary>Levels left → right, in the order the user entered them.</summary>
        public List<ShaftLevel> Levels { get; set; }

        /// <summary>Keyways to cut, in order. May be empty.</summary>
        public List<ShaftKeyway> Keyways { get; set; }

        /// <summary>Retaining-ring grooves (ranuras DIN 471) to cut, in order. May be empty.</summary>
        public List<ShaftGroove> Grooves { get; set; }

        /// <summary>DIN 509 undercuts (entalladuras E/F), one per shoulder at most. May be empty.</summary>
        public List<ShaftUndercut> Undercuts { get; set; }

        /// <summary>DIN 332 centre points (puntos de centrado), one per end at most. May be empty.</summary>
        public List<ShaftCenterHole> CenterHoles { get; set; }

        public double TotalLengthMm
        {
            get
            {
                double total = 0;
                foreach (var level in Levels) total += level.LengthMm;
                return total;
            }
        }

        public bool IsValid
        {
            get { return Validate() == null; }
        }

        /// <summary>
        /// Returns null when the spec is buildable, otherwise a Spanish reason. Re-checked host-side
        /// so invalid geometry is never modelled even if the page's JS were bypassed.
        /// </summary>
        public string Validate()
        {
            if (Levels == null || Levels.Count < 1)
            {
                return "El eje necesita al menos un nivel.";
            }
            for (int i = 0; i < Levels.Count; i++)
            {
                var level = Levels[i];
                if (level == null || !(level.DiameterMm > 0) || !(level.LengthMm > 0))
                {
                    return "Nivel " + (i + 1) + ": diámetro y longitud deben ser mayores que 0.";
                }
            }

            if (Keyways != null)
            {
                for (int k = 0; k < Keyways.Count; k++)
                {
                    string err = ValidateKeyway(Keyways[k]);
                    if (err != null) return "Chaveta " + (k + 1) + ": " + err;
                }
            }

            if (Grooves != null)
            {
                for (int g = 0; g < Grooves.Count; g++)
                {
                    string err = ValidateGroove(Grooves[g]);
                    if (err != null) return "Ranura " + (g + 1) + ": " + err;
                }
            }

            if (Undercuts != null)
            {
                for (int u = 0; u < Undercuts.Count; u++)
                {
                    string err = ValidateUndercut(Undercuts[u], u);
                    if (err != null) return "Entalladura " + (u + 1) + ": " + err;
                }
            }

            if (CenterHoles != null)
            {
                for (int c = 0; c < CenterHoles.Count; c++)
                {
                    string err = ValidateCenterHole(CenterHoles[c], c);
                    if (err != null) return "Punto de centrado " + (c + 1) + ": " + err;
                }
            }
            return null;
        }

        private string ValidateCenterHole(ShaftCenterHole hole, int index)
        {
            if (hole == null) return "sin datos.";
            if (hole.End < 0 || hole.End > 1) return "extremo no válido.";
            if (hole.Form != "A" && hole.Form != "B" && hole.Form != "R" && hole.Form != "D")
            {
                return "el tipo debe ser A, B, R o D (DIN 332).";
            }
            if (!(hole.PilotDiameterMm > 0) || !(hole.CountersinkDiameterMm > hole.PilotDiameterMm))
            {
                return "elige un tamaño DIN 332 (d2 debe ser mayor que d1).";
            }
            if (!(hole.PilotDepthMm > 0)) return "la profundidad del taladro debe ser mayor que 0.";
            if ((hole.Form == "B" || hole.Form == "D") && !(hole.ProtectDiameterMm > hole.CountersinkDiameterMm))
            {
                return "el avellanado de protección d3 debe ser mayor que d2.";
            }
            if (hole.Form == "R")
            {
                if (!(hole.ArcRadiusMm > 0) || !(hole.ArcFlankDepthMm() > 0))
                {
                    return "el radio r de la forma R es demasiado pequeño para el perfil.";
                }
            }
            if (hole.Form == "D")
            {
                if (!(hole.CounterboreDepthMm > 0)) return "la longitud del rebaje debe ser mayor que 0.";
                if (!(hole.ThreadDiameterMm > hole.PilotDiameterMm))
                {
                    return "el Ø nominal de la rosca debe ser mayor que el Ø de la broca d1.";
                }
            }

            var endLevel = hole.End == 0 ? Levels[0] : Levels[Levels.Count - 1];
            if (!(hole.MouthDiameterMm < endLevel.DiameterMm))
            {
                return "no cabe en la cara del extremo (Ø boca " + hole.MouthDiameterMm +
                       " ≥ Ø" + endLevel.DiameterMm + ").";
            }
            double depth = hole.TotalDepthMm();
            if (!(depth < endLevel.LengthMm - PositionToleranceMm))
            {
                return "es más profundo que el nivel del extremo (" + System.Math.Round(depth, 2) +
                       " ≥ " + endLevel.LengthMm + " mm).";
            }

            for (int i = 0; i < CenterHoles.Count; i++)
            {
                if (i != index && CenterHoles[i] != null && CenterHoles[i].End == hole.End)
                {
                    return "ya hay otro punto de centrado en ese extremo.";
                }
            }
            return null;
        }

        /// <summary>X positions (mm from the left face) of every level boundary: 0, after level 1,
        /// …, total. Index matches <see cref="ShaftKeyway.EdgeIndex"/>.</summary>
        public List<double> BoundariesMm()
        {
            var xs = new List<double> { 0 };
            double x = 0;
            foreach (var level in Levels) { x += level.LengthMm; xs.Add(x); }
            return xs;
        }

        private string ValidateKeyway(ShaftKeyway key)
        {
            if (key == null) return "sin datos.";
            if (!(key.WidthMm > 0) || !(key.LengthMm > 0) || !(key.DepthMm > 0))
            {
                return "ancho, largo y profundidad deben ser mayores que 0.";
            }
            if (key.CenterArc < 0 || key.CenterArc > 2)
            {
                return "modo de anclaje no válido.";
            }
            if (key.CenterArc == 0 && !(key.LengthMm > key.WidthMm))
            {
                return "el largo debe ser mayor que el ancho (forma A, extremos redondeados).";
            }
            if (key.CenterArc != 0 && !(key.LengthMm > key.WidthMm / 2.0))
            {
                return "la cota centro→extremo debe ser mayor que b/2.";
            }
            if (key.EdgeIndex < 0 || key.EdgeIndex > Levels.Count)
            {
                return "arista de referencia no válida.";
            }
            if (key.CenterArc == 0 && !(System.Math.Abs(key.OffsetMm) > PositionToleranceMm))
            {
                return "la cota de posición no puede ser 0.";
            }
            if (key.Count < 1)
            {
                return "el número de chavetas debe ser al menos 1.";
            }

            var xs = BoundariesMm();
            double total = xs[xs.Count - 1];
            double x1 = key.StartXMm(xs[key.EdgeIndex]);
            double x2 = x1 + key.SpanMm;

            // PARTIAL overhang past a shaft end is allowed (open keyway): some stretch must stay
            // on the shaft…
            if (!(x2 > PositionToleranceMm) || !(x1 < total - PositionToleranceMm))
            {
                return "la chaveta queda fuera del eje.";
            }
            // …but an arc extreme exactly ON an end face would leave a zero-thickness wall.
            if (System.Math.Abs(x1) <= PositionToleranceMm || System.Math.Abs(x2) <= PositionToleranceMm ||
                System.Math.Abs(x1 - total) <= PositionToleranceMm || System.Math.Abs(x2 - total) <= PositionToleranceMm)
            {
                return "un extremo del arco cae justo en la cara del extremo del eje.";
            }

            // Arc extremes must not sit exactly on a DIAMETER-CHANGE boundary (the cut would leave a
            // zero-thickness wall). Equal-diameter split-line boundaries are not walls: crossing or
            // touching them is geometrically fine.
            for (int i = 1; i < Levels.Count; i++)
            {
                if (System.Math.Abs(Levels[i - 1].DiameterMm - Levels[i].DiameterMm) < DiameterToleranceMm)
                {
                    continue;
                }
                double xc = xs[i];
                if (System.Math.Abs(x1 - xc) <= PositionToleranceMm || System.Math.Abs(x2 - xc) <= PositionToleranceMm)
                {
                    return "un extremo del arco cae justo en el cambio de nivel (x = " + xc + " mm).";
                }
            }

            // The reference diameter must belong to one of the levels the key spans.
            bool refFound = false;
            for (int i = 0; i < Levels.Count; i++)
            {
                if (xs[i + 1] <= x1 + PositionToleranceMm || xs[i] >= x2 - PositionToleranceMm)
                {
                    continue; // level does not overlap the key
                }
                if (System.Math.Abs(Levels[i].DiameterMm - key.RefDiameterMm) < DiameterToleranceMm)
                {
                    refFound = true;
                    break;
                }
            }
            if (!refFound)
            {
                return "el Ø de referencia debe ser el de un nivel que la chaveta atraviesa.";
            }
            if (!(key.DepthMm < key.RefDiameterMm / 2.0))
            {
                return "la profundidad debe ser menor que el radio de referencia.";
            }
            if (!(key.WidthMm < key.RefDiameterMm))
            {
                return "el ancho debe ser menor que el Ø de referencia.";
            }
            return null;
        }

        /// <summary>
        /// Diameter of the (single) surface the groove sits on, or 0 when the groove overlaps levels
        /// with different diameters (invalid). Assumes a valid edge index.
        /// </summary>
        public double GrooveSurfaceDiameterMm(ShaftGroove groove)
        {
            var xs = BoundariesMm();
            double x1 = groove.StartXMm(xs[groove.EdgeIndex]);
            double x2 = x1 + groove.WidthMm;
            double d = 0;
            for (int i = 0; i < Levels.Count; i++)
            {
                if (xs[i + 1] <= x1 + PositionToleranceMm || xs[i] >= x2 - PositionToleranceMm)
                {
                    continue; // level does not overlap the groove
                }
                if (d == 0) d = Levels[i].DiameterMm;
                else if (System.Math.Abs(Levels[i].DiameterMm - d) >= DiameterToleranceMm) return 0;
            }
            return d;
        }

        private string ValidateGroove(ShaftGroove groove)
        {
            if (groove == null) return "sin datos.";
            if (!(groove.WidthMm > 0) || !(groove.BottomDiameterMm > 0))
            {
                return "el ancho E1 y el Ø de fondo D3 deben ser mayores que 0.";
            }
            if (groove.EdgeIndex < 0 || groove.EdgeIndex > Levels.Count)
            {
                return "arista de referencia no válida.";
            }
            if (!(System.Math.Abs(groove.OffsetMm) > PositionToleranceMm))
            {
                return "la cota de posición no puede ser 0.";
            }

            var xs = BoundariesMm();
            double total = xs[xs.Count - 1];
            double x1 = groove.StartXMm(xs[groove.EdgeIndex]);
            double x2 = x1 + groove.WidthMm;
            if (!(x1 > PositionToleranceMm) || !(x2 < total - PositionToleranceMm))
            {
                return "la ranura se sale del eje (o toca un extremo).";
            }

            // The groove must stay clear of every DIAMETER-CHANGE boundary (crossing one would cut
            // into the step; touching leaves a zero-thickness wall). Equal-diameter split-line
            // boundaries are not walls and may be crossed.
            for (int i = 1; i < Levels.Count; i++)
            {
                if (System.Math.Abs(Levels[i - 1].DiameterMm - Levels[i].DiameterMm) < DiameterToleranceMm)
                {
                    continue;
                }
                double xc = xs[i];
                if (x1 <= xc + PositionToleranceMm && x2 >= xc - PositionToleranceMm)
                {
                    return "la ranura cruza o toca un cambio de nivel (x = " + xc + " mm).";
                }
            }

            double surfaceD = GrooveSurfaceDiameterMm(groove);
            if (!(surfaceD > 0))
            {
                return "la ranura debe caber entera en un nivel.";
            }
            if (!(groove.BottomDiameterMm < surfaceD))
            {
                return "el Ø de fondo D3 debe ser menor que el Ø del nivel (" + surfaceD + ").";
            }
            return null;
        }

        /// <summary>
        /// True when the small-diameter side of the shoulder is the LEFT level (diameter grows to
        /// the right, so the relief extends left of the boundary). Assumes a valid boundary index.
        /// </summary>
        public bool UndercutSmallSideIsLeft(ShaftUndercut undercut)
        {
            return Levels[undercut.BoundaryIndex - 1].DiameterMm < Levels[undercut.BoundaryIndex].DiameterMm;
        }

        /// <summary>
        /// Axial zone [z1, z2] the relief occupies (mm from the left face) and the diameter of the
        /// surface it is cut into. For form E one end of the zone is the shoulder itself; form F
        /// extends t2 PAST the shoulder into the big-diameter side (the face relief).
        /// </summary>
        public void UndercutZoneMm(ShaftUndercut undercut, out double z1, out double z2, out double smallDiameter)
        {
            var xs = BoundariesMm();
            double xShoulder = xs[undercut.BoundaryIndex];
            double t2 = undercut.IsFormF ? undercut.Depth2Mm : 0.0;
            if (UndercutSmallSideIsLeft(undercut))
            {
                z1 = xShoulder - undercut.WidthMm;
                z2 = xShoulder + t2;
                smallDiameter = Levels[undercut.BoundaryIndex - 1].DiameterMm;
            }
            else
            {
                z1 = xShoulder - t2;
                z2 = xShoulder + undercut.WidthMm;
                smallDiameter = Levels[undercut.BoundaryIndex].DiameterMm;
            }
        }

        /// <summary>
        /// Extent [start, end] (mm) of the continuous small-diameter SURFACE the relief lies on —
        /// the merged run of equal-diameter levels, because split-line boundaries are not walls.
        /// </summary>
        public void UndercutSegmentMm(ShaftUndercut undercut, out double start, out double end)
        {
            int smallIdx = UndercutSmallSideIsLeft(undercut) ? undercut.BoundaryIndex - 1 : undercut.BoundaryIndex;
            var xs = BoundariesMm();
            double d = Levels[smallIdx].DiameterMm;
            int first = smallIdx, last = smallIdx;
            while (first > 0 && System.Math.Abs(Levels[first - 1].DiameterMm - d) < DiameterToleranceMm) first--;
            while (last < Levels.Count - 1 && System.Math.Abs(Levels[last + 1].DiameterMm - d) < DiameterToleranceMm) last++;
            start = xs[first];
            end = xs[last + 1];
        }

        private string ValidateUndercut(ShaftUndercut undercut, int index)
        {
            if (undercut == null) return "sin datos.";
            if (undercut.BoundaryIndex < 1 || undercut.BoundaryIndex > Levels.Count - 1)
            {
                return "hombro no válido (¿cambiaste los niveles?).";
            }
            double dLeft = Levels[undercut.BoundaryIndex - 1].DiameterMm;
            double dRight = Levels[undercut.BoundaryIndex].DiameterMm;
            if (System.Math.Abs(dLeft - dRight) < DiameterToleranceMm)
            {
                return "la frontera elegida no es un hombro (los Ø son iguales).";
            }
            if (undercut.Form != "E" && undercut.Form != "F")
            {
                return "el tipo debe ser E o F (DIN 509).";
            }
            if (!(undercut.RadiusMm > 0) || !(undercut.DepthMm > 0) || !(undercut.WidthMm > 0))
            {
                return "elige un tamaño DIN 509 (r, t1 y f deben ser mayores que 0).";
            }
            if (undercut.IsFormF && !(undercut.Depth2Mm > 0))
            {
                return "la forma F necesita t2 mayor que 0.";
            }

            // Form E: the corner arc is tangent to the shoulder face at height r − t1 ABOVE the
            // small surface. Form F: the face relief exits on the shoulder face at height
            // max(r, t2/tan 8°) − t1 (the 8° run-out, or the fillet if it reaches higher). Either
            // way the shoulder must be tall enough to receive it.
            double shoulderH = System.Math.Abs(dLeft - dRight) / 2.0;
            double reliefTop = undercut.RadiusMm - undercut.DepthMm;
            if (undercut.IsFormF)
            {
                double faceRun = undercut.Depth2Mm / System.Math.Tan(UndercutFaceRunOutDeg * System.Math.PI / 180.0);
                reliefTop = System.Math.Max(undercut.RadiusMm, faceRun) - undercut.DepthMm;
            }
            if (!(shoulderH > reliefTop + PositionToleranceMm))
            {
                return "el hombro es demasiado bajo para este tamaño " + undercut.Form +
                       " (altura " + shoulderH + " ≤ " + reliefTop + " mm).";
            }
            // Profile must close with a flat bottom: f > r + t1/tan(15°). Always true for the
            // published table; guards a corrupt message.
            double ramp = undercut.DepthMm / System.Math.Tan(UndercutRunOutDeg * System.Math.PI / 180.0);
            if (!(undercut.WidthMm > undercut.RadiusMm + ramp + PositionToleranceMm))
            {
                return "el ancho f no cierra el perfil (f ≤ r + t1/tan 15°).";
            }

            // Only one undercut per shoulder.
            for (int i = 0; i < Undercuts.Count; i++)
            {
                if (i != index && Undercuts[i] != null && Undercuts[i].BoundaryIndex == undercut.BoundaryIndex)
                {
                    return "ya hay otra entalladura en ese hombro.";
                }
            }

            // The relief must fit on the continuous small surface: it may cross split lines but
            // not reach another diameter change or a shaft end.
            // One zone end IS the shoulder (= a segment boundary); only the FAR end — the 15°
            // run-out — must stay strictly inside the continuous surface.
            double z1, z2, smallD, segStart, segEnd;
            UndercutZoneMm(undercut, out z1, out z2, out smallD);
            UndercutSegmentMm(undercut, out segStart, out segEnd);
            bool fits = UndercutSmallSideIsLeft(undercut)
                ? z1 > segStart + PositionToleranceMm
                : z2 < segEnd - PositionToleranceMm;
            if (!fits)
            {
                return "el ancho f (" + undercut.WidthMm + " mm) no cabe en el tramo de Ø" + smallD + ".";
            }

            // Form F pokes t2 past the shoulder into the BIG side: that continuous surface must be
            // longer than t2 (split lines are not walls there either).
            if (undercut.IsFormF)
            {
                var xsB = BoundariesMm();
                double xShoulder = xsB[undercut.BoundaryIndex];
                int bigIdx = UndercutSmallSideIsLeft(undercut) ? undercut.BoundaryIndex : undercut.BoundaryIndex - 1;
                double dBig = Levels[bigIdx].DiameterMm;
                int firstB = bigIdx, lastB = bigIdx;
                while (firstB > 0 && System.Math.Abs(Levels[firstB - 1].DiameterMm - dBig) < DiameterToleranceMm) firstB--;
                while (lastB < Levels.Count - 1 && System.Math.Abs(Levels[lastB + 1].DiameterMm - dBig) < DiameterToleranceMm) lastB++;
                double room = UndercutSmallSideIsLeft(undercut) ? xsB[lastB + 1] - xShoulder : xShoulder - xsB[firstB];
                if (!(room > undercut.Depth2Mm + PositionToleranceMm))
                {
                    return "la profundidad t2 (" + undercut.Depth2Mm + " mm) no cabe en el tramo de Ø" + dBig + ".";
                }
            }

            // No overlap (or touch) with the other undercuts' zones…
            for (int i = 0; i < Undercuts.Count; i++)
            {
                if (i == index || Undercuts[i] == null) continue;
                double o1, o2, od;
                if (Undercuts[i].BoundaryIndex < 1 || Undercuts[i].BoundaryIndex > Levels.Count - 1) continue;
                UndercutZoneMm(Undercuts[i], out o1, out o2, out od);
                if (z1 < o2 + PositionToleranceMm && z2 > o1 - PositionToleranceMm)
                {
                    return "se solapa con la entalladura " + (i + 1) + ".";
                }
            }
            // …nor with retaining-ring grooves…
            var xs = BoundariesMm();
            if (Grooves != null)
            {
                for (int g = 0; g < Grooves.Count; g++)
                {
                    var groove = Grooves[g];
                    if (groove == null || groove.EdgeIndex < 0 || groove.EdgeIndex > Levels.Count) continue;
                    double g1 = groove.StartXMm(xs[groove.EdgeIndex]);
                    double g2 = g1 + groove.WidthMm;
                    if (z1 < g2 + PositionToleranceMm && z2 > g1 - PositionToleranceMm)
                    {
                        return "se solapa con la ranura de anillo " + (g + 1) + ".";
                    }
                }
            }
            // …nor with keyways (cut afterwards, they would eat the relief).
            if (Keyways != null)
            {
                for (int k = 0; k < Keyways.Count; k++)
                {
                    var key = Keyways[k];
                    if (key == null || key.EdgeIndex < 0 || key.EdgeIndex > Levels.Count) continue;
                    double k1 = key.StartXMm(xs[key.EdgeIndex]);
                    double k2 = k1 + key.SpanMm;
                    if (z1 < k2 + PositionToleranceMm && z2 > k1 - PositionToleranceMm)
                    {
                        return "se solapa con la chaveta " + (k + 1) + ".";
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// Merges consecutive equal-diameter levels into revolve segments and records where each
        /// swallowed boundary sits, so the builder can put a split line there. Assumes a valid spec.
        /// </summary>
        public List<ShaftSegment> GetMergedSegments()
        {
            var segments = new List<ShaftSegment>();
            double x = 0;
            foreach (var level in Levels)
            {
                var last = segments.Count > 0 ? segments[segments.Count - 1] : null;
                if (last != null && System.Math.Abs(last.DiameterMm - level.DiameterMm) < DiameterToleranceMm)
                {
                    last.SplitPositionsMm.Add(x);          // the merged boundary → split line here
                    last.LengthMm += level.LengthMm;
                }
                else
                {
                    segments.Add(new ShaftSegment
                    {
                        StartMm = x,
                        DiameterMm = level.DiameterMm,
                        LengthMm = level.LengthMm
                    });
                }
                x += level.LengthMm;
            }
            return segments;
        }
    }
}
