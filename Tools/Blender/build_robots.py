"""
Your Last Order — человекоподобные роботы-курьеры со скелетом и анимациями (Blender 4.4+ / 5.x).

    blender --background --python Tools/Blender/build_robots.py -- [--preview] [--tex 2048]

Все три модели («Консерва», «Тостер», «Фонарь») используют ОДИН скелет, поэтому анимации общие.
Стиль: гуманоид в духе современных роботов-помощников (тканевый костюм, чёрные шарниры,
визор-экран вместо лица), но старый — выцветший, грязный, с облупленной краской и ржавчиной.

Результат для каждого робота:
    Assets/_Project/Art/Models/Robots/Robot_<Name>.fbx          — меш со скиннингом + арматура + клипы
    Assets/_Project/Art/Models/Robots/Robot_<Name>_Albedo.png    — запечённый цвет
    Assets/_Project/Art/Models/Robots/Robot_<Name>_MetalSmooth.png — R: металличность, A: гладкость (URP)
    Assets/_Project/Art/Robots/Portrait_<Name>.png               — портрет для меню (--preview)
    docs/renders/robots_lineup.png                               — общий рендер (--preview)

Кости: Hips, Spine, Chest, Neck, Head, Antenna, Shoulder/UpperArm/LowerArm/Hand.L/R,
       UpperLeg/LowerLeg/Foot/Toes.L/R. Робот смотрит в -Y Blender; .L — левая сторона робота (+X).
Клипы: Idle, Walk (1.6 м/с), Run (4.5 м/с), CrouchIdle, CrouchWalk (1.4 м/с).
Материал «Visor» — экран-лицо: в Unity на него выводится анимированное лицо (UV 0..1 спереди).
Пустышки Front (1 м впереди) и Top (1 м вверх) помогают Unity выровнять модель.
"""

import argparse
import math
import os
import sys

import bpy  # noqa: I001 — bpy импортируется раньше bmesh
import bmesh
from mathutils import Matrix, Quaternion, Vector

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT_DIR = os.path.join(ROOT, "Assets", "_Project", "Art", "Models", "Robots")
PORTRAIT_DIR = os.path.join(ROOT, "Assets", "_Project", "Art", "Robots")
RENDER_DIR = os.path.join(ROOT, "docs", "renders")
FPS = 30


def parse_args():
    argv = sys.argv
    argv = argv[argv.index("--") + 1:] if "--" in argv else argv[1:]
    p = argparse.ArgumentParser()
    p.add_argument("--preview", action="store_true")
    p.add_argument("--preview-only", action="store_true")
    p.add_argument("--tex", type=int, default=2048)
    p.add_argument("--samples", type=int, default=48)
    p.add_argument("--only", default="", help="собрать только одного робота (Can/Toaster/Lantern)")
    return p.parse_args(argv)


# =========================================================================== скелет

BONES = {
    # имя: (голова, хвост, родитель)
    "Hips": ((0, 0, 0.98), (0, 0, 1.08), None),
    "Spine": ((0, 0, 1.08), (0, 0, 1.27), "Hips"),
    "Chest": ((0, 0, 1.27), (0, 0, 1.50), "Spine"),
    "Neck": ((0, 0, 1.50), (0, 0, 1.61), "Chest"),
    "Head": ((0, 0, 1.61), (0, 0, 1.87), "Neck"),
    "Antenna": ((0.055, 0.075, 1.83), (0.055, 0.075, 1.93), "Head"),
    "Shoulder.L": ((0.05, 0, 1.45), (0.20, 0, 1.44), "Chest"),
    "UpperArm.L": ((0.20, 0, 1.44), (0.235, 0.01, 1.16), "Shoulder.L"),
    "LowerArm.L": ((0.235, 0.01, 1.16), (0.25, -0.01, 0.905), "UpperArm.L"),
    "Hand.L": ((0.25, -0.01, 0.905), (0.255, -0.02, 0.745), "LowerArm.L"),
    "UpperLeg.L": ((0.10, 0, 0.95), (0.11, 0, 0.53), "Hips"),
    "LowerLeg.L": ((0.11, 0, 0.53), (0.115, 0.02, 0.10), "UpperLeg.L"),
    "Foot.L": ((0.115, 0.02, 0.10), (0.115, -0.10, 0.03), "LowerLeg.L"),
    "Toes.L": ((0.115, -0.10, 0.03), (0.115, -0.17, 0.02), "Foot.L"),
}


def mirror_name(n):
    return n[:-2] + ".R" if n.endswith(".L") else n


def all_bones():
    out = {}
    for name, (h, t, p) in BONES.items():
        out[name] = (h, t, p)
        if name.endswith(".L"):
            out[mirror_name(name)] = ((-h[0], h[1], h[2]), (-t[0], t[1], t[2]), mirror_name(p) if p and p.endswith(".L") else p)
    return out


def build_armature():
    data = bpy.data.armatures.new("Armature")
    arm = bpy.data.objects.new("Armature", data)
    bpy.context.scene.collection.objects.link(arm)
    bpy.context.view_layer.objects.active = arm
    arm.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    bones = all_bones()
    edit = {}
    for name, (h, t, _) in bones.items():
        b = data.edit_bones.new(name)
        b.head, b.tail = h, t
        b.roll = 0.0
        edit[name] = b
    for name, (_, _, p) in bones.items():
        if p:
            edit[name].parent = edit[p]
            edit[name].use_connect = False
    bpy.ops.object.mode_set(mode="OBJECT")
    data.display_type = "STICK"
    return arm


# =========================================================================== материалы

def _nodes(mat):
    mat.use_nodes = True
    nt = mat.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
    nt.links.new(bsdf.outputs["BSDF"], out.inputs["Surface"])
    return nt, nt.nodes, nt.links, bsdf, nt.nodes.new("ShaderNodeTexCoord")


def _noise(n, l, coord, scale, detail=6.0, rough=0.55, mapping=None):
    t = n.new("ShaderNodeTexNoise")
    t.inputs["Scale"].default_value = scale
    t.inputs["Detail"].default_value = detail
    t.inputs["Roughness"].default_value = rough
    if mapping is not None:
        m = n.new("ShaderNodeMapping")
        m.inputs["Scale"].default_value = mapping
        l.new(coord.outputs["Object"], m.inputs["Vector"])
        l.new(m.outputs[0], t.inputs["Vector"])
    else:
        l.new(coord.outputs["Object"], t.inputs["Vector"])
    return t


def _range(n, l, src, a, b):
    r = n.new("ShaderNodeMapRange")
    r.inputs["From Min"].default_value = a
    r.inputs["From Max"].default_value = b
    l.new(src, r.inputs["Value"])
    return r.outputs[0]


def _mix(n, l, fac, a, b):
    m = n.new("ShaderNodeMix")
    m.data_type = "RGBA"
    if isinstance(fac, float):
        m.inputs["Factor"].default_value = fac
    else:
        l.new(fac, m.inputs["Factor"])
    for sock, v in (("A", a), ("B", b)):
        if isinstance(v, tuple):
            m.inputs[sock].default_value = (*v, 1)
        else:
            l.new(v, m.inputs[sock])
    return m.outputs["Result"]


