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
from mathutils import Matrix, Quaternion, Vector

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
    ("elf_skin", "F0D9CC"),
    ("hair_brown", "5A3A22"),
    ("hair_blond", "E6C77A"),
    ("hair_red", "B5502E"),
    ("ghost_blue", "A9C7E8"),
    ("fire", "E0662A"),
    ("ice", "A8DDF2"),
    ("moss", "6E8F3E"),
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
    """往一个 bmesh 里堆零件；每个零件指定色块名或 GLOW。

    卡通角色还会登记骨骼（bone）：每个零件记一个编号，导出前按零件中心找最近的骨骼，整块绑到那根骨骼上
    （卡通角色是一块块拼起来的，整块刚性绑定就够了）。attach 可以把一段代码画的零件强制绑到指定骨骼。"""

    def __init__(self):
        self.bm = bmesh.new()
        self.uv = self.bm.loops.layers.uv.verify()
        self.part = self.bm.verts.layers.int.new("part")
        self.next_part = 1
        self.part_bone = {}  # 零件编号 → 强制绑定的骨骼
        self.force = None
        self.bones = []  # [名字, 头, 尾, 父骨骼]，骨骼坐标和网格一样
        self.draw_base = True  # 卡通角色不把底座画进角色网格，底座单独导出（只在展示界面用）
        self.base_spec = None

    def _paint(self, faces, colour):
        glow = colour == GLOW
        uv = (0.0, 0.0) if glow else palette_uv(colour)
        part = self.next_part
        self.next_part += 1
        if self.force:
            self.part_bone[part] = self.force
        for face in faces:
            face.material_index = 1 if glow else 0
            for loop in face.loops:
                loop[self.uv].uv = uv
            for v in face.verts:
                if v[self.part] == 0:
                    v[self.part] = part

    def bone(self, name, head, tail, parent="root"):
        """登记一根骨骼；同名的只保留第一根。返回名字。"""
        if not any(bn[0] == name for bn in self.bones):
            if not self.bones and name != "root":
                self.bones.append(["root", Vector((0, 0, 0)), Vector((0, 0, 0.12)), None])
            self.bones.append([name, Vector(head), Vector(tail), parent if name != "root" else None])
        return name

    def has_bone(self, name):
        return any(bn[0] == name for bn in self.bones)

    def hand_bone(self, hand):
        """离 hand 最近的前臂骨骼（武器绑在拿着它的手上）。"""
        best, dist = None, 0.25
        for name, head, tail, _ in self.bones:
            if name.startswith("arm_lower") and (tail - hand).length < dist:
                best, dist = name, (tail - hand).length
        return best

    class _Attach:
        def __init__(self, builder, bone):
            self.builder, self.bone, self.saved = builder, bone, None

        def __enter__(self):
            self.saved = self.builder.force
            if self.bone:
                self.builder.force = self.bone

        def __exit__(self, *exc):
            self.builder.force = self.saved

    def attach(self, bone):
        """with b.attach("arm_lower.R"): 里面画的零件都绑到这根骨骼；bone 为 None 时不改变。"""
        return Builder._Attach(self, bone)

    def held(self, hand):
        return self.attach(self.hand_bone(hand))

    def _bind(self):
        """把每个零件绑到骨骼：强制绑定优先，否则找离零件中心最近的骨骼段。"""
        sums = {}
        for v in self.bm.verts:
            acc = sums.setdefault(v[self.part], [Vector((0, 0, 0)), 0])
            acc[0] += v.co
            acc[1] += 1
        index = {bn[0]: i for i, bn in enumerate(self.bones)}
        choice = {}
        for part, (total, n) in sums.items():
            if part in self.part_bone and self.part_bone[part] in index:
                choice[part] = index[self.part_bone[part]]
                continue
            c = total / n
            best, dist = 0, None
            for i, (name, head, tail, _) in enumerate(self.bones):
                seg = tail - head
                t = max(0.0, min(1.0, (c - head).dot(seg) / max(seg.length_squared, 1e-9)))
                d = (head + seg * t - c).length
                if name == "root":
                    d += 0.15  # 根骨骼只接没有别的骨骼可接的零件（比如树根）
                if dist is None or d < dist:
                    best, dist = i, d
            choice[part] = best
        deform = self.bm.verts.layers.deform.verify()
        for v in self.bm.verts:
            v[deform][choice[v[self.part]]] = 1.0

    def ground(self):
        """把模型最低点移到 z = 0（角色不带底座，脚踩地面）。"""
        low = min(v.co.z for v in self.bm.verts)
        for v in self.bm.verts:
            v.co.z -= low
        for bn in self.bones:
            bn[1] = bn[1] - Vector((0, 0, low))
            bn[2] = bn[2] - Vector((0, 0, low))
        if self.bones:  # 根骨骼在脚底中心，倒下时绕脚底转
            self.bones[0][1], self.bones[0][2] = Vector((0, 0, 0)), Vector((0, 0, 0.12))
        return low

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
        rigged = len(self.bones) > 1
        if rigged:
            self._bind()
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
        if rigged:
            for bn in self.bones:
                obj.vertex_groups.new(name=bn[0])
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
    n_bones = len(b.bones)
    fn()
    c = Vector(pivot)
    for v in b.bm.verts:
        if v not in before:
            v.co = c + (v.co - c) * k
    for bn in b.bones[n_bones:]:
        for i in (1, 2):
            bn[i] = c + (bn[i] - c) * k


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
    n_bones = len(b.bones)
    fn()
    for v in b.bm.verts:
        if v not in before:
            v.co.y -= lean * (v.co.z - pivot_z)
    for bn in b.bones[n_bones:]:
        for i in (1, 2):
            bn[i] = bn[i] - Vector((0, lean * (bn[i].z - pivot_z), 0))


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
    humanoid_bones(b, z0, head, head_r, legs=not robe)
    return {
        "head": head, "head_r": head_r,
        "shoulder": (P(0, 0.22, z0 + 0.82), P(0, -0.22, z0 + 0.82)),  # 右、左
        "chest": P(0.15, 0, z0 + 0.7), "back": P(-0.15, 0, z0 + 0.72), "z0": z0,
    }


def humanoid_bones(b, z0, head, head_r, legs=True, parent="root", f=0.0):
    """人形骨骼：hips（骨盆）、spine（躯干）、head，有腿时加 leg.R / leg.L。"""
    b.bone("hips", P(f, 0, z0 + 0.3), P(f, 0, z0 + 0.52), parent)
    b.bone("spine", P(f, 0, z0 + 0.52), P(f, 0, z0 + 0.88), "hips")
    b.bone("head", P(f, 0, z0 + 0.88), head + Vector((0, 0, head_r)), "spine")
    if legs:
        for side, s in (("R", 1), ("L", -1)):
            b.bone("leg." + side, P(f, s * 0.1, z0 + 0.44), P(f + 0.02, s * 0.1, z0 + 0.04), "hips")


def arm(b, shoulder, elbow, hand, sleeve, glove="skin", r=0.06):
    side = "R" if shoulder.x >= 0 else "L"
    upper = b.bone("arm_upper." + side, shoulder, elbow, "spine")
    lower = b.bone("arm_lower." + side, elbow, hand, upper)
    with b.attach(upper):
        R(b, shoulder, elbow, r, r * 0.925, sleeve, 12)
    with b.attach(lower):
        S(b, elbow, (r * 0.925,) * 3, sleeve, 12, 8)
        R(b, elbow, hand, r * 0.925, r * 0.85, sleeve, 12)
        S(b, hand, (0.065, 0.065, 0.065), glove, 12, 8)


def rivets(b, points, colour="gold", r=0.02):
    for p in points:
        S(b, p, (r, r, r), colour, 8, 6)


# ---- 升星外观 ------------------------------------------------------------------
# 卡通角色每个星级一档模型：★1 朴素，★2 银色滚边和扣件，★3 精锐（金边、刺绣、披风、宝石扣），
# ★4 在 ★3 上提前加一部分传说元素，★5 传说（华贵配色、多层饰边、发光宝石）。升星不换造型，只提升服装和装备的质感。

STAR_TIERS = (1, 2, 3, 4, 5)


def metal(star, plain):
    """饰边和扣件的金属：★1 用 plain，★2 银，★3 起金。"""
    return plain if star == 1 else ("silver" if star == 2 else "gold")


def model_id(unit_id, star):
    return unit_id if star == 1 else "%s_s%d" % (unit_id, star)


def star_base(b, r, star, colour="stone_dark"):
    """底座：★2 银边，★3 金边，★4 金边镶 4 颗发光宝石，★5 再加一道内圈、镶 8 颗。
    卡通角色的底座不画进角色网格（实际战斗不需要底座），只记下规格，另外导出 base_s<星级> / base_large_s<星级>。"""
    if not b.draw_base:
        b.base_spec = (r, star, colour)
        return
    base(b, r, colour, sides=32)
    if star >= 2:
        b.ring(P(0, 0, 0.082), r * 0.95, 0.024, metal(star, "silver"), segments=48, sides=6)
    if star >= 5:
        b.ring(P(0, 0, 0.082), r * 0.8, 0.014, "gold_light", segments=40, sides=6)
    if star >= 4:
        n = 8 if star >= 5 else 4
        for i in range(n):
            a = i * 2 * math.pi / n + math.pi / n
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


def held_weapon(fn):
    """武器零件绑在拿着它的那只手（最近的前臂骨骼）上。"""
    def wrapped(b, hand, *args, **kwargs):
        with b.held(hand):
            return fn(b, hand, *args, **kwargs)
    wrapped.__doc__ = fn.__doc__
    return wrapped


def cape(b, top_z, bottom_z, *args, f0=-0.13, flare=0.14, **kwargs):
    """披风单独一根骨骼（cape），待机和走路时会飘动。"""
    name = "cape" if not b.has_bone("cape") else "cape2"
    b.bone(name, P(f0, 0, top_z), P(f0 - 0.04 - flare, 0, bottom_z), "spine" if b.has_bone("spine") else "root")
    with b.attach(name):
        return _cape(b, top_z, bottom_z, *args, f0=f0, flare=flare, **kwargs)


