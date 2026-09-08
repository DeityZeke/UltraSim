# Concept Demo deployment composition

`UltraSim.Server.ConceptDemo` owns the passive deployment composition shared by dedicated Direct
Connect and Complete-hosted Concept Demo servers:

- the exact authenticated content activation containing the canonical Concept Demo WorldPack and
  standard Inventory UI definition;
- the finite stock client-profile advertisements used by F0.3c.

It references server/Game/Network/content foundations but deliberately does not reference
`Client.Local`. A dedicated server can therefore reuse the same deployment identities and artifacts
without carrying the embedded local-player adapter.

This project does not own a listener, participant, or Godot client. `Server/DirectConnect` composes
it with a dedicated `SharedPlayableAuthority`; `Server/ConceptDemo.LocalHost` composes it with a
listener that borrows an already-running local authority.
