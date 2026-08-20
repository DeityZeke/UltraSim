using System;
using System.Runtime.CompilerServices;

namespace UltraSim.ECS
{
    public static class ComponentManager
    {
        private static Func<int, IComponentColumn>?[] _columnFactories = new Func<int, IComponentColumn>?[64];
        private static int _factoryCount;
        private static readonly object _lock = new();
        private static volatile bool _sealed;

        private static volatile Func<int, IComponentColumn>?[]? _sealedFactories;

        public static int Count
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => ComponentRegistry.Count;
        }

        public static int HighestId
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => ComponentRegistry.HighestId;
        }

        public static bool IsSealed
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _sealed;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ComponentId Register<T>() where T : unmanaged, IComponent<T>
        {
            if (_sealed)
                ThrowHelper.ThrowSealed("Component manager is sealed.");

            var id = ComponentRegistry.Register<T>();
            RegisterFactoryInternal(id, static capacity => new ComponentColumn<T>(capacity));
            return id;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ComponentId GetId<T>() where T : unmanaged, IComponent<T>
            => ComponentRegistry.GetId<T>();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ComponentId GetId(Type type)
            => ComponentRegistry.GetId(type);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryGetId(Type type, out ComponentId id)
            => ComponentRegistry.TryGetId(type, out id);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Type GetType(ComponentId id)
            => ComponentRegistry.GetType(id);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryGetType(ComponentId id, out Type type)
            => ComponentRegistry.TryGetType(id, out type);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ReadOnlySpan<Type> GetAllTypesSpan()
            => ComponentRegistry.GetAllTypesSpan();

        public static Type[] GetAllTypesSnapshot()
            => ComponentRegistry.GetAllTypesSnapshot();

        internal static void RegisterFactory(ComponentId id, Func<int, IComponentColumn> factory)
        {
            ArgumentNullException.ThrowIfNull(factory);

            RegisterFactoryInternal(id, factory);
        }

        private static void RegisterFactoryInternal(ComponentId id, Func<int, IComponentColumn> factory)
        {
            int index = id.Value;
            if (index < 0)
                return;

            lock (_lock)
            {
                if (_sealed)
                    ThrowHelper.ThrowSealed("Component manager is sealed.");

                if ((uint)index < (uint)_columnFactories.Length && _columnFactories[index] is not null)
                    return;

                EnsureCapacity(index + 1);
                _columnFactories[index] = factory;
                if (index >= _factoryCount)
                    _factoryCount = index + 1;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static IComponentColumn CreateColumn(ComponentId id, int capacity)
        {
            int index = id.Value;

            if (_sealed)
            {
                var factories = _sealedFactories!;
                if ((uint)index < (uint)factories.Length)
                {
                    var factory = factories[index];
                    if (factory is not null)
                        return factory(capacity);
                }
                ThrowNoFactory(id);
            }

            lock (_lock)
            {
                if ((uint)index < (uint)_columnFactories.Length)
                {
                    var factory = _columnFactories[index];
                    if (factory is not null)
                        return factory(capacity);
                }
            }

            ThrowNoFactory(id);
            return null!;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ThrowNoFactory(ComponentId id)
            => throw new InvalidOperationException($"No column factory registered for component id {id}.");

        public static void Seal()
        {
            lock (_lock)
            {
                if (_sealed)
                    return;

                var snapshot = new Func<int, IComponentColumn>?[_factoryCount];
                Array.Copy(_columnFactories, snapshot, _factoryCount);
                _sealedFactories = snapshot;
                _sealed = true;
            }

            ComponentRegistry.Seal();
        }

        internal static void Clear()
        {
            lock (_lock)
            {
                Array.Clear(_columnFactories, 0, _factoryCount);
                _factoryCount = 0;
                _sealedFactories = null;
                _sealed = false;
            }

            ComponentRegistry.Clear();
        }

        private static void EnsureCapacity(int needed)
        {
            if (needed <= _columnFactories.Length)
                return;

            int newSize = Math.Max(_columnFactories.Length * 2, needed);
            var newArray = new Func<int, IComponentColumn>?[newSize];
            Array.Copy(_columnFactories, newArray, _factoryCount);
            _columnFactories = newArray;
        }
    }
}
