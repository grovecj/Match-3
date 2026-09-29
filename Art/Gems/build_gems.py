"""
Builds the five token gems in Blender, saves them to gems.blend, and renders each one to a
transparent 256x256 PNG sprite in Assets/Sprites/Gems/.

Run from the repo root (Blender does not need to be open):

    blender --background --python Art/Gems/build_gems.py

Re-running rebuilds everything from scratch, so tweak the numbers in GEMS / the helpers below
rather than hand-editing gems.blend (hand edits would be overwritten).

How a gem is built
------------------
Every gem starts from a flat 2D outline (circle, square, ...) and is lifted into a classic
"cut" shape. Seen from the side:

        ___T___          T = table: flat top face, a shrunken copy of the outline
       /   K   \         K = crown ring: points above each edge's midpoint, part way up
      G---------G        G = girdle: the outline itself, the widest part of the gem
       \       /
        \     /          pavilion: faces sloping down to a single point (the culet)
         \   /
           C

The triangles between the rings all tilt at slightly different angles. With flat shading each
triangle reflects the lights differently, which is what makes it read as "faceted" in the sprite.
"""

import math
import os

import bmesh
import bpy
from mathutils import Vector

# --- Paths ------------------------------------------------------------------------------------

# This file lives in <repo>/Art/Gems/, so the repo root is two folders up.
ART_DIR = os.path.dirname(os.path.abspath(__file__))
REPO_ROOT = os.path.dirname(os.path.dirname(ART_DIR))
BLEND_PATH = os.path.join(ART_DIR, "gems.blend")
SPRITE_DIR = os.path.join(REPO_ROOT, "Assets", "Sprites", "Gems")

# --- Render settings --------------------------------------------------------------------------

SPRITE_SIZE = 256  # pixels; matches the sprites' Pixels Per Unit in Unity so one gem = one cell
# Orthographic camera width in Blender units. Gems have a radius of about 1, so 2.5 leaves a
# ~10% margin around each gem (similar to the old placeholder sprites).
ORTHO_SCALE = 2.5
GEM_SPACING = 3.0  # gems sit side by side in the .blend so you can see them all at once

# --- Gem definitions --------------------------------------------------------------------------


def regular_polygon(sides, radius=1.0, rotation_deg=90.0):
    """Points of a regular polygon, counter-clockwise, first point at rotation_deg."""
    points = []
    for i in range(sides):
        angle = math.radians(rotation_deg + 360.0 * i / sides)
        points.append((radius * math.cos(angle), radius * math.sin(angle)))
    return points


def chamfered_square(half_size=0.8, chamfer=0.22):
    """A square with its corners cut off, like an emerald-cut stone (8 points, CCW)."""
    s, c = half_size, half_size - chamfer
    return [(s, -c), (s, c), (c, s), (-c, s), (-s, c), (-s, -c), (-c, -s), (c, -s)]


def centered_triangle():
    """An upward-pointing triangle, shifted down so it looks visually centered in its cell."""
    return [(x, y - 0.12) for x, y in regular_polygon(3, radius=1.05)]


# Colors are the same hues the placeholder tints used (see Assets/Data/TokenTypes), a little
# deeper so the lighting has room to add highlights without washing out.
GEMS = [
    # id,        outline,                              color (linear RGB-ish, 0..1)
    ("circle",   regular_polygon(12, rotation_deg=90), (0.80, 0.03, 0.05)),   # ruby red
    ("square",   chamfered_square(),                   (0.03, 0.18, 0.85)),   # sapphire blue
    ("diamond",  [(0, 1.05), (-0.72, 0), (0, -1.05), (0.72, 0)], (0.03, 0.62, 0.10)),  # emerald green
    ("triangle", centered_triangle(),                  (0.95, 0.62, 0.02)),   # topaz yellow
    ("hexagon",  regular_polygon(6, rotation_deg=90),  (0.42, 0.05, 0.80)),   # amethyst purple
]

# Shape of the cut. Heights are in Blender units; scales shrink the outline toward the center.
CROWN_RING_SCALE = 0.85   # how far in the K ring sits
CROWN_RING_HEIGHT = 0.30
TABLE_SCALE = 0.55        # size of the flat top relative to the outline
TABLE_HEIGHT = 0.50
PAVILION_DEPTH = 0.70     # how far below the girdle the bottom point is


# --- Scene helpers ----------------------------------------------------------------------------


def reset_scene():
    """Start from an empty file so re-running the script always gives the same result."""
    bpy.ops.wm.read_factory_settings(use_empty=True)
    # Don't write gems.blend1 backup files next to the .blend; git already keeps history.
    bpy.context.preferences.filepaths.save_version = 0


def build_gem_mesh(name, outline):
    """Create a closed, faceted gem mesh from a counter-clockwise 2D outline."""
    bm = bmesh.new()
    n = len(outline)
    corners = [Vector((x, y, 0.0)) for x, y in outline]

    # G: girdle ring (the outline, at z = 0).
    girdle = [bm.verts.new(p) for p in corners]

    # K: crown ring, one point above the midpoint of each outline edge.
    crown = []
    for i in range(n):
        mid = (corners[i] + corners[(i + 1) % n]) / 2
        crown.append(bm.verts.new((mid.x * CROWN_RING_SCALE, mid.y * CROWN_RING_SCALE, CROWN_RING_HEIGHT)))

    # T: table ring, one point above each outline corner.
    table = [bm.verts.new((p.x * TABLE_SCALE, p.y * TABLE_SCALE, TABLE_HEIGHT)) for p in corners]

    # C: culet, the single point at the bottom.
    culet = bm.verts.new((0.0, 0.0, -PAVILION_DEPTH))

    for i in range(n):
        j = (i + 1) % n
        # Girdle -> crown ring: one triangle along each edge, one "star" triangle at each corner.
        bm.faces.new((girdle[i], girdle[j], crown[i]))
        bm.faces.new((girdle[j], crown[j], crown[i]))
        # Crown ring -> table: alternating triangles, again giving two facet angles per edge.
        bm.faces.new((crown[i], table[j], table[i]))
        bm.faces.new((crown[i], crown[j], table[j]))
        # Pavilion: girdle edge down to the culet.
        bm.faces.new((girdle[j], girdle[i], culet))

    bm.faces.new(table)  # the flat top

    # Make every face point outward, whatever order we listed the vertices in above.
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)

    mesh = bpy.data.meshes.new(name)
    bm.to_mesh(mesh)
    bm.free()
    # Flat shading (the default for new meshes) keeps hard edges between facets.
    return mesh


