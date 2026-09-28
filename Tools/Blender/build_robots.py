"""
Your Last Order — три модели роботов-курьеров (процедурно, Blender 4.x/5.x).

    blender --background --python Tools/Blender/build_robots.py -- [--preview] [--tex 1024]

Для каждого робота:
    Assets/_Project/Art/Models/Robots/Robot_<Name>.fbx
    Assets/_Project/Art/Models/Robots/Robot_<Name>_Albedo.png
    Assets/_Project/Art/Robots/Portrait_<Name>.png         (с флагом --preview — портрет для меню)
    docs/renders/robots_lineup.png                         (с флагом --preview)

Объекты внутри FBX (для анимации в Unity):
    Body     — корпус (неподвижный)
    Head     — голова (поворачивается за взглядом), есть не у всех
    Face     — светящиеся глаза/экран (материал Face, мерцает)
    ArmL/R   — руки, точка вращения в плече
    Antenna  — антенна, крутится при разговоре
    Front    — пустышка в 1 м перед роботом, Top — в 1 м над ним: по ним Unity выравнивает модель
Робот смотрит в -Y Blender, стоит на z = 0.
"""

import argparse
import math
import os
import sys

import bpy  # noqa: I001 — bpy импортируется раньше bmesh
import bmesh
from mathutils import Vector

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT_DIR = os.path.join(ROOT, "Assets", "_Project", "Art", "Models", "Robots")
PORTRAIT_DIR = os.path.join(ROOT, "Assets", "_Project", "Art", "Robots")
RENDER_DIR = os.path.join(ROOT, "docs", "renders")

PARTS = ["Body", "Head", "Face", "ArmL", "ArmR", "Antenna"]


def parse_args():
    argv = sys.argv
    argv = argv[argv.index("--") + 1:] if "--" in argv else argv[1:]
    p = argparse.ArgumentParser()
    p.add_argument("--preview", action="store_true")
    p.add_argument("--tex", type=int, default=1024)
    p.add_argument("--samples", type=int, default=32)
    p.add_argument("--preview-only", action="store_true", help="только превью по уже экспортированным FBX")
    return p.parse_args(argv)


# --------------------------------------------------------------------------- материалы

