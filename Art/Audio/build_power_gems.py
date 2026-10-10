"""Generates the power gem sounds for the Chimes sound set.

Kenney's Interface Sounds pack (where most Chimes clips come from) has nothing that sounds like
a blast, so these four are synthesized, like the horn (see build_horn.py). Needs only Python's
standard library.

    python Art/Audio/build_power_gems.py    # writes four WAVs into Assets/Audio/Chimes/

The four sounds, and how each gets its character:
  - power_gem_created.wav: two quick bell notes a fifth apart, the second higher. A bell is a
    sine wave plus a few overtones that are *not* whole multiples of it (that's what makes it
    ring instead of hum), each fading at its own speed.
  - flame_detonate.wav: a low "thump" whose pitch drops fast, under a burst of noise that
    fades quickly. Noise is random samples: on its own it's a hiss, short and loud it's a blast.
  - star_detonate.wav: a bright tone that sweeps upward, like a beam shooting out, with a
    shimmer (a second tone slightly out of tune with the first, so they "beat" against
    each other).
  - hypercube_activate.wav: a buzzy square wave hopping quickly between three notes, so it
    crackles like electricity, then a bell on the top note.

Each sound is kept under half a second so chains of them stay snappy. The game raises their
pitch for each wave of a chain reaction, so they're tuned a little low to leave room.
"""

import math
import random
import struct
import wave
from pathlib import Path

SAMPLE_RATE = 44100          # samples per second (CD quality)
PEAK = 0.85                  # loudest sample, where 1.0 is the most a WAV can hold

OUTPUT = Path(__file__).resolve().parents[2] / "Assets" / "Audio" / "Chimes"

# Bell overtones: (multiple of the note's frequency, loudness, how fast it fades). The odd
# multiples (2.76, 5.40) are what an actual struck bar or bell rings at.
BELL_PARTIALS = ((1.0, 1.0, 6.0), (2.76, 0.45, 10.0), (5.40, 0.25, 16.0), (8.93, 0.12, 24.0))

# Random, but the same every run (a "seed" fixes the sequence), so rebuilding gives
# byte-identical files and git doesn't see a change.
noise = random.Random(86)


def bell(t, freq):
    """A struck bell of pitch freq, t seconds after it was struck."""
    if t < 0:
        return 0.0
    value = 0.0
    for multiple, loudness, fade in BELL_PARTIALS:
        # e^(-fade * t) is a smooth fade: it halves every ln(2)/fade seconds.
        value += loudness * math.exp(-fade * t) * math.sin(2 * math.pi * freq * multiple * t)
    # A 3 ms ramp at the start so the bell doesn't click.
    return value * min(1.0, t / 0.003)


def power_gem_created(t):
    # E5 then B5 (a fifth up), 70 ms apart.
    return bell(t, 659.25) + 0.9 * bell(t - 0.07, 987.77)


def flame_detonate(t):
    # The thump: pitch starts at 160 Hz and falls toward 45 Hz. As in build_horn.py, a sine
    # wave needs its phase, which is the frequency added up over time; for
    # f(t) = 45 + 115 * e^(-t / 0.05) that adds up to the expression below.
    phase = 2 * math.pi * (45 * t + 115 * 0.05 * (1 - math.exp(-t / 0.05)))
    thump = math.sin(phase) * math.exp(-t / 0.12)
    # The crackle: noise that fades even faster than the thump.
    crackle = (noise.random() * 2 - 1) * math.exp(-t / 0.06)
    attack = min(1.0, t / 0.002)
    return attack * (1.2 * thump + 0.7 * crackle)


def star_detonate(t):
    # Sweeps from 600 Hz up to about 1800 Hz: f(t) = 600 + 1200 * (1 - e^(-t / 0.08)).
    lag = 0.08 * (1 - math.exp(-t / 0.08))
    phase = 2 * math.pi * (1800 * t - 1200 * lag)
    # The second tone is 0.7% sharp, so the two drift in and out of step (the shimmer).
    tone = math.sin(phase) + 0.6 * math.sin(phase * 1.007) + 0.25 * math.sin(phase * 2)
    fade = math.exp(-t / 0.13)
    attack = min(1.0, t / 0.004)
    return attack * tone * fade


def square(phase):
    """A square wave: +1 for the first half of every cycle, -1 for the second."""
    return 1.0 if (phase % 1.0) < 0.5 else -1.0


def hypercube_activate(t):
    # The crackle: hops between three notes every 30 ms for the first 0.24 s.
    notes = (440.0, 554.37, 659.25)   # A4, C#5, E5: an A major chord, one note at a time
    buzz = 0.0
    if t < 0.24:
        note = notes[int(t / 0.03) % len(notes)]
        buzz = 0.35 * square(note * t) * (1 - t / 0.24)
    # The ring: a bell on a high A as the last gem goes.
    return buzz + bell(t - 0.18, 880.0)


# (file name, function, length in seconds)
SOUNDS = (
    ("power_gem_created.wav", power_gem_created, 0.45),
    ("flame_detonate.wav", flame_detonate, 0.45),
    ("star_detonate.wav", star_detonate, 0.45),
    ("hypercube_activate.wav", hypercube_activate, 0.5),
)


def write(name, make_sample, duration):
    count = int(SAMPLE_RATE * duration)
    samples = [make_sample(i / SAMPLE_RATE) for i in range(count)]

    # A short fade over the last 20 ms, so a sound that's still ringing doesn't click when
    # it's cut off.
    tail = int(SAMPLE_RATE * 0.02)
    for i in range(tail):
        samples[count - 1 - i] *= i / tail

    # Normalize: scale everything so the loudest sample lands exactly on PEAK.
    loudest = max(abs(s) for s in samples)
    scale = PEAK / loudest

    target = OUTPUT / name
    with wave.open(str(target), "wb") as wav:
        wav.setnchannels(1)          # mono
        wav.setsampwidth(2)          # 16-bit samples
        wav.setframerate(SAMPLE_RATE)
        # "<h" packs each sample as a little-endian signed 16-bit integer (-32768..32767).
        wav.writeframes(b"".join(struct.pack("<h", int(s * scale * 32767)) for s in samples))
    print(f"Wrote {target} ({duration}s)")


def main():
    for name, make_sample, duration in SOUNDS:
        write(name, make_sample, duration)


if __name__ == "__main__":
    main()
