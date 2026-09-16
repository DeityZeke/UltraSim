using System.Diagnostics;
using UltraSim.ECS;
using UltraSim.Game.Admission;
using UltraSim.Game.Chat;
using UltraSim.Game.Composition;
using UltraSim.Game.Input;
using UltraSim.Game.Inventory;
using UltraSim.Game.World;
using UltraSim.Server.Runtime;

internal enum Pf5R7MovementShape : byte
{
    Stationary = 0,
    FreeMovement = 1,
    CollisionChaos = 2,
}

internal sealed class Pf5R7GameAuthorityWorkload : IP03ExternalServerWorkload
{
    private readonly int _participants;
    private readonly int _activeMovers;
    private readonly bool _chatEnabled;
    private readonly int _chatIntervalTicks;
    private readonly Pf5R7MovementShape _movement;
    private readonly WorldPack _worldPack;
    private readonly UltraSimGameComposition _composition;
    private readonly ServerRuntime _runtime;
    private readonly AdmissionService _admission;
    private readonly GameChatService _chat;
    private readonly ParticipantId[] _participantIds;
    private readonly Entity[] _actors;
    private readonly GameControlSessionEpoch[] _controlSessions;
    private readonly ulong[] _sequences;
    private readonly string[] _chatTexts;
    private readonly ServerGenerationStateOperation<GameGenerationState, int>
        _populateOperation;
    private readonly ServerGenerationStateOperation<GameGenerationState, int>
        _applyInputOperation;
    private readonly ServerGenerationStateOperation<GameGenerationState,
        GameGenerationStepTimingSample>
        _captureTimingOperation;
    private int _pendingTick;
    private ulong _inputAttempts;
    private ulong _inputAccepted;
    private ulong _jumpAttempts;
    private ulong _jumpAccepted;
    private ulong _chatAttempts;
    private ulong _chatAccepted;
    private bool _disposed;

    internal Pf5R7GameAuthorityWorkload(
        int participants,
        Pf5R7MovementShape movement,
        bool chatEnabled,
        bool chaosChat = false)
    {
        if (participants <= 0 || participants > 512)
            throw new ArgumentOutOfRangeException(nameof(participants));
        _participants = participants;
        _movement = movement;
        _activeMovers = movement == Pf5R7MovementShape.Stationary
            ? 0
            : Math.Max(1, (int)Math.Round(participants * .35, MidpointRounding.AwayFromZero));
        _chatEnabled = chatEnabled;
        _chatIntervalTicks = chaosChat ? 6 : 30;
        _participantIds = new ParticipantId[participants];
        _actors = new Entity[participants];
        _controlSessions = new GameControlSessionEpoch[participants];
        _sequences = new ulong[participants];
        _chatTexts = new string[participants];
        _populateOperation = Populate;
        _applyInputOperation = ApplyInputs;
        _captureTimingOperation = CaptureTiming;

        _worldPack = GamePlayableWorld.CreateConceptDemoWorldPack();
        var inventoryLimits = new InventoryLimits(
            participants + 32,
            checked(participants * 4 + 256),
            64);
        _composition = new UltraSimGameComposition(
            inventoryLimits,
            runtimeProfile: GameRuntimeProfile.Networked,
            baseWorldPack: _worldPack);
        _runtime = new ServerRuntime(
            _composition,
            new ServerRuntimeOptions(
                UltraSimGameComposition.CreateWorldOptions(
                    checked(256 + participants * 3),
                    new PersistenceIdentityOptions(new PersistentWorldId(0x5F_7000UL)),
                    GameRuntimeProfile.Networked),
                new WorldClockOptions(60)));
        _runtime.Initialize();
        _runtime.Start();
        _admission = new AdmissionService(_runtime.Game, _composition);
        if (!_runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider,
                GamePlayableWorldScene>(
                static (world, _, game) =>
                    GamePlayableWorld.PopulateSharedConceptDemoScene(world, game),
                out GamePlayableWorldScene scene) || !scene.IsValid)
            throw new InvalidOperationException("PF5-R7 could not populate the Concept Demo scene.");
        _runtime.Tick();

        for (int index = 0; index < participants; index++)
        {
            ParticipantId participant = new(checked((ulong)index + 1));
            AdmissionResult joined = _admission.JoinGuest(
                participant,
                new GameGuestAdmissionRequest(
                    (index & 1) == 0 ? GameGuestBody.HumanFemale : GameGuestBody.HumanMale,
                    $"PF5R7 {index + 1:D3}"));
            if (!joined.IsEntered)
                throw new InvalidOperationException(
                    $"PF5-R7 participant {index} did not enter: {joined.Message}");
            _participantIds[index] = participant;
            _actors[index] = joined.Entity;
            _controlSessions[index] = new GameControlSessionEpoch(checked((ulong)index + 1));
            _chatTexts[index] = $"PF5R7 chat {index + 1:D3}";
        }
        if (!_runtime.Game.TryUseActiveGeneration(_populateOperation, out int populated) ||
            populated != participants)
            throw new InvalidOperationException("PF5-R7 participant population was incomplete.");
        _runtime.Tick();
        if (!_runtime.Game.TryUseActiveGeneration<GameGenerationState, bool>(
                static (_, _, game) =>
                {
                    game.ConfigureStepTiming(true);
                    return true;
                }, out bool configured) || !configured)
            throw new InvalidOperationException("PF5-R7 Game phase timing could not be enabled.");

        var chatLimits = GameChatLimits.Default with
        {
            MaximumParticipants = participants,
            MaximumRecipientsPerSpeech = participants,
            MaximumCandidateEntities = Math.Max(
                GameChatLimits.Default.MaximumCandidateEntities, participants),
        };
        _chat = new GameChatService(_runtime.Game, _admission, chatLimits);
    }

