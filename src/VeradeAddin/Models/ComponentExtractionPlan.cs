namespace VeradeAddin.Models
{
    /// <summary>
    /// What the "Extraer pieza" command would do to the selected component — used to warn the
    /// user before any change. COM-free.
    /// </summary>
    public sealed class ComponentExtractionPlan
    {
        public string ComponentName { get; set; }

        /// <summary>True when the component is fixed (its "Fixed" state must be removed).</summary>
        public bool IsFixed { get; set; }

        /// <summary>Number of position mates referencing the component (to be suppressed).</summary>
        public int MateCount { get; set; }

        /// <summary>
        /// True when the component is an instance of a component pattern or mirror. Moving such
        /// an instance would break the pattern, so extraction must be refused with a warning.
        /// </summary>
        public bool IsPatternInstance { get; set; }
    }
}
