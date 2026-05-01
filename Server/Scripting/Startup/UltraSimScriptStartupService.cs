namespace UltraSim.Server.Scripting;

public sealed class UltraSimScriptStartupService
{
    private readonly UltraSimScriptCompiler _compiler;
    private readonly IScriptAssemblyHost _assemblyHost;

    public UltraSimScriptStartupService(
        UltraSimScriptCompiler? compiler = null,
        IScriptAssemblyHost? assemblyHost = null)
    {
        _compiler = compiler ?? new UltraSimScriptCompiler();
        _assemblyHost = assemblyHost ?? new UltraSimScriptAssemblyHost();
    }

    public UltraSimScriptStartupResult Run(UltraSimScriptStartupOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        UltraSimRuntimeTemplate.Ensure(options.RuntimeRoot);

        var context = new DefaultUltraSimScriptContext(options.RuntimeRoot, options.ScriptApiVersion);
        UltraSimScriptCompileResult compile = _compiler.Compile(new UltraSimScriptCompilerOptions(
            options.RuntimeRoot,
            options.ScriptApiVersion,
            options.TreatWarningsAsErrors));

        if (!compile.Succeeded || compile.AssemblyPath is null)
            return new UltraSimScriptStartupResult(compile, LoadResult: null, context);

        UltraSimScriptLoadResult load = _assemblyHost.LoadConfigureAndInitialize(compile.AssemblyPath, context);
        return new UltraSimScriptStartupResult(compile, load, context);
    }
}
