using System.Diagnostics;
using UltraSim.Client.Core;
using UltraSim.Game.Input;

static class ClientWorldInputTests
{
    internal static void Run()
    {
        NormalizesAndSequencesWithoutEnginePolicyLeakage();
        SemanticLocomotionUpdatesRemainExplicit();
        OrbitCommandInterpretsMouseGestures();
        RejectedInteractionSequenceDoesNotPoisonOrbitMovement();
        ReferenceProfilesKeepMovementAndLookIndependent();
        FocusAndActionEdgesAreCommonClientSemantics();
        SchedulerDecouplesSamplesAndPreservesEdges();
        SchedulerRetainsCadencePhaseAcrossSixtyHertzSamples();
        SchedulerIsBoundedAndLocallyRateLimited();
        InvalidSamplesAndCrossThreadUseFailClosed();
    }

    private static void NormalizesAndSequencesWithoutEnginePolicyLeakage()
    {
        var sequencer = new ClientWorldInputSequencer(41);
        Assert.True(sequencer.TryCreateCommand(
            new ClientWorldInputSample(1, 1, GamePlayerActions.None, true),
            out GamePlayerCommand diagonal));
        Assert.Equal((ulong)41, diagonal.Sequence);
        Assert.Equal(sequencer.ControlSession, diagonal.ControlSession);
        Assert.Equal(GameLocomotionUpdateKind.Direct, diagonal.Locomotion.Kind);
        Assert.Equal(diagonal.Locomotion.DirectX, diagonal.Locomotion.DirectZ);
        Assert.True(diagonal.Locomotion.DirectX is >= 23_169 and <= 23_171);
        Assert.True(diagonal.IsValid);

        Assert.True(sequencer.TryCreateCommand(
            new ClientWorldInputSample(-4, 0, GamePlayerActions.None, true),
            out GamePlayerCommand clamped));
        Assert.Equal(GamePlayerCommand.MinimumAxisMagnitude, clamped.Locomotion.DirectX);
        Assert.Equal((short)0, clamped.Locomotion.DirectZ);
        Assert.Equal((ulong)42, clamped.Sequence);
        Assert.Equal(sequencer.ControlSession, sequencer.Snapshot().ControlSession);

        var replacement = new ClientWorldInputSequencer();
        Assert.True(sequencer.ControlSession != replacement.ControlSession);
        Assert.False(new GamePlayerCommand(
            sequencer.ControlSession,
            43,
            new GameLocomotionUpdate(
                GameLocomotionUpdateKind.Direct,
                short.MaxValue,
                short.MaxValue,
                0,
                0),
            GamePlayerActions.None,
            GamePlayerActions.None).IsValid);

        var exhausted = new ClientWorldInputSequencer(
            new GameControlSessionEpoch(0xF00D), ulong.MaxValue);
        Assert.True(exhausted.TryCreateCommand(default, out GamePlayerCommand final));
        Assert.Equal(ulong.MaxValue, final.Sequence);
        Assert.False(exhausted.TryCreateCommand(default, out _));
        Assert.Equal(ulong.MaxValue, exhausted.Snapshot().LastSequence);
    }

