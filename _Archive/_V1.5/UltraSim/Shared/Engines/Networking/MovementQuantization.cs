using System;

namespace UltraSim.Shared.Engines.Networking
{
    public static class MovementQuantization
    {
        private const float TwoPi = MathF.PI * 2.0f;

        public static bool TryQuantizePosition(float meters, out int quantized)
        {
            double scaled = Math.Round(meters * MovementProtocolConstants.PositionQuantizationScale, MidpointRounding.AwayFromZero);
            if (scaled < int.MinValue || scaled > int.MaxValue)
            {
                quantized = 0;
                return false;
            }

            quantized = (int)scaled;
            return true;
        }

        public static float DequantizePosition(int quantized) =>
            quantized / MovementProtocolConstants.PositionQuantizationScale;

        public static short QuantizeDirection(float normalized)
        {
            float clamped = Math.Clamp(normalized, -1.0f, 1.0f);
            int quantized = (int)MathF.Round(clamped * MovementProtocolConstants.InputDirectionQuantizationScale);
            return (short)Math.Clamp(quantized, short.MinValue, short.MaxValue);
        }

        public static float DequantizeDirection(short quantized)
        {
            float value = quantized / MovementProtocolConstants.InputDirectionQuantizationScale;
            return Math.Clamp(value, -1.0f, 1.0f);
        }

        public static ushort QuantizeYaw(float radians)
        {
            float wrapped = WrapRadians(radians);
            float alpha = wrapped / TwoPi;
            int quantized = (int)MathF.Round(alpha * ushort.MaxValue);
            return (ushort)Math.Clamp(quantized, ushort.MinValue, ushort.MaxValue);
        }

        public static float DequantizeYaw(ushort quantized)
        {
            float alpha = quantized / (float)ushort.MaxValue;
            return alpha * TwoPi;
        }

        private static float WrapRadians(float radians)
        {
            float wrapped = radians % TwoPi;
            if (wrapped < 0.0f)
                wrapped += TwoPi;

            return wrapped;
        }
    }
}
