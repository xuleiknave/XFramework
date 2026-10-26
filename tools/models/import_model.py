"""
把外部做好的角色模型（AI 3D 生成或美术交付的 .glb / .gltf / .fbx / .obj，带一张颜色贴图）
整理成工程规格，导出到 Resources/StarlineModels/<单位 id>.fbx 和 <单位 id>_tex.png。

整理内容（规格见 docs/art-direction.md）：
- 合并成一个网格，按 --turn 转到正面朝 Blender -Y（Unity +Z），轴心放到底面中心
- 按 --height 缩放（1 格角色约 1.6，2×2 约 3.2），并保证不超出底座
- 面数超过 --faces 时减面
- 加棋子底座和魔网发光环（悬空单位用 --no-base 不加）；底座颜色写进贴图顶部新加的一条色带，所以仍然只有一张贴图
- 两个材质槽：Hull（角色贴图）和 Glow（发光环），和脚本生成的模型一致

运行方式：
    python3 tools/models/import_model.py 输入.glb 单位id [--turn 角度] [--height 1.6] [--faces 20000]
        [--cells 1|2] [--no-base] [--preview 预览图.png]
    （或 blender --background --python tools/models/import_model.py -- 同样的参数）
"""

import argparse
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import bpy  # noqa: E402  必须先于 bmesh 导入
import bmesh  # noqa: E402
from mathutils import Matrix, Vector  # noqa: E402

import build_models as bm_lib  # noqa: E402

BASE_HEIGHT = 0.08
BASE_RADIUS = {1: 0.75, 2: 1.6}
BASE_COLOUR = "stone_dark"


def parse_args():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else sys.argv[1:]
    p = argparse.ArgumentParser(description="整理外部角色模型并导出到工程")
    p.add_argument("source")
    p.add_argument("unit_id")
    p.add_argument("--turn", type=float, default=0.0, help="绕竖轴旋转的角度，让正面朝 Blender -Y")
    p.add_argument("--height", type=float, default=None, help="模型高度（不含底座），默认 1 格 1.6、2 格 3.2")
    p.add_argument("--faces", type=int, default=20000, help="三角面上限")
    p.add_argument("--cells", type=int, choices=(1, 2), default=1)
    p.add_argument("--no-base", action="store_true", help="悬空单位不加底座")
    p.add_argument("--preview", default=None, help="渲染一张预览图到这个路径")
    p.add_argument("--out", default=bm_lib.OUT_DIR)
    return p.parse_args(argv)


def import_source(path):
    ext = os.path.splitext(path)[1].lower()
    if ext in (".glb", ".gltf"):
        bpy.ops.import_scene.gltf(filepath=path)
    elif ext == ".fbx":
        bpy.ops.import_scene.fbx(filepath=path)
    elif ext == ".obj":
        bpy.ops.wm.obj_import(filepath=path)
    else:
        raise SystemExit("不支持的格式：" + ext)
    meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    if not meshes:
        raise SystemExit("文件里没有网格")
    bpy.ops.object.select_all(action="DESELECT")
    for o in meshes:
        o.select_set(True)
    bpy.context.view_layer.objects.active = meshes[0]
    if len(meshes) > 1:
        bpy.ops.object.join()
    obj = bpy.context.view_layer.objects.active
    # 去掉父级（glTF 常带一个空物体做根），把变换烘进顶点
    bpy.ops.object.parent_clear(type="CLEAR_KEEP_TRANSFORM")
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    return obj


def find_base_colour_image(obj):
    for slot in obj.material_slots:
        mat = slot.material
        if mat is None or not mat.use_nodes:
            continue
        for node in mat.node_tree.nodes:
            if node.type == "BSDF_PRINCIPLED":
                link = node.inputs["Base Color"].links
                if link and link[0].from_node.type == "TEX_IMAGE":
                    return link[0].from_node.image
    raise SystemExit("找不到颜色贴图：模型需要一张接在 Base Color 上的贴图")


def normalise(obj, turn, height, radius_limit):
    mesh = obj.data
    mesh.transform(Matrix.Rotation(math.radians(turn), 4, "Z"))
    xs = [v.co.x for v in mesh.vertices]
    ys = [v.co.y for v in mesh.vertices]
    zs = [v.co.z for v in mesh.vertices]
    cx, cy = (min(xs) + max(xs)) / 2, (min(ys) + max(ys)) / 2
    k = height / (max(zs) - min(zs))
    reach = max(math.hypot(x - cx, y - cy) for x, y in zip(xs, ys)) * k
    if reach > radius_limit:
        k *= radius_limit / reach
    m = Matrix.Diagonal((k, k, k, 1.0)) @ Matrix.Translation((-cx, -cy, -min(zs)))
    mesh.transform(m)
    mesh.update()
    return k


