#!/usr/bin/env python3
"""Original, fully synthesized music and sound effects for Cloverbrook (D-014).

No samples or third-party audio: every sound is generated here from sine/noise
building blocks, so the output carries no licence obligations. Writes OGG Vorbis
files to game/assets/audio via ffmpeg.

    python3 tools/audio/synth.py
"""
import pathlib, subprocess, tempfile, wave
import numpy as np

SR = 44100
OUT = pathlib.Path(__file__).resolve().parents[2] / "game/assets/audio"
rng = np.random.default_rng(7)


def t(seconds):
    return np.arange(int(seconds * SR)) / SR


def env(n, attack=0.005, decay=0.3, sustain=0.0, release=0.05):
    """ADSR-ish envelope over n samples (decay is an exponential time constant)."""
    x = np.arange(n) / SR
    a = np.clip(x / max(attack, 1e-4), 0, 1)
    d = sustain + (1 - sustain) * np.exp(-np.maximum(x - attack, 0) / max(decay, 1e-4))
    r = np.clip((n / SR - x) / max(release, 1e-4), 0, 1)
    return a * d * r


def note_hz(name):
    names = {"C": 0, "C#": 1, "D": 2, "D#": 3, "E": 4, "F": 5, "F#": 6, "G": 7, "G#": 8, "A": 9, "A#": 10, "B": 11}
    return 440.0 * 2 ** ((names[name[:-1]] + 12 * (int(name[-1]) + 1) - 69) / 12)


def tone(freq, seconds, partials=((1, 1.0),), attack=0.005, decay=0.4, sustain=0.0, release=0.08, vibrato=0.0):
    x = t(seconds)
    phase = 2 * np.pi * freq * x
    if vibrato:
        phase += vibrato * np.sin(2 * np.pi * 5.2 * x)
    y = sum(amp * np.sin(k * phase) for k, amp in partials)
    return y * env(len(x), attack, decay, sustain, release)


def place(track, sound, at):
    i = int(at * SR)
    end = min(len(track), i + len(sound))
    if i < len(track):
        track[i:end] += sound[: end - i]


def reverb(x, seconds=1.8, wet=0.28):
    n = int(seconds * SR)
    ir = rng.standard_normal(n) * np.exp(-np.arange(n) / SR / (seconds / 5))
    ir[: int(0.012 * SR)] = 0  # pre-delay
    size = 1 << int(np.ceil(np.log2(len(x) + n)))
    y = np.fft.irfft(np.fft.rfft(x, size) * np.fft.rfft(ir, size), size)[: len(x)]
    y /= np.max(np.abs(y)) + 1e-9
    return (1 - wet) * x + wet * y * np.max(np.abs(x))


def lowpass(x, cutoff):
    a = np.exp(-2 * np.pi * cutoff / SR)
    y = np.empty_like(x); acc = 0.0
    for i, v in enumerate(x):
        acc = (1 - a) * v + a * acc; y[i] = acc
    return y


def write(name, mono, stereo_width=0.0, quality=4):
    peak = np.max(np.abs(mono)) + 1e-9
    mono = mono / peak * 0.89
    if stereo_width:
        delay = int(0.011 * SR)
        right = np.concatenate([np.zeros(delay), mono[:-delay]])
        left = mono
        stereo = np.stack([left * (1 - stereo_width * 0.3) + right * stereo_width * 0.3,
                           right * (1 - stereo_width * 0.3) + left * stereo_width * 0.3], axis=1)
    else:
        stereo = np.stack([mono, mono], axis=1)
    pcm = (np.clip(stereo, -1, 1) * 32767).astype(np.int16)
    with tempfile.NamedTemporaryFile(suffix=".wav", delete=False) as tmp:
        with wave.open(tmp.name, "wb") as w:
            w.setnchannels(2); w.setsampwidth(2); w.setframerate(SR); w.writeframes(pcm.tobytes())
        OUT.mkdir(parents=True, exist_ok=True)
        subprocess.run(["ffmpeg", "-y", "-loglevel", "error", "-i", tmp.name, "-c:a", "libvorbis", "-q:a", str(quality),
                        str(OUT / f"{name}.ogg")], check=True)
    print("wrote", name, f"{len(mono) / SR:.1f}s")


