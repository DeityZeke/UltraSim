# Direct Connect identity gateway

This executable hosts the accepted F0.3a deployment-trust/session transport and the
F0.3b external Concept Demo application. After authenticated trust, one connection may
submit a bounded guest identity, receive a server-issued control session, and use the
real Game/Physics, public-world, Chat, Inventory, and informational-UI semantic paths.

F0.3b deliberately admits one external participant. The Game runtime stays on the
existing Hosted connection-owner thread; shared multi-participant world ownership is a
later checkpoint rather than one runtime being silently constructed per player.

Run the development endpoint with:

```bash
dotnet run --project Server/DirectConnect/UltraSim.Server.DirectConnect.csproj
```

The default bind is `127.0.0.1:7777`. `--bind=<IPv4>:<port>` and
`--identity-dir=<path>` are supported. The deployment P-256 key and independent
`ApplicationRealmId` are generated once and persisted with owner-only permissions
on Unix. The printed SHA-256 fingerprint is what a tester compares on the client's
first-use trust screen.

The endpoint publishes a self-signed candidate key before connection only to make
human onboarding possible. That candidate is untrusted. The real Hosted handshake
continues to require the exact selected/saved key as its pin, and the realm becomes
verified only after it arrives inside that authenticated session.
