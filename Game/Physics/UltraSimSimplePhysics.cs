using System.Runtime.CompilerServices;
using UltraSim.Game.World;

namespace UltraSim.Game.Physics;

/// <summary>Fixed upright endpoint-occupancy body used by the first Simple Physics seam.</summary>
public readonly record struct SimplePhysicsBody(
    int HalfExtentXMillimeters,
    int HalfExtentZMillimeters,
    int HeightMillimeters)
{
    public bool IsValid =>
        HalfExtentXMillimeters > 0 &&
        HalfExtentZMillimeters > 0 &&
        HeightMillimeters > 0;
}

/// <summary>One all-or-nothing endpoint request from Game locomotion.</summary>
public readonly record struct SimpleMotionRequest(
    WorldPosition Current,
    long TargetXMillimeters,
    long TargetZMillimeters,
    SimplePhysicsBody Body)
{
    public bool IsValid => Body.IsValid;
}

/// <summary>One first-class physical legality query for an upright body at a target location.</summary>
public readonly record struct SimpleOccupancyRequest(
    WorldPosition Target,
    SimplePhysicsBody Body)
{
    public bool IsValid => Body.IsValid;
}

/// <summary>Physical endpoint legality supported by the P0.1a occupancy seam.</summary>
public enum SimpleOccupancyStatus : byte
{
    None = 0,
    Clear,
    Blocked,
    OutsidePreparedWorld,
}

/// <summary>A terrain-resolved legal position or a fail-closed occupancy result.</summary>
public readonly record struct SimpleOccupancyResult(
    SimpleOccupancyStatus Status,
    WorldPosition Position)
{
    public bool CanOccupy => Status == SimpleOccupancyStatus.Clear;
}

/// <summary>Physical outcome supported by the extracted P0.0 endpoint resolver.</summary>
public enum SimpleMotionStatus : byte
{
    None = 0,
    Resolved,
    Blocked,
    OutsidePreparedWorld,
}

/// <summary>One realized endpoint or a fail-closed reason for no movement.</summary>
public readonly record struct SimpleMotionResult(
    SimpleMotionStatus Status,
    WorldPosition Position)
{
    public bool WasResolved => Status == SimpleMotionStatus.Resolved;
}

/// <summary>
/// Generation-owned authoritative endpoint resolver for the accepted T0.10 physical behavior.
/// It understands terrain and already-projected physical bounds, never Game prefab/state meaning.
/// </summary>
/// <remarks>
/// P0.0 deliberately performs endpoint occupancy only. It does not sweep, substep, slide,
/// depenetrate, step, or provide a general rigid-body/provider abstraction.
/// </remarks>
public sealed class UltraSimSimplePhysics
{
    public const int StaticCollisionCandidateChunks = 16;
    public const int StaticCollisionHitCapacity = 64;

    private readonly GameWorldState _world;
    private readonly WorldStaticQueryHit[] _staticHits =
        new WorldStaticQueryHit[StaticCollisionHitCapacity];

    public UltraSimSimplePhysics(GameWorldState world) =>
        _world = world ?? throw new ArgumentNullException(nameof(world));

    public long ReservedBytes =>
        (long)_staticHits.Length * Unsafe.SizeOf<WorldStaticQueryHit>();

    /// <summary>
    /// Resolves current prepared terrain and tests the resulting body endpoint against complete
    /// prepared static geometry and one complete Game-owned dynamic blocking projection.
    /// </summary>
    public SimpleMotionResult ResolveEndpoint(
        in SimpleMotionRequest request,
        ReadOnlySpan<WorldStaticBounds> dynamicBlockingBounds,
        bool dynamicProjectionComplete)
    {
        if (!request.IsValid)
            throw new ArgumentException("A valid Simple Physics motion request is required.", nameof(request));

        if (!WorldPosition.TryFromGlobalMillimeters(
                request.TargetXMillimeters,
                request.Current.YMillimeters,
                request.TargetZMillimeters,
                out WorldPosition target))
        {
            return new SimpleMotionResult(SimpleMotionStatus.OutsidePreparedWorld, default);
        }

        SimpleOccupancyResult occupancy = ProbeOccupancy(
            new SimpleOccupancyRequest(target, request.Body),
            dynamicBlockingBounds,
            dynamicProjectionComplete);
        return occupancy.Status switch
        {
            SimpleOccupancyStatus.Clear =>
                new SimpleMotionResult(SimpleMotionStatus.Resolved, occupancy.Position),
            SimpleOccupancyStatus.Blocked =>
                new SimpleMotionResult(SimpleMotionStatus.Blocked, default),
            SimpleOccupancyStatus.OutsidePreparedWorld =>
                new SimpleMotionResult(SimpleMotionStatus.OutsidePreparedWorld, default),
            _ => throw new InvalidOperationException("Simple Physics produced no occupancy result."),
        };
    }

    /// <summary>
    /// Resolves prepared terrain at the requested X/Z and tests whether the complete supplied body
    /// may legally occupy that endpoint. This is a query only; it owns no Game mutation or policy.
    /// </summary>
    public SimpleOccupancyResult ProbeOccupancy(
        in SimpleOccupancyRequest request,
        ReadOnlySpan<WorldStaticBounds> dynamicBlockingBounds,
        bool dynamicProjectionComplete)
    {
        if (!request.IsValid)
            throw new ArgumentException(
                "A valid Simple Physics occupancy request is required.", nameof(request));

        if (!_world.TrySampleBaseTerrainHeight(request.Target, out int terrainY) ||
            !WorldPosition.TryFromGlobalMillimeters(
                request.Target.GlobalXMillimeters,
                terrainY,
                request.Target.GlobalZMillimeters,
                out WorldPosition resolved))
        {
            return new SimpleOccupancyResult(
                SimpleOccupancyStatus.OutsidePreparedWorld, default);
        }

        WorldStaticBounds bodyBounds = Bounds(resolved, request.Body);
        if (!_world.TryQueryStaticBounds(
                bodyBounds,
                StaticCollisionCandidateChunks,
                _staticHits,
                out int staticCount,
                out _,
                out _))
        {
            // Complete-or-fail query pressure is physical blockage at this seam. A partial
            // candidate set is never interpreted as a clear route.
            return new SimpleOccupancyResult(SimpleOccupancyStatus.Blocked, default);
        }

        for (int index = 0; index < staticCount; index++)
        {
            if ((_staticHits[index].Flags & WorldStaticFlags.Collidable) != 0)
                return new SimpleOccupancyResult(SimpleOccupancyStatus.Blocked, default);
        }

        if (!dynamicProjectionComplete)
            return new SimpleOccupancyResult(SimpleOccupancyStatus.Blocked, default);
        for (int index = 0; index < dynamicBlockingBounds.Length; index++)
        {
            if (!dynamicBlockingBounds[index].IsValid ||
                dynamicBlockingBounds[index].Intersects(bodyBounds))
                return new SimpleOccupancyResult(SimpleOccupancyStatus.Blocked, default);
        }

        return new SimpleOccupancyResult(SimpleOccupancyStatus.Clear, resolved);
    }

    private static WorldStaticBounds Bounds(
        in WorldPosition position,
        in SimplePhysicsBody body) => new(
        position.GlobalXMillimeters - body.HalfExtentXMillimeters,
        position.YMillimeters,
        position.GlobalZMillimeters - body.HalfExtentZMillimeters,
        position.GlobalXMillimeters + body.HalfExtentXMillimeters + 1L,
        position.YMillimeters + body.HeightMillimeters,
        position.GlobalZMillimeters + body.HalfExtentZMillimeters + 1L);
}
