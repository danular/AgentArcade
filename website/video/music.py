"""Synthesizes the 30-second soundtrack for the promo (no samples, no licences needed).

120 BPM, so one bar = 2 s and every scene cut in promo.html lands on a bar line.
  0-2   coin blip + riser
  2-10  drums + bass
  10-22 + arpeggio and pad
  22-26 build (snare roll, filter opens)
  26-30 logo hit, pad tail, fade
Writes out/music.wav (44.1 kHz, 16-bit stereo).
"""
import wave
from pathlib import Path

import numpy as np

SR = 44100
DUR = 30.0
BPM = 120
BEAT = 60 / BPM
BAR = 4 * BEAT
N = int(SR * DUR)
t_all = np.arange(N) / SR
rng = np.random.default_rng(7)

L = np.zeros(N)
R = np.zeros(N)


def note(n):  # MIDI note -> Hz
    return 440.0 * 2 ** ((n - 69) / 12)


def add(sig, start, pan=0.0, gain=1.0):
    i = int(start * SR)
    if i >= N:
        return
    sig = sig[: N - i] * gain
    L[i:i + len(sig)] += sig * np.sqrt(0.5 * (1 - pan))
    R[i:i + len(sig)] += sig * np.sqrt(0.5 * (1 + pan))


def lowpass(x, cutoff):
    # one-pole low-pass; cutoff may be an array (sweeps)
    cutoff = np.broadcast_to(cutoff, x.shape)
    a = 1 - np.exp(-2 * np.pi * cutoff / SR)
    y = np.empty_like(x)
    acc = 0.0
    for k in range(len(x)):
        acc += a[k] * (x[k] - acc)
        y[k] = acc
    return y


def env(length, a=0.005, d=0.1, s=0.6, r=0.1):
    n = int(length * SR)
    t = np.arange(n) / SR
    e = np.where(t < a, t / a, s + (1 - s) * np.exp(-(t - a) / max(d, 1e-4)))
    rel = int(r * SR)
    if rel and n > rel:
        e[-rel:] *= np.linspace(1, 0, rel)
    return e


def saw(f, length, detune=0.0):
    t = np.arange(int(length * SR)) / SR
    ph = (f * (1 + detune)) * t
    return 2 * (ph - np.floor(ph + 0.5))


def square(f, length, pw=0.5):
    t = np.arange(int(length * SR)) / SR
    ph = (f * t) % 1
    return np.where(ph < pw, 1.0, -1.0)


def kick(length=0.45):
    t = np.arange(int(length * SR)) / SR
    f = 45 + 110 * np.exp(-t * 28)
    ph = 2 * np.pi * np.cumsum(f) / SR
    return np.sin(ph) * np.exp(-t * 7) + 0.3 * np.sin(ph) * np.exp(-t * 40)


def hat(length=0.06, open_=False):
    n = int((0.22 if open_ else length) * SR)
    x = rng.standard_normal(n)
    x = x - lowpass(x, 7000)
    t = np.arange(n) / SR
    return x * np.exp(-t * (14 if open_ else 60))


def clap(length=0.3):
    n = int(length * SR)
    x = rng.standard_normal(n)
    x = lowpass(x - lowpass(x, 900), 5000)
    t = np.arange(n) / SR
    e = np.exp(-t * 18)
    for off in (0.0, 0.011, 0.022):
        k = int(off * SR)
        e[k:k + 220] += 0.6
    tone = np.sin(2 * np.pi * 190 * t) * np.exp(-t * 30)
    return x * e * 0.9 + tone * 0.4


def blip(freqs, step=0.06):
    out = []
    for f in freqs:
        s = square(f, step, 0.25) * env(step, 0.002, 0.04, 0.3, 0.01)
        out.append(s)
    return lowpass(np.concatenate(out), 6000)


# A minor: Am - F - C - G, one chord per bar
CHORDS = [[57, 60, 64], [53, 57, 60], [48, 52, 55], [55, 59, 62]]
ROOTS = [45, 41, 36, 43]


