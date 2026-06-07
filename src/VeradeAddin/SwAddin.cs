using System;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swpublished;
using VeradeAddin.Infrastructure;
using VeradeAddin.Logging;

namespace VeradeAddin
{
    /// <summary>
    /// Add-in entry point. SolidWorks loads this via COM, calls <see cref="ConnectToSW"/>
    /// on load and <see cref="DisconnectFromSW"/> on unload. Responsibilities are kept thin:
    /// build the composition root, register the ribbon, and route ribbon callbacks to commands.
    /// All real work lives in services and commands.
    /// </summary>
    [ComVisible(true)]
    [Guid("8B3E2A14-6C9D-4F1A-9E2B-7A5C1D3F8E40")]
    [ProgId("VeradeAddin.SwAddin")]
    public class VeradelAddin : SwAddin
    {
        private const string Title = "Veradel Addin";
        private const string Description = "Veradel Addin for SolidWorks";

        private SldWorks _swApp;
        private int _cookie;
        private CompositionRoot _root;

        #region ISwAddin

        public bool ConnectToSW(object ThisSW, int Cookie)
        {
            _swApp = ThisSW as SldWorks;
            if (_swApp == null)
            {
                return false;
            }

            _cookie = Cookie;

            // Required so SolidWorks can route command callbacks to this instance.
            _swApp.SetAddinCallbackInfo2(0, this, _cookie);

            _root = new CompositionRoot(_swApp, _cookie);

            try
            {
                _root.Logger.Log("AddIn", "n/a", LogOutcome.Success, "ConnectToSW");
                _root.CommandRegistrar.Register();
                return true;
            }
            catch (Exception ex)
            {
                _root.Logger.Log("AddIn", "n/a", LogOutcome.Error, "ConnectToSW failed", ex.ToString());
                return false;
            }
        }

        public bool DisconnectFromSW()
        {
            try
            {
                _root?.CommandRegistrar.Unregister();
                _root?.Logger.Log("AddIn", "n/a", LogOutcome.Success, "DisconnectFromSW");
            }
            catch
            {
                // Never block SolidWorks shutdown.
            }
            finally
            {
                _root = null;
                if (_swApp != null)
                {
                    Marshal.ReleaseComObject(_swApp);
                    _swApp = null;
                }
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }

            return true;
        }

        #endregion

        #region SolidWorks command callbacks (invoked by name via SetAddinCallbackInfo2)

        // Wired by CommandRegistrar as "RunCommand(i)" / "EnableCommand(i)". Pure dispatch.

        public void RunCommand(int commandIndex)
        {
            _root?.Execute(commandIndex);
        }

        public int EnableCommand(int commandIndex)
        {
            return _root?.CanExecute(commandIndex) ?? 0;
        }

        #endregion

        #region COM registration (writes the SolidWorks add-in registry keys)

        [ComRegisterFunction]
        public static void RegisterFunction(Type t)
        {
            string guid = "{" + t.GUID.ToString().ToUpperInvariant() + "}";

            using (var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\SolidWorks\AddIns\" + guid))
            {
                key.SetValue(null, 1, RegistryValueKind.DWord); // load by default
                key.SetValue("Title", Title);
                key.SetValue("Description", Description);
            }

            using (var key = Registry.CurrentUser.CreateSubKey(@"Software\SolidWorks\AddInsStartup\" + guid))
            {
                key.SetValue(null, 1, RegistryValueKind.DWord);
            }
        }

        [ComUnregisterFunction]
        public static void UnregisterFunction(Type t)
        {
            string guid = "{" + t.GUID.ToString().ToUpperInvariant() + "}";
            Registry.LocalMachine.DeleteSubKeyTree(@"SOFTWARE\SolidWorks\AddIns\" + guid, false);
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\SolidWorks\AddInsStartup\" + guid, false);
        }

        #endregion
    }
}