def _grime(n, l, coord, color_socket, amount, low_z=0.6):
    """Грязь: низ модели, подтёки, пятна."""
    sep = n.new("ShaderNodeSeparateXYZ")
    l.new(coord.outputs["Object"], sep.inputs[0])
    low = _range(n, l, sep.outputs["Z"], low_z, 0.0)
    streak = _noise(n, l, coord, 2.5, mapping=(30.0, 30.0, 1.2))
    streak_m = _range(n, l, streak.outputs["Fac"], 0.56, 0.76)
    blotch = _noise(n, l, coord, 5.0, detail=10.0, rough=0.7)
    blotch_m = _range(n, l, blotch.outputs["Fac"], 0.58, 0.72)
    mx = n.new("ShaderNodeMath"); mx.operation = "MAXIMUM"
    l.new(low, mx.inputs[0]); l.new(streak_m, mx.inputs[1])
    mx2 = n.new("ShaderNodeMath"); mx2.operation = "MAXIMUM"
    l.new(mx.outputs[0], mx2.inputs[0]); l.new(blotch_m, mx2.inputs[1])
    amt = n.new("ShaderNodeMath"); amt.operation = "MULTIPLY"
    amt.inputs[1].default_value = amount
    l.new(mx2.outputs[0], amt.inputs[0])
    return _mix(n, l, amt.outputs[0], color_socket, (0.085, 0.07, 0.055))


