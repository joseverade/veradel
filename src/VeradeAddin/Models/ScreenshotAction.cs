namespace VeradeAddin.Models
{
    /// <summary>What the user chose in the screenshot preview dialog.</summary>
    public enum ScreenshotAction
    {
        /// <summary>Closed without saving or copying.</summary>
        None,

        /// <summary>Chose "Guardar como" and picked a destination path.</summary>
        Saved,

        /// <summary>Copied the image to the clipboard.</summary>
        Copied
    }
}
