"""
Starline 舰船模型生成脚本（Blender 5.x）。

按 docs/art-direction.md 的造型规格，用代码拼出第一幕全部单位的低多边形模型，
导出到 Unity 包的 Resources/StarlineModels/<单位 id>.fbx，并生成共用的色块贴图 Palette.png。

运行方式（任选一种）：
    blender --background --python tools/models/build_models.py
    blender --background --python tools/models/build_models.py -- --preview docs/art/models
    python3 tools/models/build_models.py --preview docs/art/models     # 装了 bpy 模块时

约定：
- 1 个 Blender 单位 = 1 个 Unity 单位，1 格 = 2 单位。
- 船首朝 Blender 的 -Y（导出后是 Unity 的 +Z），上方是 +Z，轴心在底面中心。
- 每个模型两个材质槽：Hull（色块贴图，UV 指向色块中心）和 Glow（发光部位，颜色由客户端按阵营和补给状态设置）。
- 修改造型后重新运行脚本即可覆盖导出；也可以在 Blender 里打开导出的 FBX 手工微调。
"""

import math
import os
import sys

import bpy  # 必须先于 bmesh 导入（以 Python 模块方式运行时）
import bmesh
from mathutils import Matrix, Vector

REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT_DIR = os.path.join(REPO, "Starline", "Packages", "com.starline.game", "Runtime", "Resources", "StarlineModels")

# ---- 色块贴图 ------------------------------------------------------------------
# 8 × 8 格，每格 32 像素。名字 → 颜色（sRGB 十六进制）。顺序决定在贴图里的位置，只能在末尾追加。
PALETTE = [
    ("hull_light", "C9D5E3"),
    ("hull_dark", "7F93AB"),
    ("hull_white", "E3EBF4"),
    ("hull_mid", "9FB1C6"),
    ("metal_dark", "33445A"),
    ("gold", "E8C15A"),
    ("carapace", "B35A2A"),
    ("carapace_dark", "7A3A1C"),
    ("flesh", "8E4A2A"),
    ("sinew", "5A2E18"),
    ("swarm_light", "FFB27A"),
    ("hidden", "C48CFF"),
    ("wreck_grey", "8A8F98"),
    ("wreck_dark", "6B717B"),
    ("wing", "D9A57E"),
]
PALETTE_CELLS = 8
PALETTE_CELL_PX = 32
GLOW = "glow"


def palette_uv(name):
    index = [n for n, _ in PALETTE].index(name)
    col, row = index % PALETTE_CELLS, index // PALETTE_CELLS
    # Unity 和 Blender 的 UV 原点都在左下角；贴图第 0 行画在最上面。
    u = (col + 0.5) / PALETTE_CELLS
    v = 1.0 - (row + 0.5) / PALETTE_CELLS
    return u, v


def srgb_to_linear(c):
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


def hex_rgb(h):
    return tuple(int(h[i:i + 2], 16) / 255.0 for i in (0, 2, 4))


def build_palette_image():
    size = PALETTE_CELLS * PALETTE_CELL_PX
    image = bpy.data.images.new("Palette", size, size, alpha=False)
    pixels = [0.0] * (size * size * 4)
    colours = dict(PALETTE)
    names = [n for n, _ in PALETTE]
    for y in range(size):
        row = (size - 1 - y) // PALETTE_CELL_PX  # Blender 像素从下往上
        for x in range(size):
            col = x // PALETTE_CELL_PX
            index = row * PALETTE_CELLS + col
            rgb = hex_rgb(colours[names[index]]) if index < len(names) else (1.0, 0.0, 1.0)
            o = (y * size + x) * 4
            pixels[o:o + 4] = [rgb[0], rgb[1], rgb[2], 1.0]
    image.pixels = pixels
    return image


# ---- 网格工具 ------------------------------------------------------------------

def P(f, x, z):
    """f = 向前的距离（船首方向），x = 右，z = 上。"""
    return Vector((x, -f, z))


