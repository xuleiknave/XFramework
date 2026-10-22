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
    ("royal", "23408E"),
    ("gold_light", "FFE39A"),
    ("gold_dark", "A9822F"),
    ("crimson", "8E2433"),
    ("silver", "D9E1EC"),
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


# ---- 升星外观 ------------------------------------------------------------------
# 卡通角色按星级出三档模型：★1 朴素、★3 精锐、★5 传说（★2/★4 沿用下一档以下的模型，靠体型和界面星标区分）。
# 升星不换造型，只提升服装和装备的质感：金色滚边、刺绣纹样、披风、宝石扣，★5 再换华贵配色、多层饰边和发光宝石。

STAR_TIERS = (1, 3, 5)


def model_id(unit_id, star):
    return unit_id if star == 1 else "%s_s%d" % (unit_id, star)


def star_base(b, r, star, colour="stone_dark"):
    """底座：★3 加金边，★5 再在金边上镶一圈发光宝石。"""
    base(b, r, colour, sides=32)
    if star >= 3:
        b.ring(P(0, 0, 0.082), r * 0.95, 0.024, "gold", segments=48, sides=6)
    if star >= 5:
        b.ring(P(0, 0, 0.082), r * 0.8, 0.014, "gold_light", segments=40, sides=6)
        for i in range(8):
            a = i * math.pi / 4 + math.pi / 8
            c = P(r * 0.95 * math.cos(a), r * 0.95 * math.sin(a), 0.1)
            S(b, c, (0.035, 0.035, 0.02), "gold", 10, 6)
            gem(b, c + Vector((0, 0, 0.035)), (0.022, 0.022, 0.04))


def emblem(b, c, s=1.0, colour="gold", jewel=None):
    """刺绣纹样：中间一个圆环，两侧卷草和圆点，可选中心宝石。c 在衣服正面，纹样朝前。"""
    b.ring(c, 0.045 * s, 0.007 * s, colour, axis="f", segments=16, sides=4)
    for sx in (-1, 1):
        b.ring(c + Vector((sx * 0.075 * s, 0, -0.018 * s)), 0.026 * s, 0.006 * s, colour, axis="f", segments=12,
               sides=4)
        S(b, c + Vector((sx * 0.115 * s, 0, -0.04 * s)), (0.011 * s,) * 3, colour, 8, 6)
        S(b, c + Vector((sx * 0.04 * s, 0, 0.055 * s)), (0.009 * s,) * 3, colour, 8, 6)
    if jewel:
        gem(b, c + Vector((0, -0.008, 0)), (0.026 * s, 0.02 * s, 0.036 * s), jewel)


def cape(b, top_z, bottom_z, w_top, w_bottom, colour, trim=None, trim2=None, f0=-0.13, flare=0.14, folds=3,
         cols=10, rows=8, thick=0.018):
    """背后的披风：一片有厚度的弧面，下摆外飘，带几道褶；trim 是下摆的饰边颜色，trim2 是第二道饰边。"""
    before = set(b.bm.faces)

    def point(t, u, off):
        z = top_z + (bottom_z - top_z) * t
        w = w_top + (w_bottom - w_top) * t
        x = u * w
        f = f0 - (0.04 + flare * t) * (1.0 - 0.55 * u * u) + 0.018 * math.sin(u * math.pi * folds) * t - off
        return P(f, x, z)

    grid = []
    for off in (0.0, thick):
        layer = []
        for i in range(rows + 1):
            t = i / rows
            layer.append([b.bm.verts.new(point(t, -1 + 2 * j / cols, off)) for j in range(cols + 1)])
        grid.append(layer)
    front, back = grid
    for i in range(rows):
        for j in range(cols):
            b.bm.faces.new((front[i][j], front[i][j + 1], front[i + 1][j + 1], front[i + 1][j]))
            b.bm.faces.new((back[i][j], back[i + 1][j], back[i + 1][j + 1], back[i][j + 1]))
    for i in range(rows):  # 两侧边
        for j in (0, cols):
            b.bm.faces.new((front[i][j], front[i + 1][j], back[i + 1][j], back[i][j]))
    for j in range(cols):  # 上下边
        b.bm.faces.new((front[rows][j], front[rows][j + 1], back[rows][j + 1], back[rows][j]))
        b.bm.faces.new((front[0][j], back[0][j], back[0][j + 1], front[0][j + 1]))
    b._paint(b._new_faces(before), colour)
    if trim:
        hem = [point(0.97, -1 + 2 * j / cols, thick / 2) for j in range(cols + 1)]
        b.chain(hem, 0.016, 0.016, trim, 8)
        side = [[point(i / rows, u, thick / 2) for i in range(rows + 1)] for u in (-0.98, 0.98)]
        for line in side:
            b.chain(line, 0.012, 0.012, trim, 6)
    if trim2:
        hem2 = [point(0.86, -1 + 2 * j / cols, thick / 2) for j in range(cols + 1)]
        b.chain(hem2, 0.008, 0.008, trim2, 6)
    return point


def clasp(b, c, jewel=GLOW, s=1.0):
    """披风扣：金圆片 + 宝石。"""
    S(b, c, (0.045 * s, 0.025 * s, 0.045 * s), "gold", 12, 8)
    gem(b, c + Vector((0, -0.02 * s, 0)), (0.022 * s, 0.018 * s, 0.03 * s), jewel)


