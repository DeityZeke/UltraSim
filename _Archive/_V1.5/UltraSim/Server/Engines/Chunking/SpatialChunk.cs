using System.Collections.Generic;

namespace UltraSim.Server.Engines.Chunking
{
    public sealed class SpatialChunk
    {
        private readonly HashSet<ulong> _members = new HashSet<ulong>();

        public IReadOnlyCollection<ulong> Members => _members;

        public int Version { get; private set; }

        public bool MembershipDirty { get; private set; }

        public bool Add(ulong networkEntityId)
        {
            if (!_members.Add(networkEntityId))
                return false;

            Version++;
            MembershipDirty = true;
            return true;
        }

        public bool Remove(ulong networkEntityId)
        {
            if (!_members.Remove(networkEntityId))
                return false;

            Version++;
            MembershipDirty = true;
            return true;
        }

        public void ClearDirty() => MembershipDirty = false;

        internal int MemberCount => _members.Count;
    }
}
