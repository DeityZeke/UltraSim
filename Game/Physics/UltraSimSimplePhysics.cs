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

/// <summary>One all-or-nothing swept request from Game locomotion.</summary>
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

/// <summary>Physical outcome supported by the Simple swept resolver.</summary>
public enum SimpleMotionStatus : byte
{
    None = 0,
    Resolved,
    Blocked,
    OutsidePreparedWorld,
}

/// <summary>One realized target or a fail-closed reason for no movement.</summary>
public readonly record struct SimpleMotionResult(
    SimpleMotionStatus Status,
    WorldPosition Position)
{
    public bool WasResolved => Status == SimpleMotionStatus.Resolved;
}

/// <summary>
/// Generation-owned authoritative motion/occupancy resolver for the accepted physical behavior.
/// It understands terrain and already-projected physical bounds, never Game prefab/state meaning.
/// </summary>
/// <remarks>
/// P0.1b performs one exact linear swept-AABB blocker test and remains all-or-nothing. It does not
/// substep, slide, depenetrate, step, resolve continuous support, or provide a provider abstraction.
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
    /// Resolves prepared terrain at the target and tests the complete linear body sweep against
    /// prepared static geometry and one complete Game-owned dynamic blocking projection.
    /// </summary>
    public SimpleMotionResult ResolveMotion(
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

        if (!TryResolveTerrain(target, out WorldPosition resolved))
            return new SimpleMotionResult(SimpleMotionStatus.OutsidePreparedWorld, default);

        WorldStaticBounds currentBounds = Bounds(request.Current, request.Body);
        WorldStaticBounds targetBounds = Bounds(resolved, request.Body);
        return IsSweepClear(
            currentBounds,
            targetBounds,
            dynamicBlockingBounds,
            dynamicProjectionComplete)
            ? new SimpleMotionResult(SimpleMotionStatus.Resolved, resolved)
            : new SimpleMotionResult(SimpleMotionStatus.Blocked, default);
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

        if (!TryResolveTerrain(request.Target, out WorldPosition resolved))
        {
            return new SimpleOccupancyResult(
                SimpleOccupancyStatus.OutsidePreparedWorld, default);
        }

        WorldStaticBounds bodyBounds = Bounds(resolved, request.Body);
        return IsSweepClear(
            bodyBounds,
            bodyBounds,
            dynamicBlockingBounds,
            dynamicProjectionComplete)
            ? new SimpleOccupancyResult(SimpleOccupancyStatus.Clear, resolved)
            : new SimpleOccupancyResult(SimpleOccupancyStatus.Blocked, default);
    }

    private bool TryResolveTerrain(in WorldPosition target, out WorldPosition resolved)
    {
        if (!_world.TrySampleBaseTerrainHeight(target, out int terrainY) ||
            !WorldPosition.TryFromGlobalMillimeters(
                target.GlobalXMillimeters,
                terrainY,
                target.GlobalZMillimeters,
                out resolved))
        {
            resolved = default;
            return false;
        }
        return true;
    }

    private bool IsSweepClear(
        in WorldStaticBounds currentBounds,
        in WorldStaticBounds targetBounds,
        ReadOnlySpan<WorldStaticBounds> dynamicBlockingBounds,
        bool dynamicProjectionComplete)
    {
        WorldStaticBounds queryBounds = Union(currentBounds, targetBounds);
        if (!_world.TryQueryStaticBounds(
                queryBounds,
                StaticCollisionCandidateChunks,
                _staticHits,
                out int staticCount,
                out _,
                out _))
        {
            // Complete-or-fail query pressure is physical blockage at this seam. A partial
            // candidate set is never interpreted as a clear route.
            return false;
        }

        for (int index = 0; index < staticCount; index++)
        {
            if ((_staticHits[index].Flags & WorldStaticFlags.Collidable) != 0 &&
                SweptIntersects(currentBounds, targetBounds, _staticHits[index].Bounds))
                return false;
        }

        if (!dynamicProjectionComplete)
            return false;
        for (int index = 0; index < dynamicBlockingBounds.Length; index++)
        {
            if (!dynamicBlockingBounds[index].IsValid ||
                SweptIntersects(currentBounds, targetBounds, dynamicBlockingBounds[index]))
                return false;
        }

        return true;
    }

    private static WorldStaticBounds Union(
        in WorldStaticBounds left,
        in WorldStaticBounds right) => new(
        Math.Min(left.MinimumXMillimeters, right.MinimumXMillimeters),
        Math.Min(left.MinimumYMillimeters, right.MinimumYMillimeters),
        Math.Min(left.MinimumZMillimeters, right.MinimumZMillimeters),
        Math.Max(left.MaximumXExclusiveMillimeters, right.MaximumXExclusiveMillimeters),
        Math.Max(left.MaximumYExclusiveMillimeters, right.MaximumYExclusiveMillimeters),
        Math.Max(left.MaximumZExclusiveMillimeters, right.MaximumZExclusiveMillimeters));

    private static bool SweptIntersects(
        in WorldStaticBounds current,
        in WorldStaticBounds target,
        in WorldStaticBounds obstacle)
    {
        Int128 lowerNumerator = 0;
        Int128 lowerDenominator = 1;
        Int128 upperNumerator = 1;
        Int128 upperDenominator = 1;

        if (!ConstrainAxis(
                current.MinimumXMillimeters,
                current.MaximumXExclusiveMillimeters,
                target.MinimumXMillimeters - current.MinimumXMillimeters,
                obstacle.MinimumXMillimeters,
                obstacle.MaximumXExclusiveMillimeters,
                ref lowerNumerator,
                ref lowerDenominator,
                ref upperNumerator,
                ref upperDenominator) ||
            !ConstrainAxis(
                current.MinimumYMillimeters,
                current.MaximumYExclusiveMillimeters,
                target.MinimumYMillimeters - current.MinimumYMillimeters,
                obstacle.MinimumYMillimeters,
                obstacle.MaximumYExclusiveMillimeters,
                ref lowerNumerator,
                ref lowerDenominator,
                ref upperNumerator,
                ref upperDenominator) ||
            !ConstrainAxis(
                current.MinimumZMillimeters,
                current.MaximumZExclusiveMillimeters,
                target.MinimumZMillimeters - current.MinimumZMillimeters,
                obstacle.MinimumZMillimeters,
                obstacle.MaximumZExclusiveMillimeters,
                ref lowerNumerator,
                ref lowerDenominator,
                ref upperNumerator,
                ref upperDenominator))
        {
            return false;
        }

        return CompareFractions(
            lowerNumerator,
            lowerDenominator,
            upperNumerator,
            upperDenominator) < 0;
    }

    private static bool ConstrainAxis(
        long movingMinimum,
        long movingMaximumExclusive,
        long velocity,
        long obstacleMinimum,
        long obstacleMaximumExclusive,
        ref Int128 lowerNumerator,
        ref Int128 lowerDenominator,
        ref Int128 upperNumerator,
        ref Int128 upperDenominator)
    {
        if (velocity == 0)
        {
            return movingMinimum < obstacleMaximumExclusive &&
                movingMaximumExclusive > obstacleMinimum;
        }

        Int128 entryNumerator;
        Int128 exitNumerator;
        Int128 denominator;
        if (velocity > 0)
        {
            denominator = velocity;
            entryNumerator = (Int128)obstacleMinimum - movingMaximumExclusive;
            exitNumerator = (Int128)obstacleMaximumExclusive - movingMinimum;
        }
        else
        {
            denominator = -(Int128)velocity;
            entryNumerator = (Int128)movingMinimum - obstacleMaximumExclusive;
            exitNumerator = (Int128)movingMaximumExclusive - obstacleMinimum;
        }

        if (CompareFractions(
                entryNumerator,
                denominator,
                lowerNumerator,
                lowerDenominator) > 0)
        {
            lowerNumerator = entryNumerator;
            lowerDenominator = denominator;
        }
        if (CompareFractions(
                exitNumerator,
                denominator,
                upperNumerator,
                upperDenominator) < 0)
        {
            upperNumerator = exitNumerator;
            upperDenominator = denominator;
        }

        return CompareFractions(
            lowerNumerator,
            lowerDenominator,
            upperNumerator,
            upperDenominator) < 0;
    }

    private static int CompareFractions(
        Int128 leftNumerator,
        Int128 leftDenominator,
        Int128 rightNumerator,
        Int128 rightDenominator) =>
        (leftNumerator * rightDenominator).CompareTo(
            rightNumerator * leftDenominator);

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
