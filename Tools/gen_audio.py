"""Sintesis efek suara & musik latar untuk game stealth (output WAV 16-bit mono)."""
import os
import sys
import wave

import numpy as np

OUT = sys.argv[1]
SFX_DIR = os.path.join(OUT, "SFX")
MUSIC_DIR = os.path.join(OUT, "Music")
os.makedirs(SFX_DIR, exist_ok=True)
os.makedirs(MUSIC_DIR, exist_ok=True)

SR = 44100
rng = np.random.default_rng(7)


def save(path, data, sr=SR, peak=0.9):
    data = np.asarray(data, dtype=np.float64)
    m = np.max(np.abs(data))
    if m > 0:
        data = data / m * peak
    pcm = (np.clip(data, -1, 1) * 32767).astype(np.int16)
    with wave.open(path, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(sr)
        w.writeframes(pcm.tobytes())
    print("wrote", os.path.relpath(path, OUT), f"{len(data) / sr:.2f}s")


def t_axis(dur, sr=SR):
    return np.arange(int(dur * sr)) / sr


def lowpass(x, cutoff, sr=SR):
    """One-pole lowpass; cutoff boleh array (sweep)."""
    cutoff = np.broadcast_to(np.asarray(cutoff, dtype=np.float64), x.shape)
    a = 1.0 - np.exp(-2.0 * np.pi * cutoff / sr)
    y = np.empty_like(x)
    acc = 0.0
    for i in range(len(x)):
        acc += a[i] * (x[i] - acc)
        y[i] = acc
    return y


def highpass(x, cutoff, sr=SR):
    return x - lowpass(x, cutoff, sr)


def exp_env(t, decay):
    return np.exp(-t / decay)


def adsr(n, a, d, s, r, sr=SR):
    a, d, r = int(a * sr), int(d * sr), int(r * sr)
    sustain_len = max(0, n - a - d - r)
    env = np.concatenate([
        np.linspace(0, 1, max(a, 1), endpoint=False),
        np.linspace(1, s, max(d, 1), endpoint=False),
        np.full(sustain_len, s),
        np.linspace(s, 0, max(r, 1)),
    ])
    return np.resize(env, n) if len(env) != n else env


def noise(n):
    return rng.uniform(-1, 1, n)


def tone(freq, t, harmonics=(1.0,), phase=0.0):
    """freq boleh array (glide)."""
    freq = np.broadcast_to(np.asarray(freq, dtype=np.float64), t.shape)
    ph = 2 * np.pi * np.cumsum(freq) / SR + phase
    out = np.zeros_like(t)
    for k, amp in enumerate(harmonics, start=1):
        out += amp * np.sin(k * ph)
    return out


def note_hz(midi):
    return 440.0 * 2 ** ((midi - 69) / 12)


def place(buf, clip, start):
    end = min(len(buf), start + len(clip))
    buf[start:end] += clip[: end - start]


# ============================================================
# SFX
# ============================================================

def footstep(seed):
    r = np.random.default_rng(seed)
    t = t_axis(0.14)
    n = r.uniform(-1, 1, len(t))
    body = lowpass(n, 900 + seed * 120) * exp_env(t, 0.025)
    thump = np.sin(2 * np.pi * (70 + seed * 8) * t) * exp_env(t, 0.03)
    grit = highpass(n, 3000) * exp_env(t, 0.01) * 0.25
    return body * 1.2 + thump * 0.8 + grit


for i in range(1, 4):
    save(os.path.join(SFX_DIR, f"footstep_{i}.wav"), footstep(i))

# Guling / dash: whoosh + benturan kecil
t = t_axis(0.5)
sweep = 400 + 2600 * np.sin(np.pi * np.clip(t / 0.4, 0, 1))
whoosh = lowpass(noise(len(t)), sweep) * np.sin(np.pi * np.clip(t / 0.42, 0, 1)) ** 1.5
land = np.zeros_like(t)
li = int(0.38 * SR)
tl = t[: len(t) - li]
land[li:] = np.sin(2 * np.pi * 65 * tl) * exp_env(tl, 0.05) + lowpass(noise(len(tl)), 700) * exp_env(tl, 0.03)
save(os.path.join(SFX_DIR, "roll.wav"), whoosh * 1.3 + land * 0.7)

# Jongkok: gesekan kain
t = t_axis(0.22)
rustle = highpass(lowpass(noise(len(t)), 5000), 900) * np.sin(np.pi * t / 0.22) ** 2
save(os.path.join(SFX_DIR, "crouch.wav"), rustle, peak=0.5)

# Ambil item: dua nada bel
t = t_axis(0.45)
bell = lambda f, tt: tone(f, tt, (1, 0.35, 0.12)) * exp_env(tt, 0.12)
pick = np.zeros_like(t)
place(pick, bell(note_hz(88), t_axis(0.3)), 0)
place(pick, bell(note_hz(95), t_axis(0.35)), int(0.08 * SR))
save(os.path.join(SFX_DIR, "pickup.wav"), pick, peak=0.7)

# Misi selesai: arpeggio naik
t = t_axis(1.0)
obj = np.zeros_like(t)
for k, m in enumerate([72, 76, 79, 84]):
    tt = t_axis(0.6)
    place(obj, tone(note_hz(m), tt, (1, 0.5, 0.2, 0.1)) * exp_env(tt, 0.25) * adsr(len(tt), 0.005, 0.05, 0.6, 0.3), int(k * 0.09 * SR))
save(os.path.join(SFX_DIR, "objective.wav"), obj, peak=0.7)

# Ditolak / terkunci: dua buzz rendah
t = t_axis(0.32)
den = np.zeros_like(t)
for k in range(2):
    tt = t_axis(0.12)
    sq = np.tanh(4 * np.sin(2 * np.pi * 180 * tt))
    place(den, sq * adsr(len(tt), 0.003, 0.02, 0.8, 0.03), int(k * 0.16 * SR))
save(os.path.join(SFX_DIR, "denied.wav"), lowpass(den, 2500), peak=0.55)

# Pintu geser: desis pneumatik + motor + klunk
t = t_axis(1.0)
hiss = highpass(noise(len(t)), 2500) * np.exp(-t / 0.25) * 0.6
motor = tone(110 + 60 * np.clip(t / 0.7, 0, 1), t, (1, 0.5, 0.3)) * adsr(len(t), 0.05, 0.1, 0.7, 0.25) * 0.5
clunk = np.zeros_like(t)
ci = int(0.75 * SR)
tc = t[: len(t) - ci]
clunk[ci:] = (np.sin(2 * np.pi * 90 * tc) + 0.5 * lowpass(noise(len(tc)), 900)) * exp_env(tc, 0.05)
save(os.path.join(SFX_DIR, "door_open.wav"), hiss + motor + clunk, peak=0.75)


def metal_hit(dur, partials, decays, amp=1.0):
    tt = t_axis(dur)
    out = np.zeros_like(tt)
    for f, d in zip(partials, decays):
        out += np.sin(2 * np.pi * f * tt + rng.uniform(0, 6)) * exp_env(tt, d)
    out += highpass(noise(len(tt)), 3000) * exp_env(tt, 0.006) * 1.5
    return out * amp


# Ventilasi terbuka: klank logam + kerincing
t = t_axis(0.9)
vent = np.zeros_like(t)
place(vent, metal_hit(0.8, [523, 1187, 1873, 2711, 3490], [0.25, 0.18, 0.12, 0.08, 0.05]), 0)
place(vent, metal_hit(0.5, [610, 1320, 2140], [0.12, 0.08, 0.05], 0.5), int(0.22 * SR))
place(vent, metal_hit(0.4, [580, 1250], [0.08, 0.05], 0.3), int(0.36 * SR))
save(os.path.join(SFX_DIR, "vent_open.wav"), vent, peak=0.75)

# Tick saat menahan E
t = t_axis(0.05)
save(os.path.join(SFX_DIR, "hold_tick.wav"), np.sin(2 * np.pi * 2200 * t) * exp_env(t, 0.008), peak=0.4)

# Hack selesai: kicauan digital naik
t = t_axis(0.7)
hack = np.zeros_like(t)
for k, m in enumerate([79, 83, 86, 91, 95]):
    tt = t_axis(0.07)
    sq = np.sign(np.sin(2 * np.pi * note_hz(m) * tt)) * 0.6
    place(hack, sq * adsr(len(tt), 0.002, 0.01, 0.7, 0.02), int(k * 0.08 * SR))
tt = t_axis(0.3)
place(hack, tone(note_hz(96), tt, (1, 0.3)) * exp_env(tt, 0.1), int(0.42 * SR))
save(os.path.join(SFX_DIR, "hack_done.wav"), lowpass(hack, 6000), peak=0.6)

# Lempar: whoosh kecil
t = t_axis(0.25)
throw = lowpass(noise(len(t)), 1500 + 3000 * np.sin(np.pi * t / 0.25)) * np.sin(np.pi * t / 0.25) ** 2
save(os.path.join(SFX_DIR, "throw.wav"), throw, peak=0.5)

# Kaleng jatuh: klang + pantulan
t = t_axis(0.8)
can = np.zeros_like(t)
place(can, metal_hit(0.6, [950, 1820, 2950, 4100, 5230], [0.12, 0.09, 0.06, 0.04, 0.03]), 0)
place(can, metal_hit(0.4, [980, 1900, 3050], [0.07, 0.05, 0.03], 0.5), int(0.17 * SR))
place(can, metal_hit(0.3, [1010, 1950], [0.04, 0.03], 0.25), int(0.29 * SR))
save(os.path.join(SFX_DIR, "can_impact.wav"), can, peak=0.85)

# Listrik padam: zap + dengung turun
t = t_axis(1.4)
f = 60 * np.exp(-t / 0.5) + 18
hum = tone(f, t, (1, 0.6, 0.4, 0.3, 0.2)) * np.exp(-t / 0.55)
zap = highpass(noise(len(t)), 1800) * np.exp(-t / 0.06) * 1.2
thunk = np.sin(2 * np.pi * 55 * t) * np.exp(-t / 0.12)
save(os.path.join(SFX_DIR, "power_off.wav"), hum + zap + thunk, peak=0.85)

# Listrik menyala: dengung naik
t = t_axis(1.1)
f = 25 + 35 * np.clip(t / 0.8, 0, 1)
hum = tone(f, t, (1, 0.6, 0.4, 0.3, 0.2)) * adsr(len(t), 0.4, 0.2, 0.7, 0.4)
buzz = highpass(noise(len(t)), 2500) * adsr(len(t), 0.01, 0.05, 0.1, 0.2) * 0.3
save(os.path.join(SFX_DIR, "power_on.wav"), hum + buzz, peak=0.7)

# Penjaga curiga: "hm?" naik
t = t_axis(0.45)
sus = tone(440 * (1 + 0.5 * np.clip(t / 0.35, 0, 1) ** 2), t, (1, 0.25, 0.1)) * adsr(len(t), 0.02, 0.05, 0.7, 0.15)
save(os.path.join(SFX_DIR, "guard_suspicious.wav"), sus, peak=0.5)

# Penjaga melihat Player: stab disonan
t = t_axis(0.8)
stab = np.zeros_like(t)
for f in [note_hz(69), note_hz(70), note_hz(75), note_hz(81)]:
    stab += tone(f, t, (1, 0.5, 0.33, 0.25, 0.2, 0.16))
stab *= np.exp(-t / 0.22)
stab += lowpass(noise(len(t)), 3000) * np.exp(-t / 0.04) * 2
stab += np.sin(2 * np.pi * 55 * t) * np.exp(-t / 0.25) * 2
save(os.path.join(SFX_DIR, "guard_alert.wav"), stab, peak=0.9)

# Kamera mendeteksi: 3 bip alarm
t = t_axis(0.75)
cam = np.zeros_like(t)
for k in range(3):
    tt = t_axis(0.13)
    place(cam, np.sign(np.sin(2 * np.pi * 1050 * tt)) * adsr(len(tt), 0.002, 0.01, 0.8, 0.02), int(k * 0.22 * SR))
save(os.path.join(SFX_DIR, "camera_alarm.wav"), lowpass(cam, 4500), peak=0.6)

# UI klik
t = t_axis(0.07)
click = tone(1400, t, (1, 0.3)) * exp_env(t, 0.012) + highpass(noise(len(t)), 4000) * exp_env(t, 0.003) * 0.4
save(os.path.join(SFX_DIR, "ui_click.wav"), click, peak=0.5)

# Menang: fanfare
t = t_axis(2.2)
win = np.zeros_like(t)
for k, m in enumerate([60, 64, 67, 72]):
    tt = t_axis(0.35)
    place(win, tone(note_hz(m), tt, (1, 0.6, 0.3, 0.15)) * adsr(len(tt), 0.005, 0.05, 0.7, 0.15), int(k * 0.12 * SR))
tt = t_axis(1.6)
chord = sum(tone(note_hz(m), tt, (1, 0.5, 0.25, 0.12)) for m in [60, 64, 67, 72, 76])
place(win, chord * adsr(len(tt), 0.01, 0.2, 0.6, 0.9) * 0.6, int(0.5 * SR))
save(os.path.join(SFX_DIR, "win.wav"), win, peak=0.75)

# Kalah: turun minor + gemuruh
t = t_axis(2.2)
lose = np.zeros_like(t)
for k, m in enumerate([67, 65, 63, 60]):
    tt = t_axis(0.6)
    place(lose, tone(note_hz(m), tt, (1, 0.5, 0.3, 0.2)) * adsr(len(tt), 0.01, 0.1, 0.6, 0.3), int(k * 0.28 * SR))
tt = t_axis(1.8)
place(lose, (np.sin(2 * np.pi * 41 * tt) + lowpass(noise(len(tt)), 200) * 2) * np.exp(-tt / 0.7) * 0.8, int(0.84 * SR))
save(os.path.join(SFX_DIR, "lose.wav"), lose, peak=0.8)

# ============================================================
# MUSIK (loop mulus: dirender dengan wrap-around)
# ============================================================

MSR = 22050


def mt(dur):
    return np.arange(int(dur * MSR)) / MSR


def mtone(freq, tt, harmonics=(1.0,)):
    out = np.zeros_like(tt)
    for k, a in enumerate(harmonics, start=1):
        out += a * np.sin(2 * np.pi * freq * k * tt + rng.uniform(0, 6))
    return out


def wrap_place(buf, clip, start):
    n = len(buf)
    idx = (np.arange(len(clip)) + start) % n
    np.add.at(buf, idx, clip)


def menv(n, a, r):
    """Envelope attack-release (detik) sepanjang n sampel."""
    a, r = int(a * MSR), int(r * MSR)
    env = np.ones(n)
    env[:a] = np.linspace(0, 1, a)
    env[n - r:] = np.linspace(1, 0, r)
    return env


def mlowpass(x, cutoff):
    a = 1.0 - np.exp(-2.0 * np.pi * cutoff / MSR)
    y = np.empty_like(x)
    acc = 0.0
    for i in range(len(x)):
        acc += a * (x[i] - acc)
        y[i] = acc
    return y


def pad(midis, dur, gain=1.0):
    tt = mt(dur)
    out = np.zeros_like(tt)
    for m in midis:
        f = note_hz(m)
        for det in (-0.12, 0.0, 0.12):  # detune = pad lebar
            out += mtone(f * 2 ** (det / 12), tt, (1, 0.3, 0.12))
    return out * menv(len(tt), dur * 0.35, dur * 0.4) * gain / (len(midis) * 3)


def pluck(midi, dur=0.8, gain=1.0):
    tt = mt(dur)
    return mtone(note_hz(midi), tt, (1, 0.4, 0.2, 0.1)) * np.exp(-tt / (dur * 0.25)) * gain


def kick(gain=1.0):
    tt = mt(0.35)
    f = 50 + 90 * np.exp(-tt / 0.03)
    return np.sin(2 * np.pi * np.cumsum(f) / MSR) * np.exp(-tt / 0.12) * gain


def hat(gain=1.0):
    tt = mt(0.06)
    n = rng.uniform(-1, 1, len(tt))
    return (n - mlowpass(n, 5000)) * np.exp(-tt / 0.015) * gain


def snare(gain=1.0):
    tt = mt(0.25)
    n = rng.uniform(-1, 1, len(tt))
    return (mlowpass(n, 4000) * np.exp(-tt / 0.07) + np.sin(2 * np.pi * 190 * tt) * np.exp(-tt / 0.05)) * gain


# ---- music_stealth: ambient tegang, 72 BPM, 4 akor x 2 bar ----
bpm = 72
beat = 60 / bpm
bar = beat * 4
chords = [[50, 57, 62, 65], [46, 53, 58, 62], [43, 50, 55, 58], [45, 52, 57, 61]]  # Dm Bb Gm A
length = bar * 2 * len(chords)
buf = np.zeros(int(length * MSR))
for i, ch in enumerate(chords):
    start = int(i * bar * 2 * MSR)
    wrap_place(buf, pad(ch, bar * 2 + beat * 2, 0.9), start - int(beat * MSR))
    # drone bass
    tt = mt(bar * 2)
    wrap_place(buf, mtone(note_hz(ch[0] - 12), tt, (1, 0.5, 0.2)) * menv(len(tt), 0.5, 0.8) * 0.35, start)
    # denyut bass pelan per ketukan
    for b in range(8):
        tt = mt(beat * 0.9)
        pulse = mtone(note_hz(ch[0] - 12), tt, (1, 0.6, 0.3)) * np.exp(-tt / 0.18) * 0.25
        wrap_place(buf, pulse, start + int(b * beat * MSR))
    # pluck arpeggio jarang
    pattern = [ch[1] + 12, None, ch[2] + 12, None, ch[3] + 12, None, ch[2] + 12, ch[1] + 24]
    for b, m in enumerate(pattern):
        if m is not None:
            wrap_place(buf, pluck(m, 1.2, 0.12), start + int(b * beat * MSR))
# hi-hat lembut tiap ketukan
for b in range(int(length / beat)):
    wrap_place(buf, hat(0.05 if b % 2 else 0.08), int(b * beat * MSR))
save(os.path.join(MUSIC_DIR, "music_stealth.wav"), mlowpass(buf, 6000), MSR, peak=0.8)

# ---- music_alert: aksi, 140 BPM ----
bpm = 140
beat = 60 / bpm
bar = beat * 4
roots = [38, 38, 34, 36]  # D D Bb C
length = bar * len(roots) * 2
buf = np.zeros(int(length * MSR))
for rep in range(2):
    for i, root in enumerate(roots):
        start = int((rep * len(roots) + i) * bar * MSR)
        # ostinato bass 16th
        for s in range(16):
            tt = mt(beat / 4)
            accent = 1.0 if s % 4 == 0 else 0.6
            m = root + (12 if s in (6, 14) else 0)
            note = mtone(note_hz(m), tt, (1, 0.7, 0.5, 0.35, 0.25)) * np.exp(-tt / 0.08) * 0.35 * accent
            wrap_place(buf, note, start + int(s * beat / 4 * MSR))
        # drum
        for b in range(4):
            wrap_place(buf, kick(0.9), start + int(b * beat * MSR))
            if b in (1, 3):
                wrap_place(buf, snare(0.5), start + int(b * beat * MSR))
            for h in range(2):
                wrap_place(buf, hat(0.15), start + int((b + h * 0.5) * beat * MSR))
        # stab senar
        for b in (0, 1.5, 3):
            tt = mt(beat * 0.6)
            stab = sum(mtone(note_hz(m), tt, (1, 0.5, 0.33, 0.25)) for m in (root + 24, root + 27, root + 31))
            wrap_place(buf, stab * np.exp(-tt / 0.1) * 0.12, start + int(b * beat * MSR))
# nada tinggi tegang
tt = mt(length)
buf += mtone(note_hz(86), tt, (1,)) * (0.5 + 0.5 * np.sin(2 * np.pi * tt / (bar * 2))) * 0.03
save(os.path.join(MUSIC_DIR, "music_alert.wav"), mlowpass(buf, 7000), MSR, peak=0.8)

# ---- music_menu: misterius, 60 BPM ----
bpm = 60
beat = 60 / bpm
bar = beat * 4
chords = [[45, 52, 57, 60], [41, 48, 53, 57], [43, 50, 55, 59], [40, 47, 52, 56]]  # Am F G E
length = bar * len(chords) * 2
buf = np.zeros(int(length * MSR))
melody = [76, None, 72, 74, None, None, 71, None, 72, None, 69, None, 71, None, 68, None]
for i, ch in enumerate(chords * 2):
    start = int(i * bar * MSR)
    wrap_place(buf, pad(ch, bar + beat, 1.0), start - int(beat * 0.5 * MSR))
    tt = mt(bar)
    wrap_place(buf, mtone(note_hz(ch[0] - 12), tt, (1, 0.4)) * menv(len(tt), 0.3, 0.6) * 0.3, start)
    for b in range(4):
        m = melody[(i * 4 + b) % len(melody)]
        if m is not None:
            wrap_place(buf, pluck(m, 2.0, 0.1), start + int(b * beat * MSR))
save(os.path.join(MUSIC_DIR, "music_menu.wav"), mlowpass(buf, 5000), MSR, peak=0.75)

print("done")
