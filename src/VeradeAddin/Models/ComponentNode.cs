using System.Collections.Generic;

namespace VeradeAddin.Models
{
    /// <summary>
    /// One node in the assembly selection hierarchy. The root node represents the
    /// top-level assembly; children descend through subassemblies down to the
    /// selected component. COM-free so the UI layer can render it directly.
    /// </summary>
    public sealed class ComponentNode
    {
        public ComponentNode()
        {
            Children = new List<ComponentNode>();
        }

        /// <summary>Display name (component instance name, or assembly title for the root).</summary>
        public string Name { get; set; }

        /// <summary>Resolved file path. Empty/invalid for virtual or unsaved components.</summary>
        public string FilePath { get; set; }

        /// <summary>True when this node is the component the user actually selected.</summary>
        public bool IsSelectedTarget { get; set; }

        public bool IsVirtual { get; set; }

        public bool IsSuppressed { get; set; }

        public bool IsLightweight { get; set; }

        /// <summary>True when <see cref="FilePath"/> points at a file that exists on disk.</summary>
        public bool FileExists { get; set; }

        /// <summary>True when the path can be revealed in Explorer (real, non-virtual file).</summary>
        public bool CanOpen
        {
            get { return !IsVirtual && !string.IsNullOrWhiteSpace(FilePath); }
        }

        public List<ComponentNode> Children { get; private set; }
    }
}
