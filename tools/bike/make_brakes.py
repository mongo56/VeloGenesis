"""
Builds the disc brakes in Blender: assets/bike/rotor.glb (spins with the wheel) and assets/bike/caliper.glb.

Rotor: 160 mm steel braking band drilled with a staggered hole pattern, riveted to a dark six-arm centre-lock
carrier with curved spokes. The rear wheel scales it to 140 mm.
Caliper: two-piston flat-mount body straddling the rotor, with a rounded top bridge and bolt heads.

    blender -b --factory-startup -P tools/bike/make_brakes.py

Rotor origin: hub centre, rotor plane is Blender YZ (normal +X), like the wheel's axle.
Caliper origin: on the rotor's outer edge; local +Z points out from the hub, +Y along the rim, slot in x = 0.
Meshes: rotor.glb "Band" (steel), "Carrier"; caliper.glb "Caliper".
"""

import math
import os

import bmesh
import bpy
from mathutils import Matrix, Vector

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT_DIR = os.path.join(ROOT, "assets", "bike")

R = 0.08            # rotor radius (160 mm)
BAND = 0.0155       # braking band width
THICK = 0.0018      # rotor thickness
ARMS = 6


def link(obj):
    bpy.context.collection.objects.link(obj)
    return obj


def obj_from(bm, name):
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    return link(bpy.data.objects.new(name, me))


def disc(bm, r_out, r_in, thick, x=0.0, segs=96):
    """Flat ring in the YZ plane (or a full disc when r_in is 0)."""
    m = bmesh.ops.create_circle(bm, cap_ends=False, segments=segs, radius=r_out)
    outer = m["verts"]
    if r_in > 0:
        inner = bmesh.ops.create_circle(bm, cap_ends=False, segments=segs, radius=r_in)["verts"]
        for i in range(segs):
            bm.faces.new((outer[i], outer[(i + 1) % segs], inner[(i + 1) % segs], inner[i]))
    else:
        bm.faces.new(outer)
    geom = [f for f in bm.faces if all(v in set(outer) | (set(inner) if r_in > 0 else set()) for v in f.verts)]
    ext = bmesh.ops.extrude_face_region(bm, geom=geom)
    moved = [v for v in ext["geom"] if isinstance(v, bmesh.types.BMVert)]
    bmesh.ops.translate(bm, verts=moved, vec=Vector((0, 0, thick)))
    allv = list(set(outer) | (set(inner) if r_in > 0 else set()) | set(moved))
    bmesh.ops.translate(bm, verts=allv, vec=Vector((0, 0, -thick / 2)))
    bmesh.ops.rotate(bm, verts=allv, cent=Vector(), matrix=Matrix.Rotation(math.pi / 2, 3, "Y"))
    bmesh.ops.translate(bm, verts=allv, vec=Vector((x, 0, 0)))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)


def cylinder_x(bm, center, r, length, segs=16):
    m = bmesh.ops.create_cone(bm, cap_ends=True, segments=segs, radius1=r, radius2=r, depth=length)
    bmesh.ops.rotate(bm, verts=m["verts"], cent=Vector(), matrix=Matrix.Rotation(math.pi / 2, 3, "Y"))
    bmesh.ops.translate(bm, verts=m["verts"], vec=center)


def boolean(target, cutter, op):
    m = target.modifiers.new("Bool", "BOOLEAN")
    m.operation = op
    m.solver = "EXACT"
    m.object = cutter
    bpy.context.view_layer.objects.active = target
    bpy.ops.object.modifier_apply(modifier=m.name)
    bpy.data.objects.remove(cutter)


def band():
    bm = bmesh.new()
    disc(bm, R, R - BAND, THICK)
    ring = obj_from(bm, "Band")
    # Staggered drillings: pairs of holes across the band, rotated a little each row like modern rotors.
    holes = bmesh.new()
    n = 30
    for i in range(n):
        a = 2 * math.pi * i / n
        for k, (rr, da, hr) in enumerate(((R - 0.0045, 0.0, 0.0021), (R - 0.0105, 0.07, 0.0018))):
            p = Vector((0, math.cos(a + da) * rr, math.sin(a + da) * rr))
            cylinder_x(holes, p, hr, 0.01, 12)
    boolean(ring, obj_from(holes, "Holes"), "DIFFERENCE")
    # Shallow wave along the outer edge (scallops between the hole rows).
    cut = bmesh.new()
    for i in range(n):
        a = 2 * math.pi * (i + 0.5) / n
        cylinder_x(cut, Vector((0, math.cos(a) * (R + 0.0035), math.sin(a) * (R + 0.0035))), 0.0048, 0.01, 16)
    boolean(ring, obj_from(cut, "Scallops"), "DIFFERENCE")
    # Rivets where the carrier arms meet the band.
    bm = bmesh.new()
    bm.from_mesh(ring.data)
    for i in range(ARMS):
        a = 2 * math.pi * i / ARMS + 0.42
        cylinder_x(bm, Vector((0, math.cos(a) * (R - BAND + 0.002), math.sin(a) * (R - BAND + 0.002))), 0.0026, THICK + 0.003, 12)
    bm.to_mesh(ring.data)
    bm.free()
    return ring


