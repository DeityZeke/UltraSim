# UltraSim AIOS Project Memory

This directory is the durable editorial memory/navigation layer for UltraSim.

It does **not** replace source code, tests, `Docs/`, module-local READMEs, benchmark evidence, or the repository's existing documentation layout. Existing documents remain where they are.

The purpose of AIOS is to make project knowledge discoverable across chats/agents and to keep relevant side discussions from disappearing simply because they never became a major implementation plan.

## Required contributor guidance

> **Before adding, promoting, or changing AIOS memory, read [AGENT_GUIDE.md](AGENT_GUIDE.md).**

The guide defines how to distinguish discussion from agreement, how statuses are assigned, when canonical/implementation-facing documents may be changed, and how to avoid silently promoting speculative ideas.

## Operating rule

> **Capture liberally; consolidate conservatively.**

Project-relevant ideas should be captured even when they are small, tentative, or parked. Settled design should point to a canonical reference. Chat transcripts themselves are not the memory store.

A second equally important rule is:

> **Discussion is not a decision. Preserve unsettled ideas without pretending they were agreed.**

## Read order for substantial UltraSim work

1. Read [AGENT_GUIDE.md](AGENT_GUIDE.md).
2. Read [INDEX.md](INDEX.md).
3. Read [CURRENT_STATE.md](CURRENT_STATE.md) when current project/design state matters.
4. Use [TOPICS.md](TOPICS.md) to discover relevant side topics and cross-links.
5. Follow links to existing canonical documents, module READMEs, or topic notes.
6. Use [REPOSITORY_DOCS.md](REPOSITORY_DOCS.md) when the relevant document is not obvious.

## Status vocabulary

- **SEED** — mentioned and worth retaining; no agreement implied.
- **EXPLORING** — actively discussed/developed but unresolved.
- **TENTATIVE** — an explicitly preferred/current direction, but not yet safe to treat as implementation authority unless the owning plan says otherwise.
- **CANONICAL** — explicitly accepted/settled enough to build or design against.
- **PARKED** — deliberately deferred.
- **SUPERSEDED** — retained only for historical context.

Use the **lowest status justified by the evidence**. Never silently promote an idea because it sounds good or because discussion continued without objection.

## Where new documentation goes

For future design discussions created through project/chat work:

- `AIOS/topics/` — focused topic notes, including side topics.
- `AIOS/open/` — parked ideas and unresolved cross-system questions.
- `AIOS/canonical/` — navigation/promotion layer for settled AIOS material.

Implementation-local documentation may still belong beside the code it documents. Existing documentation is not being relocated merely to satisfy this layout.

## Authority

The repository already states the correct authority rule: source/tests are executable truth; public API comments define contracts; documentation explains accepted architecture/design.

AIOS is therefore a **map and durable design-memory layer**, not an authority above implementation truth.

AIOS may preserve ideas that are not canonical. Their status must remain visible so later chats can recover them without confusing them for accepted implementation requirements.
