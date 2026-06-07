using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using VeradeAddin.Models;

namespace VeradeAddin.Services
{
    /// <summary>
    /// Captures a window straight off the screen with GDI, so on-screen overlays (Measure dialog,
    /// temporary dimensions, menus drawn over it) are included — unlike <c>IModelDoc2.SaveBMP</c>,
    /// which only renders the model. The caller supplies which window handle to grab (e.g. the
    /// SolidWorks graphics-area handle to crop to model space).
    /// </summary>
    public sealed class ScreenCaptureService : IScreenCaptureService
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        public ScreenshotResult CaptureWindow(IntPtr windowHandle, int leftInset)
        {
            var result = new ScreenshotResult();

            if (windowHandle == IntPtr.Zero)
            {
                result.Error = "Handle de ventana no válido.";
                return result;
            }

            RECT r;
            if (!GetWindowRect(windowHandle, out r))
            {
                result.Error = "No se pudo obtener el tamaño de la ventana.";
                return result;
            }

            int fullWidth = r.Right - r.Left;
            int height = r.Bottom - r.Top;
            if (fullWidth <= 0 || height <= 0)
            {
                result.Error = "La ventana de SolidWorks tiene un tamaño no válido.";
                return result;
            }

            // Trim the FeatureManager panel off the left, so only the graphics area remains.
            int inset = (leftInset > 0 && leftInset < fullWidth) ? leftInset : 0;
            int left = r.Left + inset;
            int width = fullWidth - inset;

            string dir = Path.Combine(Path.GetTempPath(), "VeradelAddin");
            Directory.CreateDirectory(dir);
            string pngPath = Path.Combine(dir, "captura_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + ".png");

            try
            {
                using (var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb))
                {
                    using (var g = Graphics.FromImage(bmp))
                    {
                        // Grab the on-screen pixels of the (cropped) window rectangle, overlays and all.
                        g.CopyFromScreen(left, r.Top, 0, 0, new Size(width, height), CopyPixelOperation.SourceCopy);
                    }
                    bmp.Save(pngPath, ImageFormat.Png);
                }
            }
            catch (Exception ex)
            {
                result.Error = "No se pudo capturar la pantalla: " + ex.Message;
                return result;
            }

            result.Success = true;
            result.ImagePath = pngPath;
            result.Width = width;
            result.Height = height;
            return result;
        }
    }
}
