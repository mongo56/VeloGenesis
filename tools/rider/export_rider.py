"""
Builds the VeloGenesis riders with MakeHuman's MPFB2 add-on for Blender and exports them as .glb.

The bodies are CC0 MakeHuman meshes with the "game_engine" rig (Unreal-style bone names). The cycling kit
is a fitted T-shirt and shorts; socks and gloves are painted into the COLOR_0 vertex attribute as masks the
game's shader colours per rider: G = shorts (skin under the shorts, so gaps such as the crotch show fabric),
B = socks, A = gloves (R unused, all zero = skin). Skin hidden under the
shirt and shorts is deleted so it can never poke through.

Fitted MakeHuman assets (installed in MPFB's asset library from the community asset packs):
    eyes low-poly, eyebrow001, eyelashes01, hair short02 / ponytail01     makehuman_system_assets (CC0)
    elvs_crude_t-shirt_male / joepal_crude_t-shirt_female                    shirts01 (CC0)
    mindfront_male_swimming_trunks_01 (shorts)                               pants03 (CC-BY, by Mindfront)
    punkduck_cycling_shoes (shoes02), punkduck_sport-sunglasses (glasses02)   CC-BY, by punkduck
The white shirt and the shorts are tinted per rider in the game.

Run headless (Blender 4.2+ with the MPFB extension installed and enabled):

    blender -b --factory-startup=false -P tools/rider/export_rider.py

Writes assets/rider/rider_male.glb and assets/rider/rider_female.glb.
"""

import os
import sys

import bpy
from mathutils import Vector

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT = os.path.join(ROOT, "assets", "rider")

HAIR = {"male": "short02", "female": "ponytail01"}
SHIRT = {"male": "elvs_crude_t-shirt_male", "female": "joepal_crude_t-shirt_female"}
SHORTS = "mindfront_male_swimming_trunks_01"
SHOES = "punkduck_cycling_shoes"
GLASSES = "punkduck_sport-sunglasses"

# Lean road cyclists. Race stays mixed: skin colour comes from the game's rider look.
BUILDS = {
    "male": {"gender": 1.0, "age": 0.5, "muscle": 0.5, "weight": 0.32, "proportions": 0.75, "height": 0.6,
             "cupsize": 0.5, "firmness": 0.5, "race": {"asian": 0.33, "caucasian": 0.34, "african": 0.33}},
    "female": {"gender": 0.0, "age": 0.5, "muscle": 0.48, "weight": 0.34, "proportions": 0.75, "height": 0.7,
               "cupsize": 0.4, "firmness": 0.6, "race": {"asian": 0.33, "caucasian": 0.34, "african": 0.33}},
}

# On top of the overall build: a cyclist's light upper body (narrow shoulders, small lats, pecs and arms,
# flat stomach) and legs that keep their muscle. Target name → weight; "l-"/"r-" pairs are both applied.
PHYSIQUE = {
    "measure-shoulder-dist-decr": 0.55,
    "torso-scale-horiz-decr": 0.2,
    "torso-vshape-decr": 0.5,
    "torso-muscle-dorsi-decr": 0.7,
    "torso-muscle-pectoral-decr": 0.5,
    "stomach-tone-incr": 0.4,
    "measure-neck-circ-decr": 0.4,
    "upperarm-muscle-decr": 0.55,
    "upperarm-shoulder-muscle-decr": 0.7,
    "upperarm-scale-horiz-decr": 0.2,
    "upperarm-scale-depth-decr": 0.2,
    "lowerarm-muscle-decr": 0.35,
    "upperleg-muscle-incr": 0.35,
    "lowerleg-muscle-incr": 0.35,
    "upperleg-fat-decr": 0.4,
}


def apply_physique(basemesh, TargetService):
    """Loads the PHYSIQUE shape targets from MPFB's data/targets onto the base mesh."""
    from glob import glob
    base = None
    for m in sys.modules.values():
        f = getattr(m, "__file__", None) or ""
        if f.endswith(os.path.join("services", "targetservice.py")):
            base = os.path.join(os.path.dirname(os.path.dirname(f)), "data", "targets")
    for name, weight in PHYSIQUE.items():
        names = [name] if name.startswith(("measure", "torso", "stomach")) else ["l-" + name, "r-" + name]
        for n in names:
            found = glob(os.path.join(base, "*", n + ".target.gz"))
            if not found:
                raise RuntimeError(f"MakeHuman target not found: {n}")
            TargetService.load_target(basemesh, found[0], weight=weight, name=n)


