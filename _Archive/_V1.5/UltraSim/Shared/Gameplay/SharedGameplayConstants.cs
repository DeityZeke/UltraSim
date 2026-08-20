namespace UltraSim.Shared.Gameplay
{
    public static class SharedGameplayConstants
    {
        /// <summary>
        /// Width of one grid unit (tile) in world-space meters.
        /// Tiles are 0.5m × 0.5m; a chunk contains 32 × 32 tiles = 16m × 16m.
        /// </summary>
        public const float WorldUnitsPerGrid = 0.5f;
    }
}