def mat_suit(name, color, dirt=0.55):
    """Вязаная ткань костюма: рубчик, выцветание, пятна."""
    m = bpy.data.materials.new(name)
    nt, n, l, bsdf, coord = _nodes(m)
    wave = n.new("ShaderNodeTexWave")
    wave.wave_type = "BANDS"
    wave.bands_direction = "X"
    wave.inputs["Scale"].default_value = 60.0
    wave.inputs["Distortion"].default_value = 1.5
    wave.inputs["Detail"].default_value = 2.0
    l.new(coord.outputs["Object"], wave.inputs["Vector"])
    rib = _range(n, l, wave.outputs["Fac"], 0.0, 1.0)
    dark = tuple(c * 0.72 for c in color)
    base = _mix(n, l, rib, dark, color)
    fade = _noise(n, l, coord, 3.0, detail=4.0)
    faded = _mix(n, l, _range(n, l, fade.outputs["Fac"], 0.55, 0.8), base, tuple(min(1, c * 1.25 + 0.03) for c in color))
    l.new(_grime(n, l, coord, faded, dirt), bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = 0.92
    bsdf.inputs["Metallic"].default_value = 0.0
    return m


def mat_paint(name, color, rust=0.5, dirt=0.5, rough=0.5, metallic=0.3):
    """Окрашенная броня: сколы до ржавчины, выцветание, грязь."""
    m = bpy.data.materials.new(name)
    nt, n, l, bsdf, coord = _nodes(m)
    tone = _noise(n, l, coord, 6.0)
    base = _mix(n, l, tone.outputs["Fac"], tuple(c * 0.85 for c in color), tuple(min(1, c * 1.08) for c in color))
    chips = _noise(n, l, coord, 16.0, detail=14.0, rough=0.72)
    chip_m = _range(n, l, chips.outputs["Fac"], 0.63 - rust * 0.13, 0.66 - rust * 0.13)
    rn = _noise(n, l, coord, 45.0)
    rust_c = _mix(n, l, rn.outputs["Fac"], (0.3, 0.11, 0.04), (0.56, 0.27, 0.09))
    # под краской — светлый грунт по краю скола
    edge = _range(n, l, chips.outputs["Fac"], 0.60 - rust * 0.13, 0.63 - rust * 0.13)
    primed = _mix(n, l, edge, base, (0.62, 0.6, 0.55))
    painted = _mix(n, l, chip_m, primed, rust_c)
    l.new(_grime(n, l, coord, painted, dirt), bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = rough
    bsdf.inputs["Metallic"].default_value = metallic
    return m


def mat_plastic(name, color=(0.025, 0.025, 0.028), scratches=0.6, dirt=0.4):
    """Глянцевый чёрный пластик шарниров: царапины и пыль."""
    m = bpy.data.materials.new(name)
    nt, n, l, bsdf, coord = _nodes(m)
    sc = _noise(n, l, coord, 3.0, detail=2.0, mapping=(60.0, 3.0, 60.0))
    sc_m = _range(n, l, sc.outputs["Fac"], 0.66, 0.72)
    amt = n.new("ShaderNodeMath"); amt.operation = "MULTIPLY"
    amt.inputs[1].default_value = scratches
    l.new(sc_m, amt.inputs[0])
    scratched = _mix(n, l, amt.outputs[0], color, (0.33, 0.32, 0.3))
    l.new(_grime(n, l, coord, scratched, dirt), bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = 0.22
    bsdf.inputs["Metallic"].default_value = 0.0
    return m


def mat_flat(name, color, rough=0.6, metallic=0.0, emission=None, strength=0.0):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    b = m.node_tree.nodes.get("Principled BSDF")
    b.inputs["Base Color"].default_value = (*color, 1)
    b.inputs["Roughness"].default_value = rough
    b.inputs["Metallic"].default_value = metallic
    if emission:
        b.inputs["Emission Color"].default_value = (*emission, 1)
        b.inputs["Emission Strength"].default_value = strength
    return m


# =========================================================================== геометрия

class Kit:
    """Детали робота. Каждая деталь жёстко привязана к одной кости (как у настоящих роботов)."""

    def __init__(self, mats):
        self.M = mats
        self.parts = []  # (obj, bone)

    def _finish(self, obj, bone, mat, bevel=0.0, smooth=True, subsurf=0):
        obj.data.materials.clear()
        obj.data.materials.append(self.M[mat])
        if bevel > 0:
            b = obj.modifiers.new("Bevel", "BEVEL")
            b.width = bevel
            b.segments = 3
            b.limit_method = "ANGLE"
            b.angle_limit = math.radians(35)
        if subsurf:
            s = obj.modifiers.new("Sub", "SUBSURF")
            s.levels = subsurf
            s.render_levels = subsurf
        if smooth:
            for p in obj.data.polygons:
                p.use_smooth = True
        self.parts.append((obj, bone))
        return obj

    def sphere(self, bone, mat, loc, r, scale=(1, 1, 1), rot=(0, 0, 0), seg=32):
        bpy.ops.mesh.primitive_uv_sphere_add(segments=seg, ring_count=seg // 2, radius=r, location=loc, rotation=rot)
        o = bpy.context.active_object
        o.scale = scale
        return self._finish(o, bone, mat)

    def box(self, bone, mat, loc, size, rot=(0, 0, 0), bevel=0.01):
        bpy.ops.mesh.primitive_cube_add(size=1.0, location=loc, rotation=rot)
        o = bpy.context.active_object
        o.scale = size
        return self._finish(o, bone, mat, bevel=bevel, smooth=False)

    def rbox(self, bone, mat, loc, size, rot=(0, 0, 0), bevel=0.02):
        """Скруглённый ящик (гладкий)."""
        o = self.box(bone, mat, loc, size, rot, bevel)
        for p in o.data.polygons:
            p.use_smooth = True
        return o

    def cyl(self, bone, mat, loc, r, depth, rot=(0, 0, 0), verts=32, r2=None, bevel=0.0):
        if r2 is None:
            bpy.ops.mesh.primitive_cylinder_add(vertices=verts, radius=r, depth=depth, location=loc, rotation=rot)
        else:
            bpy.ops.mesh.primitive_cone_add(vertices=verts, radius1=r, radius2=r2, depth=depth, location=loc, rotation=rot)
        return self._finish(bpy.context.active_object, bone, mat, bevel=bevel)

    def torus(self, bone, mat, loc, R, r, rot=(0, 0, 0)):
        bpy.ops.mesh.primitive_torus_add(major_radius=R, minor_radius=r, location=loc, rotation=rot,
                                         major_segments=40, minor_segments=10)
        return self._finish(bpy.context.active_object, bone, mat)

    def limb(self, bone, mat, a, b, r1, r2, verts=28, flat=1.0):
        """Сужающийся сегмент конечности между точками a и b со скруглёнными краями."""
        a, b = Vector(a), Vector(b)
        d = b - a
        rot = d.to_track_quat("Z", "Y").to_euler()
        bpy.ops.mesh.primitive_cone_add(vertices=verts, radius1=r1, radius2=r2, depth=d.length,
                                        location=(a + b) / 2, rotation=rot)
        o = bpy.context.active_object
        o.scale = (1.0, flat, 1.0)
        bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
        o.rotation_euler = rot
        return self._finish(o, bone, mat, bevel=min(r1, r2) * 0.45)

    def tube(self, bone, mat, a, b, r, verts=12):
        a, b = Vector(a), Vector(b)
        d = b - a
        return self.cyl(bone, mat, (a + b) / 2, r, d.length, rot=d.to_track_quat("Z", "Y").to_euler(), verts=verts)

    def loft(self, bone, mat, stations, ring=48, cap_bottom=True, cap_top=True, cy=0.0):
        """Корпус из сечений-суперэллипсов: stations = [(z, полуширина, полуглубина, степень)]."""
        bm = bmesh.new()
        rings = []
        for (z, w, d, n) in stations:
            r = []
            for i in range(ring):
                t = 2 * math.pi * i / ring
                c, s = math.cos(t), math.sin(t)
                x = math.copysign(abs(c) ** (2 / n), c) * w
                y = math.copysign(abs(s) ** (2 / n), s) * d + cy
                r.append(bm.verts.new((x, y, z)))
            rings.append(r)
        for a, b in zip(rings, rings[1:]):
            for i in range(ring):
                j = (i + 1) % ring
                bm.faces.new((a[i], a[j], b[j], b[i]))
        if cap_bottom:
            bm.faces.new(list(reversed(rings[0])))
        if cap_top:
            bm.faces.new(rings[-1])
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        me = bpy.data.meshes.new(bone + "_loft")
        bm.to_mesh(me)
        bm.free()
        o = bpy.data.objects.new(bone + "_loft", me)
        bpy.context.scene.collection.objects.link(o)
        bpy.context.view_layer.objects.active = o
        return self._finish(o, bone, mat, subsurf=1)

    def shell_cut(self, bone, mat, obj_fn, keep):
        """Создаёт объект функцией obj_fn и оставляет только вершины, где keep(co) истинно (для визора)."""
        o = obj_fn()
        bm = bmesh.new()
        bm.from_mesh(o.data)
        mw = o.matrix_world
        dead = [v for v in bm.verts if not keep(mw @ v.co)]
        bmesh.ops.delete(bm, geom=dead, context="VERTS")
        bm.to_mesh(o.data)
        bm.free()
        o.data.materials.clear()
        o.data.materials.append(self.M[mat])
        for p in o.data.polygons:
            p.use_smooth = True
        sol = o.modifiers.new("Solid", "SOLIDIFY")
        sol.thickness = 0.006
        sol.offset = 1.0
        self.parts.append((o, bone))
        return o

    def text(self, bone, mat, body, loc, size, rot):
        bpy.ops.object.text_add(location=loc, rotation=rot)
        t = bpy.context.active_object
        t.data.body = body
        t.data.size = size
        t.data.align_x = "CENTER"
        t.data.extrude = 0.0015
        bpy.ops.object.convert(target="MESH")
        return self._finish(bpy.context.active_object, bone, mat, smooth=False)


# =========================================================================== варианты

VARIANTS = {
    "Can": dict(
        title="«Консерва»",
        suit=(0.2, 0.22, 0.15), armor=(0.38, 0.47, 0.3), bulk=1.1, rust=0.7, head="egg", pack="can",
        number="K-07"),
    "Toaster": dict(
        title="«Тостер»",
        suit=(0.24, 0.2, 0.16), armor=(0.74, 0.55, 0.14), bulk=1.02, rust=0.55, head="box", pack="toaster",
        number="T-12"),
    "Lantern": dict(
        title="«Фонарь»",
        suit=(0.25, 0.27, 0.3), armor=(0.3, 0.4, 0.5), bulk=0.9, rust=0.8, head="lantern", pack="lantern",
        number="F-03"),
}


def build_body(k, v):
    b = v["bulk"]
    # --- таз и пояс
    k.loft("Hips", "black", [(0.88, 0.10, 0.075, 3), (0.92, 0.15, 0.10, 3), (0.99, 0.165, 0.112, 3), (1.05, 0.15, 0.108, 3)])
    k.torus("Hips", "rubber", (0, 0, 1.0), 0.162 * b, 0.018)
    k.box("Hips", "rust", (0.12 * b, -0.02, 0.99), (0.06, 0.05, 0.07), bevel=0.008)
    # --- живот (гофра) и корпус
    for i in range(3):
        k.torus("Spine", "rubber", (0, 0, 1.07 + i * 0.03), 0.118 * b, 0.014)
    k.loft("Spine", "suit", [(1.08, 0.12 * b, 0.09 * b, 3), (1.17, 0.13 * b, 0.095 * b, 3), (1.28, 0.16 * b, 0.105 * b, 3.2)])
    k.loft("Chest", "suit", [(1.27, 0.16 * b, 0.105 * b, 3.2), (1.35, 0.19 * b, 0.118 * b, 3.6), (1.43, 0.205 * b, 0.115 * b, 3.8),
                             (1.48, 0.17 * b, 0.095 * b, 3.2), (1.515, 0.07, 0.06, 2)])
    # нагрудная пластина с номером, индикатор
    k.rbox("Chest", "armor", (0, -0.108 * b, 1.38), (0.22 * b, 0.03, 0.15), bevel=0.012)
    k.text("Chest", "black", v["number"], (0, -0.1245 * b, 1.37), 0.045, (math.pi / 2, 0, 0))
    k.text("Chest", "black", "YCBE", (0, -0.1245 * b, 1.418), 0.022, (math.pi / 2, 0, 0))
    k.sphere("Chest", "led", (0.082 * b, -0.125 * b, 1.43), 0.008, seg=12)
    # рюкзак-батарея
    pack = v["pack"]
    if pack == "can":
        k.cyl("Chest", "armor", (0, 0.17 * b, 1.33), 0.085, 0.3, rot=(0, math.pi / 2, 0), bevel=0.01)
        for s in (-1, 1):
            k.cyl("Chest", "rust", (s * 0.155, 0.17 * b, 1.33), 0.09, 0.02, rot=(0, math.pi / 2, 0))
    elif pack == "toaster":
        k.rbox("Chest", "armor", (0, 0.17 * b, 1.33), (0.26, 0.1, 0.24), bevel=0.02)
        for s in (-1, 1):
            k.box("Chest", "black", (s * 0.06, 0.17 * b, 1.452), (0.04, 0.07, 0.01), bevel=0.0)
    else:
        k.cyl("Chest", "armor", (0, 0.15 * b, 1.3), 0.06, 0.34, bevel=0.01)
        k.cyl("Chest", "rust", (0, 0.15 * b, 1.48), 0.065, 0.03)
    for s in (-1, 1):
        k.tube("Chest", "rubber", (s * 0.08, 0.16 * b, 1.44), (s * 0.05, 0.05, 1.56), 0.012)
    # --- шея
    k.cyl("Neck", "rubber", (0, 0, 1.56), 0.042, 0.12, verts=24)
    for i in range(3):
        k.torus("Neck", "black", (0, 0, 1.52 + i * 0.035), 0.046, 0.009)

    # --- голова
    head = v["head"]
    if head == "egg":
        k.sphere("Head", "armor", (0, 0.005, 1.735), 0.12, scale=(0.9, 1.0, 1.12))
        k.shell_cut("Head", "visor",
                    lambda: _tmp_sphere((0, 0.004, 1.735), 0.123, (0.91, 1.01, 1.13)),
                    lambda co: co.y < -0.035 and 1.655 < co.z < 1.81)
        k.torus("Head", "black", (0, 0.005, 1.735), 0.108, 0.012, rot=(math.pi / 2, 0, math.pi / 2))
        for s in (-1, 1):
            k.cyl("Head", "black", (s * 0.107, 0.01, 1.735), 0.035, 0.025, rot=(0, math.pi / 2, 0))
    elif head == "box":
        k.rbox("Head", "armor", (0, 0.01, 1.74), (0.25, 0.25, 0.23), bevel=0.04)
        k.rbox("Head", "black", (0, -0.115, 1.74), (0.22, 0.03, 0.18), bevel=0.02)
        k.shell_cut("Head", "visor",
                    lambda: _tmp_box((0, -0.129, 1.74), (0.195, 0.012, 0.15)),
                    lambda co: co.y < -0.132)
        for i in range(4):
            k.box("Head", "black", (0, 0.0 + i * 0.035, 1.857), (0.15, 0.012, 0.006), bevel=0.0)
        for s in (-1, 1):
            k.cyl("Head", "rust", (s * 0.128, 0.01, 1.7), 0.03, 0.02, rot=(0, math.pi / 2, 0))
    else:  # lantern
        k.cyl("Head", "black", (0, 0, 1.735), 0.1, 0.22, verts=40, bevel=0.01)
        k.shell_cut("Head", "visor",
                    lambda: _tmp_cyl((0, 0, 1.735), 0.104, 0.12),
                    lambda co: co.y < -0.02 and abs(co.z - 1.735) < 0.055)
        for i in range(8):
            a = i / 8 * math.tau
            k.cyl("Head", "rust", (math.cos(a) * 0.112, math.sin(a) * 0.112, 1.735), 0.006, 0.24, verts=6)
        k.torus("Head", "rust", (0, 0, 1.62), 0.112, 0.01)
        k.torus("Head", "rust", (0, 0, 1.85), 0.112, 0.01)
        k.cyl("Head", "armor", (0, 0, 1.875), 0.12, 0.06, r2=0.05, verts=32)

    # --- микро-антенна (крутится, когда игрок говорит)
    k.cyl("Head", "black", (0.055, 0.075, 1.835), 0.014, 0.03 if head != "egg" else 0.05, verts=12)
    k.tube("Antenna", "metal", (0.055, 0.075, 1.84), (0.055, 0.075, 1.925), 0.0035, verts=8)
    k.box("Antenna", "metal", (0.055, 0.075, 1.925), (0.07, 0.008, 0.004), bevel=0.0)
    k.sphere("Antenna", "led", (0.055, 0.075, 1.933), 0.007, seg=12)

    # --- руки и ноги (симметрично)
    for s, side in ((1, "L"), (-1, "R")):
        def P(x, y, z):
            return (s * x, y, z)
        # плечо
        k.sphere(f"Shoulder.{side}", "black", P(0.205, 0, 1.43), 0.062 * b)
        k.sphere(f"UpperArm.{side}", "armor", P(0.218, 0, 1.45), 0.066 * b, scale=(1.0, 1.1, 0.72))
        k.limb(f"UpperArm.{side}", "suit", P(0.215, 0, 1.40), P(0.235, 0.01, 1.19), 0.048 * b, 0.04 * b)
        k.sphere(f"LowerArm.{side}", "black", P(0.237, 0.01, 1.16), 0.042 * b)
        k.limb(f"LowerArm.{side}", "suit", P(0.238, 0.01, 1.13), P(0.25, -0.01, 0.93), 0.041 * b, 0.033 * b)
        k.rbox(f"LowerArm.{side}", "armor", P(0.268 * b, 0.0, 1.03), (0.02, 0.07, 0.16), bevel=0.008)
        k.torus(f"Hand.{side}", "black", P(0.25, -0.01, 0.905), 0.034 * b, 0.01, rot=(0, 0, 0))
        # кисть: ладонь, 4 пальца, большой
        k.rbox(f"Hand.{side}", "black", P(0.252, -0.012, 0.85), (0.028, 0.07, 0.085), bevel=0.012)
        for f in range(4):
            y = -0.037 + f * 0.024
            k.limb(f"Hand.{side}", "rubber", P(0.254, y, 0.805), P(0.256, y - 0.006, 0.768), 0.0085, 0.0075, verts=10)
            k.limb(f"Hand.{side}", "black", P(0.256, y - 0.006, 0.766), P(0.25, y - 0.014, 0.735), 0.0075, 0.0065, verts=10)
        k.limb(f"Hand.{side}", "rubber", P(0.245, -0.045, 0.86), P(0.235, -0.07, 0.83), 0.01, 0.008, verts=10)
        # бедро
        k.sphere(f"UpperLeg.{side}", "black", P(0.1, 0, 0.93), 0.065 * b)
        k.limb(f"UpperLeg.{side}", "suit", P(0.102, 0, 0.9), P(0.11, 0, 0.56), 0.075 * b, 0.058 * b)
        k.rbox(f"UpperLeg.{side}", "armor", P(0.105, -0.06 * b, 0.76), (0.1 * b, 0.03, 0.2), bevel=0.012)
        # колено и голень
        k.sphere(f"LowerLeg.{side}", "black", P(0.11, 0.0, 0.53), 0.057 * b)
        k.rbox(f"LowerLeg.{side}", "armor", P(0.11, -0.05 * b, 0.53), (0.075 * b, 0.03, 0.09), bevel=0.012)
        k.limb(f"LowerLeg.{side}", "suit", P(0.111, 0.005, 0.5), P(0.115, 0.02, 0.13), 0.056 * b, 0.041 * b)
        k.rbox(f"LowerLeg.{side}", "black", P(0.113, -0.04 * b, 0.32), (0.07 * b, 0.025, 0.24), bevel=0.012)
        # стопа
        k.torus(f"Foot.{side}", "black", P(0.115, 0.02, 0.105), 0.042 * b, 0.012)
        k.rbox(f"Foot.{side}", "black", P(0.115, -0.015, 0.05), (0.095 * b, 0.19, 0.09), bevel=0.025)
        k.rbox(f"Foot.{side}", "rubber", P(0.115, -0.01, 0.008), (0.1 * b, 0.2, 0.018), bevel=0.006)
        k.rbox(f"Toes.{side}", "black", P(0.115, -0.14, 0.03), (0.09 * b, 0.07, 0.05), bevel=0.018)


def _tmp_sphere(loc, r, scale):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=48, ring_count=24, radius=r, location=loc)
    o = bpy.context.active_object
    o.scale = scale
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    return o


def _tmp_box(loc, size):
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=loc)
    o = bpy.context.active_object
    o.scale = size
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    b = o.modifiers.new("Sub", "SUBSURF")
    b.levels = 2
    bpy.ops.object.modifier_apply(modifier="Sub")
    return o


def _tmp_cyl(loc, r, depth):
    bpy.ops.mesh.primitive_cylinder_add(vertices=64, radius=r, depth=depth, location=loc)
    o = bpy.context.active_object
    bm = bmesh.new()
    bm.from_mesh(o.data)
    bmesh.ops.subdivide_edges(bm, edges=[e for e in bm.edges if abs(e.verts[0].co.z - e.verts[1].co.z) > 1e-4], cuts=6)
    bm.to_mesh(o.data)
    bm.free()
    return o


# =========================================================================== анимации

def _q_axis(axis, deg):
    return Quaternion(Vector(axis).normalized(), math.radians(deg))


class Animator:
    """Ключи задаются поворотами вокруг осей арматуры (X — вперёд/назад, Y — наклон вбок, Z — поворот)."""

    def __init__(self, arm):
        self.arm = arm
        self.rest = {b.name: b.matrix_local.to_quaternion() for b in arm.data.bones}
        for pb in arm.pose.bones:
            pb.rotation_mode = "QUATERNION"

    def clear(self):
        for pb in self.arm.pose.bones:
            pb.rotation_quaternion = (1, 0, 0, 0)
            pb.location = (0, 0, 0)

    def rot(self, bone, x=0.0, y=0.0, z=0.0):
        """x>0: кость, направленная вниз, уходит назад; направленная вверх — наклоняется вперёд."""
        q = _q_axis((0, 0, 1), z) @ _q_axis((0, 1, 0), y) @ _q_axis((1, 0, 0), x)
        r = self.rest[bone]
        self.arm.pose.bones[bone].rotation_quaternion = r.inverted() @ q @ r

    def move(self, bone, world):
        r = self.rest[bone]
        self.arm.pose.bones[bone].location = r.inverted() @ Vector(world)

    def key(self, frame):
        for pb in self.arm.pose.bones:
            pb.keyframe_insert("rotation_quaternion", frame=frame)
            if pb.name == "Hips":
                pb.keyframe_insert("location", frame=frame)


def pose_locomotion(an, phase, stride, knee, arm_swing, elbow, lean, bob, crouch=0.0):
    """Цикл шага. phase в радианах; положительный угол бедра = нога впереди."""
    an.clear()
    s = math.sin(phase)
    c = math.cos(phase)
    # таз: вниз при присяде, покачивание
    an.move("Hips", (0, 0, -0.44 * crouch + bob * math.cos(2 * phase) - bob))
    an.rot("Hips", z=6 * s * (1 - crouch * 0.5), y=2 * s)
    an.rot("Spine", x=lean + 25 * crouch, z=-3 * s)
    an.rot("Chest", x=lean * 0.5 + 8 * crouch, z=-5 * s)
    an.rot("Neck", x=-lean * 0.6 - 20 * crouch)
    an.rot("Head", x=-lean * 0.3 - 8 * crouch)
    for side, ph in (("L", phase), ("R", phase + math.pi)):
        ss, cc = math.sin(ph), math.cos(ph)
        thigh = stride * ss + 72 * crouch
        swing_knee = knee * max(0.0, cc) ** 1.3 + 6 + 115 * crouch
        an.rot(f"UpperLeg.{side}", x=-thigh)
        an.rot(f"LowerLeg.{side}", x=swing_knee)
        # стопа параллельна полу (компенсируем бедро и колено) + отталкивание носком
        foot = (thigh - swing_knee) + 14 * max(0.0, -cc) * max(0.0, -ss) * (1 - crouch)
        an.rot(f"Foot.{side}", x=foot)
        an.rot(f"Toes.{side}", x=12 * max(0.0, -ss) * (1 - crouch))
        # рука — в противофазе с ногой той же стороны
        a = -arm_swing * ss
        an.rot(f"UpperArm.{side}", x=-a + 10 * crouch, y=(6 if side == "L" else -6))
        an.rot(f"LowerArm.{side}", x=-(elbow + max(0.0, -a) * 0.4))
        an.rot(f"Hand.{side}", x=-5)



# =========================================================================== ретаргетинг mocap

MOCAP_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)), "mocap")
MOCAP_FILES = {
    "Idle": "Standing_Idle.fbx",
    "Walk": "Walking.fbx",
    "Run": "Running.fbx",
    "CrouchIdle": "Crouching_Idle.fbx",
    "CrouchWalk": "Crouch_Walking.fbx",
}
# кости, повороты которых переносятся целиком (корпус)
MAP_DELTA = {"Hips": "Hips", "Spine": "Spine", "Chest": "Spine2", "Neck": "Neck", "Head": "Head"}
# кости, которые повторяют направление (конечности)
MAP_DIR = {
    "Shoulder.{s}": "{S}Shoulder", "UpperArm.{s}": "{S}Arm", "LowerArm.{s}": "{S}ForeArm", "Hand.{s}": "{S}Hand",
    "UpperLeg.{s}": "{S}UpLeg", "LowerLeg.{s}": "{S}Leg", "Foot.{s}": "{S}Foot", "Toes.{s}": "{S}ToeBase",
}


