
#if TOOLS

using System;

using Godot;

using ExportMenus;

namespace Addons.MenuExporter
{

    [Tool]
    public partial class MenuExporterPlugin : EditorPlugin
    {
        private const string ExportButtonName = $"Export C# Script";
        private const string ExportAsButtonName = $"Export C# Script As...";
        private const string DefaultDirectory = $"res://Exported Menus/";


        private EditorFileDialog fileDialog;
        private string lastExportDirectory;


        public override void _EnterTree()
        {
            AddToolMenuItem(ExportButtonName, Callable.From(OnExportMenuClicked));
            AddToolMenuItem(ExportAsButtonName, Callable.From(() => { OnExportMenuClicked(true); }));

            fileDialog = new EditorFileDialog();
            fileDialog.FileMode = EditorFileDialog.FileModeEnum.SaveFile;
            fileDialog.Filters = new string[] { "*.cs ; C# Script" };
            fileDialog.Title = "Export Menu Script";
            fileDialog.FileSelected += OnSaveFile;
            fileDialog.Size = new Vector2I(1024, 768);
            AddChild(fileDialog);

            base._EnterTree();
        }


        public override void _ExitTree()
        {
            RemoveToolMenuItem(ExportButtonName);
            RemoveToolMenuItem(ExportAsButtonName);
        }


        private string savePath = $"{DefaultDirectory}MyGeneratedMenu.cs";
        private Menu menuNode;
        private string code;


        public virtual void OnExportMenuClicked()
        {
            OnExportMenuClicked(false);
        }


        public virtual void OnExportMenuClicked(bool openFileDialog)
        {
            var selectedNodes = EditorInterface.Singleton.GetSelection().GetSelectedNodes();
            var rootNode = selectedNodes[0];

            menuNode = rootNode as Menu;
            if (menuNode == null)
            {
                GD.Print("Must have a script that is a Menu or Subclass of a Menu");
                return;
            }

            var menuName = $"MyGeneratedMenu";
            savePath = $"{DefaultDirectory}{menuName}.cs";

            // The Current 'Menu' is NOT a Subclass, Assume It's 'New'
            if (!menuNode.GetType().IsSubclassOf(typeof(Menu)))
            {
                // The ControlNode has a Unique Name, Use it
                if (menuNode.Name != "Control")
                    menuName = menuNode.Name;

                openFileDialog = true;
            }
            else // The Current 'Menu' IS a Subclass, Look for Existing File and Overwrite if possible
            {
                // Get The Script Attached to the Scene Tree Node
                CSharpScript menuScript = (CSharpScript)rootNode.GetScript();

                // This should never happen unless something broke.
                // We've already determind before hand that it is a Subclass of Menu, wich is a script
                // But, Saftey first!
                if (menuScript == null)
                    return;

                // Use the current menu's node name, wich SHOULD already be the name of the menu/class
                menuName = menuNode.Name;

                // Point the save path to the existing script file
                savePath = menuScript.ResourcePath;
            }

            if (openFileDialog)
                ShowExportDialog(menuName, lastExportDirectory ?? DefaultDirectory);
            else
                OnSaveFile(savePath);
        }


        private void ShowExportDialog(string fileName, string fileDir)
        {
            // Ensure directory exists
            if (!DirAccess.DirExistsAbsolute(fileDir))
                DirAccess.MakeDirRecursiveAbsolute(fileDir);
                
            fileDialog.CurrentDir = fileDir;
            fileDialog.CurrentFile = fileName;
            fileDialog.PopupCentered();
        }


        private void OnSaveFile(string filePath)
        {
            if (menuNode == null)
            {
                GD.PrintErr("[MENU EXPORTER] Attempted to Export With Improper Node!");
                return;
            }

            savePath = filePath;

            string fileName = System.IO.Path.GetFileNameWithoutExtension(filePath);
            string className = CodeGenerator.SanitizeString(fileName);

            string directory = System.IO.Path.GetDirectoryName(savePath)?.Replace("\\", "/") ?? "";
            // Ensure res:// prefix
            if (directory.StartsWith("res:/") && !directory.StartsWith("res://"))
                directory = "res://" + directory.Substring(5);
            else if (!directory.StartsWith("res://"))
                directory = "res://" + directory.TrimStart('/');

            string sanitizedFileName = className + System.IO.Path.GetExtension(savePath);

            // Ensure directory exists
            if (!DirAccess.DirExistsAbsolute(directory))
                DirAccess.MakeDirRecursiveAbsolute(directory);

            savePath = directory.TrimEnd('/') + "/" + sanitizedFileName;
            //GD.Print("Final SavePath: " + savePath);

            code = CodeGenerator.GenerateMenu(menuNode, className);

            if (string.IsNullOrEmpty(code))
                return;

            if (FileAccess.FileExists(savePath))
            {
                var backupPath = savePath + ".bak";
                using (var fileBackup = FileAccess.Open(backupPath, FileAccess.ModeFlags.Write))
                {
                    fileBackup?.StoreString(FileAccess.GetFileAsString(savePath));
                }
            }

            using (var file = FileAccess.Open(savePath, FileAccess.ModeFlags.Write))
            {
                if (file == null)
                {
                    GD.PrintErr($"[MENU EXPORTER] Could not open file for writing: {savePath}");
                    return;
                }
                file.StoreString(code);
            }

            lastExportDirectory = savePath;
            code = "";

            EditorInterface.Singleton.GetResourceFilesystem().Scan();

            AttachGeneratedScript();
        }


        private void AttachGeneratedScript()
        {
            //Attach the newly generated script to the rootnode:
            var newScript = GD.Load<Script>(savePath);

            if (newScript == null)
            {
                GD.Print($"Failed to load script: {savePath}");
                return;
            }

            try { menuNode.SetDeferred("script", newScript); }
            catch (Exception ex) { GD.Print($" Exception Attaching Script: {ex}"); }

            savePath = "";
            menuNode = null;
        }
    }
}

#endif