def carrier():
    """Centre-lock carrier: a hub ring and six arms sweeping round to the band."""
    bm = bmesh.new()
    disc(bm, 0.027, 0.0165, 0.0032, segs=48)
    width, segs = 0.0072, 10
    r0, r1 = 0.024, R - BAND + 0.004
    for i in range(ARMS):
        base = 2 * math.pi * i / ARMS
        left, right = [], []
        for k in range(segs + 1):
            t = k / segs
            r = r0 + (r1 - r0) * t
            a = base + 0.42 * t * t  # arms curve towards the direction of rotation
            c = Vector((0, math.cos(a) * r, math.sin(a) * r))
            tang = Vector((0, -math.sin(a), math.cos(a)))
            w = width * (1.15 - 0.3 * t)
            left.append(c + tang * w / 2)
            right.append(c - tang * w / 2)
        h = 0.0028
        top_l = [bm.verts.new(p + Vector((h / 2, 0, 0))) for p in left]
        top_r = [bm.verts.new(p + Vector((h / 2, 0, 0))) for p in right]
        bot_l = [bm.verts.new(p - Vector((h / 2, 0, 0))) for p in left]
        bot_r = [bm.verts.new(p - Vector((h / 2, 0, 0))) for p in right]
        for k in range(segs):
            bm.faces.new((top_l[k], top_l[k + 1], top_r[k + 1], top_r[k]))
            bm.faces.new((bot_l[k], bot_r[k], bot_r[k + 1], bot_l[k + 1]))
            bm.faces.new((top_l[k], bot_l[k], bot_l[k + 1], top_l[k + 1]))
            bm.faces.new((top_r[k], top_r[k + 1], bot_r[k + 1], bot_r[k]))
        bm.faces.new((top_l[0], top_r[0], bot_r[0], bot_l[0]))
        bm.faces.new((top_l[-1], bot_l[-1], bot_r[-1], top_r[-1]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return obj_from(bm, "Carrier")


def caliper():
    """Body straddling the rotor (slot at x = 0), pads over the band, bridge on top."""
    bm = bmesh.new()
    m = bmesh.ops.create_cube(bm, size=1.0)
    bmesh.ops.scale(bm, verts=m["verts"], vec=Vector((0.03, 0.056, 0.03)))
    bmesh.ops.translate(bm, verts=m["verts"], vec=Vector((0, 0, -0.004)))
    body = obj_from(bm, "Caliper")
    bev = body.modifiers.new("Bevel", "BEVEL")
    bev.width = 0.007
    bev.segments = 4
    bpy.context.view_layer.objects.active = body
    bpy.ops.object.modifier_apply(modifier=bev.name)
    slot = bmesh.new()
    m = bmesh.ops.create_cube(slot, size=1.0)
    bmesh.ops.scale(slot, verts=m["verts"], vec=Vector((0.0045, 0.08, 0.04)))
    bmesh.ops.translate(slot, verts=m["verts"], vec=Vector((0, 0, -0.016)))
    boolean(body, obj_from(slot, "Slot"), "DIFFERENCE")
    # Piston bulges on both halves and the two mounting bolt heads on the inside face.
    bm = bmesh.new()
    bm.from_mesh(body.data)
    for s in (-1, 1):
        cylinder_x(bm, Vector((s * 0.0155, 0, -0.008)), 0.011, 0.006, 24)
    for y in (-0.018, 0.018):
        cylinder_x(bm, Vector((-0.0175, y, 0.006)), 0.0042, 0.004, 6)
    bm.to_mesh(body.data)
    bm.free()
    return body


def export(objs, name):
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        bpy.context.view_layer.objects.active = o
        o.select_set(True)
        bpy.ops.object.shade_auto_smooth(angle=math.radians(35))
    path = os.path.join(OUT_DIR, name)
    bpy.ops.export_scene.gltf(filepath=path, export_format="GLB", use_selection=True, export_materials="NONE",
                              export_yup=True, export_apply=True)
    print("wrote", path, [(o.name, len(o.data.polygons)) for o in objs])


def main():
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete()
    os.makedirs(OUT_DIR, exist_ok=True)
    export([band(), carrier()], "rotor.glb")
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete()
    export([caliper()], "caliper.glb")


main()
