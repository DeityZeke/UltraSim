
using System;

using Godot;

namespace ExportMenus
{
    [Tool]
    public partial class test_script : Menu
    {
        public test_script() : base()
        {
            AddLabel("", 959.5f, 528.5f, new Color("ffffffff"));
            SetAnchors(Control.LayoutPreset.Center);
            //SetTheme("");
            {
                AddLabel("nested label", 0f, 0f, new Color("ffffffff"), 2);
                {
                    AddLabel("further nested label", 0f, 0f, new Color("ffffffff"), 3);

                    AddRichText("", 0f, 0f, 40f, 40f, 3);
                }

                AddPanel(0f, 0f, 40f, 40f, 2);
                {
                    AddButton("", 0f, 0f, 126f, 80f, OnButtonPressed, 3);
                }
            }

            AddButton("", 0f, 0f, 8f, 8f, OnButton1Pressed);
        }

        private void OnButtonPressed()
        {
        }

        private void OnButton1Pressed()
        {
        }
    }
}