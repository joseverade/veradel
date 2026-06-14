using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;
using VeradeAddin.Models;

namespace VeradeAddin.UI
{
    /// <summary>
    /// Diálogo modal que muestra la jerarquía de la selección de un ensamblaje en un
    /// TreeView. Vista pura: mapea <see cref="ComponentNode"/> a nodos y devuelve el
    /// nodo elegido. Sin lógica de negocio.
    /// </summary>
    internal sealed class ComponentTreeDialog : Form
    {
        private const int IconAssembly = 0;
        private const int IconComponent = 1;

        private readonly TreeView _tree;
        private readonly Button _openButton;
        private readonly Button _cancelButton;
        private readonly Label _pathLabel;

        public ComponentNode SelectedNode { get; private set; }

        public ComponentTreeDialog(ComponentNode root, string title)
        {
            Text = title;
            Font = new Font("Segoe UI", 9f);
            ClientSize = new Size(520, 460);
            MinimumSize = new Size(420, 320);
            StartPosition = FormStartPosition.CenterScreen;
            MinimizeBox = false;
            MaximizeBox = false;
            FormBorderStyle = FormBorderStyle.Sizable;
            ShowInTaskbar = false;
            TopMost = true; // por encima de la ventana principal de SolidWorks

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                Padding = new Padding(12)
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // instrucción
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f)); // árbol
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // ruta
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // botones

            var header = new Label
            {
                AutoSize = false,
                Dock = DockStyle.Fill,
                Height = 36,
                Text = "Seleccione un componente del árbol y pulse Abrir para ir a su carpeta.",
                TextAlign = ContentAlignment.MiddleLeft
            };

            _tree = new TreeView
            {
                Dock = DockStyle.Fill,
                HideSelection = false,
                FullRowSelect = true,
                ShowNodeToolTips = true,
                ItemHeight = 24,
                BorderStyle = BorderStyle.FixedSingle,
                ImageList = BuildImageList()
            };
            _tree.AfterSelect += (s, e) => OnSelectionChanged();
            _tree.NodeMouseDoubleClick += (s, e) => TryAccept();

            _pathLabel = new Label
            {
                AutoSize = false,
                Dock = DockStyle.Fill,
                Height = 40,
                ForeColor = Color.DimGray,
                Text = string.Empty,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(0, 6, 0, 6)
            };

