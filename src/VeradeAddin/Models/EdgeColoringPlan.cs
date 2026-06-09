using System.Collections.Generic;

namespace VeradeAddin.Models
{
    /// <summary>
    /// What the "Colorear aristas" command found in the active drawing: the referenced part(s) and
    /// the appearance colors detected on each. COM-free; feeds the selection dialog.
    /// </summary>
    public sealed class EdgeColoringPlan
    {
        public EdgeColoringPlan()
        {
            Parts = new List<EdgeColorPartInfo>();
        }

        public bool IsDrawing { get; set; }

        public List<EdgeColorPartInfo> Parts { get; private set; }

        /// <summary>Explanation when there is nothing to color (no part / no colors).</summary>
        public string Message { get; set; }

        public bool HasAnyColor
        {
            get
            {
                foreach (var p in Parts)
                {
                    if (p.Colors.Count > 0) return true;
                }
                return false;
            }
        }

        /// <summary>Sum of faces across referenced parts — shown in the pre-run warning (time proxy).</summary>
        public int TotalFaceCount
        {
            get
            {
                int n = 0;
                foreach (var p in Parts) n += p.FaceCount;
                return n;
            }
        }
    }

    /// <summary>A part referenced by the drawing and the distinct appearance colors found on it.</summary>
    public sealed class EdgeColorPartInfo
    {
        public EdgeColorPartInfo()
        {
            Colors = new List<DetectedColor>();
        }

        public string PartPath { get; set; }

        public string PartName { get; set; }

        /// <summary>Total faces of the part — proxy for how long colouring will take.</summary>
        public int FaceCount { get; set; }

        public List<DetectedColor> Colors { get; private set; }
    }

    /// <summary>One appearance color found on a part (source color).</summary>
    public sealed class DetectedColor
    {
        public int R { get; set; }
        public int G { get; set; }
        public int B { get; set; }

        /// <summary>Appearance/material name, for display.</summary>
        public string Name { get; set; }
    }
}