def _shield_guard(b, star):
    armor = "white" if star >= 5 else "steel"
    trim = "gold" if star >= 3 else "steel_dark"
    tabard = "royal" if star >= 5 else "cloth_blue"
    k = hero(b, legs="steel_dark", boots=armor, body=armor, belt="gold" if star >= 3 else "leather",
             brows="leather")
    h, hr = k["head"], k["head_r"]
    z0 = k["z0"]
    if star >= 3:  # 披风先画，在身后
        cape(b, z0 + 0.86, z0 + 0.04, 0.2, 0.34, tabard, trim="gold", trim2="gold_light" if star >= 5 else None)
        for sh in k["shoulder"]:
            clasp(b, sh + Vector((0, -0.04, 0.02)), s=0.8)
    # 罩袍：前后两片，滚边
    for f in (0.17, -0.17):
        b.rbox(P(f, 0, z0 + 0.42), (0.26, 0.04, 0.3), tabard, 0.3)
        b.rbox(P(f, 0, z0 + 0.27), (0.27, 0.045, 0.03), trim, 0.4)
        if star >= 3:
            for s in (-1, 1):
                b.rbox(P(f, s * 0.13, z0 + 0.42), (0.025, 0.045, 0.3), trim, 0.4)
    if star >= 3:
        emblem(b, P(0.205, 0, z0 + 0.4), 0.9, "gold", jewel=GLOW if star >= 5 else None)
    if star >= 5:  # 胸甲上的金色日轮纹
        emblem(b, P(0.215, 0, z0 + 0.72), 1.1, "gold_light", jewel=GLOW)
    # 头盔
    S(b, h + Vector((0, hr * 0.3, hr * 0.28)), (hr * 1.05, hr * 1.05, hr * 0.88), armor, 20, 12)
    b.ring(h + Vector((0, hr * 0.22, hr * 0.22)), hr * 1.0, 0.025 if star == 1 else 0.032, trim, segments=24,
           sides=6)
    for s in (-1, 1):  # 护颊
        b.rbox(h + Vector((s * hr * 0.82, hr * 0.05, -hr * 0.35)), (0.05, hr * 0.9, hr * 0.7), armor, 0.5)
    R(b, h + Vector((0, 0, hr * 0.85)), h + Vector((0, 0, hr * 1.1)), 0.03, 0.03, "gold", 10)
    plume = "white" if star >= 5 else "cloth_blue"
    limb(b, [h + Vector((0, 0, hr * 1.1)), h + Vector((0, 0.1, hr * 1.38)), h + Vector((0, 0.3, hr * 1.35)),
             h + Vector((0, 0.46, hr * (0.9 if star >= 5 else 0.95)))], 0.08 if star >= 5 else 0.07, 0.025, plume, 10)
    if star >= 5:  # 头盔两侧的金翼
        for s in (-1, 1):
            for i, (dz, l) in enumerate(((0.1, 0.16), (0.03, 0.13), (-0.04, 0.1))):
                o = h + Vector((s * hr * 0.95, hr * 0.2, hr * 0.3 + dz))
                R(b, o, o + Vector((s * 0.05, l * 0.9, l * 0.45)), 0.022, 0.004, "gold", 8)
    # 肩甲
    for s, sh in zip((1, -1), k["shoulder"]):
        S(b, sh + Vector((0, 0, 0.04)), (0.13, 0.12, 0.09), armor, 14, 8)
        S(b, sh + Vector((0, 0, 0.0)), (0.12, 0.13, 0.06), "gold" if star >= 5 else "steel_dark", 14, 8)
        if star >= 3:
            b.ring(sh + Vector((0, 0, 0.03)), 0.12, 0.012, "gold", segments=20, sides=4)
        rivets(b, [sh + Vector((s * 0.05, -0.08, 0.08)), sh + Vector((s * 0.05, 0.08, 0.08))])
    rs, ls = k["shoulder"]
    # 右手持剑
    rh = P(0.18, 0.3, z0 + 0.5)
    arm(b, rs, P(0.05, 0.3, z0 + 0.62), rh, armor, "leather" if star == 1 else "gold")
    tip = rh + Vector((0, -0.45, 0.22))
    b.rbox((rh + tip) / 2 + Vector((0, -0.04, 0.02)), (0.02, 0.07, 0.46), "white", 0.3, rot=(62, 0, 0))
    if star >= 5:
        b.rbox((rh + tip) / 2 + Vector((0, -0.045, 0.02)), (0.026, 0.025, 0.36), GLOW, 0.4, rot=(62, 0, 0))
    b.rbox(rh + Vector((0, -0.05, 0.03)), (0.24 if star >= 3 else 0.2, 0.04, 0.04), "gold", 0.5, rot=(62, 0, 0))
    S(b, rh + Vector((0, 0.07, -0.03)), (0.035, 0.035, 0.035), "gold", 10, 6)
    # 左手持塔盾
    lh = P(0.24, -0.26, z0 + 0.55)
    arm(b, ls, P(0.12, -0.33, z0 + 0.6), lh, armor, "leather" if star == 1 else "gold")
    sc = P(0.34, -0.2, z0 + 0.55)
    rot = (0, 0, -18)
    rim = "gold" if star >= 3 else "steel_dark"
    face_c = "white"
    b.rbox(sc, (0.5 if star >= 3 else 0.48, 0.07, 0.84 if star >= 3 else 0.82), rim, 0.18, rot=rot)
    b.rbox(sc + Vector((-0.012, -0.035, 0)), (0.42, 0.06, 0.76), face_c, 0.18, rot=rot)
    front = Vector((-0.012, -0.06, 0))
    cross = "royal" if star >= 5 else "cloth_blue"
    b.rbox(sc + front, (0.09, 0.04, 0.6), cross, 0.3, rot=rot)
    b.rbox(sc + front + Vector((0, 0, 0.1)), (0.34, 0.04, 0.09), cross, 0.3, rot=rot)
    if star >= 3:  # 十字的金色描边
        b.rbox(sc + front * 0.95, (0.12, 0.035, 0.63), "gold", 0.3, rot=rot)
        b.rbox(sc + front * 0.95 + Vector((0, 0, 0.1)), (0.37, 0.035, 0.12), "gold", 0.3, rot=rot)
        for dx in (-0.19, 0.19):  # 四角包金
            for dz in (-0.36, 0.36):
                S(b, sc + front * 0.9 + Vector((dx, 0, dz)), (0.05, 0.03, 0.05), "gold", 12, 6)
    js = 1.4 if star >= 5 else 1.0
    gem(b, sc + front * 1.3 + Vector((0, 0, 0.1)), (0.055 * js, 0.05 * js, 0.075 * js))
    b.ring(sc + front * 1.15 + Vector((0, 0, 0.1)), 0.08 * js, 0.014, "gold", axis="f", segments=16, sides=6)
    if star == 1:
        rivets(b, [sc + front + Vector((dx, 0, dz)) for dx in (-0.15, 0.15) for dz in (-0.3, 0.3)], "steel_dark")
    if star >= 5:  # 盾面日轮：宝石周围一圈金色光芒
        for i in range(8):
            a = i * math.pi / 4
            o = sc + front * 1.2 + Vector((0, 0, 0.1))
            d = Vector((math.cos(a), 0, math.sin(a)))
            R(b, o + d * 0.12, o + d * 0.2, 0.014, 0.003, "gold_light", 6)


