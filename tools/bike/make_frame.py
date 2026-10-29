"""
Builds the bike's main frame in Blender and exports it to assets/bike/frame.glb.

Down tube, top tube, seat tube, bottom-bracket shell, chain stays, seat stays and rear dropouts are swept
separately, then voxel-remeshed into one surface so every joint gets a soft fillet (the head tube is part of
the fork, see make_fork.py). The rear thru-axle is a separate mesh.

    blender -b --factory-startup -P tools/bike/make_frame.py

Origin: the bike rig origin. Blender (x, y, z) = Godot (x, −z, y), so +Y is the bike's forward. The points
below are RiderAvatar's frame constants converted that way; keep them in sync.
Meshes: "Frame" (frame paint), "Axle".
"""

import math
import os
import sys

import bmesh
import bpy
from mathutils import Matrix, Vector

sys.path.insert(0, os.path.dirname(__file__))
from shapes import catmull, finish, link, sweep  # noqa: E402

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT = os.path.join(ROOT, "assets", "bike", "frame.glb")


def g(x, y, z):
    """Godot rig coordinates → Blender."""
    return Vector((x, -z, y))


BB = g(0, 0.29, 0)
REAR_HUB = g(0, 0.36, 0.415)
SEAT_TUBE_TOP = g(0, 0.81, 0.117)
SEAT_CLUSTER = g(0, 0.77, 0.108)
SEAT_STAY_JOIN = g(0, 0.645, 0.08)
DOWN_TUBE_END = g(0, 0.82, -0.447)   # HeadBottom + (0, 0.03, 0.016): inside the tapered head tube
TOP_TUBE_END = g(0, 0.858, -0.413)   # HeadTop + (0, −0.025, 0.012)
X = Vector((1, 0, 0))


def round_tube(name, a, b, r0, r1=None, n=24):
    r1 = r0 if r1 is None else r1
    path = [a.lerp(b, i / (n - 1)) for i in range(n)]
    return sweep(name, path, lambda t: (r0 + (r1 - r0) * t, r0 + (r1 - r0) * t, 2.0), X, 24)


def main_triangle():
    return [
        # Down tube: big and slightly oval (wider than deep) at the bottom bracket.
        sweep("DownTube", [BB.lerp(DOWN_TUBE_END, i / 23) for i in range(24)],
              lambda t: (0.033 - 0.003 * t, 0.029 + 0.001 * t, 2.2), X, 24),
        round_tube("SeatTube", g(0, 0.27, 0.0), SEAT_TUBE_TOP, 0.019, 0.017),
        round_tube("TopTube", SEAT_CLUSTER + Vector((0, -0.006, -0.004)), TOP_TUBE_END, 0.016, 0.018),
        # Bottom-bracket shell (86.5 mm press-fit).
        sweep("Shell", [BB + X * (-0.043 + 0.086 * i / 11) for i in range(12)],
              lambda t: (0.024, 0.024, 2.0), Vector((0, 1, 0)), 28),
    ]


def stays(s):
    """Chain stay, seat stay and dropout on one side (s = −1 left, +1 right)."""
    hub = REAR_HUB + X * (s * 0.066)
    chain = catmull([BB + Vector((s * 0.022, -0.02, 0.0)),
                     BB + Vector((s * 0.045, -0.1, 0.012)),
                     BB + Vector((s * 0.06, -0.3, 0.05)),
                     hub + Vector((0, 0.012, -0.002))], 40)
    # Tall and narrow at the shell (stiffness, chainring clearance), tapering to the dropout.
    cs = sweep("ChainStay", chain, lambda t: (0.0105 - 0.0035 * t, 0.016 - 0.007 * t, 2.3), X)
    seat = catmull([SEAT_STAY_JOIN + Vector((s * 0.005, -0.003, 0.012)),   # buried in the seat tube
                    SEAT_STAY_JOIN + Vector((s * 0.02, -0.022, -0.012)),
                    SEAT_STAY_JOIN.lerp(hub, 0.55) + Vector((0, 0.004, 0)),
                    hub + Vector((0, 0.01, 0.014))], 40)
    ss = sweep("SeatStay", seat, lambda t: (0.0085 - 0.002 * t, 0.0105 - 0.003 * t, 2.2), X)
    # Dropout: a flat plate wrapping round the axle where the stays meet.
    plate = catmull([hub + Vector((0, 0.03, -0.006)), hub + Vector((0, -0.004, -0.012)),
                     hub + Vector((0, -0.012, 0.006)), hub + Vector((0, 0.008, 0.026))], 20)
    do = sweep("Dropout", plate, lambda t: (0.0045, 0.014 - 0.003 * abs(t - 0.5), 3.0), X)
    return [cs, ss, do]


def axle():
    """12 mm rear thru-axle, flush head on the left, lever on the right folded along the chain stay."""
    bm = bmesh.new()

    def cyl(x0, x1, r, segs=24):
        m = bmesh.ops.create_cone(bm, cap_ends=True, segments=segs, radius1=r, radius2=r, depth=x1 - x0)
        bmesh.ops.rotate(bm, verts=m["verts"], cent=Vector(), matrix=Matrix.Rotation(math.pi / 2, 3, "Y"))
        bmesh.ops.translate(bm, verts=m["verts"], vec=REAR_HUB + X * ((x0 + x1) / 2))
    cyl(-0.078, 0.078, 0.006)
    cyl(-0.079, -0.071, 0.0105)
    cyl(0.071, 0.079, 0.011)
    fwd = (BB - REAR_HUB).normalized()
    lever = bmesh.ops.create_cone(bm, cap_ends=True, segments=12, radius1=0.0065, radius2=0.004, depth=0.042)
    bmesh.ops.scale(bm, verts=lever["verts"], vec=Vector((0.5, 1.0, 1.0)))
    bmesh.ops.rotate(bm, verts=lever["verts"], cent=Vector(), matrix=Vector((0, 0, 1)).rotation_difference(fwd).to_matrix())
    bmesh.ops.translate(bm, verts=lever["verts"], vec=REAR_HUB + X * 0.082 + fwd * 0.022)
    me = bpy.data.meshes.new("Axle")
    bm.to_mesh(me)
    bm.free()
    return link(bpy.data.objects.new("Axle", me))


def main():
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete()
    parts = main_triangle() + stays(-1) + stays(1)
    bpy.ops.object.select_all(action="DESELECT")
    for p in parts:
        p.select_set(True)
    bpy.context.view_layer.objects.active = parts[0]
    bpy.ops.object.join()
    frame = parts[0]
    frame.name = "Frame"
    finish(frame, voxel=0.0014, smooth=4, faces=14000)
    objs = [frame, finish(axle())]
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        o.select_set(True)
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    bpy.ops.export_scene.gltf(filepath=OUT, export_format="GLB", use_selection=True, export_materials="NONE",
                              export_yup=True, export_apply=True)
    print("wrote", OUT, [(o.name, len(o.data.polygons)) for o in objs])


main()
