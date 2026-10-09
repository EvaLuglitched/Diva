"""Synthesize the Diva mech sound effects (no recorded samples, so no licence questions).

Writes 16-bit mono 44.1 kHz WAV files: heavy mech footsteps, water-gun start / loop / stop, thruster hum loop.
Usage: python make_sounds.py OUTDIR
"""
import sys, wave
import numpy as np

SR = 44100
OUT = sys.argv[1]
rng = np.random.default_rng(20261008)


def t_axis(sec):
    return np.arange(int(SR * sec)) / SR


def band(x, lo, hi):
    """Band-pass by zeroing FFT bins (fine for noise colouring)."""
    X = np.fft.rfft(x)
    f = np.fft.rfftfreq(len(x), 1 / SR)
    X[(f < lo) | (f > hi)] = 0
    return np.fft.irfft(X, len(x))


def tilt(x, f0, slope_db_per_oct):
    X = np.fft.rfft(x)
    f = np.maximum(np.fft.rfftfreq(len(x), 1 / SR), 1)
    X *= (f / f0) ** (slope_db_per_oct / 6.02)
    return np.fft.irfft(X, len(x))


def env(t, attack, decay):
    return np.minimum(t / max(attack, 1e-4), 1) * np.exp(-np.maximum(t - attack, 0) / decay)


def norm(x, peak=.89):
    return x / (np.abs(x).max() + 1e-9) * peak


def fade(x, fin=.004, fout=.03):
    n = len(x); a = int(SR * fin); b = int(SR * fout)
    w = np.ones(n)
    if a: w[:a] = np.linspace(0, 1, a)
    if b: w[-b:] = np.linspace(1, 0, b)
    return x * w


def save(name, x):
    x = np.clip(x, -1, 1)
    with wave.open(f'{OUT}/{name}.wav', 'wb') as w:
        w.setnchannels(1); w.setsampwidth(2); w.setframerate(SR)
        w.writeframes((x * 32767).astype('<i2').tobytes())
    print(name, f'{len(x) / SR:.2f}s', 'peak', round(float(np.abs(x).max()), 2), 'rms', round(float(np.sqrt((x ** 2).mean())), 3))


# ---------------------------------------------------------------- footsteps: heavy thud + armour clank + tiny servo
def footstep(seed):
    r = np.random.default_rng(seed)
    t = t_axis(.75)
    f0 = r.uniform(72, 88)
    # pitch drops a little as the foot settles: deep "boom"
    phase = 2 * np.pi * np.cumsum(f0 * (1 + .6 * np.exp(-t / .03))) / SR
    thud = np.sin(phase) * env(t, .004, .16)
    thud += .5 * np.sin(2 * phase + .3) * env(t, .003, .07)
    body = band(r.standard_normal(len(t)), 120, 600) * env(t, .002, .06) * 2.2         # dust / impact (audible on laptops)
    knock = np.sin(2 * np.pi * r.uniform(170, 210) * t) * env(t, .002, .05) * .6         # wooden-ish mid thump
    grit = band(r.standard_normal(len(t)), 900, 5000) * env(t, .001, .02) * .25       # sand crunch
    # armour plates: inharmonic metal partials, short and bright, slightly after impact
    clank = np.zeros_like(t)
    d = int(SR * r.uniform(.006, .014))
    for k, (fr, amp, dec) in enumerate([(r.uniform(520, 600), .5, .09), (r.uniform(1310, 1420), .35, .07),
                                        (r.uniform(2350, 2550), .25, .05), (r.uniform(3700, 3900), .12, .03)]):
        clank[d:] += amp * np.sin(2 * np.pi * fr * t[:-d] + k) * env(t[:-d], .001, dec)
    servo = np.zeros_like(t)
    s0 = int(SR * .11)
    st = t[:int(SR * .14)]
    sweep = 2 * np.pi * np.cumsum(np.linspace(380, 520, len(st))) / SR
    servo[s0:s0 + len(st)] = .08 * np.sign(np.sin(sweep)) * np.sin(np.pi * st / st[-1]) ** 2
    servo = band(servo, 200, 3000)
    x = .8 * thud + body + knock + grit + .55 * clank + servo
    return fade(norm(x, .9), .001, .08)


for i in range(4):
    save(f'mech_step_{i + 1}', footstep(100 + i))