def shield_guard(b, star=1):  # 布兰 · 盾卫：戴羽饰头盔的骑士，身前一面塔盾；★3 金边披风，★5 白金甲、金翼盔、日轮盾
    star_base(b, 0.75, star)
    _shield_guard(b, star)


def _shadow_assassin(b, star):
    cloak = "elf_dark" if star >= 5 else "robe_black"
    metal = "gold" if star >= 3 else "steel"

    def body():
        k = hero(b, legs=cloak, boots="leather" if star < 5 else cloak, body="cloth_navy",
                 belt="leather", glow_eyes=True, head_r=0.23)
        h, hr, z0 = k["head"], k["head_r"], k["z0"]
        if star >= 5:  # 半身短披风
            cape(b, z0 + 0.86, z0 + 0.32, 0.18, 0.28, cloak, trim="gold", f0=-0.12, flare=0.2, rows=6)
        # 兜帽和面巾
        S(b, h + Vector((0, hr * 0.32, hr * 0.12)), (hr * 1.1, hr * 1.05, hr * 1.08), cloak, 20, 12)
        if star >= 3:  # 兜帽金边
            b.ring(h + Vector((0, -hr * 0.2, hr * 0.12)), hr * 0.95, 0.012, metal, axis="f", segments=24, sides=4)
        limb(b, [h + Vector((0, 0.12, hr * 0.7)), h + Vector((0, 0.3, hr * 0.6)), h + Vector((0, 0.42, hr * 0.2))],
             0.1, 0.02, cloak, 12)
        S(b, h + Vector((0, -hr * 0.55, -hr * 0.35)), (hr * 0.82, hr * 0.5, hr * 0.42), "cloth_blue", 16, 8)
        for s in (-1, 1):
            tail = [P(-0.12, s * 0.06, z0 + 0.86), P(-0.3, s * 0.1, z0 + 0.8), P(-0.46, s * 0.14, z0 + 0.72)]
            limb(b, tail, 0.045, 0.02, "cloth_blue", 10)
            if star >= 3:
                S(b, tail[-1], (0.025, 0.025, 0.025), metal, 8, 6)
        # 腰包、斜挎皮带
        for s in (-1, 1):
            b.rbox(P(0.05, s * 0.2, z0 + 0.46), (0.08, 0.1, 0.1), "leather", 0.4)
            if star >= 3:
                S(b, P(0.1, s * 0.2, z0 + 0.48), (0.018, 0.012, 0.018), metal, 8, 6)
        b.rbox(P(0.15, 0, z0 + 0.7), (0.06, 0.03, 0.4), "leather", 0.4, rot=(0, 35, 0))
        if star >= 3:  # 第二条交叉皮带和飞刀
            b.rbox(P(0.15, 0, z0 + 0.7), (0.06, 0.03, 0.4), "leather", 0.4, rot=(0, -35, 0))
            for i in range(3):
                o = P(0.17, -0.04 + i * 0.05, z0 + 0.62 + i * 0.07)
                b.rbox(o, (0.015, 0.02, 0.09), "steel", 0.3, rot=(0, -35, 0))
        rivets(b, [P(0.17, 0, z0 + 0.7)], metal)
        if star >= 5:  # 胸前的宝石徽章
            emblem(b, P(0.19, 0, z0 + 0.7), 0.7, "gold", jewel=GLOW)
        if star >= 3:  # 护肩
            for sh in k["shoulder"]:
                S(b, sh + Vector((0, 0, 0.03)), (0.1, 0.1, 0.06), "leather", 12, 8)
                b.ring(sh + Vector((0, 0, 0.03)), 0.095, 0.01, metal, segments=16, sides=4)
        # 双手反握匕首
        blade = 0.36 if star >= 5 else 0.3
        for s, sh in zip((1, -1), k["shoulder"]):
            hand = P(0.32, s * 0.28, z0 + 0.5)
            arm(b, sh, P(0.12, s * 0.33, z0 + 0.62), hand, "cloth_navy", cloak)
            b.rbox(hand + Vector((0, -0.02, 0)), (0.03, 0.05, 0.1), "leather", 0.4)
            b.rbox(hand + Vector((0, -0.02, 0.06)), (0.16 if star >= 3 else 0.12, 0.03, 0.03), metal, 0.5)
            if star >= 3:
                S(b, hand + Vector((0, -0.02, 0.1)), (0.022, 0.022, 0.022), metal, 8, 6)
            b.rbox(hand + Vector((0, -0.06, -blade * 0.6)), (0.015, 0.06, blade), GLOW, 0.3, rot=(-25, 0, 0))

    posed(b, body, lean=0.18)


def shadow_assassin(b, star=1):  # 夜莺 · 影刃刺客：兜帽面巾、围巾飘尾，双手反握发光匕首；★3 金扣交叉带飞刀，★5 紫黑短披风
    star_base(b, 0.75, star)
    _shadow_assassin(b, star)