# --- instruments -----------------------------------------------------------------
def piano(f, dur):  # soft electric-piano: fundamental, gentle 2nd/3rd partials, bell attack
    return tone(f, dur, ((1, 1.0), (2, 0.35), (3, 0.12), (4, 0.05)), attack=0.004, decay=0.9, release=0.15)


def musicbox(f, dur):
    return tone(f, dur, ((1, 1.0), (3, 0.28), (5.4, 0.08)), attack=0.002, decay=0.55, release=0.1)


def pad(f, dur):
    x = t(dur)
    y = sum(np.sin(2 * np.pi * f * d * x) for d in (0.997, 1.0, 1.004)) / 3
    y += 0.3 * np.sin(2 * np.pi * f * 2.002 * x)
    return y * env(len(x), attack=0.6, decay=99, sustain=1.0, release=0.7)


def bass(f, dur):
    return tone(f, dur, ((1, 1.0), (2, 0.25)), attack=0.01, decay=0.7, sustain=0.25, release=0.12)


# --- music -------------------------------------------------------------------------
def yard_theme():
    bpm = 88; beat = 60 / bpm; bar = 4 * beat
    chords = [("F", ["F3", "A3", "C4"], "F2"), ("Dm", ["D3", "F3", "A3"], "D2"), ("Bb", ["A#2", "D3", "F3"], "A#1"), ("C", ["C3", "E3", "G3"], "C2"),
              ("F", ["F3", "A3", "C4"], "F2"), ("Am", ["A2", "C3", "E3"], "A1"), ("Bb", ["A#2", "D3", "F3"], "A#1"), ("C", ["C3", "E3", "G3", "A#3"], "C2")]
    # Two eight-bar phrases: the melody answers itself, ending on the tonic so the loop resolves.
    melody = [
        [("C5", 0, 1), ("A4", 1, 1), ("F4", 2, 1.5), ("G4", 3.5, 0.5)],
        [("A4", 0, 1.5), ("F4", 1.5, 0.5), ("D4", 2, 2)],
        [("D4", 0, 1), ("F4", 1, 1), ("A#4", 2, 1), ("A4", 3, 1)],
        [("G4", 0, 2), ("E4", 2, 1), ("G4", 3, 1)],
        [("A4", 0, 1), ("C5", 1, 1), ("F5", 2, 1.5), ("E5", 3.5, 0.5)],
        [("C5", 0, 1.5), ("A4", 1.5, 0.5), ("E4", 2, 2)],
        [("F4", 0, 1), ("A#4", 1, 1), ("D5", 2, 1), ("C5", 3, 1)],
        [("A4", 0, 1), ("G4", 1, 1), ("F4", 2, 2)],
    ]
    phrases = 2
    length = bar * len(chords) * phrases
    total = length * 2  # render twice and keep the second pass so reverb tails wrap seamlessly
    track = np.zeros(int(total * SR) + SR * 3)
    for p in range(phrases * 2):
        for b, (_, notes, root) in enumerate(chords):
            start = (p * len(chords) + b) * bar
            for n in notes:
                place(track, 0.10 * pad(note_hz(n), bar + 0.2), start)
            place(track, 0.35 * bass(note_hz(root), beat * 1.8), start)
            place(track, 0.25 * bass(note_hz(root), beat * 1.5), start + 2.5 * beat)
            for k in range(4):  # gentle broken-chord piano on each beat
                place(track, 0.12 * piano(note_hz(notes[k % len(notes)]) * 2, beat * 1.4), start + k * beat + (0.5 * beat if k % 2 else 0))
            lead = musicbox if p % 2 else piano
            for name, offset, dur in melody[b]:
                place(track, 0.30 * lead(note_hz(name), dur * beat + 0.3), start + offset * beat)
            for k in range(8):  # soft shaker
                hit = rng.standard_normal(int(0.03 * SR)) * env(int(0.03 * SR), 0.001, 0.008)
                place(track, (0.025 if k % 2 else 0.04) * hit, start + k * beat / 2)
    track = reverb(track, 2.2, 0.3)
    loop = track[int(length * SR): int(2 * length * SR)]
    write("music_yard", loop, stereo_width=1.0, quality=3)


