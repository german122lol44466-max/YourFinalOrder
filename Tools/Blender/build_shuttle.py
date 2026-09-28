"""
Your Last Order — процедурная модель шаттла курьерской службы «You Can Buy Everything».

Запуск (Blender 4.x/5.x):
    blender --background --python Tools/Blender/build_shuttle.py -- [--preview] [--samples 32] [--tex 2048]
или через модуль bpy:
    python Tools/Blender/build_shuttle.py [--preview]

Результат:
    Assets/_Project/Art/Models/Shuttle/Shuttle.fbx          — модель (нос смотрит в -Y Blender → +Z Unity)
    Assets/_Project/Art/Models/Shuttle/Shuttle_Albedo.png   — запечённый цвет корпуса с грязью
    docs/renders/shuttle_preview.png                        — превью (с флагом --preview)

Объекты-пустышки Engine_0..2 отмечают сопла (эффекты реактора в Unity).
"""

import argparse
import math
import os
import sys

import bpy  # noqa: I001 — bpy должен импортироваться раньше bmesh
import bmesh
from mathutils import Matrix, Vector

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT_DIR = os.path.join(ROOT, "Assets", "_Project", "Art", "Models", "Shuttle")
RENDER_DIR = os.path.join(ROOT, "docs", "renders")


def parse_args():
    argv = sys.argv
    argv = argv[argv.index("--") + 1:] if "--" in argv else argv[1:]
    p = argparse.ArgumentParser()
    p.add_argument("--preview", action="store_true")
    p.add_argument("--samples", type=int, default=24)
    p.add_argument("--tex", type=int, default=2048)
    return p.parse_args(argv)


# --------------------------------------------------------------------------- утилиты

def reset_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def new_object(name, bm):
    mesh = bpy.data.meshes.new(name)
    bm.to_mesh(mesh)
    bm.free()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.scene.collection.objects.link(obj)
    return obj


def superellipse(t, n):
    c, s = math.cos(t), math.sin(t)
    x = math.copysign(abs(c) ** (2.0 / n), c)
    y = math.copysign(abs(s) ** (2.0 / n), s)
    return x, y


# --------------------------------------------------------------------------- геометрия

# Станции фюзеляжа: (y, полуширина, верх z, низ z, степень «квадратности» верха)
FUSELAGE = [
    (-9.20, 0.02, 0.62, 0.58, 2.0),
    (-9.05, 0.30, 0.95, 0.30, 2.2),
    (-8.70, 0.70, 1.35, 0.08, 2.4),
    (-8.10, 1.08, 1.78, -0.02, 2.6),
    (-7.30, 1.40, 2.22, -0.06, 2.8),
    (-6.40, 1.62, 2.62, -0.08, 3.0),
    (-5.40, 1.76, 2.88, -0.08, 3.2),
    (-4.40, 1.82, 2.92, -0.08, 3.4),
    (4.60, 1.82, 2.92, -0.08, 3.4),
    (5.80, 1.90, 2.96, -0.06, 3.4),
    (6.90, 1.92, 2.94, 0.05, 3.2),
    (7.60, 1.80, 2.84, 0.25, 3.0),
]
RING = 40


def build_fuselage():
    bm = bmesh.new()
    rings = []
    for (y, hw, top, bottom, n) in FUSELAGE:
        mid = (top + bottom) * 0.5
        hh = (top - bottom) * 0.5
        ring = []
        for i in range(RING):
            t = 2 * math.pi * i / RING
            # нижняя половина плоская (жаропрочное днище), верхняя — округлая
            nn = n if math.sin(t) >= 0 else 6.0
            sx, sz = superellipse(t, nn)
            ring.append(bm.verts.new((sx * hw, y, mid + sz * hh)))
        rings.append(ring)
    for a, b in zip(rings, rings[1:]):
        for i in range(RING):
            j = (i + 1) % RING
            bm.faces.new((a[i], a[j], b[j], b[i]))
    # заглушки: нос и корма
    bm.faces.new(list(reversed(rings[0])))
    bm.faces.new(rings[-1])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    obj = new_object("Fuselage", bm)
    return obj