def mpfb():
    import importlib
    for name in ("bl_ext.blender_org.mpfb", "mpfb"):
        try:
            return importlib.import_module(name + ".services.humanservice").HumanService, \
                   importlib.import_module(name + ".services.exportservice").ExportService, \
                   importlib.import_module(name + ".services.assetservice").AssetService, \
                   importlib.import_module(name + ".services.targetservice").TargetService
        except ImportError:
            continue
    raise RuntimeError("MPFB extension not found – install it from extensions.blender.org")


def clear_scene():
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete()
    for block in (bpy.data.meshes, bpy.data.armatures, bpy.data.materials):
        for item in list(block):
            block.remove(item)


def along(arm, bone_name, co):
    """(distance along the bone from its head, bone length) in metres, rest pose."""
    b = arm.data.bones[bone_name]
    head = arm.matrix_world @ b.head_local
    d = (arm.matrix_world @ b.tail_local) - head
    return (co - head).dot(d.normalized()), d.length


EDGE = 0.03  # metres over which a mask ramps from 0 to 1 across a seam


def ramp(dist):
    """Signed distance to a seam (positive inside the garment) → 0..1, 0.5 on the seam.
    Smooth ramps interpolate cleanly across the mesh, and the shader thresholds them at 0.5,
    so seams come out straight instead of following the quads."""
    return min(1.0, max(0.0, 0.5 + dist / EDGE))


def kit_region(bone, s, length):
    """(unused, unused, socks, gloves) for a vertex whose strongest bone is `bone`, s metres along it."""
    side = bone[:-2] if bone.endswith(("_l", "_r")) else bone
    if side == "calf":
        return (0, 0, ramp(s - 0.55 * length), 0)
    if side in ("foot", "ball"):
        return (0, 0, 1, 0)
    if side == "hand":
        return (0, 0, 0, 1)
    if side.endswith("_01") and side.split("_")[0] in ("index", "middle", "ring", "pinky", "thumb"):
        return (0, 0, 0, 1)  # short-finger gloves cover the first knuckle
    return (0, 0, 0, 0)


def paint_under_shorts(body, shorts, arm, reach=0.05):
    """G mask on skin under the shorts: hips, seat and crotch (pelvis-bound) and the thighs up to just above the
    leg hems. Most of it is deleted as covered later; what stays (by the hems and between the legs) shows the
    shorts' colour instead of skin through any gap."""
    from mathutils.bvhtree import BVHTree
    deps = bpy.context.evaluated_depsgraph_get()
    ev = shorts.evaluated_get(deps)
    me = ev.to_mesh()
    tree = BVHTree.FromPolygons([shorts.matrix_world @ v.co for v in me.vertices], [tuple(p.vertices) for p in me.polygons])
    # How far down each thigh the leg hem reaches (its highest point, at the inner thigh).
    edge_count = {}
    for poly in me.polygons:
        for e in poly.edge_keys:
            edge_count[e] = edge_count.get(e, 0) + 1
    border = {i for e, c in edge_count.items() if c == 1 for i in e}
    names = {g.index: g.name for g in shorts.vertex_groups}
    hem = {"thigh_l": [], "thigh_r": []}
    for i in border:
        v = shorts.data.vertices[i]
        g = max(v.groups, key=lambda g: g.weight, default=None)
        n = names.get(g.group) if g else None
        if n in hem:
            hem[n].append(along(arm, n, shorts.matrix_world @ v.co)[0])
    ev.to_mesh_clear()
    reach_s = {n: (min(s) if s else 0.0) for n, s in hem.items()}

    attr = body.data.color_attributes["kit"]
    groups = {g.index: g.name for g in body.vertex_groups}
    world = body.matrix_world
    for v in body.data.vertices:
        co = world @ v.co
        if tree.find_nearest(co, reach)[0] is None:
            continue
        ws = {groups.get(g.group): g.weight for g in v.groups}
        best = max(ws, key=ws.get, default=None)
        if best in hem:
            inside = ramp(reach_s[best] - 0.015 - along(arm, best, co)[0])
        elif best == "pelvis" or ws.get("pelvis", 0) > 0.3:
            inside = 1.0
        else:
            continue
        c = list(attr.data[v.index].color)
        c[1] = max(c[1], inside)
        attr.data[v.index].color = c


