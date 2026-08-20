
using System;

using Godot;

namespace ExportMenus
{
    [Tool]
    public partial class TestMenu : Menu
    {
        public TestMenu() : base()
        {
            AddPanel(0f, 0f, 40f, 40f);
            SetTheme($"res://new_theme.tres");

            AddRichText("", 0f, 0f, 40f, 40f);

            AddTextureRect("", 0f, 0f, 40f, 40f);
        }
    }
}