def fuselage_point(y, t):
    """Точка поверхности фюзеляжа (линейная интерполяция между станциями)."""
    st = FUSELAGE
    if y <= st[0][0]:
        a = b = st[0]; k = 0.0
    elif y >= st[-1][0]:
        a = b = st[-1]; k = 0.0
    else:
        i = next(i for i in range(len(st) - 1) if st[i][0] <= y <= st[i + 1][0])
        a, b = st[i], st[i + 1]
        k = (y - a[0]) / (b[0] - a[0])
    hw, top, bottom, n = [a[j] + (b[j] - a[j]) * k for j in range(1, 5)]
    mid, hh = (top + bottom) * 0.5, (top - bottom) * 0.5
    nn = n if math.sin(t) >= 0 else 6.0
    sx, sz = superellipse(t, nn)
    return Vector((sx * hw, y, mid + sz * hh))


def surface_patch(bm, y0, y1, t0, t1, lift, nu=4, nv=4):
    """Изогнутая пластина, лежащая на фюзеляже (окна, полосы)."""
    grid = []
    for i in range(nv + 1):
        y = y0 + (y1 - y0) * i / nv
        row = []
        for j in range(nu + 1):
            t = t0 + (t1 - t0) * j / nu
            p = fuselage_point(y, t)
            # нормаль по конечным разностям
            du = fuselage_point(y, t + 0.01) - fuselage_point(y, t - 0.01)
            dv = fuselage_point(y + 0.01, t) - fuselage_point(y - 0.01, t)
            nrm = du.cross(dv).normalized()
            if nrm.dot(Vector((p.x, 0.0, p.z - 1.4))) < 0:
                nrm = -nrm
            row.append(bm.verts.new(p + nrm * lift))
        grid.append(row)
    for i in range(nv):
        for j in range(nu):
            bm.faces.new((grid[i][j], grid[i][j + 1], grid[i + 1][j + 1], grid[i + 1][j]))


def build_windows():
    frames = bmesh.new()
    panes = bmesh.new()
    d = math.radians
    # лобовые: 6 стёкол
    edges = [38, 55, 72, 90, 108, 125, 142]
    surface_patch(frames, -7.98, -7.22, d(34), d(146), 0.012, nu=16, nv=4)
    for a, b in zip(edges, edges[1:]):
        surface_patch(panes, -7.90, -7.30, d(a + 1.6), d(b - 1.6), 0.025)
    # боковые
    for (a, b) in ((20, 34), (146, 160)):
        surface_patch(frames, -7.18, -6.52, d(a - 2), d(b + 2), 0.012)
        surface_patch(panes, -7.12, -6.58, d(a), d(b), 0.025)
    # верхние (обзор при стыковке)
    for (a, b) in ((72, 88), (92, 108)):
        surface_patch(frames, -6.52, -5.86, d(a - 2), d(b + 2), 0.012)
        surface_patch(panes, -6.46, -5.92, d(a), d(b), 0.025)
    for bm in (frames, panes):
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return new_object("WindowFrames", frames), new_object("WindowPanes", panes)


def build_stripe():
    """Фирменная оранжевая полоса «You Can Buy Everything» вдоль борта."""
    bm = bmesh.new()
    d = math.radians
    for (a, b) in ((d(10), d(18)), (d(162), d(170))):
        surface_patch(bm, -6.2, 6.6, a, b, 0.01, nu=2, nv=24)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return new_object("Stripe", bm)


def extrude_profile(name, pts2d, plane, thickness, offset=0.0, bevel=0.06):
    """Плоский профиль → твёрдая пластина. plane: 'XY' (крыло) или 'YZ' (киль)."""
    bm = bmesh.new()
    verts = []
    for (u, v) in pts2d:
        if plane == "XY":
            verts.append(bm.verts.new((u, v, offset)))
        else:
            verts.append(bm.verts.new((offset, u, v)))
    face = bm.faces.new(verts)
    normal = Vector((0, 0, 1)) if plane == "XY" else Vector((1, 0, 0))
    res = bmesh.ops.extrude_face_region(bm, geom=[face])
    moved = [e for e in res["geom"] if isinstance(e, bmesh.types.BMVert)]
    bmesh.ops.translate(bm, verts=moved, vec=normal * thickness)
    bmesh.ops.translate(bm, verts=bm.verts[:], vec=-normal * thickness * 0.5)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    obj = new_object(name, bm)
    if bevel > 0:
        m = obj.modifiers.new("Bevel", "BEVEL")
        m.width = bevel
        m.segments = 2
        m.limit_method = "ANGLE"
    return obj