def make_gem_material(name, color):
    """A glossy, opaque colored material.

    Real gems get their look from light refracting *through* them, but that renders poorly
    against a transparent background (the gem would mostly show the emptiness behind it).
    An opaque glossy surface plus a clear coat reads well at small sizes and is cheap to render.
    """
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes["Principled BSDF"]
    bsdf.inputs["Base Color"].default_value = (*color, 1.0)
    bsdf.inputs["Roughness"].default_value = 0.12
    bsdf.inputs["Coat Weight"].default_value = 0.35
    bsdf.inputs["Coat Roughness"].default_value = 0.03
    # A little emission in the gem's own color keeps shadowed facets from going muddy/black.
    bsdf.inputs["Emission Color"].default_value = (*color, 1.0)
    bsdf.inputs["Emission Strength"].default_value = 0.05
    return mat


def add_area_light(name, location, energy, size, parent):
    light_data = bpy.data.lights.new(name, type="AREA")
    light_data.energy = energy
    light_data.size = size
    light = bpy.data.objects.new(name, light_data)
    light.location = location
    light.parent = parent
    # Aim the light at the gem (the rig's origin).
    direction = Vector((0, 0, 0)) - Vector(location)
    light.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()
    bpy.context.scene.collection.objects.link(light)
    return light


def build_rig():
    """Camera + lights parented to one empty, so moving the empty re-frames any gem."""
    scene = bpy.context.scene
    rig = bpy.data.objects.new("Rig", None)
    scene.collection.objects.link(rig)

    # Orthographic camera looking straight down: no perspective, so the sprite shows the
    # gem's exact outline, like the flat placeholder shapes did.
    cam_data = bpy.data.cameras.new("Camera")
    cam_data.type = "ORTHO"
    cam_data.ortho_scale = ORTHO_SCALE
    cam = bpy.data.objects.new("Camera", cam_data)
    cam.location = (0, 0, 10)
    cam.parent = rig
    scene.collection.objects.link(cam)
    scene.camera = cam

    # Classic three-point lighting: a strong key light from the upper left (the usual UI
    # convention), a softer fill from the right, and a rim light from below to catch edges.
    add_area_light("Key", (-3, 3, 5), energy=700, size=1.5, parent=rig)
    add_area_light("Fill", (4, 1, 3), energy=80, size=3, parent=rig)
    add_area_light("Rim", (1, -4, 2), energy=150, size=1, parent=rig)
    return rig


def configure_render():
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"  # path tracer: accurate reflections, fine for tiny images
    scene.cycles.device = "CPU"      # works on any machine; 256px renders take seconds
    scene.cycles.samples = 128
    scene.cycles.use_denoising = True

    scene.render.resolution_x = SPRITE_SIZE
    scene.render.resolution_y = SPRITE_SIZE
    scene.render.resolution_percentage = 100
    scene.render.film_transparent = True  # background pixels get alpha = 0
    scene.render.image_settings.file_format = "PNG"
    scene.render.image_settings.color_mode = "RGBA"

    # "Standard" maps colors 1:1; Blender's default (AgX) desaturates bright colors, which
    # would make the gems look pastel.
    scene.view_settings.view_transform = "Standard"
    scene.view_settings.exposure = -0.6  # darken slightly so highlights don't wash out the color

    # The world (environment) is invisible because of film_transparent, but it still lights
    # the gem and shows up in its reflections. A dim cool gray avoids pure-black facets.
    world = bpy.data.worlds.new("World")
    world.use_nodes = True
    background = world.node_tree.nodes["Background"]
    background.inputs["Color"].default_value = (0.35, 0.38, 0.45, 1.0)
    background.inputs["Strength"].default_value = 0.25
    scene.world = world


# --- Main -------------------------------------------------------------------------------------


def main():
    reset_scene()
    configure_render()
    rig = build_rig()

    gems = []
    for index, (gem_id, outline, color) in enumerate(GEMS):
        obj = bpy.data.objects.new(f"Gem_{gem_id}", build_gem_mesh(f"Gem_{gem_id}", outline))
        obj.data.materials.append(make_gem_material(f"Gem_{gem_id}", color))
        obj.location = (index * GEM_SPACING, 0, 0)
        bpy.context.scene.collection.objects.link(obj)
        gems.append((gem_id, obj))

    # Save before rendering so the .blend exists even if a render fails.
    bpy.ops.wm.save_as_mainfile(filepath=BLEND_PATH)

    os.makedirs(SPRITE_DIR, exist_ok=True)
    for gem_id, obj in gems:
        rig.location = obj.location  # the camera only sees this gem; neighbors are out of frame
        bpy.context.scene.render.filepath = os.path.join(SPRITE_DIR, f"Gem{gem_id.capitalize()}.png")
        bpy.ops.render.render(write_still=True)
        print(f"Rendered {bpy.context.scene.render.filepath}")

    rig.location = (0, 0, 0)
    bpy.ops.wm.save_mainfile()


main()
