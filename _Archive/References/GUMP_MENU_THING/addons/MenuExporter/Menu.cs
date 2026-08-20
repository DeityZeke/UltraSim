
using System;
using System.Collections.Generic;
using System.Linq;

using Godot;

namespace ExportMenus
{
    [Tool]
    [GlobalClass]
    public partial class Menu : Control
    {
        private Dictionary<int, List<Control>> parenting = new Dictionary<int, List<Control>>();

        public Menu()
        {
            parenting = new Dictionary<int, List<Control>>();

            if (Engine.IsEditorHint() && this.GetType() == typeof(Menu) && GetChildCount() > 0)
            {
                // Don't show multiple dialogs if one already exists
                if (GetNodeOrNull<ConfirmationDialog>("MenuChildDialog") == null)
                    ShowChildrenDialog();
            }

            if (this.GetType().IsSubclassOf(typeof(Menu)))
            {
                this.Name = this.GetType().Name;

                foreach (var child in GetChildren()) //When Editor Reloads, Erase Nodes to prevent Duplication
                    (child as Node)?.QueueFree();
            }
            else if (this is Control && this.Name != "Control")
                this.Name = "Control";
        }
        private void ShowChildrenDialog()
        {
            var dialog = new ConfirmationDialog();
            dialog.Name = "Clear Children Notice";
            dialog.DialogText = "The scene tree already contains children. Would you like to keep them?";
            dialog.MinSize = new Vector2I(400, 100);

            // Hide default OK/Cancel buttons
            dialog.GetOkButton().Hide();
            dialog.GetCancelButton().Hide();

            var clearBtn = dialog.AddButton("Clear", true, "clear");
            var keepBtn = dialog.AddButton("Keep", true, "keep");

            dialog.CustomAction += (StringName action) =>
            {
                if (action == "clear")
                {
                    foreach (var child in GetChildren())
                        (child as Node)?.QueueFree();
                }

                dialog.QueueFree();
            };

            // Add dialog to the root
            GetTree().Root.AddChild(dialog);
            dialog.PopupCentered();
        }


        protected void AddPanel(float x, float y, float width, float height)
        {
            AddPanel(x, y, width, height, 1);
        }
        protected void AddPanel(float x, float y, float width, float height, int depth)
        {
            var panel = new Panel
            {
                Position = new Vector2(x, y),
                Size = new Vector2(width, height),
                Name = $"Panel ({x},{y},{width},{height})",
            };

            AttachNode(panel, depth);
        }


        protected void AddLabel(string text, float x, float y, Color color)
        {
            AddLabel(text, x, y, color, 1);
        }
        public void AddLabel(string text, float x, float y, Color color, int depth)
        {
            var label = new Label
            {
                Text = text,
                Position = new Vector2(x, y),
                SelfModulate = color,
                Name = $"Label ({x},{y}, {color.ToString()})",
            };

            AttachNode(label, depth);
        }


        protected void AddRichText(string text, float x, float y, float width, float height)
        {
            AddRichText(text, x, y, width, height, 1);
        }
        protected void AddRichText(string text, float x, float y, float width, float height, int depth)
        {
            var richText = new RichTextLabel
            {
                Text = text,
                Position = new Vector2(x, y),
                Size = new Vector2(width, height),
                Name = $"RichText ({x},{y},{width},{height})",
            };

            AttachNode(richText, depth);
        }


        protected void AddButton(string text, float x, float y, float width, float height, Action onPressed)
        {
            AddButton(text, x, y, width, height, onPressed, 1);
        }
        protected void AddButton(string text, float x, float y, float width, float height, Action onPressed, int depth)
        {
            var button = new Button
            {
                Text = text,
                Position = new Vector2(x, y),
                Size = new Vector2(width, height),
                Name = $"Button ({x},{y},{width},{height})",
            };

            if (onPressed != null)
                button.Pressed += onPressed;

            AttachNode(button, depth);
        }


        protected void AddTextureRect(string path, float x, float y, float width, float height)
        {
            AddTextureRect(path, x, y, width, height, 1);
        }
        protected void AddTextureRect(string path, float x, float y, float width, float height, int depth)
        {
            Texture2D texture = GD.Load<Texture2D>(path);

            var rect = new TextureRect
            {
                Texture = texture,
                Position = new Vector2(x, y),
                Size = new Vector2(width, height),
                Name = $"TextureRect ({x},{y},{width},{height}",
            };

            AttachNode(rect, depth);
        }


        private Control lastAdded;

        protected void SetAnchors(float left, float right, float top, float bottom)
        {
            if (lastAdded == null)
                return;

            lastAdded.SetAnchor(Side.Left, left, false, true);
            lastAdded.SetAnchor(Side.Right, right, false, true);
            lastAdded.SetAnchor(Side.Top, top, false, true);
            lastAdded.SetAnchor(Side.Bottom, bottom, false, true);
        }

        protected void SetAnchors(LayoutPreset layout)
        {
            if (lastAdded == null)
                return;

            lastAdded.SetAnchorsPreset(layout);
        }

        protected void SetTheme(string path)
        {
            Theme theme = GD.Load<Theme>(path);

            if (theme != null)
                lastAdded.Theme = theme;
        }


        private void AttachNode(Control node, int depth)
        {
            if (depth <= 1)
                depth = 1;

            if (!parenting.ContainsKey(depth))
                parenting[depth] = new List<Control>();

            int parent = depth - 1;

            if (parent > 0)
                parenting[parent].Last().AddChild(node);
            else
                AddChild(node);

            lastAdded = node; // Used for Anchors
            parenting[depth].Add(node); // Save Current Node at Specified Tree Depth
            node.Owner = this;
        }


        // (Optional) Serialization methods, override in derived classes as needed

        //public virtual void Serialize(Common.BinaryWriter writer) { }
        public virtual void Serialize()
        {
        }

        //public virtual void Deserialize(Common.BinaryReader reader) { }
        public virtual void Deserialize()
        {
        }
    }
}