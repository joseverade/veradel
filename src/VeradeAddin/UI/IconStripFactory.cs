using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using VeradeAddin.Models;

namespace VeradeAddin.UI
{
    /// <summary>
    /// Generates the PNG icon strips the SolidWorks CommandManager needs, at runtime, so no binary
    /// image assets have to be shipped. SolidWorks expects, for each size, one horizontal strip image
    /// containing every command's icon side by side; the group flyout icon is a single image per size.
    ///
    /// Each <see cref="CommandIcon"/> draws its OWN professional vector glyph (folder, cosmetic-thread
    /// roller tip, camera, gear, exploded view, ...), fully redrawn per size so it stays crisp from
    /// 20&#215;20 to 128&#215;128.
    ///
    /// Command icons are drawn on a TRANSPARENT cell (32bpp PNG with alpha) so the glyph floats over
    /// the ribbon and its hover/selection highlight instead of showing a solid box. The add-in brand
    /// icon (group flyout) keeps an opaque GREEN background as the add-in identity mark.
    ///
    /// NOTE: a previous iteration avoided alpha because it was suspected of destabilising
    /// <c>ICommandGroup.Activate()</c> on SW 2026. If the ribbon group fails to appear after this
    /// change, that is the regression to look at first (revert command cells to an opaque fill).
    /// </summary>
    public static class IconStripFactory
    {
        private static readonly int[] Sizes = { 20, 32, 40, 64, 96, 128 };

