"""Renders the particle textures used by the clear explosion (issue #54).

Both are pure white with the shape in the alpha channel, so a ParticleSystem can tint each
particle any color (the white gets multiplied by the particle's color).

    Spark.png  a soft four-pointed sparkle: the shards of the burst
    Glow.png   a soft round glow: the white flash behind a 5+ burst

Run from the repo root (needs Pillow: pip install pillow):

    python Art/Effects/build_particles.py
"""

import math
import os

from PIL import Image

OUT_DIR = os.path.join("Assets", "Sprites", "Effects")


def render(size, alpha_at):
    """Builds a white RGBA image whose alpha at each pixel is alpha_at(x, y), with x and y
    running from -1 to 1 across the image (0, 0 is the center)."""
    image = Image.new("RGBA", (size, size))
    pixels = image.load()
    for py in range(size):
        for px in range(size):
            # Sample the middle of the pixel, mapped to -1..1.
            x = (px + 0.5) / size * 2 - 1
            y = (py + 0.5) / size * 2 - 1
            alpha = max(0.0, min(1.0, alpha_at(x, y)))
            pixels[px, py] = (255, 255, 255, round(alpha * 255))
    return image


def spark(x, y):
    """A bright round core plus four thin rays along the axes, all fading to nothing at the edge."""
    r = math.hypot(x, y)
    core = max(0.0, 1 - r / 0.35) ** 1.5
    # A ray is bright near its axis (small |x| or |y|) and fades along its length.
    ray_h = max(0.0, 1 - abs(y) / 0.09) * max(0.0, 1 - abs(x))
    ray_v = max(0.0, 1 - abs(x) / 0.09) * max(0.0, 1 - abs(y))
    return core + ray_h + ray_v


def glow(x, y):
    """Bright in the middle, smoothly fading out to fully transparent at the edge."""
    r = min(1.0, math.hypot(x, y))
    return (1 - r) ** 2


def main():
    os.makedirs(OUT_DIR, exist_ok=True)
    render(64, spark).save(os.path.join(OUT_DIR, "Spark.png"))
    render(128, glow).save(os.path.join(OUT_DIR, "Glow.png"))
    print(f"Wrote Spark.png and Glow.png to {OUT_DIR}")


if __name__ == "__main__":
    main()
