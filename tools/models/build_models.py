"""
Starline 部队模型生成脚本（Blender 5.x）。

按 docs/art-direction.md 的造型规格，用代码拼出第一幕全部单位的低多边形模型，
导出到 Unity 包的 Resources/StarlineModels/<单位 id>.fbx，并生成共用的色块贴图 Palette.png。

运行方式（任选一种）：
    blender --background --python tools/models/build_models.py
    blender --background --python tools/models/build_models.py -- --preview docs/art/models
    python3 tools/models/build_models.py --preview docs/art/models     # 装了 bpy 模块时

约定：
- 1 个 Blender 单位 = 1 个 Unity 单位，1 格 = 2 单位。
- 正面朝 Blender 的 -Y（导出后是 Unity 的 +Z），上方是 +Z，轴心在底面中心。
- 每个模型两个材质槽：Hull（色块贴图，UV 指向色块中心）和 Glow（发光部位，颜色由客户端按阵营和魔网接入状态设置）。
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
    ("steel", "C9D5E3"),
    ("steel_dark", "7F93AB"),
    ("white", "E3EBF4"),
    ("cloth_blue", "3E78B8"),
    ("cloth_navy", "2A4870"),
    ("gold", "E8C15A"),
    ("leather", "6B4A32"),
    ("wood", "8A6440"),
    ("skin", "E0B894"),
    ("stone", "8C8F96"),
    ("stone_dark", "5C6068"),
    ("iron", "3A3D42"),
    ("griffin", "C9A46A"),
    ("feather", "EDE6D6"),
    ("bone", "D8D2BC"),
    ("bone_dark", "A39C85"),
    ("rot_flesh", "7E8F6A"),
    ("rot_dark", "4E5A42"),
    ("ghost", "B8C9B0"),
    ("crystal_dead", "5E6E7A"),
    ("robe_black", "2B2A33"),
    ("bark", "5E4630"),
    ("leaf", "4F7A4A"),
    ("elf_purple", "6A4C9C"),
    ("elf_dark", "3A2D5C"),
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

    # 截面放样：sections = [(f, 半宽, 底, 顶), ...]，截面是切角六边形或八边形（organic）
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

    def rbox(self, centre, size, colour, roundness=0.3, rot=(0.0, 0.0, 0.0), segments=2):
        """圆角方块：roundness 是圆角半径占最短边一半的比例。"""
        before_v, before_f = set(self.bm.verts), set(self.bm.faces)
        bmesh.ops.create_cube(self.bm, size=1.0, matrix=Matrix.Diagonal((size[0], size[1], size[2], 1.0)))
        verts = [v for v in self.bm.verts if v not in before_v]
        edges = list({e for v in verts for e in v.link_edges})
        bmesh.ops.bevel(self.bm, geom=verts + edges, offset=min(size) * 0.5 * min(roundness, 0.95),
                        segments=segments, profile=0.5, affect="EDGES", clamp_overlap=True)
        m = Matrix.Translation(centre) @ _euler(rot)
        for v in self.bm.verts:
            if v not in before_v:
                v.co = m @ v.co
        self._paint(self._new_faces(before_f), colour)

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

    def finish(self, name, hull_mat, glow_mat, smooth=False):
        bmesh.ops.remove_doubles(self.bm, verts=self.bm.verts, dist=0.0001)
        bmesh.ops.recalc_face_normals(self.bm, faces=self.bm.faces)
        mesh = bpy.data.meshes.new(name)
        self.bm.to_mesh(mesh)
        self.bm.free()
        if smooth:  # 卡通角色：平滑着色，折角大的边保持硬边
            mesh.shade_smooth()
            mesh.set_sharp_from_angle(angle=math.radians(50))
        else:
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


# ---- 通用零件 ------------------------------------------------------------------

def base(b, r, colour="stone_dark", sides=8, h=0.08):
    """棋子底座 + 魔网指示环（发光环随接入状态变色）。"""
    b.rod(P(0, 0, 0), P(0, 0, h), r, r * 0.96, colour, sides)
    b.ring(P(0, 0, h * 0.5), r, 0.03, GLOW, segments=max(12, sides * 2))


def scaled(b, k, fn, pivot=(0.0, 0.0, 0.08)):
    """调用 fn 画出部件，再把新画的部分以底座顶面中心为基点放大 k 倍（单个角色要比原来小队里的小人大，才看得清）。"""
    before = set(b.bm.verts)
    fn()
    c = Vector(pivot)
    for v in b.bm.verts:
        if v not in before:
            v.co = c + (v.co - c) * k


def gem(b, centre, radii, colour=GLOW, rot=(0.0, 0.0, 0.0)):
    """双锥形水晶。"""
    b.ball(centre, radii, colour, segments=4, rings=2, rot=rot)


def figure(b, f, x, h=0.72, cloth="cloth_blue", armor="steel", head="skin", hat=None, robe=False, z0=0.08):
    """低多边形人形，面朝前方。返回右手和左手的位置，用来挂武器。"""
    s = h / 0.75

    def z(v):
        return z0 + v * s

    if robe:
        b.rod(P(f, x, z(0)), P(f, x, z(0.5)), 0.17 * s, 0.09 * s, cloth, 8)
    else:
        for side in (-1, 1):
            b.rod(P(f, x + side * 0.055 * s, z(0)), P(f, x + side * 0.055 * s, z(0.32)), 0.045 * s, 0.05 * s,
                  "leather", 5)
    b.box(P(f, x, z(0.45)), (0.26 * s, 0.16 * s, 0.28 * s), armor)
    b.ball(P(f, x, z(0.66)), (0.085 * s, 0.085 * s, 0.085 * s), head, 6, 4)
    if hat == "helm":
        b.ball(P(f, x, z(0.69)), (0.095 * s, 0.1 * s, 0.08 * s), armor, 6, 4)
    elif hat == "hood":
        b.rod(P(f - 0.02 * s, x, z(0.6)), P(f - 0.04 * s, x, z(0.84)), 0.11 * s, 0.02 * s, cloth, 6)
    elif hat == "wizard":
        b.rod(P(f, x, z(0.7)), P(f, x, z(0.73)), 0.16 * s, 0.16 * s, cloth, 8)
        b.rod(P(f, x, z(0.73)), P(f - 0.06 * s, x, z(1.02)), 0.12 * s, 0.01 * s, cloth, 6)
    right = P(f + 0.12 * s, x + 0.17 * s, z(0.42))
    left = P(f + 0.1 * s, x - 0.17 * s, z(0.42))
    b.rod(P(f, x + 0.14 * s, z(0.56)), right, 0.035 * s, 0.03 * s, cloth, 5)
    b.rod(P(f, x - 0.14 * s, z(0.56)), left, 0.035 * s, 0.03 * s, cloth, 5)
    return right, left


# ---- 晨星远征军 ----------------------------------------------------------------
# 蓝白配色的骑士、游侠和法师，站在棋子底座上；发光部位是魔法（法杖水晶、附魔武器）。

def _mounted_knight(b, f, x):
    b.ball(P(f, x, 0.85), (0.24, 0.6, 0.26), "feather")  # 白色战马
    b.box(P(f - 0.05, x, 0.9), (0.52, 0.5, 0.3), "cloth_blue")  # 马衣
    for df in (0.38, -0.38):
        for dx in (-0.12, 0.12):
            b.rod(P(f + df, x + dx, 0.08), P(f + df, x + dx, 0.72), 0.05, 0.06, "steel_dark", 5)
    b.rod(P(f + 0.45, x, 0.95), P(f + 0.65, x, 1.3), 0.11, 0.09, "steel", 6)
    b.box(P(f + 0.75, x, 1.3), (0.15, 0.32, 0.15), "steel")
    b.rod(P(f - 0.55, x, 0.95), P(f - 0.75, x, 0.55), 0.05, 0.02, "leather", 4)
    b.box(P(f - 0.05, x, 1.32), (0.3, 0.22, 0.36), "steel")
    b.ball(P(f - 0.05, x, 1.6), (0.11, 0.12, 0.12), "steel", 6, 4)
    b.rod(P(f - 0.05, x, 1.68), P(f - 0.18, x, 1.88), 0.035, 0.01, "cloth_blue", 4)
    b.box(P(f, x - 0.2, 1.3), (0.04, 0.3, 0.38), "white")
    gem(b, P(f, x - 0.225, 1.33), (0.02, 0.06, 0.08))
    b.rod(P(f - 0.4, x + 0.2, 1.15), P(f + 1.15, x + 0.2, 1.45), 0.045, 0.012, "wood", 5)


def battleship(b):  # 重甲骑士：一名骑白马、披蓝马衣的骑士
    base(b, 1.65, sides=10)
    scaled(b, 1.25, lambda: _mounted_knight(b, 0.0, 0.0))


def _ranger(b):
    right, left = figure(b, 0.0, 0.0, cloth="cloth_navy", armor="leather", hat="hood")
    b.chain([left + Vector((0, 0, -0.25)), left + Vector((0, -0.06, 0)), left + Vector((0, 0, 0.3))],
            0.022, 0.018, "wood", 4)
    b.box(P(-0.1, 0.05, 0.6), (0.06, 0.08, 0.26), "leather")
    b.ball(right + Vector((0, -0.05, 0)), (0.03, 0.03, 0.03), GLOW, 4, 2)


def frigate(b):  # 游侠
    base(b, 0.75)
    scaled(b, 1.7, lambda: _ranger(b))


def _mage(b):
    right, _ = figure(b, 0.0, 0.0, h=0.78, cloth="cloth_blue", armor="white", hat="wizard", robe=True)
    b.rod(Vector((right.x, right.y, 0.08)), Vector((right.x, right.y, 1.05)), 0.022, 0.02, "wood", 5)
    gem(b, Vector((right.x, right.y, 1.13)), (0.06, 0.06, 0.11))


def missile_ship(b):  # 奥术法师：法杖水晶和环绕的法力球表示剩余法力
    base(b, 0.75)
    scaled(b, 1.6, lambda: _mage(b))
    for a in (0.4, 2.5, 4.6):
        b.ball(P(0.55 * math.cos(a), 0.55 * math.sin(a), 1.3), (0.06, 0.06, 0.06), GLOW, 6, 4)


def _crossbowman(b):
    right, _ = figure(b, 0.0, 0.0, armor="steel", hat="helm")
    tip = right + Vector((0, -0.25, 0.32))
    b.rod(right, tip, 0.025, 0.02, "wood", 4)
    b.rod(tip + Vector((-0.12, 0.04, -0.04)), tip + Vector((0.12, 0.04, -0.04)), 0.015, 0.015, "steel_dark", 4)
    b.ball(tip, (0.025, 0.025, 0.025), GLOW, 4, 2)
    b.box(P(0.22, -0.08, 0.3), (0.22, 0.04, 0.42), "cloth_blue")


def pd_ship(b):  # 弩手：弩斜指天空，专打狮鹫
    base(b, 0.75)
    scaled(b, 1.7, lambda: _crossbowman(b))


def _griffin(b, f, x, z):
    b.ball(P(f, x, z), (0.17, 0.4, 0.17), "griffin")
    b.ball(P(f + 0.42, x, z + 0.12), (0.12, 0.12, 0.12), "feather", 6, 4)
    b.rod(P(f + 0.5, x, z + 0.12), P(f + 0.66, x, z + 0.07), 0.045, 0.005, "gold", 4)
    mirror(lambda s: b.box(P(f + 0.05, x + s * 0.4, z + 0.12), (0.62, 0.3, 0.025), "feather", rot=(0, s * 25, 0)))
    b.rod(P(f - 0.35, x, z), P(f - 0.68, x, z + 0.1), 0.045, 0.01, "griffin", 4)
    b.box(P(f, x, z + 0.24), (0.14, 0.14, 0.2), "cloth_blue")
    b.ball(P(f, x, z + 0.4), (0.07, 0.07, 0.07), "steel", 6, 4)
    b.rod(P(f - 0.1, x + 0.1, z + 0.28), P(f + 0.55, x + 0.1, z + 0.36), 0.02, 0.01, "wood", 4)
    b.ball(P(f + 0.56, x + 0.1, z + 0.36), (0.03, 0.03, 0.03), GLOW, 4, 2)


def strike_craft(b):  # 狮鹫骑士（悬空，高度由客户端另加）
    scaled(b, 1.35, lambda: _griffin(b, 0.0, 0.0, 0.0), pivot=(0.0, 0.0, 0.0))


def supply_ship(b):  # 源晶塔：魔网源头，正面的光环是源口
    b.rod(P(0, 0, 0), P(0, 0, 0.25), 1.5, 1.4, "stone_dark", 8)
    b.ring(P(0, 0, 0.12), 1.5, 0.035, GLOW, segments=16)
    b.rod(P(0, 0, 0.25), P(0, 0, 2.1), 0.75, 0.5, "stone", 8)
    for z, r in ((0.9, 0.7), (1.6, 0.6)):
        b.rod(P(0, 0, z), P(0, 0, z + 0.08), r, r, "stone_dark", 8)
    b.rod(P(0, 0, 2.1), P(0, 0, 2.3), 0.68, 0.68, "stone_dark", 8)
    for i in range(8):
        a = i * math.pi / 4
        b.box(P(0.6 * math.cos(a), 0.6 * math.sin(a), 2.38), (0.16, 0.16, 0.16), "stone_dark")
    gem(b, P(0, 0, 3.0), (0.32, 0.32, 0.62))
    b.ring(P(0, 0, 3.0), 0.6, 0.03, GLOW, segments=16)
    b.box(P(0.68, 0, 0.6), (0.34, 0.12, 0.6), "iron")
    b.ring(P(0.78, 0, 0.75), 0.27, 0.035, GLOW, axis="f")
    mirror(lambda s: b.box(P(0, s * 0.62, 1.3), (0.1, 0.1, 0.22), GLOW))


def relay(b):  # 法力水晶
    b.rod(P(0, 0, 0), P(0, 0, 0.35), 0.32, 0.22, "stone_dark", 6)
    b.rod(P(0, 0, 0.35), P(0, 0, 0.45), 0.26, 0.26, "stone", 6)
    gem(b, P(0, 0, 1.25), (0.22, 0.22, 0.5))
    for a in (0.3, 2.4, 4.5):
        gem(b, P(0.45 * math.cos(a), 0.45 * math.sin(a), 1.0), (0.06, 0.06, 0.13))


# ---- 亡灵 ----------------------------------------------------------------------
# 灰绿色的骸骨与腐肉，幽绿光；比人类部队更佝偻、更不对称。

def hive(b):  # 亡灵祭坛
    b.rod(P(0, 0, 0), P(0, 0, 0.3), 1.6, 1.55, "stone_dark", 8)
    b.ring(P(0, 0, 0.15), 1.6, 0.035, GLOW, segments=16)
    b.rod(P(0, 0, 0.3), P(0, 0, 0.6), 1.1, 1.05, "stone", 8)
    for i in range(4):
        a = math.pi / 4 + i * math.pi / 2
        c = (1.25 * math.cos(a), 1.25 * math.sin(a))
        b.rod(P(c[0], c[1], 0.3), P(c[0], c[1], 1.5), 0.12, 0.09, "bone", 6)
        b.ball(P(c[0], c[1], 1.62), (0.13, 0.14, 0.13), "bone", 6, 4)
    b.rod(P(0, 0, 0.6), P(0, 0, 0.95), 0.3, 0.45, "iron", 8)
    b.ball(P(0, 0, 1.05), (0.28, 0.28, 0.22), GLOW, 6, 4)
    b.rod(P(0, 0, 1.0), P(0, 0, 1.75), 0.3, 0.02, GLOW, 6)
    for f, x in ((0.85, -0.3), (0.9, 0.15), (0.75, 0.45)):
        b.ball(P(f, x, 0.72), (0.12, 0.13, 0.12), "bone", 6, 4)


def hive_node(b):  # 骨碑
    b.rod(P(0, 0, 0), P(0, 0, 0.15), 0.4, 0.36, "stone_dark", 6)
    b.ring(P(0, 0, 0.08), 0.4, 0.025, GLOW, segments=12)
    b.rod(P(0, 0, 0.15), P(0, 0, 1.7), 0.2, 0.1, "stone_dark", 4)
    b.ball(P(0, 0, 1.82), (0.14, 0.15, 0.14), "bone", 6, 4)
    mirror(lambda s: b.ball(P(0.12, s * 0.05, 1.84), (0.03, 0.03, 0.03), GLOW, 4, 2))
    b.box(P(0.12, 0, 1.0), (0.06, 0.03, 0.6), GLOW)


def _ghoul(b, f, x):
    for s in (-1, 1):
        b.chain([P(f - 0.05, x + s * 0.07, 0.38), P(f + 0.06, x + s * 0.08, 0.22), P(f - 0.04, x + s * 0.08, 0.08)],
                0.045, 0.035, "rot_flesh", 5)
    b.rod(P(f - 0.05, x, 0.38), P(f + 0.15, x, 0.6), 0.1, 0.12, "rot_flesh", 6)
    b.ball(P(f - 0.02, x, 0.58), (0.06, 0.08, 0.06), "bone", 6, 4)
    b.ball(P(f + 0.25, x, 0.62), (0.08, 0.1, 0.08), "bone", 6, 4)
    mirror(lambda s: b.ball(P(f + 0.33, x + s * 0.035, 0.64), (0.018, 0.018, 0.018), GLOW, 4, 2))
    for s in (-1, 1):
        hand = P(f + 0.38, x + s * 0.16, 0.14)
        b.chain([P(f + 0.12, x + s * 0.12, 0.56), P(f + 0.26, x + s * 0.19, 0.38), hand], 0.035, 0.022,
                "rot_flesh", 5)
        b.rod(hand, hand + Vector((0, -0.12, -0.04)), 0.02, 0.004, "bone", 4)


def ripper(b):  # 食尸鬼
    base(b, 0.75)
    scaled(b, 1.9, lambda: _ghoul(b, -0.1, 0.0))


def crawler(b):  # 憎恶
    base(b, 0.8)
    mirror(lambda s: b.rod(P(0, s * 0.18, 0.08), P(0, s * 0.18, 0.45), 0.11, 0.12, "rot_flesh", 6))
    b.ball(P(0.02, 0, 0.66), (0.42, 0.38, 0.4), "rot_flesh")
    b.ball(P(-0.05, 0, 0.98), (0.36, 0.3, 0.28), "rot_dark")
    b.ball(P(0.15, 0, 1.18), (0.1, 0.11, 0.1), "bone", 6, 4)
    b.ball(P(0.38, 0.05, 0.62), (0.13, 0.05, 0.1), GLOW, 6, 4)
    for z in (0.5, 0.75):
        b.box(P(0.4, -0.05, z), (0.24, 0.02, 0.025), "iron")
    b.rod(P(0, 0.36, 0.98), P(0.2, 0.46, 0.5), 0.1, 0.08, "rot_flesh", 6)
    b.box(P(0.38, 0.46, 0.38), (0.04, 0.38, 0.32), "iron")
    b.rod(P(0, -0.36, 0.98), P(0.22, -0.46, 0.5), 0.09, 0.07, "rot_flesh", 6)
    b.chain([P(0.22, -0.46, 0.5), P(0.3, -0.5, 0.3), P(0.4, -0.46, 0.38)], 0.025, 0.015, "iron", 4)


def _wraith(b, f, x, z):
    b.rod(P(f, x, z + 0.2), P(f - 0.18, x, z - 0.38), 0.13, 0.01, "ghost", 6)
    b.ball(P(f + 0.01, x, z + 0.27), (0.11, 0.12, 0.12), "rot_dark", 6, 4)
    b.ball(P(f + 0.08, x, z + 0.26), (0.05, 0.03, 0.03), GLOW, 4, 2)
    mirror(lambda s: b.rod(P(f + 0.02, x + s * 0.1, z + 0.15), P(f + 0.28, x + s * 0.16, z - 0.02), 0.03, 0.005,
                           "ghost", 4))


def locust(b):  # 怨灵（悬空）
    scaled(b, 2.3, lambda: _wraith(b, 0.0, 0.0, 0.0), pivot=(0.0, 0.0, 0.0))


def acid_turret(b):  # 毒雾骨塔
    base(b, 0.8)
    for i in range(6):
        a = i * math.pi / 3
        c, s = math.cos(a), math.sin(a)
        b.chain([P(0.5 * c, 0.5 * s, 0.1), P(0.42 * c, 0.42 * s, 0.7), P(0.2 * c, 0.2 * s, 1.2)], 0.05, 0.03, "bone", 5)
    b.rod(P(0, 0, 0.1), P(0, 0, 1.3), 0.14, 0.12, "bone_dark", 6)
    for a in (0.5, 2.6, 4.7):
        b.ball(P(0.3 * math.cos(a), 0.3 * math.sin(a), 0.3), (0.1, 0.11, 0.1), "bone", 6, 4)
    b.rod(P(0, 0, 1.25), P(0, 0, 1.5), 0.22, 0.3, "iron", 8)
    b.ball(P(0, 0, 1.56), (0.2, 0.2, 0.18), GLOW, 6, 4)


def brood_mother(b):  # 缝合巨怪：背上的光斑是隐藏特性（客户端显示为紫色）
    base(b, 1.6, sides=10)
    mirror(lambda s: b.rod(P(0, s * 0.35, 1.1), P(0.05, s * 0.4, 0.08), 0.2, 0.16, "rot_flesh", 6))
    b.ball(P(0, 0, 1.1), (0.55, 0.45, 0.35), "rot_dark")
    b.ball(P(0.05, 0, 1.75), (0.75, 0.6, 0.65), "rot_flesh")
    b.ball(P(-0.2, 0, 2.2), (0.5, 0.45, 0.4), "rot_flesh")
    b.ball(P(0.35, 0, 2.25), (0.18, 0.2, 0.2), "bone", 6, 4)
    for s in (-1, 1):
        fist = P(0.45, s * 0.8, 0.28)
        b.chain([P(0.05, s * 0.7, 2.0), P(0.3, s * 0.85, 1.2), fist], 0.2, 0.14, "rot_flesh", 6)
        b.ball(fist, (0.2, 0.2, 0.2), "rot_dark", 6, 4)
    for z in (1.5, 1.75, 2.0):
        b.box(P(0.62, 0, z), (0.4, 0.03, 0.03), "iron")
    for f, x, z in ((-0.5, -0.3, 2.3), (-0.35, 0.35, 2.15), (-0.6, 0.05, 1.8), (-0.2, -0.55, 1.9)):
        b.ball(P(f, x, z), (0.12, 0.12, 0.1), GLOW, 6, 4)


def wreck_fortress(b):  # 骸骨要塞：守军尸骨和碎水晶垒成，顶上架着骨弩
    base(b, 1.75, sides=10)
    b.rod(P(0, 0, 0.08), P(0, 0, 1.6), 1.15, 0.9, "stone_dark", 4)
    for i in range(10):
        a = i * math.pi / 5 + 0.3
        c, s = math.cos(a), math.sin(a)
        b.rod(P(1.2 * c, 1.2 * s, 0.3), P(1.7 * c, 1.7 * s, 0.95), 0.07, 0.01, "bone", 5)
    b.ball(P(0.95, 0, 1.1), (0.45, 0.4, 0.42), "bone", 8, 5)
    mirror(lambda s: b.ball(P(1.3, s * 0.16, 1.2), (0.08, 0.08, 0.08), GLOW, 4, 2))
    b.box(P(1.1, 0, 0.72), (0.4, 0.3, 0.12), "bone_dark")
    gem(b, P(-0.3, 0.35, 2.0), (0.15, 0.15, 0.4), "crystal_dead", rot=(20, 0, 30))
    gem(b, P(-0.5, -0.3, 1.9), (0.12, 0.12, 0.3), "crystal_dead", rot=(-15, 10, 0))
    b.rod(P(-0.2, 0, 1.75), P(1.4, 0, 2.3), 0.1, 0.07, "bone", 6)
    b.rod(P(0.9, -0.55, 2.2), P(0.9, 0.55, 2.2), 0.05, 0.05, "bone_dark", 5)
    b.ball(P(1.45, 0, 2.32), (0.07, 0.07, 0.07), GLOW, 4, 2)


# ---- 第二批角色：圆润卡通风格 -------------------------------------------------------
# 和上面的低多边形单位不同，这一批用高分段的球、圆柱和圆角方块拼成，平滑着色（折角大于 50° 的边保留硬边），
# 头身比约 1:3，大头、大手、大眼睛；细节靠铆钉、饰边、扣环、褶边等小零件补。

SEG = 16  # 卡通角色的圆周分段


def S(b, centre, radii, colour, segments=SEG, rings=10, rot=(0.0, 0.0, 0.0)):
    """平滑球（椭球）。"""
    b.ball(centre, radii, colour, segments, rings, rot)


def R(b, a, c, r1, r2, colour, segments=SEG):
    b.rod(a, c, r1, r2, colour, segments)


def limb(b, points, r1, r2, colour, segments=12):
    """关节处补一个球，让手臂和腿弯折处圆润。"""
    b.chain(points, r1, r2, colour, segments)
    n = len(points) - 1
    for i in range(1, n):
        r = r1 + (r2 - r1) * i / n
        S(b, points[i], (r, r, r), colour, 12, 8)


def posed(b, fn, lean=0.0, pivot_z=0.1):
    """画出 fn 的部件后整体前倾：越高的点越往前（f 方向）挪。"""
    before = set(b.bm.verts)
    fn()
    for v in b.bm.verts:
        if v not in before:
            v.co.y -= lean * (v.co.z - pivot_z)


def face(b, c, r, eye="iron", glow_eyes=False, nose=True, brows=None):
    """在头部球体 c、半径 r 的正面画五官：大眼睛带高光、小鼻子、可选眉毛。"""
    f, x, z = -c.y, c.x, c.z
    for s in (-1, 1):
        ex, ez = x + s * r * 0.36, z + r * 0.02
        ef = f + r * 0.86
        if glow_eyes:
            S(b, P(ef, ex, ez), (r * 0.15, r * 0.08, r * 0.13), GLOW, 10, 6)
        else:
            S(b, P(ef, ex, ez), (r * 0.13, r * 0.08, r * 0.19), eye, 10, 8)
            S(b, P(ef + r * 0.07, ex + r * 0.04, ez + r * 0.07), (r * 0.045, r * 0.03, r * 0.045), "white", 6, 4)
        if brows:
            b.rbox(P(f + r * 0.9, ex, z + r * 0.3), (r * 0.3, r * 0.07, r * 0.07), brows, 0.5,
                   rot=(0, s * -12, 0))
    if nose:
        S(b, P(f + r * 0.98, x, z - r * 0.18), (r * 0.09, r * 0.08, r * 0.08), "skin", 8, 6)


def hero(b, z0=0.1, legs="leather", boots="leather", body="cloth_blue", skin="skin", belt="leather",
         robe=None, head_r=0.25, eye="iron", glow_eyes=False, brows=None, has_face=True):
    """卡通人形的腿、靴、躯干、腰带和头。手臂由各角色自己画（姿势不同）。返回关键点。"""
    if robe:
        # 长袍：下摆张开的放样，下沿一圈饰边
        R(b, P(0, 0, z0), P(0, 0, z0 + 0.62), 0.3, 0.17, robe)
        b.ring(P(0, 0, z0 + 0.04), 0.29, 0.035, boots, segments=24, sides=8)  # 下摆饰边
    else:
        for s in (-1, 1):
            b.rbox(P(0.04, s * 0.1, z0 + 0.06), (0.15, 0.24, 0.12), boots, 0.45)
            b.rbox(P(0.0, s * 0.1, z0 + 0.17), (0.15, 0.15, 0.14), boots, 0.45)  # 靴筒
            R(b, P(0, s * 0.1, z0 + 0.2), P(0, s * 0.1, z0 + 0.44), 0.06, 0.075, legs, 12)
        S(b, P(0, 0, z0 + 0.45), (0.19, 0.14, 0.1), legs)
    S(b, P(0, 0, z0 + 0.68), (0.21, 0.16, 0.24), body)
    R(b, P(0, 0, z0 + 0.48), P(0, 0, z0 + 0.56), 0.2, 0.2, belt)
    b.rbox(P(0.19, 0, z0 + 0.52), (0.09, 0.03, 0.08), "gold", 0.4)
    head = P(0, 0, z0 + 0.88 + head_r * 0.9)
    S(b, head, (head_r, head_r * 0.95, head_r * 0.95), skin, 20, 12)
    if has_face:
        face(b, head, head_r, eye=eye, glow_eyes=glow_eyes, brows=brows)
    return {
        "head": head, "head_r": head_r,
        "shoulder": (P(0, 0.22, z0 + 0.82), P(0, -0.22, z0 + 0.82)),  # 右、左
        "chest": P(0.15, 0, z0 + 0.7), "back": P(-0.15, 0, z0 + 0.72), "z0": z0,
    }


def arm(b, shoulder, elbow, hand, sleeve, glove="skin", r=0.06):
    limb(b, [shoulder, elbow, hand], r, r * 0.85, sleeve)
    S(b, hand, (0.065, 0.065, 0.065), glove, 12, 8)


def rivets(b, points, colour="gold", r=0.02):
    for p in points:
        S(b, p, (r, r, r), colour, 8, 6)


def _shield_guard(b):
    k = hero(b, legs="steel_dark", boots="steel", body="steel", belt="leather", brows="leather")
    h, hr = k["head"], k["head_r"]
    z0 = k["z0"]
    # 蓝色罩袍：前后两片，金色滚边
    for f in (0.17, -0.17):
        b.rbox(P(f, 0, z0 + 0.42), (0.26, 0.04, 0.3), "cloth_blue", 0.3)
        b.rbox(P(f, 0, z0 + 0.27), (0.27, 0.045, 0.03), "gold", 0.4)
    # 头盔：半球盔顶 + 帽檐 + 护鼻 + 羽饰
    S(b, h + Vector((0, hr * 0.3, hr * 0.28)), (hr * 1.05, hr * 1.05, hr * 0.88), "steel", 20, 12)
    b.ring(h + Vector((0, hr * 0.22, hr * 0.22)), hr * 1.0, 0.025, "steel_dark", segments=24, sides=6)
    for s in (-1, 1):  # 护颊
        b.rbox(h + Vector((s * hr * 0.82, hr * 0.05, -hr * 0.35)), (0.05, hr * 0.9, hr * 0.7), "steel", 0.5)
    R(b, h + Vector((0, 0, hr * 0.85)), h + Vector((0, 0, hr * 1.1)), 0.03, 0.03, "gold", 10)
    limb(b, [h + Vector((0, 0, hr * 1.1)), h + Vector((0, 0.1, hr * 1.35)), h + Vector((0, 0.3, hr * 1.3)),
             h + Vector((0, 0.42, hr * 0.95))], 0.07, 0.025, "cloth_blue", 10)
    # 肩甲：叠两层圆片，铆钉
    for s, sh in zip((1, -1), k["shoulder"]):
        S(b, sh + Vector((0, 0, 0.04)), (0.13, 0.12, 0.09), "steel", 14, 8)
        S(b, sh + Vector((0, 0, 0.0)), (0.12, 0.13, 0.06), "steel_dark", 14, 8)
        rivets(b, [sh + Vector((s * 0.05, -0.08, 0.08)), sh + Vector((s * 0.05, 0.08, 0.08))])
    rs, ls = k["shoulder"]
    # 右手持剑
    rh = P(0.18, 0.3, z0 + 0.5)
    arm(b, rs, P(0.05, 0.3, z0 + 0.62), rh, "steel", "leather")
    tip = rh + Vector((0, -0.45, 0.22))
    b.rbox((rh + tip) / 2 + Vector((0, -0.04, 0.02)), (0.02, 0.07, 0.46), "white", 0.3,
           rot=(62, 0, 0))
    b.rbox(rh + Vector((0, -0.05, 0.03)), (0.2, 0.04, 0.04), "gold", 0.5, rot=(62, 0, 0))
    S(b, rh + Vector((0, 0.07, -0.03)), (0.035, 0.035, 0.035), "gold", 10, 6)
    # 左手持塔盾：金边白盾、蓝十字、中央水晶、四颗铆钉
    lh = P(0.24, -0.26, z0 + 0.55)
    arm(b, ls, P(0.12, -0.33, z0 + 0.6), lh, "steel", "leather")
    sc = P(0.34, -0.2, z0 + 0.55)
    rot = (0, 0, -18)
    b.rbox(sc, (0.48, 0.07, 0.82), "gold", 0.18, rot=rot)
    b.rbox(sc + Vector((0, -0.01, 0)) + Vector((-0.012, -0.025, 0)), (0.42, 0.06, 0.76), "white", 0.18, rot=rot)
    front = Vector((-0.012, -0.06, 0))
    b.rbox(sc + front, (0.09, 0.04, 0.6), "cloth_blue", 0.3, rot=rot)
    b.rbox(sc + front + Vector((0, 0, 0.1)), (0.34, 0.04, 0.09), "cloth_blue", 0.3, rot=rot)
    gem(b, sc + front * 1.25 + Vector((0, 0, 0.1)), (0.055, 0.05, 0.075))
    b.ring(sc + front * 1.15 + Vector((0, 0, 0.1)), 0.08, 0.014, "gold", axis="f", segments=16, sides=6)
    rivets(b, [sc + front + Vector((dx, 0, dz)) for dx in (-0.15, 0.15) for dz in (-0.3, 0.3)])


def shield_guard(b):  # 布兰 · 盾卫：戴羽饰头盔的骑士，身前一面金边塔盾
    base(b, 0.75, sides=32)
    _shield_guard(b)


def _shadow_assassin(b):
    def body():
        k = hero(b, legs="robe_black", boots="leather", body="cloth_navy", belt="leather", glow_eyes=True,
                 head_r=0.23)
        h, hr, z0 = k["head"], k["head_r"], k["z0"]
        # 兜帽：比头大一圈，帽尖拖向后方；面巾遮住下半张脸
        S(b, h + Vector((0, hr * 0.32, hr * 0.12)), (hr * 1.1, hr * 1.05, hr * 1.08), "robe_black", 20, 12)
        limb(b, [h + Vector((0, 0.12, hr * 0.7)), h + Vector((0, 0.3, hr * 0.6)), h + Vector((0, 0.42, hr * 0.2))],
             0.1, 0.02, "robe_black", 12)
        S(b, h + Vector((0, -hr * 0.55, -hr * 0.35)), (hr * 0.82, hr * 0.5, hr * 0.42), "cloth_blue", 16, 8)
        # 围巾两条长尾飘在身后
        for s in (-1, 1):
            limb(b, [P(-0.12, s * 0.06, z0 + 0.86), P(-0.3, s * 0.1, z0 + 0.8), P(-0.46, s * 0.14, z0 + 0.72)],
                 0.045, 0.02, "cloth_blue", 10)
        # 腰间小包和交叉的皮带
        for s in (-1, 1):
            b.rbox(P(0.05, s * 0.2, z0 + 0.46), (0.08, 0.1, 0.1), "leather", 0.4)
        b.rbox(P(0.15, 0, z0 + 0.7), (0.06, 0.03, 0.4), "leather", 0.4, rot=(0, 35, 0))
        rivets(b, [P(0.17, 0, z0 + 0.7)], "steel")
        # 双手反握匕首
        for s, sh in zip((1, -1), k["shoulder"]):
            hand = P(0.32, s * 0.28, z0 + 0.5)
            arm(b, sh, P(0.12, s * 0.33, z0 + 0.62), hand, "cloth_navy", "robe_black")
            b.rbox(hand + Vector((0, -0.02, 0)), (0.03, 0.05, 0.1), "leather", 0.4)
            b.rbox(hand + Vector((0, -0.02, 0.06)), (0.12, 0.03, 0.03), "gold", 0.5)
            b.rbox(hand + Vector((0, -0.06, -0.18)), (0.015, 0.06, 0.3), GLOW, 0.3, rot=(-25, 0, 0))

    # 压低身形：膝盖弯曲由整体下压 + 前倾表现
    posed(b, body, lean=0.18)


def shadow_assassin(b):  # 夜莺 · 影刃刺客：兜帽面巾、围巾飘尾，双手反握发光匕首
    base(b, 0.75, sides=32)
    _shadow_assassin(b)


def _storm_mage(b):
    k = hero(b, body="white", robe="white", belt="gold", boots="cloth_navy", brows="steel_dark", head_r=0.25)
    h, hr, z0 = k["head"], k["head_r"], k["z0"]
    # 袍子前襟的深蓝饰带和金扣
    b.rbox(P(0.2, 0, z0 + 0.32), (0.1, 0.04, 0.55), "cloth_navy", 0.4, rot=(0, -14, 0))
    rivets(b, [P(0.21 + 0.02 * i, 0, z0 + 0.15 + 0.12 * i) for i in range(3)])
    # 兜帽：翻在脑后的厚兜帽 + 前额的银发
    S(b, h + Vector((0, hr * 0.3, 0.02)), (hr * 1.08, hr * 1.0, hr * 1.06), "cloth_navy", 20, 12)
    S(b, h + Vector((0, -hr * 0.5, hr * 0.55)), (hr * 0.7, hr * 0.4, hr * 0.3), "steel", 14, 8)
    # 胡子：三束向下的白须
    for dx, l in ((-0.06, 0.2), (0, 0.26), (0.06, 0.2)):
        R(b, P(-h.y + hr * 0.8, dx, h.z - hr * 0.45), P(-h.y + hr * 0.7, dx * 1.3, h.z - hr * 0.45 - l),
          0.05, 0.01, "steel", 10)
    # 高立领
    b.ring(P(0, 0, z0 + 0.88), 0.17, 0.04, "gold", segments=20, sides=6)
    for s in (-1, 1):
        b.rbox(P(-0.03, s * 0.14, z0 + 0.98), (0.12, 0.04, 0.2), "cloth_navy", 0.4, rot=(0, 0, s * 20))
    # 肩上披肩
    for sh in k["shoulder"]:
        S(b, sh + Vector((0, 0, 0.02)), (0.12, 0.13, 0.08), "cloth_navy", 14, 8)
    rs, ls = k["shoulder"]
    # 右手持法杖：金环里一颗水晶，周围是折线闪电
    rh = P(0.15, 0.32, z0 + 0.62)
    arm(b, rs, P(0.05, 0.32, z0 + 0.65), rh, "white")
    R(b, Vector((rh.x, rh.y, z0)), Vector((rh.x, rh.y, z0 + 1.45)), 0.025, 0.022, "wood", 10)
    for dz in (0.35, 0.95):
        R(b, Vector((rh.x, rh.y, z0 + dz)), Vector((rh.x, rh.y, z0 + dz + 0.05)), 0.035, 0.035, "gold", 10)
    top = Vector((rh.x, rh.y, z0 + 1.58))
    b.ring(top, 0.13, 0.02, "gold", axis="f", segments=20, sides=6)
    gem(b, top, (0.05, 0.05, 0.09))
    for s in (-1, 1):
        pts = [top + Vector((s * 0.13, 0, 0)), top + Vector((s * 0.2, 0, 0.08)), top + Vector((s * 0.16, 0, 0.13)),
               top + Vector((s * 0.26, 0, 0.24))]
        b.chain(pts, 0.022, 0.006, GLOW, 6)
    # 左手托一团小雷云，下面挂一道闪电
    lh = P(0.16, -0.4, z0 + 0.98)
    arm(b, ls, P(0.06, -0.4, z0 + 0.76), lh, "white")
    cloud = lh + Vector((0, -0.02, 0.2))
    for dx, dz, r in ((-0.07, 0, 0.08), (0.06, 0.01, 0.07), (0, 0.05, 0.09)):
        S(b, cloud + Vector((dx, 0, dz)), (r * 1.2, r, r), "steel_dark", 12, 8)
    b.chain([cloud + Vector((0, -0.02, -0.05)), cloud + Vector((0.04, -0.02, -0.12)),
             cloud + Vector((-0.02, -0.02, -0.16))], 0.02, 0.006, GLOW, 6)


def storm_mage(b):  # 泽恩 · 风暴法师：白袍金立领、白胡子，金环法杖上跳着闪电，左手托雷云
    base(b, 0.75, sides=32)
    _storm_mage(b)


def _skull(b, c, r, glow=True):
    S(b, c, (r, r * 1.05, r * 0.95), "bone", 16, 10)
    b.rbox(c + Vector((0, -r * 0.55, -r * 0.65)), (r * 1.1, r * 0.7, r * 0.45), "bone", 0.5)  # 下颌
    for s in (-1, 1):
        S(b, c + Vector((s * r * 0.38, -r * 0.82, r * 0.05)), (r * 0.26, r * 0.12, r * 0.3), "iron", 10, 6)
        if glow:
            S(b, c + Vector((s * r * 0.38, -r * 0.9, r * 0.05)), (r * 0.1, r * 0.06, r * 0.1), GLOW, 6, 4)
    S(b, c + Vector((0, -r * 0.92, -r * 0.3)), (r * 0.1, r * 0.06, r * 0.1), "iron", 6, 4)
    for i in range(4):
        b.rbox(c + Vector(((i - 1.5) * r * 0.2, -r * 0.88, -r * 0.55)), (r * 0.12, r * 0.06, r * 0.16), "white",
               0.4)


def _necromancer(b):
    k = hero(b, body="robe_black", robe="robe_black", belt="bone_dark", boots="rot_dark", skin="bone", head_r=0.24,
             has_face=False)
    h, hr, z0 = k["head"], k["head_r"], k["z0"]
    # 头部换成骷髅：先盖掉 hero 画的脸
    _skull(b, h + Vector((0, -0.01, 0)), hr * 1.0)
    # 绿色饰带和骨扣
    b.rbox(P(0.19, 0, z0 + 0.32), (0.08, 0.04, 0.55), "rot_dark", 0.4, rot=(0, -14, 0))
    for i in range(3):
        S(b, P(0.21 + 0.02 * i, 0, z0 + 0.15 + 0.12 * i), (0.03, 0.03, 0.03), "bone", 8, 6)
    # 兜帽：尖顶垂在脑后
    S(b, h + Vector((0, hr * 0.3, 0.04)), (hr * 1.15, hr * 1.05, hr * 1.12), "robe_black", 20, 12)
    limb(b, [h + Vector((0, 0.15, hr * 0.8)), h + Vector((0, 0.32, hr * 0.8)), h + Vector((0, 0.45, hr * 0.3))],
         0.11, 0.02, "robe_black", 12)
    # 骨刺领和骷髅肩甲
    for i in range(7):
        a = math.pi * (-0.1 + 1.2 * i / 6)
        o = P(-0.1 * math.sin(a) + 0.02, 0.2 * math.cos(a), z0 + 0.86)
        out = (o - P(0.02, 0, z0 + 0.86)).normalized()
        R(b, o, o + out * 0.1 + Vector((0, 0, 0.16)), 0.035, 0.005, "bone", 8)
    for s, sh in zip((1, -1), k["shoulder"]):
        S(b, sh + Vector((0, 0, 0.02)), (0.13, 0.13, 0.08), "rot_dark", 14, 8)
        _skull(b, sh + Vector((0, -0.02, 0.1)), 0.075, glow=False)
    rs, ls = k["shoulder"]
    # 右手骷髅法杖：骨杖、骷髅杖头、两只角、绿火
    rh = P(0.15, 0.32, z0 + 0.62)
    arm(b, rs, P(0.05, 0.32, z0 + 0.65), rh, "robe_black", "bone")
    R(b, Vector((rh.x, rh.y, z0)), Vector((rh.x, rh.y, z0 + 1.42)), 0.025, 0.02, "bone_dark", 10)
    for dz in (0.3, 0.7, 1.1):
        S(b, Vector((rh.x, rh.y, z0 + dz)), (0.04, 0.04, 0.03), "bone", 10, 6)
    top = Vector((rh.x, rh.y, z0 + 1.55))
    _skull(b, top, 0.1)
    for s in (-1, 1):
        limb(b, [top + Vector((s * 0.08, 0, 0.04)), top + Vector((s * 0.17, 0.02, 0.1)),
                 top + Vector((s * 0.18, 0.05, 0.22))], 0.03, 0.006, "bone_dark", 8)
    for dz, r in ((0.14, 0.07), (0.24, 0.05), (0.32, 0.025)):
        S(b, top + Vector((0, 0.02, dz)), (r, r, r * 1.3), GLOW, 10, 6)
    # 左手掌心一团绿火
    lh = P(0.24, -0.3, z0 + 0.72)
    arm(b, ls, P(0.08, -0.36, z0 + 0.62), lh, "robe_black", "bone")
    for dz, r in ((0.1, 0.06), (0.18, 0.04), (0.25, 0.02)):
        S(b, lh + Vector((0, 0, dz)), (r, r, r * 1.3), GLOW, 10, 6)


def necromancer(b):  # 维克多 · 死灵法师：骷髅头、尖兜帽、骨刺领、骷髅法杖，身边飘着两颗绿火骷髅
    base(b, 0.75, sides=32)
    _necromancer(b)
    for a in (2.0, 4.2):
        c = P(0.6 * math.cos(a), 0.6 * math.sin(a), 1.25)
        _skull(b, c, 0.09)
        for dz, r in ((0.1, 0.06), (0.18, 0.04), (0.25, 0.02)):
            S(b, c + Vector((0, 0.02, dz)), (r, r, r * 1.3), GLOW, 10, 6)


def treant_guard(b):  # 老橡 · 树人守卫：粗树干身躯带树皮纹、苔藓和蘑菇，枝条手臂，茂密树冠里开着紫花
    base(b, 0.8, colour="bark", sides=32)
    # 树根
    for i in range(6):
        a = i * 2 * math.pi / 6 + 0.3
        c, s = math.cos(a), math.sin(a)
        limb(b, [P(0.22 * c, 0.22 * s, 0.4), P(0.45 * c, 0.45 * s, 0.18), P(0.66 * c, 0.66 * s, 0.09)], 0.1, 0.03,
             "bark", 10)
    # 树干：下粗上细，竖向的树皮棱
    R(b, P(0, 0, 0.08), P(0, 0, 1.0), 0.36, 0.3, "bark", 20)
    S(b, P(0, 0, 1.0), (0.3, 0.3, 0.32), "bark", 20, 12)
    for i in range(10):
        a = i * 2 * math.pi / 10
        c, s = math.cos(a), math.sin(a)
        R(b, P(0.35 * c, 0.35 * s, 0.12), P(0.3 * c, 0.3 * s, 1.05), 0.035, 0.025, "wood", 8)
    # 脸：深陷的眼窝、紫色眼光、眉骨、树结嘴、木须
    for s in (-1, 1):
        S(b, P(0.3, s * 0.11, 0.95), (0.07, 0.04, 0.06), "iron", 12, 8)
        S(b, P(0.315, s * 0.11, 0.95), (0.04, 0.03, 0.035), GLOW, 10, 6)
        b.rbox(P(0.32, s * 0.12, 1.04), (0.16, 0.08, 0.06), "wood", 0.5, rot=(0, s * -15, 0))
    S(b, P(0.33, 0, 0.88), (0.04, 0.05, 0.06), "wood", 10, 6)
    S(b, P(0.33, 0, 0.74), (0.09, 0.03, 0.045), "iron", 12, 6)
    for dx in (-0.08, 0, 0.08):
        R(b, P(0.32, dx, 0.7), P(0.3, dx * 1.3, 0.48), 0.035, 0.008, "leaf", 8)
    # 苔藓和小蘑菇
    for f, x, z, r in ((0.2, 0.25, 0.4, 0.09), (-0.2, -0.28, 0.6, 0.1), (0.05, -0.33, 0.25, 0.07)):
        S(b, P(f, x, z), (r, r, r * 0.6), "leaf", 12, 6)
    for f, x in ((0.5, 0.3), (0.45, -0.4)):
        R(b, P(f, x, 0.08), P(f, x, 0.2), 0.025, 0.02, "white", 8)
        S(b, P(f, x, 0.21), (0.07, 0.07, 0.04), "elf_purple", 12, 6)
    # 枝条手臂和三根指头
    for s in (-1, 1):
        hand = P(0.3, s * 0.62, 0.42)
        limb(b, [P(0, s * 0.27, 0.95), P(0.12, s * 0.55, 0.8), hand], 0.11, 0.07, "bark", 12)
        for k in (-1, 0, 1):
            limb(b, [hand, hand + Vector((k * 0.06, -0.08, -0.08)), hand + Vector((k * 0.09, -0.16, -0.06))],
                 0.035, 0.012, "wood", 8)
        S(b, P(0.12, s * 0.5, 0.95), (0.08, 0.08, 0.05), "leaf", 10, 6)  # 肩上的苔
    # 树冠：一大团加周围几团，点缀紫花和星光
    S(b, P(-0.05, 0, 1.55), (0.5, 0.46, 0.36), "leaf", 20, 12)
    for f, x, z, r in ((0.25, 0.3, 1.6, 0.26), (-0.25, -0.32, 1.65, 0.28), (0.05, 0.05, 1.85, 0.3),
                       (-0.3, 0.3, 1.5, 0.24), (0.3, -0.25, 1.5, 0.22)):
        S(b, P(f, x, z), (r, r, r * 0.85), "leaf", 16, 10)
    for f, x, z in ((0.45, 0.2, 1.6), (0.35, -0.38, 1.65), (0.2, 0.1, 2.0), (-0.1, 0.5, 1.7)):
        S(b, P(f, x, z), (0.05, 0.05, 0.05), "elf_purple", 10, 6)
        S(b, P(f, x, z + 0.025), (0.02, 0.02, 0.02), GLOW, 6, 4)

UNITS = [
    ("supply_ship", supply_ship), ("relay", relay), ("battleship", battleship), ("frigate", frigate),
    ("missile_ship", missile_ship), ("pd_ship", pd_ship), ("strike_craft", strike_craft),
    ("hive", hive), ("hive_node", hive_node), ("ripper", ripper), ("crawler", crawler),
    ("locust", locust), ("acid_turret", acid_turret), ("brood_mother", brood_mother),
    ("wreck_fortress", wreck_fortress),
    ("shield_guard", shield_guard), ("shadow_assassin", shadow_assassin), ("storm_mage", storm_mage),
    ("necromancer", necromancer), ("treant_guard", treant_guard),
]

# 用卡通风格（平滑着色）的单位
SMOOTH_UNITS = {"shield_guard", "shadow_assassin", "storm_mage", "necromancer", "treant_guard"}

# 预览图里发光部位的颜色（游戏里由客户端设置，这里只为看效果）
PLAYER_GLOW, UNDEAD_GLOW, ELF_GLOW = "5AB8FF", "8EE06A", "B48CFF"
PREVIEW_GLOW = {"supply_ship": "6FD0B4", "relay": "6FD0B4", "brood_mother": "C48CFF"}

# 预览图：名字 → (单位 id, 默认发光色)
PREVIEW_SHEETS = [
    ("morningstar", ["supply_ship", "relay", "battleship", "frigate", "missile_ship", "pd_ship", "strike_craft"],
     PLAYER_GLOW),
    ("undead", ["hive", "hive_node", "ripper", "crawler", "locust", "acid_turret", "brood_mother",
                "wreck_fortress"], UNDEAD_GLOW),
]
# 第二批样品分属不同阵营，单独排一张
PREVIEW_SAMPLES = [("shield_guard", PLAYER_GLOW), ("shadow_assassin", PLAYER_GLOW), ("storm_mage", PLAYER_GLOW),
                   ("necromancer", UNDEAD_GLOW), ("treant_guard", ELF_GLOW)]


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


def render_sheet(objects, path, spacing=4.6, turn=90, margin=4.5, px=320, height_px=480):
    """把一组模型排成一排，从战斗镜头的角度（侧前方斜看）渲染一张预览图。turn=90 时正面朝画面右侧。"""
    scene = bpy.context.scene
    for i, obj in enumerate(objects):
        obj.location = Vector((i * spacing, 0, 0))
        obj.rotation_euler = (0, 0, math.radians(turn))
    width = (len(objects) - 1) * spacing
    cam_data = bpy.data.cameras.new("PreviewCam")
    cam_data.type = "ORTHO"
    cam_data.ortho_scale = width + margin
    cam_data.shift_y = 0.06
    cam = bpy.data.objects.new("PreviewCam", cam_data)
    scene.collection.objects.link(cam)
    cam.location = Vector((width / 2, -13, 8.5))
    cam.rotation_euler = (math.radians(56), 0, 0)  # 和战斗镜头一样从侧前方斜看
    scene.camera = cam
    sun = bpy.data.objects.new("Sun", bpy.data.lights.new("Sun", "SUN"))
    sun.data.energy = 3.5
    sun.rotation_euler = (math.radians(50), math.radians(10), math.radians(-35))
    scene.collection.objects.link(sun)
    fill = bpy.data.objects.new("Fill", bpy.data.lights.new("Fill", "SUN"))
    fill.data.energy = 1.2
    fill.rotation_euler = (math.radians(60), 0, math.radians(150))  # 从背后打的轮廓光
    scene.collection.objects.link(fill)
    scene.world = bpy.data.worlds.new("World")
    scene.world.use_nodes = True
    scene.world.node_tree.nodes["Background"].inputs["Color"].default_value = (0.004, 0.006, 0.012, 1)
    scene.world.node_tree.nodes["Background"].inputs["Strength"].default_value = 1.0
    scene.render.engine = "CYCLES"
    scene.cycles.samples = 24
    scene.cycles.device = "CPU"
    scene.render.resolution_x = int(px * len(objects))
    scene.render.resolution_y = height_px
    scene.render.filepath = path
    scene.view_settings.view_transform = "Standard"
    bpy.ops.render.render(write_still=True)
    bpy.data.objects.remove(cam)
    bpy.data.objects.remove(sun)
    bpy.data.objects.remove(fill)


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
        obj = b.finish(unit_id, hull, glow, smooth=unit_id in SMOOTH_UNITS)
        export_fbx(obj, os.path.join(OUT_DIR, unit_id + ".fbx"))
        built[unit_id] = (obj, glow)
        print("已导出 %s（%d 个三角面）" % (unit_id, sum(len(p.vertices) - 2 for p in obj.data.polygons)))

    if preview_dir:
        os.makedirs(preview_dir, exist_ok=True)
        sheets = PREVIEW_SHEETS + [("samples", [u for u, _ in PREVIEW_SAMPLES], None)]
        side_glow = dict(PREVIEW_SAMPLES)
        for _, group, colour in PREVIEW_SHEETS:
            side_glow.update((u, colour) for u in group)
        for unit_id, (obj, glow) in built.items():
            set_preview_glow(glow, PREVIEW_GLOW.get(unit_id, side_glow[unit_id]))
        for name, group, _ in sheets:
            for obj, _ in built.values():
                obj.hide_render = True
            objects = [built[u][0] for u in group]
            for obj in objects:
                obj.hide_render = False
            if name == "samples":  # 1 格角色的近景，侧前方 3/4 视角
                render_sheet(objects, os.path.join(preview_dir, name + ".png"), spacing=2.2, turn=35, margin=1.6,
                             px=600, height_px=1000)
            else:
                render_sheet(objects, os.path.join(preview_dir, name + ".png"))
            print("已渲染预览", name)


if __name__ == "__main__":
    main()
