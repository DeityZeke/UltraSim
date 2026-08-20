using System.Text.Json;
using UltraSim.Network;

namespace UltraSim.Server.Network;

public static class ServerRuntimeNetworkPayloadCodec
{
    private static readonly JsonSerializerOptions s_options = new(JsonSerializerDefaults.Web);

    public static ImmutablePayloadBuffer Encode<T>(T value)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(value, s_options);
        return ImmutablePayloadBuffer.FromOwned(bytes);
    }

    public static T Decode<T>(ImmutablePayloadBuffer payload)
    {
        return JsonSerializer.Deserialize<T>(payload.Span, s_options)
            ?? throw new InvalidOperationException($"Failed to decode {typeof(T).Name} payload.");
    }
}
