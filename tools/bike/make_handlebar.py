"""
Builds the bike's cockpit in Blender and exports it to assets/bike/handlebar.glb.

A compact gravel drop bar (≈420 mm at the hoods, slight flare in the drops), taped, with a 31.8 mm clamp
bulge; shaped brake/shift hoods with the raised nose of modern levers; curved lever blades; and an 80 mm
stem with steerer clamp and faceplate.

    blender -b --factory-startup -P tools/bike/make_handlebar.py

Origin: centre of the bar clamp. Blender +Y is the bike's forward (Godot −Z), +Z up. The bar follows the
path RiderAvatar's hand IK uses (hoods at HoodOffset, drops 120 mm below the tops).
Meshes: "Bar" (tape), "Clamp", "Hoods", "Levers", "Stem".
"""

import math
import os

import bmesh
import bpy

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT = os.path.join(ROOT, "assets", "bike", "handlebar.glb")

STEM = 0.08
# Bar centre line for the right half (x outward, y forward, z up), from the clamp to the bar end.
BAR = [(0.0, 0.0, 0.0), (0.12, -0.004, 0.0), (0.195, 0.005, 0.0), (0.205, 0.05, 0.004), (0.212, 0.085, -0.03),
       (0.226, 0.075, -0.095), (0.236, 0.03, -0.13), (0.245, -0.035, -0.125)]
TAPE_R = 0.0125


def link(obj):
    bpy.context.collection.objects.link(obj)
    return obj


def to_mesh(obj, smooth=True):
    bpy.ops.object.select_all(action="DESELECT")
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    bpy.ops.object.convert(target="MESH")
    if smooth:
        bpy.ops.object.shade_smooth()
    obj.select_set(False)
    return obj


def tube(name, points, radius, resolution=12):
    curve = bpy.data.curves.new(name, "CURVE")
    curve.dimensions = "3D"
    curve.bevel_depth = radius
    curve.bevel_resolution = 4
    curve.resolution_u = resolution
    curve.use_fill_caps = True
    sp = curve.splines.new("BEZIER")
    sp.bezier_points.add(len(points) - 1)
    for bp, co in zip(sp.bezier_points, points):
        bp.co = co
        bp.handle_left_type = bp.handle_right_type = "AUTO"
    return link(bpy.data.objects.new(name, curve))


def bar():
    """Both halves of the taped bar, joined."""
    halves = []
    for side in (-1, 1):
        pts = [(side * x, y, z) for x, y, z in BAR]
        halves.append(to_mesh(tube("Bar", pts, TAPE_R, 16)))
    for h in halves:
        h.select_set(True)
    bpy.context.view_layer.objects.active = halves[0]
    bpy.ops.object.join()
    halves[0].name = "Bar"
    return halves[0]


def loft(name, sections):
    """Closed loft through rings of points (each ring the same length)."""
    bm = bmesh.new()
    rings = [[bm.verts.new(p) for p in ring] for ring in sections]
    n = len(rings[0])
    for a, b in zip(rings, rings[1:]):
        for k in range(n):
            bm.faces.new((a[k], a[(k + 1) % n], b[(k + 1) % n], b[k]))
    bm.faces.new(list(reversed(rings[0])))
    bm.faces.new(rings[-1])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    obj = link(bpy.data.objects.new(name, me))
    mod = obj.modifiers.new("Smooth", "SUBSURF")
    mod.levels = mod.render_levels = 2
    return obj


def superellipse(cx, cz, w, h, y, n=16, p=3.0):
    pts = []
    for k in range(n):
        a = 2 * math.pi * k / n
        c, s = math.cos(a), math.sin(a)
        pts.append((cx + w * math.copysign(abs(c) ** (2 / p), c), y, cz + h * math.copysign(abs(s) ** (2 / p), s)))
    return pts


def hoods():
    """Lever hoods: sit on the bar's ramp, rise to a nose at the front."""
    parts = []
    for side in (-1, 1):
        sections = []
        for i in range(9):
            t = i / 8
            y = 0.03 + 0.085 * t
            bottom = 0.002 - 0.05 * t * t
            top = 0.028 + 0.006 * math.sin(math.pi * t) + 0.024 * t ** 5
            w = 0.017 - 0.004 * t
            sections.append(superellipse(side * (0.205 + 0.004 * t), (top + bottom) / 2, w, (top - bottom) / 2, y))
        parts.append(to_mesh(loft("Hoods", sections)))
    for h in parts:
        h.select_set(True)
    bpy.context.view_layer.objects.active = parts[0]
    bpy.ops.object.join()
    parts[0].name = "Hoods"
    return parts[0]


def levers():
    blades = []
    for side in (-1, 1):
        pts = [(side * 0.209, 0.108, -0.035), (side * 0.214, 0.112, -0.07), (side * 0.222, 0.098, -0.11), (side * 0.228, 0.08, -0.135)]
        blade = tube("Levers", pts, 0.0065, 10)
        blade.data.bevel_resolution = 2
        blade.scale = (1.0, 1.0, 1.0)
        blades.append(to_mesh(blade))
        # Flatten the round blade sideways into a lever shape.
        for v in blades[-1].data.vertices:
            v.co.x = side * 0.214 + (v.co.x - side * 0.214) * 0.55
    for b in blades:
        b.select_set(True)
    bpy.context.view_layer.objects.active = blades[0]
    bpy.ops.object.join()
    blades[0].name = "Levers"
    return blades[0]


def clamp():
    bpy.ops.mesh.primitive_cylinder_add(vertices=32, radius=0.0159, depth=0.1, rotation=(0, math.pi / 2, 0))
    c = bpy.context.active_object
    c.name = "Clamp"
    bevel = c.modifiers.new("Bevel", "BEVEL")
    bevel.width = 0.004
    bevel.segments = 3
    return to_mesh(c)


def stem():
    """80 mm, −10° stem: rounded body from the steerer clamp to a faceplate round the bar."""
    steerer = (0.0, -STEM * 0.985, -STEM * 0.17)
    parts = []
    bpy.ops.mesh.primitive_cylinder_add(vertices=32, radius=0.021, depth=0.042, location=steerer)
    parts.append(bpy.context.active_object)
    bpy.ops.mesh.primitive_cylinder_add(vertices=32, radius=0.022, depth=0.042, location=(0, 0, 0), rotation=(0, math.pi / 2, 0))
    parts.append(bpy.context.active_object)
    body = []
    for i in range(7):
        t = i / 6
        y = steerer[1] + (0.0 - steerer[1]) * t
        z = steerer[2] + (0.0 - steerer[2]) * t
        body.append(superellipse(0.0, z, 0.017 - 0.003 * t, 0.016 - 0.002 * t, y, 16, 3.5))
    parts.append(to_mesh(loft("StemBody", body)))
    for p in parts:
        bev = p.modifiers.new("Bevel", "BEVEL")
        bev.width = 0.003
        bev.segments = 3
        to_mesh(p)
    for p in parts:
        p.select_set(True)
    bpy.context.view_layer.objects.active = parts[0]
    bpy.ops.object.join()
    parts[0].name = "Stem"
    return parts[0]


def main():
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete()
    objs = [bar(), clamp(), hoods(), levers(), stem()]
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        o.select_set(True)
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    bpy.ops.export_scene.gltf(filepath=OUT, export_format="GLB", use_selection=True, export_materials="NONE",
                              export_yup=True, export_apply=True)
    print("wrote", OUT, [(o.name, len(o.data.polygons)) for o in objs])


main()
