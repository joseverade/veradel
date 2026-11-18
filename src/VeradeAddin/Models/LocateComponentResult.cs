using System.Collections.Generic;

namespace VeradeAddin.Models
{
    /// <summary>
    /// Outcome of "Localizar en vista" (hide the parts covering the selected component, zoom to it). COM-free.
    /// </summary>
    public sealed class LocateComponentResult
    {
        public bool Success { get; set; }

        public string ComponentName { get; set; }

        /// <summary>Rays fired from the component towards the camera.</summary>
        public int RayCount { get; set; }

        /// <summary>Unit vector towards the viewer (assembly space) the rays followed; logged for diagnosis.</summary>
        public double[] ViewDirection { get; set; }

        /// <summary>Visible parts whose bounding box crossed at least one ray (then tested against their solids).</summary>
        public int Candidates { get; set; }

        /// <summary>Parts hidden because a ray crossed them.</summary>
        public List<string> HiddenComponents { get; } = new List<string>();

        /// <summary>
        /// How many of <see cref="HiddenComponents"/> were hidden on the bounding-box test alone because
        /// their solids were not available (lightweight / SpeedPak / graphics-only component).
        /// </summary>
        public int HiddenByBoxOnly { get; set; }

        /// <summary>Error detail when <see cref="Success"/> is false.</summary>
        public string Error { get; set; }
    }
}