def build_wings():
    # Двойная дельта, как у орбитального челнока (правое крыло, X > 0)
    right = [
        (1.60, -5.20), (2.20, -2.60), (3.00, -0.20), (6.40, 5.00),
        (6.60, 6.40), (5.60, 6.70), (1.70, 6.70),
    ]
    left = [(-x, y) for (x, y) in reversed(right)]
    w1 = extrude_profile("WingR", right, "XY", 0.34, offset=0.30)
    w2 = extrude_profile("WingL", left, "XY", 0.34, offset=0.30)
    # элевоны — отдельные тёмные пластины на задней кромке
    elevR = [(2.2, 6.70), (6.55, 6.40), (6.62, 7.05), (2.2, 7.25)]
    elevL = [(-x, y) for (x, y) in reversed(elevR)]
    e1 = extrude_profile("ElevonR", elevR, "XY", 0.18, offset=0.30, bevel=0.03)
    e2 = extrude_profile("ElevonL", elevL, "XY", 0.18, offset=0.30, bevel=0.03)
    return [w1, w2, e1, e2]


def build_tail():
    fin = [(3.20, 2.70), (6.60, 7.60), (7.70, 7.60), (7.80, 5.60), (7.55, 2.70)]
    t = extrude_profile("Tail", fin, "YZ", 0.34, offset=0.0, bevel=0.05)
    rudder = [(7.62, 3.10), (7.80, 3.10), (7.95, 5.50), (7.75, 7.40), (7.60, 7.40)]
    r = extrude_profile("Rudder", rudder, "YZ", 0.22, offset=0.0, bevel=0.02)
    return [t, r]


def build_pods():
    objs = []
    for side in (-1, 1):
        bpy.ops.mesh.primitive_uv_sphere_add(segments=32, ring_count=16, radius=1.0,
                                             location=(side * 1.25, 6.0, 2.62))
        pod = bpy.context.active_object
        pod.name = "OMSPod" + ("R" if side > 0 else "L")
        pod.scale = (0.72, 2.3, 0.62)
        objs.append(pod)
    return objs


def build_engines():
    """Три сопла: одно сверху, два снизу. Возвращает меши и позиции среза сопел."""
    specs = [((0.0, 7.55, 2.05), 0.62), ((-0.95, 7.55, 1.05), 0.58), ((0.95, 7.55, 1.05), 0.58)]
    meshes, exits = [], []
    for i, (loc, r) in enumerate(specs):
        bm = bmesh.new()
        seg = 32
        length = 1.35
        rings = []
        # профиль колокола: горло → срез
        profile = [(0.0, r * 0.55), (0.25, r * 0.50), (0.6, r * 0.66), (1.0, r * 0.86), (length, r)]
        for (d, rad) in profile:
            ring = []
            for s in range(seg):
                a = 2 * math.pi * s / seg
                ring.append(bm.verts.new((loc[0] + math.cos(a) * rad, loc[1] + d, loc[2] + math.sin(a) * rad)))
            rings.append(ring)
        for a_, b_ in zip(rings, rings[1:]):
            for s in range(seg):
                t = (s + 1) % seg
                bm.faces.new((a_[s], a_[t], b_[t], b_[s]))
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        obj = new_object(f"Nozzle_{i}", bm)
        sol = obj.modifiers.new("Solid", "SOLIDIFY")
        sol.thickness = 0.05
        meshes.append(obj)

        # светящийся диск внутри горла
        bpy.ops.mesh.primitive_circle_add(vertices=24, radius=r * 0.54, fill_type="NGON",
                                          location=(loc[0], loc[1] + 0.08, loc[2]),
                                          rotation=(math.radians(90), 0, 0))
        glow = bpy.context.active_object
        glow.name = f"NozzleGlow_{i}"
        meshes.append(glow)
        exits.append(Vector((loc[0], loc[1] + length, loc[2])))
    return meshes, exits


