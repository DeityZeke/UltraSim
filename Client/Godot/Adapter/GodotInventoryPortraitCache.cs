using System.Diagnostics;
using Godot;
using UltraSim.Framework.Definitions;
using UltraSim.Game.Composition; // first-party public prefab vocabulary lives in Game.Contracts
using UltraSim.Game.World;

namespace UltraSim.Client.Godot;

/// <summary>
/// One owner-thread, disposable Inventory portrait realization. The semantic reference selects
/// already-admitted World presentation; it never names a file or grants content authorization.
/// A single staging scene renders a distinct source once, then ordinary rows borrow a cached
/// ImageTexture. The source-resolver object fences process-local presentation generations.
/// </summary>
internal sealed class GodotInventoryPortraitCache : IGodotUiSemanticImageResolver, IDisposable
{
    private const int MaximumPortraits = 32;
    private const int PortraitPixels = 128;
    // Exact first-party public mapping; no server-supplied path or dense handle is interpreted.
    private static readonly StableDefinitionId IronSwordDefinition = new("game.item.iron-sword");
    private readonly Node _parent;
    private readonly Func<IGodotWorldAssetResolver?> _world;
    private readonly int _ownerThread = System.Environment.CurrentManagedThreadId;
    private readonly Dictionary<PortraitKey, ImageTexture> _images = [];
    private readonly HashSet<PortraitKey> _unavailable = [];
    private readonly PortraitPump _pump;
    private SubViewport? _viewport;
    private ViewportTexture? _viewportTexture;
    private World3D? _world3D;
    private MeshInstance3D? _model;
    private bool _disposed;
    private ulong _builds;
    private ulong _hits;
    private long _coldBuildTicks;
    private long _warmLookupTicks;
    private PortraitKey? _pending;
    private long _pendingStarted;
    private bool _frameDrawn;

    internal event Action? PortraitReady;

    internal GodotInventoryPortraitCache(Node parent, Func<IGodotWorldAssetResolver?> world)
    {
        _parent = parent ?? throw new ArgumentNullException(nameof(parent));
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _pump = new PortraitPump { Step = CompleteDraw };
        _pump.SetProcess(false);
        _parent.AddChild(_pump);
    }

    internal (int Images, ulong Builds, ulong Hits, bool StagingScene,
        long ColdBuildTicks, long WarmLookupTicks, long ReservedPixelBytes) Snapshot
    {
        get { EnsureOwner(); return (_images.Count, _builds, _hits, _viewport is not null,
            _coldBuildTicks, _warmLookupTicks,
            (long)_images.Count * PortraitPixels * PortraitPixels * 4); }
    }

    // Strong references to the actual owner-lived wrappers for native retirement tests.
    internal (SubViewport? Viewport, ViewportTexture? Texture, World3D? World) OwnedStaging
    {
        get { EnsureOwner(); return (_viewport, _viewportTexture, _world3D); }
    }

    public Texture2D? Resolve(string assetRole) => null;

