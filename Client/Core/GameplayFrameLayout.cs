namespace UltraSim.Client.Core;

/// <summary>Finite application-space size used by the client workspace contract.</summary>
public readonly record struct ClientPixelSize
{
    public const int MaximumDimension = 32_768;

    private ClientPixelSize(int width, int height)
    {
        Width = width;
        Height = height;
    }

    public int Width { get; }
    public int Height { get; }
    public bool IsValid => Width is > 0 and <= MaximumDimension &&
        Height is > 0 and <= MaximumDimension;

    public static bool TryCreate(int width, int height, out ClientPixelSize size)
    {
        size = new ClientPixelSize(width, height);
        if (size.IsValid) return true;
        size = default;
        return false;
    }

    public static ClientPixelSize Create(int width, int height) =>
        TryCreate(width, height, out ClientPixelSize size)
            ? size
            : throw new ArgumentOutOfRangeException(nameof(width),
                $"Client pixel dimensions must be between 1 and {MaximumDimension}.");
}

/// <summary>Integer application-space point. The frame-local seam remains renderer neutral.</summary>
public readonly record struct ClientPixelPoint(int X, int Y);

/// <summary>Half-open integer rectangle in application space.</summary>
public readonly record struct ClientPixelRect(int X, int Y, int Width, int Height)
{
    public int Right => checked(X + Width);
    public int Bottom => checked(Y + Height);
    public bool IsValid => X >= 0 && Y >= 0 && Width > 0 && Height > 0;

    public bool Contains(in ClientPixelPoint point) =>
        point.X >= X && point.X < Right && point.Y >= Y && point.Y < Bottom;
}

/// <summary>
/// Renderer-neutral application workspace result. F1.7 uses the selected output only as an
/// aspect/layout contract; it does not claim an independent render target.
/// </summary>
public readonly record struct GameplayFrameLayout(
    ClientPixelSize Application,
    ClientPixelSize SelectedOutput,
    ClientPixelRect Frame,
    int UnusedLeft,
    int UnusedTop,
    int UnusedRight,
    int UnusedBottom)
{
    public long UnusedArea => checked(
        (long)Application.Width * Application.Height - (long)Frame.Width * Frame.Height);

    public bool ContainsApplicationPoint(in ClientPixelPoint point) => Frame.Contains(point);

    public bool TryToFrameLocal(
        in ClientPixelPoint applicationPoint,
        out ClientPixelPoint frameLocalPoint)
    {
        if (!Frame.Contains(applicationPoint))
        {
            frameLocalPoint = default;
            return false;
        }
        frameLocalPoint = new ClientPixelPoint(
            applicationPoint.X - Frame.X,
            applicationPoint.Y - Frame.Y);
        return true;
    }

    public static GameplayFrameLayout MaxFit(
        in ClientPixelSize application,
        in ClientPixelSize selectedOutput)
    {
        if (!application.IsValid)
            throw new ArgumentException("A finite application size is required.", nameof(application));
        if (!selectedOutput.IsValid)
            throw new ArgumentException("A finite selected output size is required.", nameof(selectedOutput));

        int frameWidth;
        int frameHeight;
        if ((long)application.Width * selectedOutput.Height <=
            (long)application.Height * selectedOutput.Width)
        {
            frameWidth = application.Width;
            frameHeight = checked((int)((long)application.Width * selectedOutput.Height /
                selectedOutput.Width));
        }
        else
        {
            frameHeight = application.Height;
            frameWidth = checked((int)((long)application.Height * selectedOutput.Width /
                selectedOutput.Height));
        }

        int left = (application.Width - frameWidth) / 2;
        int top = (application.Height - frameHeight) / 2;
        var frame = new ClientPixelRect(left, top, frameWidth, frameHeight);
        return new GameplayFrameLayout(
            application,
            selectedOutput,
            frame,
            left,
            top,
            application.Width - frame.Right,
            application.Height - frame.Bottom);
    }
}
