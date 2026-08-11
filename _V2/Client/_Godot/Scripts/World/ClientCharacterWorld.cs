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
    private readonly Dictionary<ulong, string> _pendingDisplayNames = new();
    private readonly Dictionary<ulong, string> _pendingDefinitionIds = new();
    private readonly List<ulong> _remoteRemovalScratch = new();
    private LocalPlayerController? _localPlayerController;
    private MobileCatalogNode? _mobileCatalogNode;

    public IReadOnlyDictionary<ulong, CharacterRoot> Characters => _characters;
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

        string effectiveDefinitionId = _pendingDefinitionIds.TryGetValue(entityId, out string? pendingDefinitionId)
            ? pendingDefinitionId
            : definitionId;
        MobileDefinition? definition = _mobileCatalogNode?.Resolve(effectiveDefinitionId) ?? DefaultMobileDefinition;
        if (definition is not null)
        {
            character.ApplyDefinition(definition);
        }

        _pendingDefinitionIds.Remove(entityId);

        if (_pendingDisplayNames.Remove(entityId, out string? displayName))
        {
            character.SetDisplayNameOverride(displayName);
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

    public void CorrectActorPose(ulong entityId, Vector3 position, float yawDegrees, float snapDistance = 1.5f, float blend = 0.18f)
    {
        CharacterRoot character = GetOrCreateCharacter(entityId);
        float distance = character.GlobalPosition.DistanceTo(position);
        character.GlobalPosition = distance > snapDistance
            ? position
            : character.GlobalPosition.Lerp(position, Mathf.Clamp(blend, 0.0f, 1.0f));
        character.SetPresentationFacingYawDegrees(yawDegrees);
    }

    public void SetActorDefinition(ulong entityId, string definitionId)
    {
        if (entityId == 0)
        {
            return;
        }

        string cleanDefinitionId = string.IsNullOrWhiteSpace(definitionId)
            ? string.Empty
            : definitionId.Trim();
        if (!_characters.TryGetValue(entityId, out CharacterRoot? character))
        {
            if (cleanDefinitionId.Length == 0)
            {
                _pendingDefinitionIds.Remove(entityId);
            }
            else
            {
                _pendingDefinitionIds[entityId] = cleanDefinitionId;
            }

            return;
        }

        MobileDefinition? definition = _mobileCatalogNode?.Resolve(cleanDefinitionId) ?? DefaultMobileDefinition;
        if (definition is not null)
        {
            character.ApplyDefinition(definition);
        }
    }

    public void SetActorDisplayName(ulong entityId, string displayName)
    {
        if (entityId == 0)
        {
            return;
        }

        string cleanDisplayName = string.IsNullOrWhiteSpace(displayName)
            ? string.Empty
            : displayName.Trim();
        if (!_characters.TryGetValue(entityId, out CharacterRoot? character))
        {
            if (cleanDisplayName.Length == 0)
            {
                _pendingDisplayNames.Remove(entityId);
            }
            else
            {
                _pendingDisplayNames[entityId] = cleanDisplayName;
            }

            return;
        }

        character.SetDisplayNameOverride(cleanDisplayName);
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
            _pendingDisplayNames.Remove(entityId);
            _pendingDefinitionIds.Remove(entityId);
            return;
        }

        _pendingDisplayNames.Remove(entityId);
        _pendingDefinitionIds.Remove(entityId);
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
        _pendingDisplayNames.Clear();
        _pendingDefinitionIds.Clear();
        LocalControlledEntityId = 0;
        _localPlayerController?.SetControlledCharacter(null);
    }

    public void ClearRemoteActors()
    {
        if (LocalControlledEntityId == 0)
        {
            ClearAllActors();
            return;
        }

        _remoteRemovalScratch.Clear();
        foreach (ulong entityId in _characters.Keys)
        {
            if (entityId != LocalControlledEntityId)
            {
                _remoteRemovalScratch.Add(entityId);
            }
        }

        for (int i = 0; i < _remoteRemovalScratch.Count; i++)
        {
            DespawnActor(_remoteRemovalScratch[i]);
        }

        _remoteRemovalScratch.Clear();
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
