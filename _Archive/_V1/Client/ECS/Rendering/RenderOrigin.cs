#nullable enable

using Godot;

using UltraSim.ECS.Components;
using UltraSim.Server.ECS.Chunk;

namespace Client.ECS.Rendering
{
    /// <summary>
    /// Keeps track of the current render-space origin so players remain at (0,0,0)
    /// and the world shifts underneath them. World data stays in absolute coordinates;
    /// render systems convert to relative coordinates via this helper.
    /// </summary>
    public static class RenderOrigin
    {
        private static int _chunkSizeXZ = 64;
        private static int _chunkSizeY = 32;

        public static Vector3 WorldOrigin { get; private set; } = Vector3.Zero;
        public static ChunkLocation PlayerChunk { get; private set; } = new ChunkLocation(0, 0, 0);

        /// <summary>
        /// Called automatically during runtime bootstrapping (no parameters allowed).
        /// Ensures default chunk sizes are initialized even before a manager is assigned.
        /// </summary>
        public static void Configure()
        {
            _chunkSizeXZ = 64;
            _chunkSizeY = 32;
        }

        /// <summary>
        /// Called once the SimplifiedChunkManager is available so we can match its configuration.
        /// </summary>
        public static void ApplyChunkManager(SimplifiedChunkManager? chunkManager)
        {
            if (chunkManager == null)
                return;

            _chunkSizeXZ = chunkManager.ChunkSizeXZ;
            _chunkSizeY = chunkManager.ChunkSizeY;
        }

        public static void UpdateOrigin(Vector3 playerWorldPosition, ChunkLocation playerChunk)
        {
            WorldOrigin = playerWorldPosition;
            PlayerChunk = playerChunk;
        }

        public static Vector3 ToRenderSpace(Vector3 worldPosition) => worldPosition - WorldOrigin;

        public static Vector3 ChunkOriginToRenderSpace(ChunkLocation serverChunk)
        {
            var chunkWorldOrigin = new Vector3(
                serverChunk.X * _chunkSizeXZ,
                serverChunk.Y * _chunkSizeY,
                serverChunk.Z * _chunkSizeXZ);
            return chunkWorldOrigin - WorldOrigin;
        }
    }
}
