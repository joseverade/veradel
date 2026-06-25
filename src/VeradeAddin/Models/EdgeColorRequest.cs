using System.Collections.Generic;

namespace VeradeAddin.Models
{
    /// <summary>
    /// What the user chose in the "Colorear aristas" dialog: for each ticked source color, the
    /// target palette color to apply to the matching edges. COM-free.
    /// </summary>
    public sealed class EdgeColorRequest
    {
        public EdgeColorRequest()
        {
            Mappings = new List<EdgeColorMapping>();
        }

        /// <summary>The drawing view (by name) the colouring is applied to. Set by the command from the plan.</summary>
        public string ViewName { get; set; }

        public List<EdgeColorMapping> Mappings { get; private set; }
    }

    /// <summary>Maps one part's source appearance color to the edge (target) color to apply.</summary>
    public sealed class EdgeColorMapping
    {
        public string PartPath { get; set; }

        public int SourceR { get; set; }
        public int SourceG { get; set; }
        public int SourceB { get; set; }

        public int TargetR { get; set; }
        public int TargetG { get; set; }
        public int TargetB { get; set; }
    }
}
