namespace UltraSim.Server.Scripting;

public interface IUltraSimScriptPrefabDefinition
{
    string Key { get; }
    string SourceName { get; }
    PrefabDescriptor BuildDescriptor();
}
