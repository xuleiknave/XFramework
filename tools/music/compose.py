#!/usr/bin/env python3
"""
星线配乐：程序化生成全部背景音乐。

    python3 tools/music/compose.py              # 生成全部曲目
    python3 tools/music/compose.py title boss   # 只生成指定曲目
    python3 tools/music/compose.py --preview DIR  # 另存一份 mp3 试听

输出 Starline/Packages/com.starline.game/Runtime/Resources/StarlineMusic/<id>.ogg。
曲目清单、风格和每首的 AI 音乐工具提示词见 docs/music.md；
客户端按 Resources/StarlineMusic/music.json 选曲。

全部旋律围绕同一个“星线主题”（D 大调八小节）展开：主菜单完整呈现，地图改成
鲁特琴轻快版，草原战斗用圆号吹出片段，巫妖王用管风琴奏小调版，结局由合唱唱完。
所有随机都用固定种子，同一份代码每次输出相同。
"""

import argparse
import os
import subprocess
import sys
import tempfile
import time
import wave

import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import synth as S  # noqa: E402

REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT_DIR = os.path.join(REPO, "Starline", "Packages", "com.starline.game", "Runtime", "Resources", "StarlineMusic")


# ----------------------------------------------------------------------------
# 曲子

class Track:
    def __init__(self, bpm, bars, bpb=4, loop=True, tail=8.0, seed=1, rt60=2.4, wet=0.3, damp=0.45):
        self.bpm = bpm
        self.bars = bars
        self.bpb = bpb
        self.spb = 60.0 / bpm
        self.loop = loop
        self.n = int(round(bars * bpb * self.spb * S.SR))
        self.tail = int(tail * S.SR)
        self.rng = np.random.default_rng(seed)
        self.seed = seed
        self.rt60 = rt60
        self.wet = wet
        self.damp = damp
        self.buses = {}
        self._cache = {}

    @property
    def total_beats(self):
        return self.bars * self.bpb

    def bus(self, name, gain=1.0, send=0.3, hp=None, lp=None, delay=None):
        """delay=(拍数, 反馈, 湿度)。hp 缺省时，除低音和打击乐外都切掉低频，避免低中频发闷。"""
        if hp is None and name not in ("bass", "perc"):
            hp = 160 if name in ("pad", "ost", "brass") else 110
        self.buses[name] = dict(buf=np.zeros((2, self.n + self.tail)), gain=gain, send=send, hp=hp, lp=lp, delay=delay)

    def add(self, sig, beat, vel, bus, pan=0.0, human=0.0):
        t = beat * self.spb + (self.rng.normal(0, human) if human else 0.0)
        start = max(0, int(round(t * S.SR)))
        b = self.buses[bus]["buf"]
        end = min(b.shape[1], start + len(sig))
        if end <= start:
            return
        seg = sig[: end - start] * vel
        ang = (np.clip(pan, -1, 1) + 1) * np.pi / 4
        b[0, start:end] += seg * np.cos(ang)
        b[1, start:end] += seg * np.sin(ang)

    def note(self, inst, beat, dur, m, vel=0.8, bus="main", pan=0.0, human=0.006, **kw):
        sec = round(dur * self.spb, 3)
        key = (inst.__name__, m, sec, round(vel, 1), tuple(sorted(kw.items())))
        sig = self._cache.get(key)
        if sig is None:
            sig = inst(S.hz(m), sec, round(vel, 1), **kw)
            self._cache[key] = sig
        v = vel * (1 + self.rng.normal(0, 0.04)) if human else vel
        self.add(sig, beat, v, bus, pan, human)

    def play(self, inst, events, bus="main", vel=0.8, pan=0.0, semis=0, legato=1.0, offset=0.0, **kw):
        for s, d, ms in events:
            for i, m in enumerate(ms):
                p = pan if len(ms) == 1 else pan + (i - (len(ms) - 1) / 2) * 0.25
                self.note(inst, s + offset, d * legato, m + semis, vel, bus, p, **kw)

    def hit(self, kind, beat, vel=0.8, bus="perc", pan=0.0, variant=None, human=0.004):
        v = variant if variant is not None else int(self.rng.integers(0, 3))
        self.add(S.drum(kind, v), beat, vel, bus, pan, human)

    def pattern(self, kind, pat, start_bar, bars, step=0.25, vel=0.8, bus="perc", pan=0.0):
        """pat 是一小节（或几小节）的步进字符串：X 重音、x 普通、o 轻、. 空。"""
        pat = pat.replace(" ", "")
        levels = {"X": 1.0, "x": 0.72, "o": 0.42}
        span = len(pat) * step
        beat = start_bar * self.bpb
        end = (start_bar + bars) * self.bpb
        while beat < end - 1e-6:
            for i, ch in enumerate(pat):
                b = beat + i * step
                if ch in levels and b < end - 1e-6:
                    self.hit(kind, b, vel * levels[ch], bus, pan)
            beat += span

    def swell(self, beat, beats, vel=0.4, bus="perc"):
        sec = beats * self.spb
        sig = S.swell(sec, int(beat))
        self.add(sig, beat - beats, vel, bus)

    # -------------------------------------------------------------------- 渲染

    def render(self):
        n = self.n
        dry = np.zeros((2, n + self.tail))
        send = np.zeros((2, n + self.tail))
        for b in self.buses.values():
            x = b["buf"]
            if self.loop:
                x = self._fold(x)
            if b["hp"] or b["lp"]:
                x = S.fft_filter(x, b["hp"], b["lp"])
            if b["delay"]:
                beats, fb, mix = b["delay"]
                d = int(beats * self.spb * S.SR)
                acc = np.zeros_like(x)
                for k in range(1, 7):
                    shifted = np.roll(x, k * d, axis=1)
                    if not self.loop:
                        shifted[:, : k * d] = 0
                    # 乒乓延迟：奇数次回声偏左、偶数次偏右
                    shifted[0 if k % 2 else 1] *= 0.6
                    acc += shifted * fb ** k
                x = x + acc * mix
            if self.loop:
                x = np.concatenate([x, np.zeros((2, self.tail))], axis=1)
            dry += x * b["gain"]
            send += x * b["send"]
        ir = S.reverb_ir(self.rt60, damp=self.damp, seed=self.seed)
        if self.loop:
            dry = dry[:, :n]
            wet = S.convolve(send[:, :n], ir, circular=True)
            out = dry + wet * self.wet
        else:
            wet = S.convolve(send, ir, circular=False)
            out = np.zeros_like(wet)
            out[:, : dry.shape[1]] += dry
            out += wet * self.wet
            out = out[:, : self.n + int(3 * S.SR)]  # 短曲：最后一拍后最多留 3 秒余音
        out = S.fft_filter(out, lo=28)
        return master(out, fade=not self.loop)

    def _fold(self, x):
        n = self.n
        y = x[:, :n].copy()
        rest = x[:, n:]
        pos = 0
        while pos < rest.shape[1]:
            seg = rest[:, pos:pos + n]
            y[:, : seg.shape[1]] += seg
            pos += n
        return y