def paint_material(name, color, rust=0.5, dirt=0.6, rough=0.62, metallic=0.35):
    """Облупленная краска: основа + сколы до ржавчины + грязь в нижней части."""
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    n, l = nt.nodes, nt.links
    n.clear()
    out = n.new("ShaderNodeOutputMaterial")
    bsdf = n.new("ShaderNodeBsdfPrincipled")
    l.new(bsdf.outputs["BSDF"], out.inputs["Surface"])
    coord = n.new("ShaderNodeTexCoord")

    # вариация тона краски
    tone = n.new("ShaderNodeTexNoise")
    tone.inputs["Scale"].default_value = 6.0
    tone.inputs["Detail"].default_value = 6.0
    l.new(coord.outputs["Object"], tone.inputs["Vector"])
    base = n.new("ShaderNodeMix"); base.data_type = "RGBA"
    base.inputs["A"].default_value = (*[c * 0.8 for c in color], 1)
    base.inputs["B"].default_value = (*[min(1, c * 1.1) for c in color], 1)
    l.new(tone.outputs["Fac"], base.inputs["Factor"])

    # сколы до ржавчины (края пятен шума)
    chips = n.new("ShaderNodeTexNoise")
    chips.inputs["Scale"].default_value = 14.0
    chips.inputs["Detail"].default_value = 12.0
    chips.inputs["Roughness"].default_value = 0.7
    l.new(coord.outputs["Object"], chips.inputs["Vector"])
    chip_mask = n.new("ShaderNodeMapRange")
    chip_mask.inputs["From Min"].default_value = 0.62 - rust * 0.12
    chip_mask.inputs["From Max"].default_value = 0.66 - rust * 0.12
    l.new(chips.outputs["Fac"], chip_mask.inputs["Value"])

    rust_noise = n.new("ShaderNodeTexNoise")
    rust_noise.inputs["Scale"].default_value = 40.0
    l.new(coord.outputs["Object"], rust_noise.inputs["Vector"])
    rust_col = n.new("ShaderNodeMix"); rust_col.data_type = "RGBA"
    rust_col.inputs["A"].default_value = (0.32, 0.12, 0.04, 1)
    rust_col.inputs["B"].default_value = (0.55, 0.26, 0.08, 1)
    l.new(rust_noise.outputs["Fac"], rust_col.inputs["Factor"])

    painted = n.new("ShaderNodeMix"); painted.data_type = "RGBA"
    l.new(chip_mask.outputs[0], painted.inputs["Factor"])
    l.new(base.outputs["Result"], painted.inputs["A"])
    l.new(rust_col.outputs["Result"], painted.inputs["B"])

    # грязь снизу и подтёки
    sep = n.new("ShaderNodeSeparateXYZ")
    l.new(coord.outputs["Object"], sep.inputs[0])
    low = n.new("ShaderNodeMapRange")
    low.inputs["From Min"].default_value = 0.9
    low.inputs["From Max"].default_value = 0.0
    l.new(sep.outputs["Z"], low.inputs["Value"])
    streak_map = n.new("ShaderNodeMapping")
    streak_map.inputs["Scale"].default_value = (18.0, 18.0, 1.5)
    l.new(coord.outputs["Object"], streak_map.inputs["Vector"])
    streak = n.new("ShaderNodeTexNoise")
    streak.inputs["Scale"].default_value = 2.0
    l.new(streak_map.outputs[0], streak.inputs["Vector"])
    streak_r = n.new("ShaderNodeMapRange")
    streak_r.inputs["From Min"].default_value = 0.55
    streak_r.inputs["From Max"].default_value = 0.75
    l.new(streak.outputs["Fac"], streak_r.inputs["Value"])
    grime = n.new("ShaderNodeMath"); grime.operation = "MAXIMUM"
    l.new(low.outputs[0], grime.inputs[0]); l.new(streak_r.outputs[0], grime.inputs[1])
    grime_amt = n.new("ShaderNodeMath"); grime_amt.operation = "MULTIPLY"
    grime_amt.inputs[1].default_value = dirt
    l.new(grime.outputs[0], grime_amt.inputs[0])
    dirty = n.new("ShaderNodeMix"); dirty.data_type = "RGBA"
    dirty.inputs["B"].default_value = (0.09, 0.07, 0.05, 1)
    l.new(grime_amt.outputs[0], dirty.inputs["Factor"])
    l.new(painted.outputs["Result"], dirty.inputs["A"])

    l.new(dirty.outputs["Result"], bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = rough
    bsdf.inputs["Metallic"].default_value = metallic
    return m


def flat_material(name, color, emission=None, strength=0.0, metallic=0.2, roughness=0.5):
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


# --------------------------------------------------------------------------- примитивы

class Builder:
    def __init__(self):
        self.objects = []  # (obj, part)

    def _add(self, obj, name, mat, part, bevel=0.0, smooth=True):
        obj.name = name
        obj.data.materials.clear()
        obj.data.materials.append(mat)
        if bevel > 0:
            b = obj.modifiers.new("Bevel", "BEVEL")
            b.width = bevel
            b.segments = 3
            b.limit_method = "ANGLE"
        if smooth:
            for p in obj.data.polygons:
                p.use_smooth = True
        self.objects.append((obj, part))
        return obj

    def cyl(self, name, r, depth, loc, mat, part="Body", rot=(0, 0, 0), verts=24, bevel=0.0, r2=None):
        if r2 is None:
            bpy.ops.mesh.primitive_cylinder_add(vertices=verts, radius=r, depth=depth, location=loc, rotation=rot)
        else:
            bpy.ops.mesh.primitive_cone_add(vertices=verts, radius1=r, radius2=r2, depth=depth, location=loc, rotation=rot)
        return self._add(bpy.context.active_object, name, mat, part, bevel)

    def sphere(self, name, r, loc, mat, part="Body", scale=(1, 1, 1)):
        bpy.ops.mesh.primitive_uv_sphere_add(segments=24, ring_count=12, radius=r, location=loc)
        o = bpy.context.active_object
        o.scale = scale
        return self._add(o, name, mat, part)

    def box(self, name, size, loc, mat, part="Body", bevel=0.01, rot=(0, 0, 0)):
        bpy.ops.mesh.primitive_cube_add(size=1.0, location=loc, rotation=rot)
        o = bpy.context.active_object
        o.scale = size
        return self._add(o, name, mat, part, bevel, smooth=False)

    def torus(self, name, R, r, loc, mat, part="Body", rot=(0, 0, 0)):
        bpy.ops.mesh.primitive_torus_add(major_radius=R, minor_radius=r, location=loc, rotation=rot,
                                         major_segments=32, minor_segments=10)
        return self._add(bpy.context.active_object, name, mat, part)

    def tube(self, name, a, b, r, mat, part="Body", verts=12):
        """Цилиндр между точками a и b."""
        a, b = Vector(a), Vector(b)
        d = b - a
        rot = d.to_track_quat("Z", "Y").to_euler()
        return self.cyl(name, r, d.length, (a + b) / 2, mat, part, rot=rot, verts=verts)


# --------------------------------------------------------------------------- роботы

def robot_can(b, M):
    """«Консерва»: купол-банка, окуляры, ржавая юбка из колец, короткие лапы."""
    paint, rust, dark, face = M["paint"], M["rust"], M["dark"], M["face"]
    for s in (-1, 1):
        b.box(f"Foot{s}", (0.22, 0.34, 0.08), (s * 0.15, -0.04, 0.04), rust, bevel=0.03)
        for k in (-1, 0, 1):
            b.cyl(f"Claw{s}{k}", 0.025, 0.1, (s * 0.15 + k * 0.065, -0.23, 0.035), dark, rot=(math.pi / 2, 0, 0), verts=8)
        b.cyl(f"Leg{s}", 0.055, 0.24, (s * 0.15, 0, 0.2), dark, verts=12)
        b.sphere(f"Knee{s}", 0.07, (s * 0.15, -0.01, 0.2), rust)
    b.cyl("Skirt1", 0.34, 0.08, (0, 0, 0.34), rust, bevel=0.015)
    b.cyl("Skirt2", 0.37, 0.08, (0, 0, 0.43), rust, bevel=0.015)
    b.cyl("Skirt3", 0.40, 0.08, (0, 0, 0.52), rust, bevel=0.015)
    b.cyl("Torso", 0.40, 0.7, (0, 0, 0.91), paint, verts=40)
    b.sphere("Dome", 0.40, (0, 0, 1.26), paint, scale=(1, 1, 0.85))
    b.torus("Seam", 0.405, 0.022, (0, 0, 1.26), dark)
    b.torus("Band", 0.405, 0.03, (0, 0, 0.6), dark)
    # окуляры
    for s in (-1, 1):
        b.cyl(f"Goggle{s}", 0.085, 0.12, (s * 0.14, -0.36, 1.33), rust, rot=(math.pi / 2, 0, 0), bevel=0.01)
        b.cyl(f"Lens{s}", 0.062, 0.03, (s * 0.14, -0.425, 1.33), face, part="Face", rot=(math.pi / 2, 0, 0))
    b.tube("Hose", (0.22, -0.34, 1.38), (0.34, -0.05, 1.52), 0.018, dark)
    # иллюминатор-динамик сбоку
    b.torus("Porthole", 0.09, 0.022, (0.40, -0.05, 0.95), rust, rot=(0, math.pi / 2, 0))
    b.cyl("PortholeGlass", 0.08, 0.02, (0.395, -0.05, 0.95), dark, rot=(0, math.pi / 2, 0))
    for i in range(6):
        a = i / 6 * math.tau
        b.sphere(f"Rivet{i}", 0.018, (math.cos(a) * 0.4, math.sin(a) * 0.4, 0.72), dark)
    # руки
    for s, part in ((-1, "ArmL"), (1, "ArmR")):
        sh = (s * 0.44, 0, 1.05)
        el = (s * 0.5, -0.03, 0.8)
        wr = (s * 0.52, -0.1, 0.58)
        b.sphere(f"Shoulder{s}", 0.085, sh, rust, part=part)
        b.tube(f"Upper{s}", sh, el, 0.035, dark, part=part)
        b.sphere(f"Elbow{s}", 0.05, el, rust, part=part)
        b.tube(f"Sleeve{s}", el, wr, 0.07, paint, part=part, verts=16)
        for k in (-1, 0, 1):
            b.tube(f"Finger{s}{k}", (wr[0] + k * 0.03, wr[1] - 0.02, wr[2]),
                   (wr[0] + k * 0.035, wr[1] - 0.08, wr[2] - 0.1), 0.014, dark, part=part, verts=8)
    # антенна
    b.tube("AntennaRod", (0.18, 0.1, 1.5), (0.18, 0.1, 1.8), 0.012, dark, part="Antenna", verts=8)
    b.box("AntennaBar", (0.16, 0.012, 0.012), (0.18, 0.1, 1.8), dark, part="Antenna", bevel=0.0)
    b.sphere("AntennaTip", 0.03, (0.18, 0.1, 1.83), M["red"], part="Antenna")
    return {"ArmL": (-0.44, 0, 1.05), "ArmR": (0.44, 0, 1.05), "Antenna": (0.18, 0.1, 1.55)}


def robot_toaster(b, M):
    """«Тостер»: квадратный грузчик с ЭЛТ-монитором вместо головы."""
    paint, rust, dark, face, head = M["paint"], M["rust"], M["dark"], M["face"], M["paint2"]
    for s in (-1, 1):
        b.box(f"Boot{s}", (0.2, 0.3, 0.1), (s * 0.15, -0.03, 0.05), dark, bevel=0.025)
        b.cyl(f"Piston{s}", 0.045, 0.45, (s * 0.15, 0, 0.32), rust, verts=12)
        b.cyl(f"Sleeve{s}", 0.07, 0.24, (s * 0.15, 0, 0.44), paint, verts=16, bevel=0.01)
    b.box("Hip", (0.44, 0.3, 0.12), (0, 0, 0.6), dark, bevel=0.02)
    b.box("Torso", (0.62, 0.46, 0.6), (0, 0, 0.95), paint, bevel=0.05)
    for i in range(5):
        b.box(f"Vent{i}", (0.34, 0.02, 0.022), (0, -0.235, 0.82 + i * 0.045), dark, bevel=0.0)
    for s in (-1, 1):  # тостерные прорези сверху
        b.box(f"Slot{s}", (0.07, 0.3, 0.02), (s * 0.12, 0, 1.25), dark, bevel=0.0)
    b.box("Plate", (0.16, 0.01, 0.08), (0.18, -0.235, 1.1), rust, bevel=0.0)
    b.cyl("Neck", 0.06, 0.12, (0, 0, 1.31), dark, verts=12)
    # голова-монитор
    b.box("Monitor", (0.46, 0.4, 0.36), (0, 0.01, 1.55), head, part="Head", bevel=0.04)
    b.box("Bezel", (0.4, 0.02, 0.3), (0, -0.19, 1.55), dark, part="Head", bevel=0.01)
    b.box("Screen", (0.34, 0.01, 0.24), (0, -0.203, 1.55), face, part="Face", bevel=0.0)
    b.box("Knob", (0.04, 0.03, 0.04), (0.17, -0.19, 1.43), rust, part="Head", bevel=0.005)
    # антенна на голове
    b.tube("AntennaRod", (0.14, 0.05, 1.72), (0.2, 0.05, 1.95), 0.01, dark, part="Antenna", verts=8)
    b.box("AntennaBar", (0.18, 0.01, 0.01), (0.2, 0.05, 1.95), dark, part="Antenna", bevel=0.0)
    b.sphere("AntennaTip", 0.03, (0.2, 0.05, 1.97), M["red"], part="Antenna")
    # руки-манипуляторы
    for s, part in ((-1, "ArmL"), (1, "ArmR")):
        b.cyl(f"Shoulder{s}", 0.07, 0.1, (s * 0.36, 0, 1.12), rust, part=part, rot=(0, math.pi / 2, 0))
        b.box(f"UpperArm{s}", (0.1, 0.1, 0.28), (s * 0.4, 0, 0.97), paint, part=part, bevel=0.02)
        b.box(f"Forearm{s}", (0.12, 0.12, 0.26), (s * 0.41, -0.04, 0.71), dark, part=part, bevel=0.02)
        for k in (-1, 1):
            b.box(f"Clamp{s}{k}", (0.03, 0.06, 0.1), (s * 0.41 + k * 0.04, -0.06, 0.53), rust, part=part, bevel=0.005)
    return {"ArmL": (-0.36, 0, 1.12), "ArmR": (0.36, 0, 1.12), "Antenna": (0.14, 0.05, 1.72), "Head": (0, 0, 1.36)}


def robot_lantern(b, M):
    """«Фонарь»: высокий худой разведчик, голова — фонарь с одним глазом."""
    paint, rust, dark, face = M["paint"], M["rust"], M["dark"], M["face"]
    for s in (-1, 1):
        b.sphere(f"Foot{s}", 0.1, (s * 0.12, -0.05, 0.04), dark, scale=(1, 1.6, 0.45))
        b.cyl(f"Shin{s}", 0.04, 0.42, (s * 0.12, 0, 0.27), dark, verts=10)
        b.sphere(f"Knee{s}", 0.055, (s * 0.12, -0.01, 0.48), rust)
        b.cyl(f"Thigh{s}", 0.05, 0.34, (s * 0.12, 0, 0.66), paint, verts=12)
    b.torus("Hip", 0.17, 0.04, (0, 0, 0.84), rust)
    b.cyl("Torso", 0.22, 0.62, (0, 0, 1.15), paint, verts=32)
    for i in range(4):
        b.torus(f"Rib{i}", 0.225, 0.018, (0, 0, 0.92 + i * 0.15), rust)
    b.box("Chest", (0.2, 0.06, 0.16), (0, -0.21, 1.2), dark, bevel=0.01)
    for k in (-1, 1):
        b.cyl(f"Gauge{k}", 0.035, 0.02, (k * 0.05, -0.245, 1.22), M["paint2"], rot=(math.pi / 2, 0, 0))
    b.cyl("Neck", 0.05, 0.1, (0, 0, 1.51), dark, verts=10)
    # голова-фонарь
    b.cyl("LampBase", 0.17, 0.06, (0, 0, 1.57), dark, part="Head", bevel=0.01)
    b.cyl("LampGlass", 0.14, 0.24, (0, 0, 1.72), M["glass"], part="Head", verts=24)
    for i in range(6):
        a = i / 6 * math.tau
        b.cyl(f"Bar{i}", 0.012, 0.26, (math.cos(a) * 0.155, math.sin(a) * 0.155, 1.72), rust, part="Head", verts=6)
    b.cyl("LampTop", 0.19, 0.1, (0, 0, 1.89), dark, part="Head", r2=0.05)
    b.sphere("Eye", 0.07, (0, -0.02, 1.72), face, part="Face")
    # антенна-тарелка
    b.tube("AntennaRod", (0, 0, 1.93), (0, 0, 2.04), 0.012, dark, part="Antenna", verts=8)
    b.sphere("Dish", 0.08, (0.05, 0, 2.05), rust, part="Antenna", scale=(0.3, 1, 1))
    # длинные руки
    for s, part in ((-1, "ArmL"), (1, "ArmR")):
        sh = (s * 0.28, 0, 1.38)
        el = (s * 0.33, 0.0, 1.0)
        wr = (s * 0.35, -0.08, 0.64)
        b.sphere(f"Shoulder{s}", 0.07, sh, rust, part=part)
        b.tube(f"Upper{s}", sh, el, 0.035, paint, part=part)
        b.sphere(f"Elbow{s}", 0.045, el, rust, part=part)
        b.tube(f"Fore{s}", el, wr, 0.03, dark, part=part)
        for k in (-1, 0, 1):
            b.tube(f"Finger{s}{k}", wr, (wr[0] + k * 0.04, wr[1] - 0.05, wr[2] - 0.13), 0.011, dark, part=part, verts=6)
    return {"ArmL": (-0.28, 0, 1.38), "ArmR": (0.28, 0, 1.38), "Antenna": (0, 0, 1.93), "Head": (0, 0, 1.52)}


ROBOTS = {
    "Can": dict(build=robot_can, paint=(0.42, 0.52, 0.36), paint2=(0.7, 0.66, 0.55), rust=0.6),
    "Toaster": dict(build=robot_toaster, paint=(0.72, 0.56, 0.18), paint2=(0.62, 0.6, 0.52), rust=0.5),
    "Lantern": dict(build=robot_lantern, paint=(0.3, 0.38, 0.45), paint2=(0.85, 0.8, 0.65), rust=0.7),
}


# --------------------------------------------------------------------------- сборка

def build_robot(name, spec, args):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    M = {
        "paint": paint_material("Paint", spec["paint"], rust=spec["rust"]),
        "paint2": paint_material("Paint2", spec["paint2"], rust=spec["rust"] * 0.6, dirt=0.4),
        "rust": paint_material("Rust", (0.42, 0.2, 0.08), rust=1.0, dirt=0.5, rough=0.85, metallic=0.5),
        "dark": paint_material("DarkMetal", (0.1, 0.1, 0.1), rust=0.3, dirt=0.3, rough=0.5, metallic=0.8),
        "red": flat_material("Red", (0.6, 0.05, 0.03), emission=(1, 0.1, 0.05), strength=2.0),
        "glass": flat_material("Glass", (0.05, 0.07, 0.06), roughness=0.1),
        "face": flat_material("Face", (0.2, 1.0, 0.45), emission=(0.3, 1.0, 0.5), strength=6.0),
    }
    b = Builder()
    pivots = spec["build"](b, M)

    # применяем модификаторы/масштаб и помечаем каждую грань номером детали
    for obj, part in b.objects:
        bpy.ops.object.select_all(action="DESELECT")
        obj.select_set(True)
        bpy.context.view_layer.objects.active = obj
        for m in list(obj.modifiers):
            bpy.ops.object.modifier_apply(modifier=m.name)
        bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
        attr = obj.data.attributes.new("part", "INT", "FACE")
        idx = PARTS.index(part)
        for i in range(len(obj.data.polygons)):
            attr.data[i].value = idx

    # объединяем → одна развёртка и одна текстура на робота
    bpy.ops.object.select_all(action="DESELECT")
    for obj, _ in b.objects:
        obj.select_set(True)
    body = b.objects[0][0]
    bpy.context.view_layer.objects.active = body
    bpy.ops.object.join()
    robot = bpy.context.active_object
    robot.name = "Body"
    try:
        robot.data.set_sharp_from_angle(angle=math.radians(40))
    except AttributeError:
        pass

    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=math.radians(55), island_margin=0.006)
    bpy.ops.object.mode_set(mode="OBJECT")

    os.makedirs(OUT_DIR, exist_ok=True)
    bake(robot, name, args.tex)

    # разделяем обратно на детали
    parts = {"Body": robot}
    for idx, part in enumerate(PARTS):
        if part == "Body":
            continue
        obj = separate_part(robot, idx)
        if obj is None:
            continue
        obj.name = part
        obj.data.name = part
        if part in pivots:
            set_origin(obj, pivots[part])
        if part == "Face":
            project_front_uv(obj)
        parts[part] = obj

    # Лицо и антенна двигаются вместе с головой
    if "Head" in parts:
        for child in ("Face", "Antenna"):
            if child in parts:
                keep_parent(parts[child], parts["Head"])

    markers = []
    for marker, loc in (("Front", (0, -1.0, 0)), ("Top", (0, 0, 1.0))):
        e = bpy.data.objects.new(marker, None)
        e.location = loc
        bpy.context.scene.collection.objects.link(e)
        markers.append(e)

    export(name, list(parts.values()) + markers)
    return parts