        // Palette. Command cells are transparent; Background is reused only as a light "cut" detail
        // colour drawn ON glyphs (e.g. document fold lines), so it stays white.
        private static readonly Color Background = Color.White;                     // detail/cut colour on glyphs
        private static readonly Color BrandBackground = Color.FromArgb(0x2E, 0xA0, 0x4E); // add-in brand icon (green)
        private static readonly Color Ink = Color.Black;                            // primary glyph (on white)
        private static readonly Color Steel = Color.FromArgb(0x66, 0x70, 0x80);      // slate accent (visible on white)
        private static readonly Color Amber = Color.FromArgb(0xFF, 0xCB, 0x5E);      // folder
        private static readonly Color AmberLite = Color.FromArgb(0xFF, 0xDD, 0x94);
        private static readonly Color AmberDark = Color.FromArgb(0xC8, 0x90, 0x2F);
        private static readonly Color Green = Color.FromArgb(0x5A, 0xD0, 0x8A);      // out / export
        private static readonly Color Red = Color.FromArgb(0xF0, 0x55, 0x55);
        private static readonly Color Blue = Color.FromArgb(0x49, 0x9B, 0xFF);
        private static readonly Color Yellow = Color.FromArgb(0xFF, 0xD1, 0x4A);

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
        /// One strip per size; each strip is (size * icons.Length) wide, drawing <paramref name="icons"/>
        /// left to right so strip position <c>i</c> matches command image-index <c>i</c>.
        /// <paramref name="key"/> namespaces the files so each command group gets its OWN icon files.
        /// </summary>
        public static string[] CreateCommandIconStrips(string key, CommandIcon[] icons)
        {
            if (icons == null || icons.Length == 0) icons = new[] { CommandIcon.Default };
            string safe = Sanitize(key);
            var paths = new string[Sizes.Length];

            for (int s = 0; s < Sizes.Length; s++)
            {
                int size = Sizes[s];
                using (var bmp = new Bitmap(size * icons.Length, size, PixelFormat.Format32bppArgb))
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Transparent);
                    for (int c = 0; c < icons.Length; c++)
                    {
                        DrawGlyph(g, new Rectangle(c * size, 0, size, size), icons[c]);
                    }
                    paths[s] = Save(bmp, "cmd_" + safe + "_" + size + "x" + icons.Length);
                }
            }
            return paths;
        }

        /// <summary>One single-icon image per size for the command group flyout (brand mark).</summary>
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
                    g.Clear(BrandBackground);
                    DrawGlyph(g, new Rectangle(0, 0, size, size), CommandIcon.Default);
                    paths[s] = Save(bmp, "grp_" + safe + "_" + size);
                }
            }
            return paths;
        }

        // ---- glyphs ------------------------------------------------------------------------------

        private static void DrawGlyph(Graphics g, Rectangle cell, CommandIcon icon)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

            // Only the brand mark (Default) gets a filled (green) background; command glyphs are left
            // on the transparent cell so they blend into the ribbon.
            if (icon == CommandIcon.Default)
            {
                using (var bg = new SolidBrush(BrandBackground))
                {
                    g.FillRectangle(bg, cell);
                }
            }

            // Inner drawing rectangle (square) with a margin so glyphs never touch the edge.
            float m = cell.Width * 0.15f;
            var r = new RectangleF(cell.X + m, cell.Y + m, cell.Width - 2 * m, cell.Height - 2 * m);
            float unit = r.Width; // pen widths scale with icon size

            switch (icon)
            {
                case CommandIcon.Folder: Folder(g, r); break;
                case CommandIcon.Thread: Thread(g, r, unit); break;
                case CommandIcon.Extract: Extract(g, r, unit); break;
                case CommandIcon.Camera: Camera(g, r, unit); break;
                case CommandIcon.Export: Export(g, r, unit); break;
                case CommandIcon.ColorEdges: ColorEdges(g, r, unit, false); break;
                case CommandIcon.ClearColors: ColorEdges(g, r, unit, true); break;
                case CommandIcon.Breakdown: Breakdown(g, r, unit); break;
                case CommandIcon.Configure: Gear(g, r, unit); break;
                default: BrandV(g, cell); break;
            }
        }

        private static void BrandV(Graphics g, Rectangle cell)
        {
            using (var font = new Font("Segoe UI", cell.Height * 0.6f, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var brush = new SolidBrush(Ink))
            using (var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                g.DrawString("V", font, brush, cell, fmt);
            }
        }

        // Windows-Explorer style manila folder: back panel with tab + lighter open front flap.
        private static void Folder(Graphics g, RectangleF r)
        {
            float bodyTop = r.Top + r.Height * 0.30f;
            float bodyBot = r.Top + r.Height * 0.84f;

            var back = new[]
            {
                new PointF(r.Left, bodyTop),
                new PointF(r.Left, r.Top + r.Height * 0.20f),
                new PointF(r.Left + r.Width * 0.40f, r.Top + r.Height * 0.20f),
                new PointF(r.Left + r.Width * 0.52f, bodyTop),
                new PointF(r.Right, bodyTop),
                new PointF(r.Right, bodyBot),
                new PointF(r.Left, bodyBot)
            };
            var front = new[]
            {
                new PointF(r.Left, bodyBot),
                new PointF(r.Left + r.Width * 0.14f, bodyTop + r.Height * 0.10f),
                new PointF(r.Right, bodyTop + r.Height * 0.10f),
                new PointF(r.Right - r.Width * 0.14f, bodyBot)
            };

            using (var fill = new SolidBrush(Amber))
            using (var fillLite = new SolidBrush(AmberLite))
            using (var pen = new Pen(AmberDark, Math.Max(1f, r.Width * 0.05f)) { LineJoin = LineJoin.Round })
            {
                g.FillPolygon(fill, back);
                g.DrawPolygon(pen, back);
                g.FillPolygon(fillLite, front);
                g.DrawPolygon(pen, front);
            }
        }

        // Cosmetic-thread roller: a diagonal shaft (lower-left to upper-right) with a chamfered tip and
        // perpendicular thread ticks across it.
        private static void Thread(Graphics g, RectangleF r, float unit)
        {
            var a = new PointF(r.Left + r.Width * 0.16f, r.Bottom - r.Height * 0.14f);
            var b = new PointF(r.Left + r.Width * 0.72f, r.Top + r.Height * 0.30f);

            float dx = b.X - a.X, dy = b.Y - a.Y;
            float len = (float)Math.Sqrt(dx * dx + dy * dy);
            float ux = dx / len, uy = dy / len;     // shaft direction
            float px = -uy, py = ux;                 // perpendicular
            float half = unit * 0.16f;               // shaft half-thickness

            using (var rod = new Pen(Ink, half * 2f) { StartCap = LineCap.Round, EndCap = LineCap.Flat })
            using (var tick = new Pen(Steel, Math.Max(1f, unit * 0.055f)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            using (var ink = new SolidBrush(Ink))
            {
                g.DrawLine(rod, a, b);

                // chamfered conical tip past b
                var tip = new[]
                {
                    new PointF(b.X + px * half, b.Y + py * half),
                    new PointF(b.X - px * half, b.Y - py * half),
                    new PointF(b.X + ux * unit * 0.26f, b.Y + uy * unit * 0.26f)
                };
                g.FillPolygon(ink, tip);

                // thread ticks
                float[] ts = { 0.16f, 0.34f, 0.52f, 0.70f, 0.88f };
                foreach (var t in ts)
                {
                    var c = new PointF(a.X + dx * t, a.Y + dy * t);
                    g.DrawLine(tick,
                        new PointF(c.X + px * half * 1.15f, c.Y + py * half * 1.15f),
                        new PointF(c.X - px * half * 1.15f, c.Y - py * half * 1.15f));
                }
            }
        }

        // Pull a part out of an assembly: open container at lower-left + arrow lifting a cube out top-right.
        private static void Extract(Graphics g, RectangleF r, float unit)
        {
            using (var pen = new Pen(Ink, Math.Max(1f, unit * 0.09f)) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round })
            using (var cube = new SolidBrush(Green))
            {
                // open container (three sides)
                var box = new[]
                {
                    new PointF(r.Left + r.Width * 0.46f, r.Top + r.Height * 0.52f),
                    new PointF(r.Left, r.Top + r.Height * 0.52f),
                    new PointF(r.Left, r.Bottom),
                    new PointF(r.Right * 1f - r.Width * 0.0f, r.Bottom)
                };
                // draw left + bottom only (leave top-right open)
                g.DrawLines(pen, new[]
                {
                    new PointF(r.Left + r.Width * 0.42f, r.Top + r.Height * 0.55f),
                    new PointF(r.Left, r.Top + r.Height * 0.55f),
                    new PointF(r.Left, r.Bottom),
                    new PointF(r.Left + r.Width * 0.70f, r.Bottom),
                    new PointF(r.Left + r.Width * 0.70f, r.Top + r.Height * 0.62f)
                });

                // extracted cube
                var cr = new RectangleF(r.Left + r.Width * 0.60f, r.Top, r.Width * 0.34f, r.Width * 0.34f);
                g.FillRectangle(cube, cr);
                g.DrawRectangle(pen, cr.X, cr.Y, cr.Width, cr.Height);

                // lifting arrow
                var s = new PointF(r.Left + r.Width * 0.30f, r.Top + r.Height * 0.74f);
                var e = new PointF(r.Left + r.Width * 0.56f, r.Top + r.Height * 0.42f);
                g.DrawLine(pen, s, e);
                Arrowhead(g, pen, s, e, unit * 0.30f);
            }
        }

        private static void Camera(Graphics g, RectangleF r, float unit)
        {
            float bodyTop = r.Top + r.Height * 0.30f;
            var body = new RectangleF(r.Left, bodyTop, r.Width, r.Height * 0.55f);

            using (var white = new SolidBrush(Ink))
            using (var lens = new SolidBrush(Blue))
            using (var ring = new Pen(Ink, Math.Max(1f, unit * 0.06f)))
            using (var path = Rounded(body, unit * 0.14f))
            {
                // viewfinder bump
                var bump = new[]
                {
                    new PointF(r.Left + r.Width * 0.26f, bodyTop),
                    new PointF(r.Left + r.Width * 0.36f, r.Top + r.Height * 0.16f),
                    new PointF(r.Left + r.Width * 0.58f, r.Top + r.Height * 0.16f),
                    new PointF(r.Left + r.Width * 0.66f, bodyTop)
                };
                g.FillPolygon(white, bump);
                g.FillPath(white, path);

                float lr = r.Height * 0.17f;
                var lc = new PointF(body.Left + body.Width * 0.5f, body.Top + body.Height * 0.55f);
                g.FillEllipse(lens, lc.X - lr, lc.Y - lr, lr * 2, lr * 2);
                g.DrawEllipse(ring, lc.X - lr, lc.Y - lr, lr * 2, lr * 2);

                // flash dot
                float fr = r.Width * 0.05f;
                g.FillEllipse(lens, r.Right - r.Width * 0.20f, bodyTop + r.Height * 0.07f, fr * 2, fr * 2);
            }
        }

        // Document with an arrow leaving it (export out).
        private static void Export(Graphics g, RectangleF r, float unit)
        {
            var doc = new RectangleF(r.Left, r.Top + r.Height * 0.06f, r.Width * 0.60f, r.Height * 0.88f);
            float fold = r.Width * 0.20f;

            using (var white = new SolidBrush(Ink))
            using (var line = new Pen(Background, Math.Max(1f, unit * 0.05f)))
            using (var arrow = new Pen(Green, Math.Max(1.5f, unit * 0.11f)) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                var page = new[]
                {
                    new PointF(doc.Left, doc.Top),
                    new PointF(doc.Right - fold, doc.Top),
                    new PointF(doc.Right, doc.Top + fold),
                    new PointF(doc.Right, doc.Bottom),
                    new PointF(doc.Left, doc.Bottom)
                };
                g.FillPolygon(white, page);
                // folded corner
                g.DrawLines(line, new[]
                {
                    new PointF(doc.Right - fold, doc.Top),
                    new PointF(doc.Right - fold, doc.Top + fold),
                    new PointF(doc.Right, doc.Top + fold)
                });
                // text lines on page
                for (int i = 0; i < 3; i++)
                {
                    float ly = doc.Top + fold + r.Height * 0.18f + i * r.Height * 0.16f;
                    g.DrawLine(line, doc.Left + r.Width * 0.10f, ly, doc.Right - r.Width * 0.10f, ly);
                }

                // outgoing arrow (up-right)
                var s = new PointF(r.Left + r.Width * 0.50f, r.Bottom - r.Height * 0.16f);
                var e = new PointF(r.Right, r.Top + r.Height * 0.20f);
                g.DrawLine(arrow, s, e);
                Arrowhead(g, arrow, s, e, unit * 0.30f);
            }
        }

        // Iso diamond whose edges are coloured (ColorEdges) or reset + a "clear" slash (ClearColors).
        private static void ColorEdges(Graphics g, RectangleF r, float unit, bool clear)
        {
            var top = new PointF(r.Left + r.Width * 0.5f, r.Top);
            var right = new PointF(r.Right, r.Top + r.Height * 0.5f);
            var bot = new PointF(r.Left + r.Width * 0.5f, r.Bottom);
            var left = new PointF(r.Left, r.Top + r.Height * 0.5f);

            using (var face = new SolidBrush(Color.FromArgb(0x24, 0x55, 0xB5))) // darker face for contrast
            {
                g.FillPolygon(face, new[] { top, right, bot, left });
            }

            float w = Math.Max(1.5f, unit * 0.11f);
            if (clear)
            {
                using (var neutral = new Pen(Ink, w) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                using (var slash = new Pen(Red, Math.Max(2f, unit * 0.13f)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                {
                    g.DrawPolygon(neutral, new[] { top, right, bot, left });
                    g.DrawLine(slash, left.X + r.Width * 0.04f, top.Y + r.Height * 0.10f,
                                       right.X - r.Width * 0.04f, bot.Y - r.Height * 0.10f);
                }
            }
            else
            {
                using (var e1 = new Pen(Red, w) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                using (var e2 = new Pen(Green, w) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                using (var e3 = new Pen(Yellow, w) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                using (var e4 = new Pen(Steel, w) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                {
                    g.DrawLine(e1, top, right);
                    g.DrawLine(e2, right, bot);
                    g.DrawLine(e3, bot, left);
                    g.DrawLine(e4, left, top);
                }
            }
        }

        // Exploded view: three iso plates separated along a dashed vertical axis.
        private static void Breakdown(Graphics g, RectangleF r, float unit)
        {
            float cx = r.Left + r.Width * 0.5f;
            float pw = r.Width * 0.34f;   // plate half-width
            float sk = r.Width * 0.16f;   // iso skew
            float ph = r.Height * 0.16f;  // plate height

            using (var axis = new Pen(Steel, Math.Max(1f, unit * 0.05f)) { DashStyle = DashStyle.Dash })
            {
                g.DrawLine(axis, cx, r.Top, cx, r.Bottom);
            }

            using (var fill = new SolidBrush(Ink))
            using (var pen = new Pen(Background, Math.Max(1f, unit * 0.05f)) { LineJoin = LineJoin.Round })
            {
                for (int k = 0; k < 3; k++)
                {
                    float yt = r.Top + k * (r.Height * 0.30f) + r.Height * 0.04f;
                    var plate = new[]
                    {
                        new PointF(cx - pw, yt + ph),
                        new PointF(cx - pw + sk, yt),
                        new PointF(cx + pw, yt),
                        new PointF(cx + pw - sk, yt + ph)
                    };
                    g.FillPolygon(fill, plate);
                    g.DrawPolygon(pen, plate);
                }
            }
        }

        // Settings gear: toothed ring with a hub hole.
        private static void Gear(Graphics g, RectangleF r, float unit)
        {
            float cx = r.Left + r.Width * 0.5f, cy = r.Top + r.Height * 0.5f;
            float rOut = r.Width * 0.40f;
            float rTooth = r.Width * 0.50f;
            float rHole = r.Width * 0.16f;
            const int teeth = 8;
            double step = Math.PI * 2 / teeth;
            double tw = step * 0.30; // tooth half-angular-width

            using (var white = new SolidBrush(Ink))
            using (var bg = new SolidBrush(Background))
            using (var ring = new Pen(Steel, Math.Max(1f, unit * 0.06f)))
            {
                for (int i = 0; i < teeth; i++)
                {
                    double th = i * step;
                    var quad = new[]
                    {
                        Polar(cx, cy, th - tw, rOut * 0.96f),
                        Polar(cx, cy, th - tw * 0.7, rTooth),
                        Polar(cx, cy, th + tw * 0.7, rTooth),
                        Polar(cx, cy, th + tw, rOut * 0.96f)
                    };
                    g.FillPolygon(white, quad);
                }
                g.FillEllipse(white, cx - rOut, cy - rOut, rOut * 2, rOut * 2);
                g.FillEllipse(bg, cx - rHole, cy - rHole, rHole * 2, rHole * 2);
                g.DrawEllipse(ring, cx - rHole, cy - rHole, rHole * 2, rHole * 2);
            }
        }

        // ---- small drawing helpers ---------------------------------------------------------------

        private static PointF Polar(float cx, float cy, double ang, float rad)
        {
            return new PointF(cx + (float)Math.Cos(ang) * rad, cy + (float)Math.Sin(ang) * rad);
        }

        private static void Arrowhead(Graphics g, Pen pen, PointF from, PointF to, float size)
        {
            float dx = to.X - from.X, dy = to.Y - from.Y;
            float len = (float)Math.Sqrt(dx * dx + dy * dy);
            if (len < 1e-3f) return;
            float ux = dx / len, uy = dy / len;
            float px = -uy, py = ux;
            var b1 = new PointF(to.X - ux * size + px * size * 0.55f, to.Y - uy * size + py * size * 0.55f);
            var b2 = new PointF(to.X - ux * size - px * size * 0.55f, to.Y - uy * size - py * size * 0.55f);
            g.DrawLine(pen, to, b1);
            g.DrawLine(pen, to, b2);
        }

        private static GraphicsPath Rounded(RectangleF rect, float radius)
        {
            float d = radius * 2;
            var p = new GraphicsPath();
            p.AddArc(rect.Left, rect.Top, d, d, 180, 90);
            p.AddArc(rect.Right - d, rect.Top, d, d, 270, 90);
            p.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            p.AddArc(rect.Left, rect.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
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