def _cape(b, top_z, bottom_z, w_top, w_bottom, colour, trim=None, trim2=None, f0=-0.13, flare=0.14, folds=3,
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
    trim = metal(star, "steel_dark")
    tabard = "royal" if star >= 5 else "cloth_blue"
    k = hero(b, legs="steel_dark", boots=armor, body=armor, belt="leather" if star == 1 else metal(star, ""),
             brows="leather")
    h, hr = k["head"], k["head_r"]
    z0 = k["z0"]
    if star >= 3:  # 披风先画，在身后
        cape(b, z0 + 0.86, z0 + 0.04, 0.2, 0.34, tabard, trim="gold", trim2="gold_light" if star >= 4 else None)
        for sh in k["shoulder"]:
            clasp(b, sh + Vector((0, -0.04, 0.02)), s=0.8)
    # 罩袍：前后两片，滚边
    for f in (0.17, -0.17):
        b.rbox(P(f, 0, z0 + 0.42), (0.26, 0.04, 0.3), tabard, 0.3)
        b.rbox(P(f, 0, z0 + 0.27), (0.27, 0.045, 0.03), trim, 0.4)
        if star >= 2:
            for s in (-1, 1):
                b.rbox(P(f, s * 0.13, z0 + 0.42), (0.025, 0.045, 0.3), trim, 0.4)
    if star >= 3:
        emblem(b, P(0.205, 0, z0 + 0.4), 0.9, "gold", jewel=GLOW if star >= 4 else None)
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
    if star >= 4:  # 头盔两侧的金翼
        for s in (-1, 1):
            for i, (dz, l) in enumerate(((0.1, 0.16), (0.03, 0.13), (-0.04, 0.1))):
                o = h + Vector((s * hr * 0.95, hr * 0.2, hr * 0.3 + dz))
                R(b, o, o + Vector((s * 0.05, l * 0.9, l * 0.45)), 0.022, 0.004, "gold", 8)
    # 肩甲
    for s, sh in zip((1, -1), k["shoulder"]):
        S(b, sh + Vector((0, 0, 0.04)), (0.13, 0.12, 0.09), armor, 14, 8)
        S(b, sh + Vector((0, 0, 0.0)), (0.12, 0.13, 0.06), "gold" if star >= 5 else "steel_dark", 14, 8)
        if star >= 2:
            b.ring(sh + Vector((0, 0, 0.03)), 0.12, 0.012, trim, segments=20, sides=4)
        rivets(b, [sh + Vector((s * 0.05, -0.08, 0.08)), sh + Vector((s * 0.05, 0.08, 0.08))], metal(star, "gold"))
    rs, ls = k["shoulder"]
    # 右手持剑
    rh = P(0.18, 0.3, z0 + 0.5)
    arm(b, rs, P(0.05, 0.3, z0 + 0.62), rh, armor, "leather" if star == 1 else metal(star, ""))
    tip = rh + Vector((0, -0.45, 0.22))
    b.rbox((rh + tip) / 2 + Vector((0, -0.04, 0.02)), (0.02, 0.07, 0.46), "white", 0.3, rot=(62, 0, 0))
    if star >= 5:
        b.rbox((rh + tip) / 2 + Vector((0, -0.045, 0.02)), (0.026, 0.025, 0.36), GLOW, 0.4, rot=(62, 0, 0))
    b.rbox(rh + Vector((0, -0.05, 0.03)), (0.24 if star >= 3 else 0.2, 0.04, 0.04), "gold", 0.5, rot=(62, 0, 0))
    S(b, rh + Vector((0, 0.07, -0.03)), (0.035, 0.035, 0.035), "gold", 10, 6)
    # 左手持塔盾
    lh = P(0.24, -0.26, z0 + 0.55)
    arm(b, ls, P(0.12, -0.33, z0 + 0.6), lh, armor, "leather" if star == 1 else metal(star, ""))
    sc = P(0.34, -0.2, z0 + 0.55)
    rot = (0, 0, -18)
    rim = metal(star, "steel_dark")
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
    js = 1.4 if star >= 5 else (1.2 if star >= 4 else 1.0)
    gem(b, sc + front * 1.3 + Vector((0, 0, 0.1)), (0.055 * js, 0.05 * js, 0.075 * js))
    b.ring(sc + front * 1.15 + Vector((0, 0, 0.1)), 0.08 * js, 0.014, "gold", axis="f", segments=16, sides=6)
    if star <= 2:
        rivets(b, [sc + front + Vector((dx, 0, dz)) for dx in (-0.15, 0.15) for dz in (-0.3, 0.3)],
               metal(star, "steel_dark"))
    if star >= 5:  # 盾面日轮：宝石周围一圈金色光芒
        for i in range(8):
            a = i * math.pi / 4
            o = sc + front * 1.2 + Vector((0, 0, 0.1))
            d = Vector((math.cos(a), 0, math.sin(a)))
            R(b, o + d * 0.12, o + d * 0.2, 0.014, 0.003, "gold_light", 6)


def shield_guard(b, star=1):  # 布兰 · 盾卫：戴羽饰头盔的骑士，身前一面塔盾；★2 银边，★3 金边披风，★4 金翼盔，★5 白金甲、日轮盾
    star_base(b, 0.75, star)
    _shield_guard(b, star)


def _shadow_assassin(b, star):
    cloak = "elf_dark" if star >= 5 else "robe_black"
    trim = metal(star, "steel")

    def body():
        k = hero(b, legs=cloak, boots="leather" if star < 5 else cloak, body="cloth_navy",
                 belt="leather", glow_eyes=True, head_r=0.23)
        h, hr, z0 = k["head"], k["head_r"], k["z0"]
        if star >= 4:  # 半身短披风
            cape(b, z0 + 0.86, z0 + 0.32, 0.18, 0.28, cloak, trim="gold", f0=-0.12, flare=0.2, rows=6)
        # 兜帽和面巾
        S(b, h + Vector((0, hr * 0.32, hr * 0.12)), (hr * 1.1, hr * 1.05, hr * 1.08), cloak, 20, 12)
        if star >= 2:  # 兜帽饰边
            b.ring(h + Vector((0, -hr * 0.2, hr * 0.12)), hr * 0.95, 0.012, trim, axis="f", segments=24, sides=4)
        limb(b, [h + Vector((0, 0.12, hr * 0.7)), h + Vector((0, 0.3, hr * 0.6)), h + Vector((0, 0.42, hr * 0.2))],
             0.1, 0.02, cloak, 12)
        S(b, h + Vector((0, -hr * 0.55, -hr * 0.35)), (hr * 0.82, hr * 0.5, hr * 0.42), "cloth_blue", 16, 8)
        for s in (-1, 1):
            tail = [P(-0.12, s * 0.06, z0 + 0.86), P(-0.3, s * 0.1, z0 + 0.8), P(-0.46, s * 0.14, z0 + 0.72)]
            limb(b, tail, 0.045, 0.02, "cloth_blue", 10)
            if star >= 2:
                S(b, tail[-1], (0.025, 0.025, 0.025), trim, 8, 6)
        # 腰包、斜挎皮带
        for s in (-1, 1):
            b.rbox(P(0.05, s * 0.2, z0 + 0.46), (0.08, 0.1, 0.1), "leather", 0.4)
            if star >= 2:
                S(b, P(0.1, s * 0.2, z0 + 0.48), (0.018, 0.012, 0.018), trim, 8, 6)
        b.rbox(P(0.15, 0, z0 + 0.7), (0.06, 0.03, 0.4), "leather", 0.4, rot=(0, 35, 0))
        if star >= 3:  # 第二条交叉皮带和飞刀
            b.rbox(P(0.15, 0, z0 + 0.7), (0.06, 0.03, 0.4), "leather", 0.4, rot=(0, -35, 0))
            for i in range(3):
                o = P(0.17, -0.04 + i * 0.05, z0 + 0.62 + i * 0.07)
                b.rbox(o, (0.015, 0.02, 0.09), "steel", 0.3, rot=(0, -35, 0))
        rivets(b, [P(0.17, 0, z0 + 0.7)], trim)
        if star >= 4:  # 胸前的宝石徽章
            emblem(b, P(0.19, 0, z0 + 0.7), 0.7, "gold", jewel=GLOW)
        if star >= 3:  # 护肩
            for sh in k["shoulder"]:
                S(b, sh + Vector((0, 0, 0.03)), (0.1, 0.1, 0.06), "leather", 12, 8)
                b.ring(sh + Vector((0, 0, 0.03)), 0.095, 0.01, trim, segments=16, sides=4)
        # 双手反握匕首
        blade = 0.36 if star >= 5 else (0.33 if star >= 4 else 0.3)
        for s, sh in zip((1, -1), k["shoulder"]):
            hand = P(0.32, s * 0.28, z0 + 0.5)
            arm(b, sh, P(0.12, s * 0.33, z0 + 0.62), hand, "cloth_navy", cloak)
            b.rbox(hand + Vector((0, -0.02, 0)), (0.03, 0.05, 0.1), "leather", 0.4)
            b.rbox(hand + Vector((0, -0.02, 0.06)), (0.16 if star >= 3 else 0.12, 0.03, 0.03), trim, 0.5)
            if star >= 2:
                S(b, hand + Vector((0, -0.02, 0.1)), (0.022, 0.022, 0.022), trim, 8, 6)
            b.rbox(hand + Vector((0, -0.06, -blade * 0.6)), (0.015, 0.06, blade), GLOW, 0.3, rot=(-25, 0, 0))

    posed(b, body, lean=0.18)


def shadow_assassin(b, star=1):  # 夜莺 · 影刃刺客：兜帽面巾、围巾飘尾，双手反握发光匕首；★2 银扣，★3 金扣交叉带飞刀，★4 短披风，★5 紫黑配色
    star_base(b, 0.75, star)
    _shadow_assassin(b, star)


def _storm_mage(b, star):
    trim = metal(star, "cloth_navy")
    k = hero(b, body="white", robe="white", belt="gold", boots="royal" if star >= 5 else "cloth_navy",
             brows="steel_dark", head_r=0.25)
    h, hr, z0 = k["head"], k["head_r"], k["z0"]
    if star >= 5:  # 深蓝外袍披肩，金边
        cape(b, z0 + 0.88, z0 + 0.02, 0.22, 0.36, "royal", trim="gold", trim2="gold_light", f0=-0.1, flare=0.1)
    if star >= 2:  # 下摆饰边
        b.ring(P(0, 0, z0 + 0.1), 0.28, 0.018, trim, segments=32, sides=6)
    if star >= 3:  # 金色闪电纹
        for a in (-0.5, 0.5):
            o = P(0.27 * math.cos(a), 0.27 * math.sin(a), z0 + 0.18)
            b.chain([o, o + Vector((0, 0, 0.06)), o + Vector((0.02 * a, -0.01, 0.09)), o + Vector((0, 0, 0.15))],
                    0.01, 0.006, "gold", 6)
    # 前襟饰带和扣子
    b.rbox(P(0.2, 0, z0 + 0.32), (0.1, 0.04, 0.55), "royal" if star >= 5 else "cloth_navy", 0.4, rot=(0, -14, 0))
    if star >= 2:
        for s in (-1, 1):
            b.rbox(P(0.205, s * 0.055, z0 + 0.32), (0.015, 0.045, 0.55), trim, 0.4, rot=(0, -14, 0))
    if star >= 4:
        for i in range(3):
            gem(b, P(0.22 + 0.02 * i, 0, z0 + 0.15 + 0.12 * i), (0.022, 0.018, 0.03))
    else:
        rivets(b, [P(0.21 + 0.02 * i, 0, z0 + 0.15 + 0.12 * i) for i in range(3)], metal(star, "gold"))
    # 兜帽、银发、胡子
    S(b, h + Vector((0, hr * 0.3, 0.02)), (hr * 1.08, hr * 1.0, hr * 1.06), "royal" if star >= 5 else "cloth_navy",
      20, 12)
    if star >= 2:
        b.ring(h + Vector((0, -hr * 0.18, 0.02)), hr * 0.98, 0.014, trim, axis="f", segments=24, sides=4)
    S(b, h + Vector((0, -hr * 0.5, hr * 0.55)), (hr * 0.7, hr * 0.4, hr * 0.3), "steel", 14, 8)
    for dx, l in ((-0.06, 0.2), (0, 0.26), (0.06, 0.2)):
        R(b, P(-h.y + hr * 0.8, dx, h.z - hr * 0.45), P(-h.y + hr * 0.7, dx * 1.3, h.z - hr * 0.45 - l),
          0.05, 0.01, "steel", 10)
    if star >= 4:  # 额前的星饰
        gem(b, h + Vector((0, -hr * 0.95, hr * 0.45)), (0.03, 0.02, 0.045))
    # 立领
    b.ring(P(0, 0, z0 + 0.88), 0.17, 0.04, "gold", segments=20, sides=6)
    collar = "royal" if star >= 5 else "cloth_navy"
    for s in (-1, 1):
        b.rbox(P(-0.03, s * 0.14, z0 + 0.98), (0.12, 0.04, 0.22 if star >= 5 else 0.2), collar, 0.4,
               rot=(0, 0, s * 20))
        if star >= 2:
            b.rbox(P(-0.03, s * 0.16, z0 + 0.98), (0.13, 0.015, 0.23 if star >= 5 else 0.21), trim, 0.4,
                   rot=(0, 0, s * 20))
    # 披肩、袖口
    for sh in k["shoulder"]:
        S(b, sh + Vector((0, 0, 0.02)), (0.12, 0.13, 0.08), collar, 14, 8)
        if star >= 2:
            b.ring(sh + Vector((0, 0, 0.0)), 0.12, 0.012, trim, segments=20, sides=4)
    rs, ls = k["shoulder"]
    rh = P(0.15, 0.32, z0 + 0.62)
    arm(b, rs, P(0.05, 0.32, z0 + 0.65), rh, "white")
    lh = P(0.16, -0.4, z0 + 0.98)
    arm(b, ls, P(0.06, -0.4, z0 + 0.76), lh, "white")
    if star >= 2:
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
    if star >= 4:
        b.ring(top, 0.17, 0.012, "gold_light", axis="x", segments=20, sides=6)
    if star >= 5:
        b.ring(top, 0.19, 0.01, "gold_light", axis="z", segments=24, sides=6)
    gem(b, top, (0.05, 0.05, 0.09) if star < 4 else ((0.06, 0.06, 0.11) if star == 4 else (0.07, 0.07, 0.13)))
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


def storm_mage(b, star=1):  # 泽恩 · 风暴法师：白袍金立领、白胡子，金环法杖，左手托雷云；★2 银边，★3 金边刺绣，★4 宝石扣星饰，★5 深蓝外袍、符文环
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
    trim = "bone_dark" if star == 1 else ("silver" if star == 2 else "gold_dark")
    k = hero(b, body=robe, robe=robe, belt=trim, boots="crimson" if star >= 5 else "rot_dark", skin="bone",
             head_r=0.24, has_face=False)
    h, hr, z0 = k["head"], k["head_r"], k["z0"]
    if star >= 3:  # 破旧但华贵的长披风：★3 黑底暗金边，★5 深红里衬
        cape(b, z0 + 0.9, z0 + 0.02, 0.22, 0.38, "crimson" if star >= 5 else robe, trim=trim,
             trim2="gold" if star >= 4 else None, f0=-0.1, flare=0.16)
    _skull(b, h + Vector((0, -0.01, 0)), hr, crown="gold_dark" if star >= 5 else None)
    # 前襟饰带
    b.rbox(P(0.19, 0, z0 + 0.32), (0.08, 0.04, 0.55), "crimson" if star >= 5 else "rot_dark", 0.4, rot=(0, -14, 0))
    if star >= 2:
        for s in (-1, 1):
            b.rbox(P(0.195, s * 0.045, z0 + 0.32), (0.012, 0.045, 0.55), trim, 0.4, rot=(0, -14, 0))
        b.ring(P(0, 0, z0 + 0.12), 0.28, 0.018, trim, segments=32, sides=6)
    for i in range(3):
        S(b, P(0.21 + 0.02 * i, 0, z0 + 0.15 + 0.12 * i), (0.03, 0.03, 0.03), "bone", 8, 6)
    # 兜帽
    S(b, h + Vector((0, hr * 0.3, 0.04)), (hr * 1.15, hr * 1.05, hr * 1.12), robe, 20, 12)
    if star >= 2:
        b.ring(h + Vector((0, -hr * 0.2, 0.04)), hr * 1.0, 0.014, trim, axis="f", segments=24, sides=4)
    limb(b, [h + Vector((0, 0.15, hr * 0.8)), h + Vector((0, 0.32, hr * 0.8)), h + Vector((0, 0.45, hr * 0.3))],
         0.11, 0.02, robe, 12)
    # 骨刺领
    for i in range(9 if star >= 4 else 7):
        n = 8 if star >= 4 else 6
        a = math.pi * (-0.1 + 1.2 * i / n)
        o = P(-0.1 * math.sin(a) + 0.02, 0.2 * math.cos(a), z0 + 0.86)
        out = (o - P(0.02, 0, z0 + 0.86)).normalized()
        R(b, o, o + out * 0.1 + Vector((0, 0, 0.16 if star < 5 else 0.2)), 0.035, 0.005, "bone", 8)
    if star >= 3:  # 胸前的骷髅链
        for i in range(5):
            u = -1 + i / 2
            _skull(b, P(0.2 - 0.03 * abs(u), u * 0.13, z0 + 0.66 - 0.06 * (1 - abs(u))), 0.035, glow=False)
    for s, sh in zip((1, -1), k["shoulder"]):
        S(b, sh + Vector((0, 0, 0.02)), (0.13, 0.13, 0.08), trim if star >= 5 else "rot_dark", 14, 8)
        _skull(b, sh + Vector((0, -0.02, 0.1)), 0.075, glow=star >= 4)
        if star >= 2:
            b.ring(sh + Vector((0, 0, 0.0)), 0.13, 0.012, trim, segments=20, sides=4)
    rs, ls = k["shoulder"]
    rh = P(0.15, 0.32, z0 + 0.62)
    arm(b, rs, P(0.05, 0.32, z0 + 0.65), rh, robe, "bone")
    R(b, Vector((rh.x, rh.y, z0)), Vector((rh.x, rh.y, z0 + 1.42)), 0.025, 0.02, "bone_dark", 10)
    for dz in (0.3, 0.7, 1.1):
        S(b, Vector((rh.x, rh.y, z0 + dz)), (0.04, 0.04, 0.03), trim if star >= 3 else "bone", 10, 6)
    top = Vector((rh.x, rh.y, z0 + 1.55))
    _skull(b, top, 0.1, crown="gold_dark" if star >= 3 else None)
    horns = ((0.08, 0.17, 0.18),) if star < 4 else ((0.08, 0.17, 0.18), (0.06, 0.24, 0.3))
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


def necromancer(b, star=1):  # 维克多 · 死灵法师：骷髅头、尖兜帽、骨刺领、骷髅法杖、绿火骷髅；★2 银边，★3 暗金边披风骷髅链，★4 双角杖三颗骷髅，★5 金冠深红披风
    star_base(b, 0.75, star)
    _necromancer(b, star)
    for a in ((2.0, 4.2) if star < 4 else (1.8, 3.1, 4.4)):
        c = P(0.6 * math.cos(a), 0.6 * math.sin(a), 1.25)
        _skull(b, c, 0.09)
        for dz, r in ((0.1, 0.06), (0.18, 0.04), (0.25, 0.02)):
            S(b, c + Vector((0, 0.02, dz)), (r, r, r * 1.3), GLOW, 10, 6)
    if star >= 5:
        b.ring(P(0, 0, 0.35), 0.55, 0.008, GLOW, segments=40, sides=4)


def treant_guard(b, star=1):  # 老橡 · 树人守卫：树干身躯、枝条手臂、树冠；★2 藤蔓腰带，★3 宝石扣五瓣花，★4 符文环发光果实，★5 满树花冠和枝角
    star_base(b, 0.8, star, colour="bark")
    b.bone("spine", P(0, 0, 0.1), P(0, 0, 1.0))
    b.bone("head", P(0, 0, 1.0), P(0, 0, 2.0), "spine")  # 树冠
    for side, s in (("R", 1), ("L", -1)):
        b.bone("arm_upper." + side, P(0, s * 0.27, 0.95), P(0.12, s * 0.55, 0.8), "spine")
        b.bone("arm_lower." + side, P(0.12, s * 0.55, 0.8), P(0.3, s * 0.62, 0.42), "arm_upper." + side)
    with b.attach("root"):  # 树根扎在地上不动
        for i in range(6):
            a = i * 2 * math.pi / 6 + 0.3
            c, s = math.cos(a), math.sin(a)
            limb(b, [P(0.22 * c, 0.22 * s, 0.4), P(0.45 * c, 0.45 * s, 0.18), P(0.66 * c, 0.66 * s, 0.09)], 0.1,
                 0.03, "bark", 10)
    R(b, P(0, 0, 0.08), P(0, 0, 1.0), 0.36, 0.3, "bark", 20)
    S(b, P(0, 0, 1.0), (0.3, 0.3, 0.32), "bark", 20, 12)
    for i in range(10):
        a = i * 2 * math.pi / 10
        c, s = math.cos(a), math.sin(a)
        R(b, P(0.35 * c, 0.35 * s, 0.12), P(0.3 * c, 0.3 * s, 1.05), 0.035, 0.025, "wood", 8)
    if star >= 2:  # 藤蔓腰带，★3 起加紫宝石扣
        b.ring(P(0, 0, 0.55), 0.355, 0.03, "leaf", segments=32, sides=6)
        b.ring(P(0, 0, 0.55), 0.36, 0.012, "silver", segments=32, sides=4)
    if star >= 3:
        S(b, P(0.36, 0, 0.55), (0.06, 0.03, 0.06), "silver", 12, 8)
        gem(b, P(0.385, 0, 0.55), (0.035, 0.025, 0.05))
    if star >= 4:  # 树干上的银色符文环：★4 一道，★5 两道
        for z in ((0.3, 0.8) if star >= 5 else (0.8,)):
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
        if star >= 2:
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
        if star >= 2:  # 手腕上的银环
            b.ring(P(0.24, s * 0.6, 0.52), 0.075, 0.012, "silver", segments=16, sides=4)
    # 树冠
    S(b, P(-0.05, 0, 1.55), (0.5, 0.46, 0.36), "leaf", 20, 12)
    for f, x, z, r in ((0.25, 0.3, 1.6, 0.26), (-0.25, -0.32, 1.65, 0.28), (0.05, 0.05, 1.85, 0.3),
                       (-0.3, 0.3, 1.5, 0.24), (0.3, -0.25, 1.5, 0.22)):
        S(b, P(f, x, z), (r, r, r * 0.85), "leaf", 16, 10)
    flowers = [(0.45, 0.2, 1.6), (0.35, -0.38, 1.65), (0.2, 0.1, 2.0), (-0.1, 0.5, 1.7)]
    if star >= 2:
        flowers += [(0.5, -0.05, 1.45), (0.1, -0.45, 1.85)]
    if star >= 3:
        flowers += [(-0.2, 0.2, 2.05), (0.3, 0.4, 1.8)]
    if star >= 4:
        flowers += [(0.0, 0.55, 1.5), (-0.4, -0.2, 1.8)]
    if star >= 5:
        flowers += [(0.4, 0.05, 1.85), (-0.15, -0.5, 1.55), (0.25, -0.2, 2.05)]
    for f, x, z in flowers:
        for i in range(5 if star >= 3 else 1):  # ★3 起是五瓣花
            a = i * 2 * math.pi / 5
            d = 0.03 if star >= 3 else 0.0
            S(b, P(f, x + d * math.cos(a), z + d * math.sin(a)), (0.035 if star >= 3 else 0.05,) * 3,
              "elf_purple" if i % 2 == 0 else "white", 10, 6)
        S(b, P(f + 0.02, x, z + (0 if star >= 3 else 0.025)), (0.02, 0.02, 0.02), GLOW, 6, 4)
    if star >= 4:  # 垂下的发光果实
        for f, x in ((0.3, 0.35), (0.35, -0.3), (-0.2, 0.45), (-0.3, -0.4)):
            R(b, P(f, x, 1.38), P(f, x, 1.2), 0.008, 0.008, "wood", 6)
            gem(b, P(f, x, 1.15), (0.04, 0.04, 0.06))
    if star >= 5:  # 枝角花冠
        for s in (-1, 1):
            limb(b, [P(0, s * 0.2, 2.0), P(0.05, s * 0.32, 2.2), P(0.02, s * 0.45, 2.3)], 0.03, 0.008, "wood", 8)
            R(b, P(0.04, s * 0.3, 2.17), P(0.08, s * 0.25, 2.3), 0.015, 0.004, "wood", 6)


# ---- 卡通角色通用零件：发型、精灵耳、武器、按星级换装、坐骑 ------------------------------------

def hair(b, k, colour, style="short"):
    """头发：盖住后脑和头顶的发壳 + 刘海；style 可选 short / long / pony / bun / spiky。"""
    h, hr = k["head"], k["head_r"]
    S(b, h + Vector((0, hr * 0.18, hr * 0.25)), (hr * 1.04, hr * 1.0, hr * 0.85), colour, 20, 12)
    S(b, h + Vector((0, -hr * 0.55, hr * 0.55)), (hr * 0.75, hr * 0.35, hr * 0.3), colour, 14, 8)
    if style == "long":
        R(b, h + Vector((0, hr * 0.45, -hr * 0.1)), h + Vector((0, hr * 0.6, -hr * 1.7)), hr * 0.8, hr * 0.55, colour)
        for s in (-1, 1):  # 垂在耳前的两缕
            limb(b, [h + Vector((s * hr * 0.85, -hr * 0.1, 0)), h + Vector((s * hr * 0.9, -hr * 0.05, -hr * 0.9))],
                 hr * 0.2, hr * 0.12, colour, 10)
    elif style == "pony":
        limb(b, [h + Vector((0, hr * 0.85, hr * 0.35)), h + Vector((0, hr * 1.3, -hr * 0.2)),
                 h + Vector((0, hr * 1.25, -hr * 1.0))], hr * 0.32, hr * 0.1, colour, 12)
    elif style == "bun":
        S(b, h + Vector((0, hr * 0.55, hr * 0.9)), (hr * 0.42, hr * 0.42, hr * 0.42), colour, 14, 8)
    elif style == "spiky":
        for i in range(6):
            a = math.pi * (0.15 + 0.7 * i / 5)
            o = h + Vector((math.cos(a) * hr * 0.6, hr * 0.15, hr * 0.55 + math.sin(a) * hr * 0.2))
            R(b, o, o + Vector((math.cos(a) * hr * 0.4, hr * 0.35, hr * 0.55)), hr * 0.22, hr * 0.02, colour, 10)


def ears(b, k, skin):
    """精灵的尖耳朵。"""
    h, hr = k["head"], k["head_r"]
    for s in (-1, 1):
        R(b, h + Vector((s * hr * 0.85, 0, 0)), h + Vector((s * hr * 1.55, hr * 0.3, hr * 0.5)), hr * 0.16, hr * 0.02,
          skin, 10)


def beard(b, k, colour, length=0.22):
    h, hr = k["head"], k["head_r"]
    S(b, h + Vector((0, -hr * 0.62, -hr * 0.55)), (hr * 0.72, hr * 0.42, hr * 0.42), colour, 14, 8)
    R(b, h + Vector((0, -hr * 0.65, -hr * 0.7)), h + Vector((0, -hr * 0.75, -hr * 0.7 - length)), hr * 0.5, hr * 0.08,
      colour, 12)


def circlet(b, k, colour, jewel=None):
    h, hr = k["head"], k["head_r"]
    b.ring(h + Vector((0, hr * 0.1, hr * 0.45)), hr * 0.98, 0.014, colour, segments=24, sides=4)
    if jewel:
        gem(b, h + Vector((0, -hr * 0.92, hr * 0.5)), (0.025, 0.018, 0.035), jewel)


def dress(b, k, star, plain="steel_dark", pad=None, cape_colour=None, cape5=None, cape_from=3, emblem_at=None,
          cape_f0=-0.13):
    """按星级换装的通用部分：★2 起肩部和腰带饰边，★3 起胸前刺绣和披风，★4 徽章镶宝石、披风双边，★5 披风换华贵色。
    返回这一档的饰边金属色。"""
    t = metal(star, plain)
    z0 = k["z0"]
    if pad:
        for sh in k["shoulder"]:
            S(b, sh + Vector((0, 0, 0.03)), (0.11, 0.11, 0.07), pad, 14, 8)
            if star >= 2:
                b.ring(sh + Vector((0, 0, 0.02)), 0.11, 0.011, t, segments=20, sides=4)
    if star >= 2:
        mid = (k["chest"] + k["back"]) / 2
        b.ring(Vector((mid.x, mid.y, z0 + 0.52)), 0.205, 0.012, t, segments=24, sides=4)
    if star >= 3:
        emblem(b, emblem_at or P(0.2, 0, z0 + 0.7), 0.75, "gold", jewel=GLOW if star >= 4 else None)
    if cape_colour and star >= cape_from:
        cape(b, z0 + 0.86, z0 + 0.06, 0.2, 0.32, cape5 if (cape5 and star >= 5) else cape_colour, trim="gold",
             trim2="gold_light" if star >= 4 else None, f0=cape_f0)
        for sh in k["shoulder"]:
            clasp(b, sh + Vector((0, -0.04, 0.02)), s=0.7)
    return t


@held_weapon
def sword(b, hand, star, length=0.42, blade="white", guard=None, glow_from=5, tilt=62):
    """斜举向前上方的剑。"""
    g = guard or metal(star, "steel_dark")
    tip = hand + Vector((0, -math.sin(math.radians(tilt)) * length, math.cos(math.radians(tilt)) * length))
    mid = (hand + tip) / 2 + Vector((0, -0.04, 0.02))
    b.rbox(mid, (0.02, 0.065, length), blade, 0.3, rot=(tilt, 0, 0))
    if star >= glow_from:
        b.rbox(mid + Vector((0, -0.005, 0)), (0.026, 0.022, length * 0.8), GLOW, 0.4, rot=(tilt, 0, 0))
    b.rbox(hand + Vector((0, -0.05, 0.03)), (0.24 if star >= 3 else 0.18, 0.04, 0.04), g, 0.5, rot=(tilt, 0, 0))
    S(b, hand + Vector((0, 0.07, -0.03)), (0.032, 0.032, 0.032), g, 10, 6)
    if star >= 4:
        gem(b, hand + Vector((0, -0.07, 0.035)), (0.02, 0.016, 0.03))


@held_weapon
def bow(b, hand, star, wood="wood", size=0.36, string="white", tips=None):
    """竖握在左手的弓，弓臂向前弯。"""
    t = tips or metal(star, wood)
    pts = [hand + Vector((0, 0.02, -size)), hand + Vector((0, -0.07, -size * 0.55)), hand,
           hand + Vector((0, -0.07, size * 0.55)), hand + Vector((0, 0.02, size))]
    b.chain(pts, 0.022, 0.022, "gold" if star >= 5 else wood, 8)
    R(b, pts[0], pts[-1], 0.004, 0.004, string, 4)
    for p in (pts[0], pts[-1]):
        S(b, p, (0.025, 0.025, 0.025), t, 8, 6)
    if star >= 3:
        b.ring(hand, 0.03, 0.01, "gold", axis="z", segments=12, sides=4)
    if star >= 4:  # 搭在弦上的发光箭
        R(b, hand + Vector((0, 0.1, 0)), hand + Vector((0, -0.25, 0)), 0.008, 0.008, GLOW, 6)


@held_weapon
def staff(b, hand, z0, star, wood="wood", height=1.45):
    """立在地上的法杖，按星级加金属箍；返回杖顶位置。"""
    base_p = Vector((hand.x, hand.y, z0))
    top = Vector((hand.x, hand.y, z0 + height))
    R(b, base_p, top, 0.025, 0.022, "gold" if star >= 5 else wood, 10)
    bands = {1: (), 2: (0.95,), 3: (0.35, 0.95), 4: (0.35, 0.65, 0.95), 5: (0.35, 0.65, 0.95, 1.25)}[star]
    for dz in bands:
        R(b, Vector((hand.x, hand.y, z0 + dz)), Vector((hand.x, hand.y, z0 + dz + 0.05)), 0.034, 0.034,
          "royal" if star >= 5 else metal(star, wood), 10)
    return top


@held_weapon
def spear(b, hand, star, length=1.3, shaft="wood", pennant=None):
    """斜指前方的长枪；★3 起挂小旗。"""
    back = hand + Vector((0, 0.45, -0.25))
    d = (hand - back).normalized()
    tip = back + d * length
    R(b, back, tip, 0.02, 0.018, shaft, 10)
    head = "gold" if star >= 5 else ("silver" if star >= 2 else "steel")
    R(b, tip, tip + d * 0.2, 0.05, 0.003, head, 8)
    S(b, tip, (0.035, 0.035, 0.035), metal(star, "steel_dark"), 10, 6)
    if pennant and star >= 3:  # 枪头下的燕尾小旗
        n = Vector((0, -d.z, d.y))  # 垂直于枪杆、朝下
        ang = math.degrees(math.atan2(d.z, d.y))
        for i in range(2):
            b.rbox(tip - d * (0.13 + i * 0.02) + n * (0.04 + i * 0.05), (0.01, 0.2 - i * 0.05, 0.05), pennant, 0.3,
                   rot=(ang, 0, 0))
    if star >= 4:
        b.ring(tip - d * 0.02, 0.04, 0.008, "gold", segments=12, sides=4)
    return tip


def bird(b, z, body="griffin", wing="feather", head_c="feather", beak="gold", lion=True, star=1, span=0.75):
    """卡通飞行坐骑（狮鹫或巨鹰）：椭球身体、昂起的鸟头和尖喙、羽片展开的大翅膀；返回鞍座位置。"""
    t = metal(star, "leather")
    head = P(0.56, 0, z + 0.36)
    mount = b.bone("mount", P(0.3, 0, z), P(-0.45, 0, z))
    neck = b.bone("mount_head", P(0.38, 0, z + 0.12), head + Vector((0, 0, 0.16)), mount)
    b.bone("mount_tail", P(-0.45, 0, z), P(-0.8, 0, z + 0.1), mount)
    for side, s in (("R", 1), ("L", -1)):
        o = P(0.06, s * 0.2, z + 0.16)
        b.bone("wing." + side, o, o + Vector((s * span * 0.8, 0.25, span * 0.45)), mount)
    S(b, P(0, 0, z), (0.25, 0.5, 0.24), body, 18, 10)
    S(b, P(0.3, 0, z + 0.08), (0.2, 0.2, 0.2), head_c, 16, 10)  # 胸前羽毛
    with b.attach(neck):
        R(b, P(0.38, 0, z + 0.12), head, 0.14, 0.12, head_c, 14)
        S(b, head, (0.16, 0.17, 0.16), head_c, 16, 10)
        R(b, head + Vector((0, -0.13, -0.01)), head + Vector((0, -0.3, -0.08)), 0.06, 0.004, beak, 10)
    for s in (-1, 1):
        with b.attach(neck):
            S(b, head + Vector((s * 0.085, -0.1, 0.04)), (0.028, 0.02, 0.036), "iron", 8, 6)
            S(b, head + Vector((s * 0.09, -0.115, 0.055)), (0.01, 0.008, 0.01), "white", 6, 4)
            R(b, head + Vector((s * 0.06, 0.1, 0.1)), head + Vector((s * 0.09, 0.25, 0.16)), 0.035, 0.006, head_c,
              8)
        if lion:  # 狮鹫的前爪、后腿
            for f in (0.26, -0.28):
                limb(b, [P(f, s * 0.13, z - 0.1), P(f + 0.05, s * 0.14, z - 0.24), P(f + 0.11, s * 0.14, z - 0.3)],
                     0.055, 0.04, body if f < 0 else head_c, 10)
        # 翅膀：翅根一块圆鼓的覆羽，外面六根飞羽向上后方展开
        o = P(0.06, s * 0.2, z + 0.16)
        with b.attach("wing." + ("R" if s > 0 else "L")):
            S(b, o + Vector((s * 0.2, 0.08, 0.14)), (0.26, 0.2, 0.07), wing, 16, 8, rot=(0, -s * 35, 0))
            for i in range(6):
                el = math.radians(22 + i * 5)
                ln = span * (1.0 - 0.06 * i)
                end = o + Vector((s * ln * math.cos(el), 0.1 + i * 0.06, ln * math.sin(el)))
                R(b, o + Vector((s * 0.12, 0.03 + i * 0.04, 0.08)), end, 0.075, 0.035, wing, 10)
            el = math.radians(32)  # 飞羽之间的翼面，填满羽毛的缝
            S(b, o + Vector((s * span * 0.42 * math.cos(el), 0.2, span * 0.42 * math.sin(el))),
              (span * 0.48, 0.2, 0.035), wing, 18, 8, rot=(0, -s * 32, 0))
            if star >= 3:  # 翅根金饰
                b.ring(o + Vector((s * 0.1, 0.04, 0.08)), 0.07, 0.012, "gold", axis="x", segments=12, sides=4)
    with b.attach("mount_tail"):
        R(b, P(-0.45, 0, z), P(-0.8, 0, z + 0.1), 0.07, 0.012, body if lion else wing, 10)  # 尾巴
        if lion:
            S(b, P(-0.82, 0, z + 0.1), (0.06, 0.06, 0.06), head_c, 8, 6)
    # 鞍和缰绳
    seat = P(-0.06, 0, z + 0.2)
    b.rbox(seat + Vector((0, 0, -0.02)), (0.3, 0.28, 0.06), "leather" if star < 3 else "royal", 0.5)
    b.ring(seat + Vector((0, 0, -0.03)), 0.16, 0.014, t, segments=16, sides=4)
    with b.attach(neck):
        if star >= 4:
            gem(b, head + Vector((0, -0.15, 0.08)), (0.03, 0.02, 0.045))  # 额前宝石
        if star >= 5:
            b.ring(head + Vector((0, 0, 0.06)), 0.15, 0.014, "gold", segments=16, sides=4)
    return seat


def seated(b, seat, body, legs="leather", boots="leather", skin="skin", belt="leather", head_r=0.21, brows=None,
           eye="iron"):
    """骑在坐骑上的卡通人形：腿分开跨在鞍两侧，其余和 hero 相同；返回同样的关键点。"""
    z0 = seat.z - 0.45
    f = -seat.y
    for s in (-1, 1):
        foot = P(f + 0.06, s * 0.27, seat.z - 0.24)
        limb(b, [P(f, s * 0.1, seat.z + 0.02), P(f + 0.12, s * 0.25, seat.z - 0.04), foot], 0.065, 0.06, legs, 12)
        b.rbox(foot + Vector((0, -0.04, -0.03)), (0.12, 0.18, 0.11), boots, 0.45)
    S(b, P(f, 0, z0 + 0.45), (0.19, 0.15, 0.1), legs)
    S(b, P(f, 0, z0 + 0.68), (0.21, 0.16, 0.24), body)
    R(b, P(f, 0, z0 + 0.48), P(f, 0, z0 + 0.56), 0.2, 0.2, belt)
    b.rbox(P(f + 0.19, 0, z0 + 0.52), (0.09, 0.03, 0.08), "gold", 0.4)
    head = P(f, 0, z0 + 0.88 + head_r * 0.9)
    S(b, head, (head_r, head_r * 0.95, head_r * 0.95), skin, 20, 12)
    face(b, head, head_r, eye=eye, brows=brows)
    humanoid_bones(b, z0, head, head_r, legs=False, parent="mount" if b.has_bone("mount") else "root", f=f)
    return {
        "head": head, "head_r": head_r,
        "shoulder": (P(f, 0.22, z0 + 0.82), P(f, -0.22, z0 + 0.82)),
        "chest": P(f + 0.15, 0, z0 + 0.7), "back": P(f - 0.15, 0, z0 + 0.72), "z0": z0,
    }


# ---- 晨星骑士团 ----------------------------------------------------------------------

def priest(b, star=1):  # 艾琳 · 牧师：白袍蓝圣带、金色长发，右手日轮法杖，左手捧圣典；★2 银饰，★3 金边披风，★4 光环，★5 深蓝外袍、日轮光芒
    star_base(b, 0.75, star)
    blue = "royal" if star >= 5 else "cloth_blue"
    trim = metal(star, "cloth_navy")
    k = hero(b, body="white", robe="white", belt=blue, boots=blue, brows="gold_dark", head_r=0.25)
    h, hr, z0 = k["head"], k["head_r"], k["z0"]
    dress(b, k, star, plain="cloth_navy", cape_colour="cloth_blue", cape5="royal", emblem_at=P(0.2, 0, z0 + 0.74))
    hair(b, k, "hair_blond", "long")
    if star >= 2:
        circlet(b, k, metal(star, "silver"), jewel=GLOW if star >= 4 else None)
        b.ring(P(0, 0, z0 + 0.1), 0.28, 0.018, trim, segments=32, sides=6)
    for s in (-1, 1):  # 垂在身前的圣带
        b.rbox(P(0.2, s * 0.07, z0 + 0.36), (0.07, 0.035, 0.62), blue, 0.4, rot=(0, -12, 0))
        if star >= 2:
            b.rbox(P(0.207, s * 0.07, z0 + 0.09), (0.075, 0.04, 0.05), trim, 0.4)
        if star >= 3:
            b.ring(P(0.205, s * 0.07, z0 + 0.4), 0.025, 0.006, "gold", axis="f", segments=12, sides=4)
    S(b, P(0, 0, z0 + 0.86), (0.25, 0.21, 0.08), blue, 20, 8)  # 小披肩
    if star >= 2:
        b.ring(P(0, 0, z0 + 0.83), 0.245, 0.012, trim, segments=28, sides=4)
    rs, ls = k["shoulder"]
    rh = P(0.15, 0.32, z0 + 0.62)
    arm(b, rs, P(0.05, 0.32, z0 + 0.65), rh, "white")
    top = staff(b, rh, z0, star, height=1.4)
    sun = top + Vector((0, 0, 0.13))
    b.ring(sun, 0.11, 0.02, "gold" if star >= 3 else metal(star, "steel"), axis="f", segments=20, sides=6)
    gem(b, sun, (0.045, 0.045, 0.07) if star < 4 else (0.055, 0.055, 0.085))
    for i in range(8 if star >= 4 else 4):  # 日轮光芒
        a = i * 2 * math.pi / (8 if star >= 4 else 4) + math.pi / 4
        d = Vector((math.cos(a), 0, math.sin(a)))
        R(b, sun + d * 0.12, sun + d * (0.2 if star >= 5 else 0.17), 0.016, 0.003, "gold" if star >= 3 else "steel",
          6)
    lh = P(0.24, -0.16, z0 + 0.7)
    arm(b, ls, P(0.1, -0.34, z0 + 0.62), lh, "white")
    book = lh + Vector((0, -0.06, 0.04))
    b.rbox(book, (0.24, 0.07, 0.17), "crimson" if star >= 5 else "cloth_navy", 0.25, rot=(-35, 0, 0))
    b.rbox(book + Vector((0, -0.012, 0.012)), (0.22, 0.06, 0.155), "white", 0.2, rot=(-35, 0, 0))
    if star >= 2:  # 书角包边
        for dx in (-0.11, 0.11):
            S(b, book + Vector((dx, 0.0, 0.0)), (0.022, 0.04, 0.05), metal(star, "silver"), 8, 6)
    if star >= 3:  # 书页上方的光点
        for dx, dz, r in ((0, 0.14, 0.03), (-0.06, 0.2, 0.018), (0.06, 0.22, 0.015)):
            S(b, book + Vector((dx, -0.03, dz)), (r, r, r), GLOW, 8, 6)
    if star >= 4:  # 头顶光环
        b.ring(h + Vector((0, 0.08, hr * 1.35)), hr * 0.75, 0.014, "gold_light", segments=28, sides=6)


def lancer(b, star=1):  # 塞德里克 · 长枪手：棉甲胸甲、宽檐铁盔，长枪斜指前方，左臂小圆盾；★2 银饰，★3 枪旗披风，★4 宝石盾心，★5 白金甲
    star_base(b, 0.75, star)
    armor = "white" if star >= 5 else "steel"

    def body():
        k = hero(b, legs="cloth_navy", boots="leather", body="cloth_blue", brows="hair_brown", head_r=0.24)
        h, hr, z0 = k["head"], k["head_r"], k["z0"]
        dress(b, k, star, pad=armor, cape_colour="cloth_blue", cape5="royal", emblem_at=P(0.21, 0, z0 + 0.72))
        S(b, P(0.03, 0, z0 + 0.72), (0.2, 0.15, 0.19), armor, 18, 10)  # 胸甲
        if star >= 2:
            b.ring(P(0.03, 0, z0 + 0.6), 0.19, 0.012, metal(star, "steel_dark"), segments=24, sides=4)
        for sd in (-1, 1):  # 盔下露出的鬓发
            S(b, h + Vector((sd * hr * 0.85, hr * 0.15, hr * 0.15)), (hr * 0.25, hr * 0.4, hr * 0.35), "hair_brown",
              10, 6)
        helm = h + Vector((0, 0.03, hr * 0.55))
        S(b, helm, (hr * 1.04, hr * 1.04, hr * 0.62), armor, 20, 10)
        b.ring(helm + Vector((0, 0, -hr * 0.08)), hr * 1.22, 0.03, metal(star, "steel_dark"), segments=28, sides=6)
        R(b, helm + Vector((0, 0, hr * 0.55)), helm + Vector((0, 0, hr * 0.8)), 0.022, 0.012, metal(star, "steel"),
          8)
        if star >= 3:  # 盔顶短羽
            limb(b, [helm + Vector((0, 0, hr * 0.8)), helm + Vector((0, 0.08, hr * 1.0)),
                     helm + Vector((0, 0.2, hr * 0.85))], 0.045, 0.015, "white" if star >= 5 else "cloth_blue", 8)
        rs, ls = k["shoulder"]
        rh = P(0.3, 0.2, z0 + 0.62)
        arm(b, rs, P(0.1, 0.32, z0 + 0.58), rh, "cloth_blue", "leather" if star == 1 else metal(star, ""))
        spear(b, rh, star, pennant="royal" if star >= 5 else "cloth_blue")
        lh = P(0.24, -0.32, z0 + 0.6)
        arm(b, ls, P(0.1, -0.36, z0 + 0.62), lh, "cloth_blue", "leather")
        sc = lh + Vector((-0.02, -0.06, 0))
        S(b, sc, (0.19, 0.045, 0.19), "white" if star >= 3 else "wood", 20, 8)
        b.ring(sc + Vector((0, -0.01, 0)), 0.185, 0.02, metal(star, "steel_dark"), axis="f", segments=24, sides=6)
        S(b, sc + Vector((0, -0.04, 0)), (0.05, 0.03, 0.05), metal(star, "steel"), 12, 6)
        if star >= 4:
            gem(b, sc + Vector((0, -0.07, 0)), (0.03, 0.02, 0.04))
        if star >= 3:
            for i in range(4):
                a = i * math.pi / 2 + math.pi / 4
                S(b, sc + Vector((0.12 * math.cos(a), -0.03, 0.12 * math.sin(a))), (0.018, 0.015, 0.018), "gold", 8,
                  6)

    posed(b, body, lean=0.08)


def banner_bearer(b, star=1):  # 奥莉薇亚 · 旗手：红发马尾、胸甲短裙，右手高举骑士团战旗；★2 银边旗，★3 金穗旗徽，★4 宝石旗顶，★5 深蓝金纹大旗
    star_base(b, 0.75, star)
    armor = "white" if star >= 5 else "steel"
    flag_c = "royal" if star >= 5 else "cloth_blue"
    k = hero(b, legs="cloth_navy", boots="leather", body="cloth_blue", brows="hair_red", head_r=0.25)
    h, hr, z0 = k["head"], k["head_r"], k["z0"]
    dress(b, k, star, pad=armor, cape_colour="cloth_blue", cape5="royal", emblem_at=P(0.21, 0, z0 + 0.72))
    S(b, P(0.03, 0, z0 + 0.72), (0.2, 0.15, 0.19), armor, 18, 10)
    R(b, P(0, 0, z0 + 0.3), P(0, 0, z0 + 0.5), 0.27, 0.2, "cloth_blue")  # 短裙
    if star >= 2:
        b.ring(P(0, 0, z0 + 0.31), 0.265, 0.014, metal(star, ""), segments=28, sides=4)
    hair(b, k, "hair_red", "pony")
    if star >= 2:
        b.ring(h + Vector((0, hr * 0.9, hr * 0.32)), 0.04, 0.012, metal(star, ""), axis="f", segments=12, sides=4)
    rs, ls = k["shoulder"]
    rh = P(0.12, 0.3, z0 + 0.95)
    arm(b, rs, P(0.0, 0.34, z0 + 0.75), rh, "cloth_blue", "leather")
    with b.attach("arm_lower.R"):
        pole_top = z0 + 2.0
        R(b, Vector((rh.x, rh.y, z0 + 0.25)), Vector((rh.x, rh.y, pole_top)), 0.022, 0.02, "gold" if star >= 5 else "wood",
          10)
        b.rbox(Vector((rh.x, rh.y + 0.0, pole_top - 0.05)), (0.025, 0.55, 0.03), metal(star, "wood"), 0.4)  # 横杆
        fc = Vector((rh.x, rh.y + 0.0, pole_top - 0.36))
        b.rbox(fc, (0.03, 0.5, 0.6), flag_c, 0.25)
        for dz in (0.27, -0.27):  # 旗边
            b.rbox(fc + Vector((0, 0, dz)), (0.04, 0.52 if star >= 2 else 0.5, 0.03), metal(star, "cloth_navy"), 0.4)
        if star >= 2:
            for dy in (0.24, -0.24):
                b.rbox(fc + Vector((0, dy, 0)), (0.04, 0.03, 0.56), metal(star, "cloth_navy"), 0.4)
        # 旗徽：两面都有
        for sx in (1, -1):
            c = fc + Vector((sx * 0.02, 0, 0.03))
            b.ring(c, 0.11, 0.018, "gold" if star >= 3 else "white", axis="x", segments=20, sides=6)
            gem(b, c, (0.035, 0.04, 0.05) if star >= 3 else (0.025, 0.03, 0.04), "gold" if star < 3 else GLOW)
        if star >= 3:  # 下沿金穗
            for i in range(5):
                dy = -0.2 + i * 0.1
                R(b, fc + Vector((0, dy, -0.29)), fc + Vector((0, dy, -0.38)), 0.012, 0.006, "gold", 6)
        tip = Vector((rh.x, rh.y, pole_top + 0.02))
        if star >= 4:
            S(b, tip, (0.045, 0.045, 0.045), "gold", 12, 8)
            gem(b, tip + Vector((0, 0, 0.09)), (0.035, 0.035, 0.07))
        else:
            R(b, tip, tip + Vector((0, 0, 0.14)), 0.04, 0.003, metal(star, "steel"), 8)
        if star >= 5:
            b.ring(tip + Vector((0, 0, 0.09)), 0.08, 0.01, "gold_light", axis="x", segments=16, sides=4)
    lh = P(0.24, -0.26, z0 + 0.52)
    arm(b, ls, P(0.08, -0.33, z0 + 0.62), lh, "cloth_blue", "leather")
    sword(b, lh, star, length=0.34)


def ballista(b, star=1):  # 哈罗德 · 弩炮手：大胡子老兵、护目铁盔，守着一架仰射的小型守城弩；★2 银箍，★3 金饰弩臂，★4 发光弩矢，★5 金弩臂、蓝绸弩身
    star_base(b, 0.75, star)
    t = metal(star, "steel_dark")
    k = hero(b, legs="cloth_navy", boots="leather", body="steel_dark", belt="leather", brows="steel", head_r=0.24)
    h, hr, z0 = k["head"], k["head_r"], k["z0"]
    dress(b, k, star, pad="steel", cape_colour="cloth_navy", cape5="royal", emblem_at=P(0.22, 0, z0 + 0.66))
    b.rbox(P(0.17, 0, z0 + 0.48), (0.28, 0.05, 0.4), "leather", 0.35)  # 皮围裙
    beard(b, k, "steel", 0.16)
    S(b, h + Vector((0, 0.05, hr * 0.4)), (hr * 1.06, hr * 1.06, hr * 0.72), "steel", 20, 10)
    b.ring(h + Vector((0, 0.05, hr * 0.3)), hr * 1.04, 0.026, t, segments=24, sides=6)
    R(b, h + Vector((0, -hr * 0.95, hr * 0.5)), h + Vector((0, -hr * 1.05, hr * 0.05)), 0.03, 0.025, t, 8)  # 护鼻
    # 守城弩：三脚架、仰角 40° 的弩身、弩臂和弩矢
    c = P(0.42, -0.3, z0 + 0.45)
    for a in (0.0, 2.1, 4.2):
        R(b, c, c + Vector((0.2 * math.sin(a), 0.2 * math.cos(a), -0.5)), 0.025, 0.02, "wood", 8)
    S(b, c, (0.05, 0.05, 0.05), t, 10, 6)
    stock = "royal" if star >= 5 else "wood"
    b.rbox(c + Vector((0, -0.05, 0.04)), (0.07, 0.6, 0.06), stock, 0.35, rot=(140, 0, 0))
    up = Vector((0, -math.cos(math.radians(40)), math.sin(math.radians(40))))
    front = c + Vector((0, -0.05, 0.04)) + up * 0.26
    arm_c = "gold" if star >= 5 else "steel_dark"
    tips = []
    for s in (-1, 1):
        pts = [front, front + Vector((s * 0.16, 0.02, 0.0)), front + Vector((s * 0.3, 0.1, -0.06))]
        b.chain(pts, 0.04, 0.025, arm_c, 8)
        tips.append(pts[-1])
        if star >= 3:
            S(b, pts[-1], (0.025, 0.025, 0.025), "gold", 8, 6)
    nock = front - up * 0.3
    b.chain([tips[0], nock, tips[1]], 0.006, 0.006, "white", 4)
    bolt = GLOW if star >= 4 else "wood"
    R(b, nock, front + up * 0.12, 0.014, 0.014, bolt, 6)
    R(b, front + up * 0.12, front + up * 0.22, 0.03, 0.003, "steel", 8)
    for d in (0.1, 0.4):
        b.ring(c + Vector((0, -0.05, 0.04)) + up * (d - 0.25), 0.055, 0.012, t, axis="f", segments=12, sides=4)
    rs, ls = k["shoulder"]
    lh = nock + Vector((0.02, 0.04, -0.02))
    arm(b, ls, P(0.12, -0.34, z0 + 0.6), lh, "steel_dark", "leather")
    rh = P(0.2, 0.3, z0 + 0.5)
    arm(b, rs, P(0.05, 0.3, z0 + 0.62), rh, "steel_dark", "leather")
    R(b, rh + Vector((0, 0.05, -0.2)), rh + Vector((0, -0.05, 0.25)), 0.014, 0.014, "wood", 6)  # 备用弩矢
    R(b, rh + Vector((0, -0.05, 0.25)), rh + Vector((0, -0.06, 0.33)), 0.03, 0.003, "steel", 8)
    # 背后的箭筒
    R(b, P(-0.18, 0.08, z0 + 0.4), P(-0.22, 0.12, z0 + 0.9), 0.07, 0.07, "leather", 12)
    for i in range(3):
        R(b, P(-0.21, 0.08 + i * 0.03, z0 + 0.88), P(-0.23, 0.08 + i * 0.03, z0 + 1.02), 0.012, 0.012, "wood", 6)
    if star >= 2:
        b.ring(P(-0.21, 0.11, z0 + 0.8), 0.072, 0.01, t, segments=16, sides=4)


def griffin_squire(b, star=1):  # 菲恩 · 狮鹫侍从（悬空）：骑小狮鹫的少年，刺猬头、长围巾；★2 银鞍扣，★3 金翼饰短披风，★4 额前宝石，★5 金冠狮鹫、蓝金鞍
    seat = bird(b, 0.42, star=star, span=0.7)
    k = seated(b, seat, "cloth_blue", legs="cloth_navy", boots="leather", brows="gold_dark", head_r=0.2)
    h, hr, z0 = k["head"], k["head_r"], k["z0"]
    dress(b, k, star, pad="leather", cape_colour="cloth_blue", cape5="royal", emblem_at=P(0.14, 0, z0 + 0.72),
          cape_f0=-0.19)
    S(b, P(-0.02, 0, z0 + 0.7), (0.19, 0.15, 0.17), "leather", 16, 8)  # 皮甲
    hair(b, k, "hair_blond", "spiky")
    scarf = "crimson"
    b.ring(P(-0.04, 0, z0 + 0.86), 0.15, 0.04, scarf, segments=20, sides=8)
    limb(b, [P(-0.16, 0.05, z0 + 0.86), P(-0.36, 0.1, z0 + 0.9), P(-0.56, 0.12, z0 + 0.84)], 0.04, 0.02, scarf, 10)
    # 飞行护目镜
    b.ring(h + Vector((0, 0.02, hr * 0.55)), hr * 1.0, 0.02, "leather", segments=24, sides=4)
    for s in (-1, 1):
        b.ring(h + Vector((s * hr * 0.36, -hr * 0.85, hr * 0.6)), 0.04, 0.012, metal(star, "steel_dark"), axis="f",
               segments=12, sides=4)
    for s, sh in zip((1, -1), k["shoulder"]):  # 双手握缰绳
        hand = P(0.24, s * 0.12, z0 + 0.62)
        arm(b, sh, P(0.1, s * 0.28, z0 + 0.6), hand, "cloth_blue", "leather")
        R(b, hand, P(0.5, s * 0.08, 0.74), 0.008, 0.008, "leather", 4)


def _spirit_knight(b, star):
    armor = "ghost_blue"
    trim = metal(star, "steel")
    k = hero(b, legs="cloth_navy", boots=armor, body=armor, belt="cloth_navy", brows="white", head_r=0.25)
    h, hr, z0 = k["head"], k["head_r"], k["z0"]
    cape(b, z0 + 0.86, z0 + 0.0, 0.22, 0.38, "royal" if star >= 5 else "cloth_navy",
         trim=metal(star, "steel") if star < 3 else "gold", trim2="gold_light" if star >= 4 else None)
    for sh in k["shoulder"]:
        clasp(b, sh + Vector((0, -0.04, 0.02)), s=0.8)
    for f in (0.17, -0.17):  # 罩袍
        b.rbox(P(f, 0, z0 + 0.4), (0.26, 0.04, 0.32), "royal" if star >= 5 else "cloth_blue", 0.3)
        b.rbox(P(f, 0, z0 + 0.25), (0.27, 0.045, 0.03), trim, 0.4)
    if star >= 2:
        b.ring(P(0, 0, z0 + 0.52), 0.205, 0.012, trim, segments=24, sides=4)
    if star >= 3:
        emblem(b, P(0.205, 0, z0 + 0.42), 0.9, "gold", jewel=GLOW if star >= 4 else None)
    beard(b, k, "white", 0.3)
    for s in (-1, 1):  # 八字胡
        R(b, h + Vector((s * 0.02, -hr * 0.92, -hr * 0.3)), h + Vector((s * hr * 0.6, -hr * 0.8, -hr * 0.45)),
          0.035, 0.012, "white", 8)
    # 头盔：开面盔加护颊、顶冠
    S(b, h + Vector((0, hr * 0.12, hr * 0.3)), (hr * 1.06, hr * 1.04, hr * 0.88), armor, 20, 12)
    b.ring(h + Vector((0, hr * 0.05, hr * 0.22)), hr * 1.02, 0.03, trim, segments=24, sides=6)
    crest = "gold" if star >= 3 else trim
    b.rbox(h + Vector((0, hr * 0.3, hr * 1.05)), (0.035, hr * 1.2, 0.12), crest, 0.4)
    if star >= 5:
        for i in range(5):
            a = math.pi * (0.1 + 0.8 * i / 4)
            o = h + Vector((hr * 0.95 * math.cos(a), -hr * 0.6 * math.sin(a), hr * 0.62))
            R(b, o, o + Vector((0, 0, 0.08 if i == 2 else 0.05)), 0.018, 0.004, "gold_light", 6)
    for s, sh in zip((1, -1), k["shoulder"]):
        S(b, sh + Vector((0, 0, 0.04)), (0.14, 0.13, 0.09), armor, 14, 8)
        b.ring(sh + Vector((0, 0, 0.02)), 0.13, 0.014, trim, segments=20, sides=4)
        if star >= 4:
            gem(b, sh + Vector((0, -0.1, 0.06)), (0.022, 0.018, 0.03))
    # 双手按在身前拄地的大剑上
    with b.attach("arm_lower.R"):
        sx = P(0.36, 0, 0)
        R(b, Vector((sx.x, sx.y, z0 + 0.02)), Vector((sx.x, sx.y, z0 + 0.6)), 0.03, 0.055, "white", 4)
        if star >= 5:
            R(b, Vector((sx.x, sx.y - 0.01, z0 + 0.1)), Vector((sx.x, sx.y - 0.01, z0 + 0.56)), 0.02, 0.03, GLOW, 4)
        b.rbox(Vector((sx.x, sx.y, z0 + 0.62)), (0.34 if star >= 3 else 0.28, 0.05, 0.05), crest, 0.5)
        R(b, Vector((sx.x, sx.y, z0 + 0.64)), Vector((sx.x, sx.y, z0 + 0.76)), 0.022, 0.022, "leather", 8)
        S(b, Vector((sx.x, sx.y, z0 + 0.79)), (0.04, 0.04, 0.04), crest, 10, 6)
        if star >= 4:
            gem(b, Vector((sx.x, sx.y - 0.03, z0 + 0.62)), (0.025, 0.02, 0.035))
    for s, sh in zip((1, -1), k["shoulder"]):
        hand = Vector((sx.x + s * 0.05, sx.y - 0.0, z0 + 0.72))
        arm(b, sh, P(0.15, s * 0.3, z0 + 0.66), hand, armor, armor)
    with b.attach("root"):
        # 英灵：脚下发光的光环和飘浮的光点
        b.ring(P(0, 0, z0 + 0.05), 0.32, 0.012, GLOW, segments=32, sides=4)
        for i in range(3 if star < 3 else 6):
            a = i * 2 * math.pi / (3 if star < 3 else 6) + 0.4
            S(b, P(0.42 * math.cos(a), 0.42 * math.sin(a), z0 + 0.3 + 0.25 * (i % 3)), (0.025, 0.025, 0.025), GLOW, 8, 6)


def spirit_knight(b, star=1):  # 雷恩爵士 · 英灵（2×2）：淡蓝铠甲的老骑士，白胡子，双手拄大剑；★2 银饰，★3 金冠饰刺绣，★4 宝石，★5 金冠、发光剑刃
    star_base(b, 1.6, star)
    scaled(b, 1.9, lambda: _spirit_knight(b, star))


# ---- 边境游侠会 ----------------------------------------------------------------------

def hood(b, k, colour, star, trim_plain="leather", up=True):
    """游侠兜帽：up 为戴在头上（露出脸），否则垂在肩后；★2 起帽沿饰边。"""
    h, hr, z0 = k["head"], k["head_r"], k["z0"]
    t = metal(star, trim_plain)
    if up:
        S(b, h + Vector((0, hr * 0.3, hr * 0.12)), (hr * 1.1, hr * 1.04, hr * 1.08), colour, 20, 12)
        limb(b, [h + Vector((0, hr * 0.6, hr * 0.7)), h + Vector((0, hr * 1.2, hr * 0.5)),
                 h + Vector((0, hr * 1.6, 0))], hr * 0.4, hr * 0.08, colour, 12)
        if star >= 2:
            b.ring(h + Vector((0, -hr * 0.2, hr * 0.12)), hr * 0.96, 0.012, t, axis="f", segments=24, sides=4)
    else:
        S(b, P(-0.14, 0, z0 + 0.86), (0.2, 0.14, 0.1), colour, 16, 8)
    # 披肩
    S(b, P(0, 0, z0 + 0.85), (0.25, 0.21, 0.08), colour, 20, 8)
    if star >= 2:
        b.ring(P(0, 0, z0 + 0.82), 0.245, 0.012, t, segments=28, sides=4)


def quiver(b, z0, star, colour="leather", n=4, flights="feather"):
    R(b, P(-0.18, 0.06, z0 + 0.4), P(-0.24, 0.14, z0 + 0.95), 0.07, 0.075, colour, 12)
    for i in range(n):
        dx = 0.06 + i * 0.03
        R(b, P(-0.23, dx, z0 + 0.92), P(-0.26, dx + 0.01, z0 + 1.06), 0.01, 0.01, "wood", 6)
        S(b, P(-0.26, dx + 0.01, z0 + 1.08), (0.015, 0.008, 0.03), GLOW if star >= 4 else flights, 6, 4)
    if star >= 2:
        for dz in (0.5, 0.85):
            b.ring(P(-0.19 - 0.06 * (dz - 0.4), 0.06 + 0.08 * (dz - 0.4), z0 + dz), 0.074, 0.01, metal(star, ""),
                   segments=16, sides=4)


def hunter(b, star=1):  # 格雷 · 猎人：绿兜帽、胡茬、皮甲，左手长弓右手搭箭，背箭筒；★2 银扣，★3 金饰披风，★4 发光箭羽，★5 金弓、墨绿金边斗篷
    star_base(b, 0.75, star)
    green = "elf_dark" if star >= 5 else "leaf"

    def body():
        k = hero(b, legs="hair_brown", boots="leather", body="leather", brows="hair_brown", head_r=0.24)
        h, hr, z0 = k["head"], k["head_r"], k["z0"]
        dress(b, k, star, plain="leather", cape_colour="leaf", cape5="elf_dark", emblem_at=P(0.2, 0, z0 + 0.72))
        quiver(b, z0, star)
        S(b, h + Vector((0, -hr * 0.55, -hr * 0.55)), (hr * 0.65, hr * 0.38, hr * 0.32), "hair_brown", 12, 8)
        hood(b, k, green, star)
        b.rbox(P(0.15, 0, z0 + 0.7), (0.06, 0.03, 0.4), "hair_brown", 0.4, rot=(0, 35, 0))  # 箭筒背带
        rs, ls = k["shoulder"]
        lh = P(0.3, -0.38, z0 + 0.6)
        arm(b, ls, P(0.14, -0.38, z0 + 0.66), lh, "leather", "leather")
        bow(b, lh, star, size=0.44)
        rh = P(0.16, -0.22, z0 + 0.62)
        arm(b, rs, P(0.0, 0.2, z0 + 0.76), rh, "leather", "leather")
        if star < 4:
            R(b, rh + Vector((0, 0.04, 0)), lh + Vector((0, -0.2, 0)), 0.008, 0.008, "wood", 6)
            R(b, lh + Vector((0, -0.2, 0)), lh + Vector((0, -0.26, 0)), 0.02, 0.002, "steel", 6)
        if star >= 3:  # 腰间猎刀
            b.rbox(P(0.12, 0.2, z0 + 0.42), (0.03, 0.05, 0.2), "leather", 0.4, rot=(0, 0, 0))
            S(b, P(0.12, 0.2, z0 + 0.54), (0.025, 0.025, 0.025), "gold", 8, 6)

    posed(b, body, lean=0.05)


def trapper(b, star=1):  # 贝丝 · 陷阱师：双麻花辫、皮背心、兜帽垂在肩后，左手提捕兽夹、右手小斧；★2 银扣，★3 金饰披风，★4 夹齿发光，★5 墨绿金边、金斧
    star_base(b, 0.75, star)
    k = hero(b, legs="hair_brown", boots="leather", body="moss", belt="leather", brows="hair_brown", head_r=0.25)
    h, hr, z0 = k["head"], k["head_r"], k["z0"]
    dress(b, k, star, plain="leather", cape_colour="leaf", cape5="elf_dark", emblem_at=P(0.21, 0, z0 + 0.72))
    S(b, P(0.02, 0, z0 + 0.7), (0.2, 0.155, 0.2), "leather", 16, 8)  # 背心
    for s in (-1, 1):
        b.rbox(P(0.19, s * 0.06, z0 + 0.72), (0.02, 0.02, 0.24), "moss", 0.4)
    hair(b, k, "hair_brown", "short")
    for s in (-1, 1):  # 麻花辫垂在胸前
        pts = [h + Vector((s * hr * 0.8, hr * 0.1, -hr * 0.2)), h + Vector((s * hr * 0.95, -hr * 0.2, -hr * 0.9)),
               h + Vector((s * hr * 0.9, -hr * 0.35, -hr * 1.6))]
        for i in range(5):
            c = pts[0].lerp(pts[1], i / 2) if i <= 2 else pts[1].lerp(pts[2], (i - 2) / 2)
            S(b, c, (hr * 0.17, hr * 0.17, hr * 0.2), "hair_brown", 10, 6)
        S(b, pts[2] + Vector((0, 0, -hr * 0.15)), (hr * 0.12, hr * 0.12, hr * 0.08), metal(star, "crimson"), 8, 6)
    hood(b, k, "elf_dark" if star >= 5 else "leaf", star, up=False)
    rs, ls = k["shoulder"]
    lh = P(0.22, -0.32, z0 + 0.5)
    arm(b, ls, P(0.08, -0.36, z0 + 0.62), lh, "moss", "leather")
    trap = lh + Vector((0, -0.05, -0.16))
    R(b, lh, trap + Vector((0, 0, 0.05)), 0.008, 0.008, "iron", 4)  # 提链
    tc = "gold" if star >= 5 else metal(star, "iron")
    for tilt in (-1, 1):  # 张开的两片夹口
        b.ring(trap + Vector((0, tilt * 0.04, 0)), 0.11, 0.014, tc, axis="f", segments=20, sides=4)
        for i in range(7):
            a = math.pi * (0.1 + 0.8 * i / 6) * (1 if tilt > 0 else -1)
            o = trap + Vector((0.11 * math.cos(a), tilt * 0.04, 0.11 * math.sin(a)))
            R(b, o, o + Vector((-0.04 * math.cos(a), 0, -0.04 * math.sin(a))), 0.012, 0.002,
              GLOW if star >= 4 else "steel", 4)
    S(b, trap, (0.03, 0.05, 0.03), tc, 10, 6)
    rh = P(0.2, 0.32, z0 + 0.55)
    arm(b, rs, P(0.05, 0.32, z0 + 0.64), rh, "moss", "leather")
    R(b, rh + Vector((0, 0.03, -0.12)), rh + Vector((0, -0.05, 0.22)), 0.018, 0.018, "wood", 8)
    axe = rh + Vector((0, -0.05, 0.2))
    b.rbox(axe + Vector((0, -0.06, 0)), (0.03, 0.12, 0.1), "gold" if star >= 5 else "steel", 0.3)
    if star >= 2:
        b.ring(rh + Vector((0, 0.0, 0.05)), 0.024, 0.008, metal(star, ""), segments=10, sides=4)
    for i in range(2):  # 腰间挂的备用夹子和绳圈
        b.ring(P(0.05, 0.2 + i * 0.02, z0 + 0.4), 0.06, 0.012, "hair_brown", axis="x", segments=14, sides=4)


def herbalist(b, star=1):  # 小麦 · 草药师：草帽丸子头、围裙，背大药草包，手捧发光药草；★2 银扣，★3 金饰披风花环，★4 宝石药瓶，★5 草帽金丝花冠
    star_base(b, 0.75, star)
    k = hero(b, legs="hair_brown", boots="leather", body="moss", robe="leaf", belt="leather", brows="hair_blond",
             head_r=0.26)
    h, hr, z0 = k["head"], k["head_r"], k["z0"]
    dress(b, k, star, plain="leather", cape_colour="moss", cape5="leaf", emblem_at=P(0.21, 0, z0 + 0.74))
    b.rbox(P(0.22, 0, z0 + 0.32), (0.3, 0.04, 0.46), "white", 0.35, rot=(0, -12, 0))  # 围裙
    b.rbox(P(0.25, 0, z0 + 0.26), (0.14, 0.03, 0.1), "white", 0.4, rot=(0, -12, 0))
    if star >= 2:
        b.rbox(P(0.212, 0, z0 + 0.09), (0.31, 0.045, 0.03), metal(star, "leaf"), 0.4)
    hair(b, k, "hair_blond", "bun")
    # 草帽
    straw = "hair_blond"
    b.ring(h + Vector((0, 0.06, hr * 0.78)), hr * 1.32, 0.026, straw, segments=32, sides=6)
    S(b, h + Vector((0, 0.06, hr * 0.8)), (hr * 1.3, hr * 1.3, 0.022), straw, 28, 4, rot=(-14, 0, 0))
    S(b, h + Vector((0, 0.06, hr * 0.95)), (hr * 0.8, hr * 0.8, hr * 0.38), straw, 20, 8)
    b.ring(h + Vector((0, 0.06, hr * 0.86)), hr * 0.81, 0.022, "gold" if star >= 5 else "crimson", segments=24,
           sides=4)
    if star >= 3:  # 帽沿的小花
        for i in range(3 if star < 5 else 6):
            a = math.pi * (1.1 + 0.25 * i)
            c = h + Vector((hr * 0.83 * math.cos(a), 0.06 + hr * 0.83 * math.sin(a), hr * 0.9))
            S(b, c, (0.03, 0.03, 0.02), "white" if i % 2 else "gold_light", 8, 6)
    # 背后的大药草包
    bag = P(-0.24, 0, z0 + 0.65)
    b.rbox(bag, (0.32, 0.18, 0.3), "leather", 0.35)
    b.rbox(bag + Vector((0, -0.0, 0.12)), (0.33, 0.19, 0.08), "hair_brown", 0.4)
    for dx, dz in ((-0.08, 0.22), (0.02, 0.27), (0.1, 0.2)):
        S(b, bag + Vector((dx, 0.02, dz)), (0.06, 0.05, 0.08), "leaf", 10, 6)
    if star >= 2:
        S(b, bag + Vector((0, -0.1, 0.1)), (0.025, 0.012, 0.025), metal(star, ""), 8, 6)
    for s, sh in zip((1, -1), k["shoulder"]):
        R(b, sh + Vector((0, -0.08, 0.02)), sh + Vector((0, -0.08, -0.28)), 0.02, 0.02, "hair_brown", 6)
    # 双手捧着发光药草
    hands = []
    for s, sh in zip((1, -1), k["shoulder"]):
        hand = P(0.28, s * 0.08, z0 + 0.62)
        arm(b, sh, P(0.12, s * 0.3, z0 + 0.6), hand, "moss")
        hands.append(hand)
    sprig = (hands[0] + hands[1]) / 2 + Vector((0, 0, 0.06))
    R(b, sprig, sprig + Vector((0, 0, 0.14)), 0.008, 0.006, "leaf", 6)
    for i in range(4):
        a = i * math.pi / 2 + 0.4
        S(b, sprig + Vector((0.05 * math.cos(a), 0.05 * math.sin(a), 0.1 + 0.02 * i)), (0.04, 0.02, 0.02), "leaf",
          8, 6, rot=(0, 0, math.degrees(a)))
    S(b, sprig + Vector((0, 0, 0.17)), (0.03, 0.03, 0.03), GLOW, 8, 6)
    # 腰间药瓶
    for i, s in enumerate((-1, 1)):
        c = P(0.1, s * 0.2, z0 + 0.42)
        S(b, c, (0.04, 0.04, 0.05), GLOW if star >= 4 else "ice", 10, 6)
        R(b, c + Vector((0, 0, 0.04)), c + Vector((0, 0, 0.08)), 0.015, 0.015, "wood", 6)


def slinger(b, star=1):  # 杜克 · 投石手：红头巾、圆脸胡子，头顶甩着投石索，腰挂石袋；★2 银扣，★3 金饰披风，★4 发光弹丸，★5 金边头巾、墨绿斗篷
    star_base(b, 0.75, star)
    k = hero(b, legs="cloth_navy", boots="leather", body="moss", belt="leather", brows="hair_brown", head_r=0.26)
    h, hr, z0 = k["head"], k["head_r"], k["z0"]
    dress(b, k, star, plain="leather", pad="leather", cape_colour="leaf", cape5="elf_dark",
          emblem_at=P(0.21, 0, z0 + 0.72))
    S(b, P(0.03, 0, z0 + 0.6), (0.22, 0.18, 0.16), "moss", 16, 8)  # 圆肚子
    beard(b, k, "hair_brown", 0.06)
    # 头巾和飘带
    S(b, h + Vector((0, hr * 0.12, hr * 0.32)), (hr * 1.04, hr * 1.02, hr * 0.78), "crimson", 20, 10)
    b.ring(h + Vector((0, 0, hr * 0.38)), hr * 1.0, 0.028 if star < 5 else 0.034, metal(star, "crimson"),
           segments=24, sides=6)
    for s in (-1, 1):
        limb(b, [h + Vector((s * 0.04, hr * 1.0, hr * 0.3)), h + Vector((s * 0.08, hr * 1.5, hr * 0.0)),
                 h + Vector((s * 0.1, hr * 1.9, -hr * 0.2))], 0.035, 0.015, "crimson", 8)
    rs, ls = k["shoulder"]
    rh = P(0.05, 0.3, z0 + 1.32)
    arm(b, rs, P(0.0, 0.36, z0 + 1.05), rh, "moss", "leather")
    # 投石索：一圈飞旋的绳子和弹兜
    loop_c = rh + Vector((0, 0, 0.12))
    b.ring(loop_c, 0.22, 0.006, "hair_brown", segments=28, sides=4)
    pouch = loop_c + Vector((0.0, -0.22, 0))
    R(b, rh, pouch, 0.006, 0.006, "hair_brown", 4)
    S(b, pouch, (0.05, 0.04, 0.035), "leather", 10, 6)
    S(b, pouch + Vector((0, 0, 0.03)), (0.035, 0.035, 0.035), GLOW if star >= 4 else "stone", 10, 6)
    if star >= 4:
        b.ring(loop_c, 0.24, 0.005, GLOW, segments=28, sides=4)
    lh = P(0.2, -0.32, z0 + 0.5)
    arm(b, ls, P(0.06, -0.36, z0 + 0.6), lh, "moss", "leather")
    S(b, lh + Vector((0, -0.02, 0.06)), (0.04, 0.04, 0.04), "stone", 8, 6)
    bag = P(0.08, -0.24, z0 + 0.38)  # 腰间石袋
    S(b, bag, (0.08, 0.08, 0.09), "leather", 12, 8)
    for dx, dy in ((-0.02, 0.0), (0.02, 0.03), (0.0, -0.03)):
        S(b, bag + Vector((dx, dy, 0.08)), (0.03, 0.03, 0.03), "stone", 8, 6)
    if star >= 2:
        b.ring(bag + Vector((0, 0, 0.05)), 0.07, 0.01, metal(star, ""), segments=14, sides=4)


def marksman(b, star=1):  # 约翰 · 神射手：插羽宽檐帽、长风衣，肩扛带瞄准镜的长弩；★2 银扣，★3 金饰帽羽披风，★4 发光弩矢，★5 金镜长弩、墨绿金边风衣
    star_base(b, 0.75, star)
    coat = "elf_dark" if star >= 5 else "cloth_navy"
    k = hero(b, legs="hair_brown", boots="leather", body=coat, robe=coat, belt="leather", brows="hair_brown",
             head_r=0.24)
    h, hr, z0 = k["head"], k["head_r"], k["z0"]
    dress(b, k, star, plain="leather", pad="leather", cape_colour="leaf", cape5="elf_dark",
          emblem_at=P(0.2, 0, z0 + 0.74))
    if star >= 2:
        b.ring(P(0, 0, z0 + 0.1), 0.28, 0.018, metal(star, ""), segments=32, sides=6)
    for i in range(3):  # 风衣双排扣
        for s in (-1, 1):
            S(b, P(0.2, s * 0.05, z0 + 0.62 + i * 0.08), (0.014, 0.01, 0.014), metal(star, "leather"), 6, 4)
    hair(b, k, "hair_brown", "short")
    S(b, h + Vector((0, -hr * 0.6, -hr * 0.4)), (hr * 0.4, hr * 0.2, hr * 0.12), "hair_brown", 10, 6)  # 胡子
    # 宽檐帽和帽羽
    hat = "leather"
    S(b, h + Vector((0, 0.05, hr * 0.74)), (hr * 1.5, hr * 1.45, 0.03), hat, 28, 4, rot=(-16, 0, 0))
    S(b, h + Vector((0, 0.05, hr * 0.95)), (hr * 0.88, hr * 0.88, hr * 0.42), hat, 20, 8)
    b.ring(h + Vector((0, 0.05, hr * 0.82)), hr * 0.88, 0.022, metal(star, "hair_brown"), segments=24, sides=4)
    limb(b, [h + Vector((hr * 0.8, 0.05, hr * 0.8)), h + Vector((hr * 1.0, 0.2, hr * 1.3)),
             h + Vector((hr * 0.9, 0.4, hr * 1.6))], 0.035, 0.008, "crimson" if star >= 3 else "feather", 8)
    if star >= 4:
        gem(b, h + Vector((hr * 0.85, 0.02, hr * 0.78)), (0.02, 0.02, 0.03))
    # 长弩：架在右肩，平指前方
    rs, ls = k["shoulder"]
    stock_c = P(0.25, 0.14, z0 + 0.86)
    wood = "gold" if star >= 5 else "wood"
    b.rbox(stock_c, (0.06, 0.8, 0.07), wood, 0.35)
    front = stock_c + Vector((0, -0.38, 0.0))
    for s in (-1, 1):
        b.chain([front, front + Vector((s * 0.14, 0.0, 0)), front + Vector((s * 0.24, 0.08, 0))], 0.022, 0.014,
                metal(star, "steel_dark"), 8)
    b.chain([front + Vector((-0.24, 0.08, 0)), front + Vector((0, 0.3, 0.0)), front + Vector((0.24, 0.08, 0))],
            0.005, 0.005, "white", 4)
    R(b, front + Vector((0, 0.3, 0.03)), front + Vector((0, -0.08, 0.03)), 0.01, 0.01, GLOW if star >= 4 else "wood",
      6)
    scope = stock_c + Vector((0, -0.1, 0.07))  # 瞄准镜
    R(b, scope + Vector((0, 0.12, 0)), scope + Vector((0, -0.12, 0)), 0.025, 0.03, metal(star, "iron"), 12)
    gem(b, scope + Vector((0, -0.125, 0)), (0.024, 0.01, 0.024), GLOW if star >= 3 else "ice")
    rh = stock_c + Vector((0, 0.12, -0.06))
    arm(b, rs, P(0.12, 0.32, z0 + 0.66), rh, coat, "leather")
    lh = stock_c + Vector((-0.04, -0.2, -0.06))
    arm(b, ls, P(0.15, -0.2, z0 + 0.64), lh, coat, "leather")
    quiver(b, z0, star, n=3)


def beast_tamer(b, star=1):  # 托恩 · 驯兽师（悬空）：骑巨鹰的大胡子猎人，毛皮兜帽，肩上停着小鹰；★2 银扣，★3 金饰披风，★4 宝石，★5 金冠巨鹰、墨绿金边
    seat = bird(b, 0.42, body="hair_brown", wing="hair_brown", head_c="feather", lion=False, star=star, span=0.82)
    k = seated(b, seat, "moss", legs="hair_brown", boots="leather", brows="hair_brown", head_r=0.21)
    h, hr, z0 = k["head"], k["head_r"], k["z0"]
    dress(b, k, star, plain="leather", cape_colour="leaf", cape5="elf_dark", emblem_at=P(0.14, 0, z0 + 0.72),
          cape_f0=-0.19)
    beard(b, k, "hair_brown", 0.18)
    # 毛皮兜帽
    S(b, h + Vector((0, hr * 0.3, hr * 0.15)), (hr * 1.12, hr * 1.05, hr * 1.08), "bone_dark", 20, 12)
    b.ring(h + Vector((0, -hr * 0.2, hr * 0.12)), hr * 1.0, 0.03, "feather", axis="f", segments=24, sides=8)
    for s in (-1, 1):  # 兽耳
        R(b, h + Vector((s * hr * 0.55, hr * 0.2, hr * 0.85)), h + Vector((s * hr * 0.7, hr * 0.3, hr * 1.25)),
          0.05, 0.01, "bone_dark", 8)
    for s, sh in zip((1, -1), k["shoulder"]):
        hand = P(0.24, s * 0.12, z0 + 0.62)
        arm(b, sh, P(0.1, s * 0.28, z0 + 0.6), hand, "leather", "leather")
        R(b, hand, P(0.5, s * 0.08, 0.74), 0.008, 0.008, "leather", 4)
    # 左肩上的小鹰
    ls = k["shoulder"][1]
    c = ls + Vector((0, 0, 0.13))
    S(b, c, (0.06, 0.09, 0.07), "hair_brown", 12, 8)
    S(b, c + Vector((0, -0.07, 0.07)), (0.05, 0.05, 0.05), "feather", 12, 8)
    R(b, c + Vector((0, -0.11, 0.07)), c + Vector((0, -0.16, 0.05)), 0.02, 0.002, "gold", 6)
    for s in (-1, 1):
        S(b, c + Vector((s * 0.025, -0.1, 0.09)), (0.01, 0.008, 0.012), "iron", 6, 4)
        R(b, c + Vector((s * 0.05, 0.0, 0.02)), c + Vector((s * 0.08, 0.1, 0.0)), 0.03, 0.005, "hair_brown", 6)


# ---- 银月法师塔 ----------------------------------------------------------------------

def mage_hat(b, k, colour, star, band=None, bend=0.12, height=0.5):
    """尖顶法师帽：宽帽檐、弯折的帽尖；★2 起帽带饰边，★4 帽尖挂星饰。"""
    h, hr = k["head"], k["head_r"]
    base_c = h + Vector((0, 0.05, hr * 0.78))
    b.ring(base_c, hr * 1.32, 0.026, colour, segments=32, sides=6)
    S(b, base_c, (hr * 1.3, hr * 1.3, 0.022), colour, 28, 4, rot=(-14, 0, 0))
    mid = base_c + Vector((0, 0.03, height * 0.55))
    tip = base_c + Vector((0, bend + 0.04, height))
    R(b, base_c, mid, hr * 0.85, hr * 0.42, colour, 18)
    S(b, mid, (hr * 0.42, hr * 0.42, hr * 0.42), colour, 12, 8)
    R(b, mid, tip, hr * 0.42, 0.01, colour, 14)
    b.ring(base_c + Vector((0, 0, 0.04)), hr * 0.84, 0.024, band or metal(star, colour), segments=24, sides=4)
    if star >= 3:
        gem(b, base_c + Vector((0, -hr * 0.84, 0.05)), (0.025, 0.018, 0.035))
    if star >= 4:
        S(b, tip, (0.03, 0.03, 0.03), "gold", 8, 6)
        gem(b, tip + Vector((0, 0, -0.05)), (0.02, 0.02, 0.035))
    return tip


def robe_mage(b, star, robe, inner="white", boots=None, hair_c=None, hair_style=None, brows=None, head_r=0.25,
              cape_c=None, cape5=None):
    """法师的通用身体：长袍、下摆饰边、前襟饰带和披肩；返回关键点。"""
    trim = metal(star, inner)
    k = hero(b, body=robe, robe=robe, belt=trim if star > 1 else inner, boots=boots or inner, brows=brows,
             head_r=head_r)
    z0 = k["z0"]
    dress(b, k, star, plain=inner, cape_colour=cape_c or robe, cape5=cape5, emblem_at=P(0.2, 0, z0 + 0.74))
    b.rbox(P(0.2, 0, z0 + 0.32), (0.1, 0.04, 0.55), inner, 0.4, rot=(0, -14, 0))
    if star >= 2:
        b.ring(P(0, 0, z0 + 0.1), 0.28, 0.018, trim, segments=32, sides=6)
        for s in (-1, 1):
            b.rbox(P(0.205, s * 0.055, z0 + 0.32), (0.015, 0.045, 0.55), trim, 0.4, rot=(0, -14, 0))
    S(b, P(0, 0, z0 + 0.86), (0.25, 0.21, 0.08), inner if cape_c is None else robe, 20, 8)
    if star >= 2:
        b.ring(P(0, 0, z0 + 0.83), 0.245, 0.012, trim, segments=28, sides=4)
    if hair_c:
        hair(b, k, hair_c, hair_style or "short")
    return k


def hexer(b, star=1):  # 莫娜 · 咒术师：紫袍尖帽、黑长发，右手短杖，左手悬着断网咒印；★2 银饰，★3 金边披风，★4 帽尖星饰，★5 深紫金纹
    star_base(b, 0.75, star)
    robe = "elf_dark" if star >= 5 else "elf_purple"
    k = robe_mage(b, star, robe, inner="robe_black", hair_c="robe_black", hair_style="long", brows="robe_black",
                  cape_c="robe_black", cape5="crimson")
    h, hr, z0 = k["head"], k["head_r"], k["z0"]
    mage_hat(b, k, "robe_black" if star < 5 else "elf_dark", star, band=metal(star, "elf_purple"))
    rs, ls = k["shoulder"]
    rh = P(0.24, 0.3, z0 + 0.6)
    arm(b, rs, P(0.08, 0.32, z0 + 0.62), rh, robe)
    tip = rh + Vector((0, -0.22, 0.2))
    R(b, rh + Vector((0, 0.06, -0.05)), tip, 0.015, 0.012, "gold" if star >= 5 else "iron", 8)
    gem(b, tip, (0.03, 0.03, 0.05))
    if star >= 3:
        b.ring(tip, 0.05, 0.008, "gold", axis="f", segments=12, sides=4)
    lh = P(0.28, -0.3, z0 + 0.82)
    arm(b, ls, P(0.12, -0.36, z0 + 0.7), lh, robe)
    sig = lh + Vector((0, -0.1, 0.12))  # 咒印：一个断开的圆环加十字
    for i in range(3):
        a0 = i * 2 * math.pi / 3
        pts = [sig + Vector((0.13 * math.cos(a0 + t * 0.35), 0, 0.13 * math.sin(a0 + t * 0.35))) for t in range(5)]
        b.chain(pts, 0.012, 0.012, GLOW, 6)
    b.rbox(sig, (0.15, 0.015, 0.02), GLOW, 0.4, rot=(0, 45, 0))
    b.rbox(sig, (0.15, 0.015, 0.02), GLOW, 0.4, rot=(0, -45, 0))
    if star >= 4:
        b.ring(sig, 0.18, 0.006, GLOW, axis="f", segments=24, sides=4)


def pyromancer(b, star=1):  # 欧文 · 炎术士：红袍、火焰色刺猬头，左手托火球，右手火焰法杖；★2 银饰，★3 金边披风，★4 三层火球，★5 深红金纹、火冠
    star_base(b, 0.75, star)
    robe = "crimson"
    k = robe_mage(b, star, robe, inner="robe_black", boots="robe_black", hair_c="hair_red", hair_style="spiky",
                  brows="hair_red", cape_c="robe_black", cape5="crimson")
    h, hr, z0 = k["head"], k["head_r"], k["z0"]
    if star >= 5:
        for i in range(5):
            a = math.pi * (0.1 + 0.8 * i / 4)
            o = h + Vector((hr * 0.9 * math.cos(a), -hr * 0.5 * math.sin(a), hr * 0.6))
            R(b, o, o + Vector((0, 0, 0.09 if i == 2 else 0.06)), 0.02, 0.004, "gold", 6)
    rs, ls = k["shoulder"]
    rh = P(0.15, 0.32, z0 + 0.62)
    arm(b, rs, P(0.05, 0.32, z0 + 0.65), rh, robe)
    top = staff(b, rh, z0, star, wood="iron", height=1.4)
    for dz, r, c in ((0.08, 0.08, "fire"), (0.16, 0.06, "gold_light"), (0.24, 0.035, "fire")):
        S(b, top + Vector((0, 0, dz)), (r, r, r * 1.3), c, 10, 6)
    for s in (-1, 1):
        R(b, top + Vector((s * 0.07, 0, -0.02)), top + Vector((s * 0.1, 0, 0.12)), 0.02, 0.004, metal(star, "iron"), 6)
    lh = P(0.26, -0.32, z0 + 0.72)
    arm(b, ls, P(0.1, -0.36, z0 + 0.64), lh, robe)
    ball = lh + Vector((0, -0.02, 0.16))
    layers = ((0.12, "fire"), (0.085, "gold_light")) if star < 4 else ((0.14, "fire"), (0.1, "gold_light"),
                                                                      (0.06, "white"))
    for r, c in layers:
        S(b, ball, (r, r, r * 1.1), c, 14, 8)
    for i in range(4):  # 向上窜的火舌
        a = i * math.pi / 2 + 0.3
        o = ball + Vector((0.08 * math.cos(a), 0.08 * math.sin(a), 0.06))
        R(b, o, o + Vector((0.02 * math.cos(a), 0.02 * math.sin(a), 0.12)), 0.035, 0.003, "fire", 6)


def frost_mage(b, star=1):  # 苏菲 · 冰霜法师：冰蓝长袍、银白长发，冰晶法杖，身边飘雪花；★2 银饰，★3 金边披风，★4 冰晶冠，★5 白金外袍、冰环
    star_base(b, 0.75, star)
    robe = "white" if star >= 5 else "ice"
    k = robe_mage(b, star, robe, inner="cloth_navy" if star < 5 else "royal", hair_c="silver", hair_style="long",
                  brows="steel", cape_c="cloth_navy", cape5="royal")
    h, hr, z0 = k["head"], k["head_r"], k["z0"]
    if star >= 4:  # 冰晶冠
        for i in range(5):
            a = math.pi * (0.1 + 0.8 * i / 4)
            o = h + Vector((hr * 0.85 * math.cos(a), -hr * 0.55 * math.sin(a), hr * 0.62))
            gem(b, o + Vector((0, 0, 0.04)), (0.018, 0.018, 0.06 if i == 2 else 0.04), "ice")
    rs, ls = k["shoulder"]
    rh = P(0.15, 0.32, z0 + 0.62)
    arm(b, rs, P(0.05, 0.32, z0 + 0.65), rh, robe)
    top = staff(b, rh, z0, star, wood="steel_dark", height=1.4)
    gem(b, top + Vector((0, 0, 0.12)), (0.05, 0.05, 0.13), "ice")
    for i in range(3 if star < 3 else 5):
        a = i * 2 * math.pi / (3 if star < 3 else 5)
        d = Vector((math.cos(a), math.sin(a), 0.6))
        gem(b, top + Vector((0, 0, 0.06)) + d * 0.07, (0.02, 0.02, 0.06), "ice", rot=(0, 30, math.degrees(a)))
    gem(b, top + Vector((0, 0, 0.12)), (0.025, 0.025, 0.06))
    lh = P(0.26, -0.32, z0 + 0.72)
    arm(b, ls, P(0.1, -0.36, z0 + 0.64), lh, robe)
    flake = lh + Vector((0, -0.03, 0.17))  # 手上的雪花
    for i in range(6):
        a = i * math.pi / 3
        d = Vector((math.cos(a), 0, math.sin(a)))
        R(b, flake, flake + d * 0.11, 0.012, 0.006, "ice", 6)
        S(b, flake + d * 0.07, (0.014, 0.01, 0.014), "white", 6, 4)
    gem(b, flake, (0.025, 0.02, 0.025))
    if star >= 5:
        b.ring(P(0, 0, z0 + 0.7), 0.5, 0.008, "ice", segments=40, sides=4)
        for i in range(6):
            a = i * math.pi / 3
            gem(b, P(0.5 * math.cos(a), 0.5 * math.sin(a), z0 + 0.7), (0.02, 0.02, 0.04), "ice")


def archmage(b, star=1):  # 阿尔德 · 大法师：深蓝星纹长袍、高尖帽、长白胡，身前悬浮法典，法杖顶托法力球；★2 银饰，★3 金边披风，★4 帽尖星饰、双环，★5 白金外袍、三环
    star_base(b, 0.75, star)
    robe = "royal"
    k = robe_mage(b, star, robe, inner="gold" if star >= 3 else "white", boots="cloth_navy", brows="white",
                  cape_c="white" if star >= 5 else "cloth_navy", cape5="white")
    h, hr, z0 = k["head"], k["head_r"], k["z0"]
    beard(b, k, "white", 0.4)
    mage_hat(b, k, "royal", star, band="gold" if star >= 3 else "white", height=0.62, bend=0.16)
    for i in range(3 if star < 3 else 6):  # 袍子和帽子上的星点
        a = i * 1.7
        S(b, P(0.27 * math.cos(a), 0.27 * math.sin(a), z0 + 0.15 + 0.05 * (i % 3)), (0.018, 0.018, 0.018),
          "gold_light", 6, 4)
    rs, ls = k["shoulder"]
    rh = P(0.15, 0.32, z0 + 0.62)
    arm(b, rs, P(0.05, 0.32, z0 + 0.65), rh, robe)
    top = staff(b, rh, z0, star, height=1.5)
    orb = top + Vector((0, 0, 0.13))
    for s in (-1, 1):
        limb(b, [top + Vector((s * 0.03, 0, 0)), top + Vector((s * 0.1, 0, 0.08)), top + Vector((s * 0.07, 0, 0.2))],
             0.018, 0.008, metal(star, "wood"), 8)
    gem(b, orb, (0.075, 0.075, 0.075))
    rings = 1 if star < 4 else (2 if star == 4 else 3)
    for i, ax in enumerate(("f", "x", "z")[:rings]):
        b.ring(orb, 0.12 + 0.02 * i, 0.008, "gold_light", axis=ax, segments=24, sides=4)
    lh = P(0.26, -0.3, z0 + 0.66)
    arm(b, ls, P(0.1, -0.36, z0 + 0.6), lh, robe)
    book = P(0.42, -0.12, z0 + 0.82)  # 悬浮的法典，翻开朝上
    for s in (-1, 1):
        b.rbox(book + Vector((s * 0.08, 0, 0)), (0.16, 0.2, 0.025), "crimson", 0.3, rot=(0, s * -12, 0))
        b.rbox(book + Vector((s * 0.075, 0, 0.018)), (0.145, 0.18, 0.02), "white", 0.2, rot=(0, s * -12, 0))
    if star >= 2:
        b.ring(book + Vector((0, 0, -0.01)), 0.14, 0.008, metal(star, ""), segments=20, sides=4)
    for dx, dz, r in ((0, 0.1, 0.025), (-0.06, 0.15, 0.015), (0.05, 0.18, 0.012)):
        S(b, book + Vector((dx, 0, dz)), (r, r, r), GLOW, 8, 6)


def golem_bones(b, z0, hips, spine, head, leg_x, leg_top, shoulder, elbow, hand):
    """魔像的骨骼：和人形同名（动画通用），比例按魔像的身体。shoulder/elbow/hand 给右侧，左侧镜像。"""
    b.bone("hips", P(0, 0, z0 + hips[0]), P(0, 0, z0 + hips[1]))
    b.bone("spine", P(0, 0, z0 + hips[1]), P(0, 0, z0 + spine), "hips")
    b.bone("head", P(0, 0, z0 + spine), P(0, 0, z0 + head), "spine")
    for side, s in (("R", 1), ("L", -1)):
        b.bone("leg." + side, P(0, s * leg_x, z0 + leg_top), P(0.02, s * leg_x, z0), "hips")
        sh, el, ha = (Vector((v.x * s, v.y, v.z)) for v in (shoulder, elbow, hand))
        b.bone("arm_upper." + side, sh, el, "spine")
        b.bone("arm_lower." + side, el, ha, "arm_upper." + side)


def crystal_golem(b, star=1):  # 微光 · 水晶傀儡：石块拼成的小傀儡，背上和肩上长出水晶，胸口发光核心；★2 银箍，★3 金箍更多水晶，★4 水晶冠，★5 金纹、环绕水晶
    star_base(b, 0.75, star)
    stone = "stone"
    t = metal(star, "stone_dark")
    z0 = 0.1
    golem_bones(b, z0, hips=(0.2, 0.36), spine=0.82, head=1.1, leg_x=0.14, leg_top=0.24,
                shoulder=P(0, 0.3, z0 + 0.7), elbow=P(0.05, 0.38, z0 + 0.5), hand=P(0.18, 0.36, z0 + 0.32))
    for s in (-1, 1):  # 粗短的腿
        b.rbox(P(0.02, s * 0.14, z0 + 0.12), (0.18, 0.18, 0.24), "stone_dark", 0.4)
    b.rbox(P(0, 0, z0 + 0.55), (0.5, 0.36, 0.48), stone, 0.45)  # 身体
    b.rbox(P(0, 0, z0 + 0.32), (0.42, 0.3, 0.14), "stone_dark", 0.45)
    core = P(0.18, 0, z0 + 0.58)
    gem(b, core, (0.09, 0.05, 0.11))
    b.ring(core + Vector((0, 0.02, 0)), 0.11, 0.02, t, axis="f", segments=20, sides=6)
    head = P(0.02, 0, z0 + 0.95)
    b.rbox(head, (0.3, 0.28, 0.24), stone, 0.5)
    for s in (-1, 1):
        S(b, head + Vector((s * 0.07, -0.14, 0.01)), (0.035, 0.02, 0.04), GLOW, 8, 6)
    if star >= 2:
        b.ring(P(0, 0, z0 + 0.78), 0.27, 0.02, t, segments=24, sides=6)
    # 背上和肩上的水晶簇
    spots = [(-0.15, 0, 0.82, 0.22), (-0.12, 0.14, 0.75, 0.15), (-0.12, -0.14, 0.75, 0.15), (0.0, 0.25, 0.8, 0.12),
             (0.0, -0.25, 0.8, 0.12)]
    if star >= 3:
        spots += [(-0.18, 0.06, 0.6, 0.12), (-0.18, -0.06, 0.6, 0.12)]
    for f, x, z, l in spots:
        o = P(f, x, z0 + z)
        out = Vector((x * 1.2, -f * 0.6 + 0.1, 1.0)).normalized()
        gem(b, o + out * l * 0.5, (0.05, 0.05, l * 0.6), "ice")
    if star >= 4:  # 水晶冠
        for i in range(3):
            gem(b, head + Vector(((i - 1) * 0.08, 0.02, 0.17 + (0.04 if i == 1 else 0))), (0.03, 0.03, 0.07))
    # 手臂：大石拳
    for s in (-1, 1):
        sh = P(0, s * 0.3, z0 + 0.7)
        hand = P(0.18, s * 0.36, z0 + 0.32)
        limb(b, [sh, P(0.05, s * 0.38, z0 + 0.5), hand], 0.08, 0.07, "stone_dark", 12)
        b.rbox(hand, (0.17, 0.17, 0.17), stone, 0.5)
        if star >= 2:
            b.ring(hand + Vector((0, 0, 0.06)), 0.09, 0.014, t, segments=16, sides=4)
    if star >= 5:
        b.ring(P(0, 0, z0 + 0.7), 0.5, 0.008, GLOW, segments=40, sides=4)
        for i in range(4):
            a = i * math.pi / 2 + 0.4
            gem(b, P(0.5 * math.cos(a), 0.5 * math.sin(a), z0 + 0.7), (0.03, 0.03, 0.07), "ice")


def _siege_golem(b, star):
    iron = "iron"
    plate = "gold_dark" if star >= 5 else "steel_dark"
    t = metal(star, "steel_dark")
    z0 = 0.1
    golem_bones(b, z0, hips=(0.3, 0.5), spine=0.98, head=1.22, leg_x=0.17, leg_top=0.42,
                shoulder=P(0, 0.38, z0 + 0.92), elbow=P(0.06, 0.48, z0 + 0.62), hand=P(0.2, 0.46, z0 + 0.32))
    for s in (-1, 1):
        R(b, P(0, s * 0.17, z0 + 0.15), P(0, s * 0.17, z0 + 0.42), 0.1, 0.11, "stone_dark", 14)
        b.rbox(P(0.04, s * 0.17, z0 + 0.08), (0.24, 0.24, 0.14), plate, 0.4)
    b.rbox(P(0, 0, z0 + 0.72), (0.6, 0.42, 0.5), "stone", 0.45)  # 躯干
    b.rbox(P(0.16, 0, z0 + 0.74), (0.46, 0.14, 0.36), plate, 0.4)  # 胸甲
    rivets(b, [P(0.235, x, z0 + z) for x in (-0.18, 0.18) for z in (0.6, 0.88)], t, 0.022)
    core = P(0.24, 0, z0 + 0.74)
    gem(b, core, (0.07, 0.04, 0.09))
    b.ring(core, 0.09, 0.016, t, axis="f", segments=16, sides=6)
    if star >= 3:
        emblem(b, P(0.24, 0, z0 + 0.92), 0.8, "gold", jewel=GLOW if star >= 4 else None)
    head = P(0.06, 0, z0 + 1.08)
    b.rbox(head, (0.24, 0.24, 0.18), "stone", 0.5)
    b.rbox(head + Vector((0, -0.11, 0.0)), (0.2, 0.04, 0.05), GLOW, 0.4)  # 一条发光的眼缝
    b.rbox(head + Vector((0, 0.02, 0.1)), (0.26, 0.26, 0.05), plate, 0.4)
    if star >= 4:
        R(b, head + Vector((0, 0, 0.12)), head + Vector((0, 0, 0.24)), 0.03, 0.01, "gold", 8)
    for s in (-1, 1):  # 巨大的肩甲和拳头
        sh = P(0, s * 0.38, z0 + 0.92)
        S(b, sh, (0.17, 0.15, 0.14), plate, 14, 8)
        if star >= 2:
            b.ring(sh + Vector((0, 0, -0.02)), 0.16, 0.018, t, segments=20, sides=4)
        hand = P(0.2, s * 0.46, z0 + 0.32)
        limb(b, [sh, P(0.06, s * 0.48, z0 + 0.62), hand], 0.09, 0.09, "stone_dark", 12)
        b.rbox(hand, (0.26, 0.24, 0.24), iron if star < 5 else "gold_dark", 0.45)
        b.ring(hand + Vector((0, 0, 0.1)), 0.13, 0.02, t, segments=20, sides=6)
        for i in range(3):
            S(b, hand + Vector((0.13, -0.08 + i * 0.08, 0.04)), (0.03, 0.03, 0.03), t, 8, 6)
    # 背后的烟囱和符文
    for s in (-1, 1):
        R(b, P(-0.22, s * 0.12, z0 + 0.9), P(-0.24, s * 0.12, z0 + 1.2), 0.05, 0.05, iron, 10)
        if star >= 3:
            S(b, P(-0.24, s * 0.12, z0 + 1.24), (0.035, 0.035, 0.035), GLOW, 8, 6)
    if star >= 5:
        b.ring(P(0, 0, z0 + 0.72), 0.46, 0.012, "gold_light", segments=32, sides=4)


def siege_golem(b, star=1):  # 铁砧 · 攻城魔像（2×2）：石身铁甲的魔像，胸口法力核心，巨拳；★2 银箍，★3 金饰胸徽，★4 宝石、盔顶尖刺，★5 暗金甲
    star_base(b, 1.6, star)
    scaled(b, 1.9, lambda: _siege_golem(b, star))


def spell_knight(b, star=1):  # 卡兰 · 秘法骑士：蓝钢板甲刻发光符文，法力剑和环绕的法盾；★2 银饰，★3 金边披风，★4 宝石护手，★5 白金甲、双层法盾
    star_base(b, 0.75, star)
    armor = "white" if star >= 5 else "steel"
    k = hero(b, legs="cloth_navy", boots=armor, body=armor, belt="royal", brows="hair_brown", head_r=0.24)
    h, hr, z0 = k["head"], k["head_r"], k["z0"]
    dress(b, k, star, pad=armor, cape_colour="royal", cape5="elf_purple", emblem_at=P(0.21, 0, z0 + 0.74))
    for f in (0.17, -0.17):
        b.rbox(P(f, 0, z0 + 0.4), (0.26, 0.04, 0.3), "royal", 0.3)
    for i in range(3):  # 胸甲上的符文线
        S(b, P(0.205, -0.07 + i * 0.07, z0 + 0.62), (0.012, 0.01, 0.03), GLOW, 6, 4)
    hair(b, k, "hair_brown", "short")
    S(b, h + Vector((0, hr * 0.35, hr * 0.3)), (hr * 1.02, hr * 0.85, hr * 0.85), armor, 20, 10)  # 半盔
    b.ring(h + Vector((0, hr * 0.25, hr * 0.3)), hr * 0.98, 0.022, metal(star, "steel_dark"), segments=24, sides=6)
    gem(b, h + Vector((0, -hr * 0.6, hr * 0.85)), (0.02, 0.018, 0.03))
    for s, sh in zip((1, -1), k["shoulder"]):
        if star >= 4:
            gem(b, sh + Vector((0, -0.1, 0.07)), (0.02, 0.016, 0.03))
    rs, ls = k["shoulder"]
    rh = P(0.18, 0.3, z0 + 0.5)
    arm(b, rs, P(0.05, 0.3, z0 + 0.62), rh, armor, metal(star, "steel_dark"))
    sword(b, rh, star, blade="ice", glow_from=3)
    lh = P(0.26, -0.3, z0 + 0.62)
    arm(b, ls, P(0.12, -0.34, z0 + 0.6), lh, armor, metal(star, "steel_dark"))
    sc = lh + Vector((0, -0.08, 0.02))  # 法盾：发光的圆环和中心符文
    b.ring(sc, 0.2, 0.014, GLOW, axis="f", segments=32, sides=4)
    b.ring(sc, 0.13, 0.008, GLOW, axis="f", segments=24, sides=4)
    gem(b, sc, (0.04, 0.02, 0.05))
    if star >= 5:
        b.ring(sc + Vector((0, -0.03, 0)), 0.26, 0.008, "gold_light", axis="f", segments=32, sides=4)


# ---- 亡灵 ------------------------------------------------------------------------

def banshee(b, star=1):  # 赛薇 · 女妖：没有腿、拖着幽魂尾巴飘在底座上，白发飞扬、张口尖啸；★2 银饰，★3 暗金冠饰披纱，★4 发光泪痕，★5 深红破纱、骨冠
    star_base(b, 0.75, star)
    ghost = "ghost"
    z0 = 0.25
    # 幽魂尾巴：从底座盘旋升起
    tail = [P(-0.1, 0.12, 0.12), P(0.05, -0.1, 0.2), P(0.02, 0.05, 0.38), P(0, 0, z0 + 0.45)]
    limb(b, tail, 0.06, 0.2, ghost, 14)
    for i in range(5):  # 破烂的裙摆
        a = i * 2 * math.pi / 5
        o = P(0.17 * math.cos(a), 0.17 * math.sin(a), z0 + 0.4)
        R(b, o + Vector((0, 0, 0.12)), o + Vector((0.08 * math.sin(a), 0.08 * math.cos(a), -0.16)), 0.07, 0.01,
          "crimson" if star >= 5 else "ghost", 8)
    k = {"head_r": 0.25, "z0": z0}
    humanoid_bones(b, z0, P(0, 0, z0 + 0.88 + 0.25 * 0.9), 0.25, legs=False)
    S(b, P(0, 0, z0 + 0.66), (0.19, 0.15, 0.23), ghost)
    b.ring(P(0, 0, z0 + 0.52), 0.18, 0.014, metal(star, "bone_dark"), segments=24, sides=4)
    h = P(0, 0, z0 + 0.88 + 0.25 * 0.9)
    hr = 0.25
    k.update(head=h, shoulder=(P(0, 0.2, z0 + 0.8), P(0, -0.2, z0 + 0.8)), chest=P(0.15, 0, z0 + 0.68),
             back=P(-0.15, 0, z0 + 0.7))
    S(b, h, (hr, hr * 0.95, hr * 0.95), "ghost", 20, 12)
    for s in (-1, 1):
        S(b, h + Vector((s * hr * 0.36, -hr * 0.86, hr * 0.05)), (hr * 0.15, hr * 0.08, hr * 0.13), GLOW, 10, 6)
        if star >= 4:
            R(b, h + Vector((s * hr * 0.38, -hr * 0.9, -hr * 0.12)), h + Vector((s * hr * 0.4, -hr * 0.82, -hr * 0.5)),
              0.012, 0.004, GLOW, 6)
    S(b, h + Vector((0, -hr * 0.88, -hr * 0.38)), (hr * 0.18, hr * 0.08, hr * 0.24), "robe_black", 10, 6)  # 尖啸的嘴
    # 向后飞扬的长发
    S(b, h + Vector((0, hr * 0.2, hr * 0.25)), (hr * 1.05, hr * 1.0, hr * 0.85), "white", 20, 12)
    for dx, dz in ((-0.11, 0.08), (0.0, 0.14), (0.11, 0.08), (-0.07, -0.08), (0.07, -0.08)):
        limb(b, [h + Vector((dx, hr * 0.45, dz)), h + Vector((dx * 1.3, hr * 1.4, dz - 0.04)),
                 h + Vector((dx * 1.6, hr * 2.2, dz - 0.2))], 0.11, 0.025, "white", 12)
    if star >= 3:
        crown = "bone" if star >= 5 else "gold_dark"
        b.ring(h + Vector((0, 0.03, hr * 0.5)), hr * 0.95, 0.016, crown, segments=24, sides=4)
        for i in range(5):
            a = math.pi * (0.1 + 0.8 * i / 4)
            o = h + Vector((hr * 0.92 * math.cos(a), -hr * 0.6 * math.sin(a), hr * 0.55))
            R(b, o, o + Vector((0, 0, 0.1 if i == 2 else 0.06)), 0.018, 0.003, crown, 6)
        cape(b, z0 + 0.84, z0 + 0.2, 0.18, 0.3, "crimson" if star >= 5 else "robe_black", trim="gold_dark",
             trim2="gold" if star >= 4 else None, flare=0.25, rows=6)
    for s, sh in zip((1, -1), k["shoulder"]):  # 张开的双臂、细长的手指
        hand = P(0.2, s * 0.55, z0 + 0.95)
        arm(b, sh, P(0.08, s * 0.4, z0 + 0.82), hand, ghost, ghost, r=0.05)
        for j in range(3):
            R(b, hand, hand + Vector((s * 0.08, -0.04 + j * 0.04, 0.06 - j * 0.03)), 0.014, 0.004, ghost, 6)
        if star >= 2:
            b.ring(hand + Vector((-s * 0.06, 0, -0.02)), 0.05, 0.01, metal(star, "bone_dark"), axis="x", segments=12,
                   sides=4)
    if star >= 5:
        b.ring(P(0, 0, z0 + 0.6), 0.46, 0.008, GLOW, segments=40, sides=4)


def mana_leech(b, star=1):  # 卡斯 · 吸魔者：佝偻的亡灵法师，破兜帽、腐皮，背上的导管连着吸来的法力水晶；★2 银饰，★3 暗金边披风，★4 双水晶，★5 深红破袍、金冠
    star_base(b, 0.75, star)
    robe = "crimson" if star >= 5 else "rot_dark"
    trim = "bone_dark" if star == 1 else ("silver" if star == 2 else "gold_dark")

    def body():
        k = hero(b, body=robe, robe=robe, belt=trim, boots="robe_black", skin="rot_flesh", head_r=0.24,
                 glow_eyes=True)
        h, hr, z0 = k["head"], k["head_r"], k["z0"]
        if star >= 3:
            cape(b, z0 + 0.88, z0 + 0.04, 0.2, 0.34, "robe_black", trim=trim, trim2="gold" if star >= 4 else None,
                 f0=-0.1, flare=0.18)
        if star >= 2:
            b.ring(P(0, 0, z0 + 0.12), 0.28, 0.018, trim, segments=32, sides=6)
        S(b, h + Vector((0, hr * 0.3, hr * 0.1)), (hr * 1.12, hr * 1.05, hr * 1.1), "robe_black", 20, 12)  # 破兜帽
        if star >= 2:
            b.ring(h + Vector((0, -hr * 0.2, hr * 0.1)), hr * 0.98, 0.012, trim, axis="f", segments=24, sides=4)
        if star >= 5:
            b.ring(h + Vector((0, 0.05, hr * 0.85)), hr * 0.6, 0.016, "gold", segments=20, sides=4)
        S(b, h + Vector((0, -hr * 0.86, -hr * 0.4)), (hr * 0.3, hr * 0.08, hr * 0.12), GLOW, 10, 6)  # 发光的嘴
        # 背上的导管和吸来的水晶
        crystals = [P(-0.3, 0.0, z0 + 1.35)] if star < 4 else [P(-0.32, 0.16, z0 + 1.3), P(-0.32, -0.16, z0 + 1.3)]
        for c in crystals:
            limb(b, [P(-0.15, c.x * 0.4, z0 + 0.8), P(-0.3, c.x * 0.8, z0 + 1.0), c + Vector((0, 0, -0.12))], 0.03,
                 0.025, "bone_dark", 8)
            gem(b, c, (0.07, 0.07, 0.13), "crystal_dead")
            gem(b, c, (0.035, 0.035, 0.08))
            b.ring(c + Vector((0, 0, -0.1)), 0.05, 0.012, trim, segments=12, sides=4)
        for s, sh in zip((1, -1), k["shoulder"]):  # 伸向前方吸取法力的爪手
            hand = P(0.36, s * 0.18, z0 + 0.66)
            arm(b, sh, P(0.15, s * 0.3, z0 + 0.6), hand, robe, "rot_flesh")
            for j in range(3):
                R(b, hand, hand + Vector((-0.03 + j * 0.03, -0.08, 0.02)), 0.012, 0.003, "bone", 6)
        S(b, P(0.5, 0, z0 + 0.7), (0.04, 0.04, 0.04), GLOW, 8, 6)
        for t in (0.25, 0.55):
            S(b, P(0.36 + 0.14 * t, 0, z0 + 0.68), (0.02, 0.02, 0.02), GLOW, 6, 4)

    posed(b, body, lean=0.16)


def blood_knight(b, star=1):  # 瓦伦 · 血族骑士：苍白脸、黑背头、红眼，黑甲深红披风，蝠翼高领和细长弯刃；★2 银饰，★3 金边，★4 宝石胸针，★5 金纹黑甲、蝠翼披风
    star_base(b, 0.75, star)
    armor = "robe_black"
    trim = metal(star, "iron")
    k = hero(b, legs="robe_black", boots=armor, body=armor, belt="crimson", skin="bone", eye="crimson",
             brows="robe_black", head_r=0.24)
    h, hr, z0 = k["head"], k["head_r"], k["z0"]
    cape(b, z0 + 0.9, z0 + 0.02, 0.22, 0.38, "crimson", trim=trim if star >= 2 else None,
         trim2="gold_light" if star >= 4 else None, flare=0.2)
    for sh in k["shoulder"]:
        clasp(b, sh + Vector((0, -0.04, 0.02)), jewel="crimson" if star < 4 else GLOW, s=0.7)
    hair(b, k, "robe_black", "short")
    R(b, h + Vector((0, -hr * 0.6, hr * 0.7)), h + Vector((0, -hr * 0.9, hr * 0.2)), 0.025, 0.008, "robe_black", 6)
    for s in (-1, 1):  # 蝠翼高领
        b.rbox(P(-0.04, s * 0.15, z0 + 1.02), (0.14, 0.03, 0.26), "crimson", 0.4, rot=(0, 0, s * 24))
        if star >= 2:
            b.rbox(P(-0.04, s * 0.165, z0 + 1.02), (0.15, 0.012, 0.27), trim, 0.4, rot=(0, 0, s * 24))
    b.rbox(P(0.2, 0, z0 + 0.4), (0.24, 0.04, 0.32), "crimson", 0.3)  # 前摆
    if star >= 2:
        b.rbox(P(0.205, 0, z0 + 0.25), (0.25, 0.045, 0.03), trim, 0.4)
    if star >= 3:
        emblem(b, P(0.21, 0, z0 + 0.72), 0.8, "gold", jewel="crimson" if star < 4 else GLOW)
    for s, sh in zip((1, -1), k["shoulder"]):
        S(b, sh + Vector((0, 0, 0.04)), (0.12, 0.11, 0.08), armor, 14, 8)
        R(b, sh + Vector((0, 0, 0.08)), sh + Vector((s * 0.06, 0.02, 0.2)), 0.03, 0.004, trim, 8)  # 肩刺
        if star >= 2:
            b.ring(sh + Vector((0, 0, 0.02)), 0.11, 0.012, trim, segments=20, sides=4)
    if star >= 5:  # 背后的蝠翼
        for s in (-1, 1):
            for i in range(3):
                o = P(-0.2, s * 0.12, z0 + 0.95)
                R(b, o, o + Vector((s * (0.35 - i * 0.06), 0.15, 0.25 - i * 0.15)), 0.02, 0.006, "gold_dark", 8)
            S(b, P(-0.24, s * 0.3, z0 + 1.06), (0.26, 0.025, 0.13), "robe_black", 16, 8, rot=(0, -s * 30, 0))
    rs, ls = k["shoulder"]
    rh = P(0.22, 0.3, z0 + 0.55)
    arm(b, rs, P(0.06, 0.3, z0 + 0.62), rh, armor, "bone")
    sword(b, rh, star, length=0.5, blade="crimson" if star < 3 else "silver", guard=metal(star, "iron"), tilt=50)
    lh = P(0.16, -0.3, z0 + 0.48)
    arm(b, ls, P(0.04, -0.32, z0 + 0.62), lh, armor, "bone")


# ---- 影歌精灵 ----------------------------------------------------------------------

def elf(b, star, body, legs=None, boots="elf_dark", robe=None, hair_c="silver", hair_style="long", head_r=0.25,
        belt="elf_dark", cape_c="elf_dark", cape5="royal", pad=None):
    """精灵的通用身体：浅肤色、尖耳朵、头发，按星级换装；返回关键点。"""
    k = hero(b, legs=legs or body, boots=boots, body=body, robe=robe, skin="elf_skin", belt=belt, brows=hair_c,
             head_r=head_r)
    z0 = k["z0"]
    dress(b, k, star, plain="elf_dark", pad=pad, cape_colour=cape_c, cape5=cape5, emblem_at=P(0.2, 0, z0 + 0.72))
    ears(b, k, "elf_skin")
    hair(b, k, hair_c, hair_style)
    if star >= 2:
        circlet(b, k, metal(star, "silver"), jewel=GLOW if star >= 4 else None)
    return k


@held_weapon
def curved_blade(b, hand, star, length=0.34, side=1, colour=None):
    """精灵弯刃：一段弧形刀身。"""
    c = colour or ("gold_light" if star >= 5 else "silver")
    pts = [hand + Vector((0, -0.02, 0.05)), hand + Vector((side * 0.02, -0.12, length * 0.45)),
           hand + Vector((side * 0.04, -0.1, length * 0.8)), hand + Vector((side * 0.05, -0.02, length))]
    b.chain(pts, 0.022, 0.004, c, 6)
    if star >= 4:
        b.chain(pts[1:3], 0.01, 0.006, GLOW, 4)
    b.rbox(hand + Vector((0, -0.01, 0.04)), (0.1, 0.03, 0.025), metal(star, "elf_dark"), 0.5)


def shadow_dancer(b, star=1):  # 希尔瓦 · 影舞者：面纱、高马尾，紫黑紧身衣和飘带，双持弯刃；★2 银饰，★3 金饰短披风，★4 发光刃纹，★5 金刃、夜空色飘带
    star_base(b, 0.75, star)
    suit = "elf_dark"
    sash = "royal" if star >= 5 else "elf_purple"

    def body():
        k = elf(b, star, suit, boots="robe_black", hair_c="silver", hair_style="pony", head_r=0.24, cape_c=suit,
                cape5="royal")
        h, hr, z0 = k["head"], k["head_r"], k["z0"]
        S(b, h + Vector((0, -hr * 0.55, -hr * 0.4)), (hr * 0.82, hr * 0.48, hr * 0.42), sash, 16, 8)  # 面纱
        for s in (-1, 1):  # 腰间飘带
            limb(b, [P(0.05, s * 0.18, z0 + 0.5), P(-0.15, s * 0.25, z0 + 0.4), P(-0.38, s * 0.3, z0 + 0.42)],
                 0.035, 0.015, sash, 10)
        for s, sh in zip((1, -1), k["shoulder"]):
            hand = P(0.3, s * 0.34, z0 + 0.55 + (0.2 if s > 0 else 0))
            arm(b, sh, P(0.12, s * 0.36, z0 + 0.65), hand, suit, "robe_black")
            curved_blade(b, hand, star, side=s)

    posed(b, body, lean=0.15)


def blade_singer(b, star=1):  # 塞伦 · 影歌剑士：银白长发、紫色轻甲长摆，双手持一柄长刃，身边绕着剑气飘带；★2 银饰，★3 金边披风，★4 发光剑纹，★5 金刃、星辉剑气
    star_base(b, 0.75, star)
    k = elf(b, star, "elf_purple", legs="elf_dark", hair_c="silver", hair_style="long", head_r=0.24, pad="silver")
    h, hr, z0 = k["head"], k["head_r"], k["z0"]
    for f in (0.17, -0.17):  # 长摆
        b.rbox(P(f, 0, z0 + 0.32), (0.24, 0.04, 0.42), "elf_dark", 0.3)
        if star >= 2:
            b.rbox(P(f, 0, z0 + 0.12), (0.25, 0.045, 0.03), metal(star, ""), 0.4)
    S(b, P(0.03, 0, z0 + 0.72), (0.2, 0.15, 0.19), "silver" if star < 5 else "gold_light", 18, 10)
    rs, ls = k["shoulder"]
    hand = P(0.24, 0.08, z0 + 0.58)
    arm(b, rs, P(0.1, 0.3, z0 + 0.58), hand + Vector((0.03, 0, 0)), "elf_purple", "elf_dark")
    arm(b, ls, P(0.12, -0.28, z0 + 0.6), hand + Vector((-0.03, 0, -0.05)), "elf_purple", "elf_dark")
    sword(b, hand, star, length=0.62, blade="gold_light" if star >= 5 else "silver", guard=metal(star, "elf_dark"),
          glow_from=4, tilt=20)
    ribbon = GLOW if star >= 5 else "elf_purple"  # 绕身的剑气
    pts = [P(0.3 * math.cos(a), 0.3 * math.sin(a), z0 + 0.3 + 0.12 * a) for a in [i * 0.6 for i in range(9)]]
    b.chain(pts, 0.012, 0.004, ribbon, 6)


def moon_archer(b, star=1):  # 莉安 · 月光射手：银发兜帽，新月形长弓，箭头发光；★2 银饰，★3 金边披风，★4 月轮光环，★5 金弓、星纹斗篷
    star_base(b, 0.75, star)
    k = elf(b, star, "elf_purple", legs="elf_dark", hair_c="silver", hair_style="long", head_r=0.24)
    h, hr, z0 = k["head"], k["head_r"], k["z0"]
    hood(b, k, "royal" if star >= 5 else "elf_dark", star, trim_plain="elf_purple", up=False)
    quiver(b, z0, star, colour="elf_dark", flights="silver")
    rs, ls = k["shoulder"]
    lh = P(0.3, -0.38, z0 + 0.62)
    arm(b, ls, P(0.14, -0.38, z0 + 0.66), lh, "elf_purple", "elf_dark")
    # 新月弓：弓臂更弯，两端翘起
    wood = "gold" if star >= 5 else "silver"
    size = 0.46
    pts = [lh + Vector((0, -0.06, -size)), lh + Vector((0, -0.12, -size * 0.6)), lh, lh + Vector((0, -0.12, size * 0.6)),
           lh + Vector((0, -0.06, size))]
    b.chain(pts, 0.024, 0.024, wood, 8)
    R(b, pts[0], pts[-1], 0.004, 0.004, "white", 4)
    for p in (pts[0], pts[-1]):
        R(b, p, p + Vector((0, -0.07, 0)), 0.02, 0.004, wood, 6)
    rh = P(0.16, -0.22, z0 + 0.62)
    arm(b, rs, P(0.0, 0.2, z0 + 0.7), rh, "elf_purple", "elf_dark")
    R(b, rh + Vector((0, 0.04, 0)), lh + Vector((0, -0.22, 0)), 0.008, 0.008, "silver", 6)
    S(b, lh + Vector((0, -0.24, 0)), (0.025, 0.025, 0.025), GLOW, 8, 6)
    if star >= 4:
        b.ring(h + Vector((0, 0.2, hr * 0.4)), hr * 1.3, 0.012, "gold_light" if star >= 5 else "silver", axis="f",
               segments=28, sides=4)


def druid(b, star=1):  # 卡蕾 · 德鲁伊：鹿角头冠、叶片长袍，藤蔓法杖，手边缠着藤蔓；★2 银饰，★3 金边花环披风，★4 发光花朵，★5 金鹿角、满身花
    star_base(b, 0.75, star)
    k = elf(b, star, "leaf", robe="leaf", boots="bark", hair_c="hair_red", hair_style="long", belt="bark",
            cape_c="moss", cape5="elf_purple")
    h, hr, z0 = k["head"], k["head_r"], k["z0"]
    for i in range(6):  # 叶片下摆
        a = i * 2 * math.pi / 6
        S(b, P(0.27 * math.cos(a), 0.27 * math.sin(a), z0 + 0.12), (0.09, 0.05, 0.12), "moss", 10, 6,
          rot=(0, 0, math.degrees(a)))
    antler = "gold" if star >= 5 else "bark"
    for s in (-1, 1):  # 鹿角
        base_p = h + Vector((s * hr * 0.45, 0.02, hr * 0.75))
        limb(b, [base_p, base_p + Vector((s * 0.08, 0.02, 0.14)), base_p + Vector((s * 0.12, 0.05, 0.26))], 0.025,
             0.01, antler, 8)
        R(b, base_p + Vector((s * 0.08, 0.02, 0.14)), base_p + Vector((s * 0.18, -0.02, 0.18)), 0.015, 0.004, antler,
          6)
    rs, ls = k["shoulder"]
    rh = P(0.15, 0.32, z0 + 0.62)
    arm(b, rs, P(0.05, 0.32, z0 + 0.65), rh, "leaf")
    top = staff(b, rh, z0, star, wood="bark", height=1.35)
    for i in range(3):  # 杖顶缠绕的藤蔓和叶子
        a = i * 2 * math.pi / 3
        S(b, top + Vector((0.07 * math.cos(a), 0.07 * math.sin(a), 0.05)), (0.06, 0.03, 0.04), "leaf", 8, 6,
          rot=(0, 0, math.degrees(a)))
    gem(b, top + Vector((0, 0, 0.1)), (0.04, 0.04, 0.06), "moss" if star < 3 else GLOW)
    lh = P(0.26, -0.32, z0 + 0.72)
    arm(b, ls, P(0.1, -0.36, z0 + 0.64), lh, "leaf")
    vine = [lh + Vector((0.08 * math.cos(t), 0.08 * math.sin(t) - 0.02, 0.04 * t)) for t in [i * 0.8 for i in range(8)]]
    b.chain(vine, 0.012, 0.006, "moss", 6)
    flowers = 2 if star < 3 else (4 if star < 5 else 7)
    for i in range(flowers):
        a = i * 2.4
        c = P(0.26 * math.cos(a), 0.26 * math.sin(a), z0 + 0.25 + 0.08 * (i % 3))
        S(b, c, (0.03, 0.03, 0.025), GLOW if star >= 4 else "white", 8, 6)


def wind_rider(b, star=1):  # 埃兰 · 风鹰骑手（悬空）：骑白色风鹰的精灵，发带飘扬，手持长枪；★2 银鞍扣，★3 金饰披风，★4 鹰额宝石，★5 金冠风鹰、蓝金鞍
    seat = bird(b, 0.42, body="white", wing="silver", head_c="white", lion=False, star=star, span=0.85)
    k = seated(b, seat, "elf_purple", legs="elf_dark", boots="elf_dark", skin="elf_skin", brows="hair_blond",
               head_r=0.2)
    h, hr, z0 = k["head"], k["head_r"], k["z0"]
    dress(b, k, star, plain="elf_dark", cape_colour="elf_dark", cape5="royal", emblem_at=P(0.14, 0, z0 + 0.72),
          cape_f0=-0.19)
    ears(b, k, "elf_skin")
    hair(b, k, "hair_blond", "long")
    b.ring(h + Vector((0, 0.02, hr * 0.5)), hr * 1.0, 0.014, metal(star, "elf_purple"), segments=24, sides=4)
    for s in (-1, 1):  # 飘扬的发带
        limb(b, [h + Vector((s * 0.03, hr * 0.95, hr * 0.5)), h + Vector((s * 0.06, hr * 1.7, hr * 0.6)),
                 h + Vector((s * 0.08, hr * 2.4, hr * 0.4))], 0.02, 0.01, "elf_purple", 8)
    rs, ls = k["shoulder"]
    lh = P(0.2, -0.14, z0 + 0.6)
    arm(b, ls, P(0.08, -0.28, z0 + 0.6), lh, "elf_purple", "elf_dark")
    R(b, lh, P(0.48, -0.08, 0.74), 0.008, 0.008, "leather", 4)
    rh = P(0.12, 0.3, z0 + 0.7)
    arm(b, rs, P(0.0, 0.32, z0 + 0.62), rh, "elf_purple", "elf_dark")
    spear(b, rh, star, length=1.1, shaft="elf_dark", pennant="elf_purple")


def envoy(b, star=1):  # 维拉 · 使者：长裙披纱、金色长发，手捧盟约卷轴，背后的光翼饰；★2 银饰，★3 金边披风，★4 光翼，★5 白金长裙、星辉光翼
    star_base(b, 0.75, star)
    gown = "white" if star >= 5 else "elf_purple"
    k = elf(b, star, gown, robe=gown, boots="elf_dark", hair_c="hair_blond", hair_style="long", belt="gold" if star >= 3
            else "elf_dark", cape_c="elf_dark", cape5="royal")
    h, hr, z0 = k["head"], k["head_r"], k["z0"]
    if star >= 2:
        b.ring(P(0, 0, z0 + 0.1), 0.28, 0.018, metal(star, ""), segments=32, sides=6)
    b.rbox(P(0.2, 0, z0 + 0.32), (0.12, 0.04, 0.55), "elf_dark" if star < 5 else "gold_light", 0.4, rot=(0, -14, 0))
    S(b, P(0, 0, z0 + 0.86), (0.25, 0.21, 0.08), "white", 20, 8)  # 披纱
    hands = []
    for s, sh in zip((1, -1), k["shoulder"]):
        hand = P(0.28, s * 0.1, z0 + 0.64)
        arm(b, sh, P(0.12, s * 0.3, z0 + 0.6), hand, gown)
        hands.append(hand)
    c = (hands[0] + hands[1]) / 2 + Vector((0.02, -0.04, 0.04))  # 卷轴
    R(b, c + Vector((-0.14, 0, 0)), c + Vector((0.14, 0, 0)), 0.035, 0.035, "feather", 12)
    for dx in (-0.15, 0.15):
        S(b, c + Vector((dx, 0, 0)), (0.02, 0.04, 0.04), metal(star, "wood"), 8, 6)
    b.rbox(c + Vector((0, -0.03, -0.07)), (0.22, 0.01, 0.12), "feather", 0.3)
    S(b, c + Vector((0, -0.045, -0.07)), (0.03, 0.01, 0.03), "crimson", 8, 6)  # 封蜡
    if star >= 4:  # 背后的光翼饰
        for s in (-1, 1):
            for i in range(3):
                o = P(-0.18, s * 0.08, z0 + 0.88)
                R(b, o, o + Vector((s * (0.4 - i * 0.08), 0.12, 0.3 - i * 0.18)), 0.03, 0.006,
                  GLOW if star >= 5 else "gold_light", 8)


def star_speaker(b, star=1):  # 伊娜 · 星语者：星纹深色长袍、新月头饰，星辰法杖，身边漂浮星星；★2 银饰，★3 金边披风，★4 星环，★5 金星袍、三颗星
    star_base(b, 0.75, star)
    k = elf(b, star, "elf_dark", robe="elf_dark", boots="royal", hair_c="silver", hair_style="bun",
            belt="royal", cape_c="royal", cape5="elf_purple")
    h, hr, z0 = k["head"], k["head_r"], k["z0"]
    if star >= 2:
        b.ring(P(0, 0, z0 + 0.1), 0.28, 0.018, metal(star, ""), segments=32, sides=6)
    for i in range(5 if star < 5 else 9):  # 袍上的星点
        a = i * 1.3
        S(b, P(0.27 * math.cos(a) * (0.8 + 0.1 * (i % 3)), 0.27 * math.sin(a), z0 + 0.12 + 0.07 * (i % 4)),
          (0.016, 0.016, 0.016), "gold_light", 6, 4)
    crescent = [h + Vector((0.12 * math.cos(a), -hr * 0.2, hr * 0.95 + 0.12 * math.sin(a)))
                for a in [math.pi * (0.15 + 0.7 * i / 6) for i in range(7)]]
    b.chain(crescent, 0.014, 0.014, metal(star, "silver") if star > 1 else "silver", 6)
    rs, ls = k["shoulder"]
    rh = P(0.15, 0.32, z0 + 0.62)
    arm(b, rs, P(0.05, 0.32, z0 + 0.65), rh, "elf_dark")
    top = staff(b, rh, z0, star, wood="silver", height=1.4)
    sc = top + Vector((0, 0, 0.13))
    for i in range(5):  # 五角星
        a = math.pi / 2 + i * 2 * math.pi / 5
        R(b, sc, sc + Vector((0.11 * math.cos(a), 0, 0.11 * math.sin(a))), 0.035, 0.005, "gold_light", 6)
    gem(b, sc, (0.035, 0.035, 0.035))
    if star >= 4:
        b.ring(sc, 0.15, 0.008, GLOW, axis="f", segments=24, sides=4)
    lh = P(0.26, -0.32, z0 + 0.72)
    arm(b, ls, P(0.1, -0.36, z0 + 0.64), lh, "elf_dark")
    n = 1 if star < 3 else (2 if star < 5 else 3)
    for i in range(n):
        S(b, lh + Vector((0.05 * (i - 1), -0.03, 0.15 + 0.1 * i)), (0.035, 0.035, 0.035), GLOW, 8, 6)


def sentinel(b, star=1):  # 法埃 · 林间哨兵：叶纹轻甲、半盔，右手举标枪朝天，背着标枪袋，肩上停着猫头鹰；★2 银饰，★3 金边披风，★4 发光枪头，★5 金甲
    star_base(b, 0.75, star)
    armor = "gold_light" if star >= 5 else "silver"
    k = elf(b, star, "leaf", legs="elf_dark", hair_c="hair_brown", hair_style="pony", head_r=0.24, pad=armor,
            cape_c="elf_dark", cape5="royal")
    h, hr, z0 = k["head"], k["head_r"], k["z0"]
    S(b, P(0.03, 0, z0 + 0.72), (0.2, 0.15, 0.19), armor, 18, 10)
    for i in range(3):  # 胸甲叶纹
        S(b, P(0.2, -0.06 + i * 0.06, z0 + 0.7), (0.025, 0.01, 0.05), "leaf", 8, 6)
    S(b, h + Vector((0, hr * 0.3, hr * 0.42)), (hr * 1.02, hr * 0.85, hr * 0.72), armor, 20, 10)  # 半盔
    R(b, h + Vector((0, hr * 0.1, hr * 1.05)), h + Vector((0, hr * 0.8, hr * 1.3)), 0.04, 0.008, armor, 8)
    rs, ls = k["shoulder"]
    rh = P(0.05, 0.3, z0 + 1.15)
    arm(b, rs, P(-0.02, 0.36, z0 + 0.95), rh, "leaf", "elf_dark")
    d = Vector((0, -0.6, 0.8)).normalized()
    tail = rh - d * 0.35
    tip = rh + d * 0.55
    R(b, tail, tip, 0.014, 0.012, "elf_dark", 8)
    R(b, tip, tip + d * 0.14, 0.035, 0.003, GLOW if star >= 4 else metal(star, "steel"), 8)
    lh = P(0.24, -0.3, z0 + 0.52)
    arm(b, ls, P(0.08, -0.34, z0 + 0.62), lh, "leaf", "elf_dark")
    R(b, P(-0.2, 0.1, z0 + 0.4), P(-0.22, 0.14, z0 + 0.9), 0.06, 0.065, "elf_dark", 12)  # 标枪袋
    for i in range(3):
        R(b, P(-0.21, 0.08 + i * 0.04, z0 + 0.88), P(-0.25, 0.08 + i * 0.04, z0 + 1.2), 0.01, 0.01, "elf_dark", 6)
        R(b, P(-0.25, 0.08 + i * 0.04, z0 + 1.2), P(-0.26, 0.08 + i * 0.04, z0 + 1.28), 0.02, 0.002, "silver", 6)
    c = ls + Vector((0, 0.02, 0.14))  # 猫头鹰
    S(b, c, (0.07, 0.07, 0.09), "hair_brown", 12, 8)
    S(b, c + Vector((0, -0.01, 0.1)), (0.065, 0.06, 0.055), "hair_brown", 12, 8)
    for s in (-1, 1):
        S(b, c + Vector((s * 0.03, -0.055, 0.1)), (0.022, 0.01, 0.022), "gold_light", 8, 6)
        R(b, c + Vector((s * 0.04, 0, 0.14)), c + Vector((s * 0.05, 0.0, 0.18)), 0.015, 0.003, "hair_brown", 6)


UNITS = [
    ("supply_ship", supply_ship), ("relay", relay), ("battleship", battleship), ("frigate", frigate),
    ("missile_ship", missile_ship), ("pd_ship", pd_ship), ("strike_craft", strike_craft),
    ("hive", hive), ("hive_node", hive_node), ("ripper", ripper), ("crawler", crawler),
    ("locust", locust), ("acid_turret", acid_turret), ("brood_mother", brood_mother),
    ("wreck_fortress", wreck_fortress),
    ("shield_guard", shield_guard), ("shadow_assassin", shadow_assassin), ("storm_mage", storm_mage),
    ("necromancer", necromancer), ("treant_guard", treant_guard),
    ("priest", priest), ("lancer", lancer), ("banner_bearer", banner_bearer), ("ballista", ballista),
    ("griffin_squire", griffin_squire), ("spirit_knight", spirit_knight),
    ("beast_tamer", beast_tamer), ("trapper", trapper), ("hunter", hunter), ("herbalist", herbalist),
    ("slinger", slinger), ("marksman", marksman),
    ("hexer", hexer), ("siege_golem", siege_golem), ("pyromancer", pyromancer), ("frost_mage", frost_mage),
    ("archmage", archmage), ("crystal_golem", crystal_golem), ("spell_knight", spell_knight),
    ("banshee", banshee), ("mana_leech", mana_leech), ("blood_knight", blood_knight),
    ("shadow_dancer", shadow_dancer), ("blade_singer", blade_singer), ("moon_archer", moon_archer), ("druid", druid),
    ("wind_rider", wind_rider), ("envoy", envoy), ("star_speaker", star_speaker), ("sentinel", sentinel),
]

# 用卡通风格（平滑着色）的单位
SAMPLE_UNITS = ["shield_guard", "shadow_assassin", "storm_mage", "necromancer", "treant_guard"]

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
# 按阵营补齐的卡通角色，每个阵营出一张升星对比图 stars_<阵营>.png（文档里存的是压缩后的 jpg）
CARTOON_FACTIONS = [
    ("morningstar", ["priest", "lancer", "banner_bearer", "ballista", "griffin_squire", "spirit_knight"],
     PLAYER_GLOW),
    ("rangers", ["beast_tamer", "trapper", "hunter", "herbalist", "slinger", "marksman"], PLAYER_GLOW),
    ("silvermoon", ["hexer", "siege_golem", "pyromancer", "frost_mage", "archmage", "crystal_golem", "spell_knight"],
     PLAYER_GLOW),
    ("undead", ["banshee", "mana_leech", "blood_knight"], UNDEAD_GLOW),
    ("elves", ["shadow_dancer", "blade_singer", "moon_archer", "druid", "wind_rider", "envoy", "star_speaker",
               "sentinel"], ELF_GLOW),
]
LARGE_UNITS = {"spirit_knight", "siege_golem"}  # 2×2 卡通角色
SMOOTH_UNITS = set(SAMPLE_UNITS) | {u for _, group, _ in CARTOON_FACTIONS for u in group}


# ---- 骨骼和动画 ------------------------------------------------------------------
# 卡通角色带骨骼和动画一起导出（决策 46）。骨骼在建模时登记（Builder.bone），零件整块绑到最近的骨骼。
# 所有角色共用一套骨骼名字，所以动画按“动作风格”写一次就能套到每个角色上：
#   root（脚底，整体位移和倒下）、hips、spine、head、leg.R/L、arm_upper.R/L、arm_lower.R/L、cape，
#   飞骑另有 mount（坐骑身体）、mount_head、mount_tail、wing.R/L。
# 每段动画是一个 NLA 片段，导出成 FBX 里同名的动画：idle、walk、attack、hit、death（24 帧每秒）。
# 旋转写成“绕世界轴转多少度”：X 轴正方向 = 向前俯身（手臂、腿往后摆），Z 轴 = 扭身；
# 右侧手臂向外抬是绕 Y 轴负方向，左侧相反。位移按角色高度缩放（2×2 角色动作幅度跟着变大）。

ANIM_FPS = 24

# 攻击动作风格：slash 劈砍、thrust 突刺、punch 拳击、shoot 射击、throw 投掷、cast 施法
ATTACK_STYLE = {
    "shield_guard": "slash", "shadow_assassin": "slash", "storm_mage": "cast", "necromancer": "cast",
    "treant_guard": "punch",
    "priest": "cast", "lancer": "thrust", "banner_bearer": "slash", "ballista": "shoot", "griffin_squire": "slash",
    "spirit_knight": "slash",
    "beast_tamer": "cast", "trapper": "slash", "hunter": "shoot", "herbalist": "cast", "slinger": "throw",
    "marksman": "shoot",
    "hexer": "cast", "siege_golem": "punch", "pyromancer": "cast", "frost_mage": "cast", "archmage": "cast",
    "crystal_golem": "punch", "spell_knight": "slash",
    "banshee": "cast", "mana_leech": "cast", "blood_knight": "slash",
    "shadow_dancer": "slash", "blade_singer": "slash", "moon_archer": "shoot", "druid": "cast",
    "wind_rider": "thrust", "envoy": "cast", "star_speaker": "cast", "sentinel": "throw",
}


def _wing(up):
    """翅膀上扬 up 度（负数是下扑）：右翅绕 Y 负方向，左翅相反。"""
    return {"wing.R": [("Y", -up)], "wing.L": [("Y", up)]}


def _merge(*poses):
    out = {}
    for pose in poses:
        for bone, value in pose.items():
            rot, loc = out.get(bone, ([], (0, 0, 0)))
            if isinstance(value, tuple) and len(value) == 2:  # 已经合并过的（旋转, 位移）
                rot, loc = rot + list(value[0]), tuple(a + b for a, b in zip(loc, value[1]))
            elif isinstance(value, tuple):  # 位移
                loc = tuple(a + b for a, b in zip(loc, value))
            else:
                rot = rot + list(value)
            out[bone] = (rot, loc)
    return out


def _loc(f=0.0, x=0.0, z=0.0):
    """root / mount 的位移：f 向前、x 向右、z 向上（世界坐标，Blender 里向前是 -Y）。"""
    return (x, -f, z)


def clip_keys(clip, style, flyer, k):
    """返回 (帧数, [(帧, 姿势)])；姿势是 {骨骼: [(轴, 度)] 或位移}，k 是位移缩放。"""
    if clip == "idle":
        mid = {"root": _loc(z=0.012 * k), "spine": [("X", 2)], "head": [("X", -3), ("Z", 4)],
               "arm_upper.R": [("X", -4), ("Y", -3)], "arm_upper.L": [("X", -4), ("Y", 3)], "cape": [("X", 6)],
               "mount_tail": [("Z", 10)]}
        if not flyer:
            return 48, [(0, {}), (24, mid), (48, {})]
        up, down = _merge(_wing(28)), _merge(_wing(-12), {"mount": _loc(z=0.03 * k)})
        return 48, [(0, up), (12, down), (24, _merge(up, mid)), (36, down), (48, up)]
    if clip == "walk":
        lean = {"spine": [("X", 6)], "cape": [("X", 14)]}
        a = {"leg.R": [("X", -25)], "leg.L": [("X", 25)], "arm_upper.R": [("X", 20)], "arm_upper.L": [("X", -20)],
             "hips": [("Z", 5)]}
        b = {"leg.R": [("X", 25)], "leg.L": [("X", -25)], "arm_upper.R": [("X", -20)], "arm_upper.L": [("X", 20)],
             "hips": [("Z", -5)]}
        bob = {"root": _loc(z=0.03 * k)}
        if flyer:
            up, down = _merge(_wing(35), {"mount": [("X", 6)]}), _merge(_wing(-20), {"mount": [("X", 6)]}, bob)
            return 16, [(0, _merge(up, lean)), (4, _merge(down, lean)), (8, _merge(up, lean)),
                        (12, _merge(down, lean)), (16, _merge(up, lean))]
        return 16, [(0, _merge(a, lean)), (4, _merge(bob, lean)), (8, _merge(b, lean)), (12, _merge(bob, lean)),
                    (16, _merge(a, lean))]
    if clip == "attack":
        rider = {
            "slash": [(5, {"arm_upper.R": [("X", -125)], "arm_lower.R": [("X", -25)], "spine": [("X", -6), ("Z", 14)]}),
                      (8, {"arm_upper.R": [("X", 15)], "spine": [("X", 14), ("Z", -14)], "root": _loc(f=0.08 * k)})],
            "thrust": [(5, {"arm_upper.R": [("X", 25)], "spine": [("X", -5)], "root": _loc(f=-0.03 * k)}),
                       (8, {"arm_upper.R": [("X", -55)], "arm_lower.R": [("X", -15)], "spine": [("X", 10)],
                            "root": _loc(f=0.14 * k)})],
            "punch": [(4, {"arm_upper.R": [("X", 30)], "spine": [("Z", 15)]}),
                      (7, {"arm_upper.R": [("X", -85)], "arm_lower.R": [("X", -10)], "spine": [("X", 10), ("Z", -15)],
                           "root": _loc(f=0.06 * k)})],
            "shoot": [(5, {"arm_upper.R": [("X", 25)], "arm_lower.R": [("X", 30)], "arm_upper.L": [("X", -20)],
                           "spine": [("X", -3)]}),
                      (7, {"arm_upper.R": [("X", 5)], "arm_upper.L": [("X", -20)], "spine": [("X", -8)],
                           "root": _loc(f=-0.03 * k)})],
            "throw": [(5, {"arm_upper.R": [("X", -160)], "spine": [("X", -10)]}),
                      (8, {"arm_upper.R": [("X", -40)], "spine": [("X", 12)], "root": _loc(f=0.05 * k)})],
            "cast": [(6, {"arm_upper.R": [("X", -90)], "arm_upper.L": [("X", -90)], "arm_lower.R": [("X", -20)],
                          "arm_lower.L": [("X", -20)], "spine": [("X", -8)], "head": [("X", -8)],
                          "root": _loc(z=0.03 * k)}),
                     (9, {"arm_upper.R": [("X", -60)], "arm_upper.L": [("X", -60)], "spine": [("X", 8)],
                          "head": [("X", 4)]})],
        }[style]
        keys = [(0, {})] + rider + [(14, {})]
        if flyer:  # 坐骑先抬头扬翅，再俯冲
            dive = {6: _merge(_wing(40), {"mount": [("X", -15)]}),
                    9: _merge(_wing(-25), {"mount": [("X", 25)], "root": _loc(f=0.2 * k, z=-0.05 * k)})}
            keys = [(0, {})] + [(f, _merge(p, dive.get(6 if f < 7 else 9, {}))) for f, p in rider] + [(14, {})]
        return 14, keys
    if clip == "hit":
        hit = {"spine": [("X", -14)], "head": [("X", -12)], "root": _loc(f=-0.05 * k), "arm_upper.R": [("X", 10)],
               "arm_upper.L": [("X", 10)], "mount": [("X", -10)]}
        return 8, [(0, {}), (2, hit), (8, {})]
    if clip == "death":
        if flyer:
            fall = _merge({"root": [("X", -60)]}, {"root": _loc(z=-0.6 * k)}, _wing(-35),
                          {"spine": [("X", -20)], "arm_upper.R": [("X", -50)], "arm_upper.L": [("X", -50)]})
        else:
            fall = {"root": [("X", -88)], "spine": [("X", -10)], "head": [("X", -15)],
                    "arm_upper.R": [("X", -40), ("Y", -30)], "arm_upper.L": [("X", -40), ("Y", 30)],
                    "cape": [("X", -30)]}
        return 24, [(0, {}), (6, {"spine": [("X", -15)], "head": [("X", -10)], "root": _loc(f=-0.04 * k)}),
                    (18, fall), (24, fall)]
    raise ValueError(clip)


ANIM_CLIPS = ("idle", "walk", "attack", "hit", "death")


def rig(obj, bones):
    """按登记的骨骼建骨架，网格挂上 Armature 修改器（顶点组在 Builder.finish 里已经按骨骼分好）。"""
    data = bpy.data.armatures.new(obj.name + "_rig")
    arm_obj = bpy.data.objects.new(obj.name + "_rig", data)
    bpy.context.scene.collection.objects.link(arm_obj)
    bpy.ops.object.select_all(action="DESELECT")
    bpy.context.view_layer.objects.active = arm_obj
    arm_obj.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    for name, head, tail, parent in bones:
        eb = data.edit_bones.new(name)
        eb.head = head
        eb.tail = tail if (tail - head).length > 0.01 else head + Vector((0, 0, 0.05))
        if parent:
            eb.parent = data.edit_bones[parent]
    bpy.ops.object.mode_set(mode="OBJECT")
    obj.parent = arm_obj
    mod = obj.modifiers.new("Armature", "ARMATURE")
    mod.object = arm_obj
    return arm_obj


def animate(arm_obj, style, flyer, k):
    """给骨架做 ANIM_CLIPS 里的几段动画，每段放进一条 NLA 轨道（导出成同名动画）。"""
    scene = bpy.context.scene
    scene.render.fps = ANIM_FPS
    arm_obj.animation_data_create()
    rest = {pb.name: pb.bone.matrix_local.to_3x3() for pb in arm_obj.pose.bones}
    start = 0
    for clip in ANIM_CLIPS:
        length, keys = clip_keys(clip, style, flyer, k)
        action = bpy.data.actions.new(arm_obj.name + "|" + clip)
        arm_obj.animation_data.action = action
        for frame, pose in keys:
            pose = _merge(pose)
            for pb in arm_obj.pose.bones:
                rot, loc = pose.get(pb.name, ([], (0, 0, 0)))
                m = rest[pb.name]
                q = Quaternion()
                for axis, deg in rot:
                    q = Quaternion(Vector({"X": (1, 0, 0), "Y": (0, 1, 0), "Z": (0, 0, 1)}[axis]),
                                   math.radians(deg)) @ q
                # 绕世界轴的旋转换到骨骼自己的坐标系
                local = m.inverted() @ q.to_matrix() @ m
                pb.rotation_mode = "QUATERNION"
                pb.rotation_quaternion = local.to_quaternion()
                pb.location = m.inverted() @ Vector(loc)
                pb.keyframe_insert("rotation_quaternion", frame=frame)
                pb.keyframe_insert("location", frame=frame)
        arm_obj.animation_data.action = None
        track = arm_obj.animation_data.nla_tracks.new()
        track.name = clip
        strip = track.strips.new(clip, start, action)
        strip.extrapolation = "NOTHING"
        start += length + 10
    for pb in arm_obj.pose.bones:
        pb.rotation_quaternion = Quaternion()
        pb.location = Vector((0, 0, 0))


# ---- 卡通角色的烘焙贴图和描边 ------------------------------------------------------
# 色块贴图只有平涂颜色。卡通角色另外展开一套 UV，把“颜色 × 环境光遮蔽（AO）”烘焙进角色专属贴图
# <单位 id>_tex.png（客户端 ModelLibrary 优先用它），缝隙、腋下、帽檐下会自然变暗，体积感更强。
# 描边用“反向外壳”：沿法线外扩一层、翻转朝向的网格，用深色；Unity 默认剔除背面，所以只在轮廓处露出一圈线。

BAKE_SIZE = 768
FAST = False  # --fast：试做用，烘焙只采样 8 次
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
    scene.cycles.samples = 8 if FAST else 128
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
    deform = b.verts.layers.deform.active
    first = len(b.faces)
    src = list(b.faces)
    vmap = {}
    for v in list(b.verts):
        nv = vmap[v] = b.verts.new(v.co + v.normal * OUTLINE_WIDTH)
        if deform is not None:  # 外壳跟着原来的骨骼动
            for group, weight in v[deform].items():
                nv[deform][group] = weight
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
    mat.diffuse_color = (*rgb, 1.0)
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
    glow_mat.diffuse_color = (*rgb, 1.0)  # Workbench 动画预览用


def export_fbx(obj, path):
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    if obj.parent is not None and obj.parent.type == "ARMATURE":
        # 带骨骼的角色：骨架、网格和 NLA 里的每段动画一起导出。不烘焙坐标变换（对骨骼动画不可靠），
        # Unity 会在模型根节点上补一个 -90° 的旋转，效果一样
        obj.parent.select_set(True)
        bpy.ops.export_scene.fbx(
            filepath=path, use_selection=True, object_types={"ARMATURE", "MESH"}, apply_unit_scale=True,
            apply_scale_options="FBX_SCALE_ALL", bake_space_transform=False, axis_forward="-Z", axis_up="Y",
            mesh_smooth_type="FACE", use_mesh_modifiers=False, add_leaf_bones=False, use_armature_deform_only=False,
            armature_nodetype="NULL", primary_bone_axis="Y", secondary_bone_axis="X",
            bake_anim=True, bake_anim_use_all_bones=True, bake_anim_use_nla_strips=True,
            bake_anim_use_all_actions=False, bake_anim_force_startend_keying=True, bake_anim_step=1.0,
            bake_anim_simplify_factor=0.0, path_mode="STRIP", embed_textures=False)
        return
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=True, object_types={"MESH"}, apply_unit_scale=True,
        apply_scale_options="FBX_SCALE_ALL", bake_space_transform=True, axis_forward="-Z", axis_up="Y",
        mesh_smooth_type="FACE", use_mesh_modifiers=True, add_leaf_bones=False, bake_anim=False,
        path_mode="STRIP", embed_textures=False)