def separate_part(obj, idx):
    before = set(bpy.data.objects)
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.mode_set(mode="EDIT")
    bm = bmesh.from_edit_mesh(obj.data)
    layer = bm.faces.layers.int.get("part")
    found = False
    for f in bm.faces:
        sel = f[layer] == idx
        f.select = sel
        found |= sel
    bmesh.update_edit_mesh(obj.data)
    if found:
        bpy.ops.mesh.separate(type="SELECTED")
    bpy.ops.object.mode_set(mode="OBJECT")
    new = [o for o in bpy.data.objects if o not in before]
    return new[0] if new else None


def project_front_uv(obj):
    """Лицу — своя развёртка 0..1 проекцией спереди: на экран «Тостера» рисуется лицо."""
    me = obj.data
    xs = [v.co.x for v in me.vertices]
    zs = [v.co.z for v in me.vertices]
    w = max(max(xs) - min(xs), 1e-4)
    h = max(max(zs) - min(zs), 1e-4)
    uv = me.uv_layers.active.data
    for poly in me.polygons:
        for li in poly.loop_indices:
            co = me.vertices[me.loops[li].vertex_index].co
            uv[li].uv = ((co.x - min(xs)) / w, (co.z - min(zs)) / h)


def set_origin(obj, point):
    bpy.context.scene.cursor.location = point
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.origin_set(type="ORIGIN_CURSOR")
    bpy.context.scene.cursor.location = (0, 0, 0)