def mocap_path(clip):
    path = os.path.join(MOCAP_DIR, MOCAP_FILES.get(clip, ""))
    return path if os.path.isfile(path) else None


def _mixamo(name, src):
    for prefix in ("mixamorig:", "mixamorig1:", "mixamorig2:", ""):
        if prefix + name in src.pose.bones:
            return src.pose.bones[prefix + name]
    return None


def retarget_action(arm, clip, path):
    """Переносит анимацию mixamo на наш скелет и запекает в новое действие clip."""
    bpy.ops.object.mode_set(mode="OBJECT")
    before = set(bpy.data.objects)
    actions_before = set(bpy.data.actions)
    bpy.ops.import_scene.fbx(filepath=path)
    imported = [o for o in bpy.data.objects if o not in before]
    src = next(o for o in imported if o.type == "ARMATURE")
    src_act = src.animation_data.action
    f0, f1 = (int(round(x)) for x in src_act.frame_range)
    scene = bpy.context.scene

    # соответствие костей
    pairs_dir, pairs_delta = {}, {}
    for ours, theirs in MAP_DELTA.items():
        pb = _mixamo(theirs, src)
        if pb:
            pairs_delta[ours] = pb
    for side, S in (("L", "Left"), ("R", "Right")):
        for ours, theirs in MAP_DIR.items():
            pb = _mixamo(theirs.format(S=S), src)
            if pb:
                pairs_dir[ours.format(s=side)] = pb

    src_w = src.matrix_world
    src_rot = lambda m: (src_w @ m).to_3x3().normalized().to_quaternion()  # noqa: E731
    src_rest = {name: src_rot(pb.bone.matrix_local) for name, pb in pairs_delta.items()}
    hips_pb = pairs_delta.get("Hips")
    rest_hips = src_w @ hips_pb.bone.head_local
    our_hips_h = arm.data.bones["Hips"].head_local.z
    scale = our_hips_h / max(rest_hips.z, 1e-3)

    # поза «снять» с источника по кадрам
    samples = []
    for f in range(f0, f1 + 1):
        scene.frame_set(f)
        frame = {"hips": src_w @ hips_pb.head}
        for name, pb in pairs_delta.items():
            frame[name] = src_rot(pb.matrix)
        for name, pb in pairs_dir.items():
            frame[name] = ((src_w @ pb.tail) - (src_w @ pb.head)).normalized()
        samples.append(frame)

    for o in imported:
        bpy.data.objects.remove(o, do_unlink=True)
    for a in set(bpy.data.actions) - actions_before:
        bpy.data.actions.remove(a)

    # убираем перемещение вперёд (root motion), оставляем покачивание
    drift = samples[-1]["hips"] - samples[0]["hips"]
    n = len(samples) - 1
    loops = drift.length > 0.3

    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode="POSE")
    act = bpy.data.actions.new(clip)
    act.use_fake_user = True
    arm.animation_data.action = act
    bones = arm.data.bones
    rest_rot = {b.name: b.matrix_local.to_quaternion() for b in bones}
    order = list(all_bones().keys())

    for i, frame in enumerate(samples):
        world = {}
        for name in order:
            b = bones[name]
            if b.parent:
                pr = b.parent.name
                r0 = world[pr] @ rest_rot[pr].inverted() @ rest_rot[name]
            else:
                r0 = rest_rot[name]
            if name in frame and name in pairs_delta:
                delta = frame[name] @ src_rest[name].inverted()
                r = delta @ rest_rot[name]
            elif name in frame and name in pairs_dir:
                d0 = r0 @ Vector((0, 1, 0))
                r = d0.rotation_difference(frame[name]) @ r0
            else:
                r = r0
            world[name] = r
            pb = arm.pose.bones[name]
            pb.rotation_mode = "QUATERNION"
            pb.rotation_quaternion = r0.inverted() @ r
            pb.keyframe_insert("rotation_quaternion", frame=i)

        off = frame["hips"] - samples[0]["hips"]
        if loops:
            off -= drift * (i / n)
        base = samples[0]["hips"] - rest_hips
        world_off = (off + base) * scale
        hips = arm.pose.bones["Hips"]
        hips.location = rest_rot["Hips"].inverted() @ world_off
        hips.keyframe_insert("location", frame=i)

    speed = drift.length / (n / FPS) if loops else 0.0
    print(f"    {clip}: {os.path.basename(path)}, кадров {n + 1}, скорость {speed:.2f} м/с")
    return act


