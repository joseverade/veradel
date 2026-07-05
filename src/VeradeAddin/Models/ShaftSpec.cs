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
    /// about the axis) + slot extrude-cut + optional circular pattern.
    /// </summary>
    public sealed class ShaftKeyway
    {
        /// <summary>b: slot width (also the end-arc diameter).</summary>
        public double WidthMm { get; set; }
        /// <summary>l: TOTAL slot length along the axis, arc ends included. l ≥ b.</summary>
        public double LengthMm { get; set; }
        /// <summary>
        /// Reference edge the position is measured from: 0 = shaft left face, i = boundary after
        /// level i (1-based levels), levels.Count = right face.
        /// </summary>
        public int EdgeIndex { get; set; }
        /// <summary>
        /// Signed distance, never 0, ALWAYS from the edge to the LEFT arc extreme:
        /// x1 = edge + value. Negative puts the left extreme left of the edge (the key may
        /// straddle it).
        /// </summary>
        public double OffsetMm { get; set; }
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

        /// <summary>Left arc extreme X (mm from the shaft left face), given the edge position.</summary>
        public double StartXMm(double edgeX)
        {
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
        /// Signed distance, never 0, ALWAYS from the edge to the LEFT wall: x1 = edge + value.
        /// Negative puts the left wall left of the edge.
        /// </summary>
        public double OffsetMm { get; set; }

        /// <summary>Left wall X (mm from the shaft left face), given the edge position.</summary>
        public double StartXMm(double edgeX)
        {
            // The cota always points at the LEFT wall: x1 = edge + offset.
            return edgeX + OffsetMm;
        }
    }

    /// <summary>
    /// One DIN 509 form E undercut (entalladura) at a diameter-change shoulder. The relief is
    /// machined into the SMALLER-diameter cylinder against the shoulder face: width <see cref="WidthMm"/>
    /// (f) along the axis, depth <see cref="DepthMm"/> (t1) below the small surface, corner radius
    /// <see cref="RadiusMm"/> (r) tangent to the shoulder face, 15° run-out back to the surface.
    /// Sizes come from the DIN 509:1998 table (combobox in the UI, filtered by the small diameter);
    /// the host revalidates the geometry but not norm membership.
    /// </summary>
    public sealed class ShaftUndercut
    {
        /// <summary>Boundary index of the shoulder (1 … Levels.Count − 1). The two adjacent levels
        /// must have DIFFERENT diameters (an equal-diameter split boundary is not a shoulder).</summary>
        public int BoundaryIndex { get; set; }
        /// <summary>r: tool/corner radius, tangent to the shoulder face and the groove bottom.</summary>
        public double RadiusMm { get; set; }
        /// <summary>t1: depth below the small-diameter surface.</summary>
        public double DepthMm { get; set; }
        /// <summary>f: total axial width measured from the shoulder face.</summary>
        public double WidthMm { get; set; }
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

        /// <summary>Run-out angle of the form E relief back to the cylindrical surface (DIN 509).</summary>
        public const double UndercutRunOutDeg = 15.0;

        public ShaftSpec()
        {
            Levels = new List<ShaftLevel>();
            Keyways = new List<ShaftKeyway>();
            Grooves = new List<ShaftGroove>();
            Undercuts = new List<ShaftUndercut>();
        }

        /// <summary>Levels left → right, in the order the user entered them.</summary>
        public List<ShaftLevel> Levels { get; set; }

        /// <summary>Keyways to cut, in order. May be empty.</summary>
        public List<ShaftKeyway> Keyways { get; set; }

        /// <summary>Retaining-ring grooves (ranuras DIN 471) to cut, in order. May be empty.</summary>
        public List<ShaftGroove> Grooves { get; set; }

        /// <summary>DIN 509-E undercuts (entalladuras), one per shoulder at most. May be empty.</summary>
        public List<ShaftUndercut> Undercuts { get; set; }

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
            if (!(key.LengthMm >= key.WidthMm))
            {
                return "el largo debe ser ≥ el ancho (forma A, extremos redondeados).";
            }
            if (key.EdgeIndex < 0 || key.EdgeIndex > Levels.Count)
            {
                return "arista de referencia no válida.";
            }
            if (!(System.Math.Abs(key.OffsetMm) > PositionToleranceMm))
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
            double x2 = x1 + key.LengthMm;

            if (!(x1 > PositionToleranceMm) || !(x2 < total - PositionToleranceMm))
            {
                return "la chaveta se sale del eje (o toca un extremo).";
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
        /// surface it is cut into. One end of the zone is always the shoulder itself.
        /// </summary>
        public void UndercutZoneMm(ShaftUndercut undercut, out double z1, out double z2, out double smallDiameter)
        {
            var xs = BoundariesMm();
            double xShoulder = xs[undercut.BoundaryIndex];
            if (UndercutSmallSideIsLeft(undercut))
            {
                z1 = xShoulder - undercut.WidthMm;
                z2 = xShoulder;
                smallDiameter = Levels[undercut.BoundaryIndex - 1].DiameterMm;
            }
            else
            {
                z1 = xShoulder;
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
            if (!(undercut.RadiusMm > 0) || !(undercut.DepthMm > 0) || !(undercut.WidthMm > 0))
            {
                return "elige un tamaño DIN 509-E (r, t1 y f deben ser mayores que 0).";
            }

            // The corner arc is tangent to the shoulder face at height r − t1 ABOVE the small
            // surface: the shoulder must be tall enough to receive it.
            double shoulderH = System.Math.Abs(dLeft - dRight) / 2.0;
            if (!(shoulderH > undercut.RadiusMm - undercut.DepthMm + PositionToleranceMm))
            {
                return "el hombro es demasiado bajo para este tamaño (altura " + shoulderH +
                       " ≤ r − t1 = " + (undercut.RadiusMm - undercut.DepthMm) + " mm).";
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
                    double k2 = k1 + key.LengthMm;
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
