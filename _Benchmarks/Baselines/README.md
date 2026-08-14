# Accepted benchmark artifacts

This directory contains immutable machine-readable evidence for accepted ECS,
Runtime, persistence, S0, comparison, experiment, and stress checkpoints.

JSON filenames and embedded workload/version metadata identify each result. Git
history records when and why it was accepted. This README intentionally does not
repeat every artifact's numbers or revision narrative.

## Policy

- Never overwrite, rename, or silently regenerate an accepted artifact.
- New evidence receives a new descriptive filename and capture date.
- Preserve old JSON for longitudinal analysis even after its implementation
  checkpoint is historical.
- Do not treat a result as comparable unless its embedded semantic classification
  admits a ratio.
- Do not copy performance tables into multiple Markdown reports.
- Record cadence, playback/adaptation, capacities, workers/process order,
  allocation scope, runtime/hardware, sample counts, and canonical validation.

Unless a workload says otherwise, Runtime performance evidence uses fixed 60 Hz,
1x playback, variable cadence disabled, and adaptive cadence disabled.

## Current anchors

Useful current anchors can be located by their embedded workload or filename:

- `ecs-v2-v3-3.4.2-*` — current cross-generation ECS control;
- `ecs-v3-3.4.1-persistence-*` — current ECS persistence evidence;
- `s0.8-integrated-acceptance-cadence-independent-*` — accepted integrated
  Runtime/game/persistence/cadence gate;
- `network-n0.1-game-projection-100k-*` — canonical changed-state capture and
  packet-free OfflineDirect semantic-apply evidence;
- `network-n0.2-kernel-foundation-*` — fixed-runtime generated codec, packet pool,
  producer ring, deterministic datagram, and connection reuse evidence; the
  `hardened` artifact includes checked signed codecs and the versioned fingerprint;
- `network-n0.3-secure-protocol-*` — fixed-runtime secure/reliable loopback delivery,
  fragmentation, and impaired convergence evidence;
- `ecs-extreme-sphere-*` — extreme CPU/Godot scale evidence.

Earlier `3.0.x`, `3.1.x`, experiment, and soak files are intentionally retained
as historical measurements, not active design documentation.

See [benchmark methodology](../README.md) for execution and acceptance rules.