def make_action(arm, name, frames, pose_fn):
    act = bpy.data.actions.new(name)
    act.use_fake_user = True
    arm.animation_data.action = act
    an = Animator(arm)
    for f in range(frames + 1):
        pose_fn(an, f / frames)
        an.key(f)
    return act


def build_actions(arm):
    arm.animation_data_create()
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode="POSE")

    def idle(an, t):
        p = t * math.tau
        an.clear()
        an.move("Hips", (0, 0, -0.006 + 0.004 * math.sin(p)))
        an.rot("Spine", x=2 + 1.2 * math.sin(p))
        an.rot("Chest", x=1.5 * math.sin(p + 0.4))
        an.rot("Neck", x=-2)
        an.rot("Head", x=3 * math.sin(p * 2 + 1) * 0.5, z=10 * math.sin(p) * math.sin(p * 0.5 + 0.3))
        for side, sg in (("L", 1), ("R", -1)):
            an.rot(f"UpperArm.{side}", x=-3 - 2 * math.sin(p + 0.8), y=sg * 7)
            an.rot(f"LowerArm.{side}", x=-12 - 3 * math.sin(p + 1.2))
            an.rot(f"Hand.{side}", x=-6)
            an.rot(f"UpperLeg.{side}", x=-3, y=sg * 2)
            an.rot(f"LowerLeg.{side}", x=5)
            an.rot(f"Foot.{side}", x=-2)

    def walk(an, t):
        pose_locomotion(an, t * math.tau, stride=26, knee=52, arm_swing=22, elbow=18, lean=4, bob=0.018)

    def run(an, t):
        pose_locomotion(an, t * math.tau, stride=46, knee=95, arm_swing=48, elbow=72, lean=13, bob=0.04)

    def crouch_idle(an, t):
        p = t * math.tau
        pose_locomotion(an, 0.0, stride=0, knee=0, arm_swing=0, elbow=40, lean=4, bob=0.0, crouch=1.0)
        an.rot("Head", x=-8 + 2 * math.sin(p), z=6 * math.sin(p))
        for side in ("L", "R"):
            an.rot(f"UpperArm.{side}", x=-25, y=(10 if side == "L" else -10))
        an.key  # noqa: B018 — позы ключуются в make_action

    def crouch_walk(an, t):
        pose_locomotion(an, t * math.tau, stride=18, knee=30, arm_swing=12, elbow=40, lean=4, bob=0.01, crouch=1.0)

    procedural = {
        "Idle": (90, idle), "Walk": (30, walk), "Run": (20, run),
        "CrouchIdle": (60, crouch_idle), "CrouchWalk": (36, crouch_walk),
    }
    acts = []
    for clip, (frames, fn) in procedural.items():
        path = mocap_path(clip)
        if path:
            acts.append(retarget_action(arm, clip, path))
        else:
            acts.append(make_action(arm, clip, frames, fn))
    arm.animation_data.action = acts[0]
    bpy.ops.object.mode_set(mode="OBJECT")
    return acts


