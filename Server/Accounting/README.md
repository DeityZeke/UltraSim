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

Password hashing is intentionally interface-backed and envelope-versioned. OWASP
currently recommends Argon2id as the first choice for new applications; PBKDF2 is
provided here because it is available in the .NET base class library and gives the
accounting contract a working secure baseline without adding a package dependency
in the first slice. Adding an Argon2id implementation later should not change the
account registry or admission flow shape.

Credential bytes must not flow through generic schema UI field values. Admission
should receive a dedicated trusted login/auth packet and pass a typed
`CredentialValidationRequest` into `ICredentialService`.
