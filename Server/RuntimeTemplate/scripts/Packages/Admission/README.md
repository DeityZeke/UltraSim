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
- `admission.character_selected`
- `admission.character_create_requested`
- `admission.character_created`
- `admission.enter_world`

Servers may install a shorter flow, such as direct entry on local test shards, or
insert custom panels between any two events. Replacement must be single-owner:
one package owns a step at a time, with explicit priority and conflict reporting.

Trusted credential fields must use the trusted credential path. Do not submit
password values through generic `UiActionRequestDocument.FieldValues`.

`AdmissionModule` registers the default panel providers and exposes the default
step descriptors. The current script context does not yet have a flow-step
registry, so the descriptors are data for the upcoming admission runtime slice;
they should not be hard-coded again elsewhere.
