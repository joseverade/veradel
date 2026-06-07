namespace VeradeAddin.Models
{
    /// <summary>
    /// Result of capturing the active model view to an image file. COM-free so the command
    /// layer can report/log without touching SolidWorks types.
    /// </summary>
    public sealed class ScreenshotResult
    {
        public bool Success { get; set; }

        /// <summary>Full path to the captured image (a temporary PNG) when <see cref="Success"/>.</summary>
        public string ImagePath { get; set; }

        public int Width { get; set; }

        public int Height { get; set; }

        /// <summary>Title of the captured document, for logging.</summary>
        public string DocumentTitle { get; set; }

        /// <summary>Human-readable failure reason when not successful.</summary>
        public string Error { get; set; }
    }
}
