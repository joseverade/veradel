namespace VeradeAddin.Models
{
    /// <summary>Outcome of extracting (freeing + moving) a component from an assembly. COM-free.</summary>
    public sealed class ExtractComponentResult
    {
        public bool Success { get; set; }

        public string ComponentName { get; set; }

        /// <summary>True if the component was fixed and the fix was removed.</summary>
        public bool WasFixed { get; set; }

        /// <summary>How many position mates were suppressed.</summary>
        public int MatesSuppressed { get; set; }

        /// <summary>True if the component was translated past the assembly bounding box.</summary>
        public bool Moved { get; set; }

        /// <summary>Error detail when <see cref="Success"/> is false.</summary>
        public string Error { get; set; }
    }
}
