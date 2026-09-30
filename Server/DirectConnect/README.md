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
composes the development executable with a canonical Concept Demo bootstrap package containing
the structural manifest plus standard Inventory and read-only Server Monitor UI definitions. The
authority publishes the exact authenticated requirement and later grants the two canonical Packs
independently by coordinate; the separate HTTP host only serves immutable `.uspkg` bytes.
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

P7's active headless operator slice adds a bounded stdin command reader and owner-thread
Control Plane routing. Type `help` for the currently published actions. `save`, `backup`,
`status`, `participants`, `audit`, `logs`, `shutdown confirm`, `restart confirm`, and
`kick <participant-id> confirm` are available. For managed restart, launch the published
`run-managed.sh`; a successful final save yields exit code 75, and that launcher reopens
the same identity/configuration from a validated cold save. Running the binary directly
leaves code 75 for another supervisor to interpret. `operator-policy.json` in the identity
directory is cold configuration for autosave cadence, backup retention, recovery mode and roots. See
[Server Operator](../Operator/README.md) for exact limitations. This slice does not close P7.

Selected backup recovery is an exclusive offline command on the same published binary:
`--identity-dir=<existing directory> --restore-backup-sequence=<positive sequence>`.
It validates a separate backup and publishes a new forward live generation before returning;
run the server normally afterward. This command never admits clients.

The canonical Network development default is Full. Dedicated product builds select
`ServerOnly` explicitly so their physical output omits the unused Hosted client
role; switching this product to Full later requires no consumer-code change.

P2 adds an opt-in trusted server-authoring startup seam. Point
`--scripts-root=<server-owned Scripts directory>` at a tree of canonical
`<package-id>/manifest.json` and bounded canonical nested C# source files; optionally set
`--scripts-cache=<writable cache directory>`. The repository's
`Server/Scripts.Sample` is an executable example. The selected packages are
discovered, compiled, admitted and started **before** Concept Demo authority or
Network is constructed; a failure exits nonzero without starting either. The
startup summary prints the resolved runtime composition fingerprint and cache-hit
count. No Scripts polling occurs in the authority loop. This development/sample
composition does not yet alter the fixed Concept Demo Game rules or create a
privileged path for downloaded client code. See [Server Authoring](../Authoring/README.md).

The default protocol bind is `127.0.0.1:7777` and the content host defaults to
`127.0.0.1:7778`. F1.12-R3 composes `StaticContentHttpService` with the shared inbound
Framework HTTP Endpoint and Kestrel Bridge. Dedicated hosting requires the ASP.NET Core 8
runtime (tested 8.0.31) in the selected dotnet installation; the SDK alone is not a runtime
deployment. Complete's Godot export carries supported private Kestrel dependencies instead.
`--bind=<IPv4>:<port>`, `--content-bind=<IPv4>:<port>`,
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

PF4B extends the same synchronized Detailed window with the authoritative publication-source delta
and each live application's bounded ChunkPublication knowledge census. Reports distinguish
Delta-120 records/application bytes from Network frame/UDP bytes, Proof-130 entries from envelopes,
knowledge-advancing Applied entries from non-advancing/negative results, and current/high-water
delivery debt. Unique-chunk saturation is explicit. These fields are diagnostic snapshots; they do
not reset or advance application knowledge and do not include HTTP content bytes.

PF4C selects beneficial message-129 composition for ordinary per-recipient ChunkPublication
deltas/absences while retaining the existing 1/2/4 public-tier cadence. The defaults are
`--public-representation=message129` and `--public-cadence=1/2/4`. Controlled evidence may select
`--public-representation=standalone` or another positive ordered cadence such as `1/3/6`; these are
measurement controls, not alternate ownership/AOI profiles. Snapshots remain 123/124/125, and
bundled entries retain individual proof and recovery identity. Per-application Detailed reports
include bundle packing, byte-limit closure, per-tier source-to-due/queue/proof freshness, and
per-recipient authority-step publication/message/bundle/application-byte distributions.

PF4D selects Network-layer aggregation independently of that Game representation. The production
defaults are `--unreliable-frame-cap=2` and `--unreliable-aggregation-scope=cross-lane`. Supported
frame-cap controls are `1`, `2`, `4`, and `8`; scope is `same-lane` or `cross-lane`. These options
change only how already-ready unreliable Network frames share secure UDP Data datagrams. They add
no wait, never combine reliable frames, and do not alter `ChunkPublication`, message 129, the 1/2/4
public cadence, AOI, participant capacity, or authority frequency. The readiness record and console
startup summary print the effective cap/scope so a baseline cannot silently use another policy.

PF4F independently selects `--public-compression=lz4` (also accepted as `selected`). The server
still constructs the unchanged canonical message 129 first. Only canonical payloads of at least
384 bytes are attempted, and message 148 is selected only when its complete five-byte wrapper saves
at least 32 bytes. Use `--public-compression=off` or `canonical` for a matched control. Compression
does not add bundle entries or change cadence, proof/debt, AOI, MTU, Network aggregation, authority
frequency, or capacity. The startup/readiness record prints the effective codec and thresholds.

PF5 characterization may add `--accounting-test-marker=<bounded text>` only when an explicit
accounting window is configured. The authority emits one trusted System occurrence at window start
and one at completion through the existing Chat occurrence path; participants do not gain System
publication authority. Detailed snapshots also expose a bounded cold census of current remote
recipients, occupied actor chunks, unique/multiply-subscribed chunks, memberships, tier counts,
recipients per chunk, dirty-chunk fanout, source commits, and source gaps. These observations do not
change AOI, cadence, proof state, or publication scheduling.

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

P7 operator commands (`help`, `status`, `participants`, `logs`, `audit`, `save`,
`backup`, `shutdown confirm`, `restart confirm`, `kick <participant> confirm`)
route through the same typed Control Plane provider as the optional
[Godot server window](../Godot/README.md). `run-managed.sh` supervises only the
explicit restart exit code 75. `--list-backups` is read-only candidate inspection
that can run beside a live server without hashing on its authority thread;
`--restore-backup-sequence=N` is validated stopped-server maintenance, not a
live mutation. The optional GUI child uses `--shutdown-on-stdin-eof` so parent
loss requests ordinary host-owned shutdown. See [operator ownership](../Operator/README.md)
for the exact recovery and identity contract.
