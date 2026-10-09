"""Diva 出场开场（原创，摔角出场风格的短号角）：嘚嘚——嘚嘚——轰！约 3 秒。

两组短促的铜管 + 失真吉他重音，然后烟火重击、镲片和观众欢呼。旋律是自己写的，
不使用任何现成曲目或采样，全部合成。需要 numpy 和 scipy。用法：
    python make_entrance.py 输出.wav
时间点（秒）和 DivaShowDirector.EntranceHits 一致：0.00 / 0.16（第一组）、0.62 / 0.78（第二组）、1.24（重击）。
"""
import sys, wave
import numpy as np
from scipy.signal import butter, lfilter

SR = 44100
LENGTH = 3.6
rng = np.random.default_rng(20261008)
out = np.zeros((int(SR * LENGTH), 2))

def t_(sec): return np.arange(int(SR * sec)) / SR

def add(sig, at, pan=0.0, gain=1.0):
    i = int(at * SR)
    sig = sig[: len(out) - i] * gain
    out[i:i + len(sig), 0] += sig * np.sqrt(.5 * (1 - pan))
    out[i:i + len(sig), 1] += sig * np.sqrt(.5 * (1 + pan))

def lp(x, hz, order=2): b, a = butter(order, hz / (SR / 2)); return lfilter(b, a, x)
def hp(x, hz, order=2): b, a = butter(order, hz / (SR / 2), 'high'); return lfilter(b, a, x)
def bp(x, lo, hi): b, a = butter(2, [lo / (SR / 2), hi / (SR / 2)], 'band'); return lfilter(b, a, x)
def env(n, attack, decay):
    t = np.arange(n) / SR
    return np.minimum(1, t / max(attack, 1e-4)) * np.exp(-t / decay)
def noise(sec): return rng.uniform(-1, 1, int(SR * sec))
def saw(hz, t, ph=0.0): return 2 * ((hz * t + ph) % 1) - 1

def brass(notes_hz, sec):
    """铜管齐奏的重音：几支锯齿波稍微错开音高，滤波器先亮后暗（"嘚"的起音）。"""
    t = t_(sec)
    vib = 1 + .004 * np.sin(2 * np.pi * 5.5 * t) * np.clip(t / .08, 0, 1)
    sig = np.zeros_like(t)
    for hz in notes_hz:
        for det in (-4, 0, 5):
            sig += saw(hz * (1 + det / 1000) * vib, t, rng.uniform())
    sig /= 3 * len(notes_hz)
    bright = lp(sig, 5200) * env(len(t), .006, .05) + lp(sig, 1400) * .8
    shape = np.minimum(1, t / .008) * np.clip((sec - t) / .03, 0, 1)
    return np.tanh(bright * 2.2) * shape * .55

def power_chord(root_hz, sec, decay):
    t = t_(sec)
    sig = sum(a * saw(root_hz * m * (1 + d / 1000), t, rng.uniform()) for m, a, d in ((1, 1, 0), (1.5, .8, .7), (2, .6, -.5)))
    tone = lp(np.tanh(sig * 3.5), 2800)
    return tone * env(len(t), .003, decay) * np.clip((sec - t) / .04, 0, 1) * .3

def kick():
    t = t_(.5)
    f = 42 + 100 * np.exp(-t / .035)
    return np.sin(2 * np.pi * np.cumsum(f) / SR) * env(len(t), .001, .18)

def boom():
    t = t_(2.2)
    f = 28 + 70 * np.exp(-t / .1)
    return np.sin(2 * np.pi * np.cumsum(f) / SR) * env(len(t), .002, .8) * 1.3 + lp(noise(2.2), 900) * env(len(t), .001, .3) * 1.6

def crash(sec=2.3): n = hp(noise(sec), 5000); return n * env(len(n), .002, .75) * .5

def crowd(sec):
    n = noise(sec)
    t = t_(sec)
    roar = bp(n, 350, 1900) * 3.0
    shape = np.clip(t / .25, 0, 1) * np.clip((sec - t) / 1.4, 0, 1) ** 1.3
    return roar * shape * (1 + .2 * np.sin(2 * np.pi * .9 * t))

# 音高：E 小调色彩。第一组 E-E，第二组 G-A，然后在高八度 E 上重击（自己写的动机）
E4, G4, A4, B4, E5 = 329.63, 392.0, 440.0, 493.88, 659.25
E2, G2, A2, E3 = 82.41, 98.0, 110.0, 164.81
hits = [(0.00, [E4, B4], E2), (0.16, [E4, B4], E2), (0.62, [G4, 587.33], G2), (0.78, [A4, E5], A2)]
for at, chord, root in hits:
    add(brass(chord, .13), at, pan=-.15)
    add(brass(chord, .13), at + .004, pan=.15)
    add(power_chord(root, .14, .08), at, pan=-.5)
    add(power_chord(root, .14, .08), at + .005, pan=.5)
    add(kick(), at, gain=.8)

# 1.24 s：烟火重击 + 大和弦 + 欢呼
add(boom(), 1.24, gain=.9)
add(crash(), 1.24, pan=-.3); add(crash(), 1.26, pan=.3)
add(brass([E4, B4, E5], 1.6), 1.24, pan=-.1)
add(brass([E4, B4, E5], 1.6), 1.244, pan=.1)
add(power_chord(E2, 2.2, .9), 1.24, pan=-.5)
add(power_chord(E2, 2.2, .9), 1.245, pan=.5)
add(power_chord(E3, 2.2, .9), 1.25, gain=.5)
add(crowd(2.3), 1.2, gain=.55)

peak = np.max(np.abs(out))
mix = np.tanh(out / peak * 1.3) / np.tanh(1.3) * .89
mix[-int(SR * .3):] *= np.linspace(1, 0, int(SR * .3))[:, None]
pcm = (mix * 32767).astype(np.int16)
with wave.open(sys.argv[1] if len(sys.argv) > 1 else 'diva_entrance.wav', 'wb') as w:
    w.setnchannels(2); w.setsampwidth(2); w.setframerate(SR)
    w.writeframes(pcm.tobytes())
print('wrote', round(len(pcm) / SR, 2), 's, peak', round(float(np.max(np.abs(mix))), 3), 'nan', bool(np.isnan(mix).any()))