class Builder:
    """往一个 bmesh 里堆零件；每个零件指定色块名或 GLOW。"""

    def __init__(self):
        self.bm = bmesh.new()
        self.uv = self.bm.loops.layers.uv.verify()

    def _paint(self, faces, colour):
        glow = colour == GLOW
        uv = (0.0, 0.0) if glow else palette_uv(colour)
        for face in faces:
            face.material_index = 1 if glow else 0
            for loop in face.loops:
                loop[self.uv].uv = uv

    def _new_faces(self, before):
        return [f for f in self.bm.faces if f not in before]

    # 截面放样：sections = [(f, 半宽, 底, 顶), ...]，截面是切角六边形（舰船）或八边形（蜂群）
    def loft(self, sections, colour, organic=False, x=0.0):
        rings = []
        for f, hw, zb, zt in sections:
            hw = max(hw, 0.02)
            if organic:
                zc, hz = (zb + zt) / 2, max((zt - zb) / 2, 0.02)
                pts = [(x + hw * math.cos(a), zc + hz * math.sin(a))
                       for a in (i * math.pi / 4 for i in range(8))]
            else:
                zm = zb + (zt - zb) * 0.45
                pts = [(x - hw * 0.55, zb), (x + hw * 0.55, zb), (x + hw, zm),
                       (x + hw * 0.6, zt), (x - hw * 0.6, zt), (x - hw, zm)]
            rings.append([self.bm.verts.new(P(f, px, pz)) for px, pz in pts])
        faces = []
        n = len(rings[0])
        for a, b in zip(rings, rings[1:]):
            for i in range(n):
                j = (i + 1) % n
                faces.append(self.bm.faces.new((a[i], a[j], b[j], b[i])))
        faces.append(self.bm.faces.new(rings[0]))
        faces.append(self.bm.faces.new(list(reversed(rings[-1]))))
        self._paint(faces, colour)
        return faces

    def box(self, centre, size, colour, rot=(0.0, 0.0, 0.0)):
        before = set(self.bm.faces)
        m = (Matrix.Translation(centre) @ _euler(rot) @ Matrix.Diagonal((size[0], size[1], size[2], 1.0)))
        bmesh.ops.create_cube(self.bm, size=1.0, matrix=m)
        self._paint(self._new_faces(before), colour)

    def ball(self, centre, radii, colour, segments=8, rings=6, rot=(0.0, 0.0, 0.0)):
        before = set(self.bm.faces)
        m = (Matrix.Translation(centre) @ _euler(rot) @ Matrix.Diagonal((radii[0], radii[1], radii[2], 1.0)))
        bmesh.ops.create_uvsphere(self.bm, u_segments=segments, v_segments=rings, radius=1.0, matrix=m)
        self._paint(self._new_faces(before), colour)

    def rod(self, a, b, r1, r2, colour, segments=6):
        """从 a 到 b 的圆柱或圆锥（r1 在 a 端，r2 在 b 端）。"""
        d = b - a
        before = set(self.bm.faces)
        rot = Vector((0, 0, 1)).rotation_difference(d.normalized()).to_matrix().to_4x4()
        m = Matrix.Translation((a + b) / 2) @ rot
        bmesh.ops.create_cone(self.bm, cap_ends=True, cap_tris=False, segments=segments,
                              radius1=max(r1, 0.005), radius2=max(r2, 0.005), depth=d.length, matrix=m)
        self._paint(self._new_faces(before), colour)

    def chain(self, points, r1, r2, colour, segments=6):
        """沿折线逐段变细的触须、钳子、腿。"""
        n = len(points) - 1
        for i in range(n):
            ra = r1 + (r2 - r1) * i / n
            rb = r1 + (r2 - r1) * (i + 1) / n
            self.rod(points[i], points[i + 1], ra, rb, colour, segments)

    def ring(self, centre, radius, thickness, colour, axis="z", segments=12, sides=4):
        before = set(self.bm.faces)
        loops = []
        for i in range(segments):
            a = 2 * math.pi * i / segments
            loop = []
            for k in range(sides):
                b = 2 * math.pi * k / sides
                rr = radius + thickness * math.cos(b)
                loop.append(Vector((rr * math.cos(a), rr * math.sin(a), thickness * math.sin(b))))
            loops.append(loop)
        if axis == "f":  # 光环面朝船首
            turn = Matrix.Rotation(math.radians(90), 4, "X")
        elif axis == "x":
            turn = Matrix.Rotation(math.radians(90), 4, "Y")
        else:
            turn = Matrix.Identity(4)
        m = Matrix.Translation(centre) @ turn
        verts = [[self.bm.verts.new(m @ p) for p in loop] for loop in loops]
        for i in range(segments):
            a, b = verts[i], verts[(i + 1) % segments]
            for k in range(sides):
                kk = (k + 1) % sides
                self.bm.faces.new((a[k], b[k], b[kk], a[kk]))
        self._paint(self._new_faces(before), colour)

    def finish(self, name, hull_mat, glow_mat):
        bmesh.ops.remove_doubles(self.bm, verts=self.bm.verts, dist=0.0001)
        bmesh.ops.recalc_face_normals(self.bm, faces=self.bm.faces)
        mesh = bpy.data.meshes.new(name)
        self.bm.to_mesh(mesh)
        self.bm.free()
        for poly in mesh.polygons:
            poly.use_smooth = False
        mesh.materials.append(hull_mat)
        mesh.materials.append(glow_mat)
        obj = bpy.data.objects.new(name, mesh)
        bpy.context.scene.collection.objects.link(obj)
        return obj