def keep_parent(child, parent):
    child.parent = parent
    child.matrix_parent_inverse = parent.matrix_world.inverted()


def bake(obj, name, size):
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.samples = 4
    img = bpy.data.images.new(f"Robot_{name}_Albedo", size, size, alpha=False)
    for mat in obj.data.materials:
        nt = mat.node_tree
        node = nt.nodes.new("ShaderNodeTexImage")
        node.image = img
        nt.nodes.active = node
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    scene.render.bake.use_pass_direct = False
    scene.render.bake.use_pass_indirect = False
    scene.render.bake.use_pass_color = True
    scene.render.bake.margin = 6
    bpy.ops.object.bake(type="DIFFUSE")
    img.filepath_raw = os.path.join(OUT_DIR, f"Robot_{name}_Albedo.png")
    img.file_format = "PNG"
    img.save()


def export(name, objects):
    bpy.ops.object.select_all(action="DESELECT")
    for o in objects:
        o.select_set(True)
    bpy.ops.export_scene.fbx(
        filepath=os.path.join(OUT_DIR, f"Robot_{name}.fbx"),
        use_selection=True,
        object_types={"MESH", "EMPTY"},
        apply_scale_options="FBX_SCALE_UNITS",
        axis_forward="-Z",
        axis_up="Y",
        bake_space_transform=True,
        mesh_smooth_type="FACE",
        path_mode="STRIP",
        add_leaf_bones=False,
    )


