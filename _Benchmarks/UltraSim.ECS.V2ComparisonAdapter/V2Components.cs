using UltraSim.ECS;

namespace UltraSim.ECS.V2ComparisonAdapter;

internal readonly record struct Position(int X, int Y) : IComponent<Position>;

internal readonly record struct Velocity(int X, int Y) : IComponent<Velocity>;

internal readonly record struct Health(int Value) : IComponent<Health>;

internal readonly record struct Scalar(int Value) : IComponent<Scalar>;

internal readonly record struct WideBlock64(
    long Value0,
    long Value1,
    long Value2,
    long Value3,
    long Value4,
    long Value5,
    long Value6,
    long Value7) : IComponent<WideBlock64>;