def build_details():
    objs = []
    # антенна-тарелка на спине (курьерская связь)
    bpy.ops.mesh.primitive_cylinder_add(vertices=12, radius=0.05, depth=0.7, location=(0.6, -2.2, 3.25))
    objs.append(bpy.context.active_object)
    bpy.ops.mesh.primitive_uv_sphere_add(segments=16, ring_count=8, radius=0.32, location=(0.6, -2.2, 3.6))
    dish = bpy.context.active_object
    dish.scale = (1.0, 1.0, 0.3)
    objs.append(dish)
    # маневровые двигатели на носу
    for side in (-1, 1):
        for k in range(3):
            bpy.ops.mesh.primitive_cylinder_add(vertices=10, radius=0.07, depth=0.12,
                                                location=(side * (1.02 + 0.02 * k), -7.9 + k * 0.22, 1.35),
                                                rotation=(0, math.radians(90), 0))
            objs.append(bpy.context.active_object)
    # грузовой люк: рёбра створок
    for y in (-3.6, -1.2, 1.2, 3.6):
        bpy.ops.mesh.primitive_cube_add(size=1.0, location=(0, y, 2.9))
        rib = bpy.context.active_object
        rib.scale = (2.1, 0.05, 0.06)
        objs.append(rib)
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=(0, 0.0, 2.955))
    seam = bpy.context.active_object
    seam.scale = (0.05, 9.0, 0.04)
    objs.append(seam)
    # фары на носу (для посадки в тумане)
    for side in (-1, 1):
        bpy.ops.mesh.primitive_uv_sphere_add(segments=12, ring_count=6, radius=0.12,
                                             location=(side * 0.55, -8.72, 0.62))
        lamp = bpy.context.active_object
        lamp.name = "Headlight"
        objs.append(lamp)
    return objs


# --------------------------------------------------------------------------- материалы

