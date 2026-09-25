using UltraSim.Network;

namespace UltraSim.Game.Protocol;

/// <summary>
/// Finite semantic ownership families for cold traffic reporting. This mapping is
/// not a wire field and cannot alter protocol scheduling or encoding.
/// </summary>
public enum GameProtocolTrafficFamily : byte
{
    AuthenticationAndApplicationSession = 0,
    ClientProfile = 1,
    ContentAuthorizationAndProof = 2,
    GuestAdmission = 3,
    PlayableInputAndCommands = 4,
    OwnerPrivateState = 5,
    StructuralAndPublicWorld = 6,
    PublicPresentation = 7,
    ApplicationEvent = 8,
    Chat = 9,
    UserInterfaceAndServerMonitor = 10,
    ServerPerformanceProfile = 11,
    PresentationAuthorization = 12,
}

public readonly record struct GameProtocolTrafficFamilySnapshot(
    GameProtocolTrafficFamily Family,
    NetworkLogicalTrafficSnapshot Traffic);

/// <summary>Exhaustive allocation-free classification of the current Game manifest.</summary>
public static class GameProtocolTrafficClassification
{
    public const int FamilyCount = 13;

    public static bool TryClassify(
        NetworkMessageId message,
        out GameProtocolTrafficFamily family)
    {
        int classified = message.Value switch
        {
            100 or 101 or 105 or 108 or 134 =>
                (int)GameProtocolTrafficFamily.AuthenticationAndApplicationSession,
            142 or 143 => (int)GameProtocolTrafficFamily.ClientProfile,
            146 or 147 => (int)GameProtocolTrafficFamily.ServerPerformanceProfile,
            140 or 141 => (int)GameProtocolTrafficFamily.ContentAuthorizationAndProof,
            149 => (int)GameProtocolTrafficFamily.PresentationAuthorization,
            135 or 136 or 137 => (int)GameProtocolTrafficFamily.GuestAdmission,
            106 or 138 => (int)GameProtocolTrafficFamily.PlayableInputAndCommands,
            107 or 139 => (int)GameProtocolTrafficFamily.OwnerPrivateState,
            >= 109 and <= 114 or >= 117 and <= 130 or 148 or >= 150 and <= 154 =>
                (int)GameProtocolTrafficFamily.StructuralAndPublicWorld,
            144 or 145 => (int)GameProtocolTrafficFamily.PublicPresentation,
            115 or 116 => (int)GameProtocolTrafficFamily.ApplicationEvent,
            131 or 132 or 133 => (int)GameProtocolTrafficFamily.Chat,
            102 or 103 or 104 =>
                (int)GameProtocolTrafficFamily.UserInterfaceAndServerMonitor,
            _ => -1,
        };
        family = classified >= 0 ? (GameProtocolTrafficFamily)classified : default;
        return classified >= 0;
    }

    /// <summary>
    /// Aggregates one cold Network snapshot into the finite Game families. The
    /// destination must contain at least <see cref="FamilyCount"/> entries.
    /// </summary>
    public static void Aggregate(
        NetworkTrafficSnapshot traffic,
        Span<GameProtocolTrafficFamilySnapshot> destination)
    {
        ArgumentNullException.ThrowIfNull(traffic);
        if (destination.Length < FamilyCount)
            throw new ArgumentException("The Game traffic-family destination is too small.",
                nameof(destination));
        for (int index = 0; index < FamilyCount; index++)
            destination[index] = new((GameProtocolTrafficFamily)index, default);
        for (int index = 0; index < traffic.MessageCount; index++)
        {
            NetworkMessageTrafficSnapshot message = traffic.GetMessage(index);
            if (!TryClassify(message.MessageId, out GameProtocolTrafficFamily family))
                throw new InvalidOperationException(
                    $"Network message {message.MessageId.Value} has no Game traffic family.");
            int slot = (int)family;
            destination[slot] = destination[slot] with
            {
                Traffic = destination[slot].Traffic.Add(message.Traffic),
            };
        }
    }

    public static bool IsComplete(NetworkProtocolManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        foreach (NetworkMessageDescriptor descriptor in manifest.Messages)
            if (!TryClassify(descriptor.Id, out _)) return false;
        return true;
    }
}
