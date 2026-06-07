using System;
using VeradeAddin.Models;

namespace VeradeAddin.Services
{
    /// <summary>
    /// Real screen capture (GDI), as opposed to SolidWorks' model-only render. Captures the
    /// pixels as actually displayed, so on-screen UI overlays — the Measure dialog, temporary
    /// dimensions, menus, callouts — are included. No COM, no SolidWorks types.
    /// </summary>
    public interface IScreenCaptureService
    {
        /// <summary>
        /// Captures the on-screen rectangle of the given window handle to a temporary PNG.
        /// Anything drawn on top of that rectangle is included. <paramref name="leftInset"/> pixels
        /// are trimmed from the left edge — pass the FeatureManager panel width so the capture is
        /// cropped to the graphics area (model space) only. Returns a COM-free result with the
        /// image path.
        /// </summary>
        ScreenshotResult CaptureWindow(IntPtr windowHandle, int leftInset);
    }
}
