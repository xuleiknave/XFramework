"""
星线配乐的合成引擎：只依赖 numpy。

乐器都用加法合成（谐波波表 + 包络），打击乐用滤波噪声和扫频正弦，混响用卷积。
同一个参数的音符会缓存，整首曲子渲染一次大约几秒到十几秒。

循环曲的做法：按小节长度 L 渲染，超出 L 的余音折回开头叠加，混响用长度 L 的
循环卷积，所以首尾无缝衔接，Unity 里直接勾选 Loop 即可。
"""

import functools
import zlib

import numpy as np

SR = 44100
TABLE = 4096
NYQ = 15000.0

NOTE_INDEX = {"C": 0, "D": 2, "E": 4, "F": 5, "G": 7, "A": 9, "B": 11}


# ----------------------------------------------------------------------------
# 音高与记谱

def pitch_class(name):
    pc = NOTE_INDEX[name[0].upper()]
    for ch in name[1:]:
        if ch == "#":
            pc += 1
        elif ch == "b":
            pc -= 1
    return pc % 12


def midi(name):
    """'C#4' / 'Bb3' -> MIDI 音高。"""
    i = 1
    acc = 0
    while i < len(name) and name[i] in "#b":
        acc += 1 if name[i] == "#" else -1
        i += 1
    octave = int(name[i:])
    return 12 * (octave + 1) + NOTE_INDEX[name[0].upper()] + acc


def hz(m):
    return 440.0 * 2.0 ** ((m - 69) / 12.0)


def seq(text, start=0.0):
    """简谱字符串 -> [(起拍, 拍数, [音高...])], 结束拍。

    'D5:1.5 A5:.5 r:1 D4+F4+A4:2 |'；r 是休止，| 只是方便阅读的小节线。
    """
    events = []
    t = start
    for tok in text.split():
        if tok == "|":
            continue
        name, _, d = tok.partition(":")
        d = float(d) if d else 1.0
        if name.lower() != "r":
            events.append((t, d, [midi(n) for n in name.split("+")]))
        t += d
    return events, t


def transpose(events, semis):
    return [(s, d, [m + semis for m in ms]) for s, d, ms in events]


def to_minor(events, tonic_pc):
    """把大调旋律改成同主音小调：三、六、七级降半音。"""
    out = []
    for s, d, ms in events:
        mm = []
        for m in ms:
            deg = (m - tonic_pc) % 12
            mm.append(m - 1 if deg in (4, 9, 11) else m)
        out.append((s, d, mm))
    return out


def shift(events, beats):
    return [(s + beats, d, ms) for s, d, ms in events]


QUALITIES = {
    "": (0, 4, 7), "m": (0, 3, 7), "7": (0, 4, 7, 10), "m7": (0, 3, 7, 10),
    "maj7": (0, 4, 7, 11), "sus2": (0, 2, 7), "sus4": (0, 5, 7), "dim": (0, 3, 6),
    "aug": (0, 4, 8), "5": (0, 7), "add9": (0, 4, 7, 14), "madd9": (0, 3, 7, 14),
    "m6": (0, 3, 7, 9), "6": (0, 4, 7, 9),
}


def parse_chord(sym):
    """'F#m/A' -> (根音音级, 音程, 低音音级)。"""
    bass = None
    if "/" in sym:
        sym, b = sym.split("/")
        bass = pitch_class(b)
    i = 1
    while i < len(sym) and sym[i] in "#b":
        i += 1
    root = pitch_class(sym[:i])
    return root, QUALITIES[sym[i:]], root if bass is None else bass


def chord_pcs(sym):
    root, ints, _ = parse_chord(sym)
    return {(root + x) % 12 for x in ints}


def close_voicing(sym, center, n=3):
    """离 center 最近的 n 个和弦音（自动得到平滑的声部进行）。"""
    pcs = chord_pcs(sym)
    cands = [m for m in range(center - 9, center + 10) if m % 12 in pcs]
    cands.sort(key=lambda m: (abs(m - center), m))
    return sorted(cands[:n])


def bass_note(sym, lo=36):
    _, _, b = parse_chord(sym)
    m = lo
    while m % 12 != b:
        m += 1
    return m


# ----------------------------------------------------------------------------
# 波表

def _lp(f, cut, order=2):
    return 1.0 / (1.0 + (f / cut) ** (2 * order)) ** 0.5