# =========================================================================== сборка

def build_robot(name, v, args):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.context.scene.render.fps = FPS
    M = {
        "suit": mat_suit("Suit", v["suit"]),
        "armor": mat_paint("Armor", v["armor"], rust=v["rust"], rough=0.55, metallic=0.25),
        "black": mat_plastic("Black"),
        "rubber": mat_plastic("Rubber", (0.06, 0.06, 0.06), scratches=0.2, dirt=0.6),
        "rust": mat_paint("Rust", (0.42, 0.2, 0.08), rust=1.0, rough=0.85, metallic=0.5),
        "metal": mat_paint("Metal", (0.45, 0.45, 0.47), rust=0.4, rough=0.35, metallic=0.9),
        "led": mat_flat("Led", (0.6, 0.05, 0.03), emission=(1, 0.1, 0.05), strength=3.0),
        "visor": mat_flat("Visor", (0.01, 0.012, 0.014), rough=0.05),
    }
    for m in M.values():
        m["ycbe_mat"] = True

    kit = Kit(M)
    build_body(kit, v)

    # применяем модификаторы, группы вершин = кость
    for obj, bone in kit.parts:
        bpy.ops.object.select_all(action="DESELECT")
        obj.select_set(True)
        bpy.context.view_layer.objects.active = obj
        for mod in list(obj.modifiers):
            bpy.ops.object.modifier_apply(modifier=mod.name)
        bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
        vg = obj.vertex_groups.new(name=bone)
        vg.add(list(range(len(obj.data.vertices))), 1.0, "REPLACE")

    bpy.ops.object.select_all(action="DESELECT")
    for obj, _ in kit.parts:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = kit.parts[0][0]
    bpy.ops.object.join()
    body = bpy.context.active_object
    body.name = "Body"
    body.data.name = "Body"
    try:
        body.data.set_sharp_from_angle(angle=math.radians(40))
    except AttributeError:
        pass

    # развёртка и запекание (визор не запекается — это экран)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=math.radians(58), island_margin=0.004)
    bpy.ops.object.mode_set(mode="OBJECT")
    os.makedirs(OUT_DIR, exist_ok=True)
    bake_maps(body, name, args.tex)
    project_visor_uv(body)

    # арматура + скиннинг
    arm = build_armature()
    body.parent = arm
    mod = body.modifiers.new("Armature", "ARMATURE")
    mod.object = arm

    build_actions(arm)

    markers = []
    for marker, loc in (("Front", (0, -1.0, 0)), ("Top", (0, 0, 1.0))):
        e = bpy.data.objects.new(marker, None)
        e.location = loc
        bpy.context.scene.collection.objects.link(e)
        markers.append(e)

    export(name, [arm, body] + markers)