# --- sound effects -----------------------------------------------------------------
def sfx():
    write("ui_click", tone(1320, 0.09, ((1, 1), (2, 0.2)), attack=0.001, decay=0.03) * 0.6)
    pop = tone(660, 0.18, ((1, 1), (2, 0.3)), decay=0.07); pop2 = tone(990, 0.2, ((1, 1),), decay=0.08)
    on = np.zeros(int(0.3 * SR)); place(on, pop, 0); place(on, pop2, 0.07)
    write("ui_toggle", reverb(on, 0.6, 0.2))
    coins = np.zeros(int(0.9 * SR))
    for i, n in enumerate(["E6", "G#6", "B6", "E7"]):
        place(coins, tone(note_hz(n), 0.5, ((1, 1), (2.76, 0.4), (5.4, 0.15)), decay=0.16) * (0.9 - i * 0.12), i * 0.06)
    write("coins", reverb(coins, 0.8, 0.25))
    chime = np.zeros(int(2.6 * SR))
    for i, n in enumerate(["C6", "A5", "F5", "C5"]):
        place(chime, musicbox(note_hz(n), 1.6), i * 0.22)
    write("day_end", reverb(chime, 1.6, 0.35))
    fan = np.zeros(int(3.2 * SR))
    for i, n in enumerate(["F4", "A4", "C5", "F5"]):
        place(fan, piano(note_hz(n), 1.4) * 0.8, i * 0.14)
    for n in ["F4", "A4", "C5", "F5", "A5"]:
        place(fan, 0.5 * tone(note_hz(n), 1.8, ((1, 1), (2, 0.3)), attack=0.02, decay=1.2), 0.62)
    for i in range(14):
        place(fan, 0.18 * tone(note_hz("C7") * (1 + 0.06 * rng.standard_normal()), 0.25, ((1, 1),), decay=0.06), 0.7 + i * 0.07)
    write("birth_fanfare", reverb(fan, 1.8, 0.32))
    bell = np.zeros(int(1.6 * SR))
    place(bell, tone(note_hz("A5"), 1.4, ((1, 1), (2.4, 0.5), (3.9, 0.25), (5.3, 0.12)), decay=0.5), 0)
    place(bell, tone(note_hz("A5"), 1.4, ((1, 1), (2.4, 0.5), (3.9, 0.25)), decay=0.5) * 0.7, 0.18)
    write("race_bell", reverb(bell, 1.2, 0.3))
    cheer = np.zeros(int(0.8 * SR))
    for i, n in enumerate(["A5", "C#6", "E6"]):
        place(cheer, musicbox(note_hz(n), 0.6) * 0.7, i * 0.05)
    write("care_cheer", reverb(cheer, 0.7, 0.3))
    # Gallop: three-beat canter "da-da-dum" hoofbeat loop on soft ground.
    loop = np.zeros(int(1.2 * SR))
    for k, (at, gain) in enumerate([(0.0, 0.7), (0.11, 0.8), (0.24, 1.0), (0.6, 0.7), (0.71, 0.8), (0.84, 1.0)]):
        n = int(0.09 * SR)
        thud = lowpass(rng.standard_normal(n), 380) * env(n, 0.001, 0.025) * 6
        thud += 0.5 * tone(95 + 10 * (k % 3), 0.09, ((1, 1),), decay=0.03)
        place(loop, thud * gain, at)
    write("hooves_gallop", loop)
    # Yard ambience: soft breeze plus occasional little bird calls.
    amb = lowpass(rng.standard_normal(int(24 * SR)), 260) * 0.6
    amb *= 0.6 + 0.4 * np.sin(2 * np.pi * 0.07 * t(24))
    for k in range(18):
        start = rng.uniform(0.5, 22.5)
        base = rng.uniform(2600, 3800)
        for j in range(rng.integers(2, 5)):
            d = rng.uniform(0.06, 0.12); x = t(d)
            sweep = base * (1 + 0.25 * np.sin(np.pi * x / d)) * (1 + 0.1 * j)
            call = np.sin(2 * np.pi * np.cumsum(sweep) / SR) * env(len(x), 0.005, d / 2.5, release=0.02)
            place(amb, 0.10 * call, start + j * rng.uniform(0.1, 0.16))
    write("ambience_yard", reverb(amb, 1.4, 0.3), stereo_width=1.0, quality=2)


