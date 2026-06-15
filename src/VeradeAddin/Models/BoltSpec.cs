using System;

namespace VeradeAddin.Models
{
    /// <summary>
    /// User-entered geometry for the "Bulón personalizado" configurator. All values are in
    /// millimetres. The part is a stepped concentric cylinder revolved 360°: a head of diameter
    /// <see cref="HeadDiameterMm"/> × <see cref="HeadLengthMm"/> joined to a thinner shank of
    /// diameter <see cref="ShankDiameterMm"/> × <see cref="ShankLengthMm"/>, sharing one axis.
    ///
    /// Two optional features live on the shank, both built into the same revolve profile:
    /// <list type="bullet">
    /// <item>A retaining-ring <b>groove</b> (ranura): a recess of diameter <see cref="GrooveDiameterMm"/>
    /// (D3) and width <see cref="GrooveWidthMm"/> (E1), positioned <see cref="GroovePositionMm"/> (P1)
    /// from the head face to its near (head-side) edge. D3 may come from the DIN 471 table or be
    /// custom.</item>
    /// <item>A 45°-style <b>chamfer</b> (chaflán) on the free end: an axial cathetus of
    /// <see cref="ChamferSizeMm"/> at <see cref="ChamferAngleDeg"/> measured from the axis (45° ⇒ the
    /// radial drop equals the axial size). It must not overlap the groove.</item>
    /// </list>
    ///
    /// Invariant: <see cref="HeadDiameterMm"/> must be strictly greater than
    /// <see cref="ShankDiameterMm"/> (validated in the UI and re-checked before modelling).
    /// </summary>
    public sealed class BoltSpec
    {
        public double HeadDiameterMm { get; set; }
        public double HeadLengthMm { get; set; }
        public double ShankDiameterMm { get; set; }
        public double ShankLengthMm { get; set; }

        // ---- groove (ranura), optional ----
        public bool HasGroove { get; set; }
        /// <summary>P1: distance from the head face to the groove's near (head-side) edge.</summary>
        public double GroovePositionMm { get; set; }
        /// <summary>E1: groove width along the axis.</summary>
        public double GrooveWidthMm { get; set; }
        /// <summary>D3: groove bottom diameter (must be smaller than the shank Ø2).</summary>
        public double GrooveDiameterMm { get; set; }

        // ---- chamfer (chaflán) on the free end, optional ----
        public bool HasChamfer { get; set; }
        /// <summary>Chamfer angle measured from the axis, in degrees. 45° by default.</summary>
        public double ChamferAngleDeg { get; set; }
        /// <summary>Axial cathetus length of the chamfer (the "size").</summary>
        public double ChamferSizeMm { get; set; }

        /// <summary>True when every dimension is positive and Ø1 (head) &gt; Ø2 (shank).</summary>
        public bool IsValid
        {
            get { return Validate() == null; }
        }

        /// <summary>
        /// Returns null when the spec is buildable, otherwise a Spanish reason. Re-checked here so the
        /// host never models invalid geometry even if the page's JS were bypassed. Mirrors the rules
        /// enforced live in the configurator.
        /// </summary>
        public string Validate()
        {
            if (!(HeadDiameterMm > 0 && HeadLengthMm > 0 && ShankDiameterMm > 0 && ShankLengthMm > 0))
            {
                return "Todas las medidas deben ser mayores que 0.";
            }
            if (!(HeadDiameterMm > ShankDiameterMm))
            {
                return "Ø1 (cabeza) debe ser mayor que Ø2 (vástago).";
            }

            double grooveEnd = GroovePositionMm + GrooveWidthMm;
            if (HasGroove)
            {
                if (!(GroovePositionMm > 0 && GrooveWidthMm > 0 && GrooveDiameterMm > 0))
                {
                    return "La ranura: P1, E1 y D3 deben ser mayores que 0.";
                }
                if (!(GrooveDiameterMm < ShankDiameterMm))
                {
                    return "D3 (fondo de ranura) debe ser menor que Ø2 (vástago).";
                }
                if (!(grooveEnd <= ShankLengthMm))
                {
                    return "La ranura se sale del vástago (P1 + E1 debe ser ≤ L2).";
                }
            }

            if (HasChamfer)
            {
                if (!(ChamferAngleDeg > 0 && ChamferAngleDeg < 90))
                {
                    return "El ángulo del chaflán debe estar entre 0° y 90°.";
                }
                if (!(ChamferSizeMm > 0))
                {
                    return "La medida del chaflán debe ser mayor que 0.";
                }
                double radialDrop = ChamferSizeMm * Math.Tan(ChamferAngleDeg * Math.PI / 180.0);
                if (!(radialDrop < ShankDiameterMm / 2.0))
                {
                    return "El chaflán es demasiado grande (consume todo el radio del vástago).";
                }
                // Chamfer starts this far back from the free end; it must not reach the groove.
                double chamferStart = ShankLengthMm - ChamferSizeMm;
                double limit = HasGroove ? grooveEnd : 0.0;
                if (!(chamferStart >= limit))
                {
                    return HasGroove
                        ? "El chaflán se come la ranura (reduce su medida)."
                        : "El chaflán es más largo que el vástago.";
                }
            }

            return null;
        }
    }
}
