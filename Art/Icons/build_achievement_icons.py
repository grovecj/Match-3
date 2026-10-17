"""Draws the achievement icons (issue #71).

Each is a plain white shape on a transparent background, so the UI can tint it: gold once the
achievement is unlocked, white (and dimmed) while it's locked.

The file name is the icon's name. The server names an icon for each achievement ("rounds",
"score", "match", "chain", "flame", "star", "cube", "combo"), and Match3 > Build Achievements Menu pairs each name with the
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


def flame(draw):
    """A Flame gem's fire: a tall tongue of flame with a shorter one each side, hollow at its heart."""
    # Each tongue is a teardrop: a round bottom, with a triangle from its sides up to the tip.
    # The three overlap at the bottom into one fire.
    for left, right, bottom, tip_x, tip_y in ((30, 98, 122, 64, 4), (14, 62, 116, 18, 42), (66, 114, 116, 110, 42)):
        width = right - left
        draw.ellipse(s(left, bottom - width, right, bottom), fill=WHITE)
        middle = bottom - width / 2
        draw.polygon(points([(left + 1, middle), (tip_x, tip_y), (right - 1, middle)]), fill=WHITE)
    # A smaller flame cut out of the middle (drawn in transparent), so it reads as fire, not a drop.
    clear = (255, 255, 255, 0)
    draw.ellipse(s(48, 86, 80, 114), fill=clear)
    draw.polygon(points([(49, 100), (64, 62), (79, 100)]), fill=clear)


def sparkle(draw, cx, cy, reach, waist):
    """A four-pointed sparkle centred on (cx, cy): long points up, down, left and right."""
    corners = []
    for i in range(8):
        # Eight corners, alternating between a point and the narrow waist between two points.
        angle = -math.pi / 2 + i * math.pi / 4
        radius = reach if i % 2 == 0 else waist
        corners.append((cx + radius * math.cos(angle), cy + radius * math.sin(angle)))
    draw.polygon(points(corners), fill=WHITE)


def star(draw):
    """A Star gem's burst: a four-pointed sparkle, its points along the row and column it clears.

    Four points rather than five, so it doesn't look like the "score" star.
    """
    sparkle(draw, 64, 64, 58, 14)


def cube(draw):
    """A Hypercube: a cube seen from a corner, its three faces split by thin gaps."""
    cx, top, half, edge = 64, 10, 50, 56
    # The six corners of the outline, and the near corner in the middle. Each face is drawn on
    # its own, pulled in a little, which leaves a thin line between it and the next.
    mid_y = top + half * 0.58
    centre = (cx, top + half * 1.16)
    upper = [(cx, top), (cx + half, mid_y), centre, (cx - half, mid_y)]
    left = [(cx - half, mid_y), centre, (cx, centre[1] + edge), (cx - half, mid_y + edge)]
    right = [centre, (cx + half, mid_y), (cx + half, mid_y + edge), (cx, centre[1] + edge)]
    for face in (upper, left, right):
        fx = sum(x for x, _ in face) / 4
        fy = sum(y for _, y in face) / 4
        shrunk = [(fx + (x - fx) * 0.88, fy + (y - fy) * 0.88) for x, y in face]
        draw.polygon(points(shrunk), fill=WHITE)


def combo(draw):
    """Two gems swapped together, with a spark where they meet."""
    for cx in (34, 94):
        # Each gem is a diamond: a square stood on its corner.
        draw.polygon(points([(cx, 52), (cx + 28, 84), (cx, 116), (cx - 28, 84)]), fill=WHITE)
    sparkle(draw, 64, 32, 28, 7)


ICONS = {
    "trophy": trophy,
    "score": score,
    "match": match,
    "chain": chain,
    "rounds": rounds,
    "flame": flame,
    "star": star,
    "cube": cube,
    "combo": combo,
}


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
