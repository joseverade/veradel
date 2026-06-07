namespace VeradeAddin.Models
{
    /// <summary>
    /// Document type resolved from the active SolidWorks model, decoupled from
    /// the COM enum <c>swDocumentTypes_e</c> so the rest of the code never touches COM.
    /// </summary>
    public enum DocumentKind
    {
        None = 0,
        Part = 1,
        Assembly = 2,
        Drawing = 3,
        Unknown = 99
    }
}