def render_sheet(objects, path, spacing=4.6, turn=90, margin=4.5, px=320, height_px=480):
    """把一组模型排成一排，从战斗镜头的角度（侧前方斜看）渲染一张预览图。turn=90 时正面朝画面右侧。"""
    scene = bpy.context.scene
    for i, obj in enumerate(objects):
        _mover(obj).location = Vector((i * spacing, 0, 0))
        _mover(obj).rotation_euler = (0, 0, math.radians(turn))
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
    from PIL import Image, ImageChops
    bg = image.getpixel((0, 0))
    diff = ImageChops.difference(image, Image.new(image.mode, image.size, bg)).convert("L")
    box = diff.point(lambda v: 255 if v > 12 else 0).getbbox()
    if not box:
        return image
    return image.crop((0, max(0, box[1] - pad), image.width, min(image.height, box[3] + pad)))


def render_star_sheet(built, unit_ids, path):
    """升星对比图：每名角色一行，从左到右 ★1 到 ★5。每行另存在 rows/<单位 id>.png，
    这次没重新生成的角色沿用上次的行图，所以分批生成也能拼出整个阵营的对比图。"""
    from PIL import Image
    rows_dir = os.path.join(os.path.dirname(path), "rows")
    os.makedirs(rows_dir, exist_ok=True)
    rows = []
    for unit_id in unit_ids:
        row_path = os.path.join(rows_dir, unit_id + ".png")
        ids = [model_id(unit_id, star) for star in STAR_TIERS]
        if all(i in built for i in ids):
            for obj, _ in built.values():
                _show(obj, False)
            objects = [built[i][0] for i in ids]
            for obj in objects:
                _show(obj, True)
            k = 2.0 if unit_id in LARGE_UNITS else 1.0  # 2×2 角色按比例拉远镜头
            render_sheet(objects, row_path, spacing=2.2 * k, turn=35, margin=1.6 * k, px=600, height_px=1000)
            _trim_rows(Image.open(row_path).convert("RGB")).save(row_path)
        if os.path.exists(row_path):
            rows.append(Image.open(row_path).convert("RGB"))
    if not rows:
        return
    sheet = Image.new("RGB", (rows[0].width, sum(r.height for r in rows)))
    y = 0
    for r in rows:
        sheet.paste(r, (0, y))
        y += r.height
    sheet.save(path)
    print("已渲染预览", os.path.basename(path))