def _euler(rot):
    return (Matrix.Rotation(math.radians(rot[2]), 4, "Z") @ Matrix.Rotation(math.radians(rot[1]), 4, "Y")
            @ Matrix.Rotation(math.radians(rot[0]), 4, "X"))


def mirror(fn, *args):
    """左右对称地各做一次：fn(sign, ...)。"""
    for s in (-1, 1):
        fn(s, *args)


# ---- 晨星舰队 ------------------------------------------------------------------
# 白灰装甲 + 蓝色发光条，直线切面，楔形船首。

def battleship(b):
    b.loft([(-1.8, 0.45, 0.55, 1.0), (-1.4, 0.75, 0.45, 1.2), (0.6, 0.8, 0.45, 1.2),
            (1.4, 0.55, 0.5, 1.1), (1.85, 0.1, 0.62, 0.92)], "hull_light")
    b.box(P(-0.1, 0, 0.42), (1.0, 2.8, 0.16), "hull_dark")
    b.box(P(-0.75, 0, 1.45), (0.62, 0.75, 0.5), "hull_white")
    b.box(P(-0.85, 0, 1.82), (0.4, 0.45, 0.28), "hull_mid")
    b.box(P(-0.6, 0, 1.78), (0.3, 0.06, 0.06), GLOW)
    for f in (0.3, 0.95):
        b.rod(P(f, 0, 1.18), P(f, 0, 1.36), 0.3, 0.26, "hull_white", 8)
        for x in (-0.08, 0.08):
            b.rod(P(f + 0.15, x, 1.29), P(f + 0.8, x, 1.29), 0.05, 0.04, "hull_mid")
    mirror(lambda s: b.box(P(-0.2, s * 0.8, 0.86), (0.04, 2.5, 0.06), GLOW))
    mirror(lambda s: b.box(P(-1.82, s * 0.35, 0.8), (0.32, 0.08, 0.3), GLOW))


def frigate(b):
    b.loft([(-0.8, 0.22, 0.55, 0.85), (-0.5, 0.3, 0.5, 0.9), (0.3, 0.26, 0.5, 0.85), (0.85, 0.03, 0.6, 0.7)],
           "hull_light")
    b.box(P(-0.1, 0, 0.47), (0.28, 0.9, 0.08), "hull_dark")
    b.loft([(-0.45, 0.05, 0.85, 1.12), (-0.1, 0.06, 0.85, 1.0), (0.1, 0.03, 0.85, 0.88)], "hull_white")
    mirror(lambda s: b.box(P(0.0, s * 0.27, 0.7), (0.03, 0.9, 0.04), GLOW))
    b.box(P(-0.82, 0, 0.7), (0.26, 0.06, 0.18), GLOW)


def missile_ship(b):
    b.loft([(-0.85, 0.28, 0.5, 0.8), (0.4, 0.32, 0.5, 0.85), (0.85, 0.08, 0.56, 0.72)], "hull_light")
    b.box(P(-0.1, 0, 0.47), (0.36, 1.2, 0.08), "hull_dark")
    b.box(P(-0.05, 0, 0.98), (0.56, 1.05, 0.28), "hull_white")
    for f in (-0.4, -0.15, 0.1, 0.35):
        for x in (-0.13, 0.13):
            b.box(P(f, x, 1.12), (0.14, 0.14, 0.03), GLOW)  # 发射口：亮着的代表还有弹药
    b.box(P(-0.87, 0, 0.66), (0.3, 0.06, 0.18), GLOW)


