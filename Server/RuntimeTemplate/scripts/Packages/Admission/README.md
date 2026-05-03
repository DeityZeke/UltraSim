# Admission Package

This package owns the default server-authored admission flow panels.

The UI kernel owns generic schema vocabulary. Admission is gameplay/server flow:
connection, login, account validation, character select, character create, and
entry into the world. Keep those panels and event names here so custom servers
can replace, remove, or reorder admission steps without changing the kernel.

Default event shape:

- `admission.client_connected`
- `admission.login_requested`
- `admission.login_verified`
- `admission.character_select_requested`
- `admission.character_create_requested`
- `admission.enter_world`

Servers may install a shorter flow, such as direct entry on local test shards, or
insert custom panels between any two events. Step ownership is single-owner:
one package owns the active step for an input event at a time, with explicit
priority and conflict reporting.

Trusted credential fields must use the trusted credential path. Do not submit
password values through generic `UiActionRequestDocument.FieldValues`.

`AdmissionModule` registers the default panel providers and admission flow steps.
`Server/Admission` consumes the registered steps; panels only complete their
local step by emitting the configured completion event. Replacing a step is a
single-owner assignment for its start event, not `+=` accumulation.

Non-credential buttons route through explicit action routes on the active step.
For example, the default character select step maps `character_select.create` to
`admission.character_create_requested`, while its row-level play action maps to
`admission.enter_world`. The character create submit action also maps to
`admission.enter_world`. These routes are data on the step, not hardcoded
in the network or UI renderer.

Dynamic data comes from the server runtime policy, not from the static script
schema. The default movement server uses accounting to publish character rows
when the select panel opens and to validate/create character records when
`character_create.submit` is pressed. Custom packages can replace the panels or
steps while keeping that same event-driven shape.

The default character-create panel offers the stock `mobiles/human` prefab
registered from `scripts/Mobiles/`. Servers can replace the panel or options,
but stored character records should continue to use prefab keys rather than
hardcoded archetype enums.

The first runtime implementation validates trusted credential actions through a
narrow credential-validator seam. The default validator is a test/dev stub; the
accounting engine owns the real validator when account storage is rebuilt.
