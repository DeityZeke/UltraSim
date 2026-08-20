# ECS TODO / Ideas

- **Render chunk refactor**  
  - Track render chunks by spatial chunk `Entity.Packed` instead of copying `ChunkData`.  
  - Render systems should only cache GPU state (MultiMesh handles, LOD flags) and respond to `ChunkOwner` / `DirtyChunkTag` component updates.  
  - Remove the remaining direct component mutations from client render code.

- **Component transfer profiling**  
  - Confirm (via BenchmarkDotNet or allocations view) that archetype transfers stay allocation-free after recent builder/threading work.  
  - If issues appear, introduce cached copy delegates per component type to avoid repeated `TryGetIndex`/`is` checks.

- **Manual thread pool adoption**  
  - Systems that still call `Parallel.For` directly (e.g., OptimizedMovementSystem) should be evaluated and, if possible, switched to the shared `ManualThreadPool` for consistency.

- **System batching enhancements**  
  - We now cache read/write lookups per system; future work could cache conflict results or bitmasks to make `ComputeBatches` O(1) per system even when the dependency graph changes frequently.

- **Diagnostics / safety nets**  
  - Add optional logging/asserts when entity builders are dropped without being enqueued, so pooled component holders don’t leak.  
  - Consider scheduler-level timing (stopwatch per system) to centralize statistics gathering rather than relying on each system calling into `SystemStatistics`.
