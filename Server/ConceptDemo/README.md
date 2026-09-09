# Concept Demo deployment composition

`UltraSim.Server.ConceptDemo` owns the passive deployment composition shared by dedicated Direct
Connect and Complete-hosted Concept Demo servers:

- the exact authenticated content activation containing the canonical Concept Demo WorldPack plus
  standard Inventory and read-only Server Monitor UI definitions;
- the finite stock client-profile advertisements used by F0.3c.

It references server/Game/Network/content foundations but deliberately does not reference
`Client.Local`. A dedicated server can therefore reuse the same deployment identities and artifacts
without carrying the embedded local-player adapter.

The project references the canonical `UltraSim.Network` identity without defining a second server
Network API. Dedicated product composition currently selects ServerOnly; Complete selects Full.
Either exposes the exact Hosted Server API this composition uses, and switching Dedicated to Full
later requires no source change.

This project does not own a listener, participant, or Godot client. `Server/DirectConnect` composes
it with a dedicated `SharedPlayableAuthority`; `Server/ConceptDemo.LocalHost` composes it with a
listener that borrows an already-running local authority.
