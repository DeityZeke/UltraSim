# Direct Connect identity gateway

This F0.3a executable hosts the accepted external UDP transport and the minimal
authenticated application-session identity message. It deliberately does not yet
create a participant, guest, actor, world, Chat, Inventory, or UI session.

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
