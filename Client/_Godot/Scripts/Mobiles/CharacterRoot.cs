using Godot;

namespace UltraSim.Client.Godot.Mobiles;

[GlobalClass]
public partial class CharacterRoot : Node3D
{
    [Export] public MobileDefinition? Definition { get; set; }
    [Export] public bool ApplyDefinitionOnReady { get; set; } = true;

    public Node3D VisualRoot { get; private set; } = null!;
    public Node3D CollisionRoot { get; private set; } = null!;
    public Node3D AnchorsRoot { get; private set; } = null!;
    public Node3D CameraPivot { get; private set; } = null!;
    public Node3D NameplatePivot { get; private set; } = null!;
    public Node3D EffectsRoot { get; private set; } = null!;
    public float PresentationFacingYawDegrees { get; private set; }
    public bool IsMoving { get; private set; }

    private Node? _visualInstance;

    public override void _Ready()
    {
        CacheNodes();

        if (ApplyDefinitionOnReady)
        {
            ApplyDefinition(Definition ?? CreateFallbackDefinition());
        }
    }

    public void ApplyDefinition(MobileDefinition definition)
    {
        CacheNodes();
        ClearVisual();

        Definition = definition;

        if (definition.VisualScene is not null)
        {
            _visualInstance = definition.VisualScene.Instantiate();
            VisualRoot.AddChild(_visualInstance);

            if (_visualInstance is Node3D visualNode)
            {
                visualNode.Position = definition.VisualOffset;
                visualNode.RotationDegrees = definition.VisualRotationDegrees;
                visualNode.Scale = definition.VisualScale;
            }
        }
        else
        {
            _visualInstance = CreateFallbackVisual(definition);
            VisualRoot.AddChild(_visualInstance);
        }

        CameraPivot.Position = definition.CameraPivotOffset;
        NameplatePivot.Position = definition.NameplateOffset;
        EffectsRoot.Position = definition.EffectsOffset;
    }

    public void SetPresentationFacingYawDegrees(float facingYawDegrees)
    {
        PresentationFacingYawDegrees = facingYawDegrees;
        RotationDegrees = new Vector3(0.0f, facingYawDegrees, 0.0f);
    }

    public void SetPresentationMoving(bool isMoving)
    {
        IsMoving = isMoving;
    }

    private void CacheNodes()
    {
        VisualRoot ??= GetOrCreateChild<Node3D>(this, "VisualRoot");
        CollisionRoot ??= GetOrCreateChild<Node3D>(this, "CollisionRoot");
        AnchorsRoot ??= GetOrCreateChild<Node3D>(this, "Anchors");
        CameraPivot ??= GetOrCreateChild<Node3D>(AnchorsRoot, "CameraPivot");
        NameplatePivot ??= GetOrCreateChild<Node3D>(AnchorsRoot, "NameplatePivot");
        EffectsRoot ??= GetOrCreateChild<Node3D>(AnchorsRoot, "EffectsRoot");
    }

    private void ClearVisual()
    {
        if (_visualInstance is null)
        {
            return;
        }

        _visualInstance.QueueFree();
        _visualInstance = null;
    }

    private static Node3D CreateFallbackVisual(MobileDefinition definition)
    {
        var body = new MeshInstance3D
        {
            Name = "FallbackBody",
            Mesh = new CapsuleMesh
            {
                Radius = 0.32f,
                Height = 1.45f,
                RadialSegments = 16,
                Rings = 4
            },
            Position = new Vector3(0.0f, 0.72f, 0.0f),
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = definition.FallbackBodyColor,
                Roughness = 0.75f
            }
        };

        return body;
    }

    private static MobileDefinition CreateFallbackDefinition()
    {
        return new MobileDefinition
        {
            DefinitionId = "default",
            DisplayName = "Traveler"
        };
    }

    private static TNode GetOrCreateChild<TNode>(Node parent, string name)
        where TNode : Node, new()
    {
        TNode? existing = parent.GetNodeOrNull<TNode>(name);
        if (existing is not null)
        {
            return existing;
        }

        var created = new TNode
        {
            Name = name
        };
        parent.AddChild(created);
        return created;
    }
}