def _storm_mage(b, star):
    trim = "gold" if star >= 3 else "cloth_navy"
    k = hero(b, body="white", robe="white", belt="gold", boots="royal" if star >= 5 else "cloth_navy",
             brows="steel_dark", head_r=0.25)
    h, hr, z0 = k["head"], k["head_r"], k["z0"]
    if star >= 5:  # 深蓝外袍披肩，金边
        cape(b, z0 + 0.88, z0 + 0.02, 0.22, 0.36, "royal", trim="gold", trim2="gold_light", f0=-0.1, flare=0.1)
    if star >= 3:  # 下摆金边和金色闪电纹
        b.ring(P(0, 0, z0 + 0.1), 0.28, 0.018, "gold", segments=32, sides=6)
        for a in (-0.5, 0.5):
            o = P(0.27 * math.cos(a), 0.27 * math.sin(a), z0 + 0.18)
            b.chain([o, o + Vector((0, 0, 0.06)), o + Vector((0.02 * a, -0.01, 0.09)), o + Vector((0, 0, 0.15))],
                    0.01, 0.006, "gold", 6)
    # 前襟饰带和扣子
    b.rbox(P(0.2, 0, z0 + 0.32), (0.1, 0.04, 0.55), "royal" if star >= 5 else "cloth_navy", 0.4, rot=(0, -14, 0))
    if star >= 3:
        for s in (-1, 1):
            b.rbox(P(0.205, s * 0.055, z0 + 0.32), (0.015, 0.045, 0.55), "gold", 0.4, rot=(0, -14, 0))
    if star >= 5:
        for i in range(3):
            gem(b, P(0.22 + 0.02 * i, 0, z0 + 0.15 + 0.12 * i), (0.022, 0.018, 0.03))
    else:
        rivets(b, [P(0.21 + 0.02 * i, 0, z0 + 0.15 + 0.12 * i) for i in range(3)])
    # 兜帽、银发、胡子
    S(b, h + Vector((0, hr * 0.3, 0.02)), (hr * 1.08, hr * 1.0, hr * 1.06), "royal" if star >= 5 else "cloth_navy",
      20, 12)
    if star >= 3:
        b.ring(h + Vector((0, -hr * 0.18, 0.02)), hr * 0.98, 0.014, "gold", axis="f", segments=24, sides=4)
    S(b, h + Vector((0, -hr * 0.5, hr * 0.55)), (hr * 0.7, hr * 0.4, hr * 0.3), "steel", 14, 8)
    for dx, l in ((-0.06, 0.2), (0, 0.26), (0.06, 0.2)):
        R(b, P(-h.y + hr * 0.8, dx, h.z - hr * 0.45), P(-h.y + hr * 0.7, dx * 1.3, h.z - hr * 0.45 - l),
          0.05, 0.01, "steel", 10)
    if star >= 5:  # 额前的星饰
        gem(b, h + Vector((0, -hr * 0.95, hr * 0.45)), (0.03, 0.02, 0.045))
    # 立领
    b.ring(P(0, 0, z0 + 0.88), 0.17, 0.04, "gold", segments=20, sides=6)
    collar = "royal" if star >= 5 else "cloth_navy"
    for s in (-1, 1):
        b.rbox(P(-0.03, s * 0.14, z0 + 0.98), (0.12, 0.04, 0.22 if star >= 5 else 0.2), collar, 0.4,
               rot=(0, 0, s * 20))
        if star >= 3:
            b.rbox(P(-0.03, s * 0.16, z0 + 0.98), (0.13, 0.015, 0.23 if star >= 5 else 0.21), "gold", 0.4,
                   rot=(0, 0, s * 20))
    # 披肩、袖口
    for sh in k["shoulder"]:
        S(b, sh + Vector((0, 0, 0.02)), (0.12, 0.13, 0.08), collar, 14, 8)
        if star >= 3:
            b.ring(sh + Vector((0, 0, 0.0)), 0.12, 0.012, "gold", segments=20, sides=4)
    rs, ls = k["shoulder"]
    rh = P(0.15, 0.32, z0 + 0.62)
    arm(b, rs, P(0.05, 0.32, z0 + 0.65), rh, "white")
    lh = P(0.16, -0.4, z0 + 0.98)
    arm(b, ls, P(0.06, -0.4, z0 + 0.76), lh, "white")
    if star >= 3:
        for hand, elbow in ((rh, P(0.05, 0.32, z0 + 0.65)), (lh, P(0.06, -0.4, z0 + 0.76))):
            cuff = hand + (elbow - hand).normalized() * 0.08
            S(b, cuff, (0.065, 0.065, 0.04), trim, 12, 6)
    # 法杖
    R(b, Vector((rh.x, rh.y, z0)), Vector((rh.x, rh.y, z0 + 1.45)), 0.025, 0.022,
      "gold" if star >= 5 else "wood", 10)
    for dz in ((0.35, 0.65, 0.95, 1.25) if star >= 3 else (0.35, 0.95)):
        R(b, Vector((rh.x, rh.y, z0 + dz)), Vector((rh.x, rh.y, z0 + dz + 0.05)), 0.035, 0.035,
          "royal" if star >= 5 else "gold", 10)
    top = Vector((rh.x, rh.y, z0 + 1.58))
    b.ring(top, 0.13, 0.02, "gold", axis="f", segments=20, sides=6)
    if star >= 5:
        b.ring(top, 0.17, 0.012, "gold_light", axis="x", segments=20, sides=6)
        b.ring(top, 0.19, 0.01, "gold_light", axis="z", segments=24, sides=6)
    gem(b, top, (0.05, 0.05, 0.09) if star < 5 else (0.07, 0.07, 0.13))
    for s in (-1, 1):
        pts = [top + Vector((s * 0.13, 0, 0)), top + Vector((s * 0.2, 0, 0.08)), top + Vector((s * 0.16, 0, 0.13)),
               top + Vector((s * 0.26, 0, 0.24))]
        b.chain(pts, 0.022, 0.006, GLOW, 6)
    # 雷云
    cloud = lh + Vector((0, -0.02, 0.2))
    for dx, dz, r in ((-0.07, 0, 0.08), (0.06, 0.01, 0.07), (0, 0.05, 0.09)):
        S(b, cloud + Vector((dx, 0, dz)), (r * 1.2, r, r), "steel_dark", 12, 8)
    b.chain([cloud + Vector((0, -0.02, -0.05)), cloud + Vector((0.04, -0.02, -0.12)),
             cloud + Vector((-0.02, -0.02, -0.16))], 0.02, 0.006, GLOW, 6)
    if star >= 5:  # 身边环绕的符文环
        b.ring(P(0, 0, z0 + 0.7), 0.5, 0.008, GLOW, segments=40, sides=4)
        for i in range(6):
            a = i * math.pi / 3
            gem(b, P(0.5 * math.cos(a), 0.5 * math.sin(a), z0 + 0.7), (0.02, 0.02, 0.035))


def storm_mage(b, star=1):  # 泽恩 · 风暴法师：白袍金立领、白胡子，金环法杖，左手托雷云；★3 金边刺绣，★5 深蓝外袍、宝石扣、符文环
    star_base(b, 0.75, star)
    _storm_mage(b, star)


def _skull(b, c, r, glow=True, crown=None):
    if r < 0.05:  # 骷髅链上的小骷髅：省掉牙齿和下颌，控制面数
        S(b, c, (r, r * 1.05, r * 0.95), "bone", 10, 6)
        for s in (-1, 1):
            S(b, c + Vector((s * r * 0.38, -r * 0.82, r * 0.05)), (r * 0.26, r * 0.12, r * 0.3), "iron", 6, 4)
        return
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
    if crown:
        b.ring(c + Vector((0, 0, r * 0.55)), r * 0.85, r * 0.08, crown, segments=20, sides=6)
        for i in range(5):
            a = math.pi * (0.1 + 0.8 * i / 4)
            o = c + Vector((r * 0.85 * math.cos(a), -r * 0.85 * math.sin(a), r * 0.6))
            R(b, o, o + Vector((0, 0, r * (0.5 if i == 2 else 0.35))), r * 0.09, r * 0.01, crown, 6)