    public Texture2D? Resolve(SemanticPresentationReference reference)
    {
        EnsureOwner();
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!reference.IsValid || reference.Role != PresentationRoleId.InventoryPortrait ||
            _world() is not { } source)
            return null;
        var key = new PortraitKey(reference, source, ProfileVersion: 1);
        long started = Stopwatch.GetTimestamp();
        if (_images.TryGetValue(key, out ImageTexture? cached))
        { _hits++; _warmLookupTicks += Stopwatch.GetTimestamp() - started; return cached; }
        if (_images.Count == MaximumPortraits || _unavailable.Contains(key) ||
            DisplayServer.GetName() == "headless") return null;
        if (_pending is { } pending)
        {
            if (!ReferenceEquals(pending.Source, source)) CancelPending();
            else return null; // one finite native render at a time
        }
        if (!TryWorldMesh(source, reference, out Mesh? mesh))
        { if (_unavailable.Count < MaximumPortraits) _unavailable.Add(key); return null; }
        EnsureStagingScene();
        _model!.Mesh = mesh; // borrowed until next render-completion boundary
        _pending = key;
        _pendingStarted = started;
        _frameDrawn = false;
        RenderingServer.FramePostDraw += ObserveFrameDrawn;
        _viewport!.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
        _pump.SetProcess(true);
        return null; // the finite semantic primitive icon remains visible while preparing
    }

    private static bool TryWorldMesh(IGodotWorldAssetResolver source,
        SemanticPresentationReference reference, out Mesh? mesh)
    {
        mesh = null;
        // This is the first public Definition-to-World resolver mapping. Unknown Definitions
        // receive the semantic primitive icon, not an arbitrary prefab or filesystem lookup.
        if (reference.Domain.Value != "game" || reference.Definition != IronSwordDefinition)
            return false;
        GodotWorldMeshResolution resolved = source.ResolvePublicMesh(GameWorldPublicPrefabIds.IronSword);
        mesh = resolved.IsValid ? resolved.Mesh : null;
        return mesh is not null;
    }

    internal void CancelStaleSource()
    {
        EnsureOwner();
        if (_pending is { } pending && !ReferenceEquals(pending.Source, _world())) CancelPending();
    }

    private void ObserveFrameDrawn() => _frameDrawn = true;

    private void CompleteDraw()
    {
        if (!_frameDrawn || _pending is not { } key) return;
        EnsureOwner();
        RenderingServer.FramePostDraw -= ObserveFrameDrawn;
        _pump.SetProcess(false);
        _pending = null;
        _viewport!.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;
        try
        {
            using Image image = _viewportTexture!.GetImage();
            if (image.IsEmpty() || !HasVisiblePixels(image))
            { if (_unavailable.Count < MaximumPortraits) _unavailable.Add(key); return; }
            _images.Add(key, ImageTexture.CreateFromImage(image));
            _builds++;
            _coldBuildTicks += Stopwatch.GetTimestamp() - _pendingStarted;
        }
        finally { _model!.Mesh = null; }
        PortraitReady?.Invoke();
    }

    private static bool HasVisiblePixels(Image image)
    {
        int count = 0;
        for (int y = 0; y < image.GetHeight(); y++)
        for (int x = 0; x < image.GetWidth(); x++)
            if (image.GetPixel(x, y).A > .05f && ++count > 100) return true;
        return false;
    }

    private void CancelPending()
    {
        if (_pending is null) return;
        RenderingServer.FramePostDraw -= ObserveFrameDrawn;
        _viewport!.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;
        _model!.Mesh = null;
        _pump.SetProcess(false);
        _pending = null;
        _frameDrawn = false;
    }

    private void EnsureStagingScene()
    {
        if (_viewport is not null) return;
        var viewport = new SubViewport
        {
            Name = "InventoryPortraitStaging",
            Size = new Vector2I(PortraitPixels, PortraitPixels),
            TransparentBg = true,
            OwnWorld3D = false,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled,
        };
        var world = new World3D();
        viewport.World3D = world;
        var model = new MeshInstance3D { Name = "PortraitModel" };
        viewport.AddChild(model);
        var camera = new Camera3D
        {
            Name = "PortraitCamera",
            Projection = Camera3D.ProjectionType.Orthogonal,
            Size = 1.8f,
            Position = new Vector3(1.5f, 1.2f, 2.4f),
            Current = true,
        };
        viewport.AddChild(camera);
        var light = new DirectionalLight3D
        {
            Name = "PortraitLight",
            RotationDegrees = new Vector3(-35, -25, 0),
            LightEnergy = 1.8f,
        };
        viewport.AddChild(light);
        try
        {
            _parent.AddChild(viewport);
            camera.LookAt(new Vector3(0, 0.58f, 0));
            _viewportTexture = viewport.GetTexture(); // one owner-lived returned native wrapper
            _viewport = viewport;
            _world3D = world;
            _model = model;
        }
        catch
        {
            viewport.Free();
            world.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        EnsureOwner();
        if (_disposed) return;
        CancelPending();
        PortraitReady = null;
        foreach (ImageTexture image in _images.Values) image.Dispose();
        _images.Clear();
        _unavailable.Clear();
        _viewportTexture?.Dispose();
        _viewportTexture = null;
        _viewport?.GetParent()?.RemoveChild(_viewport);
        _viewport?.Free(); // the staging scene and its Node wrappers have one native owner
        _viewport = null;
        _model = null;
        _world3D?.Dispose();
        _world3D = null;
        _pump.GetParent()?.RemoveChild(_pump);
        _pump.Free();
        _disposed = true;
    }

    private void EnsureOwner()
    {
        if (_ownerThread != System.Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("Inventory portrait resources are Godot-thread-bound.");
    }

    private readonly record struct PortraitKey(SemanticPresentationReference Reference,
        IGodotWorldAssetResolver Source, uint ProfileVersion);
}

/// <summary>One engine-owned frame callback, active only while a portrait is pending.</summary>
internal sealed partial class PortraitPump : Node
{
    internal Action? Step;
    public override void _Process(double delta) => Step?.Invoke();
}
