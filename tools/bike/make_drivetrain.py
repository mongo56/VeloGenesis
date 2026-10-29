"""
Builds the 1x drivetrain in Blender and exports it to assets/bike/drivetrain.glb. RiderAvatar takes the meshes
by name and places (and animates) each one itself.

  "Chainring"  42T narrow-wide ring, plane YZ, centred on the crank axis
  "Spider"     four-arm direct-mount carrier the ring bolts to, same frame
  "CrankArm"   hollow forged-look arm from the spindle (origin) to the pedal eye at +Z = CRANK, with the pedal
               spindle sticking out along +X (RiderAvatar mirrors it for the left side)
  "Pedal"      clipless pedal body centred on the origin, spindle along X
  "Cassette"   11-speed, largest cog inboard, origin on the hub axis
  "Derailleur" rear derailleur: hanger, parallelogram body and cage with two jockey wheels, origin at the hub

    blender -b --factory-startup -P tools/bike/make_drivetrain.py

Blender (x, y, z) = Godot (x, −z, y): +Y forward, +Z up.
"""

import math
import os
import sys

import bmesh
import bpy
from mathutils import Matrix, Vector

sys.path.insert(0, os.path.dirname(__file__))
from shapes import finish, link, sweep  # noqa: E402

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT = os.path.join(ROOT, "assets", "bike", "drivetrain.glb")

CRANK = 0.1725          # RiderAvatar.Crank
CAGE_X = 0.068          # chain line (RiderAvatar's cog offset); the derailleur cage sits on it
X = Vector((1, 0, 0))


def obj_from(bm, name):
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    return link(bpy.data.objects.new(name, me))


def toothed(bm, teeth, r_root, r_tip, r_inner, thick, c=Vector()):
    """Flat toothed ring in the YZ plane centred on c, `thick` wide in X."""
    outline = []
    for k in range(teeth):
        a = 2 * math.pi * k / teeth
        step = 2 * math.pi / teeth
        for frac, r in ((0.0, r_root), (0.3, r_tip), (0.5, r_tip), (0.8, r_root)):
            outline.append((a + step * frac, r))
    n = len(outline)
    rings = []
    for dx in (-thick / 2, thick / 2):
        outer = [bm.verts.new(c + Vector((dx, math.cos(a) * r, math.sin(a) * r))) for a, r in outline]
        inner = [bm.verts.new(c + Vector((dx, math.cos(a) * r_inner, math.sin(a) * r_inner))) for a, _ in outline]
        rings.append((outer, inner))
    (o0, i0), (o1, i1) = rings
    for j in range(n):
        k = (j + 1) % n
        bm.faces.new((o0[j], o0[k], i0[k], i0[j]))       # side faces
        bm.faces.new((o1[j], i1[j], i1[k], o1[k]))
        bm.faces.new((o0[j], o1[j], o1[k], o0[k]))       # tooth profile
        bm.faces.new((i0[j], i0[k], i1[k], i1[j]))       # bore
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)


def cyl_x(bm, center, r, length, segs=24):
    m = bmesh.ops.create_cone(bm, cap_ends=True, segments=segs, radius1=r, radius2=r, depth=length)
    bmesh.ops.rotate(bm, verts=m["verts"], cent=Vector(), matrix=Matrix.Rotation(math.pi / 2, 3, "Y"))
    bmesh.ops.translate(bm, verts=m["verts"], vec=center)


def chainring():
    bm = bmesh.new()
    toothed(bm, 42, 0.0905, 0.0965, 0.077, 0.0032)
    return obj_from(bm, "Chainring")


def spider():
    """Four curved arms from the crank interface out under the ring, plus the spindle boss."""
    parts = []
    for i in range(4):
        base = 2 * math.pi * i / 4 + 0.3
        path = []
        for k in range(12):
            t = k / 11
            r = 0.02 + 0.064 * t
            a = base - 0.25 * t * t
            path.append(Vector((0.003 - 0.002 * t, math.cos(a) * r, math.sin(a) * r)))
        parts.append(sweep("Arm", path, lambda t: (0.0035, 0.0095 - 0.003 * t, 2.6), X, 16))
    bm = bmesh.new()
    cyl_x(bm, Vector((0.004, 0, 0)), 0.026, 0.009, 36)
    parts.append(obj_from(bm, "Boss"))
    for p in parts:
        p.select_set(True)
    bpy.context.view_layer.objects.active = parts[0]
    bpy.ops.object.join()
    parts[0].name = "Spider"
    return finish(parts[0], voxel=0.0009, smooth=2, faces=3000)


