# Official Local content

This directory contains deterministic distributable `world.local` package artifacts, not Godot
resources or a second content system.

`ultrasim-official-actors-v1.uspkg` is built from the exact canonical X/Y GLBs under
`Client/PresentationAssets` by `_Tools/OfficialContentBuilder`. The builder validates the compiled
file identities and passive actor profile before canonical archive encoding. Single Player and
Complete copy this package beside the executable under `Content/Official`; Online ships without it.

At runtime the archive is only a source candidate. It becomes usable after normal package
validation, `ContentScopeKey.Local` authorization, CAS install, activation/catalog publication,
and presentation-generation selection. Its location or filename grants no trust, and equivalent
bytes in a Realm do not grant cross-scope authorization.
