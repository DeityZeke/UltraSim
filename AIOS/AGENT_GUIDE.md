# UltraSim AIOS Agent / Contribution Guide

This guide defines how chats, coding agents, reviewers, and human contributors should read and maintain UltraSim's AIOS project memory.

The central purpose is to preserve project knowledge **without converting conversation into false authority**.

## 1. The prime rule

> **Discussion is not a decision.**

A statement appearing in chat, a review, a brainstorm, or an AI suggestion does not make it part of the implementation plan.

AIOS should remember useful ideas even when unsettled, but the status and destination must reflect what was actually agreed.

## 2. First-entry procedure

Before substantial UltraSim design or implementation work:

1. Read [INDEX.md](INDEX.md).
2. Read [CURRENT_STATE.md](CURRENT_STATE.md) if present-state decisions matter.
3. Search [TOPICS.md](TOPICS.md) for the subject being discussed.
4. Follow the linked canonical/topic documents relevant to that subject.
5. Use [REPOSITORY_DOCS.md](REPOSITORY_DOCS.md) if the relevant document is not obvious.
6. Treat repository source/tests and accepted implementation documents as higher authority than remembered conversation.

Do not preload the entire project memory into context. Read the map first, then retrieve only relevant material.

## 3. Evidence levels

When deciding what to record or promote, distinguish these cases.

### Mentioned / proposed

Examples:

- "Maybe we could..."
- "What if..."
- "Another possibility..."
- an assistant proposes an idea and the user has not accepted it.

Record as **SEED** or **EXPLORING** if worth preserving.

### Actively explored

The idea is being developed, compared, or stress-tested, but alternatives remain open.

Record as **EXPLORING**.

### Explicitly preferred

Examples:

- "I prefer this."
- "Let's go with that for now."
- "That's probably the direction."
- user and assistant converge on a current model but still leave material questions open.

Record as **TENTATIVE** unless the user explicitly treats it as settled/canonical.

### Explicitly accepted

Examples:

- "Agreed."
- "Yes, exactly."
- "That's settled."
- "Update the doc with that."
- "Use this as the reference."
- an existing repository document is already marked accepted/frozen/canonical.

This may justify **CANONICAL** status for the specific agreed material.

Acceptance applies only to what was actually agreed. Do not promote nearby speculative branches merely because they were discussed in the same conversation.

## 4. Never infer agreement from silence

Do **not** treat any of these as acceptance by themselves:

- the user did not object;
- the conversation moved on;
- the assistant described the idea confidently;
- several messages elaborated the idea;
- the idea appears elegant or likely correct;
- another model recommended it;
- it was included as one option among several.

If status is unclear, choose the lower status.

## 5. Lowest-justified-status rule

Always use the lowest status supported by evidence:

```text
SEED
    ↓
EXPLORING
    ↓
TENTATIVE
    ↓
CANONICAL
```

Promotion should be monotonic only when new evidence warrants it.

A topic may also become:

```text
PARKED
SUPERSEDED
```

without ever becoming canonical.

## 6. Capture vs. promotion

Use two separate operations conceptually:

### Capture

Preserve useful information so it cannot disappear.

Capture may happen for:

- side ideas;
- rejected-but-interesting alternatives;
- unresolved questions;
- future possibilities;
- tentative semantics.

Typical destinations:

- `AIOS/TOPICS.md`
- `AIOS/topics/*.md`
- `AIOS/open/*.md`

### Promotion

Move an accepted design into the source that future implementation/design should rely on.

Promotion may involve:

- marking a topic CANONICAL;
- updating a canonical AIOS note;
- updating an existing design/reference document;
- updating an implementation plan when the accepted change is implementation-relevant.

**Capture is liberal. Promotion is conservative.**

## 7. Implementation-plan safety rule

Do not add speculative or merely discussed ideas to implementation plans as requirements.

An implementation-facing document may be changed when one of these is true:

1. the user explicitly asks to update that plan/reference with the agreed design;
2. the relevant material is already CANONICAL and the change is synchronization/maintenance rather than a new decision;
3. an implementation task itself necessarily changes the accepted contract and the repository's normal engineering process records that change.