    private static void SemanticLocomotionUpdatesRemainExplicit()
    {
        var sequencer = new ClientWorldInputSequencer();
        Assert.True(sequencer.TryCreateSemanticCommand(
            new ClientWorldCommandIntent(
                GameLocomotionUpdate.Unchanged,
                GamePlayerActions.Interact,
                true),
            out GamePlayerCommand actionOnly));
        Assert.Equal(GameLocomotionUpdateKind.Unchanged, actionOnly.Locomotion.Kind);
        Assert.Equal(GamePlayerActions.Interact, actionOnly.PressedActions);

        Assert.True(sequencer.TryCreateSemanticCommand(
            new ClientWorldCommandIntent(
                GameLocomotionUpdate.CreateDestination(12_345, -67_890),
                GamePlayerActions.None,
                true),
            out GamePlayerCommand destination));
        Assert.Equal(GameLocomotionUpdateKind.Destination, destination.Locomotion.Kind);
        Assert.Equal(12_345L, destination.Locomotion.DestinationXMillimeters);
        Assert.Equal(-67_890L, destination.Locomotion.DestinationZMillimeters);

        Assert.True(sequencer.TryCreateSemanticCommand(
            new ClientWorldCommandIntent(
                GameLocomotionUpdate.Unchanged,
                GamePlayerActions.None,
                false),
            out GamePlayerCommand unfocused));
        Assert.Equal(GameLocomotionUpdateKind.Stop, unfocused.Locomotion.Kind);
        Assert.False(sequencer.TryCreateSemanticCommand(
            new ClientWorldCommandIntent(
                new GameLocomotionUpdate(GameLocomotionUpdateKind.Direct, 0, 0, 0, 0),
                GamePlayerActions.None,
                true),
            out _));
        Assert.False(new GameLocomotionUpdate(
            GameLocomotionUpdateKind.Unchanged, 1, 0, 0, 0).IsValid);
        Assert.False(new GameLocomotionUpdate(
            GameLocomotionUpdateKind.Stop, 0, 0, 1, 0).IsValid);
        Assert.False(new GameLocomotionUpdate(
            GameLocomotionUpdateKind.Destination, 1, 0, 1, 1).IsValid);
        Assert.False(new GameLocomotionUpdate(
            (GameLocomotionUpdateKind)byte.MaxValue, 0, 0, 0, 0).IsValid);
    }

    private static void FocusAndActionEdgesAreCommonClientSemantics()
    {
        var sequencer = new ClientWorldInputSequencer();
        var held = new ClientWorldInputSample(
            0.25f, -0.5f, GamePlayerActions.Interact, true);
        Assert.True(sequencer.TryCreateCommand(held, out GamePlayerCommand first));
        Assert.Equal(GamePlayerActions.Interact, first.PressedActions);
        Assert.True(sequencer.TryCreateCommand(held, out GamePlayerCommand second));
        Assert.Equal(GamePlayerActions.None, second.PressedActions);

        Assert.True(sequencer.TryCreateCommand(held with { HasFocus = false }, out GamePlayerCommand unfocused));
        Assert.Equal(GameLocomotionUpdateKind.Stop, unfocused.Locomotion.Kind);
        Assert.Equal(GamePlayerActions.None, unfocused.HeldActions);
        Assert.True(sequencer.TryCreateCommand(held, out GamePlayerCommand refocused));
        Assert.Equal(GamePlayerActions.Interact, refocused.HeldActions);
        Assert.Equal(GamePlayerActions.None, refocused.PressedActions);
        Assert.True(sequencer.TryCreateCommand(held with { HeldActions = GamePlayerActions.None }, out _));
        Assert.True(sequencer.TryCreateCommand(held, out GamePlayerCommand pressedAfterRelease));
        Assert.Equal(GamePlayerActions.Interact, pressedAfterRelease.PressedActions);
        Assert.True(pressedAfterRelease.IsValid);

        Assert.True(sequencer.TryCreateCommand(
            held with { HeldActions = GamePlayerActions.Jump }, out GamePlayerCommand jump));
        Assert.Equal(GamePlayerActions.Jump, jump.PressedActions);
        Assert.True(sequencer.TryCreateCommand(
            held with { HeldActions = GamePlayerActions.Jump }, out GamePlayerCommand heldJump));
        Assert.Equal(GamePlayerActions.None, heldJump.PressedActions);
    }