def master(x, target_rms=0.11, ceiling=0.95, fade=False):
    x = S.shelf_eq(x, low_hz=90, low_gain=0.55, high_hz=2800, high_gain=1.7)
    # 响度按 150 Hz 以上计算，低音重的曲子不会因此整体变小声
    rms = np.sqrt(np.mean(S.fft_filter(x, lo=150) ** 2)) or 1.0
    x = x * (target_rms / rms)
    knee = 0.6
    a = np.abs(x)
    over = a > knee
    x[over] = np.sign(x[over]) * (knee + (ceiling - knee) * np.tanh((a[over] - knee) / (ceiling - knee)))
    if fade:
        # 去掉尾部静音，最后 1.5 秒淡出
        level = np.max(np.abs(x), axis=0)
        idx = np.nonzero(level > 6e-3)[0]
        end = idx[-1] + 1 if len(idx) else x.shape[1]
        x = x[:, :end]
        f = min(int(1.5 * S.SR), end)
        x[:, end - f:] *= np.linspace(1, 0, f) ** 2
    return x


# ----------------------------------------------------------------------------
# 编配工具

def bars_of(prog, bpb):
    """['Dm', 'Bb C', ...] -> [(起拍, 拍数, 和弦)]；一格里写两个和弦就各占半小节。"""
    out = []
    for i, cell in enumerate(prog):
        syms = cell.split()
        each = bpb / len(syms)
        for j, s in enumerate(syms):
            out.append((i * bpb + j * each, each, s))
    return out


def pads(t, inst, prog, start_bar, center, n=3, vel=0.5, bus="pad", spread=0.6, **kw):
    for s, d, sym in bars_of(prog, t.bpb):
        notes = S.close_voicing(sym, center, n)
        for i, m in enumerate(notes):
            pan = (i - (n - 1) / 2) / max(n - 1, 1) * spread
            t.note(inst, start_bar * t.bpb + s, d, m, vel, bus, pan, **kw)


def arp(t, inst, prog, start_bar, pattern, step, center, vel=0.5, bus="arp", pan=0.0, sweep=0.0, dur=None, **kw):
    """pattern 是和弦音序号（两个八度共 6 个音，0 最低），None 为空拍。"""
    for s, d, sym in bars_of(prog, t.bpb):
        low = S.close_voicing(sym, center, 3)
        tones = low + [m + 12 for m in low]
        k = 0
        b = 0.0
        while b < d - 1e-6:
            idx = pattern[k % len(pattern)]
            if idx is not None:
                p = pan + sweep * np.sin(2 * np.pi * (start_bar * t.bpb + s + b) / (t.bpb * 2))
                t.note(inst, start_bar * t.bpb + s + b, dur or step, tones[idx % len(tones)], vel, bus, p, **kw)
            b += step
            k += 1


def bassline(t, inst, prog, start_bar, rhythm, step, lo=33, vel=0.7, bus="bass", dur=None, **kw):
    """rhythm 每个字符一步：R 根音、5 五度、8 高八度、- 延长、. 空。"""
    for s, d, sym in bars_of(prog, t.bpb):
        root = S.bass_note(sym, lo)
        steps = int(round(d / step))
        i = 0
        while i < steps:
            ch = rhythm[i % len(rhythm)]
            if ch in "R58":
                m = root + {"R": 0, "5": 7, "8": 12}[ch]
                j = i + 1
                while j < steps and rhythm[j % len(rhythm)] == "-":
                    j += 1
                t.note(inst, start_bar * t.bpb + s + i * step, dur or (j - i) * step, m, vel, bus, 0.0, **kw)
                i = j
            else:
                i += 1


SCALES = {
    "major": (0, 2, 4, 5, 7, 9, 11), "minor": (0, 2, 3, 5, 7, 8, 10), "dorian": (0, 2, 3, 5, 7, 9, 10),
    "phrygian": (0, 1, 3, 5, 7, 8, 10), "harmonic": (0, 2, 3, 5, 7, 8, 11), "hijaz": (0, 1, 4, 5, 7, 8, 10),
    "mixolydian": (0, 2, 4, 5, 7, 9, 10), "lydian": (0, 2, 4, 6, 7, 9, 11),
}

RHYTHMS = {
    4: ([[1, 1, 1, 1], [1.5, .5, 1, 1], [2, 1, 1], [.5, .5, 1, 2], [1, .5, .5, 2], [.75, .25, 1, 2],
         [1, 1, 2], [.5, .5, .5, .5, 1, 1], [1.5, .5, 2], [1, .5, .5, 1, 1]],
        [[4], [2, 2], [3, 1], [1, 1, 2]]),
    3: ([[1, 1, 1], [2, 1], [1.5, .5, 1], [.5, .5, 1, 1], [1, .5, .5, 1]], [[3], [2, 1]]),
    6: ([[3, 3], [2, 1, 3], [1, 1, 1, 3], [2, 1, 2, 1], [3, 1, 1, 1], [1, 2, 1, 2]], [[6], [3, 3]]),
}


