namespace UltraSim.Network;

/// <summary>
/// Stable connection identifier inside the network runtime.
/// This is intentionally separate from session/account/gameplay identity.
/// </summary>
public readonly record struct ConnectionId(int Value)
{
    public static readonly ConnectionId Invalid = new(0);

    public bool IsValid => Value > 0;

    public override string ToString() => Value.ToString();
}
