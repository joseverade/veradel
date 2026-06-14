namespace VeradeAddin.Models
{
    /// <summary>
    /// User-entered geometry for the "Bulón personalizado" configurator. All values are in
    /// millimetres. The part is a stepped concentric cylinder revolved 360°: a head of diameter
    /// <see cref="HeadDiameterMm"/> × <see cref="HeadLengthMm"/> joined to a thinner shank of
    /// diameter <see cref="ShankDiameterMm"/> × <see cref="ShankLengthMm"/>, sharing one axis.
    /// Invariant: <see cref="HeadDiameterMm"/> must be strictly greater than
    /// <see cref="ShankDiameterMm"/> (validated in the UI and re-checked before modelling).
    /// </summary>
    public sealed class BoltSpec
    {
        public double HeadDiameterMm { get; set; }
        public double HeadLengthMm { get; set; }
        public double ShankDiameterMm { get; set; }
        public double ShankLengthMm { get; set; }

        /// <summary>True when every dimension is positive and Ø1 (head) &gt; Ø2 (shank).</summary>
        public bool IsValid
        {
            get
            {
                return HeadDiameterMm > 0 && HeadLengthMm > 0 &&
                       ShankDiameterMm > 0 && ShankLengthMm > 0 &&
                       HeadDiameterMm > ShankDiameterMm;
            }
        }
    }
}
