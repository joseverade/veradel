using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace VeradeAddin.Infrastructure
{
    /// <summary>
    /// Safe release of COM (RCW) references. SolidWorks hands out many short-lived COM
    /// objects (components, selection manager, ...); releasing them deterministically
    /// avoids RCW build-up over long sessions.
    ///
    /// Why this matters: every interop call that returns an object — including PROPERTY reads
    /// like <c>IModelDoc2.Extension</c>, <c>.FeatureManager</c> or <c>.SelectionManager</c> —
    /// hands back a reference SolidWorks cannot free while we still hold it. Left to the GC
    /// they are only released at some indeterminate collection, so the SolidWorks process
    /// grows on every command and never shrinks.
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

        /// <summary>Releases several references in one call; nulls and non-COM values are ignored.</summary>
        public static void Release(params object[] comObjects)
        {
            if (comObjects == null) return;
            foreach (var o in comObjects)
            {
                Release(o);
            }
        }

        /// <summary>
        /// Releases every element of an array returned by SolidWorks (<c>GetFaces</c>,
        /// <c>GetBodies2</c>, <c>GetComponents</c>, ...). The array itself is a managed
        /// <c>object[]</c>; only its elements are RCWs.
        /// </summary>
        public static void ReleaseAll(System.Collections.IEnumerable comObjects)
        {
            if (comObjects == null) return;
            foreach (var o in comObjects)
            {
                Release(o);
            }
        }
    }

    /// <summary>
    /// A bag of COM references released together when the scope is disposed (LIFO, so children
    /// go before their parents). Lets a method acquire references inline —
    /// <c>var ext = com.Track(model.Extension)</c> — without growing a <c>finally</c> block per
    /// object, which is what made these releases easy to forget in the first place.
    ///
    /// Never track the ACTIVE DOCUMENT or anything handed back to the caller: releasing a
    /// reference we did not own detaches the RCW and the next use throws
    /// <see cref="InvalidComObjectException"/>. Use <see cref="Keep{T}"/> to pull an object back
    /// out of the scope when it turns out it must survive (a return value, or a reference stored
    /// in a field until it is replaced).
    /// </summary>
    public sealed class ComScope : IDisposable
    {
        private readonly List<object> _tracked = new List<object>();
        private bool _disposed;

        /// <summary>Registers a reference for release and returns it unchanged.</summary>
        public T Track<T>(T comObject) where T : class
        {
            if (comObject != null && Marshal.IsComObject(comObject))
            {
                _tracked.Add(comObject);
            }
            return comObject;
        }

        /// <summary>Registers every element of a SolidWorks array and returns the array unchanged.</summary>
        public object[] TrackAll(object[] comObjects)
        {
            if (comObjects != null)
            {
                foreach (var o in comObjects)
                {
                    Track(o);
                }
            }
            return comObjects;
        }

        /// <summary>
        /// Removes a reference from the scope so it survives disposal, and returns it unchanged.
        /// For values that outlive the method: a return value, or one stored in a field.
        /// </summary>
        public T Keep<T>(T comObject) where T : class
        {
            if (comObject != null)
            {
                // Reference equality: the CLR hands out ONE RCW per COM identity, so this removes
                // exactly the entry Track added. Only one entry goes, which is what we want when the
                // same object was tracked twice (two acquisitions = two references to give back).
                for (int i = _tracked.Count - 1; i >= 0; i--)
                {
                    if (ReferenceEquals(_tracked[i], comObject))
                    {
                        _tracked.RemoveAt(i);
                        break;
                    }
                }
            }
            return comObject;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            for (int i = _tracked.Count - 1; i >= 0; i--)
            {
                ComRelease.Release(_tracked[i]);
            }
            _tracked.Clear();
        }
    }
}