def make_hull_material():
    m = bpy.data.materials.new("Hull")
    m.use_nodes = True
    nt = m.node_tree
    nodes, links = nt.nodes, nt.links
    nodes.clear()
    out = nodes.new("ShaderNodeOutputMaterial")
    bsdf = nodes.new("ShaderNodeBsdfPrincipled")
    links.new(bsdf.outputs["BSDF"], out.inputs["Surface"])

    geo = nodes.new("ShaderNodeNewGeometry")
    obj_coord = nodes.new("ShaderNodeTexCoord")

    # 1) базовая окраска: белый верх, чёрная теплозащита снизу и на носу
    sep_n = nodes.new("ShaderNodeSeparateXYZ")
    links.new(geo.outputs["Normal"], sep_n.inputs[0])
    sep_p = nodes.new("ShaderNodeSeparateXYZ")
    links.new(obj_coord.outputs["Object"], sep_p.inputs[0])

    # маска низа по нормали
    down = nodes.new("ShaderNodeMapRange")
    down.inputs["From Min"].default_value = -0.15
    down.inputs["From Max"].default_value = -0.35
    links.new(sep_n.outputs["Z"], down.inputs["Value"])
    # маска носа по Y
    nose = nodes.new("ShaderNodeMapRange")
    nose.inputs["From Min"].default_value = -8.35
    nose.inputs["From Max"].default_value = -8.55
    links.new(sep_p.outputs["Y"], nose.inputs["Value"])
    # передние кромки крыльев — тоже тёмные
    edge = nodes.new("ShaderNodeMapRange")
    edge.inputs["From Min"].default_value = -0.55
    edge.inputs["From Max"].default_value = -0.8
    links.new(sep_n.outputs["Y"], edge.inputs["Value"])
    lowz = nodes.new("ShaderNodeMapRange")  # только для крыльев (низкий z)
    lowz.inputs["From Min"].default_value = 0.7
    lowz.inputs["From Max"].default_value = 0.55
    links.new(sep_p.outputs["Z"], lowz.inputs["Value"])
    edge_m = nodes.new("ShaderNodeMath"); edge_m.operation = "MULTIPLY"
    links.new(edge.outputs[0], edge_m.inputs[0]); links.new(lowz.outputs[0], edge_m.inputs[1])

    tiles = nodes.new("ShaderNodeMath"); tiles.operation = "MAXIMUM"
    links.new(down.outputs[0], tiles.inputs[0]); links.new(nose.outputs[0], tiles.inputs[1])
    tiles2 = nodes.new("ShaderNodeMath"); tiles2.operation = "MAXIMUM"
    links.new(tiles.outputs[0], tiles2.inputs[0]); links.new(edge_m.outputs[0], tiles2.inputs[1])

    # сетка теплозащитных плиток
    brick = nodes.new("ShaderNodeTexBrick")
    brick.inputs["Scale"].default_value = 3.2
    brick.inputs["Mortar Size"].default_value = 0.012
    brick.offset = 0.0
    brick.inputs["Color1"].default_value = (0.045, 0.045, 0.048, 1)
    brick.inputs["Color2"].default_value = (0.07, 0.068, 0.066, 1)
    brick.inputs["Mortar"].default_value = (0.02, 0.02, 0.02, 1)
    links.new(obj_coord.outputs["Object"], brick.inputs["Vector"])

    # панели корпуса
    panels = nodes.new("ShaderNodeTexBrick")
    panels.inputs["Scale"].default_value = 0.9
    panels.inputs["Mortar Size"].default_value = 0.006
    panels.inputs["Brick Width"].default_value = 1.4
    panels.inputs["Row Height"].default_value = 0.6
    panels.inputs["Color1"].default_value = (0.62, 0.61, 0.58, 1)
    panels.inputs["Color2"].default_value = (0.57, 0.56, 0.53, 1)
    panels.inputs["Mortar"].default_value = (0.22, 0.21, 0.2, 1)
    links.new(obj_coord.outputs["Object"], panels.inputs["Vector"])

    base = nodes.new("ShaderNodeMix"); base.data_type = "RGBA"
    links.new(tiles2.outputs[0], base.inputs["Factor"])
    links.new(panels.outputs["Color"], base.inputs["A"])
    links.new(brick.outputs["Color"], base.inputs["B"])

    # 2) грязь: подтёки вдоль корпуса + пятна + копоть у кормы
    streak_map = nodes.new("ShaderNodeMapping")
    streak_map.inputs["Scale"].default_value = (3.0, 0.8, 3.0)
    links.new(obj_coord.outputs["Object"], streak_map.inputs["Vector"])
    streaks = nodes.new("ShaderNodeTexNoise")
    streaks.inputs["Scale"].default_value = 1.6
    streaks.inputs["Detail"].default_value = 8.0
    links.new(streak_map.outputs[0], streaks.inputs["Vector"])
    streak_r = nodes.new("ShaderNodeMapRange")
    streak_r.inputs["From Min"].default_value = 0.56
    streak_r.inputs["From Max"].default_value = 0.8
    links.new(streaks.outputs["Fac"], streak_r.inputs["Value"])

    blotch = nodes.new("ShaderNodeTexNoise")
    blotch.inputs["Scale"].default_value = 0.9
    blotch.inputs["Detail"].default_value = 10.0
    blotch.inputs["Roughness"].default_value = 0.65
    links.new(obj_coord.outputs["Object"], blotch.inputs["Vector"])
    blotch_r = nodes.new("ShaderNodeMapRange")
    blotch_r.inputs["From Min"].default_value = 0.5
    blotch_r.inputs["From Max"].default_value = 0.68
    links.new(blotch.outputs["Fac"], blotch_r.inputs["Value"])

    soot = nodes.new("ShaderNodeMapRange")
    soot.inputs["From Min"].default_value = 4.8
    soot.inputs["From Max"].default_value = 7.8
    links.new(sep_p.outputs["Y"], soot.inputs["Value"])
    soot_n = nodes.new("ShaderNodeMath"); soot_n.operation = "MULTIPLY"
    links.new(soot.outputs[0], soot_n.inputs[0]); links.new(blotch.outputs["Fac"], soot_n.inputs[1])

    dirt = nodes.new("ShaderNodeMath"); dirt.operation = "MAXIMUM"
    links.new(streak_r.outputs[0], dirt.inputs[0]); links.new(blotch_r.outputs[0], dirt.inputs[1])
    dirt2 = nodes.new("ShaderNodeMath"); dirt2.operation = "MAXIMUM"
    links.new(dirt.outputs[0], dirt2.inputs[0]); links.new(soot_n.outputs[0], dirt2.inputs[1])
    dirt_amt = nodes.new("ShaderNodeMath"); dirt_amt.operation = "MULTIPLY"
    dirt_amt.inputs[1].default_value = 0.75
    links.new(dirt2.outputs[0], dirt_amt.inputs[0])

    dirty = nodes.new("ShaderNodeMix"); dirty.data_type = "RGBA"
    dirty.inputs["B"].default_value = (0.16, 0.12, 0.08, 1)
    links.new(dirt_amt.outputs[0], dirty.inputs["Factor"])
    links.new(base.outputs["Result"], dirty.inputs["A"])

    # ржавые сколы
    chips = nodes.new("ShaderNodeTexVoronoi")
    chips.inputs["Scale"].default_value = 9.0
    links.new(obj_coord.outputs["Object"], chips.inputs["Vector"])
    chips_r = nodes.new("ShaderNodeMapRange")
    chips_r.inputs["From Min"].default_value = 0.06
    chips_r.inputs["From Max"].default_value = 0.03
    links.new(chips.outputs["Distance"], chips_r.inputs["Value"])
    chips_m = nodes.new("ShaderNodeMath"); chips_m.operation = "MULTIPLY"
    links.new(chips_r.outputs[0], chips_m.inputs[0]); links.new(blotch.outputs["Fac"], chips_m.inputs[1])
    rusty = nodes.new("ShaderNodeMix"); rusty.data_type = "RGBA"
    rusty.inputs["B"].default_value = (0.36, 0.14, 0.05, 1)
    links.new(chips_m.outputs[0], rusty.inputs["Factor"])
    links.new(dirty.outputs["Result"], rusty.inputs["A"])

    links.new(rusty.outputs["Result"], bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = 0.72
    bsdf.inputs["Metallic"].default_value = 0.15
    return m


def flat_material(name, color, emission=None, strength=0.0, metallic=0.0, roughness=0.5):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    bsdf = m.node_tree.nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = (*color, 1)
    bsdf.inputs["Metallic"].default_value = metallic
    bsdf.inputs["Roughness"].default_value = roughness
    if emission:
        bsdf.inputs["Emission Color"].default_value = (*emission, 1)
        bsdf.inputs["Emission Strength"].default_value = strength
    return m


# --------------------------------------------------------------------------- сборка

def main():
    args = parse_args()
    reset_scene()
    scene = bpy.context.scene

    hull = make_hull_material()
    glass = flat_material("Glass", (0.015, 0.02, 0.025), emission=(1.0, 0.6, 0.3), strength=0.15,
                          metallic=0.0, roughness=0.06)
    stripe = flat_material("Stripe", (0.62, 0.24, 0.06), metallic=0.1, roughness=0.6)
    metal = flat_material("DarkMetal", (0.08, 0.08, 0.085), metallic=0.8, roughness=0.45)
    glow = flat_material("EngineGlow", (0.2, 0.6, 1.0), emission=(0.35, 0.75, 1.0), strength=25.0)
    lamp = flat_material("Lamp", (1, 1, 0.9), emission=(1.0, 0.92, 0.7), strength=15.0)

    parts = []
    fus = build_fuselage()
    parts.append(fus)
    parts += build_wings()
    parts += build_tail()
    parts += build_pods()
    details = build_details()
    parts += details
    engines, exits = build_engines()

    # материалы
    for obj in parts:
        if obj.name.startswith("Headlight"):
            obj.data.materials.append(lamp)
        elif obj.name.startswith(("Elevon", "Rudder")) or obj in details:
            obj.data.materials.append(hull if obj.name.startswith(("Elevon", "Rudder")) else metal)
        else:
            obj.data.materials.append(hull)
    for obj in engines:
        obj.data.materials.append(glow if obj.name.startswith("NozzleGlow") else metal)
    frames, panes = build_windows()
    frames.data.materials.append(metal)
    panes.data.materials.append(glass)
    band = build_stripe()
    band.data.materials.append(stripe)
    engines += [frames, panes, band]

    # применяем модификаторы и объединяем всё в один меш
    all_objs = parts + engines
    bpy.ops.object.select_all(action="DESELECT")
    for o in all_objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = fus
    for o in all_objs:
        bpy.context.view_layer.objects.active = o
        for m in list(o.modifiers):
            bpy.ops.object.modifier_apply(modifier=m.name)
        bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    for o in all_objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = fus
    bpy.ops.object.join()
    ship = bpy.context.active_object
    ship.name = "Shuttle"
    for poly in ship.data.polygons:
        poly.use_smooth = True
    try:
        ship.data.set_sharp_from_angle(angle=math.radians(35))
    except AttributeError:
        pass

    # UV для запекания
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=math.radians(60), island_margin=0.004)
    bpy.ops.object.mode_set(mode="OBJECT")

    # пустышки сопел
    empties = []
    for i, p in enumerate(exits):
        e = bpy.data.objects.new(f"Engine_{i}", None)
        e.location = p
        e.parent = ship
        scene.collection.objects.link(e)
        empties.append(e)

    os.makedirs(OUT_DIR, exist_ok=True)
    bake_textures(ship, args)
    export_fbx(ship, empties)
    if args.preview:
        render_preview(ship, args)