def paint_kit(mesh_obj, arm):
    me = mesh_obj.data
    groups = {g.index: g.name for g in mesh_obj.vertex_groups}
    attr = me.color_attributes.new("kit", "FLOAT_COLOR", "POINT")
    world = mesh_obj.matrix_world
    for v in me.vertices:
        best, w = None, 0.0
        for g in v.groups:
            name = groups.get(g.group)
            if name in arm.data.bones and g.weight > w:
                best, w = name, g.weight
        attr.data[v.index].color = kit_region(best, *along(arm, best, world @ v.co)) if best else (0, 0, 0, 0)
    me.color_attributes.active_color = attr


def hide_covered(body, garments, reach=0.035, margin=3, keep_bones=()):
    """Deletes the parts of `body` (the skin, or an inner garment) hidden under the garments: vertices whose
    outward normal runs into a garment within `reach`, minus `margin` rings next to anything visible, so the
    hems never open a gap. Under a loose neckline the shirt is further away than `reach`, so that skin stays."""
    import bmesh
    from mathutils.bvhtree import BVHTree
    deps = bpy.context.evaluated_depsgraph_get()
    verts, polys = [], []
    for g in garments:
        ev = g.evaluated_get(deps)
        me = ev.to_mesh()
        base = len(verts)
        verts += [g.matrix_world @ v.co for v in me.vertices]
        polys += [tuple(base + i for i in p.vertices) for p in me.polygons]
        ev.to_mesh_clear()
    tree = BVHTree.FromPolygons(verts, polys)

    # Openings around limbs (sleeve and leg hems): garment boundary vertices bound mostly to a limb bone.
    from mathutils.kdtree import KDTree
    openings = []
    for g in garments:
        names = {vg.index: vg.name for vg in g.vertex_groups}
        me = g.data
        edge_count = {}
        for poly in me.polygons:
            for e in poly.edge_keys:
                edge_count[e] = edge_count.get(e, 0) + 1
        border = {i for e, c in edge_count.items() if c == 1 for i in e}
        for i in border:
            v = me.vertices[i]
            ws = [(names.get(x.group), x.weight) for x in v.groups]
            total = sum(w for _, w in ws) or 1.0
            if sum(w for n, w in ws if n in keep_bones) / total > 0.5:
                openings.append(g.matrix_world @ v.co)
    hems = KDTree(max(1, len(openings)))
    for i, co in enumerate(openings):
        hems.insert(co, i)
    hems.balance()

    bm = bmesh.new()
    bm.from_mesh(body.data)
    bm.verts.ensure_lookup_table()
    world = body.matrix_world
    nrm = world.to_3x3().inverted().transposed()
    covered = set()
    for v in bm.verts:
        co = world @ v.co
        n = (nrm @ v.normal).normalized()
        # Covered if a garment surface lies over the point: the nearest garment point within `reach` and on
        # the outside (rays alone slip between faces on rounded parts like the seat), or a ray along the
        # normal either way (some MakeHuman meshes have inward-facing normals) hits one.
        hit, _, _, dist = tree.find_nearest(co, reach)
        if (hit is not None and abs((hit - co).normalized().dot(n)) > 0.5)                 or tree.ray_cast(co + n * 0.001, n, reach)[0] is not None or tree.ray_cast(co - n * 0.001, -n, reach)[0] is not None:
            covered.add(v)
    if keep_bones:
        # Skin that mostly follows these bones stays even when covered (e.g. arms inside loose sleeves).
        # Limb skin stays only near a sleeve or leg hem (within 6 cm), where a gap could open when the limb
        # moves; further in (e.g. the back of the thigh on the saddle) it goes like the rest.
        idx = {g.index for g in body.vertex_groups if g.name in keep_bones}
        deform = bm.verts.layers.deform.verify()
        near = lambda v: openings and hems.find(world @ v.co)[2] < 0.06
        kept = {v for v in covered if sum(w for g, w in v[deform].items() if g in idx) >= 0.3 and near(v)}
        for v in kept:
            v.co -= v.normal * 0.007  # tuck the limb in a little so it can't graze through the fabric
        covered -= kept
    for _ in range(margin):
        covered = {v for v in covered if all(e.other_vert(v) in covered for e in v.link_edges)}
    before = len(bm.verts)
    bmesh.ops.delete(bm, geom=list(covered), context="VERTS")
    bm.to_mesh(body.data)
    bm.free()
    print(f"    hid {before - len(body.data.vertices)} covered vertices of {body.name}")


