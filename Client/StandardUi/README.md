# Client Standard UI

This is the deliberately small client-composition library of passive, canonical compiled UI
artifacts. It references only the UI kernel. It does not ship Game UI/Inventory authority code,
model values, handlers, scripts, or executable payloads.

F0.5 selects the exact Inventory v6 `.usui` artifact as its first stock definition. A stock lookup,
a reopened exact content-cache lookup, and a newly acquired realm-content lookup all return the
same `CompiledUiDefinition` to `UiClientRuntime`; only the physical source differs. Possessing this
resource never establishes realm authorization or `ContentSetApplied` proof.
