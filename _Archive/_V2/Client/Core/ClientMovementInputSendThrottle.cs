namespace UltraSim.Client;

public sealed class ClientMovementInputSendThrottle
{
    public const double DefaultHeartbeatSeconds = 0.06d;

    private readonly double _heartbeatSeconds;
    private readonly byte _immediateFlagMask;
    private ClientMovementInput _lastSentInput;
    private bool _hasLastSentInput;
    private double _secondsSinceLastSend;

    public ClientMovementInputSendThrottle(
        double heartbeatSeconds = DefaultHeartbeatSeconds,
        byte immediateFlagMask = 0)
    {
        if (double.IsNaN(heartbeatSeconds) || double.IsInfinity(heartbeatSeconds) || heartbeatSeconds <= 0.0d)
            throw new ArgumentOutOfRangeException(nameof(heartbeatSeconds));

        _heartbeatSeconds = heartbeatSeconds;
        _immediateFlagMask = immediateFlagMask;
    }

    public double HeartbeatSeconds => _heartbeatSeconds;

    public void Reset()
    {
        _lastSentInput = default;
        _hasLastSentInput = false;
        _secondsSinceLastSend = 0.0d;
    }

    public bool ShouldSend(in ClientMovementInput input, double elapsedSeconds)
    {
        if (double.IsNaN(elapsedSeconds) || double.IsInfinity(elapsedSeconds) || elapsedSeconds < 0.0d)
            throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));

        _secondsSinceLastSend += elapsedSeconds;

        if ((input.Flags & _immediateFlagMask) != 0)
            return MarkSent(input);

        if (!_hasLastSentInput)
            return HasHeldIntent(input) && MarkSent(input);

        if (!input.Equals(_lastSentInput))
            return MarkSent(input);

        if (!HasHeldIntent(input))
            return false;

        return _secondsSinceLastSend >= _heartbeatSeconds && MarkSent(input);
    }

    private bool MarkSent(in ClientMovementInput input)
    {
        _lastSentInput = input;
        _hasLastSentInput = true;
        _secondsSinceLastSend = 0.0d;
        return true;
    }

    private static bool HasHeldIntent(in ClientMovementInput input)
    {
        return input.MoveX != 0 || input.MoveY != 0 || input.Flags != 0;
    }
}