def pd_ship(b):
    b.loft([(-0.75, 0.38, 0.5, 0.75), (0.5, 0.42, 0.5, 0.78), (0.8, 0.15, 0.55, 0.7)], "hull_light")
    b.box(P(-0.1, 0, 0.47), (0.5, 1.1, 0.08), "hull_dark")
    for f, x in ((0.35, 0.0), (-0.2, -0.2), (-0.2, 0.2)):
        b.ball(P(f, x, 0.8), (0.17, 0.17, 0.15), "hull_white")
        b.rod(P(f + 0.05, x, 0.88), P(f + 0.28, x, 1.12), 0.03, 0.025, "hull_mid", 5)
        b.box(P(f, x, 0.95), (0.05, 0.05, 0.03), GLOW)
    b.box(P(-0.77, 0, 0.64), (0.4, 0.06, 0.12), GLOW)


def strike_craft(b):
    for f, x, z in ((0.35, 0.0, 0.05), (-0.15, -0.45, 0.0), (-0.15, 0.45, -0.05)):
        b.loft([(f - 0.25, 0.04, z - 0.03, z + 0.04), (f - 0.15, 0.24, z - 0.02, z + 0.03),
                (f + 0.05, 0.12, z - 0.02, z + 0.04), (f + 0.32, 0.02, z - 0.01, z + 0.02)], "hull_white", x=x)
        b.box(P(f - 0.02, x, z + 0.05), (0.06, 0.14, 0.04), "metal_dark")
        b.box(P(f - 0.3, x, z), (0.06, 0.12, 0.03), GLOW)


def supply_ship(b):
    b.loft([(-1.8, 0.7, 0.45, 1.15), (-1.3, 0.9, 0.4, 1.25), (1.2, 0.9, 0.4, 1.25), (1.7, 0.6, 0.5, 1.05)],
           "hull_light")
    b.box(P(0.0, 0, 0.37), (1.3, 3.0, 0.14), "hull_dark")
    for f in (-0.85, -0.1, 0.65):
        b.box(P(f, 0, 1.43), (1.2, 0.6, 0.36), "hull_mid")
    b.box(P(1.2, 0, 1.38), (0.5, 0.35, 0.3), "hull_white")
    b.ring(P(1.82, 0, 0.8), 0.42, 0.07, GLOW, axis="f")  # 补给口
    mirror(lambda s: b.box(P(-1.82, s * 0.4, 0.8), (0.34, 0.08, 0.4), GLOW))


def relay(b):
    b.rod(P(0, 0, 0.0), P(0, 0, 0.22), 0.36, 0.22, "hull_dark", 8)
    b.rod(P(0, 0, 0.22), P(0, 0, 1.75), 0.07, 0.05, "hull_light", 6)
    b.ring(P(0, 0, 1.25), 0.55, 0.05, GLOW, segments=16)
    mirror(lambda s: b.box(P(0, s * 0.55, 1.25), (0.08, 0.28, 0.22), "hull_light"))
    b.ball(P(0, 0, 1.95), (0.2, 0.2, 0.2), GLOW, segments=6, rings=4)


# ---- 蜂群 ----------------------------------------------------------------------
# 深橙甲壳 + 亮橙脉络，弧线、分节、不对称。

def hive(b):
    b.ball(P(0, 0, 0.1), (1.65, 1.6, 1.15), "carapace", segments=10, rings=7)
    b.ball(P(-0.9, 0.5, 0.2), (0.8, 0.8, 0.85), "carapace_dark", segments=8, rings=6)
    for f, x, z, r in ((0.9, -0.5, 0.75, 0.26), (0.6, 0.6, 0.95, 0.22), (1.25, 0.2, 0.5, 0.2), (0.1, -0.9, 0.9, 0.2)):
        b.ball(P(f, x, z), (r, r, r * 0.8), GLOW, segments=6, rings=4)
    for a in (0.0, 1.3, 2.6, 3.9, 5.2):
        b.ball(P(1.3 * math.cos(a), 1.3 * math.sin(a), 0.35), (0.3, 0.45, 0.35), "carapace_dark",
               segments=6, rings=4, rot=(0, 0, math.degrees(a)))


