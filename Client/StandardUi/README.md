# Client Standard UI

This is the deliberately small client-composition library of passive, canonical compiled UI
artifacts. It references only the UI kernel. It does not ship Game UI/Inventory authority code,
model values, handlers, scripts, or executable payloads.

F0.5 selected the Inventory `.usui` artifact as its first stock definition. I0.12 advances that
passive definition to v7 for the authoritative item-details slot and Drop action. A stock lookup,
a reopened exact content-cache lookup, and a newly acquired realm-content lookup all return the
same `CompiledUiDefinition` to `UiClientRuntime`; only the physical source differs. Possessing this
resource never establishes realm authorization or `ContentSetApplied` proof. I0.13 changes only the
typed item-inspection source used to populate the existing details model slot; it does not create a
second Inventory definition or client-side Inventory authority.