BASE_MODELS = (("base", 0.75), ("base_large", 1.6))  # 1 格 / 2×2 角色的展示底座


def export_bases(palette):
    """展示用底座：base_s<星级>（1 格）和 base_large_s<星级>（2×2），顶面高 0.08，角色站在顶面上。
    只在展示、升星、图鉴界面用，战斗里不放底座。"""
    for name, r in BASE_MODELS:
        for star in STAR_TIERS:
            hull, glow = make_materials(palette)
            b = Builder()
            star_base(b, r, star)
            mid = "%s_s%d" % (name, star)
            obj = b.finish(mid, hull, glow, smooth=True)
            export_fbx(obj, os.path.join(OUT_DIR, mid + ".fbx"))
            bpy.data.objects.remove(obj)


def preview_base(obj, spec, palette, glow):
    """预览图里给角色垫上底座（不导出），挂在骨架下跟着角色一起摆放。"""
    r, star, colour = spec
    hull, _ = make_materials(palette)
    b = Builder()
    star_base(b, r, star, colour)
    base_obj = b.finish(obj.name + "_base", hull, glow, smooth=True)
    base_obj.parent = obj.parent
    base_obj.location = (0, 0, -BASE_TOP)


BASE_TOP = 0.08


def _mover(obj):
    """摆放预览时要移动的物体：带骨骼的角色移动骨架。"""
    return obj.parent if obj.parent is not None else obj


