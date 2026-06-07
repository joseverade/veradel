using VeradeAddin.Models;

namespace VeradeAddin.Infrastructure
{
    /// <summary>Stable identifiers and captions for the add-in's ribbon UI.</summary>
    internal static class RibbonIds
    {
        /// <summary>Base CommandGroup UserID. Each per-document-type tab gets BaseGroupId + index.</summary>
        public const int BaseGroupId = 9100;

        /// <summary>
        /// One ribbon tab per document type. A command appears in every tab whose document
        /// type is listed in its <see cref="Commands.ICommand.DocumentTypes"/>.
        /// </summary>
        public static string TabFor(DocumentKind kind)
        {
            switch (kind)
            {
                case DocumentKind.Part: return "Veradel Pieza";
                case DocumentKind.Assembly: return "Veradel Assembly";
                case DocumentKind.Drawing: return "Veradel Dibujo";
                default: return "Veradel";
            }
        }
    }
}