            var buttonPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                AutoSize = true,
                Height = 40
            };
            _cancelButton = new Button { Text = "Cancelar", DialogResult = DialogResult.Cancel, Width = 100, Height = 30 };
            _openButton = new Button { Text = "Abrir", Width = 100, Height = 30, Enabled = false };
            _openButton.Click += (s, e) => TryAccept();
            buttonPanel.Controls.Add(_cancelButton);
            buttonPanel.Controls.Add(_openButton);

            layout.Controls.Add(header, 0, 0);
            layout.Controls.Add(_tree, 0, 1);
            layout.Controls.Add(_pathLabel, 0, 2);
            layout.Controls.Add(buttonPanel, 0, 3);
            Controls.Add(layout);

            AcceptButton = _openButton;
            CancelButton = _cancelButton;

            BuildTree(root);
        }

        private static ImageList BuildImageList()
        {
            var list = new ImageList { ImageSize = new Size(16, 16), ColorDepth = ColorDepth.Depth32Bit };
            list.Images.Add(MakeAssemblyIcon());  // index 0
            list.Images.Add(MakeComponentIcon());  // index 1
            return list;
        }

        private void BuildTree(ComponentNode root)
        {
            var rootTreeNode = CreateTreeNode(root);
            _tree.Nodes.Add(rootTreeNode);
            AddChildren(rootTreeNode, root);
            _tree.ExpandAll();

            var target = FindSelectedTarget(rootTreeNode);
            _tree.SelectedNode = target ?? rootTreeNode;
            OnSelectionChanged();
        }

        private static void AddChildren(TreeNode parentTreeNode, ComponentNode parent)
        {
            foreach (var child in parent.Children)
            {
                var childTreeNode = CreateTreeNode(child);
                parentTreeNode.Nodes.Add(childTreeNode);
                AddChildren(childTreeNode, child);
            }
        }

        private static TreeNode CreateTreeNode(ComponentNode node)
        {
            string label = DisplayName(node);
            if (node.IsVirtual) label += "  [virtual]";
            else if (node.IsSuppressed) label += "  [suprimido]";
            else if (node.IsLightweight) label += "  [ligero]";
            else if (!node.FileExists && !string.IsNullOrWhiteSpace(node.FilePath)) label += "  [archivo no encontrado]";

            int icon = node.Children.Count > 0 ? IconAssembly : IconComponent;

            var tn = new TreeNode(label)
            {
                Tag = node,
                ImageIndex = icon,
                SelectedImageIndex = icon,
                ToolTipText = string.IsNullOrWhiteSpace(node.FilePath) ? "(sin ruta)" : node.FilePath
            };

            if (node.IsSelectedTarget)
            {
                tn.NodeFont = new Font(SystemFonts.DefaultFont, FontStyle.Bold);
            }
            if (!node.CanOpen)
            {
                tn.ForeColor = Color.Gray;
            }
            return tn;
        }

        /// <summary>
        /// Nombre a mostrar: nombre de archivo sin extensión a partir de la ruta. Si no hay
        /// ruta (componente virtual / sin guardar), se mantiene el nombre de instancia.
        /// </summary>
        private static string DisplayName(ComponentNode node)
        {
            if (!string.IsNullOrWhiteSpace(node.FilePath))
            {
                string name = Path.GetFileNameWithoutExtension(node.FilePath);
                if (!string.IsNullOrWhiteSpace(name)) return name;
            }
            return node.Name;
        }

        private static TreeNode FindSelectedTarget(TreeNode node)
        {
            var model = node.Tag as ComponentNode;
            if (model != null && model.IsSelectedTarget) return node;
            foreach (TreeNode child in node.Nodes)
            {
                var found = FindSelectedTarget(child);
                if (found != null) return found;
            }
            return null;
        }

        private void OnSelectionChanged()
        {
            var node = _tree.SelectedNode?.Tag as ComponentNode;
            _openButton.Enabled = node != null && node.CanOpen;

            if (node == null)
            {
                _pathLabel.Text = string.Empty;
            }
            else if (node.IsVirtual)
            {
                _pathLabel.Text = "Componente virtual — no tiene archivo en disco.";
            }
            else if (string.IsNullOrWhiteSpace(node.FilePath))
            {
                _pathLabel.Text = "Sin ruta de archivo.";
            }
            else
            {
                _pathLabel.Text = (node.FileExists ? "Ruta: " : "Archivo no encontrado: ") + node.FilePath;
            }
        }

        private void TryAccept()
        {
            var node = _tree.SelectedNode?.Tag as ComponentNode;
            if (node == null || !node.CanOpen) return;
            SelectedNode = node;
            DialogResult = DialogResult.OK;
            Close();
        }

        private static Image MakeAssemblyIcon()
        {
            var bmp = new Bitmap(16, 16);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                // dos cajas apiladas = ensamblaje
                using (var back = new SolidBrush(Color.FromArgb(120, 170, 220)))
                using (var front = new SolidBrush(Color.FromArgb(45, 108, 223)))
                using (var pen = new Pen(Color.FromArgb(30, 70, 150)))
                {
                    g.FillRectangle(back, 2, 2, 9, 9);
                    g.DrawRectangle(pen, 2, 2, 9, 9);
                    g.FillRectangle(front, 5, 5, 9, 9);
                    g.DrawRectangle(pen, 5, 5, 9, 9);
                }
            }
            return bmp;
        }

        private static Image MakeComponentIcon()
        {
            var bmp = new Bitmap(16, 16);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                using (var fill = new SolidBrush(Color.FromArgb(150, 150, 150)))
                using (var pen = new Pen(Color.FromArgb(90, 90, 90)))
                {
                    g.FillRectangle(fill, 3, 3, 10, 10);
                    g.DrawRectangle(pen, 3, 3, 10, 10);
                }
            }
            return bmp;
        }
    }
}