# --------------------------------------------------------------------------- превью

def setup_render(args, w, h):
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
    bg = world.node_tree.nodes["Background"]
    bg.inputs["Color"].default_value = (0.012, 0.011, 0.01, 1)

    def light(name, kind, energy, loc, color, size=1.0):
        data = bpy.data.lights.new(name, kind)
        data.energy = energy
        data.color = color
        if kind == "AREA":
            data.size = size
        o = bpy.data.objects.new(name, data)
        o.location = loc
        o.rotation_euler = (Vector((0, 0, 1.0)) - Vector(loc)).to_track_quat("-Z", "Y").to_euler()
        scene.collection.objects.link(o)

    light("Key", "AREA", 260, (-2.2, -3.0, 2.8), (1.0, 0.88, 0.72), 2.0)
    light("Rim", "AREA", 320, (2.4, 2.2, 2.4), (0.45, 0.6, 1.0), 1.5)
    light("Fill", "AREA", 40, (2.5, -2.5, 0.8), (1.0, 0.6, 0.35), 2.0)

    bpy.ops.mesh.primitive_plane_add(size=20, location=(0, 0, 0))
    floor = bpy.context.active_object
    floor.data.materials.append(flat_material("Floor", (0.03, 0.03, 0.03), roughness=0.6))

    cam_data = bpy.data.cameras.new("Cam")
    cam = bpy.data.objects.new("Cam", cam_data)
    scene.collection.objects.link(cam)
    scene.camera = cam
    return cam


