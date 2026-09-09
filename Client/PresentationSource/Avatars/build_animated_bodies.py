#!/usr/bin/env python3
"""Bake the archived Mixamo actions onto UltraSim's X/Y body meshes.

Run with Blender rather than the system Python:

    blender --background --factory-startup --python build_animated_bodies.py

The source FBX files remain authoring inputs.  Each output GLB deliberately contains one body,
one skeleton, and the finite Idle/Walk/Jump presentation clips consumed by Godot.  Attack and
Interact are not manufactured here; the client retains bounded primitive cues when those clips
are unavailable.
"""

from pathlib import Path
import sys

import bpy


SOURCE_ROOT = Path(__file__).resolve().parent
OUTPUT_ROOT = SOURCE_ROOT.parents[1] / "PresentationAssets" / "Avatars"
BODIES = {
    "x_bot": ("X Bot.fbx", "x_bot_animated.glb"),
    "y_bot": ("Y Bot.fbx", "y_bot_animated.glb"),
}
CLIPS = {
    "Idle": "idle.fbx",
    "Walk": "walking.fbx",
    "Jump": "jump.fbx",
}


def fail(message: str) -> None:
    raise RuntimeError(message)


def import_fbx(path: Path) -> list[bpy.types.Object]:
    before = set(bpy.data.objects)
    result = bpy.ops.import_scene.fbx(filepath=str(path), automatic_bone_orientation=False)
    if "FINISHED" not in result:
        fail(f"Blender could not import {path}")
    return [obj for obj in bpy.data.objects if obj not in before]


def only_armature(objects: list[bpy.types.Object], source: Path) -> bpy.types.Object:
    armatures = [obj for obj in objects if obj.type == "ARMATURE"]
    if len(armatures) != 1:
        fail(f"Expected one armature in {source}, found {len(armatures)}")
    return armatures[0]


def remove_import(objects: list[bpy.types.Object], retained_action: bpy.types.Action) -> None:
    for obj in objects:
        if obj.animation_data is not None:
            obj.animation_data.action = None
        bpy.data.objects.remove(obj, do_unlink=True)
    retained_action.use_fake_user = True


def bake_body(directory: Path, model_name: str, output: Path) -> None:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    model_path = directory / model_name
    model_objects = import_fbx(model_path)
    model_armature = only_armature(model_objects, model_path)
    model_bones = tuple(bone.name for bone in model_armature.data.bones)
    if not model_bones:
        fail(f"The model {model_path} has no skeleton")

    actions: list[bpy.types.Action] = []
    for action_name, file_name in CLIPS.items():
        clip_path = directory / file_name
        clip_objects = import_fbx(clip_path)
        clip_armature = only_armature(clip_objects, clip_path)
        clip_bones = tuple(bone.name for bone in clip_armature.data.bones)
        if clip_bones != model_bones:
            fail(f"Skeleton names/order in {clip_path} do not match {model_path}")
        if clip_armature.animation_data is None or clip_armature.animation_data.action is None:
            fail(f"The clip {clip_path} contains no action")
        action = clip_armature.animation_data.action
        action.name = action_name
        action.use_cyclic = action_name in {"Idle", "Walk"}
        actions.append(action)
        remove_import(clip_objects, action)

    model_armature.animation_data_create()
    model_armature.animation_data.action = actions[0]
    model_armature.name = "BodySkeleton"
    model_armature.data.name = "BodySkeleton"
    for obj in model_objects:
        obj.select_set(True)

    output.parent.mkdir(parents=True, exist_ok=True)
    result = bpy.ops.export_scene.gltf(
        filepath=str(output),
        export_format="GLB",
        use_selection=True,
        export_animations=True,
        export_animation_mode="ACTIONS",
        export_force_sampling=True,
        export_rest_position_armature=True,
        export_anim_slide_to_zero=True,
        export_optimize_animation_size=True,
        export_yup=True,
    )
    if "FINISHED" not in result or not output.is_file():
        fail(f"Blender could not export {output}")
    print(f"WROTE {output} ({output.stat().st_size} bytes; {len(model_bones)} bones)")


def main() -> None:
    for directory_name, (model_name, output_name) in BODIES.items():
        bake_body(
            SOURCE_ROOT / directory_name,
            model_name,
            OUTPUT_ROOT / directory_name / output_name,
        )


if __name__ == "__main__":
    try:
        main()
    except Exception as exception:
        print(f"ERROR: {exception}", file=sys.stderr)
        raise
