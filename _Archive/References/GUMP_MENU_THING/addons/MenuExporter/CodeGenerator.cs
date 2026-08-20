
using System;
using System.Collections.Generic;
using System.Text;

using Godot;

namespace Addons.MenuExporter
{
    public static class CodeGenerator
    {
        private static readonly string indent1 = "    ";
        private static readonly string indent2 = indent1 + "    ";

        private static StringBuilder m_Code;

        private static List<Button> m_Buttons;


        public static string GenerateMenu(Control rootNode, string className)
        {
            if (rootNode == null)
                return "";

            m_Code = new StringBuilder();
            m_Buttons = new List<Button>();


            AppendHeader(className);

            // Constructor Start        
            m_Code.AppendLine($"{indent2}public {className}() : base()");
            m_Code.AppendLine($"{indent2}{{");


            if (rootNode.GetChildren().Count > 0)
            {
                AppendChildren(rootNode, indent2);
            }


            m_Code.AppendLine($"{indent2}}}"); // Constructor End


            AppendButtonHandlers();


            m_Code.AppendLine($"{indent1}}}"); // end class
            m_Code.AppendLine("}");            // end namespace


            return m_Code.ToString();
        }

        private static void AppendHeader(string className)
        {
            m_Code.AppendLine();
            m_Code.AppendLine("using System;");
            m_Code.AppendLine();
            m_Code.AppendLine("using Godot;");
            m_Code.AppendLine();
            m_Code.AppendLine("namespace ExportMenus");
            m_Code.AppendLine("{");
            m_Code.AppendLine($"{indent1}[Tool]");
            m_Code.AppendLine($"{indent1}public partial class {className} : Menu");
            m_Code.AppendLine($"{indent1}{{");
        }

        private static int recurrsion = 0;
        private static void AppendChildren(Control node, string lastIndent)
        {
            var indent = lastIndent + "    ";
            recurrsion++;

            string depth = $", {recurrsion}"; //(recurrsion == 1) ? "" : ", " + recurrsion;
            if (recurrsion <= 1)
                depth = "";

            int currentChild = 1;
            foreach (Control child in node.GetChildren())
            {
                switch (child)
                {
                    case Label label:
                        {
                            var color = label.SelfModulate.ToHtml();
                            m_Code.AppendLine($"{indent}AddLabel(\"{EscapeString(label.Text)}\", {label.Position.X}f, {label.Position.Y}f, new Color(\"{color}\"){depth});");
                            break;
                        }
                    case RichTextLabel rich:
                        {
                            m_Code.AppendLine($"{indent}AddRichText(\"{EscapeString(rich.Text)}\", {rich.Position.X}f, {rich.Position.Y}f, {rich.Size.X}f, {rich.Size.Y}f{depth});");
                            break;
                        }
                    case Button button:
                        {
                            button.Name = SanitizeString(button.Name); // Must Be Done Because This is Used as the Delegate Pointer Name
                            m_Code.AppendLine($"{indent}AddButton(\"{EscapeString(button.Text)}\", {button.Position.X}f, {button.Position.Y}f, {button.Size.X}f, {button.Size.Y}f, On{button.Name}Pressed{depth});");
                            m_Buttons.Add(button);

                            break;
                        }
                    case Panel panel:
                        {
                            m_Code.AppendLine($"{indent}AddPanel({panel.Position.X}f, {panel.Position.Y}f, {panel.Size.X}f, {panel.Size.Y}f{depth});");
                            break;
                        }
                    case TextureRect tex:
                        {
                            m_Code.AppendLine($"{indent}AddTextureRect(\"{tex.Texture?.ResourcePath}\", {tex.Position.X}f, {tex.Position.Y}f, {tex.Size.X}f, {tex.Size.Y}f{depth});");
                            break;
                        }
                    default:
                        {
                            GD.Print($"No Definition for the Node {child.GetType()} in Menu Generator");
                            break;
                        }
                }


                AppendAnchor(child, indent);
                AppendTheme(child, indent);


                // Recursive Append
                if (child.GetChildren().Count > 0)
                {
                    m_Code.AppendLine($"{indent}{{");

                    AppendChildren(child, indent);

                    m_Code.AppendLine($"{indent}}}");
                }


                // Space Out the Generators if there are More to Add
                if (currentChild < node.GetChildren().Count)
                    m_Code.AppendLine();

                currentChild++;
            }

            recurrsion -= 1;
        }


        private static void AppendButtonHandlers()
        {
            List<Button> buttons = new List<Button>();
            for (int i = 0; i < m_Buttons.Count; i++)
            {
                bool found = false;
                for (int j = 0; j < buttons.Count; j++)
                {
                    if (m_Buttons[i].Name == buttons[j].Name)
                    {
                        found = true;
                        GD.PrintErr("[MENU EXPORTER] Attempted To Generate Buttons With Matching Delegate Methods");
                        break;
                    }
                }

                if (!found) // Do not add a second delegate method if names match
                {
                    m_Code.AppendLine();
                    m_Code.AppendLine($"{indent2}private void On{m_Buttons[i].Name}Pressed()");
                    m_Code.AppendLine($"{indent2}{{");
                    m_Code.AppendLine($"{indent2}}}");
                    buttons.Add(m_Buttons[i]);
                }
            }
        }


        private static void AppendAnchor(Control node, string indent)
        {
            if (node.AnchorsPreset > 0)
            {
                Control.LayoutPreset preset = (Control.LayoutPreset)node.AnchorsPreset;

                m_Code.AppendLine($"{indent}SetAnchors(Control.LayoutPreset.{preset.ToString()});");
            }
            else if (node.AnchorLeft != 0f || node.AnchorRight != 0f || node.AnchorTop != 0f || node.AnchorTop != 0f)
            {
                m_Code.AppendLine($"{indent}SetAnchors({node.AnchorLeft}f, {node.AnchorRight}f, {node.AnchorTop}f, {node.AnchorBottom}f);");
            }
        }

        private static void AppendTheme(Control node, string indent)
        {
            if (node.Theme == null)
                return;

            m_Code.AppendLine($"{indent}SetTheme($\"" + $"{node.Theme.ResourcePath.ToString()}\"" + $");");
        }

        private static string EscapeString(string input)
        {
            return input.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
        }

        public static string SanitizeString(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "GeneratedMenu";

            var valid = new System.Text.StringBuilder();

            // First character must be a letter or underscore
            if (!char.IsLetter(name[0]) && name[0] != '_')
                valid.Append('_');

            foreach (var ch in name)
            {
                if (char.IsLetterOrDigit(ch) || ch == '_')
                    valid.Append(ch);
                else
                    valid.Append('_'); // Replace invalid chars with underscore
            }

            return valid.ToString();
        }
    }
}