    private static void OrbitCommandInterpretsMouseGestures()
    {
        var profile = new OrbitCommandProfile();
        ClientOrbitCommandSample sample = Sample(timestamp: 0);

        ClientControlWorldPoint click = new(8_000, 6_000);
        ClientControlProfileDecision selected = profile.Evaluate(sample with
        {
            PrimaryPressed = true,
            PrimaryReleased = true,
            PointerWorldPointValid = true,
            PointerWorldPoint = click,
            TimestampMilliseconds = 100,
        });
        Assert.True(selected.SelectionRequested);
        Assert.Equal(GameLocomotionUpdateKind.Unchanged, selected.Command.Locomotion.Kind);

        ClientControlProfileDecision destination = profile.Evaluate(sample with
        {
            PrimaryPressed = true,
            PrimaryReleased = true,
            PointerWorldPointValid = true,
            PointerWorldPoint = click,
            TimestampMilliseconds = 250,
        });
        Assert.False(destination.SelectionRequested);
        Assert.Equal(GameLocomotionUpdateKind.Destination, destination.Command.Locomotion.Kind);
        Assert.Equal(click.XMillimeters, destination.Command.Locomotion.DestinationXMillimeters);

        ClientControlProfileDecision cancelled = profile.Evaluate(sample with
        {
            SecondaryPressed = true,
            TimestampMilliseconds = 251,
        });
        Assert.Equal(GameLocomotionUpdateKind.Stop, cancelled.Command.Locomotion.Kind);

        ClientControlWorldPoint steer = new(10_000, 0);
        _ = profile.Evaluate(sample with
        {
            PrimaryDown = true,
            PrimaryPressed = true,
            PointerWorldPointValid = true,
            PointerWorldPoint = steer,
            TimestampMilliseconds = 1_000,
        });
        ClientControlProfileDecision steering = profile.Evaluate(sample with
        {
            PrimaryDown = true,
            PointerWorldPointValid = true,
            PointerWorldPoint = steer,
            TimestampMilliseconds = 1_200,
        });
        Assert.Equal(GameLocomotionUpdateKind.Direct, steering.Command.Locomotion.Kind);
        Assert.Equal(GamePlayerCommand.MaximumAxisMagnitude, steering.Command.Locomotion.DirectX);
        ClientControlProfileDecision steeringReleased = profile.Evaluate(sample with
        {
            PrimaryReleased = true,
            PointerWorldPointValid = true,
            PointerWorldPoint = steer,
            TimestampMilliseconds = 1_201,
        });
        Assert.Equal(GameLocomotionUpdateKind.Stop, steeringReleased.Command.Locomotion.Kind);

        static ClientOrbitCommandSample Sample(long timestamp) => new(
            GamePlayerActions.None,
            true,
            0,
            0,
            false,
            false,
            false,
            false,
            false,
            false,
            default,
            timestamp);
    }

    private static void RejectedInteractionSequenceDoesNotPoisonOrbitMovement()
    {
        var profile = new OrbitCommandProfile();
        var sample = new ClientOrbitCommandSample(
            GamePlayerActions.None, true, 0, 0,
            false, false, false, false, false,
            true, new ClientControlWorldPoint(4_000, 0), 0);

        ClientControlProfileDecision interaction = profile.Evaluate(sample with
        {
            HeldActions = GamePlayerActions.Interact,
            TimestampMilliseconds = 100,
        });
        Assert.Equal(GamePlayerActions.Interact, interaction.Command.HeldActions);
        _ = profile.Evaluate(sample with { TimestampMilliseconds = 101 });

        _ = profile.Evaluate(sample with
        {
            PrimaryDown = true,
            PrimaryPressed = true,
            TimestampMilliseconds = 200,
        });
        ClientControlProfileDecision heldMovement = profile.Evaluate(sample with
        {
            PrimaryDown = true,
            TimestampMilliseconds = 350,
        });
        Assert.Equal(GameLocomotionUpdateKind.Direct, heldMovement.Command.Locomotion.Kind);

        _ = profile.Evaluate(sample with
        {
            PrimaryReleased = true,
            TimestampMilliseconds = 351,
        });
        _ = profile.Evaluate(sample with
        {
            PrimaryPressed = true,
            PrimaryReleased = true,
            TimestampMilliseconds = 500,
        });
        ClientControlProfileDecision doubleClickMovement = profile.Evaluate(sample with
        {
            PrimaryPressed = true,
            PrimaryReleased = true,
            TimestampMilliseconds = 700,
        });
        Assert.Equal(
            GameLocomotionUpdateKind.Destination,
            doubleClickMovement.Command.Locomotion.Kind);
    }

    private static void ReferenceProfilesKeepMovementAndLookIndependent()
    {
        var thirdPerson = new ThirdPersonDirectProfile();
        var rotated = new ClientHorizontalCameraBasis(0, 1, 1, 0);
        GameLocomotionUpdate cameraForward = thirdPerson.Evaluate(0, -1, rotated);
        Assert.Equal(GameLocomotionUpdateKind.Direct, cameraForward.Kind);
        Assert.Equal(GamePlayerCommand.MaximumAxisMagnitude, cameraForward.DirectX);
        Assert.Equal((short)0, cameraForward.DirectZ);
        Assert.Equal(GameLocomotionUpdateKind.Stop, thirdPerson.Evaluate(0, 0, rotated).Kind);

        var independent = new IndependentMoveLookProfile();
        ClientControlWorldPoint southLook = new(0, 9_000);
        ClientControlProfileDecision split = independent.Evaluate(
            0,
            -1,
            GamePlayerActions.None,
            true,
            true,
            southLook);
        Assert.Equal(GameLocomotionUpdateKind.Direct, split.Command.Locomotion.Kind);
        Assert.Equal((short)0, split.Command.Locomotion.DirectX);
        Assert.Equal(GamePlayerCommand.MinimumAxisMagnitude, split.Command.Locomotion.DirectZ);
        Assert.True(split.LookRequested);
        Assert.Equal(southLook, split.LookPoint);
    }

