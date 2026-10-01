"""Deterministic Blender 5.x authoring source for the optional P9 Cabin mesh set.

Run from the repository root:
  blender --background --python Client/PresentationSource/Cabin/build_cabin_meshes.py

The .blend is editable source. .usmesh files are passive, bounded renderer artifacts;
none carries collision or any Game/World identity. Re-run after an authored change and
update OfficialCabinPresentation's exact byte hashes before publishing a new package.
"""

import math
import random
import struct
from pathlib import Path

import bpy
from mathutils import Vector


ROOT = Path(__file__).resolve().parents[2] / "PresentationAssets" / "Cabin"
ROOT.mkdir(parents=True, exist_ok=True)
SOURCE = Path(__file__).resolve().parent
random.seed(0xC4B1A)

bpy.ops.object.select_all(action="SELECT")
bpy.ops.object.delete(use_global=False)

PALETTE = [
    ("timber", (0.91, 0.79, 0.65, 1.0)),
    ("pine", (0.47, 0.70, 0.50, 1.0)),
    ("stone", (0.84, 0.86, 0.81, 1.0)),
    ("roof", (0.84, 0.81, 0.75, 1.0)),
    ("glass", (0.38, 0.57, 0.62, 1.0)),
]
MATERIALS = []
for name, color in PALETTE:
    material = bpy.data.materials.new(name)
    material.diffuse_color = color
    MATERIALS.append(material)


def finish_object(obj, material):
    obj.data.materials.clear()
    obj.data.materials.append(MATERIALS[material])
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    for polygon in obj.data.polygons:
        polygon.use_smooth = False
    return obj


def cube(parts, name, location, dimensions, material, bevel=0):
    bpy.ops.mesh.primitive_cube_add(size=1, location=location)
    obj = bpy.context.object
    obj.name = name
    obj.dimensions = dimensions
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    if bevel:
        modifier = obj.modifiers.new("owned_edge_bevel", "BEVEL")
        modifier.width = bevel
        modifier.segments = 1
        bpy.context.view_layer.objects.active = obj
        bpy.ops.object.modifier_apply(modifier=modifier.name)
    parts.append(finish_object(obj, material))
    return obj


def cylinder(parts, name, location, radius, length, material, vertices=10, rotation=None):
    bpy.ops.mesh.primitive_cylinder_add(
        vertices=vertices, radius=radius, depth=length, location=location,
        rotation=rotation or (0, 0, 0))
    obj = bpy.context.object
    obj.name = name
    parts.append(finish_object(obj, material))
    return obj


def cone(parts, name, location, radius, depth, material, vertices=11):
    bpy.ops.mesh.primitive_cone_add(
        vertices=vertices, radius1=radius, radius2=0.10, depth=depth,
        location=location)
    obj = bpy.context.object
    obj.name = name
    parts.append(finish_object(obj, material))
    return obj


def gable(parts, name, x):
    # Close the pitched roof's two triangular end openings. This is visual
    # infill under the roof, not a new collision/support wall.
    near, far = x - 0.055, x + 0.055
    points = [(near, -3.05, 0.02), (near, 3.05, 0.02), (near, 0, 0.73),
              (far, -3.05, 0.02), (far, 3.05, 0.02), (far, 0, 0.73)]
    faces = [(0, 2, 1), (3, 4, 5), (0, 1, 4, 3),
             (1, 2, 5, 4), (2, 0, 3, 5)]
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata(points, [], faces)
    mesh.update()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    parts.append(finish_object(obj, 0))
    return obj


def join(parts, name):
    bpy.ops.object.select_all(action="DESELECT")
    for obj in parts:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = parts[0]
    bpy.ops.object.join()
    result = bpy.context.object
    result.name = name
    # Generated primitives have UVs, but joining multiple sources can leave a few
    # unwrapped faces. Smart project is deterministic for this fixed geometry.
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(island_margin=0.008)
    bpy.ops.object.mode_set(mode="OBJECT")
    return result


def export_mesh(obj, stem):
    mesh = obj.data
    mesh.calc_loop_triangles()
    uv_layer = mesh.uv_layers.active
    if uv_layer is None:
        raise RuntimeError(f"{stem} lacks authored UVs")
    triangles = list(mesh.loop_triangles)
    slots = [MATERIALS.index(mesh.materials[triangle.material_index])
             for triangle in triangles]
    used = max(slots) + 1
    vertices = bytearray()
    for triangle, slot in zip(triangles, slots):
        # Blender is Z-up; Godot is Y-up. Swap the last two triangle vertices
        # because Godot's native front-face order is opposite the canonical cross.
        for loop_id in (triangle.loops[0], triangle.loops[2], triangle.loops[1]):
            loop = mesh.loops[loop_id]
            point = mesh.vertices[loop.vertex_index].co
            normal = triangle.normal
            uv = uv_layer.data[loop_id].uv
            # One broad painted field across each assembled wall prevents the
            # same knot/edge pattern from repeating on all eight individual logs.
            if stem.startswith("wall-"):
                wall_length = float(stem[5:-1])
                uv = ((point.x + wall_length * 0.5) / wall_length,
                      point.z / 2.5)
            tint = PALETTE[slot][1]
            vertices.extend(struct.pack(
                "<3f3f2f4fI", point.x, point.z, -point.y,
                normal.x, normal.z, -normal.y,
                uv[0], uv[1], *tint, slot))
    count = len(triangles) * 3
    output = ROOT / (stem + ".usmesh")
    output.write_bytes(struct.pack("<4sIII", b"USWM", 1, count, used) + vertices)
    print(f"P9 MESH {stem}: {len(triangles)} triangles, {used} material slots, {output}")


