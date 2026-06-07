using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;

namespace VeradeAddin.UI
{
    /// <summary>
    /// Generates the PNG icon strips the SolidWorks CommandManager needs, at runtime,
    /// so no binary image assets have to be shipped. SolidWorks expects, for each size,
    /// one horizontal strip image containing every command's icon side by side; the
    /// group flyout icon is a single image per size.
    ///
    /// Sizes per SolidWorks API guidance for ICommandGroup.IconList / MainIconList.
    /// Replace this with real artwork later by pointing the registrar at PNG files.
    /// </summary>
    public static class IconStripFactory
    {
        private static readonly int[] Sizes = { 20, 32, 40, 64, 96, 128 };
        private static readonly Color Background = Color.FromArgb(0x2D, 0x6C, 0xDF);

        private static string IconDir
        {
            get
            {
                var dir = Path.Combine(Path.GetTempPath(), "VeradelAddin", "icons");
                Directory.CreateDirectory(dir);
                return dir;
            }
        }

        /// <summary>
        /// One strip per size; each strip is (size * commandCount) wide.
        /// <paramref name="key"/> namespaces the files so each command group gets its OWN
        /// icon files — sharing identical file paths across groups prevents the second group's
        /// CommandManager tab from rendering.
        /// </summary>
        public static string[] CreateCommandIconStrips(string key, int commandCount)
        {
            if (commandCount < 1) commandCount = 1;
            string safe = Sanitize(key);
            var paths = new string[Sizes.Length];

            for (int s = 0; s < Sizes.Length; s++)
            {
                int size = Sizes[s];
                using (var bmp = new Bitmap(size * commandCount, size, PixelFormat.Format24bppRgb))
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(Background);
                    for (int c = 0; c < commandCount; c++)
                    {
                        DrawGlyph(g, new Rectangle(c * size, 0, size, size));
                    }
                    paths[s] = Save(bmp, "cmd_" + safe + "_" + size + "x" + commandCount);
                }
            }
            return paths;
        }

        /// <summary>One single-icon image per size for the command group flyout.</summary>
        public static string[] CreateGroupIcons(string key)
        {
            string safe = Sanitize(key);
            var paths = new string[Sizes.Length];
            for (int s = 0; s < Sizes.Length; s++)
            {
                int size = Sizes[s];
                using (var bmp = new Bitmap(size, size, PixelFormat.Format24bppRgb))
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(Background);
                    DrawGlyph(g, new Rectangle(0, 0, size, size));
                    paths[s] = Save(bmp, "grp_" + safe + "_" + size);
                }
            }
            return paths;
        }

        private static void DrawGlyph(Graphics g, Rectangle rect)
        {
            // Fully opaque square, no transparency / rounded corners. Alpha + custom strips
            // destabilise ICommandGroup.Activate() on SW 2026, so we keep the icon flat.
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

            using (var bg = new SolidBrush(Background))
            {
                g.FillRectangle(bg, rect);
            }

            using (var font = new Font("Segoe UI", rect.Height * 0.6f, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var textBrush = new SolidBrush(Color.White))
            using (var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                g.DrawString("V", font, textBrush, rect, fmt);
            }
        }

        private static string Save(Bitmap bmp, string baseName)
        {
            var file = Path.Combine(IconDir, baseName + ".png");
            bmp.Save(file, ImageFormat.Png);
            return file;
        }

        private static string Sanitize(string key)
        {
            if (string.IsNullOrEmpty(key)) return "grp";
            foreach (var c in Path.GetInvalidFileNameChars())
            {
                key = key.Replace(c, '_');
            }
            return key.Replace(' ', '_');
        }
    }
}
