namespace VeradeAddin.Models
{
    /// <summary>COM-free outcome of building the custom shaft ("Eje personalizado").</summary>
    public sealed class ShaftBuildResult
    {
        public bool Success { get; set; }
        /// <summary>Spanish reason when <see cref="Success"/> is false.</summary>
        public string Error { get; set; }

        public int LevelCount { get; set; }
        public double TotalLengthMm { get; set; }
        /// <summary>Split lines created at merged equal-diameter boundaries.</summary>
        public int SplitLineCount { get; set; }

        /// <summary>Keyways (chavetas) cut into the shaft.</summary>
        public int KeywayCount { get; set; }

        /// <summary>Retaining-ring grooves (ranuras DIN 471) cut into the shaft.</summary>
        public int GrooveCount { get; set; }

        /// <summary>DIN 509-E undercuts (entalladuras) cut at shoulders.</summary>
        public int UndercutCount { get; set; }
    }
}