def bar_of(time):
    return int(time // BAR)


# --- intro: coin blip + riser -------------------------------------------------
add(blip([note(n) for n in (76, 83, 88, 95)]), 0.05, gain=0.35)
riser_len = 2.0
rt = np.arange(int(riser_len * SR)) / SR
riser = rng.standard_normal(len(rt))
riser = lowpass(riser, 300 + 6000 * (rt / riser_len) ** 2) * (rt / riser_len) ** 1.5
add(riser, 0.0, gain=0.25)

# --- drums ----------------------------------------------------------------------
for b in range(int(2 / BEAT), int(26 / BEAT)):
    tb = b * BEAT
    add(kick(), tb, gain=0.9)
    if b % 2 == 1:
        add(clap(), tb, gain=0.45)
    add(hat(), tb + BEAT / 2, pan=0.3, gain=0.18)
    if tb >= 10:
        add(hat(), tb + BEAT / 4, pan=-0.3, gain=0.09)
        add(hat(), tb + 3 * BEAT / 4, pan=-0.3, gain=0.09)
# snare roll build 24-26
for k in range(16):
    add(clap(0.12), 24 + k * BEAT / 4, gain=0.12 + 0.025 * k)

# --- bass: eighth notes on the chord root ---------------------------------------
for e8 in range(int(2 / (BEAT / 2)), int(26 / (BEAT / 2))):
    tb = e8 * BEAT / 2
    root = ROOTS[bar_of(tb) % 4]
    f = note(root + (12 if e8 % 2 else 0))
    s = (saw(f, BEAT / 2) + 0.5 * square(f / 2, BEAT / 2)) * env(BEAT / 2, 0.003, 0.08, 0.5, 0.03)
    add(lowpass(s, 900), tb, gain=0.28)

# --- arpeggio: 16ths, chord tones up two octaves -------------------------------
for s16 in range(int(10 / (BEAT / 4)), int(26 / (BEAT / 4))):
    tb = s16 * BEAT / 4
    ch = CHORDS[bar_of(tb) % 4]
    seq = [ch[0], ch[1], ch[2], ch[1] + 12, ch[2], ch[1], ch[0] + 12, ch[2]]
    f = note(seq[s16 % 8] + 12)
    cutoff = 2500 + (3500 if tb > 22 else 0)
    s = square(f, BEAT / 4, 0.3) * env(BEAT / 4, 0.002, 0.05, 0.25, 0.02)
    add(lowpass(s, cutoff), tb, pan=0.4 if s16 % 2 else -0.4, gain=0.09)

# --- pad: detuned saws, sidechained to the kick ---------------------------------
pad = np.zeros(N)
for bar in range(5, 13):
    tb = bar * BAR
    ch = CHORDS[bar % 4]
    sig = sum(saw(note(n), BAR, d) for n in ch for d in (-0.004, 0.004))
    sig = lowpass(sig, 1400 + (1600 if tb >= 22 else 0)) * env(BAR, 0.25, 1.0, 0.8, 0.25)
    i = int(tb * SR)
    pad[i:i + len(sig)] += sig[: N - i]
duck = np.ones(N)
for b in range(int(10 / BEAT), int(26 / BEAT)):
    i = int(b * BEAT * SR)
    k = int(0.25 * SR)
    duck[i:i + k] = np.minimum(duck[i:i + k], 0.35 + 0.65 * np.linspace(0, 1, k) ** 0.6)
pad *= duck * 0.05
L += pad
R += pad

# --- logo hit at 26 s and a long tail -------------------------------------------
hit_len = 4.0
add(kick(0.8), 26, gain=1.0)
add(clap(0.6), 26, gain=0.4)
tail = sum(saw(note(n), hit_len, d) for n in (57, 64, 69, 72, 76) for d in (-0.003, 0.003))
tail = lowpass(tail, 2200) * env(hit_len, 0.01, 1.6, 0.35, 1.8)
add(tail, 26, gain=0.05)
add(blip([note(n) for n in (88, 91, 95, 100)], 0.05), 26.1, gain=0.25)
sub = np.sin(2 * np.pi * note(33) * np.arange(int(hit_len * SR)) / SR) * env(hit_len, 0.01, 1.2, 0.0, 0.5)
add(sub, 26, gain=0.35)

# --- master ------------------------------------------------------------------------
mix = np.stack([L, R], axis=1)
mix = np.tanh(mix * 1.4) / np.tanh(1.4)
mix /= np.max(np.abs(mix)) / 0.89
fade = int(1.2 * SR)
mix[-fade:] *= np.linspace(1, 0, fade)[:, None]
mix[: int(0.01 * SR)] *= np.linspace(0, 1, int(0.01 * SR))[:, None]

out = Path(__file__).with_name('out')
out.mkdir(exist_ok=True)
with wave.open(str(out / 'music.wav'), 'wb') as w:
    w.setnchannels(2)
    w.setsampwidth(2)
    w.setframerate(SR)
    w.writeframes((mix * 32767).astype('<i2').tobytes())
print(out / 'music.wav')