def project_visor_uv(obj):
    """Визору — развёртка 0..1 проекцией спереди: в Unity на неё рисуется лицо."""
    me = obj.data
    visor_idx = next(i for i, m in enumerate(me.materials) if m.name.startswith("Visor"))
    polys = [p for p in me.polygons if p.material_index == visor_idx]
    verts = {me.loops[li].vertex_index for p in polys for li in p.loop_indices}
    xs = [me.vertices[i].co.x for i in verts]
    zs = [me.vertices[i].co.z for i in verts]
    x0, w = min(xs), max(max(xs) - min(xs), 1e-4)
    z0, h = min(zs), max(max(zs) - min(zs), 1e-4)
    uv = me.uv_layers.active.data
    for p in polys:
        for li in p.loop_indices:
            co = me.vertices[me.loops[li].vertex_index].co
            # робот смотрит в -Y: его левая сторона (+X) — справа для зрителя, поэтому u = 1 - ...
            uv[li].uv = (1.0 - (co.x - x0) / w, (co.z - z0) / h)


def bake_maps(obj, name, size):
    import numpy as np

    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.samples = 4
    scene.render.bake.margin = 8

    def target(img):
        for mat in obj.data.materials:
            nt = mat.node_tree
            node = nt.nodes.get("BakeTarget") or nt.nodes.new("ShaderNodeTexImage")
            node.name = "BakeTarget"
            node.image = img
            nt.nodes.active = node

    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj

    albedo = bpy.data.images.new(f"Robot_{name}_Albedo", size, size, alpha=False)
    target(albedo)
    scene.render.bake.use_pass_direct = False
    scene.render.bake.use_pass_indirect = False
    scene.render.bake.use_pass_color = True
    bpy.ops.object.bake(type="DIFFUSE")
    albedo.filepath_raw = os.path.join(OUT_DIR, f"Robot_{name}_Albedo.png")
    albedo.file_format = "PNG"
    albedo.save()

    half = size // 2
    rough = bpy.data.images.new("rough", half, half, alpha=False, float_buffer=True)
    target(rough)
    bpy.ops.object.bake(type="ROUGHNESS")

    # металличность: временно выводим её как эмиссию
    metal = bpy.data.images.new("metal", half, half, alpha=False, float_buffer=True)
    saved = {}
    for mat in obj.data.materials:
        nt = mat.node_tree
        bsdf = next(nd for nd in nt.nodes if nd.type == "BSDF_PRINCIPLED")
        out = next(nd for nd in nt.nodes if nd.type == "OUTPUT_MATERIAL")
        saved[mat.name] = out.inputs["Surface"].links[0].from_socket
        em = nt.nodes.new("ShaderNodeEmission")
        mv = bsdf.inputs["Metallic"].default_value
        em.inputs["Color"].default_value = (mv, mv, mv, 1)
        nt.links.new(em.outputs[0], out.inputs["Surface"])
    target(metal)
    bpy.ops.object.bake(type="EMIT")
    for mat in obj.data.materials:
        nt = mat.node_tree
        out = next(nd for nd in nt.nodes if nd.type == "OUTPUT_MATERIAL")
        nt.links.new(saved[mat.name], out.inputs["Surface"])

    r = np.array(rough.pixels[:], dtype=np.float32).reshape(-1, 4)
    m = np.array(metal.pixels[:], dtype=np.float32).reshape(-1, 4)
    ms = np.zeros_like(r)
    ms[:, 0] = m[:, 0]
    ms[:, 1] = m[:, 0]
    ms[:, 2] = m[:, 0]
    ms[:, 3] = 1.0 - r[:, 0]
    out_img = bpy.data.images.new(f"Robot_{name}_MetalSmooth", half, half, alpha=True)
    out_img.pixels[:] = ms.ravel()
    out_img.filepath_raw = os.path.join(OUT_DIR, f"Robot_{name}_MetalSmooth.png")
    out_img.file_format = "PNG"
    out_img.save()


def export(name, objects):
    bpy.ops.object.mode_set(mode="OBJECT") if bpy.context.object and bpy.context.object.mode != "OBJECT" else None
    bpy.ops.object.select_all(action="DESELECT")
    for o in objects:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]
    bpy.ops.export_scene.fbx(
        filepath=os.path.join(OUT_DIR, f"Robot_{name}.fbx"),
        use_selection=True,
        object_types={"ARMATURE", "MESH", "EMPTY"},
        apply_scale_options="FBX_SCALE_ALL",
        axis_forward="-Z",
        axis_up="Y",
        bake_space_transform=False,
        add_leaf_bones=False,
        use_armature_deform_only=False,
        bake_anim=True,
        bake_anim_use_all_actions=True,
        bake_anim_use_nla_strips=False,
        bake_anim_force_startend_keying=True,
        bake_anim_simplify_factor=0.0,
        mesh_smooth_type="FACE",
        path_mode="STRIP",
    )


# =========================================================================== превью

def preview_scene(args, w, h):
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.samples = args.samples
    scene.cycles.use_denoising = True
    scene.render.resolution_x = w
    scene.render.resolution_y = h
    scene.view_settings.view_transform = "AgX"
    scene.view_settings.look = "AgX - Medium High Contrast"
    world = bpy.data.worlds.new("Dark")
    scene.world = world
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs["Color"].default_value = (0.01, 0.009, 0.008, 1)

    def light(name, energy, loc, color, size):
        d = bpy.data.lights.new(name, "AREA")
        d.energy = energy
        d.color = color
        d.size = size
        o = bpy.data.objects.new(name, d)
        o.location = loc
        o.rotation_euler = (Vector((0, 0, 1.1)) - Vector(loc)).to_track_quat("-Z", "Y").to_euler()
        scene.collection.objects.link(o)

    light("Key", 320, (-2.4, -3.0, 3.0), (1.0, 0.86, 0.7), 2.0)
    light("Rim", 420, (2.6, 2.4, 2.6), (0.45, 0.6, 1.0), 1.5)
    light("Fill", 50, (2.6, -2.6, 0.9), (1.0, 0.6, 0.35), 2.0)
    bpy.ops.mesh.primitive_plane_add(size=30)
    bpy.context.active_object.data.materials.append(mat_flat("Floor", (0.03, 0.03, 0.03), rough=0.55))
    cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam"))
    scene.collection.objects.link(cam)
    scene.camera = cam
    return cam


