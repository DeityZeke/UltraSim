using System.Collections.Generic;
using Godot;
using UltraSim.Client.Godot.Controllers;
using UltraSim.Client.Godot.Mobiles;

namespace UltraSim.Client.Godot.World;

[GlobalClass]
public partial class ClientCharacterWorld : Node3D
{
    [Export] public PackedScene? CharacterRootScene { get; set; }
    [Export] public MobileDefinition? DefaultMobileDefinition { get; set; }
    [Export] public NodePath? MobileCatalogNodePath { get; set; }
    [Export] public NodePath? LocalPlayerControllerPath { get; set; }
    [Export] public ulong DefaultLocalControlledEntityId { get; set; } = 1;
    [Export] public bool SpawnPreviewActorOnReady { get; set; } = true;
    [Export] public Vector3 PreviewSpawnPosition { get; set; } = Vector3.Zero;

    private readonly Dictionary<ulong, CharacterRoot> _characters = new();
    private LocalPlayerController? _localPlayerController;
    private MobileCatalogNode? _mobileCatalogNode;

    public ulong LocalControlledEntityId { get; private set; }

    public override void _Ready()
    {
        _localPlayerController = ResolveNode<LocalPlayerController>(LocalPlayerControllerPath);
        _mobileCatalogNode = ResolveNode<MobileCatalogNode>(MobileCatalogNodePath);

        if (SpawnPreviewActorOnReady)
        {
            SpawnPreviewActor();
        }
    }

    public CharacterRoot SpawnPreviewActor()
    {
        CharacterRoot actor = GetOrCreateCharacter(DefaultLocalControlledEntityId);
        actor.GlobalPosition = PreviewSpawnPosition;
        SetLocalControlledEntityId(DefaultLocalControlledEntityId);
        return actor;
    }

    public CharacterRoot GetOrCreateCharacter(ulong entityId, string definitionId = "default")
    {
        if (_characters.TryGetValue(entityId, out CharacterRoot? existing))
        {
            return existing;
        }

        CharacterRoot character = InstantiateCharacterRoot();
        character.Name = $"Character_{entityId}";
        AddChild(character);
        _characters.Add(entityId, character);

        MobileDefinition? definition = _mobileCatalogNode?.Resolve(definitionId) ?? DefaultMobileDefinition;
        if (definition is not null)
        {
            character.ApplyDefinition(definition);
        }

        return character;
    }

    public bool TryGetCharacter(ulong entityId, out CharacterRoot? character)
    {
        return _characters.TryGetValue(entityId, out character);
    }

    public void SetActorPose(ulong entityId, Vector3 position, float yawDegrees)
    {
        CharacterRoot character = GetOrCreateCharacter(entityId);
        character.GlobalPosition = position;
        character.SetPresentationFacingYawDegrees(yawDegrees);
    }

    public void SetActorDefinition(ulong entityId, string definitionId)
    {
        CharacterRoot character = GetOrCreateCharacter(entityId, definitionId);
        MobileDefinition? definition = _mobileCatalogNode?.Resolve(definitionId) ?? DefaultMobileDefinition;
        if (definition is not null)
        {
            character.ApplyDefinition(definition);
        }
    }

    public void SetLocalControlledEntityId(ulong entityId)
    {
        LocalControlledEntityId = entityId;

        if (_localPlayerController is null)
        {
            return;
        }

        if (entityId == 0 || !_characters.TryGetValue(entityId, out CharacterRoot? character))
        {
            _localPlayerController.SetControlledCharacter(null);
            return;
        }

        _localPlayerController.SetControlledCharacter(character);
    }

    public void DespawnActor(ulong entityId)
    {
        if (!_characters.Remove(entityId, out CharacterRoot? character))
        {
            return;
        }

        if (_localPlayerController?.ControlledCharacter == character)
        {
            _localPlayerController.SetControlledCharacter(null);
        }

        character.QueueFree();
    }

    public void ClearAllActors()
    {
        foreach (CharacterRoot character in _characters.Values)
        {
            character.QueueFree();
        }

        _characters.Clear();
        LocalControlledEntityId = 0;
        _localPlayerController?.SetControlledCharacter(null);
    }

    private CharacterRoot InstantiateCharacterRoot()
    {
        if (CharacterRootScene?.Instantiate() is CharacterRoot character)
        {
            return character;
        }

        return new CharacterRoot
        {
            Name = "CharacterRoot"
        };
    }

    private TNode? ResolveNode<TNode>(NodePath? path)
        where TNode : Node
    {
        return path is null || path.IsEmpty
            ? null
            : GetNodeOrNull<TNode>(path);
    }
}
