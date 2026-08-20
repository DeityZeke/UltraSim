using System;
using System.Collections.Generic;

namespace UltraSim.ECS.Persistence
{
    /// <summary>
    /// Registry of all types participating in world persistence.
    /// All registrations must be done explicitly — no assembly scanning.
    /// Each type must carry a <see cref="PersistenceIdAttribute"/> with a unique, stable uint id.
    ///
    /// Registration is open until <see cref="Seal"/> is called. After sealing,
    /// <see cref="GetAllSorted"/> returns a stable deterministic snapshot (sorted by id ascending)
    /// suitable for writing the type database (<c>types.tdb</c>).
    /// </summary>
    public sealed class PersistenceTypeRegistry
    {
        private readonly Dictionary<uint, PersistenceTypeRecord> _byId   = new();
        private readonly Dictionary<Type, PersistenceTypeRecord> _byType = new();
        private bool _sealed;
        private PersistenceTypeRecord[]? _sealedRecords;

        public bool IsSealed => _sealed;
        public int Count     => _byId.Count;

        /// <summary>
        /// Registers <typeparamref name="T"/> with the given <paramref name="kind"/>.
        /// Idempotent: registering the same type twice returns the same record.
        /// Throws if the type lacks <see cref="PersistenceIdAttribute"/> or if the id collides
        /// with a different type.
        /// </summary>
        public PersistenceTypeRecord Register<T>(PersistenceKind kind)
            => Register(typeof(T), kind);

        public PersistenceTypeRecord Register(Type type, PersistenceKind kind)
        {
            ArgumentNullException.ThrowIfNull(type);

            if (_sealed)
                throw new InvalidOperationException("PersistenceTypeRegistry is sealed; no further registrations are allowed.");

            var attr = (PersistenceIdAttribute?)Attribute.GetCustomAttribute(type, typeof(PersistenceIdAttribute));
            if (attr is null)
                throw new InvalidOperationException(
                    $"Type '{type.FullName}' is missing [PersistenceId(...)] attribute. " +
                    "All persistent types must have an explicit, stable persistence id.");

            uint id = attr.Id;

            // Idempotent only when the classification matches the existing registration.
            if (_byType.TryGetValue(type, out PersistenceTypeRecord existing))
            {
                if (existing.Kind != kind)
                    throw new InvalidOperationException(
                        $"Type '{type.FullName}' is already registered as persistence kind '{existing.Kind}' " +
                        $"and cannot also be registered as '{kind}'.");

                return existing;
            }

            // Duplicate id from a *different* type is a hard error.
            if (_byId.TryGetValue(id, out PersistenceTypeRecord collision))
                throw new InvalidOperationException(
                    $"Duplicate persistence id {id}: already registered by '{collision.ClrType.FullName}'. " +
                    $"Cannot also register '{type.FullName}'. Each persistent type must have a unique [PersistenceId(...)].");

            var record = new PersistenceTypeRecord(id, kind, type);
            _byId[id]     = record;
            _byType[type] = record;
            return record;
        }

        public bool TryGetById(uint id, out PersistenceTypeRecord record)
            => _byId.TryGetValue(id, out record);

        public bool TryGetByType(Type type, out PersistenceTypeRecord record)
            => _byType.TryGetValue(type, out record);

        public bool TryGetByType<T>(out PersistenceTypeRecord record)
            => _byType.TryGetValue(typeof(T), out record);

        /// <summary>
        /// Returns all registered records sorted by <see cref="PersistenceTypeRecord.PersistenceId"/> ascending.
        /// The sort order is the canonical write order for <c>types.tdb</c>.
        /// Requires the registry to be sealed.
        /// </summary>
        public ReadOnlySpan<PersistenceTypeRecord> GetAllSorted()
        {
            if (!_sealed)
                throw new InvalidOperationException(
                    "PersistenceTypeRegistry must be sealed before accessing the sorted snapshot.");
            return _sealedRecords.AsSpan();
        }

        /// <summary>
        /// Seals the registry. No further registrations are permitted after this call.
        /// Computes the deterministic sorted snapshot used for <c>types.tdb</c> output.
        /// Calling Seal on an already-sealed registry is a no-op.
        /// </summary>
        public void Seal()
        {
            if (_sealed)
                return;

            var records = new PersistenceTypeRecord[_byId.Count];
            int i = 0;
            foreach (var kvp in _byId)
                records[i++] = kvp.Value;

            Array.Sort(records, static (a, b) => a.PersistenceId.CompareTo(b.PersistenceId));
            _sealedRecords = records;
            _sealed = true;
        }
    }
}
