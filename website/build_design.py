"""从设计文档生成官网的“设计资料”页 website/design/index.html。

用法：python3 website/build_design.py [docs 目录，默认仓库的 docs/]

角色名录从 characters.md（每名角色的定位、加入章节和第 1 段小传）和 art-direction.md（造型）
自动生成；其余栏目写在下面的模板里。只放不剧透的内容：不放第二幕以后的剧情反转、结局、
雷震英灵和传说角色、具体数值、商业和内部决策。
"""
import html
import pathlib
import re
import sys

HERE = pathlib.Path(__file__).resolve().parent
DOCS = pathlib.Path(sys.argv[1]) if len(sys.argv) > 1 else HERE.parent / "docs"

FACTIONS = [
    # 名字, 方位, 主色, 发光色, 主题
    ("守星军", "北 · 玄武", "#2B3A4A", "#E8C77A", "守护"),
    ("逐星骑", "西 · 白虎", "#9A5A34", "#E6E2D6", "自由"),
    ("司星监", "南 · 朱雀", "#C8402F", "#F08A4B", "知识"),
    ("墨蚀者", "中 · 永夜", "#4A4560", "#9C7BD8", "救赎"),
    ("青丘灵族", "东 · 青龙", "#4F8A7A", "#9FD8D0", "信任"),
]
# 会剧透第二幕以后剧情的角色，不放进名录
HIDDEN = {"雷震"}


def looks():
    """art-direction.md 里“名字 · 职业（id）| 造型”的表格行。"""
    text = (DOCS / "art-direction.md").read_text(encoding="utf-8")
    out = {}
    for m in re.finditer(r"^\|[^|]*\|\s*(\S+) · [^（|]+（[^）]*）\s*\|\s*([^|]+?)\s*\|$", text, re.M):
        out[m.group(1)] = m.group(2)
    # 第一幕单位表“| 玄甲将（battleship） | 造型 | 识别特征 |”：岳擎、凌霄等直接用这些造型，按职业名对上
    for m in re.finditer(r"^\|\s*([^|（\s]+)（[a-z_]+）\s*\|\s*([^|]+?)\s*\|\s*([^|]+?)\s*\|$", text, re.M):
        feature = re.sub(r"（隐藏特性[^）]*）", "", m.group(3))
        out.setdefault("@" + m.group(1), f"{m.group(2)}；{feature}")
    return out


def characters():
    text = (DOCS / "characters.md").read_text(encoding="utf-8")
    text = text.split("## 传说角色")[0]
    result = {f[0]: [] for f in FACTIONS}
    faction = None
    for block in re.split(r"^(?=## |### )", text, flags=re.M):
        if block.startswith("## "):
            name = block[3:].split("\n")[0].strip()
            faction = name if name in result else None
            continue
        if not block.startswith("### ") or faction is None:
            continue
        title = block[4:].split("\n")[0].strip()
        name, _, role = title.partition(" · ")
        if name in HIDDEN:
            continue
        lines = [l.strip() for l in block.split("\n")[1:] if l.strip()]
        meta = lines[0] if lines and not lines[0].startswith("1.") else ""
        first = next((l[2:].strip() for l in lines if l.startswith("1.")), "")
        result[faction].append((name, role, meta, first))
    return result


def esc(s):
    return html.escape(s, quote=False)


def roster():
    look = looks()
    parts = []
    for fname, direction, color, glow, theme in FACTIONS:
        cards = []
        for name, role, meta, first in characters()[fname]:
            desc = look.get(name) or look.get("@" + role)
            extra = f'<p class="look">造型：{esc(desc)}</p>' if desc else ""
            cards.append(
                f'<article class="ch"><h4>{esc(name)}<span>{esc(role)}</span></h4>'
                f'<div class="meta">{esc(meta)}</div><p>{esc(first)}</p>{extra}</article>')
        parts.append(
            f'<section class="fac-block" style="--fc:{color};--fg:{glow}">'
            f'<h3><i></i>{fname}<small>{direction} · {theme}</small></h3>'
            f'<div class="chs">{"".join(cards)}</div></section>')
    return "\n".join(parts)


def main():
    page = TEMPLATE.replace("__ROSTER__", roster())
    out = HERE / "design" / "index.html"
    out.parent.mkdir(exist_ok=True)
    out.write_text(page, encoding="utf-8")
    print(out, len(page.encode("utf-8")), "bytes")


