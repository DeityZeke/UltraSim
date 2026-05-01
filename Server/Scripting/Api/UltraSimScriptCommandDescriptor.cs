namespace UltraSim.Server.Scripting;

public sealed record UltraSimScriptCommandDescriptor(
    string Name,
    string Prefix,
    int MinAccessLevel,
    string HelpText,
    Action<UltraSimScriptCommandContext> Handler,
    IReadOnlyList<string> Aliases);
