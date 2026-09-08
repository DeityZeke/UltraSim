# Network capability packaging baseline — 2026-09-08

This artifact records the accepted physical-packaging evidence for the canonical
`UltraSim.Network` assembly and its optional capability-stripped builds.

## Contract

```text
default / canonical package   Full       Core + Hosted Client + Hosted Server
optional deployment build    ClientOnly Core + Hosted Client
optional deployment build    ServerOnly Core + Hosted Server
```

Every build retains the assembly identity `UltraSim.Network`, shared Core source,
protocol identity, wire encoding, and shared public API. The stripped builds are
deployment minimization artifacts, not functionality tiers, alternate protocols,
or a security boundary. The normal `UltraSim.Network` package contains Full.

Deterministic impairment, two-ended loopback transports, controlled handshake
fixtures, and self-trusted test topology moved to the separate
`UltraSim.Network.Testing` assembly. No production capability set references or
ships that assembly.

## Release artifacts

Measured from the exact accepted tree on .NET 8 Release builds:

| Artifact | Bytes | Change from prior monolith | Public API entries |
| --- | ---: | ---: | ---: |
| Prior monolithic `UltraSim.Network.dll` | 384,000 | baseline | not re-probed |
| Full `UltraSim.Network.dll` | 343,552 | -40,448 (-10.5%) | 1,900 |
| ClientOnly `UltraSim.Network.dll` | 203,776 | -180,224 (-46.9%) | 1,199 |
| ServerOnly `UltraSim.Network.dll` | 326,144 | -57,856 (-15.1%) | 1,770 |
| `UltraSim.Network.Testing.dll` | 54,784 | separately packaged test support | n/a |

Full's smaller size is principally the removal of reusable test topology from the
production assembly. ClientOnly physically excludes Hosted admission, listener,
server-identity/signing, connection-owner, and sharding/runtime implementation.
ServerOnly physically excludes Hosted connection and discovery-client
orchestration. Shared UDP, secure protocol, codec, lane, identity, and bounded
mechanism code remains Core where both roles require it.

The relative savings are implementation-profile evidence, not permanent product
size promises.

## Identity and substitution evidence

All three builds reported:

```text
assembly identity       UltraSim.Network
protocol version        1
manifest fingerprint    5d2e1a971b5e8e4c
canonical probe bytes   01020304050607081112131421220100F9FFFF
```

The verifier compares public type/member signatures, including base types,
interfaces, fields, properties, methods, events, enum values, and layout metadata.
It proved:

```text
Full public API == union(ClientOnly public API, ServerOnly public API)

Core API in ClientOnly == Core API in ServerOnly == Core API in Full
Client role API in ClientOnly == Client role API in Full
Server role API in ServerOnly == Server role API in Full
```

Thus a consumer compiled against ClientOnly or ServerOnly can later receive the
Full assembly without changing its source-level dependency or assembly reference.

## Product composition evidence

The permanent verifier inspected the actual published/exported assemblies:

```text
Single Player     no Network assembly
Online            ClientOnly
Dedicated server  ServerOnly
Complete          Full
```

The Online Godot export contains `HostedUdpProtocolClient` and
`HostedUdpDeploymentDiscoveryClient`, but contains no
`HostedUdpAdmissionHost`, `NetworkServerIdentity`,
`HostedUdpConnectionOwnerRuntime`, or `SharedUdpServerListener`.

The dedicated Direct Connect output contains the server role and no Hosted client
role. Complete contains both. Single Player's existing zero-Network gate remains
unchanged.

## Verification

The accepted tree passed:

- Debug complete managed repository verification;
- Release complete repository verification;
- all Network kernel, protocol, application, generator, Hosted, comparison, and
  shared-authority suites;
- canonical Full package creation and inspection;
- rejection of canonical package creation from ClientOnly/ServerOnly;
- rejection of unknown capability values;
- real Online Godot export inspection;
- cold and warm exported Contentless Online journeys;
- Complete packet-free local host plus two remote exported Online clients;
- Single Player, Online, Complete, and product-mismatch Godot gates;
- repository architecture, documentation, and exact-tree checks.

The protocol manifest/version and canonical packet evidence did not change. This
checkpoint changes physical capability presence only.
