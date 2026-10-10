"""Draws small UI icons that aren't achievement icons (issue #53).

So far that's one: the "play" triangle on each sound set's preview button in Settings. Like the
achievement icons it's a plain white shape on a transparent background, so the UI can tint it.

Run from the repo root (needs Pillow: pip install pillow):

    python Art/Icons/build_ui_icons.py
"""

import os

from PIL import Image, ImageDraw

OUT_DIR = os.path.join("Assets", "Sprites", "UI")

SIZE = 128
# Draw at 4x and shrink: Pillow's shapes have hard, jagged edges, and scaling down smooths them.
SCALE = 4
WHITE = (255, 255, 255, 255)


def play(draw):
    """A triangle pointing right, with slightly rounded corners."""
    corners = [(34, 16), (34, 112), (112, 64)]
    scaled = [(x * SCALE, y * SCALE) for x, y in corners]
    draw.polygon(scaled, fill=WHITE)
    # Drawing the outline again as a thick line with round joints rounds off the sharp points.
    draw.line(scaled + scaled[:2], fill=WHITE, width=12 * SCALE, joint="curve")


ICONS = {"Play": play}


def main():
    os.makedirs(OUT_DIR, exist_ok=True)
    for name, draw_icon in ICONS.items():
        image = Image.new("RGBA", (SIZE * SCALE, SIZE * SCALE), (255, 255, 255, 0))
        draw_icon(ImageDraw.Draw(image))
        path = os.path.join(OUT_DIR, f"{name}.png")
        image.resize((SIZE, SIZE), Image.LANCZOS).save(path)
        print(f"Wrote {path}")


if __name__ == "__main__":
    main()