    public P03ExternalServerPhaseSample RunTick(int tick, bool measure)
    {
        EnsureOperational();
        _pendingTick = tick;
        long serverStarted = measure ? Stopwatch.GetTimestamp() : 0;
        long inputStarted = measure ? Stopwatch.GetTimestamp() : 0;
        if (_movement != Pf5R7MovementShape.Stationary &&
            !_runtime.Game.TryUseActiveGeneration(_applyInputOperation, out _))
            throw new InvalidOperationException("PF5-R7 could not apply current Game input.");
        long inputTicks = measure ? Stopwatch.GetTimestamp() - inputStarted : 0;

        long chatStarted = measure ? Stopwatch.GetTimestamp() : 0;
        if (_chatEnabled && tick % _chatIntervalTicks == 0)
        {
            int speaker = (tick / _chatIntervalTicks) % _participants;
            _chatAttempts++;
            GameChatSubmissionResult result = _chat.Submit(
                _participantIds[speaker],
                _chatTexts[speaker],
                (tick / _chatIntervalTicks & 1) == 0
                    ? GameSpeechMode.Say
                    : GameSpeechMode.Global);
            if (result.AcceptedSpeech) _chatAccepted++;
            else throw new InvalidOperationException(
                $"PF5-R7 Chat was rejected: {result.Status} {result.PrivateReply}");
        }
        long chatTicks = measure ? Stopwatch.GetTimestamp() - chatStarted : 0;

        long authorityStarted = measure ? Stopwatch.GetTimestamp() : 0;
        _runtime.Tick();
        long authorityTicks = measure ? Stopwatch.GetTimestamp() - authorityStarted : 0;
        if (!_runtime.Game.TryUseActiveGeneration(_captureTimingOperation,
                out GameGenerationStepTimingSample timing) || !timing.Enabled)
            throw new InvalidOperationException("PF5-R7 could not capture Game phase timing.");
        if (!measure) return default;
        long serverTicks = Stopwatch.GetTimestamp() - serverStarted;
        long authorityAndEcs = Math.Max(0,
            authorityTicks - timing.LocomotionAndPhysicsTicks -
            timing.InventoryAndItemTicks - timing.WorldCommitTicks);
        return new P03ExternalServerPhaseSample(
            serverTicks,
            inputTicks,
            chatTicks,
            authorityAndEcs,
            timing.LocomotionAndPhysicsTicks,
            timing.InventoryAndItemTicks,
            timing.WorldCommitTicks);
    }

    public void ResetMeasurement()
    {
        _inputAttempts = _inputAccepted = _jumpAttempts = _jumpAccepted = 0;
        _chatAttempts = _chatAccepted = 0;
    }