def _necromancer(b, star):
    robe = "robe_black"
    metal = "gold_dark" if star >= 3 else "bone_dark"
    k = hero(b, body=robe, robe=robe, belt=metal, boots="crimson" if star >= 5 else "rot_dark", skin="bone",
             head_r=0.24, has_face=False)
    h, hr, z0 = k["head"], k["head_r"], k["z0"]
    if star >= 3:  # 破旧但华贵的长披风：★3 黑底暗金边，★5 深红里衬
        cape(b, z0 + 0.9, z0 + 0.02, 0.22, 0.38, "crimson" if star >= 5 else robe, trim=metal,
             trim2="gold" if star >= 5 else None, f0=-0.1, flare=0.16)
    _skull(b, h + Vector((0, -0.01, 0)), hr, crown="gold_dark" if star >= 5 else None)
    # 前襟饰带
    b.rbox(P(0.19, 0, z0 + 0.32), (0.08, 0.04, 0.55), "crimson" if star >= 5 else "rot_dark", 0.4, rot=(0, -14, 0))
    if star >= 3:
        for s in (-1, 1):
            b.rbox(P(0.195, s * 0.045, z0 + 0.32), (0.012, 0.045, 0.55), metal, 0.4, rot=(0, -14, 0))
        b.ring(P(0, 0, z0 + 0.12), 0.28, 0.018, metal, segments=32, sides=6)
    for i in range(3):
        S(b, P(0.21 + 0.02 * i, 0, z0 + 0.15 + 0.12 * i), (0.03, 0.03, 0.03), "bone", 8, 6)
    # 兜帽
    S(b, h + Vector((0, hr * 0.3, 0.04)), (hr * 1.15, hr * 1.05, hr * 1.12), robe, 20, 12)
    if star >= 3:
        b.ring(h + Vector((0, -hr * 0.2, 0.04)), hr * 1.0, 0.014, metal, axis="f", segments=24, sides=4)
    limb(b, [h + Vector((0, 0.15, hr * 0.8)), h + Vector((0, 0.32, hr * 0.8)), h + Vector((0, 0.45, hr * 0.3))],
         0.11, 0.02, robe, 12)
    # 骨刺领
    for i in range(9 if star >= 5 else 7):
        n = 8 if star >= 5 else 6
        a = math.pi * (-0.1 + 1.2 * i / n)
        o = P(-0.1 * math.sin(a) + 0.02, 0.2 * math.cos(a), z0 + 0.86)
        out = (o - P(0.02, 0, z0 + 0.86)).normalized()
        R(b, o, o + out * 0.1 + Vector((0, 0, 0.16 if star < 5 else 0.2)), 0.035, 0.005, "bone", 8)
    if star >= 3:  # 胸前的骷髅链
        for i in range(5):
            u = -1 + i / 2
            _skull(b, P(0.2 - 0.03 * abs(u), u * 0.13, z0 + 0.66 - 0.06 * (1 - abs(u))), 0.035, glow=False)
    for s, sh in zip((1, -1), k["shoulder"]):
        S(b, sh + Vector((0, 0, 0.02)), (0.13, 0.13, 0.08), metal if star >= 5 else "rot_dark", 14, 8)
        _skull(b, sh + Vector((0, -0.02, 0.1)), 0.075, glow=star >= 5)
        if star >= 3:
            b.ring(sh + Vector((0, 0, 0.0)), 0.13, 0.012, metal, segments=20, sides=4)
    rs, ls = k["shoulder"]
    rh = P(0.15, 0.32, z0 + 0.62)
    arm(b, rs, P(0.05, 0.32, z0 + 0.65), rh, robe, "bone")
    R(b, Vector((rh.x, rh.y, z0)), Vector((rh.x, rh.y, z0 + 1.42)), 0.025, 0.02, "bone_dark", 10)
    for dz in (0.3, 0.7, 1.1):
        S(b, Vector((rh.x, rh.y, z0 + dz)), (0.04, 0.04, 0.03), metal if star >= 3 else "bone", 10, 6)
    top = Vector((rh.x, rh.y, z0 + 1.55))
    _skull(b, top, 0.1, crown="gold_dark" if star >= 3 else None)
    horns = ((0.08, 0.17, 0.18),) if star < 5 else ((0.08, 0.17, 0.18), (0.06, 0.24, 0.3))
    for hx, hy, hz in horns:
        for s in (-1, 1):
            limb(b, [top + Vector((s * hx, 0, 0.04)), top + Vector((s * hy, 0.02, hz * 0.55)),
                     top + Vector((s * (hy + 0.01), 0.05, hz + 0.04))], 0.03, 0.006, "bone_dark", 8)
    for dz, r in ((0.14, 0.07), (0.24, 0.05), (0.32, 0.025)):
        S(b, top + Vector((0, 0.02, dz)), (r, r, r * 1.3), GLOW, 10, 6)
    lh = P(0.24, -0.3, z0 + 0.72)
    arm(b, ls, P(0.08, -0.36, z0 + 0.62), lh, robe, "bone")
    for dz, r in ((0.1, 0.06), (0.18, 0.04), (0.25, 0.02)):
        S(b, lh + Vector((0, 0, dz)), (r, r, r * 1.3), GLOW, 10, 6)


def necromancer(b, star=1):  # 维克多 · 死灵法师：骷髅头、尖兜帽、骨刺领、骷髅法杖、绿火骷髅；★3 暗金边披风骷髅链，★5 金冠深红披风
    star_base(b, 0.75, star)
    _necromancer(b, star)
    for a in ((2.0, 4.2) if star < 5 else (1.8, 3.1, 4.4)):
        c = P(0.6 * math.cos(a), 0.6 * math.sin(a), 1.25)
        _skull(b, c, 0.09)
        for dz, r in ((0.1, 0.06), (0.18, 0.04), (0.25, 0.02)):
            S(b, c + Vector((0, 0.02, dz)), (r, r, r * 1.3), GLOW, 10, 6)
    if star >= 5:
        b.ring(P(0, 0, 0.35), 0.55, 0.008, GLOW, segments=40, sides=4)