def hive_node(b):
    b.ball(P(0, 0, 1.0), (0.34, 0.34, 0.55), "carapace", segments=8, rings=6)
    b.ball(P(0.22, 0, 1.05), (0.13, 0.13, 0.24), GLOW, segments=6, rings=4)
    for a in (0.5, 2.6, 4.7):
        b.chain([P(0.15 * math.cos(a), 0.15 * math.sin(a), 0.6), P(0.4 * math.cos(a), 0.4 * math.sin(a), 0.25),
                 P(0.6 * math.cos(a), 0.6 * math.sin(a), 0.0)], 0.09, 0.03, "sinew")


def ripper(b):
    b.loft([(-0.75, 0.08, 0.5, 0.62), (-0.4, 0.22, 0.52, 0.86), (0.1, 0.27, 0.55, 0.98),
            (0.5, 0.2, 0.55, 0.88), (0.75, 0.07, 0.6, 0.72)], "carapace", organic=True)
    for f in (-0.35, -0.05, 0.25):
        b.ball(P(f, 0, 0.9 + 0.05 * (f > -0.2)), (0.07, 0.12, 0.05), GLOW, segments=6, rings=4)
    mirror(lambda s: b.chain([P(0.45, s * 0.18, 0.68), P(0.85, s * 0.32, 0.78), P(1.1, s * 0.14, 0.6)],
                             0.08, 0.02, "swarm_light"))
    mirror(lambda s: b.chain([P(-0.1, s * 0.22, 0.6), P(-0.2, s * 0.45, 0.4), P(-0.3, s * 0.5, 0.1)],
                             0.05, 0.02, "sinew", 5))


def crawler(b):
    b.ball(P(0.0, 0, 0.55), (0.48, 0.68, 0.36), GLOW, segments=8, rings=5)  # 甲片缝隙里的光
    for f, sy, sz in ((-0.45, 0.32, 0.4), (0.0, 0.34, 0.47), (0.42, 0.28, 0.4)):
        b.ball(P(f, 0, 0.6), (0.55, sy, sz), "carapace", segments=8, rings=5)
    b.ball(P(0.72, 0, 0.45), (0.2, 0.18, 0.16), "swarm_light", segments=6, rings=4)
    for f in (-0.4, 0.0, 0.4):
        mirror(lambda s, f=f: b.chain([P(f, s * 0.4, 0.4), P(f + 0.05, s * 0.68, 0.25), P(f + 0.1, s * 0.75, 0.0)],
                                      0.06, 0.03, "sinew", 5))


def locust(b):
    for f, x, z in ((0.45, 0.0, 0.12), (0.0, -0.5, 0.0), (0.05, 0.52, -0.1), (-0.45, -0.2, 0.1), (-0.4, 0.38, -0.03)):
        b.ball(P(f, x, z), (0.1, 0.22, 0.08), "carapace", segments=6, rings=4)
        b.ball(P(f - 0.2, x, z), (0.06, 0.06, 0.06), GLOW, segments=6, rings=4)
        mirror(lambda s, f=f, x=x, z=z: b.box(P(f + 0.03, x + s * 0.2, z + 0.08), (0.3, 0.14, 0.01), "wing",
                                              rot=(s * 18, 0, 0)))


def acid_turret(b):
    for a in (0.3, 1.9, 3.4, 5.0):
        b.chain([P(0.3 * math.cos(a), 0.3 * math.sin(a), 0.25), P(0.55 * math.cos(a), 0.55 * math.sin(a), 0.0)],
                0.08, 0.03, "sinew", 5)
    b.ball(P(0, 0, 0.42), (0.5, 0.5, 0.36), "carapace", segments=10, rings=6)
    b.ring(P(0, 0, 0.45), 0.47, 0.035, GLOW, segments=10)
    b.chain([P(0.05, 0, 0.65), P(0.4, 0, 1.05), P(0.75, 0, 1.45)], 0.16, 0.1, "carapace_dark", 7)
    b.ball(P(0.78, 0, 1.5), (0.12, 0.12, 0.12), GLOW, segments=6, rings=4)


