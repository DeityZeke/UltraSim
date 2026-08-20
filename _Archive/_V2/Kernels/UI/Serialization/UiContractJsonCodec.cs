using System.Text.Json;
using System.Text.Json.Serialization;

namespace UltraSim.UI;

public static class UiContractJsonCodec
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() }
    };

    public static byte[] Encode<T>(T value)
    {
        return JsonSerializer.SerializeToUtf8Bytes(value, Options);
    }

    public static T Decode<T>(ReadOnlySpan<byte> bytes)
    {
        T? value = JsonSerializer.Deserialize<T>(bytes, Options);
        if (value is null)
        {
            throw new InvalidOperationException($"Unable to decode UI contract type {typeof(T).Name}.");
        }

        return value;
    }
}