def _show(obj, visible):
    """预览图里显示或隐藏一个模型（连同挂在同一骨架下的底座）。"""
    for o in (_mover(obj).children if obj.parent is not None else (obj,)):
        o.hide_render = not visible


# 动画预览：每种动作风格挑一名角色（★3），渲染成 GIF
ANIM_PREVIEW = ["shield_guard", "lancer", "priest", "hunter", "siege_golem", "griffin_squire"]


def render_anim_preview(built, unit_ids, out_prefix):
    """依次播放 idle、walk、attack、hit、death，几名角色并排，输出 <out_prefix>.gif。"""
    from PIL import Image
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"  # 云端没有显卡，Workbench / EEVEE 用不了；低采样的 Cycles 足够看动作
    scene.cycles.samples = 6
    scene.cycles.use_denoising = False
    for obj, _ in built.values():
        _show(obj, False)
    px, py = 360, 440
    scene.render.resolution_x, scene.render.resolution_y = px, py
    scene.render.film_transparent = False
    scene.view_settings.view_transform = "Standard"
    cam_data = bpy.data.cameras.new("AnimCam")
    cam_data.type = "ORTHO"
    cam = bpy.data.objects.new("AnimCam", cam_data)
    scene.collection.objects.link(cam)
    cam.rotation_euler = (math.radians(56), 0, 0)
    scene.camera = cam
    lights = []
    for name, energy, rot in (("Sun", 3.5, (50, 10, -35)), ("Fill", 1.2, (60, 0, 150))):
        light = bpy.data.objects.new(name, bpy.data.lights.new(name, "SUN"))
        light.data.energy = energy
        light.rotation_euler = tuple(math.radians(a) for a in rot)
        scene.collection.objects.link(light)
        lights.append(light)
    columns = []
    for unit_id in unit_ids:
        obj = built[model_id(unit_id, 3)][0]
        rig_obj = obj.parent
        _show(obj, True)
        for child in rig_obj.children:  # 动画预览模拟战斗：不放底座
            if child.name.endswith("_base"):
                child.hide_render = True
        rig_obj.location = (0, 0, 0)
        rig_obj.rotation_euler = (0, 0, math.radians(35))
        k = 2.0 if unit_id in LARGE_UNITS else 1.0
        cam_data.ortho_scale = 3.0 * k
        cam.location = Vector((0, -13, 8.5 + 0.6 * k))
        frames = []
        for track in rig_obj.animation_data.nla_tracks:
            strip = track.strips[0]
            step = 1 if track.name in ("attack", "hit") else 2
            f = strip.frame_start
            while f <= strip.frame_end:
                scene.frame_set(int(f))
                path = out_prefix + ".frame.png"
                scene.render.filepath = path
                bpy.ops.render.render(write_still=True)
                frames.append((Image.open(path).convert("RGB"), step))
                f += step
            if track.name == "death":  # 倒下后停一会
                frames.append((frames[-1][0], 12))
        os.remove(out_prefix + ".frame.png")
        columns.append(frames)
        _show(obj, False)
    n = min(len(c) for c in columns)
    out = []
    durations = []
    for i in range(n):
        sheet = Image.new("RGB", (px * len(columns), py))
        for j, c in enumerate(columns):
            sheet.paste(c[i][0], (j * px, 0))
        out.append(sheet.quantize(colors=128))
        durations.append(int(1000 * columns[0][i][1] / ANIM_FPS))
    out[0].save(out_prefix + ".gif", save_all=True, append_images=out[1:], duration=durations, loop=0)
    for o in [cam] + lights:
        bpy.data.objects.remove(o)
    print("已渲染动画预览", os.path.basename(out_prefix) + ".gif")



