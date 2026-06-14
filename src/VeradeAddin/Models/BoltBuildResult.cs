namespace VeradeAddin.Models
{
    /// <summary>COM-free outcome of building the custom bolt revolve.</summary>
    public sealed class BoltBuildResult
    {
        public bool Success { get; set; }
        public string Error { get; set; }

        /// <summary>Overall length (head + shank) in millimetres, for the success message.</summary>
        public double TotalLengthMm { get; set; }
    }
}
