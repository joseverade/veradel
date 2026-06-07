using System.Collections.Generic;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using VeradeAddin.Commands;
using VeradeAddin.Logging;
using VeradeAddin.Models;
using VeradeAddin.UI;

namespace VeradeAddin.Infrastructure
{
    /// <summary>
    /// Builds (and tears down) the ribbon from the <see cref="CommandRegistry"/>. Only class
    /// aware of SolidWorks CommandManager APIs.
    ///
    /// Pattern (the official SOLIDWORKS SDK approach): create ONE command group holding every
    /// command, then create ONE CommandManager tab per document type with
    /// <c>AddCommandTab(docType, name)</c> — "Veradel Pieza" (part), "Veradel Assembly"
    /// (assembly), "Veradel Dibujo" (drawing). Each tab is bound to a single document type, so a
    /// tab can NEVER appear under another document type. Each tab's command box contains only the
    /// commands whose <see cref="ICommand.DocumentTypes"/> includes that type, so "Abrir carpeta"
    /// (all three) shows in every tab and "Mostrar rosca" (drawing) only in "Veradel Dibujo".
    ///
    /// ShowInDocumentType is intentionally NOT used: it controls toolbars/menus, not tab binding,
    /// and setting it spawns a stray auto-tab.
    ///
    /// Callbacks are generic: a button calls "RunCommand(g)" / "EnableCommand(g)" where g is the
    /// command's GLOBAL index in the registry, so the same command can have a button in several
    /// tabs and still dispatch correctly.
    /// </summary>
    public sealed class CommandRegistrar
    {
        // swCommandItemType_e.swMenuItem | swToolbarItem
        private const int MenuAndToolbar = 3;
        private const int GroupId = RibbonIds.BaseGroupId;

        // One tab per document type, in ribbon order.
        private static readonly DocumentKind[] TabKinds =
        {
            DocumentKind.Part,
            DocumentKind.Assembly,
            DocumentKind.Drawing
        };

        private readonly ISldWorks _sw;
        private readonly int _cookie;
        private readonly CommandRegistry _registry;
        private readonly ILogger _log;

        private ICommandManager _cmdMgr;
        private readonly List<KeyValuePair<int, string>> _tabs = new List<KeyValuePair<int, string>>();

        public CommandRegistrar(ISldWorks sw, int cookie, CommandRegistry registry, ILogger log)
        {
            _sw = sw;
            _cookie = cookie;
            _registry = registry;
            _log = log;
        }

        public void Register()
        {
            _cmdMgr = _sw.GetCommandManager(_cookie);
            if (_cmdMgr == null)
            {
                _log.Log("AddIn", "n/a", LogOutcome.Error, "GetCommandManager returned null (cookie=" + _cookie + ")");
                return;
            }

            // ---- 1) one command group holding every command --------------------------------
            try { _cmdMgr.RemoveCommandGroup2(GroupId, false); } catch { }

            int errors = 0;
            var group = _cmdMgr.CreateCommandGroup2(GroupId, "Veradel", "Veradel", "Veradel", -1, true, ref errors);
            if (group == null)
            {
                _log.Log("AddIn", "n/a", LogOutcome.Error, "CreateCommandGroup2 returned null (errors=" + errors + ")");
                return;
            }

            group.IconList = IconStripFactory.CreateCommandIconStrips("Veradel", _registry.Count);
            group.MainIconList = IconStripFactory.CreateGroupIcons("Veradel");

            var itemIndices = new int[_registry.Count];
            for (int g = 0; g < _registry.Count; g++)
            {
                var cmd = _registry[g];
                itemIndices[g] = group.AddCommandItem2(
                    cmd.Name, -1, cmd.Hint, cmd.Tooltip, g,
                    "RunCommand(" + g + ")", "EnableCommand(" + g + ")",
                    GroupId + g, MenuAndToolbar);
            }

            group.HasToolbar = true;
            group.HasMenu = true;
            bool activated = group.Activate();

            // Resolve the global command IDs (valid after Activate) used by the tab boxes.
            var commandIds = new int[_registry.Count];
            for (int g = 0; g < _registry.Count; g++)
            {
                commandIds[g] = group.get_CommandID(itemIndices[g]);
            }

            _log.Log("AddIn", "n/a", LogOutcome.Success,
                "CommandGroup created: " + _registry.Count + " command(s); errors=" + errors + " Activate=" + activated);

            // ---- 2) one CommandManager tab per document type -------------------------------
            for (int t = 0; t < TabKinds.Length; t++)
            {
                BuildTab(TabKinds[t], commandIds);
            }
        }

