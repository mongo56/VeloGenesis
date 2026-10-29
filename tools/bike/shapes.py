"""Mesh-building helpers shared by the bike scripts (run inside Blender)."""

import math

import bmesh
import bpy


def link(obj):
    bpy.context.collection.objects.link(obj)
    return obj


def catmull(points, n):
    """Smooth path through `points`, n samples."""
    pts = [points[0]] + points + [points[-1]]
    out = []
    for i in range(n):
        u = i / (n - 1) * (len(points) - 1)
        k = min(int(u), len(points) - 2)
        t = u - k
        p0, p1, p2, p3 = pts[k], pts[k + 1], pts[k + 2], pts[k + 3]
        out.append(0.5 * ((2 * p1) + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t * t
                          + (-p0 + 3 * p1 - 3 * p2 + p3) * t * t * t))
    return out


def sweep(name, path, section, side_hint, segs=20):
    """Closed tube along `path`; section(t) → (half-size along side_hint, half-size across it, squareness)."""
    bm = bmesh.new()
    rings = []
    for i, c in enumerate(path):
        t = i / (len(path) - 1)
        tan = (path[min(i + 1, len(path) - 1)] - path[max(i - 1, 0)]).normalized()
        side = (side_hint - tan * side_hint.dot(tan)).normalized()
        other = tan.cross(side)
        a, b, p = section(t)
        ring = []
        for j in range(segs):
            th = 2 * math.pi * j / segs
            cs, sn = math.cos(th), math.sin(th)
            # Superellipse: p = 2 is an ellipse, higher is squarer.
            x = math.copysign(abs(cs) ** (2 / p), cs)
            y = math.copysign(abs(sn) ** (2 / p), sn)
            ring.append(bm.verts.new(c + side * (a * x) + other * (b * y)))
        rings.append(ring)
    for r0, r1 in zip(rings, rings[1:]):
        for j in range(segs):
            bm.faces.new((r0[j], r0[(j + 1) % segs], r1[(j + 1) % segs], r1[j]))
    bm.faces.new(list(reversed(rings[0])))
    bm.faces.new(rings[-1])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    return link(bpy.data.objects.new(name, me))


def finish(obj, voxel=None, smooth=0, faces=None):
    bpy.ops.object.select_all(action="DESELECT")
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    if voxel:
        m = obj.modifiers.new("Remesh", "REMESH")
        m.mode = "VOXEL"
        m.voxel_size = voxel
        bpy.ops.object.modifier_apply(modifier=m.name)
    if smooth:
        m = obj.modifiers.new("Smooth", "SMOOTH")
        m.factor = 0.5
        m.iterations = smooth
        bpy.ops.object.modifier_apply(modifier=m.name)
    if faces and len(obj.data.polygons) > faces:
        m = obj.modifiers.new("Decimate", "DECIMATE")
        m.ratio = faces / len(obj.data.polygons)
        bpy.ops.object.modifier_apply(modifier=m.name)
    bpy.ops.object.shade_smooth()
    obj.select_set(False)
    return obj
