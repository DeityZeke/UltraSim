# Framework Control Plane — P1 foundation

The viewer is not the owner. This BCL-only management substrate discovers frozen provider-scoped
Diagnostic / Control / Action / Collection descriptors, authorizes access and routes cold owner
operations. It is neither an Engine Core, a UI framework, a settings database nor a package host.

## Contracts and finite limits

Provider, control, diagnostic, action, collection, capability, option, row and request IDs use
ordinal identities: 1–96 ASCII characters, initial lowercase letter, subsequent lowercase
letters/digits/dot/hyphen/underscore; no consecutive or trailing dots. No trimming or case folding
silently creates aliases. Default IDs are invalid. Categories contain 1–8 slash-separated ASCII
alphanumeric/hyphen/underscore segments beginning with a letter, at most 192 characters.
Labels are at most 128 and descriptions/result diagnostics 1,024 characters, without control
characters. Registration is limited to 64 simultaneous providers and 512 total publications per
provider. Publication names are unique across all four kinds within a provider; no global item namespace.

ControlValue is an immutable closed Bool / Int64 / UInt64 / finite Float64 / Text / Option value.
Text is bounded to 1,024 characters; Options are canonical ControlOptionId, not reflected CLR
enums. Schemas permit only exact-kind numeric bounds, text length and up to 32 finite options.
Integer bounds never round through floating point. Batches have at most 32 distinct controls;
actions at most 16 ordered scalar argument schemas. Collections have at most 24 columns,
1,024 rows (also constrained by the descriptor), stable row IDs and schema-checked ordered cells.
All external sequences are bounded while copying, not after an unbounded ToArray.

Mostly-static descriptors are separate from owner state. Requested means the owner's accepted
desired value; Effective is actual behavior; Pending is accepted desired state awaiting the
documented boundary. Unsaved viewer drafts remain viewer-local. Each value has its own provenance:
BuiltInDefault, UserGlobal, UserRealm, ServerRecommendation, ServerColdConfig, AdminRuntimeOverride,
DeveloperOverride, TestOverride or RuntimeConstraint. Disabled states carry a machine-readable
result code and bounded reason. Apply modes are Live, SafePoint, SessionRestart, ClientRestart,
ServerRestart, BuildTime and ReadOnly; persistence is descriptive, never owned by this registry.

## Lifetime, authorization and failure

Registry generation increases globally within one registry on every successful registration,
including reuse of a retired ProviderId; no unbounded retired-ID history exists. It is a
process-local lifetime fence, not persistent identity or a network credential. A request must
target the exact registry/provider generation. Provider-owned revisions separately change with
state; an optional expected revision protects edits. Registry instances must not be replaced
under retained requests: recreate the owning request/session context at that boundary.

Registration publishes a fully validated immutable set atomically; duplicate active IDs reject,
never replace. Registry/dispatch/retirement run synchronously on one explicit management owner
thread, are non-reentrant and own no background tasks. Retirement removes admission only after
any synchronous dispatch has returned; retire adapters before disposing their owners. Repeated
retirement returns false. Calls from other threads are programming errors, not authority bypasses.
Owners on different threads need their own trusted routing contexts; P1 does not implement an
arbitrary cross-thread operator transport. No locks cover subsystem I/O or hot loops.

Trusted composition creates ControlPrincipal with bounded identity/context and capabilities.
Principal categories supply audience filtering, not privilege. Discovery requires both visibility
and read capability. Explicit reads/writes/actions enforce read and write/invoke capabilities,
even for known IDs. Never deserialize an untrusted capability claim into a trusted principal.
Authorization precedes generation disclosure for a known publication; denied or malformed
requests do not disclose provider generation/revision. No wire/admin protocol is added.

Single requests and provider-local batches perform generic authorization/schema/lifetime/writable
checks before invoking mutation. The owner receives the complete candidate and owns coupled
validation and transactional application; no cross-provider transaction exists. Expected failures
return Rejected. Unexpected owner exceptions propagate unchanged (and remain Failed audit
observations where an audit sink is configured); they never become harmless rejection or imply
rollback. Audit sink failures also surface, aggregating with an original owner failure. An optional
IControlAuditSink receives bounded request identity/principal/context, optional time/tick,
operation/target, candidate and result. There is no persistent audit database. Sinks must apply
their own bounded retention and safe-data policy.

## Real adapters and generic viewer

- Bridges/ControlPlane.ECS depends on this Framework and ECS, not vice versa. Explicit cold
  system bindings publish existing scheduler/system snapshots, bounded typed cold graph columns and idle-owner
  Enabled/Period/Offset/RunMode controls. All coupled schedules/worker policy validate before
  any setter. Maximum 120 bound systems. No private scheduler objects escape. Graph facts remain
  Collection cells, not controls. Observations occur only on management requests. During graph
  execution, the owner may inspect only the narrow execution-state probe; the adapter serves
  its last idle snapshot and rejects writes without touching live scheduler state.
- Bridges/ControlPlane.ServerRuntime depends on Framework and ServerRuntime. A narrow public
  owner-thread snapshot reports actual options, active generation/clock and idle state. Fixed
  playback/frequency descriptors are ReadOnly; variable policies permit existing setters only
  when running/idle, validating all candidates before mutation. Game callbacks remain unsafe
  boundaries. No mutable generation is exposed.
- Client/ControlPlane depends on Framework and existing Client Core. Trusted composition supplies
  the existing immutable document getter, actual effective consumer settings and transactional
  owner commit. The existing resolver/FileStore retain validation/persistence/Realm/consent
  ownership. Seven representative fields prove Bool/Float/Int/Option and device-owned next-entry
  vegetation Pending. Master Volume is explicitly a persisted preference without an Audio consumer.
  The adapter is scoped to one current context; named-profile/editor integration remains P5.
- ControlPlaneViewer is a generic cold model of category paths, descriptors and typed snapshots.
  It invokes actions, holds at most 32 local drafts and submits provider-local batches. Removing
  the ECS provider removes its content without changing the viewer. No concrete owner references,
  reflection tabs, polished F4/F10 UI or persisted drafts.

The P1 ECS, ServerRuntime and Client Settings proof adapters remain opt-in, not
default product dependencies; export gates still exclude those bridge/provider
assemblies. P3 deliberately brings the BCL-only Framework Control Plane contract
assembly into SinglePlayer/Online/Complete for one cold, read-only Godot adapter
publication owned by the actual client root. No ECS, packet or render hot path calls
this registry; adapter status is captured only on management requests.

## Verification

Run dotnet run --project _Tests/UltraSim.ControlPlane.Tests -c Release, then
bash ./verify.sh --architecture-only and bash ./verify.sh --configuration=Release.
The canonical verifier includes this proof alongside existing ECS, Runtime, Settings and
native/export/lifecycle gates. P2 Runtime Host is separate and closed; P5 product UI
and remote operator routing are deliberately not implemented here.