def fit_to_body(obj, tree, clearance, max_clearance=None, smooth=8):
    """Moves vertices of obj to `clearance` off the body (and never inside it). With max_clearance, vertices
    further out are pulled in to it, giving a close fit. `tree` is a BVH of the full body (taken before
    MakeHuman deleted the skin hidden under clothes). Each vertex keeps its own direction from the body, then
    a volume-preserving smooth irons out creases and a second pass restores the minimum clearance."""
    inv = obj.matrix_world.inverted()

    def snap(pull):
        for v in obj.data.vertices:
            co = obj.matrix_world @ v.co
            loc, normal, _, dist = tree.find_nearest(co)
            if loc is None:
                continue
            d = co - loc
            outside = d.dot(normal) > 0
            out = d.normalized() if outside and dist > 1e-5 else normal
            if not outside or dist < clearance:
                v.co = inv @ (loc + out * clearance)
            elif pull and max_clearance is not None and dist > max_clearance:
                v.co = inv @ (loc + out * max_clearance)

    def relax(iterations):
        bpy.context.view_layer.objects.active = obj
        m = obj.modifiers.new("Relax", "CORRECTIVE_SMOOTH")
        m.iterations = iterations
        m.use_only_smooth = True
        bpy.ops.object.modifier_move_to_index(modifier=m.name, index=0)
        bpy.ops.object.modifier_apply(modifier=m.name)

    snap(True)
    if smooth:
        relax(smooth)
        snap(False)


def skin_copy(basemesh):
    """A copy of the body with its bone weights and body shape, without MakeHuman's helper geometry (the
    invisible tights / skirt / hair shells, which sit right where clothes are)."""
    import bmesh
    ref = basemesh.copy()
    ref.data = basemesh.data.copy()
    ref.modifiers.clear()
    bpy.context.collection.objects.link(ref)
    bpy.context.view_layer.objects.active = ref
    if ref.data.shape_keys:
        bpy.ops.object.shape_key_remove(all=True, apply_mix=True)
    body = ref.vertex_groups["body"].index
    bm = bmesh.new()
    bm.from_mesh(ref.data)
    deform = bm.verts.layers.deform.verify()
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if v[deform].get(body, 0.0) < 0.5], context="VERTS")
    bm.to_mesh(ref.data)
    bm.free()
    return ref


ARM_BONES = ("upperarm_l", "upperarm_r", "lowerarm_l", "lowerarm_r")
# Limbs inside garment openings (sleeves, shorts legs): their skin is kept and tucked in.
LIMB_BONES = ARM_BONES + ("thigh_l", "thigh_r")


def copy_skin_weights(garment, skin):
    """Replaces the garment's bone weights with those of the nearest point on the skin, except on sleeves:
    there the nearest skin can be the torso (under the arm), which would leave the sleeve behind when the arm
    reaches for the bars, so vertices MakeHuman bound mostly to the arm keep their own weights."""
    names = {g.index: g.name for g in garment.vertex_groups}
    keep = {}
    for v in garment.data.vertices:
        ws = [(names[g.group], g.weight) for g in v.groups if g.weight > 0]
        total = sum(w for _, w in ws) or 1.0
        if sum(w for n, w in ws if n in ARM_BONES) / total > 0.4:
            keep[v.index] = ws
    for g in list(garment.vertex_groups):
        garment.vertex_groups.remove(g)
    bpy.ops.object.select_all(action="DESELECT")
    bpy.context.view_layer.objects.active = garment
    garment.select_set(True)
    m = garment.modifiers.new("Weights", "DATA_TRANSFER")
    m.object = skin
    m.use_vert_data = True
    m.data_types_verts = {"VGROUP_WEIGHTS"}
    m.vert_mapping = "POLYINTERP_NEAREST"
    m.layers_vgroup_select_src = "ALL"
    m.layers_vgroup_select_dst = "NAME"
    bpy.ops.object.datalayout_transfer(modifier=m.name)
    bpy.ops.object.modifier_move_to_index(modifier=m.name, index=0)
    bpy.ops.object.modifier_apply(modifier=m.name)
    groups = garment.vertex_groups
    for i, ws in keep.items():
        for g in groups:
            g.remove([i])
        for n, w in ws:
            (groups.get(n) or groups.new(name=n)).add([i], w, "REPLACE")


