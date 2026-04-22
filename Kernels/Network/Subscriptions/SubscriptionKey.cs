namespace UltraSim.Network;

public readonly record struct SubscriptionKey(ulong Value)
{
    public static SubscriptionKey Invalid => default;

    public bool IsValid => Value != 0;

    public override string ToString() => Value.ToString();
}
