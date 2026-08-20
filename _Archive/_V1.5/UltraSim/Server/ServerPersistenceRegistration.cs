using System;
using UltraSim.ECS;
using UltraSim.Server.Engines.Accounting;
using UltraSim.Shared.Gameplay;

namespace UltraSim.Server
{
    public static class ServerPersistenceRegistration
    {
        public static void EnsureComponentsRegistered()
        {
            EnsureComponentRegistered<PlayerMarker>();
            EnsureComponentRegistered<PlayerState>();
            EnsureComponentRegistered<PrefabIdComponent>();
            // Keep the persistence-only components in a stable order after the core player runtime
            // components so test/runtime registration paths do not drift.
            EnsureComponentRegistered<PersistentTag>();
            EnsureComponentRegistered<PlayerNameComponent>();
            EnsureComponentRegistered<PlayerCharacterComponent>();
        }

        public static void RegisterPersistentTypes(WorldPersistenceCoordinator coordinator)
        {
            ArgumentNullException.ThrowIfNull(coordinator);

            coordinator.RegisterComponent<PlayerMarker>();
            coordinator.RegisterComponent<PlayerState>();
            coordinator.RegisterComponent<PrefabIdComponent>();
            coordinator.RegisterComponent<PersistentTag>();
            coordinator.RegisterComponent<PlayerNameComponent>();
            coordinator.RegisterComponent<PlayerCharacterComponent>();
        }

        private static void EnsureComponentRegistered<T>() where T : unmanaged, IComponent<T>
        {
            if (ComponentManager.TryGetId(typeof(T), out _))
                return;

            if (ComponentManager.IsSealed)
            {
                throw new InvalidOperationException(
                    $"Persistent component {typeof(T).Name} is not registered but ComponentManager is already sealed.");
            }

            ComponentManager.Register<T>();
        }
    }
}
