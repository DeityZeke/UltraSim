using UltraSim.Client.Core;
using UltraSim.Game.World;
using UltraSim.Spatial;

namespace UltraSim.Client.Godot;

/// <summary>
/// Thin engine-thread bridge between the engine-neutral <see cref="ClientPresentationWindow"/>
/// and Godot's native presentation owner. Window membership policy remains in Client Core;
/// this bridge only admits complete operation plans to the renderer and commits accepted plans.
/// </summary>
public sealed class GodotSlidingRenderWindow
{
    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
    private readonly ClientPresentationWindow _window;
    private readonly GodotWorldRenderer _renderer;
    private readonly SpatialSpaceId _space;

    public GodotSlidingRenderWindow(
        ClientWorldSemanticCache cache,
        GodotWorldRenderer renderer,
        int renderRadiusChunks)
    {
        ArgumentNullException.ThrowIfNull(cache);
        _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
        _space = cache.StructuralSpace;
        _window = new ClientPresentationWindow(cache, renderRadiusChunks);
        if (_window.MaximumActiveChunks > renderer.Limits.MaximumRenderChunks)
            throw new ArgumentOutOfRangeException(nameof(renderRadiusChunks),
                "The complete presentation window must fit within concurrent renderer capacity.");
    }

    public int RenderRadiusChunks => _window.PresentationRadiusChunks;
    public int MaximumActiveChunks => _window.MaximumActiveChunks;

    public static void GetPlayerRelativeMillimeters(
        in WorldPosition subject,
        in WorldPosition player,
        out long xMillimeters,
        out long zMillimeters) =>
        ClientPresentationWindow.GetRelativeMillimeters(
            subject, player, out xMillimeters, out zMillimeters);

    public bool TryTrackPlayer(
        in WorldPosition player,
        Span<ClientPresentationChange> changes,
        out ClientPresentationWindowTransition transition,
        out int required,
        out ClientPresentationWindowError error,
        out GodotWorldRenderQueueError rendererError)
    {
        return TryTrackPlayer(
            player,
            player.ToChunkKey(_space),
            changes,
            out transition,
            out required,
            out error,
            out rendererError);
    }

    /// <summary>
    /// Tracks the continuous player origin while allowing finite-world composition to clamp the
    /// complete structural presentation window to a different center chunk.
    /// </summary>
    public bool TryTrackPlayer(
        in WorldPosition player,
        SpatialChunkKey presentationCenter,
        Span<ClientPresentationChange> changes,
        out ClientPresentationWindowTransition transition,
        out int required,
        out ClientPresentationWindowError error,
        out GodotWorldRenderQueueError rendererError)
    {
        EnsureOwner();
        if (!_window.TryPlanTrack(
                player, presentationCenter, changes, out ClientPresentationWindowPlan plan,
                out transition, out required, out error))
        {
            rendererError = GodotWorldRenderQueueError.None;
            return false;
        }

        if (required != 0 &&
            !_renderer.TryQueueWindowTransition(
                changes[..required], hideExits: false, out rendererError))
        {
            _window.Cancel(plan);
            error = ClientPresentationWindowError.BackendRejected;
            return false;
        }

        _window.Commit(plan);
        _renderer.SetRenderOriginFromWindow(new WorldRenderOrigin(
            player.Chunk,
            player.YMillimeters,
            player.Local.XMillimeters,
            player.Local.ZMillimeters));
        rendererError = GodotWorldRenderQueueError.None;
        error = ClientPresentationWindowError.None;
        return true;
    }

    public bool TryResetForJump(
        in WorldPosition player,
        Span<ClientPresentationChange> changes,
        out ClientPresentationWindowTransition transition,
        out int required,
        out ClientPresentationWindowError error,
        out GodotWorldRenderQueueError rendererError)
    {
        EnsureOwner();
        if (!_window.TryPlanReset(
                player, changes, out ClientPresentationWindowPlan plan,
                out transition, out required, out error))
        {
            rendererError = GodotWorldRenderQueueError.None;
            return false;
        }

        if (required != 0 &&
            !_renderer.TryQueueWindowTransition(
                changes[..required], hideExits: true, out rendererError))
        {
            _window.Cancel(plan);
            error = ClientPresentationWindowError.BackendRejected;
            return false;
        }

        _window.Commit(plan);
        _renderer.SetRenderOriginFromWindow(new WorldRenderOrigin(
            player.Chunk,
            player.YMillimeters,
            player.Local.XMillimeters,
            player.Local.ZMillimeters), rebasePendingRetirements: false);
        rendererError = GodotWorldRenderQueueError.None;
        error = ClientPresentationWindowError.None;
        return true;
    }

    public bool TryCopyBindings(
        Span<ClientPresentationBinding> destination,
        out int count,
        out int required)
    {
        EnsureOwner();
        return _window.TryCopyBindings(destination, out count, out required);
    }

    public ClientPresentationWindowSnapshot GetSnapshot()
    {
        EnsureOwner();
        return _window.GetSnapshot();
    }

    private void EnsureOwner()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("The Godot presentation bridge is engine-thread bound.");
    }
}