def aim(cam, loc, target, lens):
    cam.data.lens = lens
    cam.location = loc
    cam.rotation_euler = (Vector(target) - Vector(loc)).to_track_quat("-Z", "Y").to_euler()


def import_robot(name, offset_x=0.0, action=None, frame=0):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=os.path.join(OUT_DIR, f"Robot_{name}.fbx"))
    new = set(bpy.data.objects) - before
    albedo = bpy.data.images.load(os.path.join(OUT_DIR, f"Robot_{name}_Albedo.png"), check_existing=True)
    msmap = bpy.data.images.load(os.path.join(OUT_DIR, f"Robot_{name}_MetalSmooth.png"), check_existing=True)
    msmap.colorspace_settings.name = "Non-Color"
    baked = bpy.data.materials.new(f"Baked_{name}")
    baked.use_nodes = True
    nt = baked.node_tree
    bsdf = nt.nodes.get("Principled BSDF")
    t = nt.nodes.new("ShaderNodeTexImage"); t.image = albedo
    nt.links.new(t.outputs["Color"], bsdf.inputs["Base Color"])
    t2 = nt.nodes.new("ShaderNodeTexImage"); t2.image = msmap
    sep = nt.nodes.new("ShaderNodeSeparateColor")
    nt.links.new(t2.outputs["Color"], sep.inputs[0])
    nt.links.new(sep.outputs[0], bsdf.inputs["Metallic"])
    inv = nt.nodes.new("ShaderNodeMath"); inv.operation = "SUBTRACT"; inv.inputs[0].default_value = 1.0
    nt.links.new(t2.outputs["Alpha"], inv.inputs[1])
    nt.links.new(inv.outputs[0], bsdf.inputs["Roughness"])
    face = face_preview_material(name)
    holder = bpy.data.objects.new(f"Holder_{name}", None)
    bpy.context.scene.collection.objects.link(holder)
    holder.location.x = offset_x
    for o in new:
        if o.parent is None:
            o.parent = holder
        if o.type == "MESH":
            for slot in o.material_slots:
                n = slot.material.name.split(".")[0] if slot.material else ""
                if n == "Visor":
                    slot.material = face
                elif n != "Led":
                    slot.material = baked
        if o.type == "ARMATURE" and action:
            act = next(a for a in bpy.data.actions if a.name.split("|")[-1] == action and a.name not in _used_actions)
            o.animation_data_create()
            o.animation_data.action = act
            if hasattr(o.animation_data, "action_slot") and len(act.slots):
                o.animation_data.action_slot = act.slots[0]
            _used_actions.add(act.name)
    bpy.context.scene.frame_set(frame)
    return new


_used_actions = set()


def face_preview_material(name):
    """Превью лица на визоре: два светящихся «глаза» поверх тёмного стекла."""
    m = bpy.data.materials.new(f"FacePreview_{name}")
    nt, n, l, bsdf, coord = _nodes(m)
    uv = n.new("ShaderNodeUVMap")
    sep = n.new("ShaderNodeSeparateXYZ")
    l.new(uv.outputs["UV"], sep.inputs[0])
    eyes = None
    for cx in (0.33, 0.67):
        dx = n.new("ShaderNodeMath"); dx.operation = "SUBTRACT"; dx.inputs[1].default_value = cx
        l.new(sep.outputs["X"], dx.inputs[0])
        dy = n.new("ShaderNodeMath"); dy.operation = "SUBTRACT"; dy.inputs[1].default_value = 0.55
        l.new(sep.outputs["Y"], dy.inputs[0])
        ax = n.new("ShaderNodeMath"); ax.operation = "ABSOLUTE"; l.new(dx.outputs[0], ax.inputs[0])
        ay = n.new("ShaderNodeMath"); ay.operation = "ABSOLUTE"; l.new(dy.outputs[0], ay.inputs[0])
        cx_ = n.new("ShaderNodeMath"); cx_.operation = "LESS_THAN"; cx_.inputs[1].default_value = 0.09
        l.new(ax.outputs[0], cx_.inputs[0])
        cy_ = n.new("ShaderNodeMath"); cy_.operation = "LESS_THAN"; cy_.inputs[1].default_value = 0.14
        l.new(ay.outputs[0], cy_.inputs[0])
        e = n.new("ShaderNodeMath"); e.operation = "MULTIPLY"
        l.new(cx_.outputs[0], e.inputs[0]); l.new(cy_.outputs[0], e.inputs[1])
        if eyes is None:
            eyes = e
        else:
            mx = n.new("ShaderNodeMath"); mx.operation = "MAXIMUM"
            l.new(eyes.outputs[0], mx.inputs[0]); l.new(e.outputs[0], mx.inputs[1])
            eyes = mx
    col = n.new("ShaderNodeMix"); col.data_type = "RGBA"
    col.inputs["A"].default_value = (0, 0, 0, 1)
    col.inputs["B"].default_value = (0.35, 1.0, 0.55, 1)
    l.new(eyes.outputs[0], col.inputs["Factor"])
    l.new(col.outputs["Result"], bsdf.inputs["Emission Color"])
    bsdf.inputs["Emission Strength"].default_value = 6.0
    bsdf.inputs["Base Color"].default_value = (0.01, 0.012, 0.014, 1)
    bsdf.inputs["Roughness"].default_value = 0.08
    return m


def render_previews(args):
    names = list(VARIANTS)
    os.makedirs(PORTRAIT_DIR, exist_ok=True)
    os.makedirs(RENDER_DIR, exist_ok=True)
    for name in names:
        bpy.ops.wm.read_factory_settings(use_empty=True)
        _used_actions.clear()
        import_robot(name, action="Idle", frame=20)
        cam = preview_scene(args, 512, 640)
        aim(cam, (-1.3, -3.4, 1.45), (0, 0, 1.05), 55)
        bpy.context.scene.render.filepath = os.path.join(PORTRAIT_DIR, f"Portrait_{name}.png")
        bpy.ops.render.render(write_still=True)

    bpy.ops.wm.read_factory_settings(use_empty=True)
    _used_actions.clear()
    import_robot("Can", -1.1, "Idle", 20)
    import_robot("Toaster", 0.0, "Walk", 8)
    import_robot("Lantern", 1.1, "Idle", 50)
    bpy.context.scene.frame_set(8)
    cam = preview_scene(args, 1600, 900)
    aim(cam, (-1.1, -5.0, 1.6), (0, 0, 1.0), 40)
    bpy.context.scene.render.filepath = os.path.join(RENDER_DIR, "robots_lineup.png")
    bpy.ops.render.render(write_still=True)


def main():
    args = parse_args()
    if not args.preview_only:
        for name, v in VARIANTS.items():
            if args.only and name != args.only:
                continue
            print(f"=== Робот {name}")
            build_robot(name, v, args)
    if args.preview or args.preview_only:
        render_previews(args)


if __name__ == "__main__":
    main()