def add_crew_neck(shirt, body, arm, clearance=0.01, rise=0.035):
    """Closes the shirt's open scoop neck into a round crew collar: the neckline's edge loop is extruded twice
    towards a ring `clearance` off the neck, `rise` above the base of the neck."""
    import bmesh
    w = arm.matrix_world
    base = w @ arm.data.bones["neck_01"].head_local
    axis = ((w @ arm.data.bones["head"].head_local) - base).normalized()
    centre = base + axis * rise

    # Neck radius at the collar height, from the body's skin around it.
    pts = [body.matrix_world @ v.co for v in body.data.vertices]
    radial = []
    for co in pts:
        d = co - centre
        h = d.dot(axis)
        r = (d - axis * h).length
        if abs(h) < 0.012 and r < 0.12:
            radial.append(r)
    neck_r = sorted(radial)[len(radial) // 2] if radial else 0.055

    inv = shirt.matrix_world.inverted()
    mw = shirt.matrix_world
    bm = bmesh.new()
    bm.from_mesh(shirt.data)
    boundary = [e for e in bm.edges if e.is_boundary]
    # Connected boundary loops; the neckline is the one closest to the neck.
    loops, seen = [], set()
    for e in boundary:
        if e in seen:
            continue
        stack, comp = [e], []
        while stack:
            x = stack.pop()
            if x in seen:
                continue
            seen.add(x)
            comp.append(x)
            for v in x.verts:
                stack += [y for y in v.link_edges if y.is_boundary and y not in seen]
        loops.append(comp)

    def centroid(comp):
        vs = {v for e in comp for v in e.verts}
        return sum((mw @ v.co for v in vs), Vector()) / len(vs)

    neck = min(loops, key=lambda c: (centroid(c) - centre).length)

    def ring(p, t):
        d = p - centre
        d -= axis * d.dot(axis)
        target = centre + d.normalized() * (neck_r + clearance)
        return p.lerp(target, t)

    edges = neck
    for t in (0.55, 1.0):
        out = bmesh.ops.extrude_edge_only(bm, edges=edges)
        new_verts = [g for g in out["geom"] if isinstance(g, bmesh.types.BMVert)]
        edges = [g for g in out["geom"] if isinstance(g, bmesh.types.BMEdge) and all(v in new_verts for v in g.verts)]
        for v in new_verts:
            v.co = inv @ ring(mw @ v.co, t)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(shirt.data)
    bm.free()
    print(f"    crew neck: {len(neck)} edges, neck radius {neck_r * 100:.1f} cm")


def body_tree(basemesh):
    """BVH of the body as it is now (all skin present, shape targets applied)."""
    from mathutils.bvhtree import BVHTree
    deps = bpy.context.evaluated_depsgraph_get()
    ev = basemesh.evaluated_get(deps)
    me = ev.to_mesh()
    verts = [basemesh.matrix_world @ v.co for v in me.vertices]
    polys = [tuple(p.vertices) for p in me.polygons]
    ev.to_mesh_clear()
    return BVHTree.FromPolygons(verts, polys)


def add_asset(HumanService, AssetService, basemesh, name, subdir, asset_type):
    path = AssetService.find_asset_absolute_path(name + ".mhclo", subdir)
    if not path:
        raise RuntimeError(f"MakeHuman asset not installed: {subdir}/{name} (see the header of this script)")
    return HumanService.add_mhclo_asset(path, basemesh, asset_type=asset_type, subdiv_levels=0, material_type="GAMEENGINE")


def build(name, macros, HumanService, ExportService, AssetService, TargetService):
    clear_scene()
    basemesh = HumanService.create_human(macro_detail_dict=macros)
    apply_physique(basemesh, TargetService)
    arm = HumanService.add_builtin_rig(basemesh, "game_engine")
    full_body = body_tree(basemesh)
    skin_ref = skin_copy(basemesh)  # the whole skin with its weights, before clothes delete parts of it
    parts = {
        "Eyes": add_asset(HumanService, AssetService, basemesh, "low-poly", "eyes", "Eyes"),
        "Eyebrows": add_asset(HumanService, AssetService, basemesh, "eyebrow001", "eyebrows", "Eyebrows"),
        "Eyelashes": add_asset(HumanService, AssetService, basemesh, "eyelashes01", "eyelashes", "Eyelashes"),
        "Hair": add_asset(HumanService, AssetService, basemesh, HAIR[name], "hair", "Hair"),
        "Shirt": add_asset(HumanService, AssetService, basemesh, SHIRT[name], "clothes", "Clothes"),
        "Shorts": add_asset(HumanService, AssetService, basemesh, SHORTS, "clothes", "Clothes"),
        "Shoes": add_asset(HumanService, AssetService, basemesh, SHOES, "clothes", "Clothes"),
        "Glasses": add_asset(HumanService, AssetService, basemesh, GLASSES, "clothes", "Clothes"),
    }
    # MakeHuman deletes the skin it expects the shirt and shorts to cover, including the upper arms inside the
    # sleeves; the sleeves don't follow the arm exactly, so that left see-through holes. Drop those delete
    # masks (keep the shoes') and let hide_covered() remove only what is really hidden.
    for m in list(basemesh.modifiers):
        if m.type == "MASK" and m.name.startswith("Delete.") and (SHIRT[name] in m.name or SHORTS in m.name):
            basemesh.modifiers.remove(m)
    ExportService.bake_modifiers_remove_helpers(basemesh, bake_masks=True)
    # Fold the body-shape targets into the meshes.
    for obj in [basemesh] + list(parts.values()):
        bpy.context.view_layer.objects.active = obj
        if obj.data.shape_keys:
            bpy.ops.object.shape_key_remove(all=True, apply_mix=True)
    for p in basemesh.data.polygons:
        p.use_smooth = True
    # Keep only the deform groups (bones); helper / joint groups are no longer needed.
    for g in list(basemesh.vertex_groups):
        if g.name not in arm.data.bones:
            basemesh.vertex_groups.remove(g)
    # Close-fitting shorts (5-9 mm off the skin) and an athletic-fit shirt (12-22 mm) that stays outside them.
    fit_to_body(parts["Shorts"], full_body, 0.005, 0.009)
    add_crew_neck(parts["Shirt"], skin_ref, arm)
    fit_to_body(parts["Shirt"], full_body, 0.012, 0.022)  # athletic fit, never inside the shorts
    paint_kit(basemesh, arm)
    paint_under_shorts(basemesh, parts["Shorts"], arm)
    hide_covered(basemesh, [parts["Shirt"], parts["Shorts"]], keep_bones=LIMB_BONES)
    # Shirt and shorts take the skin's own bone weights, so they bend exactly like the body under them and
    # keep their spacing (with their own weights the waistband and the shirt's hem crossed when leaning).
    for garment in (parts["Shirt"], parts["Shorts"]):
        copy_skin_weights(garment, skin_ref)
    bpy.data.objects.remove(skin_ref)
    basemesh.name = "Body"
    arm.name = "Rider"
    for key, obj in parts.items():
        obj.name = key

    zs = [(basemesh.matrix_world @ v.co).z for v in basemesh.data.vertices]
    print(f"[{name}] {len(basemesh.data.vertices)} verts, height {max(zs) - min(zs):.3f} m, parts {[o.name for o in parts.values()]}")

    os.makedirs(OUT, exist_ok=True)
    path = os.path.join(OUT, f"rider_{name}.glb")
    bpy.ops.object.select_all(action="DESELECT")
    for obj in [basemesh, arm] + list(parts.values()):
        obj.select_set(True)
    bpy.ops.export_scene.gltf(filepath=path, export_format="GLB", use_selection=True, export_skins=True,
                              export_animations=False, export_morph=False, export_materials="EXPORT",
                              export_vertex_color="ACTIVE", export_all_vertex_colors=False,
                              export_active_vertex_color_when_no_material=True, export_yup=True)
    print(f"[{name}] wrote {path}")


def main():
    HumanService, ExportService, AssetService, TargetService = mpfb()
    only = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    for name, macros in BUILDS.items():
        if not only or name in only:
            build(name, macros, HumanService, ExportService, AssetService, TargetService)


main()
