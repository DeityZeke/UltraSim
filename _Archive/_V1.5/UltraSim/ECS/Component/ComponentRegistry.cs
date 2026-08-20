using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

namespace UltraSim.ECS
{
    public static class ComponentRegistry
    {
        private static readonly Dictionary<Type, ComponentId> _typeToId = new();
        private static Type[] _idToType = new Type[64];
        private static int _count;
        private static readonly object _lock = new();
        private static volatile bool _sealed;

        private static volatile Type[]? _sealedTypes;

        public static int Count
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Volatile.Read(ref _count);
        }

        public static int HighestId
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Volatile.Read(ref _count) - 1;
        }

        public static bool IsSealed
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _sealed;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ComponentId Register<T>() where T : unmanaged, IComponent<T>
            => RegisterInternal(typeof(T));

        public static ComponentId Register(Type type)
        {
            ArgumentNullException.ThrowIfNull(type);

            return RegisterInternal(type);
        }

        private static ComponentId RegisterInternal(Type type)
        {
            lock (_lock)
            {
                if (_sealed)
                    ThrowHelper.ThrowSealed("Component registry is sealed.");

                if (_typeToId.TryGetValue(type, out var existing))
                    return existing;

                ValidateComponentType(type);

                int id = _count;
                if (id >= _idToType.Length)
                {
                    int newLen = _idToType.Length * 2;
                    var newArray = new Type[newLen];
                    Array.Copy(_idToType, newArray, _count);
                    _idToType = newArray;
                }

                _idToType[id] = type;
                var componentId = new ComponentId(id);
                _typeToId[type] = componentId;
                Interlocked.Increment(ref _count);
                return componentId;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ComponentId GetId<T>() where T : unmanaged, IComponent<T>
            => GetIdFast(typeof(T));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ComponentId GetId(Type type)
        {
            ArgumentNullException.ThrowIfNull(type);

            return GetIdFast(type);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ComponentId GetIdFast(Type type)
        {
            if (_sealed)
            {
                if (_typeToId.TryGetValue(type, out var id))
                    return id;

                ThrowHelper.ThrowNotRegistered(type);
            }

            lock (_lock)
            {
                if (_typeToId.TryGetValue(type, out var id))
                    return id;
            }

            ThrowHelper.ThrowNotRegistered(type);
            return default;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryGetId(Type type, out ComponentId id)
        {
            if (type is null)
            {
                id = default;
                return false;
            }

            if (_sealed)
                return _typeToId.TryGetValue(type, out id);

            lock (_lock)
                return _typeToId.TryGetValue(type, out id);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Type GetType(ComponentId id)
        {
            int index = id.Value;

            if (_sealed)
            {
                var types = _sealedTypes!;
                if ((uint)index < (uint)types.Length)
                    return types[index];

                ThrowHelper.ThrowInvalidId(id);
            }

            lock (_lock)
            {
                if ((uint)index < (uint)_count)
                    return _idToType[index];
            }

            ThrowHelper.ThrowInvalidId(id);
            return null!;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryGetType(ComponentId id, out Type type)
        {
            int index = id.Value;

            if (_sealed)
            {
                var types = _sealedTypes!;
                if ((uint)index < (uint)types.Length)
                {
                    type = types[index];
                    return true;
                }

                type = null!;
                return false;
            }

            lock (_lock)
            {
                if ((uint)index < (uint)_count)
                {
                    type = _idToType[index];
                    return true;
                }
            }

            type = null!;
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ReadOnlySpan<Type> GetAllTypesSpan()
        {
            if (!_sealed)
                ThrowHelper.ThrowNotSealed("Component registry must be sealed before exposing a span.");

            return _sealedTypes.AsSpan();
        }

        public static Type[] GetAllTypesSnapshot()
        {
            if (_sealed)
            {
                var types = _sealedTypes!;
                var copy = new Type[types.Length];
                Array.Copy(types, copy, types.Length);
                return copy;
            }

            lock (_lock)
            {
                var copy = new Type[_count];
                Array.Copy(_idToType, copy, _count);
                return copy;
            }
        }

        public static void Seal()
        {
            lock (_lock)
            {
                if (_sealed)
                    return;

                var snapshot = new Type[_count];
                Array.Copy(_idToType, snapshot, _count);
                _sealedTypes = snapshot;
                _sealed = true;
            }
        }

        internal static void Clear()
        {
            lock (_lock)
            {
                _typeToId.Clear();
                Array.Clear(_idToType, 0, _count);
                _count = 0;
                _sealedTypes = null;
                _sealed = false;
            }
        }

        private static void ValidateComponentType(Type type)
        {
            if (!type.IsValueType)
                ThrowHelper.ThrowInvalidType("Component types must be structs.");

            if (!ImplementsIComponentOfSelf(type))
                ThrowHelper.ThrowInvalidType($"Type {type.FullName} does not implement IComponent<{type.Name}>.");
        }

        private static bool ImplementsIComponentOfSelf(Type type)
        {
            var interfaces = type.GetInterfaces();
            var iComponentDef = typeof(IComponent<>);

            for (int i = 0; i < interfaces.Length; i++)
            {
                var it = interfaces[i];
                if (!it.IsGenericType) continue;
                if (it.GetGenericTypeDefinition() != iComponentDef) continue;

                var args = it.GetGenericArguments();
                if (args.Length == 1 && args[0] == type) return true;
            }
            return false;
        }
    }

    internal static class ThrowHelper
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void ThrowArgumentNull(string paramName)
            => throw new ArgumentNullException(paramName);

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void ThrowSealed(string message)
            => throw new InvalidOperationException(message);

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void ThrowNotSealed(string message)
            => throw new InvalidOperationException(message);

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void ThrowNotRegistered(Type type)
            => throw new InvalidOperationException($"Component type {type.FullName} is not registered.");

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void ThrowInvalidId(ComponentId id)
            => throw new ArgumentOutOfRangeException("id", $"Invalid component id: {id.Value}");

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void ThrowInvalidType(string message)
            => throw new ArgumentException(message);

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void ThrowInvalidOperation(string message)
            => throw new InvalidOperationException(message);

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void ThrowOutOfRange(string paramName, string message)
            => throw new ArgumentOutOfRangeException(paramName, message);
    }
}
