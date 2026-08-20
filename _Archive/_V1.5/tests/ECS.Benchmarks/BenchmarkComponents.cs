using ECS;

namespace ECS.Benchmarks
{
    // Minimal 2D position — 8 bytes, unmanaged
    public struct Position : IComponent<Position>
    {
        public float X;
        public float Y;
    }

    // Velocity — 8 bytes
    public struct Velocity : IComponent<Velocity>
    {
        public float Vx;
        public float Vy;
    }

    // Health — 4 bytes
    public struct Health : IComponent<Health>
    {
        public float Value;
    }

    // Tag component — 0 data bytes (zero-size struct in C# is 1 byte but logically empty)
    public struct Active : IComponent<Active>
    {
        public byte Flag; // 1 byte
    }
}
