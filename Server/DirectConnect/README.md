# Direct Connect identity gateway

This executable hosts the accepted F0.3a deployment-trust/session transport and the
F0.3b external Concept Demo application. M0.0 now composes every authenticated connection as an
independent guest/application/control lineage inside one bounded shared Game/World authority.
Each guest uses the real Game/Physics, per-recipient public-world, Chat, owner-private Inventory,
and informational-UI semantic paths. A disconnect retires only that participant; the remaining
authority and guests continue, and reconnect creates fresh participant/control/public identity.
C0.2's Global and explicit System occurrences therefore fan out over one shared authority rather
than one isolated Chat service per connection; reconnect does not inherit prior occurrence history.

The default development profile admits at most eight simultaneous participants. That is a bounded
functional profile, not a production-capacity claim. Endpoint, Network connection, application
session, participant, control session, and generational public entity remain separate identities.

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
`--identity-dir=<path>`, and the verifier-oriented `--ready-file=<path>` are supported. The
deployment P-256 key and independent
`ApplicationRealmId` are generated once and persisted with owner-only permissions
on Unix. The printed SHA-256 fingerprint is what a tester compares on the client's
first-use trust screen.

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
