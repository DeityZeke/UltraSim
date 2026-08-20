namespace UltraSim.Server.Scripting;

public sealed record UltraSimScriptLoadResult(
    UltraSimScriptLoadResultKind Kind,
    int ModuleCount,
    IReadOnlyList<string> Errors)
{
    public bool Succeeded => Kind == UltraSimScriptLoadResultKind.Loaded;

    public static UltraSimScriptLoadResult Loaded(int moduleCount) =>
        new(UltraSimScriptLoadResultKind.Loaded, moduleCount, Array.Empty<string>());

    public static UltraSimScriptLoadResult Failed(IReadOnlyList<string> errors) =>
        new(UltraSimScriptLoadResultKind.Failed, 0, errors);
}
