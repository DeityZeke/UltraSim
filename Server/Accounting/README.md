# UltraSim Server Accounting

`Server/Accounting` is compiled server infrastructure. It owns trusted account
state and credential policy; it is not a runtime script package.

Current first-pass scope:

- In-memory account registry with stable account ids.
- Versioned password hash envelope.
- PBKDF2-HMAC-SHA256 implementation behind `IPasswordHashService`.
- Explicit account states: active, banned, suspended, pending verification, locked out.
- Login rate-limit interface and in-memory implementation.
- Active-session conflict tracking.
- Login audit sink shape.
- `DefaultCredentialService` that combines lookup, status, password verification,
  session conflict policy, rate limiting, and audit recording.
- `AccountingAdmissionCredentialValidator` adapter that maps trusted admission
  credential packets into `CredentialValidationRequest` and rejects generic UI
  action credential attempts.
- Admission-facing failure reason mapping. Unknown accounts and wrong passwords
  both surface as `InvalidCredentials`; account policy states such as banned,
  suspended, locked out, rate limited, and portal unavailable stay
  machine-readable without sending account-specific prose.
- Account-backed character records with stable `CharacterId`, owning
  `AccountId`, slot index, display name, prefab key, timestamps, and last
  position/yaw snapshot.
- `AccountCharacterService` for listing, creating, deleting, auto-join lookup,
  and snapshot updates. Character records store prefab keys, not hardcoded
  archetype enums, so character creation can select from registered mobile
  prefabs later.
- Accounting persistence snapshot and first JSON store:
  - `AccountingPersistenceSnapshot` captures immutable account + character
    records from registries.
  - `JsonAccountingPersistenceStore` writes one account file per account,
    character files under that account, and an atomic username index.
  - Writes are temp-file validated and atomically promoted.
  - Successful overwrites create timestamped rolling backups.
  - Deleted character records are pruned from disk so they cannot reappear on
    restart.
- Account authentication provider seam with two explicit modes:
  - `LocalServerAccounts` for per-server account records and password hashes.
  - `UltraSimPortalAccounts` for future portal-backed token/session validation.

Password hashing is intentionally interface-backed and envelope-versioned. OWASP
currently recommends Argon2id as the first choice for new applications; PBKDF2 is
provided here because it is available in the .NET base class library and gives the
accounting contract a working secure baseline without adding a package dependency
in the first slice. Adding an Argon2id implementation later should not change the
account registry or admission flow shape.

Credential bytes must not flow through generic schema UI field values. Admission
should receive a dedicated trusted login/auth packet and pass a typed
`CredentialValidationRequest` into `ICredentialService`.

Portal-backed authentication is intentionally modeled as a peer provider, not as
a special case inside local password verification. In portal mode the game server
should receive an opaque portal session/token plus its own server instance id,
ask the platform service to validate it, and keep only server-local policy state
such as kicks, bans, timeouts, session conflict decisions, and character/world
data. Personal credential material and platform account history stay with the
portal/platform account service.

`IPortalAccountAuthenticator` implementations must cryptographically validate
tokens or call the portal authority. A test fake may accept a known token, but a
production implementation must never trust token text merely because it came from
a connected client.

## Persistence Boundary

The current repo already has an ECS persistence extension with world
snapshot/restore and a binary adapter, plus `ServerApplication` load/save hooks.
Accounting persistence must plug into that same server runtime lifecycle rather
than running as an unrelated save loop. The intended shape mirrors the old v2
coordinator: capture account/character snapshots and world snapshots at the save
barrier, then write each subsystem's snapshot through the unified server save
path.

The JSON accounting store is the first concrete account/character backend and is
operator-debuggable by design. It is not the final production storage profile.
Encrypted binary, database-backed stores, async dirty flushing, and account/world
save coordination should layer on top of the same snapshot/store contract.

Local password mode requires transport encryption before internet deployment.
The password hash at rest is versioned and salted, but the credential packet
still carries live credential material to the server.
