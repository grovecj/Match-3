"""Draws the achievement icons (issue #71).

Each is a plain white shape on a transparent background, so the UI can tint it: gold once the
achievement is unlocked, white (and dimmed) while it's locked.

The file name is the icon's name. The server names an icon for each achievement ("rounds",
"score", "match", "chain"), and Match3 > Build Achievements Menu pairs each name with the
sprite of the same name. "trophy" is the stand-in for a name the game has no picture for.

Run from the repo root (needs Pillow: pip install pillow), then run
Match3 > Build Achievements Menu in Unity so the AchievementIcons asset picks up any new file:

    python Art/Icons/build_achievement_icons.py
"""

import math
import os

from PIL import Image, ImageDraw

OUT_DIR = os.path.join("Assets", "Sprites", "Achievements")

SIZE = 128
# Draw at 4x and shrink: Pillow's shapes have hard, jagged edges, and scaling down smooths them.
SCALE = 4
WHITE = (255, 255, 255, 255)


def canvas():
    image = Image.new("RGBA", (SIZE * SCALE, SIZE * SCALE), (255, 255, 255, 0))
    return image, ImageDraw.Draw(image)


def s(*values):
    """Scales coordinates given on the 128-pixel grid up to the drawing size."""
    return [v * SCALE for v in values]


def points(pairs):
    return [(x * SCALE, y * SCALE) for x, y in pairs]


def trophy(draw):
    """A cup with two handles on a stem and a base."""
    for box in ((16, 22, 52, 62), (76, 22, 112, 62)):  # the handles, as rings behind the bowl
        draw.ellipse(s(*box), outline=WHITE, width=7 * SCALE)
    draw.rectangle(s(34, 16, 94, 44), fill=WHITE)  # the top of the bowl
    draw.pieslice(s(34, 8, 94, 80), 0, 180, fill=WHITE)  # its rounded bottom
    draw.rectangle(s(58, 76, 70, 98), fill=WHITE)  # stem
    draw.rounded_rectangle(s(38, 96, 90, 112), radius=5 * SCALE, fill=WHITE)  # base


def score(draw):
    """A five-pointed star."""
    cx, cy, outer, inner = 64, 68, 56, 23
    star = []
    for i in range(10):
        # Ten corners, alternating between the long and the short radius, starting straight up.
        angle = -math.pi / 2 + i * math.pi / 5
        radius = outer if i % 2 == 0 else inner
        star.append((cx + radius * math.cos(angle), cy + radius * math.sin(angle)))
    draw.polygon(points(star), fill=WHITE)


def match(draw):
    """Three tokens in a row."""
    for left in (8, 47, 86):
        draw.rounded_rectangle(s(left, 47, left + 34, 81), radius=8 * SCALE, fill=WHITE)


def chain(draw):
    """A lightning bolt: one thing setting off the next."""
    draw.polygon(points([(76, 6), (28, 72), (58, 72), (46, 122), (100, 52), (70, 52)]), fill=WHITE)


def rounds(draw):
    """A circular arrow: again and again."""
    cx, cy, radius, width = 64, 64, 44, 14
    # Pillow measures angles clockwise from 3 o'clock. Leave a gap at the top right.
    start, end = -20, 285
    box = (cx - radius - width / 2, cy - radius - width / 2, cx + radius + width / 2, cy + radius + width / 2)
    draw.arc(s(*box), start, end, fill=WHITE, width=width * SCALE)
    # The arrowhead sits where the arc ends, pointing the way the arc was going (clockwise).
    angle = math.radians(end)
    tip_x, tip_y = cx + radius * math.cos(angle), cy + radius * math.sin(angle)
    along = (-math.sin(angle), math.cos(angle))  # the direction of travel there
    across = (math.cos(angle), math.sin(angle))  # outward from the center
    head = [
        (tip_x + along[0] * 26, tip_y + along[1] * 26),
        (tip_x + across[0] * 20, tip_y + across[1] * 20),
        (tip_x - across[0] * 20, tip_y - across[1] * 20),
    ]
    draw.polygon(points(head), fill=WHITE)


ICONS = {"trophy": trophy, "score": score, "match": match, "chain": chain, "rounds": rounds}


def main():
    os.makedirs(OUT_DIR, exist_ok=True)
    for name, paint in ICONS.items():
        image, draw = canvas()
        paint(draw)
        image = image.resize((SIZE, SIZE), Image.LANCZOS)
        path = os.path.join(OUT_DIR, name + ".png")
        image.save(path)
        print("wrote", path)


if __name__ == "__main__":
    main()
