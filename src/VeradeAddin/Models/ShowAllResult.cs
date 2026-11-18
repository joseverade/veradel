namespace VeradeAddin.Models
{
    /// <summary>Outcome of "Mostrar todo" (show every hidden component of the assembly). COM-free.</summary>
    public sealed class ShowAllResult
    {
        public bool Success { get; set; }

        /// <summary>True when SolidWorks' own "Mostrar con dependientes" ran on the top-level assembly.</summary>
        public bool RanShowDependents { get; set; }

        /// <summary>Components still hidden after that command, shown one by one (safety net).</summary>
        public int ShownByFallback { get; set; }

        /// <summary>Error detail when <see cref="Success"/> is false.</summary>
        public string Error { get; set; }
    }
}