def decimate(obj, faces):
    tris = sum(len(p.vertices) - 2 for p in obj.data.polygons)
    if tris <= faces:
        return tris
    mod = obj.modifiers.new("Decimate", "DECIMATE")
    mod.ratio = faces / tris
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.modifier_apply(modifier=mod.name)
    return sum(len(p.vertices) - 2 for p in obj.data.polygons)


def texture_with_base_strip(image, uv_layer_data):
    """在贴图顶部加一条底座颜色的色带，返回新贴图和色带中心的 V 坐标；角色的 UV 按比例压到下面。"""
    w, h = image.size
    strip = max(8, h // 32)
    total = h + strip
    src = list(image.pixels)
    out = bpy.data.images.new("Tex", w, total, alpha=False)
    rgb = bm_lib.hex_rgb(dict(bm_lib.PALETTE)[BASE_COLOUR])
    # 图片像素按 linear 存储，色板是 sRGB
    rgb = [bm_lib.srgb_to_linear(c) for c in rgb] if image.colorspace_settings.name != "sRGB" else list(rgb)
    pixels = src[:w * h * 4] + [rgb[0], rgb[1], rgb[2], 1.0] * (w * strip)
    out.pixels = pixels
    scale = h / total
    for loop_uv in uv_layer_data:
        u, v = loop_uv.uv
        loop_uv.uv = (u, (v % 1.0 if v > 1.0 or v < 0.0 else v) * scale)
    return out, (h + strip / 2) / total


def build_base(radius, uv_v):
    b = bm_lib.Builder()
    bm_lib.base(b, radius, sides=32, h=BASE_HEIGHT)
    for face in b.bm.faces:
        if face.material_index == 0:
            for loop in face.loops:
                loop[b.uv].uv = (0.5, uv_v)
    return b


def main():
    args = parse_args()
    bpy.ops.wm.read_factory_settings(use_empty=True)
    obj = import_source(os.path.abspath(args.source))
    image = find_base_colour_image(obj)
    image.pixels[0]  # 触发加载
    height = args.height or (1.6 if args.cells == 1 else 3.2)
    radius = BASE_RADIUS[args.cells]
    normalise(obj, args.turn, height, radius * 0.98)
    tris = decimate(obj, args.faces)

    # 只保留一套 UV，所有面归到 Hull 槽
    mesh = obj.data
    while len(mesh.uv_layers) > 1:
        mesh.uv_layers.remove(mesh.uv_layers[-1])
    if not mesh.uv_layers:
        raise SystemExit("模型没有 UV")
    tex, base_v = texture_with_base_strip(image, mesh.uv_layers[0].data)
    tex_path = os.path.join(args.out, args.unit_id + "_tex.png")
    tex.filepath_raw = tex_path
    tex.file_format = "PNG"
    tex.save()

    hull = bpy.data.materials.new("Hull")
    hull.use_nodes = True
    node = hull.node_tree.nodes.new("ShaderNodeTexImage")
    node.image = tex
    hull.node_tree.links.new(node.outputs["Color"], hull.node_tree.nodes["Principled BSDF"].inputs["Base Color"])
    glow = bpy.data.materials.new("Glow")
    glow.use_nodes = True
    mesh.materials.clear()
    mesh.materials.append(hull)
    mesh.materials.append(glow)
    for poly in mesh.polygons:
        poly.material_index = 0

    if not args.no_base:
        # 角色站在底座顶面上
        mesh.transform(Matrix.Translation((0, 0, BASE_HEIGHT)))
        b = build_base(radius, base_v)
        b.uv = None
        base_mesh = bpy.data.meshes.new("Base")
        b.bm.to_mesh(base_mesh)
        b.bm.free()
        base_mesh.materials.append(hull)
        base_mesh.materials.append(glow)
        base_obj = bpy.data.objects.new("Base", base_mesh)
        bpy.context.scene.collection.objects.link(base_obj)
        base_mesh.uv_layers[0].name = mesh.uv_layers[0].name
        bpy.ops.object.select_all(action="DESELECT")
        base_obj.select_set(True)
        obj.select_set(True)
        bpy.context.view_layer.objects.active = obj
        bpy.ops.object.join()
    obj.name = args.unit_id
    obj.data.name = args.unit_id

    os.makedirs(args.out, exist_ok=True)
    bm_lib.export_fbx(obj, os.path.join(args.out, args.unit_id + ".fbx"))
    print("已导出 %s（%d 个三角面，贴图 %s）" % (args.unit_id, sum(len(p.vertices) - 2 for p in obj.data.polygons),
                                         os.path.basename(tex_path)))

    if args.preview:
        bm_lib.set_preview_glow(glow, bm_lib.PLAYER_GLOW)
        bm_lib.render_sheet([obj], os.path.abspath(args.preview), spacing=2.2, turn=35, margin=2.8, px=900,
                            height_px=1000)
        print("已渲染预览", args.preview)


if __name__ == "__main__":
    main()