def melody(t, prog, tonic, scale, seed, lo, hi, start_bar=0, busy=0.5):
    """按和弦生成旋律。八小节一句：动机 A（两小节）、B、A 的变奏、收束。"""
    rng = np.random.default_rng(seed)
    pcs = {(tonic + x) % 12 for x in SCALES[scale]}
    notes = [m for m in range(lo, hi + 1) if m % 12 in pcs]
    bank, cad = RHYTHMS[t.bpb]
    bank = sorted(bank, key=len)
    cut = max(2, int(len(bank) * (0.4 + busy * 0.6)))
    bank = bank[:cut] if busy < 0.5 else bank[len(bank) - cut:]
    chords = [cell.split()[0] for cell in prog]
    events = []
    cur = notes[len(notes) // 2]
    motif = None

    def snap_chord(m, sym):
        cp = S.chord_pcs(sym)
        cands = [x for x in range(lo, hi + 1) if x % 12 in cp]
        return min(cands, key=lambda x: (abs(x - m), x))

    def index_of(m):
        return min(range(len(notes)), key=lambda i: abs(notes[i] - m))

    def gen_bar(bar, rhythm, steps=None):
        nonlocal cur
        sym = chords[bar % len(chords)]
        out_steps = []
        b = 0.0
        for i, d in enumerate(rhythm):
            if i == 0:
                if steps is None:
                    m = snap_chord(cur, sym)
                else:
                    m = snap_chord(cur + steps[0], sym)
            else:
                idx = index_of(cur)
                if steps is None:
                    mv = rng.choice([-2, -1, 1, 2, 0, 3, -3], p=[.14, .3, .3, .14, .04, .04, .04])
                    if idx + mv < 2 or idx + mv > len(notes) - 3:
                        mv = -mv
                else:
                    mv = steps[i]
                m = notes[int(np.clip(idx + mv, 0, len(notes) - 1))]
                strong = abs(b - round(b)) < 1e-6 and (round(b) % max(1, t.bpb // 2) == 0)
                if d >= 1.5 or strong:
                    m = snap_chord(m, sym)
            out_steps.append((m - cur) if i == 0 else index_of(m) - index_of(cur))
            events.append(((start_bar + bar) * t.bpb + b, d, [m]))
            cur = m
            b += d
        return out_steps

    for bar in range(len(prog)):
        pos = bar % 8
        if pos in (0, 1):
            r = bank[rng.integers(len(bank))]
            st = gen_bar(bar, r)
            if pos == 0:
                motif = [(r, st)]
            else:
                motif.append((r, st))
        elif pos in (4, 5) and motif:
            r, st = motif[pos - 4]
            gen_bar(bar, r, st)
        elif pos == 7:
            r = cad[rng.integers(len(cad))]
            gen_bar(bar, r)
        else:
            gen_bar(bar, bank[rng.integers(len(bank))])
    # 收束音落在主音上
    if events:
        s, d, ms = events[-1]
        target = min((x for x in notes if x % 12 == tonic), key=lambda x: abs(x - ms[0]))
        events[-1] = (s, d, [target])
    return events


# 星线主题：D 大调八小节
THEME = "D5:1.5 A5:.5 A5:2 | G5:1 F#5:.5 E5:.5 F#5:2 | D5:1.5 B5:.5 A5:1 G5:1 | F#5:1 E5:1 E5:2 | " \
        "D5:1.5 A5:.5 A5:2 | B5:1 A5:.5 G5:.5 F#5:1 D5:1 | E5:1 F#5:.5 G5:.5 A5:1 G5:.5 E5:.5 | D5:4"
THEME_CHORDS = ["D", "Bm", "G", "A", "D", "G D", "Em A7", "D"]
THEME_CHORDS_MINOR = ["Dm", "Gm/Bb", "Gm", "A", "Dm", "Gm Dm", "Edim A7", "Dm"]


def theme(start_bar=0, semis=0, minor=False, bpb=4):
    ev, _ = S.seq(THEME, start_bar * bpb)
    if minor:
        ev = S.to_minor(ev, 2)
    return S.transpose(ev, semis)


def theme_chords(semis=0, minor=False):
    prog = THEME_CHORDS_MINOR if minor else THEME_CHORDS
    if not semis:
        return list(prog)
    names = ["C", "C#", "D", "Eb", "E", "F", "F#", "G", "Ab", "A", "Bb", "B"]
    out = []
    for cell in prog:
        syms = []
        for sym in cell.split():
            base, _, slash = sym.partition("/")
            i = 2 if len(base) > 1 and base[1] in "#b" else 1
            root = names[(S.pitch_class(base[:i]) + semis) % 12] + base[i:]
            if slash:
                root += "/" + names[(S.pitch_class(slash) + semis) % 12]
            syms.append(root)
        out.append(" ".join(syms))
    return out


# ----------------------------------------------------------------------------
# 曲目

TRACKS = {}


def track(fn):
    TRACKS[fn.__name__] = fn
    return fn


@track
def title():
    """主菜单《星线》：竖琴、长笛、弦乐和法力水晶的铃音。D 大调，76 BPM。"""
    t = Track(76, 24, rt60=3.2, wet=0.38, seed=11)
    t.bus("pad", 0.55, 0.45)
    t.bus("arp", 0.55, 0.4)
    t.bus("lead", 0.9, 0.35)
    t.bus("bass", 0.6, 0.2, lp=900)
    t.bus("bell", 0.35, 0.6, delay=(0.75, 0.4, 0.5))
    t.bus("perc", 0.5, 0.35)
    intro = ["D", "Bm", "G", "A"]
    outro = ["Bm", "G", "Em", "Asus4 A"]
    prog = intro + THEME_CHORDS + THEME_CHORDS + outro
    pads(t, S.strings, prog, 0, 62, 3, 0.32, "pad", a=0.9, r=1.4)
    bassline(t, S.strings, prog, 0, "R-------", 0.5, lo=38, vel=0.5, dark=True, a=0.5, r=1.0)
    arp(t, S.harp, prog[4:], 4, [0, 1, 2, 3, 4, 3, 2, 1], 0.5, 62, 0.42, "arp", sweep=0.3)
    for b in range(4):  # 前奏：水晶铃
        arp(t, S.crystal, [intro[b]], b, [3, None, 4, None, 5, None, 4, None], 0.5, 74, 0.3, "bell", pan=0.2)
    t.play(S.flute, theme(4), "lead", 0.75, pan=-0.1)
    t.play(S.strings, theme(12, -12), "lead", 0.6, pan=0.1, a=0.15, r=0.5)
    t.play(S.flute, S.transpose(theme(12), 0), "lead", 0.3, pan=-0.3, legato=0.9)
    pads(t, S.choir, THEME_CHORDS, 12, 66, 3, 0.22, "pad", vowel="a")
    for b in (12, 16, 20):
        arp(t, S.crystal, [prog[b]], b, [5, None, None, None, 4, None, None, None], 0.5, 76, 0.22, "bell")
    for k in range(8):
        t.hit("timpani", 19 * 4 + 2 + k * 0.25, 0.15 + 0.05 * k, "perc", variant=0)
    t.hit("timpani", 20 * 4, 0.6, variant=0)
    return t


@track
def map():
    """地图与关卡选择《沿着星线》：鲁特琴、竖笛、小提琴、手鼓。G 大调，100 BPM。"""
    t = Track(100, 32, rt60=1.8, wet=0.26, seed=12)
    t.bus("pad", 0.45, 0.35)
    t.bus("arp", 0.7, 0.25)
    t.bus("lead", 0.85, 0.3)
    t.bus("bass", 0.65, 0.15, lp=1200)
    t.bus("perc", 0.55, 0.2)
    a = ["G", "D/F#", "Em", "C", "G", "F", "C", "D"]
    th = theme_chords(5)
    prog = a + th + ["Em", "C", "G", "D", "Em", "C", "Am", "D"] + th
    pads(t, S.strings, prog, 0, 62, 3, 0.22, "pad", a=0.5, r=0.8)
    arp(t, S.lute, prog, 0, [0, 2, 1, 3, 2, 4, 3, 5], 0.5, 55, 0.5, "arp", pan=-0.25)
    bassline(t, S.pizz, prog, 0, "R...5...R...5.8.", 0.25, lo=31, vel=0.6)
    t.pattern("frame", "X..x..x.X...x...", 0, 32, 0.25, 0.5, pan=0.15)
    t.pattern("shaker", "x.o.x.o.x.o.x.o.", 4, 28, 0.25, 0.25, pan=0.4)
    t.pattern("tambourine", "........x.......", 8, 24, 0.25, 0.25, pan=-0.4)
    t.play(S.flute, melody(t, a, 7, "mixolydian", 4, 67, 88, 0, busy=0.4), "lead", 0.55, pan=0.1)
    t.play(S.flute, theme(8, 5 - 12), "lead", 0.75, pan=0.1)
    t.play(S.strings, melody(t, prog[16:24], 7, "major", 9, 64, 84, 16, busy=0.6), "lead", 0.55, pan=-0.1, a=0.08, r=0.3)
    t.play(S.strings, theme(24, 5 - 12), "lead", 0.6, a=0.08, r=0.3)
    t.play(S.flute, theme(24, 5), "lead", 0.35, pan=0.3)
    return t


@track
def deploy():
    """布阵与战前推演《推演》：低音弦乐固定音型、水晶钟摆、法力的嗡鸣。A 小调，88 BPM。"""
    t = Track(88, 24, rt60=2.6, wet=0.3, seed=13)
    t.bus("pad", 0.5, 0.4)
    t.bus("ost", 0.7, 0.2)
    t.bus("bell", 0.4, 0.55, delay=(1.5, 0.35, 0.4))
    t.bus("lead", 0.6, 0.4)
    t.bus("perc", 0.5, 0.25)
    prog = ["Am", "F", "Dm", "E", "Am", "F", "G", "E"] * 3
    pads(t, S.pad, prog, 0, 60, 3, 0.4, "pad")
    for s, d, sym in bars_of(prog, 4):
        root = S.bass_note(sym, 45)
        for i, off in enumerate([0, 0, 12, 0, 7, 0, 12, 7]):
            t.note(S.spiccato, s + i * 0.5, 0.4, root + off, 0.55 if i % 2 == 0 else 0.4, "ost", -0.15)
    t.pattern("tick", "x...o...x...o...", 0, 24, 0.25, 0.45, pan=0.35)
    t.pattern("woodblock", "x...............", 8, 16, 0.25, 0.25, pan=-0.35)
    for bar in range(0, 24, 2):
        sym = prog[bar]
        arp(t, S.crystal, [sym], bar, [5, None, 3, None, 4, None, None, None], 0.5, 76, 0.25, "bell", pan=0.3)
    # 星线主题的头两个音，像水晶在提示什么
    for bar in (8, 12, 16, 20):
        t.play(S.flute, [(bar * 4, 1.5, [69]), (bar * 4 + 1.5, 2.5, [76])], "lead", 0.45, pan=-0.2)
    t.play(S.strings, melody(t, prog[16:], 9, "harmonic", 21, 57, 76, 16, busy=0.3), "lead", 0.4, a=0.3, r=0.6, dark=True)
    t.swell(24 * 4, 4, 0.25)
    return t


@track
def battle_plains():
    """城郊草原《灯塔城外》：进行曲军鼓、弦乐跳弓、圆号。D 小调转 F 大调，128 BPM。"""
    t = Track(128, 32, rt60=2.0, wet=0.24, seed=21)
    t.bus("pad", 0.5, 0.3)
    t.bus("ost", 0.6, 0.18)
    t.bus("brass", 0.8, 0.3)
    t.bus("lead", 0.85, 0.3)
    t.bus("bass", 0.7, 0.12, lp=800)
    t.bus("perc", 0.7, 0.2)
    a = ["Dm", "Bb", "F", "C", "Dm", "Bb", "Gm", "A"]
    b = ["F", "C", "Dm", "Bb", "F", "C", "Bb", "A"]
    prog = a + a + b + theme_chords(0, minor=True)
    pads(t, S.strings, prog, 0, 62, 3, 0.25, "pad", a=0.2, r=0.4)
    for s, d, sym in bars_of(prog, 4):
        tones = S.close_voicing(sym, 57, 3)
        for i in range(int(d / 0.25)):
            m = tones[[0, 1, 2, 1][i % 4]]
            t.note(S.spiccato, s + i * 0.25, 0.2, m, 0.45 if i % 4 == 0 else 0.3, "ost", 0.2)
    bassline(t, S.strings, prog, 0, "R.R.R.5.", 0.5, lo=38, vel=0.6, dark=True, a=0.01, r=0.15)
    t.pattern("snare", "X.o.x.oox.o.x.oo", 0, 32, 0.25, 0.4, pan=-0.1)
    t.pattern("bass_drum", "X.......x.......", 0, 32, 0.25, 0.55)
    t.pattern("timpani", "X...............", 8, 24, 0.25, 0.5)
    for bar in (8, 16, 24):
        t.swell(bar * 4, 2, 0.3)
        t.hit("crash", bar * 4, 0.35, pan=0.3)
    # 圆号和弦与主旋律
    for s, d, sym in bars_of(prog[8:], 4):
        for i, m in enumerate(S.close_voicing(sym, 57, 3)):
            t.note(S.brass, 32 + s, d * 0.95, m, 0.4, "brass", -0.3 + i * 0.15)
    t.play(S.brass, melody(t, a, 2, "minor", 31, 62, 79, 0, busy=0.55), "lead", 0.6, pan=0.15)
    t.play(S.strings, melody(t, b, 5, "major", 37, 65, 84, 16, busy=0.7), "lead", 0.6, a=0.05, r=0.25)
    t.play(S.brass, theme(24, -12, minor=True), "lead", 0.75)
    t.play(S.strings, theme(24, 0, minor=True), "lead", 0.4, a=0.05, r=0.3)
    return t


@track
def battle_forest():
    """古木森林《林间游侠》：长笛、竖琴、框鼓、摇铃，6/8 拍。E 多利亚，112 BPM（八分音符 336）。"""
    t = Track(336, 64, bpb=6, rt60=2.2, wet=0.3, seed=22)
    t.bus("pad", 0.5, 0.35)
    t.bus("arp", 0.6, 0.3)
    t.bus("lead", 0.85, 0.32, delay=(3, 0.25, 0.25))
    t.bus("bass", 0.6, 0.15, lp=900)
    t.bus("perc", 0.65, 0.2)
    a = ["Em", "D", "C", "D", "Em", "G", "A", "Em"]
    b = ["C", "G", "D", "Em", "C", "G", "A", "B"]
    prog = a + a + b + a + a + b + a + a
    pads(t, S.strings, prog, 0, 62, 3, 0.25, "pad", a=0.6, r=0.9)
    arp(t, S.harp, prog, 0, [0, 2, 4, 5, 4, 2], 1, 59, 0.4, "arp", sweep=0.35)
    arp(t, S.lute, prog[16:], 16, [3, None, 1, 3, None, 1], 1, 55, 0.35, "arp", pan=-0.3)
    bassline(t, S.pizz, prog, 0, "R..5..", 1, lo=28, vel=0.6)
    t.pattern("frame", "X.xx.x", 0, 64, 1, 0.5, pan=0.1)
    t.pattern("shaker", "xoxoxo", 8, 56, 1, 0.22, pan=0.4)
    t.pattern("tambourine", "...x..", 16, 48, 1, 0.2, pan=-0.4)
    t.play(S.flute, melody(t, a + a, 4, "dorian", 41, 71, 88, 0, busy=0.6), "lead", 0.7, pan=0.1)
    t.play(S.strings, melody(t, b + a + a, 4, "dorian", 43, 64, 83, 16, busy=0.5), "lead", 0.55, a=0.08, r=0.3)
    t.play(S.flute, melody(t, b + a + a, 4, "dorian", 47, 71, 88, 40, busy=0.7), "lead", 0.7, pan=-0.1)
    return t


@track
def battle_bone_waste():
    """骸骨荒原《骸骨要塞》：低沉男声合唱、管风琴、丧钟、太鼓。C 弗里几亚，100 BPM。"""
    t = Track(100, 24, rt60=3.4, wet=0.34, seed=23, damp=0.35)
    t.bus("pad", 0.55, 0.4)
    t.bus("ost", 0.65, 0.2)
    t.bus("lead", 0.8, 0.35)
    t.bus("bass", 0.7, 0.15, lp=700)
    t.bus("bell", 0.45, 0.5)
    t.bus("perc", 0.75, 0.25)
    prog = ["Cm", "Db", "Cm", "Bbm", "Cm", "Ab", "Db", "G"] * 3
    pads(t, S.choir, prog, 0, 55, 3, 0.4, "pad", vowel="o")
    pads(t, S.organ, prog[8:], 8, 48, 2, 0.25, "pad")
    for s, d, sym in bars_of(prog, 4):
        root = S.bass_note(sym, 36)
        for i, off in enumerate([0, 0, 1, 0, 0, 0, 1, 3]):
            t.note(S.spiccato, s + i * 0.5, 0.4, root + 12 + off, 0.5, "ost", -0.2)
        t.note(S.strings, s, d, root, 0.6, "bass", 0, dark=True, a=0.05, r=0.6)
    t.pattern("taiko", "X.....x...x.x...", 0, 24, 0.25, 0.6)
    t.pattern("taiko", "........X.......", 8, 16, 0.25, 0.45, pan=0.4)
    t.pattern("anvil", "............x...", 16, 8, 0.25, 0.18, pan=-0.4)
    for bar in range(0, 24, 4):
        t.note(S.church_bell, bar * 4, 4, 48, 0.55, "bell", -0.3)
    t.play(S.brass, melody(t, prog[:16], 0, "phrygian", 51, 55, 72, 0, busy=0.4), "lead", 0.6, pan=0.15)
    t.play(S.strings, melody(t, prog[16:], 0, "phrygian", 52, 60, 79, 16, busy=0.6), "lead", 0.55, a=0.05, r=0.3)
    t.swell(24 * 4, 4, 0.25)
    return t


@track
def battle_arcane_court():
    """奥术庭院《银月回廊》：钢片琴、竖琴、拨弦，3/4 圆舞曲。A 和声小调，150 BPM。"""
    t = Track(150, 48, bpb=3, rt60=2.8, wet=0.34, seed=24)
    t.bus("pad", 0.5, 0.4)
    t.bus("arp", 0.6, 0.4, delay=(1.5, 0.3, 0.3))
    t.bus("lead", 0.85, 0.32)
    t.bus("bass", 0.65, 0.18)
    t.bus("perc", 0.5, 0.3)
    a = ["Am", "Am", "Dm", "Dm", "E", "E", "Am", "E7"]
    b = ["F", "F", "Dm", "Dm", "Bdim", "E", "Am", "E"]
    prog = a + a + b + b + a + b
    pads(t, S.strings, prog, 0, 64, 3, 0.2, "pad", a=0.4, r=0.6)
    for s, d, sym in bars_of(prog, 3):  # 圆舞曲伴奏：一拍低音、二三拍和弦
        t.note(S.pizz, s, 1, S.bass_note(sym, 33), 0.7, "bass")
        for k in (1, 2):
            for i, m in enumerate(S.close_voicing(sym, 60, 3)):
                t.note(S.pizz, s + k, 0.5, m, 0.3, "bass", -0.2 + i * 0.2)
    arp(t, S.celesta, prog, 0, [3, 4, 5, 4, 3, 5], 0.5, 72, 0.32, "arp", sweep=0.5)
    arp(t, S.harp, prog[16:32], 16, [0, 2, 4, 5, 3, 1], 0.5, 60, 0.35, "arp", pan=-0.2)
    t.pattern("tambourine", "x..", 8, 40, 1, 0.15, pan=0.4)
    t.pattern("tick", ".xx", 32, 16, 1, 0.2, pan=-0.3)
    t.play(S.strings, melody(t, a + a, 9, "harmonic", 61, 69, 88, 0, busy=0.5), "lead", 0.6, a=0.08, r=0.4)
    t.play(S.clarinet, melody(t, b + b, 9, "harmonic", 63, 60, 79, 16, busy=0.5), "lead", 0.6, pan=-0.15)
    t.play(S.flute, melody(t, a + b, 9, "harmonic", 67, 72, 91, 32, busy=0.6), "lead", 0.6, pan=0.15)
    for bar in range(0, 48, 8):
        t.note(S.glock, bar * 3, 2, 81, 0.3, "arp", 0.4)
    return t


@track
def battle_desert():
    """灰沙荒漠《灰沙之城》：杜杜克、鲁特琴、手鼓、铃鼓，持续低音。D 希贾兹，108 BPM。"""
    t = Track(108, 32, rt60=2.4, wet=0.28, seed=25, damp=0.6)
    t.bus("pad", 0.55, 0.3)
    t.bus("arp", 0.6, 0.25)
    t.bus("lead", 0.85, 0.32, delay=(1.5, 0.25, 0.25))
    t.bus("bass", 0.7, 0.12, lp=800)
    t.bus("perc", 0.7, 0.2)
    prog = ["D", "Eb", "D", "Cm", "Gm", "Eb", "D", "D"] * 4
    for bar in range(0, 32, 4):  # D-A 持续音
        t.note(S.strings, bar * 4, 16, 38, 0.45, "pad", -0.2, dark=True, a=1.0, r=1.5)
        t.note(S.strings, bar * 4, 16, 45, 0.35, "pad", 0.2, dark=True, a=1.0, r=1.5)
    pads(t, S.pad, prog[8:], 8, 62, 3, 0.3, "pad")
    arp(t, S.lute, prog, 0, [0, None, 2, 1, None, 2, 3, 1], 0.5, 57, 0.45, "arp", pan=-0.3)
    t.pattern("dum", "X..x..X.....X...", 0, 32, 0.25, 0.65)
    t.pattern("tek", "..x.x...x.xx..x.", 0, 32, 0.25, 0.4, pan=0.25)
    t.pattern("tambourine", "x...x...x...x...", 8, 24, 0.25, 0.25, pan=-0.4)
    t.pattern("frame", "X...............", 16, 16, 0.25, 0.45)
    t.play(S.reed, melody(t, prog[:16], 2, "hijaz", 71, 62, 79, 0, busy=0.55), "lead", 0.75, pan=0.1)
    t.play(S.strings, melody(t, prog[16:], 2, "hijaz", 73, 66, 86, 16, busy=0.7), "lead", 0.5, a=0.05, r=0.3)
    t.play(S.reed, melody(t, prog[16:], 2, "hijaz", 73, 66, 86, 16, busy=0.7), "lead", 0.4, semis=-12, pan=-0.2)
    return t


@track
def battle_lake():
    """湖心枢纽《湖心夜》：钢琴琶音加回声、水晶、低声脉冲、长笛。B 小调，96 BPM，夜战。"""
    t = Track(96, 24, rt60=3.6, wet=0.42, seed=26)
    t.bus("pad", 0.5, 0.45)
    t.bus("arp", 0.6, 0.45, delay=(0.75, 0.45, 0.45))
    t.bus("lead", 0.8, 0.4)
    t.bus("bass", 0.7, 0.15, lp=600)
    t.bus("bell", 0.4, 0.6)
    t.bus("perc", 0.5, 0.3)
    prog = ["Bm", "G", "D", "A", "Bm", "G", "Em", "F#"] * 3
    pads(t, S.pad, prog, 0, 62, 3, 0.45, "pad")
    arp(t, S.piano, prog, 0, [0, 2, 4, 2, 5, 2, 4, 2], 0.5, 59, 0.38, "arp", sweep=0.4)
    bassline(t, S.sub_bass, prog, 0, "R.RR.RR.", 0.5, lo=35, vel=0.4)
    t.pattern("timpani", "x.......o.......", 8, 16, 0.25, 0.3)
    t.pattern("shaker", "..o...o...o...o.", 0, 24, 0.25, 0.18, pan=0.4)
    t.pattern("bass_drum", "x.........x.....", 8, 16, 0.25, 0.4)
    for bar in range(0, 24, 2):
        t.note(S.crystal, bar * 4 + 3, 2, 83 + (0 if bar % 4 else 2), 0.25, "bell", 0.4)
    t.play(S.flute, melody(t, prog[8:16], 11, "minor", 81, 66, 86, 8, busy=0.35), "lead", 0.65)
    t.play(S.strings, melody(t, prog[16:], 11, "minor", 82, 62, 83, 16, busy=0.45), "lead", 0.55, a=0.2, r=0.6)
    return t


@track
def battle_mist_marsh():
    """迷雾沼泽《雾中之林》：精灵女声、竖琴、全音阶长笛、风铃，6/8 拍。升 F 小调，84 BPM（八分音符 252）。"""
    t = Track(252, 48, bpb=6, rt60=4.0, wet=0.45, seed=27, damp=0.55)
    t.bus("pad", 0.55, 0.5)
    t.bus("arp", 0.55, 0.45, delay=(3, 0.35, 0.3))
    t.bus("lead", 0.8, 0.45)
    t.bus("bass", 0.6, 0.2, lp=700)
    t.bus("bell", 0.35, 0.6)
    t.bus("perc", 0.45, 0.3)
    prog = ["F#m", "D", "Bm", "C#", "F#m", "D", "E", "C#"] * 2 + ["D", "E", "C#m", "F#m", "D", "Bm", "G", "C#"]
    pads(t, S.choir, prog, 0, 66, 3, 0.35, "pad", vowel="a")
    arp(t, S.harp, prog, 0, [0, 2, 4, 5, 4, 2], 1, 57, 0.38, "arp", sweep=0.5)
    bassline(t, S.strings, prog, 0, "R-----", 1, lo=30, vel=0.5, dark=True, a=0.6, r=1.0)
    t.pattern("frame", "X.....", 0, 48, 1, 0.35)
    t.pattern("frame", "...o..", 8, 40, 1, 0.25, pan=0.3)
    for bar in range(0, 48, 3):
        for i in range(5):
            m = 84 + [0, 2, 4, 6, 8][int((bar * 7 + i * 3) % 5)]
            t.note(S.crystal, bar * 6 + 2.5 + i * 0.35, 1, m, 0.12, "bell", -0.5 + i * 0.25, human=0.02)
    # 全音阶动机：雾里分不清方向
    wt, _ = S.seq("F#5:2 G#5:1 A#5:2 C6:1 | D6:3 C6:3 | A#5:2 G#5:1 F#5:3 | C#5:6", 0)
    for bar in (8, 32):
        t.play(S.flute, S.shift(wt, bar * 6), "lead", 0.6, pan=0.2)
    t.play(S.flute, melody(t, prog[16:24], 6, "minor", 91, 66, 85, 16, busy=0.5), "lead", 0.6, pan=-0.1)
    t.play(S.strings, melody(t, prog[:8], 6, "minor", 93, 61, 80, 40, busy=0.4), "lead", 0.45, a=0.3, r=0.8)
    return t


@track
def battle_night_tundra():
    """永夜冰原《永夜冰原》：高音弦乐震音、钟琴、低音脉冲、圆号。G 小调，112 BPM。"""
    t = Track(112, 32, rt60=3.0, wet=0.36, seed=28, damp=0.6)
    t.bus("pad", 0.5, 0.4)
    t.bus("arp", 0.5, 0.45, delay=(0.5, 0.35, 0.35))
    t.bus("lead", 0.85, 0.32)
    t.bus("bass", 0.7, 0.15, lp=700)
    t.bus("perc", 0.6, 0.25)
    prog = ["Gm", "Eb", "Bb", "F", "Gm", "Eb", "Cm", "D"] * 4
    pads(t, S.strings, prog, 0, 74, 3, 0.28, "pad", trem=13, a=0.3, r=0.6)
    arp(t, S.glock, prog, 0, [3, 5, 4, 5], 0.5, 79, 0.2, "arp", sweep=0.6)
    bassline(t, S.sub_bass, prog, 0, "RRRRRRRR", 0.5, lo=43, vel=0.4, dur=0.4)
    t.pattern("taiko", "X.......x.......", 8, 24, 0.25, 0.4)
    t.pattern("snare", "....x.......x...", 16, 16, 0.25, 0.25, pan=-0.2)
    t.pattern("tick", "x.x.x.x.x.x.x.x.", 0, 32, 0.25, 0.12, pan=0.4)
    for bar in (8, 16, 24):
        t.hit("crash", bar * 4, 0.25, pan=-0.3)
    t.play(S.brass, melody(t, prog[8:24], 7, "minor", 101, 55, 74, 8, busy=0.4), "lead", 0.65, pan=0.1)
    t.play(S.strings, melody(t, prog[24:], 7, "harmonic", 102, 67, 86, 24, busy=0.6), "lead", 0.55, a=0.05, r=0.3)
    return t


@track
def battle_abyss():
    """永夜深渊《永夜深渊》：管风琴、男声合唱、十六分音符弦乐、太鼓与军鼓。C 小调，132 BPM。"""
    t = Track(132, 32, rt60=3.0, wet=0.3, seed=29, damp=0.35)
    t.bus("pad", 0.55, 0.35)
    t.bus("ost", 0.6, 0.18)
    t.bus("brass", 0.8, 0.3)
    t.bus("lead", 0.85, 0.3)
    t.bus("bass", 0.75, 0.12, lp=700)
    t.bus("perc", 0.8, 0.2)
    a = ["Cm", "Ab", "Db", "G", "Cm", "Ab", "Fm", "G"]
    prog = a + theme_chords(-2, minor=True) + a + a
    pads(t, S.organ, prog, 0, 55, 3, 0.3, "pad")
    pads(t, S.choir, prog[8:], 8, 60, 3, 0.35, "pad", vowel="o")
    for s, d, sym in bars_of(prog, 4):
        root = S.bass_note(sym, 48)
        for i in range(int(d / 0.25)):
            off = [0, 12, 7, 12, 0, 12, 8, 7][i % 8]
            t.note(S.spiccato, s + i * 0.25, 0.2, root + off, 0.4 if i % 4 else 0.55, "ost", 0.2)
    bassline(t, S.strings, prog, 0, "R-R-R-RR", 0.5, lo=36, vel=0.6, dark=True, a=0.01, r=0.15)
    t.pattern("taiko", "X..x..X.X..x..x.", 0, 32, 0.25, 0.55)
    t.pattern("snare", "....X.......X..o", 8, 24, 0.25, 0.45, pan=-0.15)
    t.pattern("bass_drum", "X.......X.......", 16, 16, 0.25, 0.5)
    for bar in (8, 16, 24):
        t.swell(bar * 4, 2, 0.3)
        t.hit("crash", bar * 4, 0.4)
    stabs = "x.....x...x.....".replace(" ", "")
    for s, d, sym in bars_of(prog[16:], 4):
        for i, ch in enumerate(stabs):
            if ch == "x":
                for m in S.close_voicing(sym, 55, 3):
                    t.note(S.brass, 64 + s + i * 0.25, 0.3, m, 0.55, "brass", -0.2)
    t.play(S.brass, S.transpose(S.to_minor(theme(8, -2), 0), -12), "lead", 0.7)
    t.play(S.strings, melody(t, prog[16:], 0, "harmonic", 111, 67, 87, 16, busy=0.7), "lead", 0.6, a=0.04, r=0.25)
    return t


@track
def boss():
    """章末首领《首领战》：合唱、铜管、定音鼓与太鼓、十六分音符弦乐。D 小调，144 BPM。"""
    t = Track(144, 32, rt60=2.6, wet=0.28, seed=31)
    t.bus("pad", 0.55, 0.32)
    t.bus("ost", 0.6, 0.18)
    t.bus("brass", 0.85, 0.3)
    t.bus("lead", 0.9, 0.3)
    t.bus("bass", 0.75, 0.12, lp=800)
    t.bus("perc", 0.85, 0.2)
    a = ["Dm", "Bb", "C", "A", "Dm", "Gm", "Bb", "A"]
    b = ["Gm", "Dm", "Bb", "F", "Gm", "Dm", "E", "A"]
    prog = a + a + b + theme_chords(0, minor=True)
    pads(t, S.choir, prog, 0, 62, 3, 0.4, "pad", vowel="a")
    for s, d, sym in bars_of(prog, 4):
        tones = S.close_voicing(sym, 57, 3)
        for i in range(int(d / 0.25)):
            m = tones[[0, 2, 1, 2][i % 4]] + (12 if i % 8 == 6 else 0)
            t.note(S.spiccato, s + i * 0.25, 0.2, m, 0.5 if i % 4 == 0 else 0.35, "ost", -0.2)
    bassline(t, S.strings, prog, 0, "R.RR.RR.", 0.5, lo=38, vel=0.65, dark=True, a=0.01, r=0.15)
    t.pattern("taiko", "X...x.x.X...x.xx", 0, 32, 0.25, 0.55)
    t.pattern("timpani", "X.......X.......", 8, 24, 0.25, 0.5)
    t.pattern("snare", "....X..o....X.oo", 8, 24, 0.25, 0.4, pan=0.15)
    for bar in (8, 16, 24):
        t.swell(bar * 4, 2, 0.35)
        t.hit("crash", bar * 4, 0.45)
    for s, d, sym in bars_of(prog, 4):
        for i, m in enumerate(S.close_voicing(sym, 55, 3)):
            t.note(S.brass, s, 0.6, m, 0.55, "brass", -0.3 + i * 0.15)
            t.note(S.brass, s + 2.5, 0.4, m, 0.45, "brass", -0.3 + i * 0.15)
    t.play(S.brass, melody(t, a + a, 2, "harmonic", 131, 62, 81, 0, busy=0.55), "lead", 0.75, pan=0.1)
    t.play(S.strings, melody(t, b, 7, "minor", 133, 67, 88, 16, busy=0.75), "lead", 0.6, a=0.04, r=0.25)
    t.play(S.brass, theme(24, -12, minor=True), "lead", 0.8)
    t.play(S.choir, theme(24, 0, minor=True), "lead", 0.4, vowel="a", a=0.08, r=0.4)
    return t


@track
def boss_final():
    """最终首领《巫妖王》：管风琴奏出小调的星线主题，合唱与铜管压上来。D 小调，120 BPM。"""
    t = Track(120, 32, rt60=3.8, wet=0.34, seed=32, damp=0.35)
    t.bus("pad", 0.6, 0.35)
    t.bus("ost", 0.6, 0.18)
    t.bus("lead", 0.9, 0.32)
    t.bus("brass", 0.8, 0.3)
    t.bus("bass", 0.8, 0.12, lp=700)
    t.bus("bell", 0.4, 0.5)
    t.bus("perc", 0.85, 0.22)
    tm = theme_chords(0, minor=True)
    prog = tm + ["Dm", "Bb", "Gm", "A", "Dm", "Eb", "C", "A"] + tm + tm
    pads(t, S.organ, prog, 0, 57, 4, 0.3, "pad")
    t.play(S.organ, theme(0, 0, minor=True), "lead", 0.55)
    t.play(S.organ, theme(0, -24, minor=True), "lead", 0.3)
    pads(t, S.choir, prog[8:], 8, 62, 3, 0.4, "pad", vowel="a")
    pads(t, S.choir, prog[8:], 8, 48, 2, 0.35, "pad", vowel="o")
    for s, d, sym in bars_of(prog[8:], 4):
        root = S.bass_note(sym, 50)
        for i in range(int(d / 0.25)):
            off = [0, 3, 7, 3, 12, 7, 3, 7][i % 8]
            t.note(S.spiccato, 32 + s + i * 0.25, 0.2, root + off, 0.45, "ost", 0.2)
    bassline(t, S.strings, prog, 0, "R-------", 0.5, lo=38, vel=0.7, dark=True, a=0.05, r=0.6)
    t.pattern("taiko", "X.....X.X...x...", 8, 24, 0.25, 0.6)
    t.pattern("timpani", "X...X...X...X.xx", 16, 16, 0.25, 0.45)
    t.pattern("snare", "....X.......X...", 24, 8, 0.25, 0.4)
    for bar in (0, 8, 16, 24):
        t.note(S.church_bell, bar * 4, 4, 50, 0.5, "bell", -0.3)
        if bar:
            t.hit("crash", bar * 4, 0.4)
            t.swell(bar * 4, 2, 0.3)
    t.play(S.brass, melody(t, prog[8:16], 2, "harmonic", 141, 60, 79, 8, busy=0.5), "lead", 0.75)
    t.play(S.brass, theme(16, -12, minor=True), "brass", 0.7)
    t.play(S.strings, theme(16, 0, minor=True), "lead", 0.45, a=0.05, r=0.3)
    t.play(S.choir, theme(24, 0, minor=True), "lead", 0.6, vowel="a", a=0.08, r=0.4)
    t.play(S.brass, theme(24, -12, minor=True), "brass", 0.75)
    return t


@track
def ending():
    """结局与片尾《重织星线》：弦乐和合唱完整唱出星线主题，竖琴与水晶铃收尾。D 大调，70 BPM。"""
    t = Track(70, 32, rt60=3.6, wet=0.4, seed=41)
    t.bus("pad", 0.55, 0.45)
    t.bus("arp", 0.5, 0.45)
    t.bus("lead", 0.9, 0.38)
    t.bus("bass", 0.6, 0.2, lp=800)
    t.bus("bell", 0.35, 0.6, delay=(1, 0.35, 0.4))
    t.bus("perc", 0.55, 0.35)
    prog = THEME_CHORDS + THEME_CHORDS + ["Bm", "G", "D", "A", "Bm", "G", "Em", "A"] + THEME_CHORDS
    pads(t, S.strings, prog, 0, 62, 3, 0.3, "pad", a=0.8, r=1.2)
    pads(t, S.choir, prog[8:], 8, 66, 3, 0.25, "pad", vowel="a")
    bassline(t, S.strings, prog, 0, "R-------", 0.5, lo=38, vel=0.55, dark=True, a=0.4, r=1.0)
    arp(t, S.harp, prog, 0, [0, 2, 4, 5, 3, 4, 2, 1], 0.5, 62, 0.38, "arp", sweep=0.35)
    t.play(S.flute, theme(0), "lead", 0.6)
    t.play(S.strings, theme(8, -12), "lead", 0.65, a=0.15, r=0.5)
    t.play(S.strings, melody(t, prog[16:24], 2, "major", 151, 69, 88, 16, busy=0.35), "lead", 0.55, a=0.2, r=0.6)
    t.play(S.choir, theme(24), "lead", 0.55, vowel="a", a=0.12, r=0.6)
    t.play(S.strings, theme(24, -12), "lead", 0.55, a=0.12, r=0.5)
    t.play(S.brass, theme(24, -24), "lead", 0.3)
    for bar in range(0, 32, 2):
        arp(t, S.crystal, [prog[bar]], bar, [5, None, None, None, None, None, None, None], 0.5, 76, 0.2, "bell")
    for k in range(8):
        t.hit("timpani", 23 * 4 + 2 + k * 0.25, 0.12 + 0.05 * k, variant=0)
    t.hit("timpani", 24 * 4, 0.55, variant=0)
    t.hit("crash", 24 * 4, 0.3)
    return t


@track
def victory():
    """胜利短曲（不循环）：铜管吹出主题开头，定音鼓滚奏收在 D 大三和弦。"""
    t = Track(96, 2, loop=False, tail=4.0, rt60=2.4, wet=0.3, seed=51)
    t.bus("lead", 0.9, 0.3)
    t.bus("pad", 0.6, 0.35)
    t.bus("bell", 0.4, 0.5)
    t.bus("perc", 0.7, 0.25)
    t.play(S.brass, S.seq("D4:.5 D4:.25 D4:.25 A4:1 G4:.5 F#4:.5 E4:.5 F#4:.5 | D4+F#4+A4+D5:4", 0)[0], "lead", 0.8, human=0)
    t.play(S.brass, S.seq("D5:.5 D5:.25 D5:.25 A5:1 G5:.5 F#5:.5 E5:.5 F#5:.5 | A5:4", 0)[0], "lead", 0.45, human=0)
    pads(t, S.strings, ["D", "D"], 0, 62, 3, 0.35, "pad", a=0.1, r=1.5)
    for k in range(12):
        t.hit("timpani", 2.5 + k * 1 / 8, 0.2 + 0.03 * k, variant=0)
    t.hit("timpani", 4, 0.8, variant=0)
    t.hit("crash", 4, 0.45)
    t.note(S.crystal, 4, 4, 86, 0.4, "bell", 0.3)
    t.note(S.crystal, 4.5, 4, 90, 0.3, "bell", -0.3)
    return t


@track
def defeat():
    """失败短曲（不循环）：弦乐慢慢奏出小调的主题开头，停在属和弦上。"""
    t = Track(60, 3, loop=False, tail=5.0, rt60=3.2, wet=0.38, seed=52)
    t.bus("lead", 0.85, 0.4)
    t.bus("pad", 0.6, 0.4)
    t.bus("perc", 0.5, 0.3)
    t.play(S.strings, S.seq("D5:1.5 A5:.5 A5:1 G5:1 | F5:1 E5:1 | C#5:4", 0)[0], "lead", 0.6, a=0.3, r=1.2)
    pads(t, S.strings, ["Dm", "Gm/Bb A", "A"], 0, 57, 3, 0.3, "pad", dark=True, a=0.6, r=2.0)
    t.note(S.strings, 0, 10, 38, 0.5, "pad", 0, dark=True, a=0.5, r=2.0)
    t.hit("timpani", 0, 0.4, variant=0)
    t.hit("bass_drum", 6, 0.35)
    return t


# ----------------------------------------------------------------------------

def write_wav(path, x):
    pcm = (np.clip(x, -1, 1) * 32767).astype(np.int16).T.copy()
    with wave.open(path, "wb") as w:
        w.setnchannels(2)
        w.setsampwidth(2)
        w.setframerate(S.SR)
        w.writeframes(pcm.tobytes())


def encode(wav, out, quality):
    fmt = ["-c:a", "libvorbis", "-q:a", str(quality)] if out.endswith(".ogg") else ["-c:a", "libmp3lame", "-q:a", "4"]
    # bitexact：不写随机的流序列号和编码器信息，同样的输入得到同样的文件
    subprocess.run(["ffmpeg", "-y", "-loglevel", "error", "-i", wav, "-map_metadata", "-1",
                    "-fflags", "+bitexact", "-flags:a", "+bitexact", *fmt, out], check=True)


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("tracks", nargs="*", help="曲目 id（默认全部）：" + " ".join(TRACKS))
    ap.add_argument("--out", default=OUT_DIR)
    ap.add_argument("--preview", help="再输出一份 mp3 到这个目录，方便试听")
    ap.add_argument("--quality", type=float, default=4, help="Vorbis 质量 0~10（默认 4，约 128 kbps）")
    args = ap.parse_args()
    names = args.tracks or list(TRACKS)
    os.makedirs(args.out, exist_ok=True)
    if args.preview:
        os.makedirs(args.preview, exist_ok=True)
    for name in names:
        if name not in TRACKS:
            sys.exit("没有这首曲目：" + name)
        t0 = time.time()
        tr = TRACKS[name]()
        audio = tr.render()
        with tempfile.TemporaryDirectory() as tmp:
            wav = os.path.join(tmp, name + ".wav")
            write_wav(wav, audio)
            encode(wav, os.path.join(args.out, name + ".ogg"), args.quality)
            if args.preview:
                encode(wav, os.path.join(args.preview, name + ".mp3"), args.quality)
        peak = float(np.max(np.abs(audio)))
        print(f"{name:20s} {audio.shape[1] / S.SR:6.1f} 秒  峰值 {peak:.2f}  用时 {time.time() - t0:.1f} 秒")


if __name__ == "__main__":
    main()