def bake_textures(ship, args):
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.samples = 4

    size = args.tex
    albedo = bpy.data.images.new("Shuttle_Albedo", size, size, alpha=False)
    for mat in ship.data.materials:
        nt = mat.node_tree
        node = nt.nodes.new("ShaderNodeTexImage")
        node.image = albedo
        node.name = "BakeTarget"
        nt.nodes.active = node

    bpy.ops.object.select_all(action="DESELECT")
    ship.select_set(True)
    bpy.context.view_layer.objects.active = ship
    scene.render.bake.use_pass_direct = False
    scene.render.bake.use_pass_indirect = False
    scene.render.bake.use_pass_color = True
    scene.render.bake.margin = 8
    bpy.ops.object.bake(type="DIFFUSE")
    albedo.filepath_raw = os.path.join(OUT_DIR, "Shuttle_Albedo.png")
    albedo.file_format = "PNG"
    albedo.save()


def export_fbx(ship, empties):
    bpy.ops.object.select_all(action="DESELECT")
    ship.select_set(True)
    for e in empties:
        e.select_set(True)
    bpy.ops.export_scene.fbx(
        filepath=os.path.join(OUT_DIR, "Shuttle.fbx"),
        use_selection=True,
        object_types={"MESH", "EMPTY"},
        apply_scale_options="FBX_SCALE_UNITS",
        axis_forward="-Z",
        axis_up="Y",
        bake_space_transform=True,
        mesh_smooth_type="FACE",
        use_mesh_modifiers=True,
        path_mode="STRIP",
        add_leaf_bones=False,
    )


