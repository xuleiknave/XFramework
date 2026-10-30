"""从 website/source/roster.json 生成官网角色页 website/roster/index.html。

用法：python3 website/build_roster.py
名单的原稿在项目共享文件夹 website-source/roster.json。每一族里按卡片颜色（稀有度）分组；数值不公开。
"""
import html
import json
import pathlib

HERE = pathlib.Path(__file__).resolve().parent

FACTIONS = [
    # 族名, 副标题, 颜色, 一句话, 特色词条
    ("人族", "取经人和凡人", "#B07A2E", "取经人唐僧、路上的凡人和门神。没有法力，靠的是人心。", "同心 · 香火"),
    ("仙族", "尊神、神将和仙人", "#2E6E8E", "三清、佛门、天庭的尊神和神将星官，修道的仙人，有仙籍的悟空、八戒、沙僧，还有取经成功后成佛成正果的师徒。", "腾云 · 仙籍"),
    ("魔族", "人形的妖王", "#4A4560", "以人形出战的妖王和精怪，还有大闹天宫时的齐天大圣。", "法宝 · 占山"),
    ("兽族", "神兽、龙族和星宿", "#5E9C6A", "坐骑神兽、四海龙族和水族，还有现出本相出战的二十八宿。", "本相 · 四象"),
]
ROLES = ["轻装", "重装", "远程", "支援", "对空"]
# 卡片颜色从高到低
RARITIES = [("红", "#C8402F"), ("金", "#C9962A"), ("紫", "#8A5CC2"), ("蓝", "#3C7FC0"), ("绿", "#4E9A5A"), ("白", "#9A948A")]
RARITY_COLOR = dict(RARITIES)


def esc(s):
    return html.escape(s, quote=True)


def main():
    roster = json.loads((HERE / "source" / "roster.json").read_text(encoding="utf-8"))
    blocks = []
    for name, sub, color, intro, tags in FACTIONS:
        members = [c for c in roster if c["faction"] == name]
        parts = []
        for rar, rc in RARITIES:
            tier = [c for c in members if c["rarity"] == rar]
            if not tier:
                continue
            cards = "".join(
                f'<article class="ch" style="--rc:{rc}" data-role="{esc(c["role"])}" data-rar="{rar}" data-text="{esc(c["name"] + c["intro"] + c["group"])}">'
                f'<h4>{esc(c["name"])}</h4><span class="role">{esc(c["role"])}</span>'
                f'<p>{esc(c["intro"])}</p><span class="from">{esc(c["group"])}</span></article>'
                for c in tier)
            parts.append(f'<div class="grp" style="--rc:{rc}"><h4 class="gh"><b></b>{rar}卡<small>{len(tier)}</small></h4><div class="chs">{cards}</div></div>')
        blocks.append(
            f'<section class="fac" id="{name}" data-faction="{name}" style="--fc:{color}">'
            f'<header><h3><i></i>{name}<small>{sub} · {len(members)} 人</small></h3>'
            f'<p>{intro}<span class="tags">特色词条：{tags}</span></p></header>{"".join(parts)}</section>')
    fac_buttons = "".join(f'<button data-f="{n}" style="--fc:{c}">{n}</button>' for n, _, c, _, _ in FACTIONS)
    role_buttons = "".join(f'<button data-r="{r}">{r}</button>' for r in ROLES)
    rar_buttons = "".join(f'<button data-q="{r}" style="--rc:{c}"><b></b>{r}卡</button>' for r, c in RARITIES)
    page = (TEMPLATE.replace("__COUNT__", str(len(roster)))
            .replace("__FACBTN__", fac_buttons).replace("__RARBTN__", rar_buttons).replace("__ROLEBTN__", role_buttons)
            .replace("__BLOCKS__", "\n".join(blocks)))
    out = HERE / "roster" / "index.html"
    out.parent.mkdir(exist_ok=True)
    out.write_text(page, encoding="utf-8")
    print(out, len(page.encode("utf-8")), "bytes")