def aim(cam, loc, target, lens):
    cam.data.lens = lens
    cam.location = loc
    cam.rotation_euler = (Vector(target) - Vector(loc)).to_track_quat("-Z", "Y").to_euler()


def portrait(name, args):
    """Портрет одного робота для карточки выбора в меню."""
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=os.path.join(OUT_DIR, f"Robot_{name}.fbx"))
    apply_preview_materials(name)
    cam = setup_render(args, 512, 640)
    aim(cam, (-1.6, -3.6, 1.35), (0, 0, 0.95), 50)
    bpy.context.scene.render.filepath = os.path.join(PORTRAIT_DIR, f"Portrait_{name}.png")
    bpy.ops.render.render(write_still=True)


def lineup(args):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    for i, name in enumerate(ROBOTS):
        before = set(bpy.data.objects)
        bpy.ops.import_scene.fbx(filepath=os.path.join(OUT_DIR, f"Robot_{name}.fbx"))
        new = set(bpy.data.objects) - before
        apply_preview_materials(name, new)
        for o in new:
            if o.parent is None:
                o.location.x += (i - 1) * 1.25
    cam = setup_render(args, 1600, 900)
    aim(cam, (-1.2, -5.2, 1.5), (0, 0, 0.95), 42)
    bpy.context.scene.render.filepath = os.path.join(RENDER_DIR, "robots_lineup.png")
    bpy.ops.render.render(write_still=True)


