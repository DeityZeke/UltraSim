namespace UltraSim.Network;

/// <summary>
/// Logical session identifier owned by the control/session layer.
/// This is not baked into the base packet envelope.
/// </summary>
public readonly record struct SessionId(uint Value)
{
    public static readonly SessionId Invalid = new(0);

    public bool IsValid => Value != 0;

    public override string ToString() => Value.ToString();
}
