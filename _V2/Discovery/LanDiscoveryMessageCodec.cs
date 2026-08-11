using System.Text.Json;

namespace UltraSim.Discovery;

public static class LanDiscoveryMessageCodec
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static byte[] EncodeRequest(LanDiscoveryRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return JsonSerializer.SerializeToUtf8Bytes(request, JsonOptions);
    }

    public static byte[] EncodeResponse(LanDiscoveryResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        return JsonSerializer.SerializeToUtf8Bytes(response, JsonOptions);
    }

    public static bool TryDecodeRequest(ReadOnlySpan<byte> bytes, out LanDiscoveryRequest request)
    {
        request = null!;
        try
        {
            LanDiscoveryRequest? parsed = JsonSerializer.Deserialize<LanDiscoveryRequest>(bytes, JsonOptions);
            if (parsed is null ||
                parsed.Magic != LanDiscoveryDefaults.RequestMagic ||
                parsed.ProtocolVersion != LanDiscoveryDefaults.ProtocolVersion ||
                string.IsNullOrWhiteSpace(parsed.Nonce))
            {
                return false;
            }

            request = parsed;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static bool TryDecodeResponse(ReadOnlySpan<byte> bytes, out LanDiscoveryResponse response)
    {
        response = null!;
        try
        {
            LanDiscoveryResponse? parsed = JsonSerializer.Deserialize<LanDiscoveryResponse>(bytes, JsonOptions);
            if (parsed is null ||
                parsed.Magic != LanDiscoveryDefaults.ResponseMagic ||
                parsed.ProtocolVersion != LanDiscoveryDefaults.ProtocolVersion ||
                string.IsNullOrWhiteSpace(parsed.Nonce))
            {
                return false;
            }

            response = parsed;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
