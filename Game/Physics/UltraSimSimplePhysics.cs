using System.Runtime.CompilerServices;
using UltraSim.Game.World;

namespace UltraSim.Game.Physics;

/// <summary>
/// Fixed upright body plus explicit grounded traversal limits. Step policy belongs to this body
/// profile; there is no provider-global implicit step height.
/// </summary>
public readonly record struct SimplePhysicsBody(
    int HalfExtentXMillimeters,
    int HalfExtentZMillimeters,
    int HeightMillimeters,
    int MaximumStepUpMillimeters,
    int MaximumStepDownMillimeters)
{
    public bool IsValid =>
        HalfExtentXMillimeters > 0 &&
        HalfExtentZMillimeters > 0 &&
        HeightMillimeters > 0 &&
        MaximumStepUpMillimeters >= 0 &&
        MaximumStepDownMillimeters >= 0;
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

/// <summary>
/// One first-class physical legality query for an upright body at a target location. Target Y is
/// the desired elevation used to select the physically nearest support, not trusted ground truth.
/// </summary>
public readonly record struct SimpleOccupancyRequest(
    WorldPosition Target,
    SimplePhysicsBody Body)
{
    public bool IsValid => Body.IsValid;
}

/// <summary>
/// One authoritative airborne displacement. Target Y is physical body-bottom elevation; unlike
/// grounded motion it is never snapped to a nearby support merely because one exists.
/// </summary>
public readonly record struct SimpleAirborneMotionRequest(
    WorldPosition Current,
    long TargetXMillimeters,
    int TargetYMillimeters,
    long TargetZMillimeters,
    SimplePhysicsBody Body)
{
    public bool IsValid => Body.IsValid;
}

/// <summary>Support-aware physical endpoint legality supported through P0.1c.</summary>
public enum SimpleOccupancyStatus : byte
{
    None = 0,
    Clear,
    Unsupported,
    Blocked,
    OutsidePreparedWorld,
}

/// <summary>A support-resolved legal position or a fail-closed occupancy result.</summary>
public readonly record struct SimpleOccupancyResult(
    SimpleOccupancyStatus Status,
    WorldPosition Position,
    WorldTerrainColumnSurface Support = default)
{
    public bool CanOccupy =>
        Status == SimpleOccupancyStatus.Clear && Support.IsValid && Support.IsSupport;
    public bool IsGrounded => CanOccupy;
}

/// <summary>Physical outcome supported by the Simple swept resolver.</summary>
public enum SimpleMotionStatus : byte
{
    None = 0,
    Resolved,
    Unsupported,
    Blocked,
    OutsidePreparedWorld,
}

/// <summary>One realized target or a fail-closed reason for no movement.</summary>
public readonly record struct SimpleMotionResult(
    SimpleMotionStatus Status,
    WorldPosition Position,
    WorldTerrainColumnSurface Support = default)
{
    public bool WasResolved =>
        Status == SimpleMotionStatus.Resolved && Support.IsValid && Support.IsSupport;
    public bool IsGrounded => WasResolved;
}

/// <summary>Bounded terrain-contact result for one airborne displacement.</summary>
public enum SimpleAirborneMotionStatus : byte
{
    None = 0,
    Airborne,
    Landed,
    CeilingBlocked,
    Blocked,
    OutsidePreparedWorld,
}

/// <summary>
/// One realized airborne endpoint. Landed carries a Top/Base support; CeilingBlocked carries the
/// Bottom face that limited upward travel. Both contacts remain independently meaningful.
/// </summary>
public readonly record struct SimpleAirborneMotionResult(
    SimpleAirborneMotionStatus Status,
    WorldPosition Position,
    WorldTerrainColumnSurface Contact = default)
{
    public bool WasResolved => Status is SimpleAirborneMotionStatus.Airborne or
        SimpleAirborneMotionStatus.Landed or SimpleAirborneMotionStatus.CeilingBlocked;
    public bool IsGrounded => Status == SimpleAirborneMotionStatus.Landed &&
        Contact.IsValid && Contact.IsSupport;
}

/// <summary>
/// Generation-owned authoritative motion/occupancy resolver for the accepted physical behavior.
/// It understands terrain and already-projected physical bounds, never Game prefab/state meaning.
/// </summary>
/// <remarks>
/// P0.1d adds bounded Top landing and Bottom ceiling contacts for Game-owned vertical integration
/// while retaining the grounded P0.1c path and exact linear swept-AABB blocker test. It does not
/// slide, depenetrate, own gravity/jump policy, or provide a provider abstraction.
/// </remarks>
public sealed class UltraSimSimplePhysics
{
    public const int StaticCollisionCandidateChunks = 16;
    public const int StaticCollisionHitCapacity = 64;
    public const int GroundedPathSampleSpacingMillimeters =
        GameWorldSpatialProfile.TerrainCellEdgeUnits / 2;
    public const int MaximumGroundedPathSamples = 512;
    public const int TerrainColumnSurfaceCapacity =
        WorldTerrainColumnQueries.DefaultMaximumColumnSurfaceCount;

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

        TerrainResolutionStatus terrain = ResolveGroundedPath(
            request.Current,
            request.TargetXMillimeters,
            request.TargetZMillimeters,
            request.Body,
            out WorldPosition resolved,
            out WorldTerrainColumnSurface support);
        if (terrain != TerrainResolutionStatus.Grounded)
            return new SimpleMotionResult(ToMotionStatus(terrain), default);

        WorldStaticBounds currentBounds = Bounds(request.Current, request.Body);
        WorldStaticBounds targetBounds = Bounds(resolved, request.Body);
        return IsSweepClear(
            currentBounds,
            targetBounds,
            dynamicBlockingBounds,
            dynamicProjectionComplete)
            ? new SimpleMotionResult(SimpleMotionStatus.Resolved, resolved, support)
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

        TerrainResolutionStatus terrain = ResolveNearestGroundedEndpoint(
            request.Target,
            request.Body,
            out WorldPosition resolved,
            out WorldTerrainColumnSurface support);
        if (terrain != TerrainResolutionStatus.Grounded)
            return new SimpleOccupancyResult(ToOccupancyStatus(terrain), default);

        WorldStaticBounds bodyBounds = Bounds(resolved, request.Body);
        return IsSweepClear(
            bodyBounds,
            bodyBounds,
            dynamicBlockingBounds,
            dynamicProjectionComplete)
            ? new SimpleOccupancyResult(SimpleOccupancyStatus.Clear, resolved, support)
            : new SimpleOccupancyResult(SimpleOccupancyStatus.Blocked, default);
    }

    /// <summary>
    /// Resolves one combined airborne X/Y/Z displacement without support snapping. Descending
    /// bodies acquire the first complete footprint-supporting Top/Base face crossed; ascending
    /// bodies stop at the first Bottom face crossed by any footprint sample.
    /// </summary>
    public SimpleAirborneMotionResult ResolveAirborneMotion(
        in SimpleAirborneMotionRequest request,
        ReadOnlySpan<WorldStaticBounds> dynamicBlockingBounds,
        bool dynamicProjectionComplete)
    {
        if (!request.IsValid)
            throw new ArgumentException(
                "A valid Simple Physics airborne request is required.", nameof(request));
        if (_world.PreparedWorld is not WorldPreparedView world ||
            !WorldPosition.TryFromGlobalMillimeters(
                request.TargetXMillimeters,
                request.TargetYMillimeters,
                request.TargetZMillimeters,
                out WorldPosition target))
        {
            return new SimpleAirborneMotionResult(
                SimpleAirborneMotionStatus.OutsidePreparedWorld, default);
        }

        long deltaX = checked(request.TargetXMillimeters - request.Current.GlobalXMillimeters);
        long deltaZ = checked(request.TargetZMillimeters - request.Current.GlobalZMillimeters);
        long deltaY = (long)request.TargetYMillimeters - request.Current.YMillimeters;
        ulong distance = Math.Max(Math.Max(Absolute(deltaX), Absolute(deltaZ)), Absolute(deltaY));
        ulong spacing = GroundedPathSampleSpacingMillimeters;
        ulong measuredSamples = Math.Max(1UL, checked((distance + spacing - 1) / spacing));
        if (measuredSamples > MaximumGroundedPathSamples)
            return new SimpleAirborneMotionResult(SimpleAirborneMotionStatus.Blocked, default);

        int samples = checked((int)measuredSamples);
        int previousY = request.Current.YMillimeters;
        for (int sample = 1; sample <= samples; sample++)
        {
            long x = checked(request.Current.GlobalXMillimeters +
                (long)((Int128)deltaX * sample / samples));
            long z = checked(request.Current.GlobalZMillimeters +
                (long)((Int128)deltaZ * sample / samples));
            int y = checked(request.Current.YMillimeters +
                (int)((Int128)deltaY * sample / samples));
            if (!WorldPosition.TryFromGlobalMillimeters(x, y, z, out WorldPosition samplePosition))
                return new SimpleAirborneMotionResult(
                    SimpleAirborneMotionStatus.OutsidePreparedWorld, default);

            TerrainResolutionStatus structural = ValidateStructuralFootprint(
                world, samplePosition, request.Body);
            if (structural == TerrainResolutionStatus.OutsidePreparedWorld)
                return new SimpleAirborneMotionResult(
                    SimpleAirborneMotionStatus.OutsidePreparedWorld, default);
            if (structural == TerrainResolutionStatus.Blocked)
                return new SimpleAirborneMotionResult(SimpleAirborneMotionStatus.Blocked, default);

            if (y < previousY)
            {
                WorldTerrainColumnQueryStatus query = world.TryFindHighestSupportAtOrBelow(
                    samplePosition, previousY, out WorldTerrainColumnSurface support);
                if (query == WorldTerrainColumnQueryStatus.Unavailable)
                    return new SimpleAirborneMotionResult(
                        SimpleAirborneMotionStatus.OutsidePreparedWorld, default);
                if (query != WorldTerrainColumnQueryStatus.Complete)
                    return new SimpleAirborneMotionResult(SimpleAirborneMotionStatus.Blocked, default);
                if (support.IsValid && support.IsSupport && support.HeightMillimeters >= y &&
                    WorldPosition.TryFromGlobalMillimeters(
                        x, support.HeightMillimeters, z, out WorldPosition landing))
                {
                    TerrainResolutionStatus fit = ValidateFootprintAndClearance(
                        world, landing, request.Body, support);
                    if (fit == TerrainResolutionStatus.OutsidePreparedWorld)
                        return new SimpleAirborneMotionResult(
                            SimpleAirborneMotionStatus.OutsidePreparedWorld, default);
                    if (fit == TerrainResolutionStatus.Blocked)
                        return new SimpleAirborneMotionResult(SimpleAirborneMotionStatus.Blocked, default);
                    if (fit == TerrainResolutionStatus.Grounded)
                    {
                        return IsSweepClear(
                            Bounds(request.Current, request.Body),
                            Bounds(landing, request.Body),
                            dynamicBlockingBounds,
                            dynamicProjectionComplete)
                            ? new SimpleAirborneMotionResult(
                                SimpleAirborneMotionStatus.Landed, landing, support)
                            : new SimpleAirborneMotionResult(
                                SimpleAirborneMotionStatus.Blocked, default);
                    }
                }
            }
            else if (y > previousY)
            {
                int previousTop = ClampAdd(previousY, request.Body.HeightMillimeters);
                int currentTop = ClampAdd(y, request.Body.HeightMillimeters);
                WorldTerrainColumnSurface contact = default;
                for (int footprint = 0; footprint < 5; footprint++)
                {
                    long footprintX = checked(x + SampleOffsetX(footprint, request.Body));
                    long footprintZ = checked(z + SampleOffsetZ(footprint, request.Body));
                    if (!WorldPosition.TryFromGlobalMillimeters(
                            footprintX, y, footprintZ, out WorldPosition probe))
                        return new SimpleAirborneMotionResult(
                            SimpleAirborneMotionStatus.OutsidePreparedWorld, default);
                    WorldTerrainColumnQueryStatus query = world.TryFindLowestCeilingAtOrAbove(
                        probe, previousTop, out WorldTerrainColumnSurface ceiling);
                    if (query == WorldTerrainColumnQueryStatus.Unavailable)
                        return new SimpleAirborneMotionResult(
                            SimpleAirborneMotionStatus.OutsidePreparedWorld, default);
                    if (query != WorldTerrainColumnQueryStatus.Complete)
                        return new SimpleAirborneMotionResult(SimpleAirborneMotionStatus.Blocked, default);
                    if (ceiling.IsValid && ceiling.IsCeiling && ceiling.HeightMillimeters < currentTop &&
                        (!contact.IsValid || ceiling.HeightMillimeters < contact.HeightMillimeters))
                        contact = ceiling;
                }
                if (contact.IsValid)
                {
                    int contactY = checked(contact.HeightMillimeters - request.Body.HeightMillimeters);
                    if (!WorldPosition.TryFromGlobalMillimeters(x, contactY, z, out WorldPosition stopped))
                        return new SimpleAirborneMotionResult(
                            SimpleAirborneMotionStatus.OutsidePreparedWorld, default);
                    return IsSweepClear(
                        Bounds(request.Current, request.Body),
                        Bounds(stopped, request.Body),
                        dynamicBlockingBounds,
                        dynamicProjectionComplete)
                        ? new SimpleAirborneMotionResult(
                            SimpleAirborneMotionStatus.CeilingBlocked, stopped, contact)
                        : new SimpleAirborneMotionResult(
                            SimpleAirborneMotionStatus.Blocked, default);
                }
            }
            previousY = y;
        }

        return IsSweepClear(
            Bounds(request.Current, request.Body),
            Bounds(target, request.Body),
            dynamicBlockingBounds,
            dynamicProjectionComplete)
            ? new SimpleAirborneMotionResult(SimpleAirborneMotionStatus.Airborne, target)
            : new SimpleAirborneMotionResult(SimpleAirborneMotionStatus.Blocked, default);
    }

    private TerrainResolutionStatus ResolveGroundedPath(
        in WorldPosition current,
        long targetXMillimeters,
        long targetZMillimeters,
        in SimplePhysicsBody body,
        out WorldPosition resolved,
        out WorldTerrainColumnSurface support)
    {
        resolved = default;
        support = default;
        TerrainResolutionStatus currentStatus = ResolveGroundedEndpoint(
            current.GlobalXMillimeters,
            current.GlobalZMillimeters,
            current.YMillimeters,
            0,
            0,
            body,
            out WorldPosition groundedCurrent,
            out _);
        if (currentStatus != TerrainResolutionStatus.Grounded)
            return currentStatus;
        if (groundedCurrent.YMillimeters != current.YMillimeters)
            return TerrainResolutionStatus.Unsupported;

        long deltaX = checked(targetXMillimeters - current.GlobalXMillimeters);
        long deltaZ = checked(targetZMillimeters - current.GlobalZMillimeters);
        ulong distance = Math.Max(Absolute(deltaX), Absolute(deltaZ));
        ulong spacing = GroundedPathSampleSpacingMillimeters;
        ulong measuredSamples = Math.Max(1UL, checked((distance + spacing - 1) / spacing));
        if (measuredSamples > MaximumGroundedPathSamples)
            return TerrainResolutionStatus.Blocked;

        int samples = checked((int)measuredSamples);
        int referenceY = current.YMillimeters;
        for (int sample = 1; sample <= samples; sample++)
        {
            long x = checked(current.GlobalXMillimeters +
                (long)((Int128)deltaX * sample / samples));
            long z = checked(current.GlobalZMillimeters +
                (long)((Int128)deltaZ * sample / samples));
            TerrainResolutionStatus status = ResolveGroundedEndpoint(
                x,
                z,
                referenceY,
                body.MaximumStepUpMillimeters,
                body.MaximumStepDownMillimeters,
                body,
                out resolved,
                out support);
            if (status != TerrainResolutionStatus.Grounded)
                return status;
            referenceY = resolved.YMillimeters;
        }
        return TerrainResolutionStatus.Grounded;
    }

    private TerrainResolutionStatus ResolveNearestGroundedEndpoint(
        in WorldPosition target,
        in SimplePhysicsBody body,
        out WorldPosition resolved,
        out WorldTerrainColumnSurface support)
    {
        resolved = default;
        support = default;
        WorldPreparedView? world = _world.PreparedWorld;
        if (world is null)
            return TerrainResolutionStatus.OutsidePreparedWorld;
        WorldTerrainColumnQueryStatus query = world.TryFindNearestSupport(
            target,
            target.YMillimeters,
            out support);
        if (query == WorldTerrainColumnQueryStatus.Unavailable)
            return TerrainResolutionStatus.OutsidePreparedWorld;
        if (query != WorldTerrainColumnQueryStatus.Complete)
            return TerrainResolutionStatus.Blocked;
        if (!support.IsValid || !support.IsSupport)
            return TerrainResolutionStatus.Unsupported;
        if (!WorldPosition.TryFromGlobalMillimeters(
                target.GlobalXMillimeters,
                support.HeightMillimeters,
                target.GlobalZMillimeters,
                out resolved))
            return TerrainResolutionStatus.OutsidePreparedWorld;
        return ValidateFootprintAndClearance(world, resolved, body, support);
    }

    private TerrainResolutionStatus ResolveGroundedEndpoint(
        long globalXMillimeters,
        long globalZMillimeters,
        int referenceYMillimeters,
        int maximumStepUpMillimeters,
        int maximumStepDownMillimeters,
        in SimplePhysicsBody body,
        out WorldPosition resolved,
        out WorldTerrainColumnSurface support)
    {
        resolved = default;
        support = default;
        WorldPreparedView? world = _world.PreparedWorld;
        if (world is null ||
            !WorldPosition.TryFromGlobalMillimeters(
                globalXMillimeters,
                referenceYMillimeters,
                globalZMillimeters,
                out WorldPosition probe))
            return TerrainResolutionStatus.OutsidePreparedWorld;

        int maximumY = ClampAdd(referenceYMillimeters, maximumStepUpMillimeters);
        int minimumY = ClampAdd(referenceYMillimeters, -maximumStepDownMillimeters);
        WorldTerrainColumnQueryStatus query = world.TryFindHighestSupportAtOrBelow(
            probe,
            maximumY,
            out support);
        if (query == WorldTerrainColumnQueryStatus.Unavailable)
            return TerrainResolutionStatus.OutsidePreparedWorld;
        if (query != WorldTerrainColumnQueryStatus.Complete)
            return TerrainResolutionStatus.Blocked;
        if (!support.IsValid || !support.IsSupport || support.HeightMillimeters < minimumY)
            return TerrainResolutionStatus.Unsupported;
        if (!WorldPosition.TryFromGlobalMillimeters(
                globalXMillimeters,
                support.HeightMillimeters,
                globalZMillimeters,
                out resolved))
            return TerrainResolutionStatus.OutsidePreparedWorld;
        return ValidateFootprintAndClearance(world, resolved, body, support);
    }

    private static TerrainResolutionStatus ValidateFootprintAndClearance(
        WorldPreparedView world,
        in WorldPosition standing,
        in SimplePhysicsBody body,
        in WorldTerrainColumnSurface centerSupport)
    {
        int maximumSupport = ClampAdd(
            standing.YMillimeters, body.MaximumStepUpMillimeters);
        int minimumSupport = ClampAdd(
            standing.YMillimeters, -body.MaximumStepDownMillimeters);
        long bodyTopExclusive = checked((long)standing.YMillimeters + body.HeightMillimeters);
        Span<WorldTerrainColumnSurface> surfaces =
            stackalloc WorldTerrainColumnSurface[TerrainColumnSurfaceCapacity];
        for (int sample = 0; sample < 5; sample++)
        {
            long x = checked(standing.GlobalXMillimeters + SampleOffsetX(sample, body));
            long z = checked(standing.GlobalZMillimeters + SampleOffsetZ(sample, body));
            if (!WorldPosition.TryFromGlobalMillimeters(
                    x, standing.YMillimeters, z, out WorldPosition probe))
                return TerrainResolutionStatus.OutsidePreparedWorld;

            WorldTerrainColumnQueryStatus query = world.TryCopyTerrainColumnSurfaces(
                probe, surfaces, out int count, out _);
            if (query == WorldTerrainColumnQueryStatus.Unavailable)
                return TerrainResolutionStatus.OutsidePreparedWorld;
            if (query != WorldTerrainColumnQueryStatus.Complete)
                return TerrainResolutionStatus.Blocked;

            WorldTerrainColumnSurface footprintSupport = sample == 0 ? centerSupport : default;
            for (int index = 0; sample != 0 && index < count; index++)
            {
                WorldTerrainColumnSurface candidate = surfaces[index];
                if (candidate.IsSupport && candidate.HeightMillimeters <= maximumSupport)
                {
                    footprintSupport = candidate;
                    break;
                }
            }
            if (!footprintSupport.IsValid ||
                footprintSupport.HeightMillimeters < minimumSupport)
                return TerrainResolutionStatus.Unsupported;

            for (int index = count - 1; index >= 0; index--)
            {
                WorldTerrainColumnSurface ceiling = surfaces[index];
                if (!ceiling.IsCeiling || ceiling.HeightMillimeters < standing.YMillimeters)
                    continue;
                // A two-sided shell whose Top is the reachable support at this footprint sample
                // is a step candidate, not a ceiling that blocks reaching its own upper face.
                if (footprintSupport.Face == WorldTerrainSurfaceFace.Top &&
                    footprintSupport.SurfaceId == ceiling.SurfaceId)
                    continue;
                if (ceiling.HeightMillimeters < bodyTopExclusive)
                    return TerrainResolutionStatus.Blocked;
            }
        }
        return TerrainResolutionStatus.Grounded;
    }

    private static TerrainResolutionStatus ValidateStructuralFootprint(
        WorldPreparedView world,
        in WorldPosition position,
        in SimplePhysicsBody body)
    {
        Span<WorldTerrainColumnSurface> surfaces =
            stackalloc WorldTerrainColumnSurface[TerrainColumnSurfaceCapacity];
        for (int sample = 0; sample < 5; sample++)
        {
            long x = checked(position.GlobalXMillimeters + SampleOffsetX(sample, body));
            long z = checked(position.GlobalZMillimeters + SampleOffsetZ(sample, body));
            if (!WorldPosition.TryFromGlobalMillimeters(
                    x, position.YMillimeters, z, out WorldPosition probe))
                return TerrainResolutionStatus.OutsidePreparedWorld;
            WorldTerrainColumnQueryStatus query = world.TryCopyTerrainColumnSurfaces(
                probe, surfaces, out _, out _);
            if (query == WorldTerrainColumnQueryStatus.Unavailable)
                return TerrainResolutionStatus.OutsidePreparedWorld;
            if (query != WorldTerrainColumnQueryStatus.Complete)
                return TerrainResolutionStatus.Blocked;
        }
        return TerrainResolutionStatus.Grounded;
    }

    private static int SampleOffsetX(int sample, in SimplePhysicsBody body) => sample switch
    {
        0 => 0,
        1 or 3 => -body.HalfExtentXMillimeters,
        2 or 4 => body.HalfExtentXMillimeters,
        _ => throw new ArgumentOutOfRangeException(nameof(sample)),
    };

    private static int SampleOffsetZ(int sample, in SimplePhysicsBody body) => sample switch
    {
        0 => 0,
        1 or 2 => -body.HalfExtentZMillimeters,
        3 or 4 => body.HalfExtentZMillimeters,
        _ => throw new ArgumentOutOfRangeException(nameof(sample)),
    };

    private static int ClampAdd(int value, int delta)
    {
        long result = (long)value + delta;
        return result > int.MaxValue ? int.MaxValue :
            result < int.MinValue ? int.MinValue : (int)result;
    }

    private static ulong Absolute(long value) =>
        value >= 0 ? (ulong)value : unchecked((ulong)(-(Int128)value));

    private static SimpleMotionStatus ToMotionStatus(TerrainResolutionStatus status) => status switch
    {
        TerrainResolutionStatus.Unsupported => SimpleMotionStatus.Unsupported,
        TerrainResolutionStatus.Blocked => SimpleMotionStatus.Blocked,
        TerrainResolutionStatus.OutsidePreparedWorld => SimpleMotionStatus.OutsidePreparedWorld,
        _ => throw new InvalidOperationException("A grounded terrain result was mapped as failure."),
    };

    private static SimpleOccupancyStatus ToOccupancyStatus(
        TerrainResolutionStatus status) => status switch
    {
        TerrainResolutionStatus.Unsupported => SimpleOccupancyStatus.Unsupported,
        TerrainResolutionStatus.Blocked => SimpleOccupancyStatus.Blocked,
        TerrainResolutionStatus.OutsidePreparedWorld => SimpleOccupancyStatus.OutsidePreparedWorld,
        _ => throw new InvalidOperationException("A grounded terrain result was mapped as failure."),
    };

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

    private enum TerrainResolutionStatus : byte
    {
        Grounded,
        Unsupported,
        Blocked,
        OutsidePreparedWorld,
    }
}
