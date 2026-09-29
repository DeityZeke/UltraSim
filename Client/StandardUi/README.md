# Client Standard UI

This is the deliberately small client-composition library of passive, canonical compiled UI
artifacts. It references the UI kernel and P4's neutral Definition identity contract. It does not ship Game UI/Inventory authority code,
model values, handlers, scripts, or executable payloads.

F0.5 selected the Inventory `.usui` artifact as its first stock definition. I0.12 advanced that
passive definition to v7 for the authoritative item-details slot and Drop action. Human-play input
hardening advances it to v8 for the canonical modeless outside-pointer policy. A stock lookup,
a reopened exact content-cache lookup, and a newly acquired realm-content lookup all return the
same `CompiledUiDefinition` to `UiClientRuntime`; only the physical source differs. Possessing this
resource never establishes realm authorization or `ContentSetApplied` proof. I0.13 changes only the
typed item-inspection source used to populate the existing details model slot; it does not create a
second Inventory definition or client-side Inventory authority.

P5 adds a bounded, presentation-only `UiSemanticImageBindings` seam. It attaches an already
admitted `SemanticPresentationReference` to an image element of one exact view instance and
compiled definition;
the Godot adapter may resolve a borrowed texture or use its owned primitive icon fallback. This
binding does not authorize content, change the Game UI wire format, create a second item identity,
or implement P6's dynamic Inventory portraits and derived cache.