def brood_mother(b):
    b.ball(P(0, 0, 1.0), (1.15, 1.6, 0.75), "flesh", segments=10, rings=7)
    b.ball(P(1.35, 0, 0.95), (0.42, 0.38, 0.36), "carapace", segments=8, rings=5)
    for f, x, z in ((-0.6, -0.5, 1.5), (0.1, 0.55, 1.6), (0.5, -0.35, 1.55), (-0.2, 0.0, 1.72), (-1.0, 0.4, 1.3)):
        b.ball(P(f, x, z), (0.17, 0.17, 0.12), GLOW, segments=6, rings=4)  # 隐藏特性：紫色光斑
    mirror(lambda s: b.chain([P(1.5, s * 0.2, 0.9), P(1.9, s * 0.35, 0.95), P(2.05, s * 0.12, 0.7)],
                             0.1, 0.03, "swarm_light"))
    for f in (-0.8, 0.0, 0.8):
        mirror(lambda s, f=f: b.chain([P(f, s * 0.9, 0.7), P(f + 0.1, s * 1.35, 0.45), P(f + 0.2, s * 1.45, 0.0)],
                                      0.1, 0.04, "sinew", 5))


def wreck_fortress(b):
    b.box(P(-0.3, -0.3, 0.55), (1.6, 2.4, 0.8), "wreck_grey", rot=(0, 8, 12))
    b.box(P(0.6, 0.5, 0.9), (1.0, 1.4, 1.1), "wreck_dark", rot=(-10, 0, -20))
    b.rod(P(-0.9, 0.6, 0.3), P(-0.5, 0.9, 2.4), 0.09, 0.06, "wreck_grey", 6)
    b.ring(P(-0.55, 0.85, 2.0), 0.5, 0.05, "wreck_dark", segments=12, axis="x")
    for f, x, z, r in ((0.1, 0.1, 1.2, 0.55), (-0.8, -0.2, 1.05, 0.45), (0.9, -0.5, 0.6, 0.4)):
        b.ball(P(f, x, z), (r, r * 1.1, r * 0.7), "carapace", segments=8, rings=5)
    for f, x, z in ((0.3, -0.3, 1.45), (-0.6, -0.5, 1.3), (1.0, -0.2, 0.9)):
        b.ball(P(f, x, z), (0.13, 0.13, 0.1), GLOW, segments=6, rings=4)
    b.rod(P(0.0, 0.0, 1.55), P(1.9, 0.0, 2.05), 0.2, 0.14, "wreck_dark", 8)  # 重炮
    b.rod(P(-0.2, 0.0, 1.45), P(0.2, 0.0, 1.65), 0.38, 0.32, "carapace_dark", 8)
    b.ball(P(1.95, 0.0, 2.07), (0.14, 0.14, 0.14), GLOW, segments=6, rings=4)


UNITS = [
    ("supply_ship", supply_ship), ("relay", relay), ("battleship", battleship), ("frigate", frigate),
    ("missile_ship", missile_ship), ("pd_ship", pd_ship), ("strike_craft", strike_craft),
    ("hive", hive), ("hive_node", hive_node), ("ripper", ripper), ("crawler", crawler),
    ("locust", locust), ("acid_turret", acid_turret), ("brood_mother", brood_mother),
    ("wreck_fortress", wreck_fortress),
]

# 预览图里发光部位的颜色（游戏里由客户端设置，这里只为看效果）
PREVIEW_GLOW = {"supply_ship": "6FD0B4", "relay": "6FD0B4", "brood_mother": "C48CFF"}


# ---- 材质、导出、预览 -------------------------------------------------------------

def make_materials(palette):
    hull = bpy.data.materials.new("Hull")
    hull.use_nodes = True
    nodes = hull.node_tree.nodes
    bsdf = nodes.get("Principled BSDF")
    tex = nodes.new("ShaderNodeTexImage")
    tex.image = palette
    tex.interpolation = "Closest"
    hull.node_tree.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = 0.6
    glow = bpy.data.materials.new("Glow")
    glow.use_nodes = True
    return hull, glow