    private static void InvalidSamplesAndCrossThreadUseFailClosed()
    {
        var sequencer = new ClientWorldInputSequencer();
        Assert.False(sequencer.TryCreateCommand(
            new ClientWorldInputSample(float.NaN, 0, GamePlayerActions.None, true), out _));
        Assert.False(sequencer.TryCreateCommand(
            new ClientWorldInputSample(0, 0, (GamePlayerActions)0x8000, true), out _));

        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                sequencer.TryCreateCommand(default, out _);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.Start();
        thread.Join();
        Assert.True(failure is InvalidOperationException);
    }

    private static void SchedulerDecouplesSamplesAndPreservesEdges()
    {
        long start = 10_000;
        var scheduler = new ClientWorldInputScheduler(
            new ClientInputSubmissionPolicy(60, 20, 8, 100), start);
        var sequencer = new ClientWorldInputSequencer();
        var sent = new List<GamePlayerCommand>();
        bool accepting = true;

        bool Submit(in ClientWorldScheduledCommand scheduled, out ulong sequence)
        {
            sequence = 0;
            if (!accepting || !sequencer.TryCreateSemanticCommand(scheduled.Intent, out var command))
                return false;
            sequence = command.Sequence;
            sent.Add(command);
            return true;
        }

        var move = new ClientWorldCommandIntent(
            GameLocomotionUpdate.CreateDirect(short.MaxValue, 0),
            GamePlayerActions.None,
            true);
        Assert.True(scheduler.TryObserve(move, default, start));
        Assert.True(scheduler.TryPump(start, Submit));
        Assert.Equal(1, sent.Count);

        // Faster samples replace continuous intent without producing one command per sample.
        for (int index = 1; index <= 12; index++)
        {
            var changed = move with
            {
                Locomotion = GameLocomotionUpdate.CreateDirect(
                    checked((short)(short.MaxValue - index)), checked((short)index)),
            };
            Assert.True(scheduler.TryObserve(changed, default, start + index));
            Assert.True(scheduler.TryPump(start + index, Submit));
        }
        Assert.Equal(1, sent.Count);

        // A discrete edge is not held behind the recommended continuous cadence.
        var jump = new ClientWorldCommandIntent(
            GameLocomotionUpdate.Unchanged, GamePlayerActions.Jump, true);
        Assert.True(scheduler.TryObserve(jump, default, start + 20));
        Assert.True(scheduler.TryPump(start + 20, Submit));
        Assert.Equal(2, sent.Count);
        Assert.Equal(GamePlayerActions.Jump, sent[1].PressedActions);

        // Stop/release remains retained across ordinary transport pressure.
        accepting = false;
        var stop = new ClientWorldCommandIntent(
            GameLocomotionUpdate.Stop, GamePlayerActions.None, true);
        Assert.True(scheduler.TryObserve(stop, default, start + 30));
        Assert.False(scheduler.TryPump(start + 30, Submit));
        Assert.Equal(2, sent.Count);
        accepting = true;
        Assert.True(scheduler.TryPump(start + 31, Submit));
        Assert.Equal(3, sent.Count);
        Assert.Equal(GameLocomotionUpdateKind.Stop, sent[2].Locomotion.Kind);
        Assert.Equal(GamePlayerActions.None, sent[2].HeldActions);
        scheduler.ObserveCompleted(sent[^1].Sequence);

        ClientWorldInputScheduleSnapshot snapshot = scheduler.GetSnapshot();
        Assert.True(snapshot.SamplesObserved >= 14);
        Assert.True(snapshot.ContinuousCommandsCoalesced >= 11);
        Assert.Equal((ulong)1, snapshot.EdgeObligationsGenerated);
        Assert.Equal((ulong)1, snapshot.EdgeObligationsSubmitted);
        Assert.Equal((ulong)1, snapshot.EdgeObligationsCompleted);
        Assert.Equal((ulong)1, snapshot.ReleaseOrStopGenerated);
        Assert.Equal((ulong)1, snapshot.ReleaseOrStopSubmitted);
        Assert.Equal((ulong)1, snapshot.ReleaseOrStopCompleted);
        Assert.Equal((ulong)1, snapshot.TransportDeferred);
        Assert.Equal(0, snapshot.PendingObligations);
        Assert.Equal(0, snapshot.SubmittedCompletionDebt);
        Assert.False(snapshot.ContinuousStatePending);
        scheduler.ResetTelemetryWindow();
        ClientWorldInputScheduleSnapshot reset = scheduler.GetSnapshot();
        Assert.Equal((ulong)0, reset.SamplesObserved);
        Assert.Equal((ulong)0, reset.EdgeObligationsGenerated);
    }