def render_preview(ship, args):
    scene = bpy.context.scene
    os.makedirs(RENDER_DIR, exist_ok=True)
    scene.render.engine = "CYCLES"
    scene.cycles.samples = args.samples
    scene.cycles.use_denoising = True
    scene.render.resolution_x = 1600
    scene.render.resolution_y = 900
    scene.render.film_transparent = False

    world = bpy.data.worlds.new("Space")
    scene.world = world
    world.use_nodes = True
    bg = world.node_tree.nodes["Background"]
    bg.inputs["Color"].default_value = (0.004, 0.006, 0.012, 1)
    bg.inputs["Strength"].default_value = 1.0

    cam_data = bpy.data.cameras.new("Cam")
    cam_data.lens = 40
    cam = bpy.data.objects.new("Cam", cam_data)
    scene.collection.objects.link(cam)
    scene.camera = cam
    target = Vector((0.0, -0.5, 2.0))

    key = bpy.data.lights.new("Key", "SUN")
    key.energy = 4.5
    key.color = (1.0, 0.93, 0.85)
    k = bpy.data.objects.new("Key", key)
    k.rotation_euler = (math.radians(55), math.radians(10), math.radians(-40))
    scene.collection.objects.link(k)
    rim = bpy.data.lights.new("Rim", "SUN")
    rim.energy = 2.5
    rim.color = (0.45, 0.6, 1.0)
    r = bpy.data.objects.new("Rim", rim)
    r.rotation_euler = (math.radians(-60), math.radians(0), math.radians(150))
    scene.collection.objects.link(r)

    scene.view_settings.view_transform = "AgX"
    scene.view_settings.look = "AgX - Medium High Contrast"
    shots = {
        "shuttle_preview.png": (-22.0, -21.0, 10.0),
        "shuttle_preview_rear.png": (17.0, 24.0, 7.0),
    }
    for name, loc in shots.items():
        cam.location = loc
        cam.rotation_euler = (target - cam.location).to_track_quat("-Z", "Y").to_euler()
        scene.render.filepath = os.path.join(RENDER_DIR, name)
        bpy.ops.render.render(write_still=True)


if __name__ == "__main__":
    main()