def treant_guard(b, star=1):  # 老橡 · 树人守卫：树干身躯、枝条手臂、树冠；★3 藤蔓腰带和银饰，★5 花冠、发光果实、银色符文环
    star_base(b, 0.8, star, colour="bark")
    for i in range(6):
        a = i * 2 * math.pi / 6 + 0.3
        c, s = math.cos(a), math.sin(a)
        limb(b, [P(0.22 * c, 0.22 * s, 0.4), P(0.45 * c, 0.45 * s, 0.18), P(0.66 * c, 0.66 * s, 0.09)], 0.1, 0.03,
             "bark", 10)
    R(b, P(0, 0, 0.08), P(0, 0, 1.0), 0.36, 0.3, "bark", 20)
    S(b, P(0, 0, 1.0), (0.3, 0.3, 0.32), "bark", 20, 12)
    for i in range(10):
        a = i * 2 * math.pi / 10
        c, s = math.cos(a), math.sin(a)
        R(b, P(0.35 * c, 0.35 * s, 0.12), P(0.3 * c, 0.3 * s, 1.05), 0.035, 0.025, "wood", 8)
    if star >= 3:  # 藤蔓腰带，紫宝石扣
        b.ring(P(0, 0, 0.55), 0.355, 0.03, "leaf", segments=32, sides=6)
        b.ring(P(0, 0, 0.55), 0.36, 0.012, "silver", segments=32, sides=4)
        S(b, P(0.36, 0, 0.55), (0.06, 0.03, 0.06), "silver", 12, 8)
        gem(b, P(0.385, 0, 0.55), (0.035, 0.025, 0.05))
    if star >= 5:  # 树干上的银色符文环
        for z in (0.3, 0.8):
            b.ring(P(0, 0, z), 0.34 - 0.02 * z, 0.014, "silver", segments=32, sides=4)
            for i in range(6):
                a = i * math.pi / 3 + z
                gem(b, P((0.35 - 0.02 * z) * math.cos(a), (0.35 - 0.02 * z) * math.sin(a), z), (0.018, 0.018, 0.03))
    # 脸
    for s in (-1, 1):
        S(b, P(0.3, s * 0.11, 0.95), (0.07, 0.04, 0.06), "iron", 12, 8)
        S(b, P(0.315, s * 0.11, 0.95), (0.04, 0.03, 0.035), GLOW, 10, 6)
        b.rbox(P(0.32, s * 0.12, 1.04), (0.16, 0.08, 0.06), "wood", 0.5, rot=(0, s * -15, 0))
    S(b, P(0.33, 0, 0.88), (0.04, 0.05, 0.06), "wood", 10, 6)
    S(b, P(0.33, 0, 0.74), (0.09, 0.03, 0.045), "iron", 12, 6)
    for dx in (-0.08, 0, 0.08):
        R(b, P(0.32, dx, 0.7), P(0.3, dx * 1.3, 0.48), 0.035, 0.008, "leaf", 8)
    # 苔藓、蘑菇
    for f, x, z, r in ((0.2, 0.25, 0.4, 0.09), (-0.2, -0.28, 0.6, 0.1), (0.05, -0.33, 0.25, 0.07)):
        S(b, P(f, x, z), (r, r, r * 0.6), "leaf", 12, 6)
    shrooms = ((0.5, 0.3), (0.45, -0.4)) + (((0.1, 0.6), (-0.4, 0.4)) if star >= 3 else ())
    for f, x in shrooms:
        R(b, P(f, x, 0.08), P(f, x, 0.2), 0.025, 0.02, "white", 8)
        S(b, P(f, x, 0.21), (0.07, 0.07, 0.04), "elf_purple", 12, 6)
        if star >= 3:
            for a in (0.0, 2.1, 4.2):
                S(b, P(f + 0.04 * math.cos(a), x + 0.04 * math.sin(a), 0.245), (0.012,) * 3, GLOW, 6, 4)
    # 手臂
    for s in (-1, 1):
        hand = P(0.3, s * 0.62, 0.42)
        limb(b, [P(0, s * 0.27, 0.95), P(0.12, s * 0.55, 0.8), hand], 0.11, 0.07, "bark", 12)
        for k in (-1, 0, 1):
            limb(b, [hand, hand + Vector((k * 0.06, -0.08, -0.08)), hand + Vector((k * 0.09, -0.16, -0.06))],
                 0.035, 0.012, "wood", 8)
        S(b, P(0.12, s * 0.5, 0.95), (0.08, 0.08, 0.05), "leaf", 10, 6)
        if star >= 3:  # 手腕上的银环
            b.ring(P(0.24, s * 0.6, 0.52), 0.075, 0.012, "silver", segments=16, sides=4)
    # 树冠
    S(b, P(-0.05, 0, 1.55), (0.5, 0.46, 0.36), "leaf", 20, 12)
    for f, x, z, r in ((0.25, 0.3, 1.6, 0.26), (-0.25, -0.32, 1.65, 0.28), (0.05, 0.05, 1.85, 0.3),
                       (-0.3, 0.3, 1.5, 0.24), (0.3, -0.25, 1.5, 0.22)):
        S(b, P(f, x, z), (r, r, r * 0.85), "leaf", 16, 10)
    flowers = [(0.45, 0.2, 1.6), (0.35, -0.38, 1.65), (0.2, 0.1, 2.0), (-0.1, 0.5, 1.7)]
    if star >= 3:
        flowers += [(0.5, -0.05, 1.45), (0.1, -0.45, 1.85), (-0.2, 0.2, 2.05), (0.3, 0.4, 1.8)]
    if star >= 5:
        flowers += [(0.0, 0.55, 1.5), (-0.4, -0.2, 1.8), (0.4, 0.05, 1.85), (-0.15, -0.5, 1.55), (0.25, -0.2, 2.05)]
    for f, x, z in flowers:
        for i in range(5 if star >= 3 else 1):  # ★3 起是五瓣花
            a = i * 2 * math.pi / 5
            d = 0.03 if star >= 3 else 0.0
            S(b, P(f, x + d * math.cos(a), z + d * math.sin(a)), (0.035 if star >= 3 else 0.05,) * 3,
              "elf_purple" if i % 2 == 0 else "white", 10, 6)
        S(b, P(f + 0.02, x, z + (0 if star >= 3 else 0.025)), (0.02, 0.02, 0.02), GLOW, 6, 4)
    if star >= 5:  # 垂下的发光果实和枝角花冠
        for f, x in ((0.3, 0.35), (0.35, -0.3), (-0.2, 0.45), (-0.3, -0.4)):
            R(b, P(f, x, 1.38), P(f, x, 1.2), 0.008, 0.008, "wood", 6)
            gem(b, P(f, x, 1.15), (0.04, 0.04, 0.06))
        for s in (-1, 1):
            limb(b, [P(0, s * 0.2, 2.0), P(0.05, s * 0.32, 2.2), P(0.02, s * 0.45, 2.3)], 0.03, 0.008, "wood", 8)
            R(b, P(0.04, s * 0.3, 2.17), P(0.08, s * 0.25, 2.3), 0.015, 0.004, "wood", 6)


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


