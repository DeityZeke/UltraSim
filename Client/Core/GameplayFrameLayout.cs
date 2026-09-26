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
/// Renderer-neutral application workspace result. SelectedOutput is the native World image size.
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

    /// <summary>Map a logical application pointer into the independent World output pixel grid.</summary>
    public bool TryToOutput(in ClientPixelPoint applicationPoint, out ClientPixelPoint outputPoint)
    {
        if (!TryToFrameLocal(applicationPoint, out ClientPixelPoint local))
        {
            outputPoint = default;
            return false;
        }
        outputPoint = new ClientPixelPoint(
            (int)((long)local.X * SelectedOutput.Width / Frame.Width),
            (int)((long)local.Y * SelectedOutput.Height / Frame.Height));
        return true;
    }

    public static GameplayFrameLayout Fill(in ClientPixelSize application, in ClientPixelSize output) =>
        Custom(application, output, application);

    public static GameplayFrameLayout Custom(
        in ClientPixelSize application, in ClientPixelSize output, in ClientPixelSize frameSize)
    {
        if (!application.IsValid || !output.IsValid || !frameSize.IsValid ||
            frameSize.Width > application.Width || frameSize.Height > application.Height)
            throw new ArgumentException("The frame, output and application must be finite and the frame must fit.");
        int left = (application.Width - frameSize.Width) / 2;
        int top = (application.Height - frameSize.Height) / 2;
        var frame = new ClientPixelRect(left, top, frameSize.Width, frameSize.Height);
        return new GameplayFrameLayout(application, output, frame, left, top,
            application.Width - frame.Right, application.Height - frame.Bottom);
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

    /// <summary>
    /// Largest centered integer frame with exactly the output's reduced aspect. Unlike the
    /// historical rounded MaxFit helper, this is safe for a non-stretched native output texture.
    /// </summary>
    public static GameplayFrameLayout MaxFitExact(
        in ClientPixelSize application, in ClientPixelSize selectedOutput)
    {
        if (!application.IsValid || !selectedOutput.IsValid)
            throw new ArgumentException("A finite application and output are required.");
        int width = selectedOutput.Width;
        int height = selectedOutput.Height;
        while (height != 0)
        {
            int remainder = width % height;
            width = height;
            height = remainder;
        }
        int reducedWidth = selectedOutput.Width / width;
        int reducedHeight = selectedOutput.Height / width;
        int multiplier = Math.Min(application.Width / reducedWidth,
            application.Height / reducedHeight);
        if (multiplier == 0)
            throw new ArgumentException("The application cannot fit one exact output-aspect pixel unit.");
        return Custom(application, selectedOutput,
            ClientPixelSize.Create(reducedWidth * multiplier, reducedHeight * multiplier));
    }
}