def _formant(f, formants):
    a = np.zeros_like(f)
    for fc, g, bw in formants:
        a += g * np.exp(-(((f - fc) / bw) ** 2))
    return a


VOWELS = {
    "a": [(750, 1.0, 140), (1150, 0.6, 160), (2600, 0.18, 260), (3300, 0.08, 300)],
    "o": [(450, 1.0, 120), (800, 0.55, 140), (2500, 0.06, 260)],
    "u": [(330, 1.0, 100), (700, 0.25, 140), (2400, 0.03, 260)],
    "e": [(500, 1.0, 120), (1800, 0.45, 180), (2600, 0.15, 260)],
}


def _spectrum(kind, k, f):
    kf = k * f
    if kind == "sine":
        return (k == 1).astype(float)
    if kind == "strings":
        return k ** -1.1 * _lp(kf, 3200) * (1 + 0.5 * np.exp(-(((kf - 500) / 300) ** 2)))
    if kind == "strings_dark":
        return k ** -1.4 * _lp(kf, 1600)
    if kind == "brass_dark":
        return k ** -1.0 * _lp(kf, 650, 2)
    if kind == "brass_bright":
        return k ** -0.7 * _lp(kf, 3000, 2)
    if kind == "flute":
        return np.where(k <= 6, np.array([1, 0.35, 0.14, 0.06, 0.03, 0.015, 0, 0])[np.minimum(k, 7).astype(int) - 1], 0)
    if kind == "reed":  # 杜杜克/双簧管一类，带鼻音
        return k ** -1.0 * _lp(kf, 2200) * (1 + 1.2 * np.exp(-(((kf - 1200) / 400) ** 2))) * np.where(k % 2 == 0, 0.55, 1)
    if kind == "clarinet":
        return np.where(k % 2 == 1, k ** -1.0, 0.08 * k ** -1.0) * _lp(kf, 2500)
    if kind == "organ":
        bars = {1: 1.0, 2: 0.75, 3: 0.5, 4: 0.45, 6: 0.3, 8: 0.28, 10: 0.1, 12: 0.1, 16: 0.06}
        return np.array([bars.get(int(x), 0.0) for x in k]) * _lp(kf, 6000)
    if kind == "pad":
        return k ** -1.8 * _lp(kf, 1400)
    if kind == "pluck_bright":
        return k ** -1.0 * np.abs(np.sin(np.pi * k * 0.17)) * _lp(kf, 6000)
    if kind == "pluck_dark":
        return k ** -2.6
    if kind == "piano_bright":
        return k ** -1.2 * np.abs(np.sin(np.pi * k / 7.3)) * _lp(kf, 4500)
    if kind.startswith("choir_"):
        return (_formant(kf, VOWELS[kind[6:]]) + 0.02 * k ** -1.0) * _lp(kf, 4500)
    raise KeyError(kind)