TEMPLATE = r"""<!doctype html>
<html lang="zh-CN">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1, viewport-fit=cover">
<title>星线 · 设计资料</title>
<meta name="description" content="《星线》的世界观、五方阵营、角色名录、玩法规则和美术风格。">
<meta name="theme-color" content="#141A2A">
<link rel="icon" href="../favicon.svg" type="image/svg+xml">
<!-- 由 website/build_design.py 生成，改内容请改脚本或设计文档后重新生成 -->
<style>
:root {
  --paper: #F3EAD8; --paper-deep: #E8DCC2; --ink: #1E2230; --night: #141A2A; --night-2: #232B40;
  --gold: #C9A14A; --gold-hi: #E8C77A; --cinnabar: #C8402F; --azure: #2E6E8E; --muted: #7A6F5E; --body: #4a4436;
  --serif: "Noto Serif SC", "Source Han Serif SC", "Songti SC", "STSong", "SimSun", serif;
  --sans: "Noto Sans SC", "PingFang SC", "Microsoft YaHei", system-ui, sans-serif;
}
* { box-sizing: border-box; }
html { scroll-behavior: smooth; scroll-padding-top: 64px; }
body { margin: 0; background: var(--paper); color: var(--ink); font-family: var(--sans); line-height: 1.75; -webkit-font-smoothing: antialiased; }
a { color: inherit; }
h1, h2, h3, h4 { font-family: var(--serif); margin: 0; }
.nav { position: sticky; top: 0; z-index: 10; display: flex; align-items: center; gap: 24px; padding: 12px 20px; background: rgba(20, 26, 42, .95); color: var(--paper); }
.brand { font-family: var(--serif); letter-spacing: .2em; text-decoration: none; color: var(--gold-hi); font-size: 18px; white-space: nowrap; }
.nav ul { display: flex; gap: 18px; list-style: none; margin: 0; padding: 0; font-size: 14px; overflow-x: auto; scrollbar-width: none; }
.nav ul a { text-decoration: none; opacity: .8; white-space: nowrap; }
.nav ul a:hover { opacity: 1; color: var(--gold-hi); }
header.top { background: radial-gradient(ellipse at 50% 0%, var(--night-2), var(--night) 70%); color: var(--paper); text-align: center; padding: 64px 20px 56px; }
header.top h1 { font-size: clamp(34px, 7vw, 56px); letter-spacing: .2em; color: var(--gold-hi); }
header.top p { color: var(--paper); opacity: .75; max-width: 600px; margin: 12px auto 0; font-size: 15px; }
main { max-width: 1000px; margin: 0 auto; padding: 0 20px 80px; }
section.doc { padding-top: 64px; }
section.doc > h2 { font-size: clamp(24px, 4.4vw, 32px); letter-spacing: .1em; padding-bottom: 10px; border-bottom: 1px solid var(--ink); margin-bottom: 22px; }
section.doc > h2 small { font-family: var(--sans); font-size: 13px; color: var(--gold); letter-spacing: .3em; margin-left: 12px; font-weight: 400; }
h3 { font-size: 20px; letter-spacing: .06em; margin: 28px 0 10px; }
p { margin: 0 0 1em; color: var(--body); }
em { font-style: normal; color: var(--cinnabar); }
.table { overflow-x: auto; margin: 0 0 1.2em; }
table { border-collapse: collapse; width: 100%; font-size: 15px; background: #F7F0E2; }
th, td { border: 1px solid rgba(30, 34, 48, .25); padding: 9px 12px; text-align: left; vertical-align: top; }
td:first-child { white-space: nowrap; }
th { background: var(--paper-deep); font-family: var(--serif); white-space: nowrap; }
ul.list { padding-left: 1.2em; margin: 0 0 1em; color: var(--body); }
ul.list li { margin-bottom: .35em; }
.note { font-size: 13px; color: var(--muted); border-left: 3px solid var(--gold); padding: 4px 12px; background: rgba(201, 161, 74, .08); }
.fac-block { margin-top: 32px; }
.fac-block h3 { display: flex; align-items: center; gap: 10px; }
.fac-block h3 i { width: 18px; height: 18px; border-radius: 50%; background: var(--fc); box-shadow: 0 0 0 3px var(--paper), 0 0 0 4px var(--fg); }
.fac-block h3 small { font-family: var(--sans); font-size: 13px; color: var(--muted); font-weight: 400; letter-spacing: .1em; }
.chs { display: grid; grid-template-columns: repeat(auto-fill, minmax(280px, 1fr)); gap: 14px; }
.ch { background: #F7F0E2; border: 1px solid rgba(30, 34, 48, .3); border-top: 3px solid var(--fc); padding: 14px 16px; }
.ch h4 { font-size: 18px; }
.ch h4 span { font-family: var(--sans); font-size: 13px; color: var(--muted); font-weight: 400; margin-left: 8px; }
.ch .meta { font-size: 12px; color: var(--gold); margin: 2px 0 8px; letter-spacing: .05em; }
.ch p { font-size: 14px; margin: 0 0 .6em; }
.ch p.look { font-size: 13px; color: var(--muted); margin: 0; }
.swatches { display: grid; grid-template-columns: repeat(auto-fill, minmax(150px, 1fr)); gap: 12px; margin-bottom: 1.2em; }
.sw { border: 1px solid rgba(30, 34, 48, .25); background: #F7F0E2; font-size: 13px; }
.sw b { display: block; height: 56px; }
.sw span { display: block; padding: 6px 10px; color: var(--body); }
.sw code { color: var(--muted); font-size: 12px; }
.ring { font-family: var(--serif); font-size: 17px; text-align: center; padding: 14px; background: #F7F0E2; border: 1px solid rgba(30, 34, 48, .25); margin-bottom: 1em; }
.ring b { color: var(--cinnabar); font-weight: 400; margin: 0 6px; }
footer { background: var(--ink); color: rgba(243, 234, 216, .6); text-align: center; padding: 28px 20px; font-size: 13px; }
footer a { color: var(--gold-hi); }
@media (max-width: 600px) {
  .nav { gap: 14px; }
  th, td { padding: 7px 9px; font-size: 14px; }
}
</style>
</head>
<body>
<nav class="nav">
  <a class="brand" href="../">← 星线</a>
  <ul>
    <li><a href="#world">世界观</a></li>
    <li><a href="#factions">五方阵营</a></li>
    <li><a href="#story">第一幕</a></li>
    <li><a href="#roster">角色名录</a></li>
    <li><a href="#rules">玩法规则</a></li>
    <li><a href="#growth">养成</a></li>
    <li><a href="#art">美术风格</a></li>
  </ul>
</nav>
<header class="top">
  <h1>设计资料</h1>
  <p>《星线》正在开发中，这里整理了目前的世界观、角色和玩法设计。名字和规则都还是草案，会随开发和测试调整，以正式上线版本为准。</p>
</header>
<main>

<section class="doc" id="world">
  <h2>世界观<small>WORLD</small></h2>
  <h3>星线是一道封印</h3>
  <p>一千年前，大地深处醒来一股吞食灵力的饥饿，人们叫它<em>永夜</em>。天上的织女带着人间的术士和青丘的灵族，把二十八星宿落下的碎星——<em>星石</em>——一颗颗立在九州大地上，照着天上星宿的样子连成<em>星络</em>，把永夜压在大地尽头。这群人叫<em>织网者</em>，这张网就是<em>星线</em>。</p>
  <p>千年过去，人们忘了星线是封印，只记得它是城池的灯火、结界和田地的来源。青丘灵族记得，但他们不再信任贪图灵力的人，退回了山林。</p>
  <h3>断线日</h3>
  <p>星历四百一十二年，司星监的前任监正打开了初源星石。星线从源头开始被永夜吸干，这一天叫<em>断线日</em>。失去灵力的城池结界崩塌，永夜的墨色漫进城里，被墨色沾上的人成了<em>墨蚀者</em>：他们不是天生的妖魔，而是陷落城池的百姓和守军，记忆被墨色封住，只剩下饥饿。</p>
  <h3>望星城与持线人</h3>
  <p>星线最末端的边城望星城，靠一座观星台撑了下来。观星台的星石里住着一缕星魂<em>阿织</em>，她能预见战局，却记不起自己是谁。断线日那一夜，观星台也选中了一个人：只有这个人听得见阿织的声音。这个人就是玩家，望星城新任的<em>持线人</em>。持线人不配语音、不定性别，要沿着星线一段段打回去，把星石重新点亮，把各方的人连在一起。</p>
  <h3>五根线</h3>
  <p>阿织说，重织星线需要五根线，五方各出一根。主界面的星图上从一开始就有五个空的星宿位，每拿到一根亮一个。最意外的是“救赎”：墨蚀者也能成为一根线。</p>
  <div class="table"><table>
    <tr><th>线</th><th>阵营</th><th>主题</th></tr>
    <tr><td>守护</td><td>守星军</td><td>宁可自己倒下，也不让星石熄灭</td></tr>
    <tr><td>自由</td><td>逐星骑</td><td>只为自己认的人拼命</td></tr>
    <tr><td>知识</td><td>司星监</td><td>力量来自理解，也会被它反噬</td></tr>
    <tr><td>救赎</td><td>墨蚀者</td><td>想起自己是谁，就能选择站在哪一边</td></tr>
    <tr><td>信任</td><td>青丘灵族</td><td>一千年前的约，要不要再立一次</td></tr>
  </table></div>
</section>

<section class="doc" id="factions">
  <h2>五方阵营<small>FACTIONS</small></h2>
  <div class="table"><table>
    <tr><th>阵营</th><th>方位</th><th>是谁</th><th>和持线人的关系</th></tr>
    <tr><td>守星军</td><td>北 · 玄武</td><td>望星城的守军，由老将军雷震带领，旗上绣玄武</td><td>开局就在身边</td></tr>
    <tr><td>逐星骑</td><td>西 · 白虎</td><td>散落在边州的猎户、斥候和游侠，旗上绣白虎</td><td>第一章起陆续加入</td></tr>
    <tr><td>司星监</td><td>南 · 朱雀</td><td>观星的术士，掌管星线的学问；断线日后半数术士失踪，旗上绣朱雀</td><td>第二幕加入</td></tr>
    <tr><td>墨蚀者</td><td>中</td><td>被永夜墨色侵蚀的陷落城池之民</td><td>先是敌人；洗去墨色、唤醒记忆后加入</td></tr>
    <tr><td>青丘灵族</td><td>东 · 青龙方位</td><td>山林里的狐族和花木之灵，织网者的另一半</td><td>先因误会交战，第三幕结盟</td></tr>
  </table></div>
  <h3>阵营克制</h3>
  <p>五个阵营首尾相连、各克制一个，克制只带来小幅伤害加成，让“看敌方阵容换阵”多一个理由，又不压过站位和星络。</p>
  <div class="ring">守星军<b>→</b>墨蚀者<b>→</b>司星监<b>→</b>青丘灵族<b>→</b>逐星骑<b>→</b>守星军</div>
  <ul class="list">
    <li>守星军镇守星石，星光克制墨色</li>
    <li>墨蚀者以灵力为食，克制术士</li>
    <li>司星监的星术照破青丘的隐身</li>
    <li>山林是青丘的主场</li>
    <li>逐星骑轻骑绕后，重甲转身慢</li>
  </ul>
</section>

<section class="doc" id="story">
  <h2>第一幕 · 边州<small>ACT I</small></h2>
  <p>主线分三幕九章，每一幕回答一个大问题，同时抛出下一个。首发是第一幕三章，第一幕的问题是：<em>墨蚀者到底是什么？</em></p>
  <div class="table"><table>
    <tr><th>章</th><th>这一章发生什么</th><th>章末 Boss</th></tr>
    <tr><td>1 望星城</td><td>第一次出城，点亮最近的星石。只有持线人听得见阿织，阿织说“要五根线”。点亮的星石里传出墨蚀者的低语：“让我们回家。”</td><td>蚀爪头目</td></tr>
    <tr><td>2 白虎岭</td><td>逐星骑在山岭里独自抵抗墨蚀者，答应合作。陆离和燕回有一笔旧账，孟婉在找她的妹妹。</td><td>墨聚巨灵莫大</td></tr>
    <tr><td>3 墨城</td><td>攻下墨蚀者用陷落城池改成的墨城。林大师在废墟里唤醒了第一个墨蚀者灰爪：墨蚀者是陷落城池的人，能想起自己是谁。</td><td>墨城</td></tr>
  </table></div>
  <p class="note">第二、三幕的剧情留到游戏里揭晓。</p>
</section>

<section class="doc" id="roster">
  <h2>角色名录<small>CHARACTERS</small></h2>
  <p>角色不分敌我：墨蚀者和青丘灵族在剧情前半段是对手，打赢、唤醒或结盟后都能招募。每名角色有一段五节的个人小故事，招募时能看第一节，每升一星解锁一节。下面是每名角色的第一节。</p>
  <p class="note">定位：轻装、重装、远程、支援、对空、飞骑；“2×2”表示大型单位占四格。还有几名隐藏角色不在这里列出。</p>
__ROSTER__
</section>

<section class="doc" id="rules">
  <h2>玩法规则<small>RULES</small></h2>
  <h3>布阵</h3>
  <ul class="list">
    <li>竖向战场：我方在下、敌方在上，前排隔着中间的接触线相对。</li>
    <li>每方前排 5 格、纵深 3~4 格，一格要够手指点准。小型和中型角色占 1 格，大型单位占 2×2。</li>
    <li>上阵人数受指挥点上限限制。可以一键上阵，或沿用上次阵容直接开战。</li>
    <li>前排适合重装，先接敌；后排放远程和术士；两翼适合速度快、能绕后的飞骑。</li>
  </ul>
  <h3>星络</h3>
  <ul class="list">
    <li>每场战斗己方后方有一座观星台，是星络的源头。</li>
    <li>布阵时可以在格子上放星石（占一格，不能和角色同格）。两颗星石在同一行或同一列、相距不超过 3 格时自动连线。</li>
    <li>星石周围一圈是覆盖范围。角色在范围里，并且这片范围能沿连线连回观星台，就算<em>接入星络</em>：灵力持续补充，护盾正常恢复。</li>
    <li>没接入的角色只有自身的灵力，用完就停手，护盾也不再恢复。</li>
    <li>战斗中星石可以被摧毁，下游的星石和角色全部断开。墨蚀者也有自己的网络（墨祭坛和墨碑），布阵时看得到，可以规划切断路线。</li>
  </ul>
  <p>于是阵型紧凑容易覆盖，但怕范围攻击；分散需要更多星石。派鹤骑绕后切断敌方网络，敌方后排用光灵力就失去威胁。</p>
  <h3>自动战斗</h3>
  <ul class="list">
    <li>角色按自己的索敌偏好自动选目标：鹤骑优先攻击星石和远程，连弩手优先打飞骑，其余攻击最近的敌人。</li>
    <li>默认 2 倍速，可以切换或跳过；一场最长 45 秒，到时比较双方剩余耐久。</li>
    <li>持线人带 1~2 个技能，按钮在屏幕下方，单手好按，消耗随时间恢复的灵力点数。例如“星落”砸向选定的区域，“玄武盾”给全队恢复护盾。</li>
  </ul>
  <h3>战前推演</h3>
  <p>布阵后点一下推演，用不同的随机种子模拟几十场，给出胜率、平均用时、常见败因和阿织的建议。推演不算持线人技能；敌方部分单位有隐藏特性，推演按默认值计算，所以有误差。真实战斗使用推演中没用过的随机种子。</p>
  <h3>四种玩法</h3>
  <div class="table"><table>
    <tr><th>玩法</th><th>节奏</th><th>说明</th></tr>
    <tr><td>主线</td><td>一关 1~2 分钟</td><td>三幕九章，跟着剧情结识伙伴；打过的关卡可以重复挑战，刷经验和碎片</td></tr>
    <tr><td>远征</td><td>一趟 8~10 分钟，可中途退出</td><td>带自己培养的伙伴闯随机路线：三幕各两个节点加一个 Boss，沿途三选一拿技能和祝福</td></tr>
    <tr><td>摘星楼</td><td>一层 1~2 分钟</td><td>一层一场，敌方阵容完全公开，失败无限重试；越往上规则越刁钻，强度统一、不看养成</td></tr>
    <tr><td>天工坊</td><td>局外</td><td>研究战斗中缴获的墨晶，看破墨蚀者的隐藏特性，推演更准</td></tr>
  </table></div>
</section>

<section class="doc" id="growth">
  <h2>养成<small>GROWTH</small></h2>
  <ul class="list">
    <li><b>获得角色</b>：剧情里每章固定加入几名；其余角色从对应章节起掉落碎片，集齐后招募。敌对阵营的角色主要靠碎片。</li>
    <li><b>等级</b>：关卡给经验，打过的关卡可以重复挑战。星级决定等级上限。</li>
    <li><b>星级</b>：一星到五星，用本角色的碎片升星。每升一星属性提高，解锁一个符文槽或强化专属技能，再解锁一节个人小故事。</li>
    <li><b>升星外观</b>：每升一星换一身更华丽的装束和装备，体型也略微变大。</li>
    <li><b>羁绊</b>：同阵营或同职业的不同角色凑够人数就激活。五个阵营羁绊、六个职业羁绊（轻装、重装、飞骑、对空、远程、支援），只做加成，不改定位。敌方也吃羁绊。</li>
    <li><b>离线收益</b>：上下班各打开一次就能领。</li>
  </ul>
  <h3>不卖战力</h3>
  <p>没有体力，没有每日任务，没有付费抽卡，也不卖碎片、经验和金币。看广告只换便利：关卡奖励翻倍、失败后直接重打、远征和商店多刷新一次，不看也不耽误进度。</p>
</section>

<section class="doc" id="art">
  <h2>美术风格<small>ART</small></h2>
  <p>“墨蓝夜空上的宣纸棋盘”：圆润卡通的东方角色，每个角色像一枚摆在宣纸棋盘上的棋子；星络是画面里最亮的金线。参考方向是国潮插画加手游 Q 版，不是写实古装，也不是水墨写意。</p>
  <ul class="list">
    <li><b>竖屏、小屏先看清</b>：一名角色在手机上只有三四十像素高，每个兵种靠一个夸张的特征认出来：玄甲将的长戟和披风、游侠的斗笠和长弓、星术士的星盘和高冠。</li>
    <li><b>颜色就是阵营</b>：衣服大块用阵营色，敌我在棋盘上一眼分开。</li>
    <li><b>星络是光</b>：每名角色脚下一圈光环，接入星络时常亮，断开时变暗、闪朱砂红。</li>
    <li><b>纹样点到为止</b>：云纹、回纹、星宿连线纹只用在饰边、底座和面板角上。</li>
    <li><b>墨蚀者</b>：被墨色侵蚀的人，墨色剪影加发光的眼睛；唤醒后墨色褪去，露出原来的衣着。</li>
    <li><b>造型</b>：头身比约 1:3，大头、大眼睛、圆球手，轮廓有一圈墨色描边。</li>
  </ul>
  <h3>色板</h3>
  <div class="swatches">
    <div class="sw"><b style="background:#F3EAD8"></b><span>宣纸<br><code>#F3EAD8</code></span></div>
    <div class="sw"><b style="background:#1E2230"></b><span>墨<br><code>#1E2230</code></span></div>
    <div class="sw"><b style="background:#141A2A"></b><span>夜空<br><code>#141A2A</code></span></div>
    <div class="sw"><b style="background:#C9A14A"></b><span>金 · 星络<br><code>#C9A14A</code></span></div>
    <div class="sw"><b style="background:#C8402F"></b><span>朱砂 · 敌方<br><code>#C8402F</code></span></div>
    <div class="sw"><b style="background:#2E6E8E"></b><span>石青 · 我方<br><code>#2E6E8E</code></span></div>
    <div class="sw"><b style="background:#7A5BA6"></b><span>紫檀 · 推演<br><code>#7A5BA6</code></span></div>
    <div class="sw"><b style="background:#5E9C6A"></b><span>松绿 · 胜利<br><code>#5E9C6A</code></span></div>
  </div>
  <h3>阵营色</h3>
  <div class="table"><table>
    <tr><th>阵营</th><th>主色 / 辅色</th><th>发光</th></tr>
    <tr><td>守星军</td><td>玄青 / 石青，铁甲</td><td>金</td></tr>
    <tr><td>逐星骑</td><td>月白 / 赭石，皮革</td><td>金</td></tr>
    <tr><td>司星监</td><td>朱砂 / 金</td><td>橘红</td></tr>
    <tr><td>青丘灵族</td><td>青绿 / 月白，藤叶</td><td>月青</td></tr>
    <tr><td>墨蚀者</td><td>墨 / 灰紫</td><td>幽紫（眼睛和墨痕）</td></tr>
  </table></div>
  <h3>界面</h3>
  <p>夜景（登录、主界面）用墨蓝夜空和金色星宿连线，夜空里的星宿连线就是菜单；局内面板用宣纸底加一像素墨线，四角各一个小云纹角花。主按钮朱砂底宣纸色字，主要按钮放在屏幕下半部分，大拇指够得着。音乐用民乐配器。</p>
</section>

</main>
<footer>
  <p>© 星线 Starline · 独立游戏开发中 · 设计资料为开发中的草案</p>
  <p><a href="../">返回首页</a></p>
</footer>
</body>
</html>
"""

if __name__ == "__main__":
    main()
