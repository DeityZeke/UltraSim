using UltraSim.Client.Core;

internal static class GameplayFrameLayoutTests
{
    internal static void Run()
    {
        ExactAndReducedAspectsAreMaxFit();
        ContainmentAndFrameLocalTranslationAreHalfOpen();
        InvalidAndOverflowProneDimensionsAreRejected();
    }

    private static void ExactAndReducedAspectsAreMaxFit()
    {
        AssertLayout(1920, 1080, 1920, 1080, new ClientPixelRect(0, 0, 1920, 1080));
        AssertLayout(1920, 1080, 800, 600, new ClientPixelRect(240, 0, 1440, 1080));
        AssertLayout(2560, 1440, 600, 800, new ClientPixelRect(740, 0, 1080, 1440));
        AssertLayout(3440, 1440, 1920, 1080, new ClientPixelRect(440, 0, 2560, 1440));
        AssertLayout(1920, 1080, 600, 800, new ClientPixelRect(555, 0, 810, 1080));
    }

    private static void ContainmentAndFrameLocalTranslationAreHalfOpen()
    {
        GameplayFrameLayout layout = GameplayFrameLayout.MaxFit(
            ClientPixelSize.Create(1920, 1080),
            ClientPixelSize.Create(800, 600));
        Assert.False(layout.ContainsApplicationPoint(new ClientPixelPoint(239, 500)));
        Assert.True(layout.TryToFrameLocal(
            new ClientPixelPoint(240, 0), out ClientPixelPoint origin));
        Assert.Equal(new ClientPixelPoint(0, 0), origin);
        Assert.True(layout.TryToFrameLocal(
            new ClientPixelPoint(1679, 1079), out ClientPixelPoint end));
        Assert.Equal(new ClientPixelPoint(1439, 1079), end);
        Assert.False(layout.TryToFrameLocal(
            new ClientPixelPoint(1680, 500), out _));
        Assert.Equal(518_400L, layout.UnusedArea);
    }

    private static void InvalidAndOverflowProneDimensionsAreRejected()
    {
        Assert.False(ClientPixelSize.TryCreate(0, 1080, out _));
        Assert.False(ClientPixelSize.TryCreate(1920, -1, out _));
        Assert.False(ClientPixelSize.TryCreate(ClientPixelSize.MaximumDimension + 1, 1, out _));
        Assert.Throws<ArgumentException>(() => GameplayFrameLayout.MaxFit(
            default, ClientPixelSize.Create(1920, 1080)));
    }

    private static void AssertLayout(
        int applicationWidth,
        int applicationHeight,
        int outputWidth,
        int outputHeight,
        ClientPixelRect expected)
    {
        GameplayFrameLayout layout = GameplayFrameLayout.MaxFit(
            ClientPixelSize.Create(applicationWidth, applicationHeight),
            ClientPixelSize.Create(outputWidth, outputHeight));
        Assert.Equal(expected, layout.Frame);
        Assert.Equal(applicationWidth - expected.Right, layout.UnusedRight);
        Assert.Equal(applicationHeight - expected.Bottom, layout.UnusedBottom);
    }
}
