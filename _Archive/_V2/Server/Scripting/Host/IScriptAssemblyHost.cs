namespace UltraSim.Server.Scripting;

public interface IScriptAssemblyHost
{
    UltraSimScriptLoadResult LoadConfigureAndInitialize(string assemblyPath, IUltraSimScriptContext context);
}
