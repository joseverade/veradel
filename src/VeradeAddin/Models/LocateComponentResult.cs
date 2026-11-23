using System.Collections.Generic;

namespace VeradeAddin.Models
{
    /// <summary>
    /// Outcome of "Localizar en vista" (find a view where the selected component is seen, else hide the parts
    /// covering it; zoom to it). COM-free.
    /// </summary>
    public sealed class LocateComponentResult
    {
        public bool Success { get; set; }

        public string ComponentName { get; set; }

        /// <summary>Rays per sightline: start points kept on the component.</summary>
        public int RayCount { get; set; }

        /// <summary>Start points dropped for lying outside the component's own solid.</summary>
        public int StartPointsDiscarded { get; set; }

        /// <summary>Sightlines tested: the current view plus as many of the 26 standard directions as needed.</summary>
        public int SightlinesTested { get; set; }

        /// <summary>True when the camera was turned to another sightline (nothing hidden).</summary>
        public bool ViewRotated { get; set; }

        /// <summary>Unit vector towards the viewer (assembly space) of the sightline used; logged for diagnosis.</summary>
        public double[] ViewDirection { get; set; }

        /// <summary>Rays of that sightline no part blocks.</summary>
        public int FreeRays { get; set; }

        /// <summary>Visible parts whose bounding box crossed at least one ray of some sightline.</summary>
        public int Candidates { get; set; }

        /// <summary>Parts hidden because no sightline was free enough (covering the component in the current view).</summary>
        public List<string> HiddenComponents { get; } = new List<string>();

        /// <summary>
        /// How many of <see cref="HiddenComponents"/> were hidden on the bounding-box test alone because
        /// their solids were not available (SpeedPak / graphics-only component).
        /// </summary>
        public int HiddenByBoxOnly { get; set; }

        public long ElapsedMs { get; set; }

        /// <summary>Error detail when <see cref="Success"/> is false.</summary>
        public string Error { get; set; }
    }
}
