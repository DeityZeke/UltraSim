# UltraSim AIOS Project Memory

This directory is the durable editorial memory/navigation layer for UltraSim.

It does **not** replace source code, tests, `Docs/`, module-local READMEs, benchmark evidence, or the repository's existing documentation layout. Existing documents remain where they are.

The purpose of AIOS is to make project knowledge discoverable across chats/agents and to keep relevant side discussions from disappearing simply because they never became a major implementation plan.

## Operating rule

> **Capture liberally; consolidate conservatively.**

Project-relevant ideas should be captured even when they are small, tentative, or parked. Settled design should point to a canonical reference. Chat transcripts themselves are not the memory store.

## Read order for substantial UltraSim work

1. Read [INDEX.md](INDEX.md).
2. Read [CURRENT_STATE.md](CURRENT_STATE.md) when current project/design state matters.
3. Use [TOPICS.md](TOPICS.md) to discover relevant side topics and cross-links.
4. Follow links to existing canonical documents, module READMEs, or topic notes.
5. Use [REPOSITORY_DOCS.md](REPOSITORY_DOCS.md) when the relevant document is not obvious.

## Status vocabulary

- **SEED** — mentioned and worth retaining.
- **EXPLORING** — actively developed but unsettled.
- **TENTATIVE** — current preferred direction.
- **CANONICAL** — settled enough to build/design against.
- **PARKED** — deliberately deferred.
- **SUPERSEDED** — retained only for historical context.

## Where new documentation goes

For future design discussions created through project/chat work:

- `AIOS/topics/` — focused topic notes, including side topics.
- `AIOS/open/` — parked ideas and unresolved cross-system questions.
- `AIOS/canonical/` — navigation/promotion layer for settled AIOS material.

Implementation-local documentation may still belong beside the code it documents. Existing documentation is not being relocated merely to satisfy this layout.

## Authority

The repository already states the correct authority rule: source/tests are executable truth; public API comments define contracts; documentation explains accepted architecture/design.

AIOS is therefore a **map and durable design-memory layer**, not an authority above implementation truth.