def rain():
    """Soft rain on the yard (D-018): pink-ish hiss, a low roof rumble and scattered droplets."""
    rng = np.random.default_rng(18)
    n = int(20 * SR)
    hiss = lowpass(rng.standard_normal(n), 3200) - lowpass(rng.standard_normal(n), 500) * 0.5
    hiss *= 0.22 * (0.85 + 0.15 * np.sin(2 * np.pi * 0.05 * t(20)))
    rumble = lowpass(rng.standard_normal(n), 140) * 0.5
    drops = np.zeros(n)
    for _ in range(900):
        at = rng.uniform(0, 19.9); d = 0.03; x = t(d)
        f = rng.uniform(1800, 4200)
        place(drops, rng.uniform(0.02, 0.07) * np.sin(2 * np.pi * f * x) * env(len(x), 0.001, 0.006), at)
    mix = hiss + rumble + drops
    # Crossfade the ends so the loop is seamless.
    fade = int(0.5 * SR); ramp = np.linspace(0, 1, fade)
    mix[:fade] = mix[:fade] * ramp + mix[-fade:] * (1 - ramp)
    write("ambience_rain", mix[:-fade], stereo_width=1.0, quality=2)


def extras():
    """Ribbon sparkle, a journal page swish and a friendly hoof clop (D-018 follow-up)."""
    r = np.random.default_rng(19)
    rib = np.zeros(int(1.8 * SR))
    for i, n in enumerate(["C6", "E6", "G6", "C7"]):
        place(rib, musicbox(note_hz(n), 1.0) * 0.75, i * 0.07)
    for i in range(10):
        place(rib, 0.12 * tone(note_hz("E7") * (1 + 0.08 * r.standard_normal()), 0.3, ((1, 1),), decay=0.08), 0.32 + i * 0.05)
    write("ribbon", reverb(rib, 1.4, 0.32))
    n = int(0.35 * SR)
    swish = (lowpass(r.standard_normal(n), 2400) - lowpass(r.standard_normal(n), 300)) * np.sin(np.pi * np.arange(n) / n) ** 2 * 0.5
    write("page", reverb(swish, 0.4, 0.15))
    clop = np.zeros(int(0.5 * SR))
    for at, f in [(0.0, 900), (0.16, 760)]:
        m = int(0.07 * SR)
        hit = lowpass(r.standard_normal(m), 1600) * env(m, 0.0005, 0.012) * 3 + 0.4 * tone(f, 0.07, ((1, 1), (2.3, 0.4)), decay=0.015)
        place(clop, hit, at)
    write("hoof_clop", reverb(clop, 0.5, 0.2))


def shutter():
    """A soft camera shutter for photo mode (D-023): two quick clicks with a breath of noise."""
    r = np.random.default_rng(23)
    snap = np.zeros(int(0.35 * SR))
    for at, f in [(0.0, 2400), (0.09, 1800)]:
        m = int(0.05 * SR)
        click = lowpass(r.standard_normal(m), 5000) * env(m, 0.0005, 0.008) * 2 + 0.3 * tone(f, 0.05, ((1, 1),), decay=0.01)
        place(snap, click, at)
    write("shutter", reverb(snap, 0.4, 0.15))


if __name__ == "__main__":
    import sys
    if sys.argv[1:] == ["shutter"]:
        shutter(); sys.exit(0)
    if sys.argv[1:] == ["rain"]:
        rain()
    elif sys.argv[1:] == ["extras"]:
        extras()
    else:
        yard_theme()
        sfx()
        rain()
        extras()
        shutter()