@functools.lru_cache(maxsize=None)
def table(kind, m):
    f = hz(m)
    nh = int(max(1, min(64, NYQ // f)))
    k = np.arange(1, nh + 1, dtype=float)
    amps = _spectrum(kind, k, f)
    x = np.arange(TABLE) / TABLE
    w = np.zeros(TABLE)
    for kk, a in zip(k, amps):
        if a > 1e-4:
            w += a * np.sin(2 * np.pi * kk * x)
    peak = np.max(np.abs(w)) or 1.0
    w /= peak
    return np.append(w, w[0])


def osc(tab, f, n, ph0=0.0):
    if np.isscalar(f):
        ph = ph0 + np.arange(n) * (f / SR)
    else:
        ph = ph0 + np.cumsum(f) / SR
    idx = (ph % 1.0) * TABLE
    i = idx.astype(np.int64)
    fr = idx - i
    return tab[i] * (1 - fr) + tab[i + 1] * fr


def env(dur, a=0.01, d=0.2, s=0.8, r=0.3, n=None):
    """ADSR；r 是衰减到 -40 dB 的时间。"""
    total = int((dur + r) * SR) if n is None else n
    t = np.arange(total) / SR
    a = max(a, 1e-3)
    e = np.where(t < a, (t / a) ** 0.8, s + (1 - s) * np.exp(-(np.maximum(t - a, 0)) / max(d, 1e-3)))
    nd = int(dur * SR)
    if nd < total:
        level = e[nd]
        e[nd:] = level * np.exp(-(t[nd:] - dur) / max(r / 4.6, 1e-3))
    return e


def _vib(n, rate, depth, delay, ph):
    t = np.arange(n) / SR
    ramp = np.clip((t - delay) / 0.4, 0, 1)
    return 1 + depth * ramp * np.sin(2 * np.pi * rate * t + ph)


def stable_hash(*key):
    """跨进程稳定的哈希（内置 hash() 对字符串每次运行都不同）。"""
    return zlib.crc32(repr(key).encode())


def _phase(*key):
    return (stable_hash(*key) % 1000) / 1000.0 * 2 * np.pi


# ----------------------------------------------------------------------------
# 乐器（返回单声道信号，峰值约 1）

def _nearest(f):
    return int(round(69 + 12 * np.log2(f / 440.0)))


def strings(f, dur, vel=0.8, a=0.25, r=0.6, voices=3, dark=False, trem=0.0):
    m = _nearest(f)
    n = int((dur + r) * SR)
    tab = table("strings_dark" if dark else "strings", m)
    out = np.zeros(n)
    for v in range(voices):
        det = 1 + (v - (voices - 1) / 2) * 0.0035
        fv = f * det * _vib(n, 5.0 + 0.4 * v, 0.003, 0.25, _phase(m, v))
        out += osc(tab, fv, n, ph0=(v * 0.37) % 1)
    out *= env(dur, a, 0.4, 0.85, r, n) / voices
    if trem:
        t = np.arange(n) / SR
        out *= 0.65 + 0.35 * np.sin(2 * np.pi * trem * t)
    return out


def spiccato(f, dur, vel=0.8):
    return strings(f, min(dur, 0.18), vel, a=0.008, r=0.12, voices=2)


def brass(f, dur, vel=0.8, a=0.06, r=0.35):
    m = _nearest(f)
    n = int((dur + r) * SR)
    e = env(dur, a, 0.25, 0.8, r, n)
    fv = f * _vib(n, 4.6, 0.0025, 0.35, _phase(m, "b"))
    dark = osc(table("brass_dark", m), fv, n)
    bright = osc(table("brass_bright", m), fv * 1.002, n, 0.3)
    mix = np.clip(e * vel, 0, 1) ** 1.6
    return e * (dark * (1 - mix) + bright * mix)


def flute(f, dur, vel=0.8, a=0.07, r=0.25):
    m = _nearest(f)
    n = int((dur + r) * SR)
    fv = f * _vib(n, 5.2, 0.006, 0.2, _phase(m, "f"))
    tone = osc(table("flute", m), fv, n)
    rng = np.random.default_rng(m)
    breath = _bandnoise(n, f * 0.8, f * 4, rng) * 0.12
    return env(dur, a, 0.3, 0.85, r, n) * (tone + breath)


def reed(f, dur, vel=0.8, a=0.06, r=0.3):
    m = _nearest(f)
    n = int((dur + r) * SR)
    fv = f * _vib(n, 5.6, 0.007, 0.3, _phase(m, "r"))
    return env(dur, a, 0.3, 0.85, r, n) * osc(table("reed", m), fv, n)


def clarinet(f, dur, vel=0.8, a=0.05, r=0.25):
    m = _nearest(f)
    n = int((dur + r) * SR)
    fv = f * _vib(n, 4.8, 0.002, 0.4, _phase(m, "c"))
    return env(dur, a, 0.3, 0.9, r, n) * osc(table("clarinet", m), fv, n)


def choir(f, dur, vel=0.8, vowel="a", a=0.35, r=0.8, voices=4):
    m = _nearest(f)
    n = int((dur + r) * SR)
    tab = table("choir_" + vowel, m)
    out = np.zeros(n)
    for v in range(voices):
        det = 1 + (v - (voices - 1) / 2) * 0.004
        fv = f * det * _vib(n, 5.3 + 0.35 * v, 0.006, 0.2, _phase(m, v, "ch"))
        out += osc(tab, fv, n, ph0=(v * 0.29) % 1)
    return out * env(dur, a, 0.5, 0.9, r, n) / voices


def organ(f, dur, vel=0.8, a=0.03, r=0.25):
    m = _nearest(f)
    n = int((dur + r) * SR)
    tab = table("organ", m)
    out = osc(tab, f, n) + 0.6 * osc(tab, f * 1.0025, n, 0.5)
    sub = 0.45 * osc(table("sine", m), f * 0.5, n)
    return env(dur, a, 0.2, 0.95, r, n) * (out / 1.6 + sub)


def pad(f, dur, vel=0.8, a=1.2, r=2.0):
    m = _nearest(f)
    n = int((dur + r) * SR)
    tab = table("pad", m)
    out = np.zeros(n)
    for v in range(3):
        det = 1 + (v - 1) * 0.005
        out += osc(tab, f * det * _vib(n, 0.3 + 0.1 * v, 0.002, 0, v), n, v * 0.3)
    return out * env(dur, a, 1.0, 0.9, r, n) / 3


def _pluck(kind_bright, kind_dark, f, length, t_bright, t_dark, n):
    m = _nearest(f)
    t = np.arange(n) / SR
    b = osc(table(kind_bright, m), f, n)
    d = osc(table(kind_dark, m), f, n, 0.1)
    e = np.minimum(t / 0.003, 1)
    return e * (b * np.exp(-t * 6.9 / t_bright) + 0.8 * d * np.exp(-t * 6.9 / t_dark))


def harp(f, dur, vel=0.8):
    ring = float(np.clip(3.5 * (220 / f) ** 0.45, 0.6, 6.0))
    n = int(ring * SR)
    return _pluck("pluck_bright", "pluck_dark", f, ring, ring * 0.25, ring, n)


def lute(f, dur, vel=0.8):
    ring = float(np.clip(2.2 * (220 / f) ** 0.4, 0.4, 3.5))
    hold = min(ring, dur + 0.15)
    n = int((hold + 0.12) * SR)
    s = _pluck("pluck_bright", "pluck_dark", f, ring, ring * 0.12, ring * 0.7, n)
    t = np.arange(n) / SR
    return s * np.where(t < hold, 1.0, np.exp(-(t - hold) / 0.03))


def pizz(f, dur, vel=0.8):
    n = int(0.6 * SR)
    return _pluck("strings", "pluck_dark", f, 0.6, 0.08, 0.35, n)


def piano(f, dur, vel=0.8):
    ring = float(np.clip(5.0 * (220 / f) ** 0.5, 1.0, 8.0))
    hold = min(ring, dur + 0.3)
    n = int((hold + 0.4) * SR)
    m = _nearest(f)
    t = np.arange(n) / SR
    out = np.zeros(n)
    for v, det in enumerate((0.9995, 1.0006)):
        b = osc(table("piano_bright", m), f * det, n, v * 0.2)
        d = osc(table("pluck_dark", m), f * det, n, v * 0.4)
        out += b * np.exp(-t * 6.9 / (ring * 0.18)) + d * np.exp(-t * 6.9 / ring)
    out *= np.minimum(t / 0.002, 1) / 2
    return out * np.where(t < hold, 1.0, np.exp(-(t - hold) / 0.08))


BELLS = {
    # (频率比, 振幅, 衰减秒数)
    "crystal": ([1, 2.0, 3.01, 4.17, 5.43, 6.8, 8.21], [1, .45, .3, .22, .14, .09, .05], [4.0, 2.6, 1.8, 1.2, .8, .6, .4]),
    "celesta": ([1, 2, 3, 4.01], [1, .25, .1, .05], [1.4, .5, .3, .2]),
    "glock": ([1, 2.76, 5.4, 8.93], [1, .35, .15, .06], [2.6, .8, .4, .2]),
    "church": ([0.5, 1, 1.19, 1.5, 2, 2.5, 3.0], [.6, 1, .5, .3, .35, .15, .1], [9, 6, 4, 3, 2.4, 1.6, 1.2]),
}


def bell(f, dur, vel=0.8, kind="crystal"):
    ratios, amps, decays = BELLS[kind]
    n = int(max(decays) * SR)
    t = np.arange(n) / SR
    out = np.zeros(n)
    for i, (rt, am, dc) in enumerate(zip(ratios, amps, decays)):
        fr = f * rt
        if fr > NYQ:
            continue
        out += am * np.sin(2 * np.pi * fr * t + i) * np.exp(-t * 6.9 / dc)
        if kind in ("crystal", "church"):
            out += 0.4 * am * np.sin(2 * np.pi * (fr + 0.7 + i * 0.3) * t) * np.exp(-t * 6.9 / dc)
    out *= np.minimum(t / 0.0015, 1)
    return out / (np.max(np.abs(out)) or 1)


def crystal(f, dur, vel=0.8):
    return bell(f, dur, vel, "crystal")


def celesta(f, dur, vel=0.8):
    return bell(f, dur, vel, "celesta")


def glock(f, dur, vel=0.8):
    return bell(f, dur, vel, "glock")


def church_bell(f, dur, vel=0.8):
    return bell(f, dur, vel, "church")


def sub_bass(f, dur, vel=0.8):
    m = _nearest(f)
    n = int((dur + 0.2) * SR)
    s = osc(table("sine", m), f, n) + 0.25 * osc(table("strings_dark", m), f, n)
    return env(dur, 0.01, 0.3, 0.8, 0.2, n) * s


# ----------------------------------------------------------------------------
# 打击乐（一次性采样）

def _bandnoise(n, lo, hi, rng):
    x = rng.standard_normal(n)
    X = np.fft.rfft(x)
    fr = np.fft.rfftfreq(n, 1 / SR)
    g = 1.0 / (1 + (lo / np.maximum(fr, 1)) ** 4) / (1 + (fr / hi) ** 4)
    y = np.fft.irfft(X * g, n)
    return y / (np.max(np.abs(y)) or 1)


def _sweep(f0, f1, tau, n):
    t = np.arange(n) / SR
    fi = f1 + (f0 - f1) * np.exp(-t / tau)
    return np.sin(2 * np.pi * np.cumsum(fi) / SR)


@functools.lru_cache(maxsize=None)
def drum(kind, variant=0):
    rng = np.random.default_rng(stable_hash(kind, variant))
    def d(sec):
        n = int(sec * SR)
        return n, np.arange(n) / SR
    if kind == "taiko":
        n, t = d(1.4)
        body = _sweep(110, 52, 0.06, n) * np.exp(-t / 0.35)
        body += 0.3 * _sweep(180, 85, 0.05, n) * np.exp(-t / 0.15)
        slap = _bandnoise(n, 150, 2000, rng) * np.exp(-t / 0.025) * 0.6
        s = body + slap
    elif kind == "bass_drum":
        n, t = d(1.8)
        s = _sweep(75, 40, 0.08, n) * np.exp(-t / 0.5) + _bandnoise(n, 40, 400, rng) * np.exp(-t / 0.08) * 0.4
    elif kind == "timpani":
        n, t = d(2.2)
        f = 73.4 * (1 + 0.03 * variant)
        s = np.zeros(n)
        for rt, am, dc in ((1, 1, .7), (1.5, .55, .45), (1.98, .4, .3), (2.44, .25, .25)):
            s += am * np.sin(2 * np.pi * f * rt * t * (1 + 0.02 * np.exp(-t / 0.05))) * np.exp(-t / dc)
        s += _bandnoise(n, 60, 800, rng) * np.exp(-t / 0.03) * 0.5
    elif kind == "snare":
        n, t = d(0.45)
        s = _bandnoise(n, 1200, 9000, rng) * np.exp(-t / 0.07)
        s += 0.6 * np.sin(2 * np.pi * 190 * t) * np.exp(-t / 0.03) + 0.3 * np.sin(2 * np.pi * 330 * t) * np.exp(-t / 0.02)
    elif kind == "dum":
        n, t = d(0.6)
        s = _sweep(140, 95, 0.03, n) * np.exp(-t / 0.18) + 0.2 * _bandnoise(n, 100, 1500, rng) * np.exp(-t / 0.02)
    elif kind == "tek":
        n, t = d(0.18)
        s = _bandnoise(n, 1800, 9000, rng) * np.exp(-t / 0.025) + 0.5 * np.sin(2 * np.pi * 520 * t) * np.exp(-t / 0.015)
    elif kind == "frame":
        n, t = d(0.7)
        s = _sweep(120, 80, 0.04, n) * np.exp(-t / 0.22) + 0.35 * _bandnoise(n, 300, 4000, rng) * np.exp(-t / 0.04)
    elif kind == "shaker":
        n, t = d(0.12)
        s = _bandnoise(n, 5000, 14000, rng) * (np.minimum(t / 0.012, 1) * np.exp(-t / 0.03))
    elif kind == "tambourine":
        n, t = d(0.35)
        jingle = _bandnoise(n, 6000, 14000, rng) * (1 + 0.6 * np.sin(2 * np.pi * 45 * t))
        s = jingle * np.exp(-t / 0.09)
    elif kind == "crash":
        n, t = d(3.0)
        s = _bandnoise(n, 2500, 15000, rng) * np.exp(-t / 0.9) * np.minimum(t / 0.003, 1)
    elif kind == "woodblock":
        n, t = d(0.12)
        s = (np.sin(2 * np.pi * 820 * t) + 0.5 * np.sin(2 * np.pi * 1290 * t)) * np.exp(-t / 0.025)
    elif kind == "tick":
        n, t = d(0.06)
        s = _bandnoise(n, 3000, 12000, rng) * np.exp(-t / 0.006)
    elif kind == "anvil":
        n, t = d(1.2)
        s = sum(a * np.sin(2 * np.pi * f * t) * np.exp(-t / dc) for f, a, dc in ((1250, 1, .4), (2980, .6, .25), (4410, .4, .15)))
    else:
        raise KeyError(kind)
    return s / (np.max(np.abs(s)) or 1)


def swell(sec, seed=0):
    """反向镲：渐强到下一个强拍。"""
    n = int(sec * SR)
    t = np.arange(n) / SR
    s = _bandnoise(n, 2000, 14000, np.random.default_rng(seed))
    return s * (t / sec) ** 2.5


# ----------------------------------------------------------------------------
# 混响

def reverb_ir(rt60=2.4, length=None, predelay=0.025, damp=0.45, seed=7):
    length = length or min(rt60 * 1.4, 5.0)
    n = int(length * SR)
    t = np.arange(n) / SR
    rng = np.random.default_rng(seed)
    ir = np.zeros((2, n))
    for ch in range(2):
        x = rng.standard_normal(n)
        X = np.fft.rfft(x)
        fr = np.fft.rfftfreq(n, 1 / SR)
        lo = np.fft.irfft(X / (1 + (fr / 2500) ** 2), n)
        hi = x - lo
        ir[ch] = lo * np.exp(-6.9 * t / rt60) + hi * np.exp(-6.9 * t / (rt60 * damp))
        ir[ch] *= np.minimum(t / 0.01, 1)
    pd = int(predelay * SR)
    ir = np.concatenate([np.zeros((2, pd)), ir[:, : n - pd]], axis=1)
    for ch in range(2):
        for k in range(6):  # 早期反射
            ir[ch, pd + int(rng.uniform(0.005, 0.06) * SR)] += rng.uniform(0.2, 0.5) * (1 if rng.random() > 0.5 else -1)
    return ir / np.sqrt(np.sum(ir ** 2) / 2)


def convolve(x, ir, circular):
    """x: (2, n)。circular=True 时做长度 n 的循环卷积（无缝循环）。"""
    n = x.shape[1]
    if circular:
        size = n
        h = np.zeros((2, n))
        m = ir.shape[1]
        for start in range(0, m, n):
            seg = ir[:, start:start + n]
            h[:, : seg.shape[1]] += seg
    else:
        size = n + ir.shape[1]
        h = ir
    X = np.fft.rfft(x, size)
    H = np.fft.rfft(h, size)
    y = np.fft.irfft(X * H, size)
    return y[:, :n] if circular else y


def fft_filter(x, lo=None, hi=None):
    n = x.shape[-1]
    X = np.fft.rfft(x)
    fr = np.fft.rfftfreq(n, 1 / SR)
    g = np.ones_like(fr)
    if lo:
        g /= np.sqrt(1 + (lo / np.maximum(fr, 1)) ** 4)
    if hi:
        g /= np.sqrt(1 + (fr / hi) ** 4)
    return np.fft.irfft(X * g, n)


def shelf_eq(x, low_hz, low_gain, high_hz, high_gain):
    """低频搁架衰减 + 高频搁架提升（一阶形状，增益是线性倍数）。"""
    n = x.shape[-1]
    X = np.fft.rfft(x)
    f = np.fft.rfftfreq(n, 1 / SR)
    lo = np.sqrt((f ** 2 + (low_hz * low_gain) ** 2) / (f ** 2 + low_hz ** 2))
    hi = np.sqrt((high_gain ** 2 * f ** 2 + high_hz ** 2) / (f ** 2 + high_hz ** 2))
    return np.fft.irfft(X * lo * hi, n)
