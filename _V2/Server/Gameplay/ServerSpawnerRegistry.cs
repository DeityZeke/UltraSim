namespace UltraSim.Server.Gameplay;

public sealed class ServerSpawnerRegistry
{
    private readonly Dictionary<ulong, ServerSpawnerDescriptor> _byHash = new();
    private readonly Dictionary<string, ulong> _hashByKey = new(StringComparer.Ordinal);

    public int Count => _byHash.Count;

    public bool TryRegister(ServerSpawnerDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        if (_hashByKey.ContainsKey(descriptor.SpawnSetKey))
            return false;

        if (_byHash.TryGetValue(descriptor.SpawnSetKeyHash, out ServerSpawnerDescriptor? existing) &&
            !string.Equals(existing.SpawnSetKey, descriptor.SpawnSetKey, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Spawn set key hash collision between '{existing.SpawnSetKey}' and '{descriptor.SpawnSetKey}'.");
        }

        _byHash.Add(descriptor.SpawnSetKeyHash, descriptor);
        _hashByKey.Add(descriptor.SpawnSetKey, descriptor.SpawnSetKeyHash);
        return true;
    }

    public void RegisterOrReplace(ServerSpawnerDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        if (_byHash.TryGetValue(descriptor.SpawnSetKeyHash, out ServerSpawnerDescriptor? existing) &&
            !string.Equals(existing.SpawnSetKey, descriptor.SpawnSetKey, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Spawn set key hash collision between '{existing.SpawnSetKey}' and '{descriptor.SpawnSetKey}'.");
        }

        if (_hashByKey.TryGetValue(descriptor.SpawnSetKey, out ulong previousHash) &&
            previousHash != descriptor.SpawnSetKeyHash)
        {
            _byHash.Remove(previousHash);
        }

        _byHash[descriptor.SpawnSetKeyHash] = descriptor;
        _hashByKey[descriptor.SpawnSetKey] = descriptor.SpawnSetKeyHash;
    }

    public bool Remove(string spawnSetKey)
    {
        string normalized = ServerSpawnerKey.Normalize(spawnSetKey);
        if (!_hashByKey.Remove(normalized, out ulong hash))
            return false;

        _byHash.Remove(hash);
        return true;
    }

    public bool TryGetByKey(string spawnSetKey, out ServerSpawnerDescriptor descriptor)
    {
        string normalized = ServerSpawnerKey.Normalize(spawnSetKey);
        if (_hashByKey.TryGetValue(normalized, out ulong hash))
            return _byHash.TryGetValue(hash, out descriptor!);

        descriptor = default!;
        return false;
    }

    public bool TryGetByHash(ulong spawnSetKeyHash, out ServerSpawnerDescriptor descriptor)
        => _byHash.TryGetValue(spawnSetKeyHash, out descriptor!);

    public IReadOnlyList<ServerSpawnerDescriptor> Snapshot(bool adminVisibleOnly = false)
    {
        IEnumerable<ServerSpawnerDescriptor> query = _byHash.Values;
        if (adminVisibleOnly)
            query = query.Where(static descriptor => descriptor.AdminVisible);

        return query
            .OrderBy(static descriptor => descriptor.SpawnSetKey, StringComparer.Ordinal)
            .ToArray();
    }
}
