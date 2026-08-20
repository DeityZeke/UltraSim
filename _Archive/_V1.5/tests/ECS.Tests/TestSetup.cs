using UltraSim.ECS;

namespace ECS.Tests
{
    /// <summary>
    /// Helpers for resetting static ECS registries between tests.
    /// Call <see cref="ResetStatics"/> in each test class constructor or in an
    /// <c>IAsyncLifetime.InitializeAsync</c> implementation.
    /// </summary>
    public static class TestRuntime
    {
        /// <summary>
        /// Clears all static ECS registries so tests start from a clean state.
        /// Must be called before each test that registers components or systems.
        /// </summary>
        public static void ResetStatics()
        {
            ComponentManager.Clear();
            PrefabRegistry.Clear();
        }
    }
}