    public object CaptureSummary() => new Pf5R7GameAuthoritySummary(
        _participants,
        _activeMovers,
        _movement.ToString(),
        _chatEnabled,
        _chatIntervalTicks,
        _inputAttempts,
        _inputAccepted,
        _jumpAttempts,
        _jumpAccepted,
        _chatAttempts,
        _chatAccepted,
        "Current Simple Physics exposes no retained broadphase/contact counter. " +
            "Active movers, jump outcomes, and phase time are exact; actor-vs-actor dynamic " +
            "collision is not part of the current resolver.");

    private int Populate(EcsWorld world, ulong generationId, GameGenerationState game)
    {
        int columns = checked((int)Math.Ceiling(Math.Sqrt(_participants)));
        for (int index = 0; index < _participants; index++)
        {
            long spacing = _movement == Pf5R7MovementShape.CollisionChaos ? 250L : 900L;
            long x = checked(GamePlayableWorld.PlayerStart.GlobalXMillimeters +
                (index % columns - columns / 2) * spacing);
            long z = checked(GamePlayableWorld.PlayerStart.GlobalZMillimeters +
                (index / columns - columns / 2) * spacing);
            if (!WorldPosition.TryFromGlobalMillimeters(
                    x, GamePlayableWorld.PlayerStart.YMillimeters, z, out WorldPosition start))
                throw new InvalidOperationException("PF5-R7 participant position was invalid.");
            _ = GamePlayableWorld.PopulateSharedParticipant(
                world, game, _actors[index], start);
            if (game.WorldActions.BeginControlSession(
                    world, _actors[index], _controlSessions[index]) !=
                GameWorldActionStatus.Accepted)
                throw new InvalidOperationException("PF5-R7 control session did not bind.");
        }
        return _participants;
    }

    private int ApplyInputs(EcsWorld world, ulong generationId, GameGenerationState game)
    {
        int applied = 0;
        for (int index = 0; index < _activeMovers; index++)
        {
            if ((_pendingTick + index) % 3 != 0) continue;
            bool xAxis = _movement != Pf5R7MovementShape.CollisionChaos || index % 3 != 0;
            short direction = (short)(((index + _pendingTick / 60) & 1) == 0
                ? GamePlayerCommand.MaximumAxisMagnitude
                : -GamePlayerCommand.MaximumAxisMagnitude);
            short x = xAxis ? direction : (short)0;
            short z = xAxis ? (short)0 : direction;
            bool jump = (_pendingTick + index * 7) % 90 == 0;
            GamePlayerActions actions = jump ? GamePlayerActions.Jump : GamePlayerActions.None;
            var command = new GamePlayerCommand(
                _controlSessions[index],
                checked(++_sequences[index]),
                GameLocomotionUpdate.CreateDirect(x, z),
                actions,
                actions);
            _inputAttempts++;
            if (jump) _jumpAttempts++;
            GameWorldActionStatus status = game.WorldActions.ApplyPlayerCommand(
                world,
                game.WorldState,
                _actors[index],
                command,
                Entity.Null,
                out GamePlayerCommandResult result);
            if (status != GameWorldActionStatus.Accepted)
                throw new InvalidOperationException($"PF5-R7 input was rejected: {status}.");
            _inputAccepted++;
            if (jump && result.Jump == GameWorldActionStatus.Accepted) _jumpAccepted++;
            applied++;
        }
        return applied;
    }

    private static GameGenerationStepTimingSample CaptureTiming(
        EcsWorld world,
        ulong generationId,
        GameGenerationState game) => game.GetLastStepTiming();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _chat.Dispose();
        _admission.Dispose();
        if (_runtime.State == ServerRuntimeState.Running) _runtime.Stop();
        _runtime.Dispose();
    }

    private void EnsureOperational()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(Pf5R7GameAuthorityWorkload));
    }
}

internal sealed record Pf5R7GameAuthoritySummary(
    int Participants,
    int ActiveMovers,
    string MovementShape,
    bool ChatEnabled,
    int ChatIntervalTicks,
    ulong InputAttempts,
    ulong InputAccepted,
    ulong JumpAttempts,
    ulong JumpAccepted,
    ulong ChatAttempts,
    ulong ChatAccepted,
    string PhysicsCounterQualification);
