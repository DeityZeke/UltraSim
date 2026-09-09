# Client presentation authoring sources

This tree contains source artwork and deterministic conversion tooling. It is not exposed as a
Godot runtime resource root and is not copied into product exports.

`Avatars/build_animated_bodies.py` must be run by Blender, not ordinary Python:

```bash
blender --background --factory-startup \
  --python Client/PresentationSource/Avatars/build_animated_bodies.py
```

The script admits an action only when its complete ordered bone-name set matches the destination
body, removes incidental animation carried by the model FBX, then exports one GLB containing that
body, its one skeleton, and exactly one sampled `Idle`, `Walk`, and `Jump` action. It does not invent
Attack or Interact clips; missing action animation continues to use the renderer's bounded primitive
cue.

The generated GLBs belong under `Client/PresentationAssets/Avatars`. Source FBXs must not also be
placed in that runtime tree, because doing so recreates two unrelated Godot-imported skeleton
resources and makes accidental runtime retargeting possible.
