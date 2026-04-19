# Persistence Extension Placeholder

Persistence is a second-wave import for the new ECS kernel.

The core kernel intentionally does not carry the old save/load implementation during Phase 1.

Expected future work:

- entity snapshot traversal,
- binary or other persistence adapters,
- system-owned state persistence,
- type registry and versioned component serialization.
