namespace UltraSim.Client;

public sealed class ClientMovementCorrectionStats
{
    private long _sampleCount;
    private double _positionErrorSum;
    private double _maxPositionError;
    private double _velocityErrorSum;
    private double _maxVelocityError;

    public void Record(double positionError, double velocityError)
    {
        _sampleCount++;
        _positionErrorSum += positionError;
        _velocityErrorSum += velocityError;
        if (positionError > _maxPositionError)
            _maxPositionError = positionError;
        if (velocityError > _maxVelocityError)
            _maxVelocityError = velocityError;
    }

    public ClientMovementCorrectionSnapshot Snapshot()
    {
        long count = _sampleCount;
        return new ClientMovementCorrectionSnapshot(
            SampleCount: count,
            AveragePositionError: count == 0 ? 0d : _positionErrorSum / count,
            MaxPositionError: _maxPositionError,
            AverageVelocityError: count == 0 ? 0d : _velocityErrorSum / count,
            MaxVelocityError: _maxVelocityError);
    }

    public void Clear()
    {
        _sampleCount = 0;
        _positionErrorSum = 0d;
        _maxPositionError = 0d;
        _velocityErrorSum = 0d;
        _maxVelocityError = 0d;
    }
}
