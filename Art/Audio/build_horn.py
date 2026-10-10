"""Generates the round-end horn for the Chimes sound set.

Kenney's Interface Sounds pack (where the other Chimes clips come from) has no horn, so this
one is synthesized: no recording, just maths. Needs only Python's standard library.

    python Art/Audio/build_horn.py    # writes Assets/Audio/Chimes/horn.wav

How it gets its sound:
  - Two notes a minor third apart played together, like a two-trumpet air horn. One note on
    its own sounds like a test tone; two that clash slightly sound like a warning.
  - Each note is a stack of harmonics (2x, 3x, 4x... its frequency), each quieter than the
    last. A plain sine wave is a soft "ooo"; the harmonics add the brassy buzz.
  - A fast attack and a short fade-out (the "envelope") so it starts with a punch and doesn't
    click when it stops.
"""

import math
import struct
import wave
from pathlib import Path

SAMPLE_RATE = 44100          # samples per second (CD quality)
DURATION = 1.2               # seconds
NOTES = (311.13, 369.99)     # E-flat 4 and G-flat 4, in Hz
HARMONICS = 14               # how many overtones to stack on each note
ATTACK = 0.02                # seconds to reach full volume
RELEASE = 0.18               # seconds to fade out at the end
SLIDE_DEPTH = 0.03           # how flat each note starts, as a fraction of its frequency
SLIDE_TIME = 0.06            # seconds for most of the slide up to the note
PEAK = 0.9                   # loudest sample, where 1.0 is the most a WAV can hold

OUTPUT = Path(__file__).resolve().parents[2] / "Assets" / "Audio" / "Chimes" / "horn.wav"


def envelope(t):
    """Volume (0-1) at time t: quick rise, hold, then fade to silence."""
    if t < ATTACK:
        return t / ATTACK
    if t > DURATION - RELEASE:
        return max(0.0, (DURATION - t) / RELEASE)
    return 1.0


def sample(t):
    """The horn's raw (not yet normalized) value at time t."""
    value = 0.0
    for note in NOTES:
        # The pitch starts 3% flat and slides up to the note over the first 60 ms or so, the
        # way a real horn "speaks": frequency = note * (1 - 0.03 * e^(-t / 0.06)).
        # A sine wave needs the phase, which is the frequency added up (integrated) over time.
        # Integrating that formula from 0 to t gives t minus the `lag` below.
        lag = SLIDE_DEPTH * SLIDE_TIME * (1 - math.exp(-t / SLIDE_TIME))
        phase = 2 * math.pi * note * (t - lag)
        for n in range(1, HARMONICS + 1):
            value += math.sin(n * phase) / n
    return value * envelope(t)


def main():
    count = int(SAMPLE_RATE * DURATION)
    samples = [sample(i / SAMPLE_RATE) for i in range(count)]

    # Normalize: scale everything so the loudest sample lands exactly on PEAK.
    loudest = max(abs(s) for s in samples)
    scale = PEAK / loudest

    with wave.open(str(OUTPUT), "wb") as wav:
        wav.setnchannels(1)          # mono
        wav.setsampwidth(2)          # 16-bit samples
        wav.setframerate(SAMPLE_RATE)
        # "<h" packs each sample as a little-endian signed 16-bit integer (-32768..32767).
        wav.writeframes(b"".join(struct.pack("<h", int(s * scale * 32767)) for s in samples))

    print(f"Wrote {OUTPUT} ({DURATION}s, {count} samples)")


if __name__ == "__main__":
    main()
