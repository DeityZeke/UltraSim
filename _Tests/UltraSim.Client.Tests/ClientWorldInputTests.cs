using UltraSim.Client.Core;
using UltraSim.Game.Input;

static class ClientWorldInputTests
{
    internal static void Run()
    {
        NormalizesAndSequencesWithoutEnginePolicyLeakage();
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
        Assert.Equal(diagonal.MoveX, diagonal.MoveZ);
        Assert.True(diagonal.MoveX is >= 23_169 and <= 23_171);

        Assert.True(sequencer.TryCreateCommand(
            new ClientWorldInputSample(-4, 0, GamePlayerActions.None, true),
            out GamePlayerCommand clamped));
        Assert.Equal(GamePlayerCommand.MinimumAxisMagnitude, clamped.MoveX);
        Assert.Equal((short)0, clamped.MoveZ);
        Assert.Equal((ulong)42, clamped.Sequence);
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
        Assert.Equal((short)0, unfocused.MoveX);
        Assert.Equal((short)0, unfocused.MoveZ);
        Assert.Equal(GamePlayerActions.None, unfocused.HeldActions);
        Assert.True(sequencer.TryCreateCommand(held, out GamePlayerCommand refocused));
        Assert.Equal(GamePlayerActions.Interact, refocused.PressedActions);
        Assert.True(refocused.IsValid);
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
