namespace UltraSim.ECS
{
    /// <summary>
    /// Optional high-performance prefab contract:
    /// - template data is copied once and cloned per spawn
    /// - per-instance mutation is applied after copy
    /// </summary>
    public interface ITemplatePrefab<TSelf> : IPrefab<TSelf>
        where TSelf : unmanaged, ITemplatePrefab<TSelf>
    {
        bool HasPerInstanceData { get; }
        void ApplyTemplateTo(Archetype archetype, int slot);
        void ApplyInstanceTo(Archetype archetype, int slot);
    }
}