        private void BuildTab(DocumentKind kind, int[] commandIds)
        {
            int swDocType = SwDocType(kind);
            string tabName = RibbonIds.TabFor(kind);

            // Command IDs (and matching text types) for the commands that belong in this tab.
            var ids = new List<int>();
            for (int g = 0; g < _registry.Count; g++)
            {
                if (Contains(_registry[g].DocumentTypes, kind) && commandIds[g] >= 0)
                {
                    ids.Add(commandIds[g]);
                }
            }

            if (ids.Count == 0)
            {
                return; // no commands for this document type -> no tab
            }

            // Remove any stale tab from a previous load, then rebuild cleanly.
            var existing = _cmdMgr.GetCommandTab(swDocType, tabName);
            if (existing != null)
            {
                _cmdMgr.RemoveCommandTab(existing);
            }

            var tab = _cmdMgr.AddCommandTab(swDocType, tabName);
            if (tab == null)
            {
                _log.Log("AddIn", "n/a", LogOutcome.Error, "AddCommandTab returned null for '" + tabName + "'");
                return;
            }

            var box = tab.AddCommandTabBox();
            if (box == null)
            {
                _log.Log("AddIn", "n/a", LogOutcome.Error, "AddCommandTabBox returned null for '" + tabName + "'");
                return;
            }

            var textTypes = new int[ids.Count];
            for (int i = 0; i < textTypes.Length; i++)
            {
                textTypes[i] = (int)swCommandTabButtonTextDisplay_e.swCommandTabButton_TextBelow;
            }

            bool added = box.AddCommands(ids.ToArray(), textTypes);
            _tabs.Add(new KeyValuePair<int, string>(swDocType, tabName));

            _log.Log("AddIn", "n/a", added ? LogOutcome.Success : LogOutcome.Error,
                "Tab '" + tabName + "' (docType=" + swDocType + ") commands=" + ids.Count + " AddCommands=" + added);
        }

        private static bool Contains(IReadOnlyList<DocumentKind> kinds, DocumentKind kind)
        {
            if (kinds == null) return false;
            for (int i = 0; i < kinds.Count; i++)
            {
                if (kinds[i] == kind) return true;
            }
            return false;
        }

        private static int SwDocType(DocumentKind kind)
        {
            // AddCommandTab takes a SINGLE swDocumentTypes_e value (not a bitmask), so the raw
            // enum values are correct here: Part = 1, Assembly = 2, Drawing = 3.
            switch (kind)
            {
                case DocumentKind.Part: return (int)swDocumentTypes_e.swDocPART;
                case DocumentKind.Assembly: return (int)swDocumentTypes_e.swDocASSEMBLY;
                case DocumentKind.Drawing: return (int)swDocumentTypes_e.swDocDRAWING;
                default: return (int)swDocumentTypes_e.swDocNONE;
            }
        }

        public void Unregister()
        {
            if (_cmdMgr == null)
            {
                return;
            }

            foreach (var tab in _tabs)
            {
                try
                {
                    var ct = _cmdMgr.GetCommandTab(tab.Key, tab.Value);
                    if (ct != null) _cmdMgr.RemoveCommandTab(ct);
                }
                catch { }
            }
            _tabs.Clear();

            try { _cmdMgr.RemoveCommandGroup2(GroupId, false); } catch { }

            Marshal.ReleaseComObject(_cmdMgr);
            _cmdMgr = null;

            _log.Log("AddIn", "n/a", LogOutcome.Success, "Unregistered all ribbon tabs");
        }
    }
}