MODELS = []


def record(parts, stem):
    obj = join(parts, stem)
    export_mesh(obj, stem)
    MODELS.append(obj)


# Pine: silhouette carried by overlapping branch whorls, not alpha cards.
parts = []
cylinder(parts, "pine_tapered_trunk", (0, 0, 2.05), 0.15, 4.10, 0, 9)
for level, (height, radius, depth) in enumerate([
        (1.35, 1.08, 1.30), (2.05, 0.94, 1.34),
        (2.77, 0.78, 1.34), (3.45, 0.60, 1.20), (4.04, 0.36, 0.88)]):
    cone(parts, f"pine_whorl_{level}", (0, 0, height), radius, depth, 1)
record(parts, "pine")


def rock(stem, seed, scale):
    parts = []
    bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=2, radius=1)
    obj = bpy.context.object
    obj.name = stem
    rng = random.Random(seed)
    for vertex in obj.data.vertices:
        wobble = 0.83 + rng.random() * 0.25
        vertex.co.x *= scale[0] * wobble
        vertex.co.y *= scale[1] * wobble
        vertex.co.z = (vertex.co.z * scale[2] * wobble + scale[2]) * 0.5
    parts.append(finish_object(obj, 2))
    record(parts, stem)


rock("rock-a", 271, (0.72, 0.58, 0.92))
rock("rock-b", 719, (0.62, 0.87, 0.68))


def wall(stem, length, window=False):
    parts = []
    for row in range(8):
        z = 0.17 + row * 0.305
        if window and row in (3, 4, 5):
            segment = (length - 1.15) * 0.5
            cylinder(parts, f"log_{row}_a", (-(length + 1.15) * 0.25, 0, z),
                     0.18, segment + 0.1, 0, rotation=(0, math.pi / 2, 0))
            cylinder(parts, f"log_{row}_b", ((length + 1.15) * 0.25, 0, z),
                     0.18, segment + 0.1, 0, rotation=(0, math.pi / 2, 0))
        else:
            cylinder(parts, f"log_{row}", (0, 0, z),
                     0.18, length + 0.12, 0, rotation=(0, math.pi / 2, 0))
    if window:
        cube(parts, "glass", (0, 0, 1.38), (1.02, 0.08, 0.92), 4)
        for x in (-0.58, 0.58):
            cube(parts, "frame_side", (x, 0, 1.38), (0.12, 0.43, 1.17), 0)
        for z in (0.81, 1.95):
            cube(parts, "frame_cross", (0, 0, z), (1.27, 0.43, 0.12), 0)
        cube(parts, "mullion", (0, -0.16, 1.38), (0.09, 0.13, 1.04), 0)
    record(parts, stem)


wall("wall-8m", 8.0)
wall("wall-6m", 6.0, window=True)
wall("wall-3m", 3.0)


parts = []
for side in (-1, 1):
    slab = cube(parts, "pitched_roof", (0, side * 1.55, 0.38),
                (8.62, 3.40, 0.19), 3)
    # Rotation is baked after moving the slab around its own centre.
    # Eaves meet the 2.5 m wall top while the ridge rises above it. The
    # opposite sign makes an inverted valley and exposes sky from inside.
    slab.rotation_euler.x = -side * 0.19
    bpy.context.view_layer.objects.active = slab
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=False)
cylinder(parts, "ridge_cap", (0, 0, 0.72), 0.10, 8.65, 0,
         rotation=(0, math.pi / 2, 0))
gable(parts, "west_gable_infill", -4.02)
gable(parts, "east_gable_infill", 4.02)
record(parts, "roof")


parts = []
for board in range(5):
    cube(parts, "door_board", ((board - 2) * 0.31, 0, 1.05),
         (0.31, 0.21, 2.10), 0, 0.025)
for z in (0.34, 1.70):
    cube(parts, "door_brace", (0, -0.16, z), (1.48, 0.085, 0.14), 0)
cylinder(parts, "iron_knob", (0.55, -0.16, 1.03), 0.07, 0.15, 2, 8,
         rotation=(math.pi / 2, 0, 0))
record(parts, "door")


parts = []
for x in (-0.39, 0.39):
    cube(parts, "shelf_post", (x, 0, 0.9), (0.10, 0.37, 1.80), 0)
for z in (0.09, 0.63, 1.18, 1.75):
    cube(parts, "shelf_board", (0, 0, z), (0.90, 0.38, 0.10), 0)
record(parts, "shelf")


parts = []
cube(parts, "book_cover", (0, 0, 0.095), (0.24, 0.18, 0.19), 0, 0.012)
record(parts, "book")


parts = []
cube(parts, "chest_body", (0, 0, 0.34), (1.20, 0.80, 0.68), 0, 0.045)
cube(parts, "chest_lid", (0, 0, 0.72), (1.25, 0.82, 0.19), 0, 0.035)
for x in (-0.48, 0.48):
    cube(parts, "chest_iron_band", (x, -0.43, 0.43), (0.06, 0.035, 0.75), 2)
record(parts, "chest")


# Park models side-by-side only in the editable gallery. Exported meshes remain bottom-anchored.
for index, obj in enumerate(MODELS):
    obj.location.x = index * 11
bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE / "cabin-p9.blend"))
