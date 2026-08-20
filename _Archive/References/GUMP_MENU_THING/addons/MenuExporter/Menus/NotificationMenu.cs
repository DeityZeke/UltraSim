
using System;

using Godot;

namespace ExportMenus
{
    [Tool]
    public partial class NotificationMenu : Menu
    {
        public NotificationMenu() : base()
        {
            AddPanel(20, 20, 400, 180);
            AddLabel("NOTIFICATION", 30, 30, new Color("ffffffff"));
            AddRichText("DESC", 30, 60, 360, 70);
            AddButton("BUTTON", 160, 140, 100, 31, null);
        }
    }
}
