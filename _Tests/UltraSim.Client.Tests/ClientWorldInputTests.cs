using UltraSim.Client.Core;
using UltraSim.Game.Input;

static class ClientWorldInputTests
{
    internal static void Run()
    {
        NormalizesAndSequencesWithoutEnginePolicyLeakage();
        SemanticLocomotionUpdatesRemainExplicit();
        OrbitCommandInterpretsMouseGestures();
        ReferenceProfilesKeepMovementAndLookIndependent();
        FocusAndActionEdgesAreCommonClientSemantics();
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
}
