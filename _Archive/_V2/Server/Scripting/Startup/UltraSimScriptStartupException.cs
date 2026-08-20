namespace UltraSim.Server.Scripting;

public sealed class UltraSimScriptStartupException : Exception
{
    public UltraSimScriptStartupException(UltraSimScriptStartupResult result)
        : base(BuildMessage(result))
    {
        Result = result;
    }

    public UltraSimScriptStartupResult Result { get; }

    private static string BuildMessage(UltraSimScriptStartupResult result)
    {
        if (!result.CompileResult.Succeeded)
            return $"Script compilation failed with {result.ErrorCount} errors and {result.WarningCount} warnings.";

        if (result.LoadResult is { Succeeded: false } load)
            return $"Script module startup failed: {string.Join("; ", load.Errors)}";

        return "Script startup failed.";
    }
}
