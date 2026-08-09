using UltraSim.ECS;

namespace UltraSim.ECS.V2ComparisonAdapter;

internal readonly record struct Position(int X, int Y) : IComponent<Position>;

internal readonly record struct Velocity(int X, int Y) : IComponent<Velocity>;

internal readonly record struct Health(int Value) : IComponent<Health>;

internal readonly record struct Scalar(int Value) : IComponent<Scalar>;
