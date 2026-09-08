# UltraSim Network testing

`UltraSim.Network.Testing` owns deterministic, self-trusted, and two-ended
transport fixtures used by tests and benchmarks:

- deterministic datagram impairment/link endpoints;
- protocol loopback;
- paired connected-UDP sessions;
- self-trusted shared-listener topologies;
- controlled handshake harnesses.

It references the canonical Full `UltraSim.Network` build so it can exercise both
Hosted roles. It is evidence/tooling, not a production runtime dependency, and is
mechanically absent from Full, ClientOnly, ServerOnly, Single Player, Online,
Dedicated, and Complete product exports.

Production protocol declarations and algorithms remain in
[`Kernels/Network`](../Network/README.md). The testing assembly may construct and
impair those mechanisms; it may not define a competing wire contract.