    private static void SchedulerIsBoundedAndLocallyRateLimited()
    {
        long start = 20_000;
        var scheduler = new ClientWorldInputScheduler(
            new ClientInputSubmissionPolicy(20, 20, 2, 100, 2), start);
        var sequencer = new ClientWorldInputSequencer();

        bool Submit(in ClientWorldScheduledCommand scheduled, out ulong sequence)
        {
            bool accepted = sequencer.TryCreateSemanticCommand(scheduled.Intent, out var command);
            sequence = accepted ? command.Sequence : 0;
            return accepted;
        }

        Assert.True(scheduler.TryObserve(new ClientWorldCommandIntent(
            GameLocomotionUpdate.Unchanged, GamePlayerActions.Jump, true), default, start));
        Assert.True(scheduler.TryObserve(new ClientWorldCommandIntent(
            GameLocomotionUpdate.Unchanged, GamePlayerActions.None, true), default, start + 1));
        Assert.False(scheduler.TryObserve(new ClientWorldCommandIntent(
            GameLocomotionUpdate.Unchanged, GamePlayerActions.Interact, true), default, start + 2));
        Assert.True(scheduler.TryPump(start + 3, Submit));
        Assert.True(scheduler.TryPump(start + 4, Submit));
        Assert.True(scheduler.TryObserve(new ClientWorldCommandIntent(
            GameLocomotionUpdate.Unchanged, GamePlayerActions.Interact, true), default, start + 5));
        Assert.True(scheduler.TryPump(start + 6, Submit));
        ClientWorldInputScheduleSnapshot snapshot = scheduler.GetSnapshot();
        Assert.Equal((ulong)1, snapshot.CapacityRejections);
        Assert.True(snapshot.LocalHardCapDeferred > 0);
        Assert.True(snapshot.PendingObligations > 0);
        Assert.Throws<InvalidOperationException>(scheduler.ResetTelemetryWindow);
    }

    private static void SchedulerRetainsCadencePhaseAcrossSixtyHertzSamples()
    {
        long start = 30_000;
        long sampleInterval = Stopwatch.Frequency / 60;
        var scheduler = new ClientWorldInputScheduler(
            new ClientInputSubmissionPolicy(60, 30, 8, 100), start);
        var sequencer = new ClientWorldInputSequencer();
        int submitted = 0;

        bool Submit(in ClientWorldScheduledCommand scheduled, out ulong sequence)
        {
            bool accepted = sequencer.TryCreateSemanticCommand(scheduled.Intent, out var command);
            sequence = accepted ? command.Sequence : 0;
            if (accepted) submitted++;
            return accepted;
        }

        for (int index = 0; index < 120; index++)
        {
            long now = checked(start + index * sampleInterval);
            var intent = new ClientWorldCommandIntent(
                GameLocomotionUpdate.CreateDirect(
                    checked((short)(GamePlayerCommand.MaximumAxisMagnitude - index)),
                    checked((short)index)),
                GamePlayerActions.None,
                true);
            Assert.True(scheduler.TryObserve(in intent, default, now));
            Assert.True(scheduler.TryPump(now, Submit));
        }

        Assert.True(submitted is >= 59 and <= 61);
        Assert.True(scheduler.GetSnapshot().ContinuousCommandsCoalesced >= 59);
    }
}