def set_preview_glow(glow_mat, hex_colour):
    bsdf = glow_mat.node_tree.nodes.get("Principled BSDF")
    rgb = [srgb_to_linear(c) for c in hex_rgb(hex_colour)]
    bsdf.inputs["Base Color"].default_value = (*rgb, 1.0)
    bsdf.inputs["Emission Color"].default_value = (*rgb, 1.0)
    bsdf.inputs["Emission Strength"].default_value = 4.0


def export_fbx(obj, path):
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=True, object_types={"MESH"}, apply_unit_scale=True,
        apply_scale_options="FBX_SCALE_ALL", bake_space_transform=True, axis_forward="-Z", axis_up="Y",
        mesh_smooth_type="FACE", use_mesh_modifiers=True, add_leaf_bones=False, bake_anim=False,
        path_mode="STRIP", embed_textures=False)


def render_sheet(objects, path):
    """把一组模型排成一排，从战斗镜头的角度（侧前方斜看）渲染一张预览图。"""
    scene = bpy.context.scene
    spacing = 4.6
    for i, obj in enumerate(objects):
        obj.location = Vector((i * spacing, 0, 0))
        obj.rotation_euler = (0, 0, math.radians(90))  # 船首朝向画面右侧
    width = (len(objects) - 1) * spacing
    cam_data = bpy.data.cameras.new("PreviewCam")
    cam_data.type = "ORTHO"
    cam_data.ortho_scale = width + 4.5
    cam = bpy.data.objects.new("PreviewCam", cam_data)
    scene.collection.objects.link(cam)
    cam.location = Vector((width / 2, -11, 12))
    cam.rotation_euler = (math.radians(42), 0, 0)  # 和战斗镜头一样从侧前方斜看
    scene.camera = cam
    sun = bpy.data.objects.new("Sun", bpy.data.lights.new("Sun", "SUN"))
    sun.data.energy = 3.5
    sun.rotation_euler = (math.radians(50), math.radians(10), math.radians(-35))
    scene.collection.objects.link(sun)
    scene.world = bpy.data.worlds.new("World")
    scene.world.use_nodes = True
    scene.world.node_tree.nodes["Background"].inputs["Color"].default_value = (0.004, 0.006, 0.012, 1)
    scene.world.node_tree.nodes["Background"].inputs["Strength"].default_value = 1.0
    scene.render.engine = "CYCLES"
    scene.cycles.samples = 24
    scene.cycles.device = "CPU"
    scene.render.resolution_x = int(320 * len(objects))
    scene.render.resolution_y = 380
    scene.render.filepath = path
    scene.view_settings.view_transform = "Standard"
    bpy.ops.render.render(write_still=True)
    bpy.data.objects.remove(cam)
    bpy.data.objects.remove(sun)


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else sys.argv[1:]
    preview_dir = None
    if "--preview" in argv:
        preview_dir = os.path.abspath(argv[argv.index("--preview") + 1])

    bpy.ops.wm.read_factory_settings(use_empty=True)
    os.makedirs(OUT_DIR, exist_ok=True)
    palette = build_palette_image()
    palette.filepath_raw = os.path.join(OUT_DIR, "Palette.png")
    palette.file_format = "PNG"
    palette.save()

    built = {}
    for unit_id, fn in UNITS:
        hull, glow = make_materials(palette)
        b = Builder()
        fn(b)
        obj = b.finish(unit_id, hull, glow)
        export_fbx(obj, os.path.join(OUT_DIR, unit_id + ".fbx"))
        built[unit_id] = (obj, glow)
        print("已导出 %s（%d 个三角面）" % (unit_id, sum(len(p.vertices) - 2 for p in obj.data.polygons)))

    if preview_dir:
        os.makedirs(preview_dir, exist_ok=True)
        ids = [u for u, _ in UNITS]
        for unit_id, (obj, glow) in built.items():
            side_glow = "5AB8FF" if ids.index(unit_id) < 7 else "FF8F4A"
            set_preview_glow(glow, PREVIEW_GLOW.get(unit_id, side_glow))
        for name, group in (("morningstar", ids[:7]), ("swarm", ids[7:])):
            for obj, _ in built.values():
                obj.hide_render = True
            objects = [built[u][0] for u in group]
            for obj in objects:
                obj.hide_render = False
            render_sheet(objects, os.path.join(preview_dir, name + ".png"))
            print("已渲染预览", name)


if __name__ == "__main__":
    main()
