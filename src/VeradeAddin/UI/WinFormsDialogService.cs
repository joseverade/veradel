using System;
using System.Collections.Generic;
using System.Windows.Forms;
using VeradeAddin.Models;
using VeradeAddin.Services;

namespace VeradeAddin.UI
{
    /// <summary>WinForms implementation of <see cref="IDialogService"/>.</summary>
    public sealed class WinFormsDialogService : IDialogService
    {
        public ComponentNode ShowComponentTree(ComponentNode root, string title)
        {
            using (var dialog = new ComponentTreeDialog(root, title))
            {
                return dialog.ShowDialog() == DialogResult.OK ? dialog.SelectedNode : null;
            }
        }

        public void ShowMessage(string title, string message)
        {
            MessageBox.Show(message, title, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        public bool Confirm(string title, string message)
        {
            return MessageBox.Show(message, title, MessageBoxButtons.YesNo, MessageBoxIcon.Warning)
                   == DialogResult.Yes;
        }

        public IDisposable ShowWait(string title, string message)
        {
            return new WaitForm(title, message);
        }

        public ScreenshotAction ShowScreenshot(string title, string imagePath, out string savedPath)
        {
            using (var dialog = new ScreenshotDialog(title, imagePath))
            {
                dialog.ShowDialog();
                savedPath = dialog.SavedPath;
                return dialog.Action;
            }
        }

        public ExportRequest ShowExportOptions(DrawingExportInfo info)
        {
            using (var dialog = new ExportOptionsDialog(info))
            {
                if (dialog.ShowDialog() != DialogResult.OK)
                {
                    return null;
                }
                return new ExportRequest { Pdf = dialog.Pdf, Dwg = dialog.Dwg, Step = dialog.Step };
            }
        }

        public bool ShowAffixPrompt(string baseName, out string prefix, out string suffix)
        {
            prefix = string.Empty;
            suffix = string.Empty;
            using (var dialog = new AffixDialog(baseName))
            {
                if (dialog.ShowDialog() != DialogResult.OK)
                {
                    return false;
                }
                prefix = dialog.PrefixText;
                suffix = dialog.SuffixText;
                return true;
            }
        }

        public EdgeColorRequest ShowEdgeColoring(EdgeColoringPlan plan)
        {
            using (var dialog = new EdgeColoringDialog(plan))
            {
                return dialog.ShowDialog() == DialogResult.OK ? dialog.Request : null;
            }
        }

        public string ChooseFromList(string title, string prompt, IReadOnlyList<string> options)
        {
            using (var dialog = new ListChoiceDialog(title, prompt, options))
            {
                return dialog.ShowDialog() == DialogResult.OK ? dialog.SelectedItem : null;
            }
        }

        public PartConfiguratorSelection ShowPartConfigurator()
        {
            using (var dialog = new PartConfiguratorDialog())
            {
                return dialog.ShowDialog() == DialogResult.OK ? dialog.Selection : null;
            }
        }
    }
}
