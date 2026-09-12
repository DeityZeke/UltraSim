# Direct Connect identity gateway

This executable hosts the accepted F0.3a deployment-trust/session transport and the
F0.3b external Concept Demo application. M0.0 now composes every authenticated connection as an
independent guest/application/control lineage inside one bounded shared Game/World authority.
Each guest uses the real Game/Physics, per-recipient public-world, Chat, owner-private Inventory,
and informational-UI semantic paths. A disconnect retires only that participant; the remaining
authority and guests continue, and reconnect creates fresh participant/control/public identity.
C0.2's Global and explicit System occurrences therefore fan out over one shared authority rather
than one isolated Chat service per connection; reconnect does not inherit prior occurrence history.

The default development profile admits at most eight simultaneous participants. PF1 may explicitly
select `--maximum-participants=<1..64>` and a loopback-appropriate
`--same-address-admission-burst=<1..4096>` for a bounded local swarm. Neither option is a capacity
bypass or production-capacity claim. Endpoint, Network connection, application session,
participant, control session, and generational public entity remain separate identities.
The shared authority derives its ECS entity and Inventory index reservations from the selected
participant bound, so an accepted configuration does not retain the smaller default world's
construction capacity.

A0.3 adds an optional immutable content authorization to the same application factory. When
configured, it publishes the bounded requirement after authenticated realm/session identity and
does not admit guest/Game semantics until an exact `ContentSetApplied` proof matches the session,
realm, activation, plan, and expected resolved fingerprint.

A0.4 adds a neutral bounded immutable-byte `StaticContentHttpHost` in `Server.Hosting`. It runs
outside Game/Network progression and knows no realm, manifest, or package semantics. A0.8 now
composes the development executable with one canonical Concept Demo deployment package containing
the complete WorldPack plus standard Inventory and read-only Server Monitor UI definitions. The authority publishes the exact
authenticated requirement while the separate HTTP host only serves its immutable `.uspkg` bytes.
The host uses fixed concurrent response workers, a bounded pending queue, and per-response
deadlines; one stalled download cannot serialize every other client behind it, and overload is
rejected without unbounded task creation.

D0.6 makes each application expose the Game-owned Server Monitor through that exact
content-backed definition. A shared authority supplies a bounded sanitized participant census,
tick timing, entity/capacity totals, and last trusted test marker. Remote endpoints and deployment
identity material are deliberately absent from the read model. Test-marker publication is a trusted
server/test surface that reuses C0.2 System occurrences; it is not a participant command.

Shared deployment content and profile definitions live in `Server/ConceptDemo`, which has no
`Client.Local` dependency. The Complete-only adapter in `Server/ConceptDemo.LocalHost` reuses those
definitions while borrowing an existing internal server; this dedicated executable never carries
the local-client authority bridge.

Run the development endpoint with:

```bash
dotnet run --project Server/DirectConnect/UltraSim.Server.DirectConnect.csproj \
    -p:UltraSimNetworkFlavor=ServerOnly
```

The canonical Network development default is Full. Dedicated product builds select
`ServerOnly` explicitly so their physical output omits the unused Hosted client
role; switching this product to Full later requires no consumer-code change.

The default protocol bind is `127.0.0.1:7777` and the content host defaults to
`127.0.0.1:7778`. `--bind=<IPv4>:<port>`, `--content-bind=<IPv4>:<port>`,
`--identity-dir=<path>`, and the verifier-oriented `--ready-file=<path>` are supported. Automated
acceptance may also request graceful retirement through `--stop-file=<path>` and capture the final
generation/tick/join/leave/participant/session state through `--summary-file=<path>`. The
deployment P-256 key and independent
`ApplicationRealmId` are generated once and persisted with owner-only permissions
on Unix. The printed SHA-256 fingerprint is what a tester compares on the client's
first-use trust screen.

PF2 adds an explicitly coordinated cumulative-snapshot accounting window. Supply all four of
`--accounting-start-file`, `--accounting-ready-file`, `--accounting-stop-file`, and
`--accounting-summary-file` together. The report labels exact UDP datagram **payload** bytes at the
listener boundary, established-connection Data/ACK/KeepAlive/retransmission counters, finite
lane/Game-family logical traffic, any pre-connection or unattributed residual, authority ticks,
and process CPU/RSS context. It reports HTTP response body bytes separately and does not claim
IP/UDP/link or complete HTTP/TCP wire cost. These file barriers are verifier/baseline coordination,
not a mutable player diagnostic or administrative protocol.

PF2.5 accepts `--telemetry=Off|Basic|Detailed`. Detailed accounting snapshots add bounded Network
service/protocol histograms, authority tick interval/duration/lateness and catch-up state,
per-application Game/replication stage timings, queue high-waters, exact manifest-indexed message
counters, and process CPU/RSS/heap/GC/thread context. These are owner-local observations aggregated
only at the cold report boundary. They do not change cadence, public-state ownership, AOI,
packetization, protocol bytes, or authority policy.

PF3 adds `--authority-hz=20|30|60`, `--maximum-input-hz`, `--recommended-input-hz`,
`--input-burst`, `--input-burst-window-ms`, and `--network-service-delay-ms`. The authority publishes
those finite semantics as an exact authenticated `ServerPerformanceProfile/v1` and waits for a fresh
application proof before content/admission. Hosted service remains event-driven with a configured
maximum polling/service interval; it is not relabeled as a Game tick frequency. Excess ordinary
commands are stopped before Game mutation by per-application token/rolling-window state and receive
an explicit `RateLimited` owner completion.

F0.3c publishes one finite stock client profile after authenticated realm identity and gates
content/admission on its exact application-session proof. Select the development profile with
`--client-profile=orbit`, `--client-profile=third-person` (the default), or
`--client-profile=independent`. The deployment publishes only canonical grammar, camera parameters,
and physical-binding recommendations; it never supplies client code. Profile revision/hash are
printed and included in the verifier readiness record.
The F0.8 canary intentionally makes the stock profiles visibly distinct: Third Person recommends
an 8.5 m / 76-degree follow camera, middle-click Select, and left-click Primary; Orbit recommends
a 14 m / 70-degree orbit camera, left-click Select, and middle-click Primary. Changing between
them under the same deployment/realm/content identity changes the exact profile hash and requires
fresh client consent without changing or redownloading Game/content state.

The default content source is plain loopback HTTP for explicit local development. The stock client
requires the corresponding private/insecure-source opt-in; production deployment is expected to
advertise HTTPS or an equivalently admitted immutable-byte source rather than silently weakening
the client policy.

The endpoint publishes a self-signed candidate key before connection only to make
human onboarding possible. That candidate is untrusted. The real Hosted handshake
continues to require the exact selected/saved key as its pin, and the realm becomes
verified only after it arrives inside that authenticated session.
