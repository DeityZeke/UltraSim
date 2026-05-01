namespace UltraSim.Server.Scripting;

public interface IUltraSimScriptPrefabDefinition
{
    string Key { get; }
    PrefabDescriptor BuildDescriptor();
}