# ---------------------------------------------------------------- water gun
def water_noise(sec, seed):
    r = np.random.default_rng(seed)
    n = r.standard_normal(int(SR * sec))
    hiss = band(n, 1200, 9000)
    hiss = tilt(hiss, 3000, -3)
    rush = band(r.standard_normal(len(n)), 150, 1100) * .55
    # turbulence: slow random amplitude flutter
    t = t_axis(sec)
    flutter = 1 + .18 * np.sin(2 * np.pi * 7.3 * t + 1.1) + .12 * np.sin(2 * np.pi * 13.1 * t + .4) \
        + .1 * band(r.standard_normal(len(n)), 2, 30) / (np.abs(band(r.standard_normal(len(n)), 2, 30)).max() + 1e-9)
    # droplet patter: sparse short blips
    pat = np.zeros(len(n))
    for _ in range(int(sec * 55)):
        p = r.integers(0, len(n) - 800)
        f = r.uniform(1800, 4200)
        tt = np.arange(600) / SR
        pat[p:p + 600] += r.uniform(.1, .3) * np.sin(2 * np.pi * f * tt * (1 + 2 * tt)) * np.exp(-tt / .004)
    return (hiss + rush) * flutter + pat * .6


# seamless loop: make 2.6 s, crossfade the tail into the head
L = 2.0; X = .3
w = water_noise(L + X + .01, 7)  # a few extra samples so the crossfade slice is full length
n, k = int(SR * L), int(SR * X)
loop = w[:n].copy()
ramp = np.linspace(0, 1, k)
loop[:k] = w[:k] * np.sqrt(ramp) + w[n:n + k] * np.sqrt(1 - ramp)
save('water_spray_loop', norm(loop, .7))

# start: pressure "chk" + rising whistle + burst of spray
t = t_axis(.55)
click = band(rng.standard_normal(len(t)), 400, 6000) * env(t, .0005, .012)
whistle = np.sin(2 * np.pi * np.cumsum(np.linspace(900, 2600, len(t))) / SR) * env(t, .02, .09) * .35
burst = water_noise(.55, 11) * np.minimum(t / .06, 1) * np.exp(-np.maximum(t - .12, 0) / .35)
save('water_spray_start', fade(norm(1.2 * click + whistle + .9 * burst / np.abs(burst).max(), .85), .0005, .05))

# stop: valve "tss" falloff + a few drips
t = t_axis(.9)
tail = water_noise(.9, 13) * np.exp(-t / .08)
drips = np.zeros_like(t)
for when, f in ((.18, 1500), (.34, 1850), (.52, 1300), (.7, 2100)):
    p = int(SR * when); tt = t[:int(SR * .06)]
    drips[p:p + len(tt)] += .5 * np.sin(2 * np.pi * f * tt * (1 + 6 * tt)) * np.exp(-tt / .012)
save('water_spray_stop', fade(norm(tail / np.abs(tail).max() + drips, .75), .001, .05))

# ---------------------------------------------------------------- thruster hum loop (quiet ambience)
L = 3.0
t = t_axis(L)
hum = np.zeros_like(t)
for h, a in ((1, .8), (2, .6), (3, .45), (4, .2), (5, .15), (7, .08)):
    hum += a * np.sin(2 * np.pi * 110 * h * t + h)     # 110 Hz: whole number of cycles in 3 s -> seamless
k = int(SR * .25)
airx = band(np.random.default_rng(5).standard_normal(len(t) + k), 300, 2500)
air = airx[:len(t)].copy()
r_ = np.linspace(0, 1, k)
air[:k] = airx[:k] * np.sqrt(r_) + airx[len(t):] * np.sqrt(1 - r_)  # tail continues into head -> seamless
air = air / np.abs(air).max()
wob = 1 + .08 * np.sin(2 * np.pi * (1 / 1.5) * t)    # 2 cycles in 3 s -> seamless
save('thruster_hum_loop', norm((hum / np.abs(hum).max() * .7 + .35 * air) * wob, .5))

# ---------------------------------------------------------------- bubble machine puff: soft air + a few cute "bloops"
t = t_axis(1.1)
air = band(np.random.default_rng(21).standard_normal(len(t)), 300, 3500) * np.minimum(t / .08, 1) * np.exp(-t / .45)
blo = np.zeros_like(t)
for when, f0, f1 in ((.10, 420, 900), (.26, 520, 1100), (.41, 380, 820), (.58, 600, 1250), (.77, 470, 980)):
    p = int(SR * when); tt = t[:int(SR * .07)]
    ph = 2 * np.pi * np.cumsum(np.linspace(f0, f1, len(tt))) / SR
    blo[p:p + len(tt)] += np.sin(ph) * np.exp(-tt / .018) * np.minimum(tt / .004, 1)
save('bubble_blow', fade(norm(.35 * air / np.abs(air).max() + .8 * blo, .7), .002, .08))
