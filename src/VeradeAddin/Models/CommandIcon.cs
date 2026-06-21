namespace VeradeAddin.Models
{
    /// <summary>
    /// Which glyph a command draws on its ribbon button. Icons are generated at runtime by
    /// <c>IconStripFactory</c> (one flat, opaque vector glyph per value) so no binary image
    /// assets ship with the add-in. Add a value here and a matching case in the factory when
    /// introducing a new command.
    /// </summary>
    public enum CommandIcon
    {
        /// <summary>Generic fallback (the "V" mark).</summary>
        Default,
        Folder,
        Thread,
        Extract,
        Camera,
        Export,
        ColorEdges,
        ClearColors,
        Breakdown,
        Configure
    }
}