# ---- 卡通角色的烘焙贴图和描边 ------------------------------------------------------
# 色块贴图只有平涂颜色。卡通角色另外展开一套 UV，把“颜色 × 环境光遮蔽（AO）”烘焙进角色专属贴图
# <单位 id>_tex.png（客户端 ModelLibrary 优先用它），缝隙、腋下、帽檐下会自然变暗，体积感更强。
# 描边用“反向外壳”：沿法线外扩一层、翻转朝向的网格，用深色；Unity 默认剔除背面，所以只在轮廓处露出一圈线。

BAKE_SIZE = 1024
OUTLINE_STRIP = 0.03  # 贴图顶部留给描边颜色的色带高度（占比）
OUTLINE_COLOUR = "1C1A26"
OUTLINE_WIDTH = 0.012
AO_STRENGTH = 0.5  # 0 = 不加阴影，1 = 缝隙全黑
AO_DISTANCE = 0.12  # 只算这个距离内的遮挡：缝隙变暗，兜帽下的脸不至于整片发黑


def _image_node(mat, image):
    node = mat.node_tree.nodes.new("ShaderNodeTexImage")
    node.image = image
    for n in mat.node_tree.nodes:
        n.select = False
    node.select = True
    mat.node_tree.nodes.active = node
    return node


def _pixels(image):
    import numpy as np
    buf = np.empty(image.size[0] * image.size[1] * 4, dtype=np.float32)
    image.pixels.foreach_get(buf)
    return buf.reshape(image.size[1], image.size[0], 4)


def bake_texture(obj, unit_id, out_dir):
    """展开 UV、烘焙颜色和 AO，合成角色贴图；返回贴图（Hull 材质改为用它）。"""
    import numpy as np
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.samples = 128
    if scene.world is None:
        scene.world = bpy.data.worlds.new("BakeWorld")
    scene.world.light_settings.distance = AO_DISTANCE
    # 之前生成的角色也都摆在原点，烘焙时要藏起来，否则会互相遮出阴影
    others = [o for o in scene.objects if o is not obj and not o.hide_render]
    for o in others:
        o.hide_render = True
    mesh = obj.data
    palette_name = mesh.uv_layers[0].name
    mesh.uv_layers[0].active_render = True
    mesh.uv_layers.new(name="Bake")
    mesh.uv_layers.active = mesh.uv_layers["Bake"]
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=math.radians(60), island_margin=0.004)
    bpy.ops.object.mode_set(mode="OBJECT")
    hull, glow = mesh.materials[0], mesh.materials[1]
    # 色块贴图走原来的 UV
    uvnode = hull.node_tree.nodes.new("ShaderNodeUVMap")
    uvnode.uv_map = palette_name
    tex_node = next(n for n in hull.node_tree.nodes if n.type == "TEX_IMAGE")
    hull.node_tree.links.new(uvnode.outputs["UV"], tex_node.inputs["Vector"])

    results = {}
    for kind in ("DIFFUSE", "AO"):
        image = bpy.data.images.new(unit_id + "_" + kind, BAKE_SIZE, BAKE_SIZE, alpha=False)
        for mat in (hull, glow):
            _image_node(mat, image)
        if kind == "DIFFUSE":
            bpy.ops.object.bake(type="DIFFUSE", pass_filter={"COLOR"}, margin=6, use_clear=True)
        else:
            bpy.ops.object.bake(type="AO", margin=6, use_clear=True)
        results[kind] = _pixels(image)

    for o in others:
        o.hide_render = False
    colour = results["DIFFUSE"][:, :, :3]
    ao = np.sqrt(results["AO"][:, :, :1])  # 开方让过渡更柔和，只有真正的深缝才明显变暗
    final = colour * (1.0 - AO_STRENGTH + AO_STRENGTH * ao)
    rows = BAKE_SIZE
    strip = max(4, int(rows * OUTLINE_STRIP))
    out = np.ones((rows + strip, BAKE_SIZE, 4), dtype=np.float32)
    out[:rows, :, :3] = final
    out[rows:, :, :3] = [srgb_to_linear(c) for c in hex_rgb(OUTLINE_COLOUR)]
    tex = bpy.data.images.new(unit_id + "_tex", BAKE_SIZE, rows + strip, alpha=False)
    tex.pixels.foreach_set(out.ravel())
    tex.filepath_raw = os.path.join(out_dir, unit_id + "_tex.png")
    tex.file_format = "PNG"
    tex.save()

    # 角色 UV 压到下方，只留 Bake 这一套 UV
    scale = rows / (rows + strip)
    for d in mesh.uv_layers["Bake"].data:
        d.uv = (d.uv[0], d.uv[1] * scale)
    mesh.uv_layers.remove(mesh.uv_layers[palette_name])
    mesh.uv_layers["Bake"].active_render = True
    for mat in (hull, glow):
        nodes = mat.node_tree.nodes
        for n in list(nodes):
            if n.type in ("TEX_IMAGE", "UVMAP"):
                nodes.remove(n)
    tex_node = hull.node_tree.nodes.new("ShaderNodeTexImage")
    tex_node.image = tex
    hull.node_tree.links.new(tex_node.outputs["Color"], hull.node_tree.nodes["Principled BSDF"].inputs["Base Color"])
    return (rows + strip / 2) / (rows + strip)


