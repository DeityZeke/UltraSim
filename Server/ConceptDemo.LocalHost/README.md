# Complete Concept Demo local-host adapter

`UltraSim.Server.ConceptDemo.LocalHost` is the Complete-only M0.0 bridge that adds remote Hosted
Network recipients to an already-running `Client.Local` Concept Demo authority.

The adapter is created and progressed on the internal server owner thread. It borrows the existing
Game generation, prepared World, shared scene, Chat service, and packet-free host participant; it
then adds the deployment identity, static content host, UDP listener, and independent remote
applications. Enabling hosting does not restart the world or move the host through loopback
Network. Stopping hosting retires remote sessions and endpoints while the local server continues.

This assembly is present only in the Complete product. Single Player has no Network family, Online
has no local authority family, and the dedicated server uses `Server/ConceptDemo` without this
adapter. Complete explicitly builds the canonical Full `UltraSim.Network` capability set because
the process contains both a remote client and a Hosted server; Online and dedicated product builds
currently select ClientOnly and ServerOnly respectively.

The current client-facing default binds loopback development endpoints. Public bind/NAT/relay UX,
host migration, and production population claims remain future deployment work.
