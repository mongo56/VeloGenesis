"""
Builds the bike's carbon gravel fork in Blender and exports it to assets/bike/fork.glb.

Tapered blades with an oval section (deep fore-aft, narrow side to side), a crown that flows into the bottom
of the head tube, dropouts wrapped round a 12 mm thru-axle, and the axle with its lever on the right.

    blender -b --factory-startup -P tools/bike/make_fork.py

Origin: RiderAvatar.HeadBottom. Blender +Y is the bike's forward (Godot −Z), +Z up. HUB must match
RiderAvatar.FrontHub − HeadBottom (Godot (0, −0.43, −0.226) → Blender (0, 0.226, −0.43)): 484 mm down the
steering axis with 47 mm of rake.
Meshes: "Fork" (frame paint, includes the head tube), "Axle".
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
OUT = os.path.join(ROOT, "assets", "bike", "fork.glb")

HUB = Vector((0.0, 0.226, -0.43))
DROPOUT_X = 0.06          # blade centre at the axle (100 mm hub)
CROWN_X = 0.05            # blade centre under the crown (clears a 45 mm tyre)
AXIS = Vector((0.0, -0.378, 0.926))  # head tube axis, upwards
FWD = Vector((0.0, 0.926, 0.378))    # perpendicular to it, forwards
HUB_U, RAKE = -HUB.dot(AXIS), HUB.dot(FWD)
HEAD_LEN = 0.1005  # RiderAvatar HeadBottom → HeadTop


def axial(u, v, x=0.0):
    """Point u metres down the steering axis from HeadBottom, v forward of it, x to the side."""
    return Vector((x, 0, 0)) - AXIS * u + FWD * v


def blade(s):
    """One fork leg: straight down the steering axis from the crown, curving forward into the rake at the
    dropout, like a carbon gravel fork."""
    pts = [axial(0.05, 0.0, s * 0.04),            # starts buried in the crown so its end never shows
           axial(0.095, 0.0, s * CROWN_X),
           axial(0.22, 0.003, s * 0.055),
           axial(0.34, 0.013, s * DROPOUT_X),
           axial(0.43, 0.032, s * DROPOUT_X),
           axial(HUB_U + 0.014, RAKE + 0.006, s * DROPOUT_X)]
    path = catmull(pts, 48)
    # Deep oval at the crown tapering to a slim leg; the last bit flattens into the dropout.
    def section(t):
        if t < 0.85:
            k = t / 0.85
            return (0.0145 - 0.0065 * k, 0.021 - 0.009 * k, 2.4)
        k = (t - 0.85) / 0.15
        return (0.008 - 0.0025 * k, 0.012 + 0.002 * k, 2.4 + 1.6 * k)
    return sweep("Blade", path, section, Vector((1, 0, 0)))


def crown():
    """Shoulders bridging the legs, square to the steering axis and arching up into the head tube."""
    path = catmull([axial(u, 0.0, x) for x, u in
                    ((-0.047, 0.1), (-0.034, 0.07), (0.0, 0.055), (0.034, 0.07), (0.047, 0.1))], 40)
    def section(t):
        # 0 at the shoulders, 1 in the middle; the ends shrink inside the blades so no collar shows.
        k = math.sqrt(1 - abs(t - 0.5) * 2)
        return (0.012 + 0.02 * k, 0.009 + 0.014 * k, 2.6)
    return sweep("Crown", path, section, FWD)


def head_tube():
    """The whole head tube, flaring at the bottom into the crown (tapered 1.5" steerer). Part of the fork mesh
    so there is no seam; it runs from 2.7 cm above RiderAvatar.HeadTop down into the crown."""
    a = AXIS.normalized()
    top, bottom = HEAD_LEN + 0.027, -0.085  # runs right through the crown so the two melt together
    path = [a * (top + (bottom - top) * i / 31) for i in range(32)]
    def section(t):
        h = top + (bottom - top) * t                      # height along the axis above HeadBottom
        below = max(0.0, -h) / -bottom                    # 0 above HeadBottom, 1 at the bottom
        # Tapered: 52 mm at the top tube, 64 mm where the down tube (60 mm) joins, then the crown flare.
        r = 0.026 + 0.006 * min(1.0, max(0.0, (HEAD_LEN - h) / HEAD_LEN)) + 0.003 * below
        return (r, r, 2.0)
    return sweep("HeadTube", path, section, Vector((1, 0, 0)), 28)


def axle():
    """12 mm thru-axle with a flush head on the left and a folding lever on the right."""
    bm = bmesh.new()
    def cyl(x0, x1, r, segs=24):
        m = bmesh.ops.create_cone(bm, cap_ends=True, segments=segs, radius1=r, radius2=r, depth=x1 - x0)
        bmesh.ops.rotate(bm, verts=m["verts"], cent=Vector(), matrix=Matrix.Rotation(math.pi / 2, 3, "Y"))
        bmesh.ops.translate(bm, verts=m["verts"], vec=HUB + Vector(((x0 + x1) / 2, 0, 0)))
    cyl(-DROPOUT_X - 0.012, DROPOUT_X + 0.012, 0.006)
    cyl(-DROPOUT_X - 0.014, -DROPOUT_X - 0.009, 0.0105)
    cyl(DROPOUT_X + 0.009, DROPOUT_X + 0.016, 0.011)
    # Lever, folded flat against the leg and pointing up it.
    up = (Vector((DROPOUT_X, 0.008, -0.07)) - HUB).normalized()
    lever = bmesh.ops.create_cone(bm, cap_ends=True, segments=12, radius1=0.0065, radius2=0.004, depth=0.042)
    bmesh.ops.scale(bm, verts=lever["verts"], vec=Vector((0.5, 1.0, 1.0)))
    bmesh.ops.rotate(bm, verts=lever["verts"], cent=Vector(), matrix=Vector((0, 0, 1)).rotation_difference(up).to_matrix())
    bmesh.ops.translate(bm, verts=lever["verts"], vec=HUB + Vector((DROPOUT_X + 0.016, 0, 0)) + up * 0.022)
    me = bpy.data.meshes.new("Axle")
    bm.to_mesh(me)
    bm.free()
    return link(bpy.data.objects.new("Axle", me))


def main():
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete()
    parts = [blade(-1), blade(1), crown(), head_tube()]
    bpy.ops.object.select_all(action="DESELECT")
    for p in parts:
        p.select_set(True)
    bpy.context.view_layer.objects.active = parts[0]
    bpy.ops.object.join()
    fork = parts[0]
    fork.name = "Fork"
    # One watertight surface: the remesh welds the legs, crown and head tube with soft fillets.
    finish(fork, voxel=0.0012, smooth=6, faces=9000)
    ax = finish(axle())
    objs = [fork, ax]
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        o.select_set(True)
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    bpy.ops.export_scene.gltf(filepath=OUT, export_format="GLB", use_selection=True, export_materials="NONE",
                              export_yup=True, export_apply=True)
    print("wrote", OUT, [(o.name, len(o.data.polygons)) for o in objs])


main()
