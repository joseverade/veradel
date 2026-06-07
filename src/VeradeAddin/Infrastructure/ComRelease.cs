using System.Runtime.InteropServices;

namespace VeradeAddin.Infrastructure
{
    /// <summary>
    /// Safe release of COM (RCW) references. SolidWorks hands out many short-lived COM
    /// objects (components, selection manager, ...); releasing them deterministically
    /// avoids RCW build-up over long sessions.
    /// </summary>
    public static class ComRelease
    {
        public static void Release(object comObject)
        {
            if (comObject != null && Marshal.IsComObject(comObject))
            {
                try { Marshal.ReleaseComObject(comObject); }
                catch { /* already released / detached — ignore */ }
            }
        }
    }
}