TEMPLATE = r"""<!doctype html>
<html lang="zh-CN">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1, viewport-fit=cover">
<title>星线 · 角色名录</title>
<meta name="description" content="《星线》的 __COUNT__ 位西游原著人物：人族、仙族、魔族、兽族四族。">
<meta name="theme-color" content="#141A2A">
<link rel="icon" href="../favicon.svg" type="image/svg+xml">
<!-- 由 website/build_roster.py 生成，改名单请改 website/source/roster.json 后重新生成 -->
<style>
:root {
  --paper: #F3EAD8; --paper-deep: #E8DCC2; --ink: #1E2230; --night: #141A2A; --night-2: #232B40;
  --gold: #C9A14A; --gold-hi: #E8C77A; --cinnabar: #C8402F; --muted: #7A6F5E; --body: #4a4436;
  --serif: "Noto Serif SC", "Source Han Serif SC", "Songti SC", "STSong", "SimSun", serif;
  --sans: "Noto Sans SC", "PingFang SC", "Microsoft YaHei", system-ui, sans-serif;
}
* { box-sizing: border-box; }
body { margin: 0; background: var(--paper); color: var(--ink); font-family: var(--sans); line-height: 1.7; -webkit-font-smoothing: antialiased; }
a { color: inherit; }
h1, h3, h4 { font-family: var(--serif); margin: 0; }
.nav { display: flex; align-items: center; gap: 20px; padding: 12px 20px; background: var(--night); color: var(--paper); font-size: 14px; }
.nav a { text-decoration: none; opacity: .85; white-space: nowrap; }
.nav .brand { font-family: var(--serif); letter-spacing: .2em; color: var(--gold-hi); font-size: 18px; opacity: 1; }
header.top { background: radial-gradient(ellipse at 50% 0%, var(--night-2), var(--night) 70%); color: var(--paper); text-align: center; padding: 52px 20px 44px; }
header.top h1 { font-size: clamp(32px, 7vw, 52px); letter-spacing: .2em; color: var(--gold-hi); }
header.top p { opacity: .75; max-width: 620px; margin: 10px auto 0; font-size: 15px; }
.bar { --fc: var(--ink); --rc: var(--ink); position: sticky; top: 0; z-index: 5; background: rgba(243, 234, 216, .96); backdrop-filter: blur(4px); border-bottom: 1px solid rgba(30, 34, 48, .2); padding: 10px 20px; }
.bar .row { max-width: 1080px; margin: 0 auto; display: flex; flex-wrap: wrap; gap: 6px 8px; align-items: center; }
.bar .row + .row { margin-top: 6px; }
.bar button { font: inherit; font-size: 14px; border: 1px solid rgba(30, 34, 48, .35); background: #F7F0E2; color: var(--ink); padding: 3px 12px; border-radius: 999px; cursor: pointer; }
.bar button[data-f] { border-color: var(--fc); }
.bar button.on { background: var(--ink); color: var(--paper); border-color: var(--ink); }
.bar button[data-f].on { background: var(--fc); border-color: var(--fc); }
.bar input { font: inherit; font-size: 14px; padding: 4px 12px; border: 1px solid rgba(30, 34, 48, .35); border-radius: 999px; background: #fff; min-width: 0; flex: 1 1 160px; max-width: 260px; }
.bar .count { font-size: 13px; color: var(--muted); margin-left: auto; }
main { max-width: 1080px; margin: 0 auto; padding: 8px 20px 72px; }
.fac { padding-top: 36px; scroll-margin-top: 130px; }
.fac header h3 { display: flex; align-items: center; gap: 10px; font-size: 26px; letter-spacing: .08em; }
.fac header h3 i { width: 18px; height: 18px; border-radius: 50%; background: var(--fc); }
.fac header h3 small { font-family: var(--sans); font-size: 13px; color: var(--muted); font-weight: 400; letter-spacing: .05em; }
.fac header p { color: var(--body); margin: 6px 0 4px; font-size: 15px; }
.fac header .tags { display: inline-block; margin-left: 10px; color: var(--fc); font-family: var(--serif); }
.grp { margin-top: 18px; }
.gh { display: flex; align-items: center; gap: 6px; font-size: 16px; color: var(--rc); border-bottom: 1px solid rgba(30, 34, 48, .15); padding-bottom: 4px; margin-bottom: 10px; }
.gh b, .bar button[data-q] b { width: 12px; height: 12px; border-radius: 3px; background: var(--rc); display: inline-block; }
.bar button[data-q] b { margin-right: 4px; vertical-align: -1px; }
.bar button[data-q].on { background: var(--rc); border-color: var(--rc); }
.gh small { font-family: var(--sans); font-size: 12px; color: var(--muted); font-weight: 400; margin-left: 6px; }
.chs { display: grid; grid-template-columns: repeat(auto-fill, minmax(230px, 1fr)); gap: 10px; }
.ch { background: #F7F0E2; border: 1px solid rgba(30, 34, 48, .22); border-left: 4px solid var(--rc); padding: 10px 12px 26px; position: relative; background-image: linear-gradient(90deg, color-mix(in srgb, var(--rc) 10%, transparent), transparent 60%); }
.ch h4 { font-size: 17px; padding-right: 44px; }
.ch .role { position: absolute; right: 10px; top: 11px; font-size: 12px; color: var(--muted); border: 1px solid rgba(30, 34, 48, .2); padding: 0 6px; border-radius: 4px; }
.ch p { margin: 4px 0 0; font-size: 13.5px; color: var(--body); }
.ch .from { position: absolute; left: 12px; bottom: 6px; font-size: 12px; color: var(--fc); }
.hide { display: none !important; }
.empty { text-align: center; color: var(--muted); padding: 48px 0; }
.note { font-size: 13px; color: var(--muted); border-left: 3px solid var(--gold); padding: 6px 12px; background: rgba(201, 161, 74, .08); margin-top: 24px; }
footer { background: var(--ink); color: rgba(243, 234, 216, .6); text-align: center; padding: 26px 20px; font-size: 13px; }
footer a { color: var(--gold-hi); }
@media (max-width: 600px) { .bar { position: static; } .bar .count { width: 100%; margin-left: 0; } .fac header .tags { display: block; margin-left: 0; } }
</style>
</head>
<body>
<nav class="nav"><a class="brand" href="../">← 星线</a><a href="../design/">设计资料</a><a href="../chat/">留言板</a></nav>
<header class="top">
  <h1>角色名录</h1>
  <p>__COUNT__ 位《西游记》原著人物，分成人、仙、魔、兽四族，每族里按卡片颜色排列。名单还是草案，开发中会增减；想看到谁，欢迎去留言板说。</p>
</header>
<div class="bar">
  <div class="row"><button data-f="" class="on">全部</button>__FACBTN__</div>
  <div class="row"><button data-q="" class="on">全部颜色</button>__RARBTN__</div>
  <div class="row"><button data-r="" class="on">全部定位</button>__ROLEBTN__<input type="search" placeholder="搜名字或出处" aria-label="搜索角色"><span class="count"></span></div>
</div>
<main>
__BLOCKS__
<p class="empty hide">没有符合条件的角色。</p>
<p class="note">名字都取自原著，造型和技能是我们自己设计的。</p>
</main>
<footer><p>© 星线 Starline · 独立游戏开发中 · 角色名单为开发中的草案</p><p><a href="../">返回首页</a></p></footer>
<script>
(function () {
  var f = '', r = '', k = '', q = '';
  var cards = Array.prototype.slice.call(document.querySelectorAll('.ch'));
  var count = document.querySelector('.count');
  function apply() {
    var shown = 0;
    document.querySelectorAll('.fac').forEach(function (sec) {
      var any = 0;
      sec.querySelectorAll('.grp').forEach(function (g) {
        var n = 0;
        g.querySelectorAll('.ch').forEach(function (c) {
          var ok = (!f || sec.dataset.faction === f) && (!r || c.dataset.role === r) && (!k || c.dataset.rar === k) && (!q || c.dataset.text.indexOf(q) >= 0);
          c.classList.toggle('hide', !ok); if (ok) n++;
        });
        g.classList.toggle('hide', !n); any += n;
      });
      sec.classList.toggle('hide', !any); shown += any;
    });
    count.textContent = '显示 ' + shown + ' / ' + cards.length + ' 人';
    document.querySelector('.empty').classList.toggle('hide', shown > 0);
  }
  function group(attr, set) {
    var btns = document.querySelectorAll('.bar button[' + attr + ']');
    btns.forEach(function (b) {
      b.addEventListener('click', function () {
        btns.forEach(function (x) { x.classList.remove('on'); });
        b.classList.add('on'); set(b.getAttribute(attr)); apply();
      });
    });
  }
  group('data-f', function (v) { f = v; });
  group('data-r', function (v) { r = v; });
  group('data-q', function (v) { k = v; });
  document.querySelector('.bar input').addEventListener('input', function (e) { q = e.target.value.trim(); apply(); });
  var h = decodeURIComponent(location.hash.slice(1));
  if (h) { var b = document.querySelector('.bar button[data-f="' + h + '"]'); if (b) b.click(); }
  apply();
})();
</script>
</body>
</html>
"""

if __name__ == "__main__":
    main()