def add_outline(obj, outline_v):
    """加反向外壳描边；返回外壳的面索引范围（预览时换成背面透明的材质）。"""
    mesh = obj.data
    b = bmesh.new()
    b.from_mesh(mesh)
    uv = b.loops.layers.uv.active
    first = len(b.faces)
    src = list(b.faces)
    vmap = {}
    for v in list(b.verts):
        vmap[v] = b.verts.new(v.co + v.normal * OUTLINE_WIDTH)
    for f in src:
        nf = b.faces.new([vmap[v] for v in reversed(f.verts)])
        nf.material_index = 0
        nf.smooth = True
        for loop in nf.loops:
            loop[uv].uv = (0.5, outline_v)
    b.to_mesh(mesh)
    b.free()
    return first, len(mesh.polygons)


def outline_preview_material(obj, span):
    """Cycles 不剔除背面：预览时外壳用“背面透明”的深色材质，模拟游戏里的效果。"""
    mat = bpy.data.materials.new("OutlinePreview")
    mat.use_nodes = True
    nodes, links = mat.node_tree.nodes, mat.node_tree.links
    bsdf = nodes["Principled BSDF"]
    rgb = [srgb_to_linear(c) for c in hex_rgb(OUTLINE_COLOUR)]
    bsdf.inputs["Base Color"].default_value = (*rgb, 1.0)
    bsdf.inputs["Roughness"].default_value = 1.0
    geo = nodes.new("ShaderNodeNewGeometry")
    clear = nodes.new("ShaderNodeBsdfTransparent")
    mix = nodes.new("ShaderNodeMixShader")
    out = nodes["Material Output"]
    # 只在镜头光线、而且看到外壳正面时才显示；阴影光线一律穿过，免得外壳把角色整个罩进阴影
    path = nodes.new("ShaderNodeLightPath")
    not_cam = nodes.new("ShaderNodeMath")
    not_cam.operation = "SUBTRACT"
    not_cam.inputs[0].default_value = 1.0
    links.new(path.outputs["Is Camera Ray"], not_cam.inputs[1])
    hide = nodes.new("ShaderNodeMath")
    hide.operation = "MAXIMUM"
    links.new(geo.outputs["Backfacing"], hide.inputs[0])
    links.new(not_cam.outputs[0], hide.inputs[1])
    links.new(hide.outputs[0], mix.inputs[0])
    links.new(bsdf.outputs[0], mix.inputs[1])  # 外壳翻转过：朝向镜头的一侧是背面，要透明
    links.new(clear.outputs[0], mix.inputs[2])
    links.new(mix.outputs[0], out.inputs["Surface"])
    obj.data.materials.append(mat)
    for poly in obj.data.polygons[span[0]:span[1]]:
        poly.material_index = 2


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


def _trim_rows(image, pad=24):
    """裁掉一行渲染图上下的空背景。"""
    from PIL import ImageChops
    bg = image.getpixel((0, 0))
    diff = ImageChops.difference(image, image.__class__.new(image.mode, image.size, bg)).convert("L")
    box = diff.point(lambda v: 255 if v > 12 else 0).getbbox()
    if not box:
        return image
    return image.crop((0, max(0, box[1] - pad), image.width, min(image.height, box[3] + pad)))


def render_star_sheet(built, unit_ids, path):
    """升星对比图：每名角色一行，从左到右 ★1、★3、★5。"""
    from PIL import Image
    rows = []
    for unit_id in unit_ids:
        ids = [model_id(unit_id, star) for star in STAR_TIERS]
        if any(i not in built for i in ids):
            continue
        for obj, _ in built.values():
            obj.hide_render = True
        objects = [built[i][0] for i in ids]
        for obj in objects:
            obj.hide_render = False
        row_path = path + "." + unit_id + ".png"
        render_sheet(objects, row_path, spacing=2.2, turn=35, margin=1.6, px=600, height_px=1000)
        rows.append(_trim_rows(Image.open(row_path).convert("RGB")))
        os.remove(row_path)
    if not rows:
        return
    sheet = Image.new("RGB", (rows[0].width, sum(r.height for r in rows)))
    y = 0
    for r in rows:
        sheet.paste(r, (0, y))
        y += r.height
    sheet.save(path)
    print("已渲染预览 stars")


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else sys.argv[1:]
    preview_dir = None
    if "--preview" in argv:
        preview_dir = os.path.abspath(argv[argv.index("--preview") + 1])
    only = set(argv[argv.index("--only") + 1].split(",")) if "--only" in argv else None

    bpy.ops.wm.read_factory_settings(use_empty=True)
    os.makedirs(OUT_DIR, exist_ok=True)
    palette = build_palette_image()
    palette.filepath_raw = os.path.join(OUT_DIR, "Palette.png")
    palette.file_format = "PNG"
    palette.save()

    built = {}
    outlines = {}
    unit_of = {}
    for unit_id, fn in UNITS:
        if only and unit_id not in only:
            continue
        cartoon = unit_id in SMOOTH_UNITS
        for star in (STAR_TIERS if cartoon else (1,)):
            mid = model_id(unit_id, star)
            hull, glow = make_materials(palette)
            b = Builder()
            if cartoon:
                fn(b, star)
            else:
                fn(b)
            obj = b.finish(mid, hull, glow, smooth=cartoon)
            if cartoon:
                outline_v = bake_texture(obj, mid, OUT_DIR)
                outlines[mid] = add_outline(obj, outline_v)
            export_fbx(obj, os.path.join(OUT_DIR, mid + ".fbx"))
            built[mid] = (obj, glow)
            unit_of[mid] = unit_id
            print("已导出 %s（%d 个三角面）" % (mid, sum(len(p.vertices) - 2 for p in obj.data.polygons)))

    if preview_dir:
        os.makedirs(preview_dir, exist_ok=True)
        sheets = PREVIEW_SHEETS + [("samples", [u for u, _ in PREVIEW_SAMPLES], None)]
        side_glow = dict(PREVIEW_SAMPLES)
        for _, group, colour in PREVIEW_SHEETS:
            side_glow.update((u, colour) for u in group)
        for mid, (obj, glow) in built.items():
            unit_id = unit_of[mid]
            set_preview_glow(glow, PREVIEW_GLOW.get(unit_id, side_glow[unit_id]))
            if mid in outlines:
                outline_preview_material(obj, outlines[mid])
        for name, group, _ in sheets:
            if any(u not in built for u in group):
                continue
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
        render_star_sheet(built, [u for u, _ in PREVIEW_SAMPLES], os.path.join(preview_dir, "stars.png"))


if __name__ == "__main__":
    main()