def apply_preview_materials(name, objects=None):
    """После импорта FBX подставляем запечённую текстуру и светящееся лицо."""
    tex = bpy.data.images.load(os.path.join(OUT_DIR, f"Robot_{name}_Albedo.png"), check_existing=True)
    baked = bpy.data.materials.new(f"Baked_{name}")
    baked.use_nodes = True
    nt = baked.node_tree
    bsdf = nt.nodes.get("Principled BSDF")
    img = nt.nodes.new("ShaderNodeTexImage")
    img.image = tex
    nt.links.new(img.outputs["Color"], bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = 0.6
    bsdf.inputs["Metallic"].default_value = 0.3
    glow = flat_material(f"Glow_{name}", (0.2, 1, 0.45), emission=(0.3, 1.0, 0.5), strength=8.0)
    glass = flat_material(f"GlassPreview_{name}", (0.7, 0.8, 0.75), roughness=0.05)
    gb = glass.node_tree.nodes.get("Principled BSDF")
    gb.inputs["Transmission Weight"].default_value = 0.9
    for o in (objects if objects is not None else bpy.data.objects):
        if o.type != "MESH":
            continue
        for slot in o.material_slots:
            if slot.material is None:
                continue
            n = slot.material.name.split(".")[0]
            if n == "Face":
                slot.material = glow
            elif n == "Red":
                pass
            elif n == "Glass":
                slot.material = glass
            else:
                slot.material = baked


def main():
    args = parse_args()
    if not args.preview_only:
        for name, spec in ROBOTS.items():
            print(f"=== Робот {name}")
            build_robot(name, spec, args)
    if args.preview or args.preview_only:
        os.makedirs(PORTRAIT_DIR, exist_ok=True)
        os.makedirs(RENDER_DIR, exist_ok=True)
        for name in ROBOTS:
            portrait(name, args)
        lineup(args)


if __name__ == "__main__":
    main()