def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else sys.argv[1:]
    preview_dir = None
    if "--preview" in argv:
        preview_dir = os.path.abspath(argv[argv.index("--preview") + 1])
    only = set(argv[argv.index("--only") + 1].split(",")) if "--only" in argv else None
    global OUT_DIR, FAST
    if "--out" in argv:  # 试做时输出到别处，不覆盖工程里的模型
        OUT_DIR = os.path.abspath(argv[argv.index("--out") + 1])
    FAST = "--fast" in argv

    bpy.ops.wm.read_factory_settings(use_empty=True)
    os.makedirs(OUT_DIR, exist_ok=True)
    palette = build_palette_image()
    palette.filepath_raw = os.path.join(OUT_DIR, "Palette.png")
    palette.file_format = "PNG"
    palette.save()

    built = {}
    outlines = {}
    unit_of = {}
    bases = {}  # 模型 → 底座规格（预览图里给角色垫上底座，模拟展示界面）
    if not only or "bases" in only:
        export_bases(palette)
    for unit_id, fn in UNITS:
        if only and unit_id not in only:
            continue
        cartoon = unit_id in SMOOTH_UNITS
        for star in (STAR_TIERS if cartoon else (1,)):
            mid = model_id(unit_id, star)
            hull, glow = make_materials(palette)
            b = Builder()
            if cartoon:
                b.draw_base = False  # 战斗里不要底座；底座单独导出，展示界面再加
                fn(b, star)
                b.ground()
            else:
                fn(b)
            flyer = b.has_bone("mount")
            height = max(v.co.z for v in b.bm.verts)
            bones = [list(bn) for bn in b.bones]
            obj = b.finish(mid, hull, glow, smooth=cartoon)
            if cartoon:
                outline_v = bake_texture(obj, mid, OUT_DIR)
                outlines[mid] = add_outline(obj, outline_v)
                rig_obj = rig(obj, bones)
                animate(rig_obj, ATTACK_STYLE.get(unit_id, "slash"), flyer, height / 1.6)
                if b.base_spec and not flyer:
                    bases[mid] = b.base_spec
            export_fbx(obj, os.path.join(OUT_DIR, mid + ".fbx"))
            built[mid] = (obj, glow)
            unit_of[mid] = unit_id
            print("已导出 %s（%d 个三角面）" % (mid, sum(len(p.vertices) - 2 for p in obj.data.polygons)))

    if preview_dir:
        os.makedirs(preview_dir, exist_ok=True)
        sheets = PREVIEW_SHEETS + [("samples", [u for u, _ in PREVIEW_SAMPLES], None)]
        side_glow = dict(PREVIEW_SAMPLES)
        for _, group, colour in PREVIEW_SHEETS + CARTOON_FACTIONS:
            side_glow.update((u, colour) for u in group)
        for mid, (obj, glow) in built.items():
            unit_id = unit_of[mid]
            set_preview_glow(glow, PREVIEW_GLOW.get(unit_id, side_glow[unit_id]))
            if mid in outlines:
                outline_preview_material(obj, outlines[mid])
            if mid in bases:
                preview_base(obj, bases[mid], palette, glow)
        for name, group, _ in sheets:
            if any(u not in built for u in group):
                continue
            for obj, _ in built.values():
                _show(obj, False)
            objects = [built[u][0] for u in group]
            for obj in objects:
                _show(obj, True)
            if name == "samples":  # 1 格角色的近景，侧前方 3/4 视角
                render_sheet(objects, os.path.join(preview_dir, name + ".png"), spacing=2.2, turn=35, margin=1.6,
                             px=600, height_px=1000)
            else:
                render_sheet(objects, os.path.join(preview_dir, name + ".png"))
            print("已渲染预览", name)
        render_star_sheet(built, [u for u, _ in PREVIEW_SAMPLES], os.path.join(preview_dir, "stars.png"))
        for name, group, _ in CARTOON_FACTIONS:
            render_star_sheet(built, group, os.path.join(preview_dir, "stars_%s.png" % name))
        anim_units = [u for u in ANIM_PREVIEW if model_id(u, 3) in built]
        if anim_units:
            render_anim_preview(built, anim_units, os.path.join(preview_dir, "anim"))


if __name__ == "__main__":
    main()
