namespace UltraSim.ECS;

public enum SystemControlKind : byte
{
    Toggle = 0,
    Integer = 1,
    Float = 2,
    Enum = 3,
    Action = 4,
    Text = 5,
}
