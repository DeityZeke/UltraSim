namespace UltraSim.Server.Scripting;

public enum UltraSimRuntimeContentGroupKind : byte
{
    Package = 0,
    ScriptPackage = 1,
    PrefabPackage = 2,
    SpawnPackage = 3,
    AssetPackage = 4,
    Custom = 255,
}
