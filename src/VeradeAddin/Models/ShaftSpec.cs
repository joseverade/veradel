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
    /// half-section on the axis. Seven forms in two families:
    /// <list type="bullet">
    /// <item><b>DIN 332-1</b> (non-threaded, keyed by pilot Ø d1): <b>A</b> straight 60° countersink
    /// (d2→d1) + pilot bore + 120° tip; <b>R</b> same but the 60° flank is a tangent radius arc;
    /// <b>B</b> = A + a 120° conical protective chamfer at the mouth (Ø d3, width b); <b>C</b> = A +
    /// a truncated 60° protective chamfer (outer Ø d5 → inner Ø d4, width b, then a flat shelf down
    /// to d2).</item>
    /// <item><b>DIN 332-2</b> (threaded, keyed by thread M; IS/ISO 2540): <b>D</b> 60° contact cone
    /// (mouth d4 → straight seat d3) + tap-drill core bore d2 + 120° tip; <b>DR</b> the 60° contact
    /// is a spherical radius R; <b>DS</b> = D + a 120° protective chamfer at the mouth (Ø d5, depth
    /// t5). d1 is the nominal thread Ø (cosmetic thread not modelled).</item>
    /// </list>
    /// The mouth Ø must fit on the end face (leave a rim) and the total depth must stay within the
    /// end level. At most one centre point per end. Sizes come from the DIN 332 tables in the UI
    /// (resources/data/din_332.txt); the host revalidates the geometry via <see cref="ProfileMm"/>.
    /// </summary>
    public sealed class ShaftCenterHole
    {
        /// <summary>0 = left face (x = 0, drills toward +X); 1 = right face (x = total, toward −X).</summary>
        public int End { get; set; }
        /// <summary>"A", "B", "C", "R" (DIN 332-1) or "D", "DR", "DS" (DIN 332-2, threaded).</summary>
        public string Form { get; set; }
        /// <summary>d1: pilot bore Ø (non-threaded) or nominal thread Ø (threaded, cosmetic only).</summary>
        public double D1Mm { get; set; }
        /// <summary>d2: 60° countersink mouth Ø (non-threaded) or tap-drill core bore Ø (threaded).</summary>
        public double D2Mm { get; set; }
        /// <summary>d3: 120° protective Ø (form B) or straight seat Ø (threaded). 0 when unused.</summary>
        public double D3Mm { get; set; }
        /// <summary>d4: truncation inner Ø (form C) or 60° contact mouth Ø (threaded). 0 when unused.</summary>
        public double D4Mm { get; set; }
        /// <summary>d5: outer Ø (form C) or 120° protective mouth Ø (form DS). 0 when unused.</summary>
        public double D5Mm { get; set; }
        /// <summary>b: axial width of the protective chamfer (forms B and C). 0 when unused.</summary>
        public double BMm { get; set; }
        /// <summary>R: contact radius (form R derived, form DR tabulated). 0 for the straight forms.</summary>
        public double RadiusMm { get; set; }
        /// <summary>t: functional depth to the pilot-cylinder bottom (non-threaded). 0 for threaded.</summary>
        public double TMm { get; set; }
        /// <summary>t1: usable thread length (threaded, cosmetic). 0 non-threaded.</summary>
        public double T1Mm { get; set; }
        /// <summary>t2: total tap-drill depth to the tip (threaded). 0 non-threaded.</summary>
        public double T2Mm { get; set; }
        /// <summary>t3: depth to the straight-seat bottom (threaded). 0 non-threaded.</summary>
        public double T3Mm { get; set; }
        /// <summary>t4: depth of the 60° contact cone / DR arc (threaded). 0 non-threaded.</summary>
        public double T4Mm { get; set; }
        /// <summary>t5: depth of the 120° protective chamfer (form DS). 0 otherwise.</summary>
        public double T5Mm { get; set; }

        /// <summary>DIN 332-2 threaded family (D, DR, DS).</summary>
        public bool IsThreaded { get { return Form == "D" || Form == "DR" || Form == "DS"; } }
        /// <summary>The contact flank is a radius arc, not a straight cone (R and DR).</summary>
        public bool IsRadiusForm { get { return Form == "R" || Form == "DR"; } }
        /// <summary>Largest Ø at the face = what must fit on the end face with a rim.</summary>
        public double MouthDiameterMm
        {
            get { var p = ProfileMm(out _, out _); return p.Count > 0 ? 2.0 * p[0][1] : 0.0; }
        }

        /// <summary>Total drilled depth (mm) from the end face to the tip = deepest profile vertex.</summary>
        public double TotalDepthMm()
        {
            var p = ProfileMm(out _, out _);
            return p.Count > 0 ? p[p.Count - 1][0] : 0.0;
        }

        /// <summary>
        /// The single source of truth for the half-section, as an ordered list of {depth-from-face,
        /// radius} vertices from the mouth to the tip (radius reaches 0 at the tip). The face segment
        /// (axis → mouth) and the axis segment (tip → axis) close the revolve loop. When one flank is
        /// a radius arc (R, DR) <paramref name="arcSeg"/> is the index of the vertex it ends at (the
        /// segment pts[arcSeg-1]→pts[arcSeg] is that arc) and <paramref name="arcRadius"/> its radius;
        /// otherwise arcSeg = −1. Mirrors chProfilePts in configurator.js.
        /// </summary>
        public List<double[]> ProfileMm(out int arcSeg, out double arcRadius)
        {
            double tanCs = System.Math.Tan(ShaftSpec.CenterHoleCountersinkHalfDeg * System.Math.PI / 180.0);
            double tanTp = System.Math.Tan(ShaftSpec.CenterHoleTaperHalfDeg * System.Math.PI / 180.0);
            var pts = new List<double[]>();
            arcSeg = -1;
            arcRadius = 0.0;

            if (IsThreaded)
            {
                double rc = D2Mm / 2.0, r3 = D3Mm / 2.0, r4 = D4Mm / 2.0, r5 = D5Mm / 2.0;
                double tip = rc / tanTp;                              // 120° core-drill point
                double s = Form == "DS" ? T5Mm : 0.0;                 // mouth shift for the protective chamfer
                if (Form == "DS") pts.Add(new[] { 0.0, r5 });         // 120° protective mouth
                pts.Add(new[] { s, r4 });                             // 60°/arc contact mouth
                pts.Add(new[] { s + T4Mm, r3 });                      // contact bottom = seat top
                if (Form == "DR") { arcSeg = pts.Count - 1; arcRadius = RadiusMm; }
                pts.Add(new[] { s + T3Mm, r3 });                      // straight seat bottom
                pts.Add(new[] { s + T3Mm, rc });                      // step down to the core bore
                pts.Add(new[] { s + T2Mm - tip, rc });               // core cylinder bottom
                pts.Add(new[] { s + T2Mm, 0.0 });                    // 120° tip
                return pts;
            }

            double r1 = D1Mm / 2.0, r2 = D2Mm / 2.0;
            double hc = (r2 - r1) / tanCs;                            // 60° countersink axial depth
            double tipN = r1 / tanTp;                                 // 120° pilot-drill point
            double protAxial = 0.0;
            if (Form == "B")
            {
                double r3 = D3Mm / 2.0;
                protAxial = (r3 - r2) / tanTp;                        // 120° protective chamfer
                pts.Add(new[] { 0.0, r3 });
                pts.Add(new[] { protAxial, r2 });
            }
            else if (Form == "C")
            {
                double r4 = D4Mm / 2.0, r5 = D5Mm / 2.0;
                protAxial = (r5 - r4) / tanCs;                        // truncated 60° protective chamfer
                pts.Add(new[] { 0.0, r5 });
                pts.Add(new[] { protAxial, r4 });
                pts.Add(new[] { protAxial, r2 });                    // flat truncation shelf r4 → r2
            }
            else
            {
                pts.Add(new[] { 0.0, r2 });                          // A and R start at the 60° mouth
            }

            pts.Add(new[] { protAxial + hc, r1 });                   // countersink bottom = pilot top
            if (Form == "R")
            {
                // Radius flank tangent to the pilot cylinder: centre r above the pilot top, passing
                // through the mouth. R = (hc² + (r2−r1)²) / (2(r2−r1)); arc ends at this vertex.
                double dr = r2 - r1;
                if (dr > 0) { arcSeg = pts.Count - 1; arcRadius = (hc * hc + dr * dr) / (2.0 * dr); }
            }
            pts.Add(new[] { TMm, r1 });                              // pilot cylinder bottom
            pts.Add(new[] { TMm + tipN, 0.0 });                     // 120° tip
            return pts;
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

        private static readonly string[] CenterHoleForms = { "A", "B", "C", "R", "D", "DR", "DS" };

        private string ValidateCenterHole(ShaftCenterHole hole, int index)
        {
            if (hole == null) return "sin datos.";
            if (hole.End < 0 || hole.End > 1) return "extremo no válido.";
            if (System.Array.IndexOf(CenterHoleForms, hole.Form) < 0)
            {
                return "el tipo debe ser A, B, C, R, D, DR o DS (DIN 332).";
            }

            if (hole.IsThreaded)
            {
                // d2 core < d3 seat < d4 60° mouth; depths 0 < t4 < t3 < t2.
                if (!(hole.D2Mm > 0) || !(hole.D3Mm > hole.D2Mm) || !(hole.D4Mm > hole.D3Mm))
                {
                    return "elige un tamaño DIN 332-2 (d2 < d3 < d4).";
                }
                if (!(hole.T4Mm > 0) || !(hole.T3Mm > hole.T4Mm) || !(hole.T2Mm > hole.T3Mm))
                {
                    return "profundidades roscadas no válidas (0 < t4 < t3 < t2).";
                }
                if (hole.Form == "DR" && !(hole.RadiusMm > 0))
                {
                    return "la forma DR necesita un radio de contacto R mayor que 0.";
                }
                if (hole.Form == "DS" && (!(hole.D5Mm > hole.D4Mm) || !(hole.T5Mm > 0)))
                {
                    return "la forma DS necesita d5 > d4 y una profundidad de protección t5 > 0.";
                }
            }
            else
            {
                // d1 pilot < d2 60° mouth; functional depth t leaves a positive pilot cylinder.
                if (!(hole.D1Mm > 0) || !(hole.D2Mm > hole.D1Mm))
                {
                    return "elige un tamaño DIN 332-1 (d2 debe ser mayor que d1).";
                }
                if (!(hole.TMm > 0)) return "la profundidad t debe ser mayor que 0.";
                if (hole.Form == "B" && !(hole.D3Mm > hole.D2Mm))
                {
                    return "la forma B necesita el Ø de protección d3 mayor que d2.";
                }
                if (hole.Form == "C" && (!(hole.D4Mm > hole.D2Mm) || !(hole.D5Mm > hole.D4Mm)))
                {
                    return "la forma C necesita d2 < d4 < d5.";
                }
                if ((hole.Form == "B" || hole.Form == "C") && !(hole.BMm > 0))
                {
                    return "el ancho b del avellanado de protección debe ser mayor que 0.";
                }
            }

            // Geometry coherence from the single source of truth: depths strictly increase, radii
            // never grow, and the straight pilot/core cylinder keeps a positive length.
            var profile = hole.ProfileMm(out _, out double arcRadius);
            if (profile.Count < 3) return "perfil del punto de centrado incompleto.";
            for (int i = 1; i < profile.Count; i++)
            {
                if (profile[i][0] < profile[i - 1][0] - PositionToleranceMm ||
                    profile[i][1] > profile[i - 1][1] + PositionToleranceMm)
                {
                    return "las cotas dan un perfil imposible (revisa Ø y profundidades).";
                }
            }
            if (!(hole.TotalDepthMm() > PositionToleranceMm)) return "el perfil no tiene profundidad.";
            if (hole.IsRadiusForm && !(arcRadius > 0))
            {
                return "el radio de contacto es demasiado pequeño para el perfil.";
            }
            // The pilot/core cylinder (two consecutive vertices at equal radius) must be non-zero.
            bool hasCylinder = false;
            for (int i = 1; i < profile.Count; i++)
            {
                if (System.Math.Abs(profile[i][1] - profile[i - 1][1]) < DiameterToleranceMm &&
                    profile[i][0] - profile[i - 1][0] > PositionToleranceMm)
                {
                    hasCylinder = true;
                }
            }
            if (!hasCylinder) return "no queda tramo recto de broca (aumenta la profundidad).";

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