def crank_arm():
    """Tapered hollow-forged arm: deep at the spindle, slim at the pedal eye, with rounded bosses."""
    path = [Vector((0, 0, CRANK * i / 23)) for i in range(24)]
    arm = sweep("Arm", path, lambda t: (0.0085 - 0.0015 * t, 0.0155 - 0.005 * t, 2.8), X, 24)
    bm = bmesh.new()
    cyl_x(bm, Vector((0, 0, 0)), 0.019, 0.018, 32)               # spindle boss
    cyl_x(bm, Vector((0, 0, CRANK)), 0.012, 0.015, 24)           # pedal eye
    cyl_x(bm, Vector((0.024, 0, CRANK)), 0.0045, 0.05, 16)       # pedal spindle, out to the pedal body
    ends = obj_from(bm, "Ends")
    arm.select_set(True)
    ends.select_set(True)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.join()
    arm.name = "CrankArm"
    return finish(arm, voxel=0.0008, smooth=2, faces=3000)


def pedal():
    """Clipless pedal: rounded body with a raised cleat binding, the spindle housing through it."""
    bm = bmesh.new()
    body = bmesh.ops.create_cube(bm, size=1.0)
    bmesh.ops.scale(bm, verts=body["verts"], vec=Vector((0.066, 0.062, 0.014)))
    bind = bmesh.ops.create_cube(bm, size=1.0)
    bmesh.ops.scale(bm, verts=bind["verts"], vec=Vector((0.05, 0.03, 0.02)))
    bmesh.ops.translate(bm, verts=bind["verts"], vec=Vector((0, 0.008, 0.002)))
    cyl_x(bm, Vector((-0.004, 0, 0)), 0.0085, 0.074, 20)
    obj = obj_from(bm, "Pedal")
    return finish(obj, voxel=0.0009, smooth=4, faces=1500)


def cassette():
    """11 cogs from 0.056 m (inboard) to 0.024 m radius, on a carrier."""
    bm = bmesh.new()
    for i in range(11):
        r = 0.056 - 0.032 * i / 10
        teeth = max(11, round(2 * math.pi * r / 0.0127))
        toothed(bm, teeth, r - 0.003, r + 0.0015, 0.016, 0.0019, X * (-0.019 + 0.0038 * i))
    cyl_x(bm, Vector((0.001, 0, 0)), 0.0175, 0.044, 32)
    return obj_from(bm, "Cassette")


def derailleur():
    """Hanger knuckle, parallelogram and cage below the right dropout; jockey wheels on the chain line."""
    parts = []
    bm = bmesh.new()
    cyl_x(bm, Vector((0.08, -0.008, -0.02)), 0.011, 0.012, 24)            # B-knuckle on the hanger
    parts.append(obj_from(bm, "Knuckle"))
    # Parallelogram body: a chunky swept link angled down and back to the cage pivot.
    body = [Vector((0.08, -0.01, -0.024)), Vector((0.081, -0.022, -0.045)), Vector((0.076, -0.016, -0.066))]
    parts.append(sweep("Body", body, lambda t: (0.009, 0.013, 3.0), X, 20))
    # Cage plates either side of the jockey wheels: upper under the cassette, lower further down and forward.
    up = Vector((CAGE_X, -0.006, -0.068))
    low = Vector((CAGE_X, 0.018, -0.128))
    for dx in (-0.0075, 0.0075):
        plate = [up.lerp(low, i / 9) + X * dx for i in range(10)]
        parts.append(sweep("Plate", plate, lambda t: (0.0012, 0.0145, 2.4), X, 16))
    bm = bmesh.new()
    for c in (up, low):
        toothed(bm, 12, 0.0105, 0.0128, 0.003, 0.005, c)
    parts.append(obj_from(bm, "Jockeys"))
    bpy.ops.object.select_all(action="DESELECT")
    for p in parts:
        p.select_set(True)
    bpy.context.view_layer.objects.active = parts[0]
    bpy.ops.object.join()
    parts[0].name = "Derailleur"
    return finish(parts[0])


def main():
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete()
    objs = [chainring(), spider(), crank_arm(), pedal(), cassette(), derailleur()]
    for o in objs:
        bpy.context.view_layer.objects.active = o
        bpy.ops.object.select_all(action="DESELECT")
        o.select_set(True)
        bpy.ops.object.shade_auto_smooth(angle=math.radians(40))
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        o.select_set(True)
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    bpy.ops.export_scene.gltf(filepath=OUT, export_format="GLB", use_selection=True, export_materials="NONE",
                              export_yup=True, export_apply=True)
    print("wrote", OUT, [(o.name, len(o.data.polygons)) for o in objs])


main()
