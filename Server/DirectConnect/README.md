# Direct Connect identity gateway

This executable hosts the accepted F0.3a deployment-trust/session transport and the
F0.3b external Concept Demo application. After authenticated trust, one connection may
submit a bounded guest identity, receive a server-issued control session, and use the
real Game/Physics, public-world, Chat, Inventory, and informational-UI semantic paths.

F0.3b deliberately admits one external participant. The Game runtime stays on the
existing Hosted connection-owner thread; shared multi-participant world ownership is a
later checkpoint rather than one runtime being silently constructed per player.
After Client F0.5 closes the remaining compiled-UI source convergence, that shared-authority
composition is the next major cross-cutting checkpoint. It will replace this one-participant
development limitation without changing endpoint, Network connection, application session,
participant, and control-session identity into one shared concept.

A0.3 adds an optional immutable content authorization to the same application factory. When
configured, it publishes the bounded requirement after authenticated realm/session identity and
does not admit guest/Game semantics until an exact `ContentSetApplied` proof matches the session,
realm, activation, plan, and expected resolved fingerprint.

A0.4 adds a neutral bounded immutable-byte `StaticContentHttpHost` in `Server.Hosting`. It runs
outside Game/Network progression and knows no realm, manifest, or package semantics. A0.8 now
composes the development executable with one canonical Concept Demo deployment package containing
the complete WorldPack and standard Inventory UI definition. The authority publishes the exact
authenticated requirement while the separate HTTP host only serves its immutable `.uspkg` bytes.

Run the development endpoint with:

```bash
dotnet run --project Server/DirectConnect/UltraSim.Server.DirectConnect.csproj
```

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

The default content source is plain loopback HTTP for explicit local development. The stock client
requires the corresponding private/insecure-source opt-in; production deployment is expected to
advertise HTTPS or an equivalently admitted immutable-byte source rather than silently weakening
the client policy.

The endpoint publishes a self-signed candidate key before connection only to make
human onboarding possible. That candidate is untrusted. The real Hosted handshake
continues to require the exact selected/saved key as its pin, and the realm becomes
verified only after it arrives inside that authenticated session.
