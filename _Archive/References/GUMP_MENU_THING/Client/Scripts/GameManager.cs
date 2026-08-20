
#nullable enable

using System;
using System.Collections.Generic;

using Common.Network;

using Godot;

namespace Client
{
    public enum GameState : byte
    {
        None,
        Login,
        CharacterSelect,
        CharacterCreation,
        InGame
    }

    [Tool] // Use In Editor
    public partial class GameManager : Node
    {
        public static GameManager Instance { get; private set;}

        [Export] private PackedScene loginScreen = new();
        [Export] private PackedScene characterSelectScreen = new();
        [Export] private PackedScene characterCreationScreen = new();
        [Export] private PackedScene gameScene = new();


        [Export] private CanvasLayer mainCanvas = new();
        [Export] private CanvasLayer menuCanvas = new();
        [Export] private CanvasLayer contextMenuCanvas = new();


        private GameState m_CurrentState = GameState.None;
        private Session m_CurrentSession = null!;

        private List<Control> m_ActiveMenus = new();




        [Export] public GameState CurrentState
        {
            get => m_CurrentState;
            set
            {
                m_CurrentState = value;

                if (Engine.IsEditorHint())
                    NotifyPropertyListChanged();

                ChangeState();
            }
        }

        public override void _Ready()
        {
            Instance = this;
            ResetAll();
        }

        public virtual void ChangeState(GameState state)
        {
            if (m_CurrentState != state)
                CurrentState = state;
        }

        public virtual void ChangeState()
        {
            switch (m_CurrentState)
            {
                case GameState.None:
                    {
                        ResetAll();
                        goto case GameState.Login;
                    }
                case GameState.Login:
                    {
                        ClearAllUI();
                        PushMenu(loginScreen);
                        break;
                    }
                case GameState.CharacterSelect:
                    {
                        ClearAllUI();
                        PushMenu(characterSelectScreen);
                        break;
                    }
                case GameState.CharacterCreation:
                    {
                        ClearAllUI();
                        PushMenu(characterCreationScreen);
                        break;
                    }
                case GameState.InGame:
                    {
                        break;
                    }
            }
        }

        public virtual void ResetAll()
        {
            if (m_CurrentState != GameState.None)
                CurrentState = GameState.None;

            ClearAllUI();
        }

        // ADD LOAD/CHANGE SCENES (NOT MENUS)

        public virtual void PushMainCanvas()
        { 
            
        }

        public virtual void PopMainCanvas()
        {

        }

        public virtual void ClearMainCanvas()
        {

        }

        public virtual void PushMenu(PackedScene scene)
        {
            var toPush = scene.Instantiate();

            if (toPush is Control menu)
                PushMenu(menu);
        }

        public virtual void PushMenu(Control menu)
        {
            if (menu == null)
                return;

            //menuCanvas.AddChild(menu);
            menuCanvas.CallDeferred("add_child", menu);

            if (Engine.IsEditorHint())
                menu.Owner = menuCanvas;

            m_ActiveMenus.Add(menu);
        }

        public virtual void PopMenu(Control menu)
        {
            if (m_ActiveMenus.Remove(menu))
                menu.QueueFree();
        }

        public virtual void ClearMenus()
        {
            foreach (var menu in m_ActiveMenus.ToArray())
            {
                m_ActiveMenus.Remove(menu);
                menu.QueueFree();
            }
        }

        public virtual void PushContextMenu()
        {
        }

        public virtual void PopContextMenu()
        {

        }

        public virtual void ClearContextMenus()
        {

        }

        public virtual void ClearAllUI()
        {
            ClearContextMenus();
            ClearMenus();
            ClearMainCanvas();
        }

        public override void _ExitTree() // Stop Button In Editor?
        {
            if (m_CurrentState != GameState.None)
                CurrentState = GameState.None;

            base._ExitTree();
        }
    }
}