using System;
using System.Collections.Generic;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using VeradeAddin.Commands;
using VeradeAddin.Logging;
using VeradeAddin.Models;
using VeradeAddin.UI;

namespace VeradeAddin.Infrastructure
{
    /// <summary>
    /// Adds (and removes) the add-in's icons in the right-click CONTEXT TOOLBAR: the small icon box that
    /// pops up over the shortcut menu in the FeatureManager tree and the graphics area. That box belongs to
    /// <c>IFrame.AddMenuPopupIcon3</c>, not to the CommandManager (whose AddContextMenu only adds text items
    /// to the menu list underneath).
    ///
    /// One icon per (command, document type, target). AddMenuPopupIcon3 invokes its callbacks by bare method
    /// name on the object given to SetAddinCallbackInfo2 (its Identifier is the add-in cookie), with no
    /// argument, so each command gets a fixed slot on the add-in class: "ContextRun{i}" / "ContextEnable{i}",
    /// where i is the command's index in the context registry. Up to <see cref="SlotCount"/> commands.
    /// </summary>
    public sealed class ContextToolbarRegistrar
    {
        /// <summary>Number of ContextRun{i} / ContextEnable{i} slot methods on the add-in class.</summary>
        public const int SlotCount = 4;

        private readonly ISldWorks _sw;
        private readonly int _cookie;
        private readonly CommandRegistry _registry;
        private readonly ILogger _log;

        // (docType, selectType) of every icon added, for removal on unload.
        private readonly List<KeyValuePair<int, int>> _icons = new List<KeyValuePair<int, int>>();

        public ContextToolbarRegistrar(ISldWorks sw, int cookie, CommandRegistry registry, ILogger log)
        {
            _sw = sw;
            _cookie = cookie;
            _registry = registry;
            _log = log;
        }

        public void Register()
        {
            var frame = _sw.Frame() as IFrame;
            if (frame == null)
            {
                _log.Log("AddIn", "n/a", LogOutcome.Error, "Context toolbar: ISldWorks.Frame() returned null");
                return;
            }

            try
            {
                for (int i = 0; i < _registry.Count; i++)
                {
                    var cmd = _registry[i] as IContextToolbarCommand;
                    if (cmd == null || i >= SlotCount)
                    {
                        _log.Log("AddIn", "n/a", LogOutcome.Error,
                            "Context toolbar: skipped '" + _registry[i].Name + "' (not a context command or no free slot)");
                        continue;
                    }

                    // A one-command strip is exactly the single image per size the context toolbar wants.
                    var images = IconStripFactory.CreateCommandIconStrips("ctx_" + i, new[] { cmd.Icon });

                    foreach (var kind in cmd.DocumentTypes)
                    {
                        foreach (var target in cmd.ContextTargets)
                        {
                            int docType = CommandRegistrar.SwDocType(kind);
                            int selType = SwSelectType(target);

                            // A failing icon must never stop the add-in (and its ribbon) from loading.
                            bool added = false;
                            string error = null;
                            try
                            {
                                added = frame.AddMenuPopupIcon3(docType, selType, cmd.Name, _cookie,
                                    "ContextRun" + i, "ContextEnable" + i, "", images);
                            }
                            catch (Exception ex)
                            {
                                error = ex.ToString();
                            }

                            if (added)
                            {
                                _icons.Add(new KeyValuePair<int, int>(docType, selType));
                            }

                            _log.Log("AddIn", "n/a", added ? LogOutcome.Success : LogOutcome.Error,
                                "Context icon '" + cmd.Name + "' docType=" + docType + " selType=" + selType + " added=" + added,
                                error);
                        }
                    }
                }
            }
            finally { ComRelease.Release(frame); }
        }

        public void Unregister()
        {
            if (_icons.Count == 0)
            {
                return;
            }

            var frame = _sw.Frame() as IFrame;
            try
            {
                foreach (var icon in _icons)
                {
                    // Index = the Identifier given to AddMenuPopupIcon3 (the cookie). Best effort.
                    try { if (frame != null) frame.RemoveMenuPopupIcon(_cookie, icon.Key, icon.Value); }
                    catch { }
                }
            }
            finally
            {
                _icons.Clear();
                ComRelease.Release(frame);
            }

            _log.Log("AddIn", "n/a", LogOutcome.Success, "Removed context toolbar icons");
        }

        private static int SwSelectType(ContextToolbarTarget target)
        {
            switch (target)
            {
                case ContextToolbarTarget.Component: return (int)swSelectType_e.swSelCOMPONENTS;
                default: return (int)swSelectType_e.swSelNOTHING;
            }
        }
    }
}
