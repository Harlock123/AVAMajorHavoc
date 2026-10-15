#!/usr/bin/env python3
"""Synthesises AVAMajorHavoc's sound effects and tunes into ../src/AVAMajorHavoc/Sounds as 16-bit mono WAV.

Nothing is sampled from any arcade machine: every effect is built here from square, triangle and
noise voices, and the tunes are new compositions. Run it from anywhere:
    python3 tools/make_sounds.py
"""
import math
import os
import random
import struct
import wave

RATE = 22050
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "src", "AVAMajorHavoc", "Sounds")
RNG = random.Random(1982)


def write(name, samples, volume=0.7):
    peak = max(1e-9, max(abs(s) for s in samples))
    with wave.open(os.path.join(OUT, name), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(b"".join(struct.pack("<h", int(s / peak * volume * 32767)) for s in samples))


def voice(kind, phase):
    p = phase % 1.0
    if kind == "square":
        return 1.0 if p < 0.5 else -1.0
    if kind == "pulse":
        return 1.0 if p < 0.25 else -1.0
    if kind == "tri":
        return 4 * p - 1 if p < 0.5 else 3 - 4 * p
    if kind == "saw":
        return 2 * p - 1
    if kind == "sine":
        return math.sin(2 * math.pi * p)
    return RNG.uniform(-1, 1)


def tone(seconds, f0, f1=None, kind="square", attack=0.004, release=1.0, vibrato=0.0, vib_rate=8.0, gain=1.0):
    """A note gliding from f0 to f1 (exponentially), with a short attack and a shaped decay."""
    n = int(RATE * seconds)
    f1 = f1 or f0
    out, phase, held = [], 0.0, 0.0
    for i in range(n):
        t = i / RATE
        f = f0 * (f1 / f0) ** (i / max(1, n - 1))
        if vibrato:
            f *= 1 + vibrato * math.sin(2 * math.pi * vib_rate * t)
        phase += f / RATE
        if kind == "noise":
            # Noise re-sampled at the voice's rate: a lower 'pitch' sounds rougher.
            if int(phase) != int(phase - f / RATE):
                held = RNG.uniform(-1, 1)
            s = held
        else:
            s = voice(kind, phase)
        env = min(1.0, t / attack) * (1 - i / n) ** release
        out.append(s * env * gain)
    return out


def silence(seconds):
    return [0.0] * int(RATE * seconds)


def cat(*parts):
    out = []
    for p in parts:
        out.extend(p)
    return out


def mix(*parts):
    n = max(len(p) for p in parts)
    out = [0.0] * n
    for p in parts:
        for i, v in enumerate(p):
            out[i] += v
    return out


NOTES = {"C": 0, "D": 2, "E": 4, "F": 5, "G": 7, "A": 9, "B": 11}


def hz(note):
    n = NOTES[note[0]]
    rest = note[1:]
    while rest and rest[0] in "#b":
        n += 1 if rest[0] == "#" else -1
        rest = rest[1:]
    return 440.0 * 2 ** ((12 * (int(rest) + 1) + n - 69) / 12)


def tune(bpm, lead, bass, kind="pulse"):
    """Two voices: a lead and a bass line, as (note, beats) lists; '-' is a rest."""
    beat = 60.0 / bpm

    def line(notes, k, gain, release):
        out = []
        for note, beats in notes:
            d = beats * beat
            out.extend(silence(d) if note == "-" else tone(d, hz(note), kind=k, release=release, gain=gain))
        return out

    return mix(line(lead, kind, 0.55, 0.6), line(bass, "tri", 0.6, 0.3))


def seq(bpm, notes, kind="pulse", gain=0.55, release=0.6):
    beat = 60.0 / bpm
    out = []
    for note, beats in notes:
        d = beats * beat
        out.extend(silence(d) if note == "-" else tone(d, hz(note), kind=kind, release=release, gain=gain))
    return out




def main():
    os.makedirs(OUT, exist_ok=True)
    for f in os.listdir(OUT):
        if f.endswith(".wav"):
            os.remove(os.path.join(OUT, f))
    # Space: the fighter's laser, the fleet's shots, explosions, and the shriek of a diver.
    write("Fire.wav", mix(tone(0.14, 3000, 900, "saw", release=0.9), tone(0.14, 1500, 450, "square", release=0.9, gain=0.4)), 0.22)
    write("EnemyFire.wav", tone(0.12, 800, 450, "pulse", release=0.8), 0.16)
    write("Explode.wav", mix(tone(0.45, 3000, 140, "noise", release=0.8), tone(0.35, 450, 60, "square", release=0.8, gain=0.5)), 0.4)
    write("Dive.wav", tone(0.7, 2400, 500, "tri", release=0.4, vibrato=0.05, vib_rate=18), 0.22)
    # Docking: the thruster's roar, the clunk of landing, and a crash.
    write("Thrust.wav", mix(tone(0.3, 700, 400, "noise", attack=0.03, release=0.4), tone(0.3, 60, 55, "saw", release=0.4, gain=0.5)), 0.22)
    write("Landed.wav", cat(mix(tone(0.15, 180, 90, "square", release=0.8), tone(0.1, 1400, 600, "noise", release=0.8, gain=0.4)),
                            *[tone(0.07, hz(n), kind="pulse", release=0.4) for n in ["E5", "G5", "C6"]]), 0.38)
    write("Crash.wav", mix(tone(1.0, 2500, 50, "noise", release=0.5), tone(1.0, 300, 30, "saw", release=0.5, gain=0.8)), 0.5)
    # On foot: a hop, a thud, a breath of air, the shield's hum, a laser's zap.
    write("Jump.wav", tone(0.14, 300, 900, "square", release=0.6), 0.16)
    write("Land.wav", tone(0.06, 160, 90, "tri", release=0.8), 0.2)
    write("Air.wav", cat(tone(0.08, 900, 1400, "tri", release=0.5), tone(0.12, 1400, 2000, "sine", release=0.5)), 0.3)
    write("Shield.wav", tone(0.5, 420, 440, "square", release=0.2, vibrato=0.08, vib_rate=30), 0.12)
    write("Zap.wav", mix(tone(0.2, 1800, 1600, "saw", release=0.5, vibrato=0.1, vib_rate=60), tone(0.2, 4000, 2000, "noise", release=0.6, gain=0.4)), 0.16)
    # The core: set with a rising whoop, then the alarm; and the station's end.
    write("CoreSet.wav", cat(tone(0.5, 200, 1600, "square", release=0.2), *[tone(0.08, hz(n), kind="pulse", release=0.4) for n in ["C6", "G5", "C6"]]), 0.36)
    write("Alarm.wav", cat(tone(0.16, 880, kind="square", release=0.2), tone(0.16, 660, kind="square", release=0.2)), 0.18)
    write("Death.wav", cat(mix(tone(0.9, 3000, 60, "noise", release=0.5), tone(0.9, 500, 40, "saw", release=0.5, gain=0.7)),
                           *[tone(0.14, hz(n), kind="tri", release=0.4) for n in ["E5", "C5", "A4"]]), 0.5)
    write("StationBoom.wav", cat(mix(tone(2.6, 1600, 25, "noise", release=0.35), tone(2.6, 220, 20, "saw", release=0.35, gain=0.9)),
                                 *[tone(0.12, hz(n), kind="pulse", release=0.4) for n in ["C5", "E5", "G5", "C6", "E6", "G6", "C7"]]), 0.6)
    write("LevelStart.wav", cat(*[tone(0.08, hz(n), kind="pulse", release=0.4) for n in ["G4", "C5", "E5", "G5"]]), 0.32)
    write("ExtraLife.wav", cat(*[tone(0.07, hz(n), kind="pulse", release=0.3) for n in ["C6", "G5", "C6", "G5", "C6", "E6"]]), 0.38)

    # The station: a deep mechanical hum, ticking over. Loops (four seconds, whole cycles).
    n = int(RATE * 4)
    hum = []
    for i in range(n):
        t = i / RATE
        tick = 0.5 + 0.5 * (1 if (t * 4) % 1 < 0.1 else 0)
        hum.append(math.sin(2 * math.pi * 50 * t) * 0.7 + 0.3 * math.sin(2 * math.pi * 75 * t) * tick + 0.08 * math.sin(2 * math.pi * 400 * t) * math.sin(2 * math.pi * 0.5 * t))
    write("Music.wav", hum, 0.22)

    # The title: a bold march in C minor (an original tune).
    lead = [("C5", 0.75), ("C5", 0.25), ("Eb5", 0.5), ("G5", 0.5), ("C6", 1), ("Bb5", 0.5), ("G5", 0.5), ("Ab5", 2),
            ("F5", 0.75), ("F5", 0.25), ("Ab5", 0.5), ("C6", 0.5), ("Eb6", 1), ("D6", 0.5), ("Bb5", 0.5), ("C6", 2),
            ("Eb6", 0.5), ("D6", 0.5), ("C6", 0.5), ("Bb5", 0.5), ("Ab5", 1), ("G5", 1), ("F5", 0.5), ("G5", 0.5), ("B4", 1), ("C5", 3)]
    bass = [("C3", 1), ("G2", 1), ("C3", 1), ("Eb3", 1), ("Ab2", 2), ("Ab2", 2),
            ("F2", 1), ("C3", 1), ("Ab2", 1), ("Bb2", 1), ("C3", 2), ("C3", 2),
            ("Ab2", 2), ("F2", 2), ("G2", 2), ("C3", 3)]
    write("TitleTune.wav", cat(tune(150, lead, bass, kind="pulse"), silence(1.2)), 0.42)
    write("StartTune.wav", tune(160, [("C5", 0.5), ("G5", 0.5), ("C6", 0.5), ("Eb6", 0.5), ("D6", 0.5), ("C6", 2)], [("C3", 1), ("G2", 1), ("C3", 2)], kind="pulse"), 0.45)
    write("GameOver.wav", tune(72, [("G4", 1), ("Eb4", 1), ("D4", 1), ("B3", 1), ("C4", 4)], [("C3", 2), ("Ab2", 2), ("G2", 2), ("C2", 2)], kind="tri"), 0.45)


if __name__ == "__main__":
    main()