Otherwise, capture the idea in AIOS and link it to the implementation plan as related/open context if useful.

## 8. Canonical-doc safety rule

Before modifying a canonical document because of conversation:

1. identify the exact accepted decision;
2. separate accepted material from suggestions/options that remain unresolved;
3. update only the accepted portion;
4. preserve unresolved branches in a topic/open note;
5. update `TOPICS.md` status/link if needed.

If a new discussion conflicts with existing canonical material but no replacement was explicitly accepted:

> **Do not rewrite the canonical material.**

Record the conflict as EXPLORING/TENTATIVE and link both sides.

## 9. Topic-note format

A focused topic note should normally include:

```text
# Topic

Status: SEED | EXPLORING | TENTATIVE | CANONICAL | PARKED | SUPERSEDED

Primary references:
Related topics:

## Current understanding

## Agreed points
    only if actually agreed

## Ideas under exploration
    clearly non-canonical

## Open questions

## Superseded/rejected alternatives
    when historically useful
```

Do not force every small topic to contain every section.

## 10. Status belongs to claims, not merely files

A topic file can contain mixed certainty.

For example:

```text
Status: EXPLORING

Agreed:
    recipes should accept Material Families

Tentative:
    Silver is the preferred Mithril catalyst

Open:
    exact mechanical material properties
```

Do not label an entire file CANONICAL if substantial contents are still speculative.

If necessary, split stable and exploratory material.

## 11. Existing repository documents

AIOS is a navigation layer over the existing repo.

Do not move existing files just to make the tree look tidy.

Prefer:

- link to existing canonical docs;
- link to module-local READMEs;
- link to benchmark evidence;
- link to archived prior designs when historically relevant.

Create AIOS-native documents primarily for:

- cross-system design memory;
- side topics that would otherwise be lost;
- unresolved ideas;
- promotion/navigation metadata.

## 12. Conflict resolution order

When sources disagree, use this rough order:

1. current source/tests for executable behavior;
2. accepted/frozen current implementation contracts/plans;
3. current canonical design/reference docs;
4. CANONICAL AIOS material;
5. TENTATIVE AIOS material;
6. EXPLORING/SEED notes;
7. archived/superseded material.

If the conflict is material and not resolvable from status/recency, surface it rather than silently choosing.

## 13. Do not erase useful history

When a design changes:

- update the canonical/current source;
- mark the old AIOS topic SUPERSEDED if it is worth retaining;
- preserve a concise explanation of what replaced it;
- rely on Git history for line-by-line archaeology rather than duplicating old full documents unnecessarily.

## 14. Chat/session closeout behavior

After a substantive design conversation, consider whether it produced:

- a new topic worth capturing;
- a status change;
- an accepted canonical change;
- an unresolved question;
- a superseded idea.

Update AIOS accordingly when repository write access is available.

Do **not** create noise for trivial conversation or restate information already captured accurately.

## 15. Minimal-memory principle

AIOS should make future retrieval easier, not become another unbounded transcript.

Prefer:

> concise durable semantics + links to deeper references

over:

> chronological copies of everything everyone said.

## 16. Examples

### Example: speculative material

Conversation:

> "Maybe Platinum should be used for Mithril."

No agreement follows.

Correct:

```text
Mithril catalyst
Status: EXPLORING
Options: Silver, Platinum
```

Incorrect:

> Update the crafting implementation plan to require Platinum.

### Example: explicit convergence

Conversation:

> "Silver fits better."
> "Agreed, use Silver."

Correct:

- mark Silver as CANONICAL/TENTATIVE according to how strongly it was accepted;
- update the canonical material reference if the user asks for/document ownership warrants it;
- retain Platinum only as a superseded alternative if useful.

### Example: discussion continues without explicit acceptance

Assistant proposes three combat models; the user starts discussing one in detail but never says it is the chosen model.

Correct:

- EXPLORING;
- preserve alternatives/open questions.

Incorrect:

- CANONICAL because it received the most discussion.

## 17. One-sentence operating summary

> **Preserve what was discussed, label what was preferred, promote only what was agreed, and never let AIOS turn speculation into implementation authority.**
