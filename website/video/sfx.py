"""Synthesizes the film's sound effects as one 30-second layer, timed to promo.html (no samples, no licences).

render.mjs mixes out/sfx.wav under out/music.wav. Edit EVENTS when a shot's timing changes in promo.html.
"""
import wave
from pathlib import Path

import numpy as np

SR = 44100
N = int(SR * 30)
rng = np.random.default_rng(11)
L = np.zeros(N)
R = np.zeros(N)


def add(sig, at, gain=1.0, pan=0.0):
    i = int(at * SR)
    sig = sig[: max(0, N - i)] * gain
    L[i:i + len(sig)] += sig * np.sqrt(0.5 * (1 - pan))
    R[i:i + len(sig)] += sig * np.sqrt(0.5 * (1 + pan))


def t_(dur):
    return np.arange(int(dur * SR)) / SR


def tone(f, dur, decay=8.0, attack=0.004):
    t = t_(dur)
    env = np.minimum(t / attack, 1) * np.exp(-t * decay)
    return np.sin(2 * np.pi * f * t) * env


def noise(dur, decay=30.0):
    t = t_(dur)
    return rng.standard_normal(len(t)) * np.exp(-t * decay)


def onepole(x, cutoff):
    # cutoff may be a number or an array (a sweep)
    a = np.broadcast_to(1 - np.exp(-2 * np.pi * np.asarray(cutoff, dtype=float) / SR), x.shape)
    y = np.empty_like(x)
    acc = 0.0
    for k in range(len(x)):
        acc += a[k] * (x[k] - acc)
        y[k] = acc
    return y


def sting():  # "needs you": two soft descending notes
    return np.concatenate([tone(988, 0.12, 14), tone(740, 0.35, 7)]) * 0.8


def whoosh(dur=0.45):
    t = t_(dur)
    x = rng.standard_normal(len(t))
    sweep = np.sin(np.pi * t / dur)
    return (x - onepole(x, 300 + 2500 * t / dur)) * sweep * 0.35


def buzz():  # a phone vibrating on a desk
    t = t_(0.24)
    s = np.sign(np.sin(2 * np.pi * 150 * t)) * 0.5 + np.sin(2 * np.pi * 300 * t) * 0.3
    return onepole(s, 900) * np.minimum(t / 0.01, 1) * np.minimum((0.24 - t) / 0.03, 1)


def click():
    return onepole(noise(0.03, 160), 5000) * 0.9 + tone(2200, 0.03, 120) * 0.3


def chime(notes, gap=0.09):
    out = np.zeros(int((gap * len(notes) + 0.6) * SR))
    for i, f in enumerate(notes):
        s = tone(f, 0.6, 6) + 0.3 * tone(f * 2, 0.6, 9)
        k = int(i * gap * SR)
        out[k:k + len(s)] += s
    return out * 0.5


def blip(f=1200, dur=0.07):
    return tone(f, dur, 30) * 0.6


EVENTS = [
    (0.02, blip(1568, 0.06), 0.35, 0.0),
    (1.2, sting(), 0.55, 0.0),
    (4.0, whoosh(), 0.8, -0.2),
    (6.0, buzz(), 0.55, 0.2), (6.4, buzz(), 0.55, 0.2),
    (7.6, click(), 0.7, 0.2),
    (10.0, click(), 0.7, 0.0),
    (10.05, chime([1047]), 0.25, 0.0),
    (12.2, chime([659, 880]), 0.45, 0.0),
    (15.0, onepole(noise(0.12, 25), 1800), 0.4, 0.0),
    (16.1, blip(1320), 0.5, 0.0),
    (18.4, blip(990), 0.4, 0.0),
    (18.85, click(), 0.35, -0.1), (18.95, click(), 0.35, 0.1), (19.05, click(), 0.35, -0.1),
    (19.1, chime([1319, 1760]), 0.35, 0.0),
    (20.6, whoosh(0.35), 0.6, 0.2),
    (27.0, blip(2093, 0.05), 0.3, 0.0), (27.08, blip(2637, 0.05), 0.25, 0.0),
]

for at, sig, gain, pan in EVENTS:
    add(sig, at, gain, pan)

mix = np.stack([L, R], axis=1)
peak = np.max(np.abs(mix))
if peak > 0.9:
    mix *= 0.9 / peak
out = Path(__file__).with_name('out')
out.mkdir(exist_ok=True)
with wave.open(str(out / 'sfx.wav'), 'wb') as w:
    w.setnchannels(2)
    w.setsampwidth(2)
    w.setframerate(SR)
    w.writeframes((mix * 32767).astype('<i2').tobytes())
print(out / 'sfx.wav')
