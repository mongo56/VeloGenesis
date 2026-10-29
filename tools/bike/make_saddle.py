"""
Builds the bike's saddle in Blender and exports it to assets/bike/saddle.glb.

A modern road / gravel saddle (≈270 mm long, 140 mm wide): wide rounded rear with a slight kick-up,
narrow nose, crowned padded top with a pressure-relief channel down the middle, thin shell underneath,
and two curved rails. Built as a cage of cross-sections and smoothed with a subdivision surface.

    blender -b --factory-startup -P tools/bike/make_saddle.py

Origin: middle of the rail clamp area. Nose points along +Y in Blender (−Z in Godot, the bike's forward).
Two meshes: "Saddle" (cover + shell) and "Rails".
"""

import math
import os

import bmesh
import bpy

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT = os.path.join(ROOT, "assets", "bike", "saddle.glb")

LENGTH = 0.27
RINGS = 22       # cross-sections from the rear (t = 0) to the nose (t = 1)
ACROSS = 10      # points across the top; the bottom mirrors them


def lerp_table(table, t):
    for (t0, v0), (t1, v1) in zip(table, table[1:]):
        if t <= t1:
            k = (t - t0) / max(t1 - t0, 1e-9)
            k = k * k * (3 - 2 * k)
            return v0 + (v1 - v0) * k
    return table[-1][1]


HALF_WIDTH = [(0.0, 0.052), (0.06, 0.068), (0.2, 0.07), (0.38, 0.062), (0.55, 0.036), (0.7, 0.022), (0.88, 0.018), (1.0, 0.012)]
TOP = [(0.0, 0.052), (0.1, 0.046), (0.3, 0.041), (0.6, 0.039), (0.85, 0.038), (1.0, 0.033)]   # padding top height
THICK = [(0.0, 0.016), (0.15, 0.03), (0.6, 0.026), (1.0, 0.016)]                             # cover + shell
CHANNEL = [(0.0, 0.0), (0.12, 0.0), (0.25, 0.006), (0.6, 0.006), (0.78, 0.0), (1.0, 0.0)]     # relief channel depth


def build_saddle():
    bm = bmesh.new()
    rings = []
    for i in range(RINGS + 1):
        t = i / RINGS
        y = (t - 0.5) * LENGTH
        w = lerp_table(HALF_WIDTH, t)
        top = lerp_table(TOP, t)
        thick = lerp_table(THICK, t)
        ch = lerp_table(CHANNEL, t)
        ring = []
        # Top: crowned across, rolling over the edges, with the channel down the middle.
        for j in range(ACROSS + 1):
            s = -1 + 2 * j / ACROSS
            crown = 0.007 * (1 - s * s)
            edge = 0.012 * abs(s) ** 6
            channel = ch * math.exp(-(s / 0.16) ** 2)
            ring.append(bm.verts.new((w * s, y, top + crown - edge - channel)))
        # Underside shell: slightly narrower, flat with a gentle dome.
        for j in range(ACROSS, -1, -1):
            s = -1 + 2 * j / ACROSS
            ring.append(bm.verts.new((w * 0.93 * s, y, top - thick + 0.004 * (1 - s * s))))
        rings.append(ring)
    n = len(rings[0])
    for a, b in zip(rings, rings[1:]):
        for k in range(n):
            bm.faces.new((a[k], a[(k + 1) % n], b[(k + 1) % n], b[k]))
    bm.faces.new(list(reversed(rings[0])))
    bm.faces.new(rings[-1])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    me = bpy.data.meshes.new("Saddle")
    bm.to_mesh(me)
    bm.free()
    obj = bpy.data.objects.new("Saddle", me)
    bpy.context.collection.objects.link(obj)
    mod = obj.modifiers.new("Smooth", "SUBSURF")
    mod.levels = mod.render_levels = 2
    return obj


def build_rails():
    """Two 7 mm rails: from under the nose, down and back to the clamp zone, and up into the rear shell."""
    curve = bpy.data.curves.new("Rails", "CURVE")
    curve.dimensions = "3D"
    curve.bevel_depth = 0.0035
    curve.bevel_resolution = 3
    for side in (-1, 1):
        sp = curve.splines.new("BEZIER")
        pts = [(0.012, 0.115, 0.022), (0.022, 0.07, 0.0), (0.03, 0.0, -0.004), (0.036, -0.06, 0.0), (0.04, -0.105, 0.025)]
        sp.bezier_points.add(len(pts) - 1)
        for bp, (x, y, z) in zip(sp.bezier_points, pts):
            bp.co = (side * x, y, z)
            bp.handle_left_type = bp.handle_right_type = "AUTO"
    obj = bpy.data.objects.new("Rails", curve)
    bpy.context.collection.objects.link(obj)
    return obj


def main():
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete()
    saddle = build_saddle()
    rails = build_rails()
    for obj in (saddle, rails):
        bpy.context.view_layer.objects.active = obj
        obj.select_set(True)
        bpy.ops.object.convert(target="MESH")
        bpy.ops.object.shade_smooth()
        obj.select_set(False)
    saddle.select_set(True)
    rails.select_set(True)
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    bpy.ops.export_scene.gltf(filepath=OUT, export_format="GLB", use_selection=True, export_materials="NONE",
                              export_yup=True, export_apply=True)
    print(f"wrote {OUT}: {len(saddle.data.polygons)} faces")


main()
