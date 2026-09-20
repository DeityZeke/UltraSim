# Social Conversation, Claims, Overhearing, and Information Reveal

**Status:** CANONICAL design direction

Primary references:
- [Sim Core](../../Docs/Plans/Content/UltraSim_Sim_Core_Reasoning_Decision_Making_Reference_v4.md)
- [World History / Knowledge](../../Docs/Plans/Content/UltraSim_World_History_Knowledge_Pools_Reference_v2.md)
- [Project Shape](project-shape-and-player-experience.md)
- [Multiplayer Chat](../../Docs/Plans/CHAT.md)

## Core rule

NPC conversation is **Sims-like social simulation**, not a Bethesda-style dialogue tree and not a free-text AI interpreter.

The player chooses:

- who to interact with;
- social intent;
- topic/subject.

Examples:

```text
Talk
    Gossip
    Local Events
    Relationships
    Talk Shop
    Ask About...
    Brag
    Beg
    Flirt
    Impress
    Joke
    Warn
```

Most actual chatter remains abstract/simlish.

## Conversation is not information transfer

Keep these concepts distinct:

```text
Conversation
    social activity between actors

Topic
    query / social policy for what may come up

Claim Transfer
    a concrete structured piece of information communicated

Information Reveal
    player-facing presentation of a noteworthy received/overheard claim
```

A long conversation may improve relationships or familiarity without transmitting any noteworthy claim.

## Structured claims

When important information is communicated, the simulation should exchange structured semantics, not rely on generated prose as truth.

Conceptually:

```text
Claim
    Subject
    Predicate / Kind
    Value / Details
    Time / Age
    Source
    Confidence / Precision
```

Presentation may render that as:

> "I heard the baker has been sick for the past few days."

The words are presentation; the claim is simulation state.

## Topic-driven access

A topic changes query/communication policy.

Examples:

- Gossip favors recent, unusual, socially salient information.
- Talk Shop favors occupation/domain history.
- Warn favors safety, disease, route, or immediate hazard information.
- Brag favors speaker-involved accomplishments.
- Ask About narrows the semantic subject without becoming a line-by-line interrogation menu.

Knowledge access does not guarantee disclosure: personality, relationship, willingness, secrecy, role, and context can still prevent a claim from being shared.

## Overhearing

Conversation occurs physically in the world.

A communicated claim should be capable of becoming a bounded local speech occurrence:

```text
Speaker communicates Claim
    ↓
SpeechOccurrence
    speaker
    intended recipient
    audibility
    claim/topic
    ↓
nearby actors resolve hearing/perception
```

This same machinery can support:

- direct conversation;
- gossip;
- warnings;
- boasting;
- begging;
- town criers;
- public announcements;
- later broadcasts.

## Player information reveal filter

Do not print every claim transfer.

Surface a literal blurb primarily when information is:

- new to the player;
- materially changed;
- contradictory;
- newly better/worse confidence or provenance;
- explicitly requested/focused;
- highly salient/actionable.

Repeated information can remain simlish.

## History and V1 conversation

Conversation does not require full personal episodic memory.

A first useful path is:

```text
Shared authoritative History
    ↓
Accessibility / actor-relative query
    ↓
ActorRelativeClaim
    ↓
Communication
    ↓
recipient Working Knowledge
```

A miner, guard, merchant, baker, and visitor may resolve different levels of detail from the same historical truth because of access, profession, location, role, relevance, and interest.

This must never become shared omniscience.

## Multiplayer chat boundary

Human multiplayer text chat is a separate communication feature.

NPC reasoning should not parse arbitrary human chat as authoritative social intent unless a future explicit mechanic is designed for that purpose.

## Implementation guardrail

The system needs to answer:

> **Who knows what, who cares, who communicates it, who hears it, and when is it worth surfacing?**

It does not need to generate fully subtitled natural-language conversations for every NPC exchange.
