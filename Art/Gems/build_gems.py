"""
Builds the six token shapes in Blender, in one or more *styles*, saves each style's scene to
Art/Gems/<style>.blend, and renders every token to a transparent 256x256 PNG sprite.

Run from the repo root (Blender does not need to be open):

    blender --background --python Art/Gems/build_gems.py                  # every style
    blender --background --python Art/Gems/build_gems.py -- --style flat  # just one style

Blender ignores everything after the lone "--", so it's left in sys.argv for this script.

Re-running rebuilds everything from scratch, so tweak the numbers in SHAPES / STYLES / the
helpers below rather than hand-editing the .blend files (hand edits would be overwritten).

Styles
------
    gems   faceted, glossy, opaque cut gems    -> Assets/Sprites/Gems/Gem<Name>.png
    glass  smooth, rounded tiles of clear glass -> Assets/Sprites/Glass/Glass<Name>.png
    flat   solid 2D shapes with a dark border   -> Assets/Sprites/Flat/Flat<Name>.png

Every style uses the same six outlines (SHAPES), so a token is recognizable by its shape
alone, whatever the style and even without color (colorblind-friendly). A style only decides
how an outline is turned into a mesh and what material it gets.

How a faceted gem is built
--------------------------
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

import argparse
import math
import os
import sys
from dataclasses import dataclass
from typing import Callable

import bmesh
import bpy
from mathutils import Vector

# --- Paths ------------------------------------------------------------------------------------

# This file lives in <repo>/Art/Gems/, so the repo root is two folders up.
ART_DIR = os.path.dirname(os.path.abspath(__file__))
REPO_ROOT = os.path.dirname(os.path.dirname(ART_DIR))
SPRITES_DIR = os.path.join(REPO_ROOT, "Assets", "Sprites")

# --- Render settings --------------------------------------------------------------------------

SPRITE_SIZE = 256  # pixels; matches the sprites' Pixels Per Unit in Unity so one token = one cell
# Orthographic camera width in Blender units. Shapes have a radius of about 1, so 2.5 leaves a
# ~10% margin around each token (similar to the old placeholder sprites).
ORTHO_SCALE = 2.5
TOKEN_SPACING = 3.0  # tokens sit side by side in the .blend so you can see them all at once

# --- Shapes -----------------------------------------------------------------------------------


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


def cross(reach=0.95, arm_half_width=0.34):
    """A plus sign: four arms of equal length (12 points, CCW).

    Unlike the other outlines this one is *concave*: it has corners that point inward (where
    two arms meet). Everything built from an outline below still works, because every point of
    a cross can be "seen" from its center, and that's all the gem cut relies on when it shrinks
    rings toward the center and joins the girdle to a single bottom point.
    """
    r, a = reach, arm_half_width
    return [(r, -a), (r, a), (a, a), (a, r), (-a, r), (-a, a),
            (-r, a), (-r, -a), (-a, -a), (-a, -r), (a, -r), (a, -a)]


# Colors are the same hues the placeholder tints used (see Assets/Data/TokenTypes), a little
# deeper so the lighting has room to add highlights without washing out. Blender works in
# linear color, so these look lighter once converted to the sRGB of the final PNG.
#
# The circle's outline is None here because how round it is depends on the style (see
# Style.circle_sides): build_style fills it in.
SHAPES = [
    # id,        outline,                              color (linear RGB, 0..1)
    ("circle",   None,                                 (0.80, 0.03, 0.05)),   # ruby red
    ("square",   chamfered_square(),                   (0.03, 0.18, 0.85)),   # sapphire blue
    ("diamond",  [(0, 1.05), (-0.72, 0), (0, -1.05), (0.72, 0)], (0.03, 0.62, 0.10)),  # emerald green
    ("triangle", centered_triangle(),                  (0.95, 0.62, 0.02)),   # topaz yellow
    ("hexagon",  regular_polygon(6, rotation_deg=90),  (0.42, 0.05, 0.80)),   # amethyst purple
    ("cross",    cross(),                              (0.90, 0.07, 0.50)),   # tourmaline pink
]

# --- Style: gems (faceted cut) ----------------------------------------------------------------

# Shape of the cut. Heights are in Blender units; scales shrink the outline toward the center.
CROWN_RING_SCALE = 0.85   # how far in the K ring sits
CROWN_RING_HEIGHT = 0.30
TABLE_SCALE = 0.55        # size of the flat top relative to the outline
TABLE_HEIGHT = 0.50
PAVILION_DEPTH = 0.70     # how far below the girdle the bottom point is


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


def make_gem_materials(name, color):
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
    return [mat]


# --- Style: glass (smooth rounded tiles) ------------------------------------------------------

GLASS_THICKNESS = 0.55     # height of the tile before its edges are rounded off
GLASS_BEVEL_WIDTH = 0.45   # how far in from the outline the rounded edge reaches
GLASS_BEVEL_SEGMENTS = 12  # more segments = smoother curve on the edge
GLASS_BACKLIGHT_DEPTH = 0.6     # how far below the tiles the glowing backlight plate sits
GLASS_BACKLIGHT_STRENGTH = 1.6  # how bright it glows at its center
GLASS_GLOW_RADIUS = 1.3         # how far from the center the backlight's glow fades out
GLASS_GLARE_WIDTH = 0.12        # width of the glare streak's light
GLASS_GLARE_LENGTH = 0.9        # length of the glare streak
GLASS_GLARE_OFFSET = 0.3        # how far toward the upper left of the tile the streak sits
GLASS_GLARE_ENERGY = 150


def build_glass_object(name, outline):
    """A thick tile in the shape of the outline, with its top edges rounded off.

    Instead of building the curve ourselves we add a Bevel *modifier*: a non-destructive
    operation Blender applies to the mesh at render time. The mesh stays a simple extruded
    outline, and the rounding can be tweaked with the constants above.
    """
    bm = bmesh.new()
    bottom = bm.faces.new([bm.verts.new((x, y, 0.0)) for x, y in outline])
    # Extrude the outline upward into a prism (the new top face is a copy of the bottom one).
    extruded = bmesh.ops.extrude_face_region(bm, geom=[bottom])
    top_verts = [v for v in extruded["geom"] if isinstance(v, bmesh.types.BMVert)]
    bmesh.ops.translate(bm, verts=top_verts, vec=(0.0, 0.0, GLASS_THICKNESS))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)

    mesh = bpy.data.meshes.new(name)
    bm.to_mesh(mesh)
    bm.free()
    # Smooth shading blends normals across faces, so the rounded edge looks like one curve
    # instead of a row of flat strips.
    mesh.shade_smooth()

    obj = bpy.data.objects.new(name, mesh)
    bevel = obj.modifiers.new("Round edges", type="BEVEL")
    bevel.width = GLASS_BEVEL_WIDTH
    bevel.segments = GLASS_BEVEL_SEGMENTS
    bevel.limit_method = "NONE"      # round every edge
    bevel.use_clamp_overlap = True   # stop bevels on short edges (the triangle's tips) overlapping
    # With smooth shading, the big flat faces would otherwise bend their normals toward the
    # curve and look warped. "Harden normals" keeps flat faces flat and only the bevel curved.
    bevel.harden_normals = True
    return obj


def make_glass_materials(name, color):
    """Clear, tinted glass.

    A fully transmissive (see-through) surface: light passing through the tile is tinted by
    the Base Color, and the smooth rounded edges catch bright reflections. Whatever is behind
    the glass shows through it, so configure_glass_scene puts a glowing plate back there.
    """
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes["Principled BSDF"]
    bsdf.inputs["Base Color"].default_value = (*color, 1.0)
    bsdf.inputs["Transmission Weight"].default_value = 1.0
    bsdf.inputs["Roughness"].default_value = 0.05
    bsdf.inputs["IOR"].default_value = 1.45
    # A faint glow in the glass's own color keeps it from looking empty where it refracts
    # something dark.
    bsdf.inputs["Emission Color"].default_value = (*color, 1.0)
    bsdf.inputs["Emission Strength"].default_value = 0.15
    return [mat]


def configure_glass_scene(scene, rig):
    """Backlights the glass, like a stained-glass window.

    What you see through glass is whatever is behind it. Behind these tiles there's nothing
    but the dim world, so they would look dark. A glowing plate under the tile fixes that, and
    turning off its camera visibility means rays straight from the camera pass it by: the
    background of the sprite stays transparent, but light refracted through the glass hits it.
    """
    # Refracted rays bounce around inside the glass; give them enough bounces to get out.
    scene.cycles.transmission_bounces = 16
    scene.cycles.max_bounces = 16

    bpy.ops.mesh.primitive_plane_add(size=ORTHO_SCALE * 2, location=(0, 0, -GLASS_BACKLIGHT_DEPTH))
    plate = bpy.context.active_object
    plate.name = "Backlight"
    plate.parent = rig  # follows the camera from token to token
    plate.visible_camera = False
    plate.data.materials.append(make_backlight_material())

    # A long, thin light straight above the tile. The flat top of the glass mirrors it, which
    # shows up as a diagonal glare streak: the classic "this is glass" cue.
    glare_data = bpy.data.lights.new("Glare", type="AREA")
    glare_data.shape = "RECTANGLE"
    glare_data.size, glare_data.size_y = GLASS_GLARE_WIDTH, GLASS_GLARE_LENGTH
    glare_data.energy = GLASS_GLARE_ENERGY
    glare = bpy.data.objects.new("Glare", glare_data)
    glare.location = (-GLASS_GLARE_OFFSET, GLASS_GLARE_OFFSET, 5.0)  # up and to the left
    glare.rotation_euler = (0.0, 0.0, math.radians(-45))  # lying flat (facing down), diagonal
    glare.parent = rig
    glare.visible_camera = False  # seen in reflections, but not as a bar across the sprite
    scene.collection.objects.link(glare)


def make_backlight_material():
    """Glows brightest right under the tile's center and fades toward its edges, so the glass
    looks lit from within instead of evenly flat."""
    mat = bpy.data.materials.new("Backlight")
    mat.use_nodes = True
    nodes, links = mat.node_tree.nodes, mat.node_tree.links
    nodes.remove(nodes["Principled BSDF"])

    # "Object" coordinates are positions on the plate, with (0, 0) at its center (right under
    # the tile). Scaling them sets how far out the glow reaches before it has faded away.
    coords = nodes.new("ShaderNodeTexCoord")
    scale = nodes.new("ShaderNodeVectorMath")
    scale.operation = "SCALE"
    scale.inputs["Scale"].default_value = 1.0 / GLASS_GLOW_RADIUS
    # A spherical gradient is 1 at the center, falling to 0 at distance 1.
    gradient = nodes.new("ShaderNodeTexGradient")
    gradient.gradient_type = "SPHERICAL"
    # The color ramp turns that 0..1 into a color: dim at the edge, bright at the center.
    ramp = nodes.new("ShaderNodeValToRGB")
    ramp.color_ramp.elements[0].color = (0.25, 0.25, 0.25, 1.0)
    ramp.color_ramp.elements[1].color = (1.0, 1.0, 1.0, 1.0)
    emission = nodes.new("ShaderNodeEmission")
    emission.inputs["Strength"].default_value = GLASS_BACKLIGHT_STRENGTH

    links.new(coords.outputs["Object"], scale.inputs[0])
    links.new(scale.outputs["Vector"], gradient.inputs["Vector"])
    links.new(gradient.outputs["Fac"], ramp.inputs["Fac"])
    links.new(ramp.outputs["Color"], emission.inputs["Color"])
    links.new(emission.outputs["Emission"], nodes["Material Output"].inputs["Surface"])
    return mat


# --- Style: flat (2D shapes) ------------------------------------------------------------------

FLAT_BORDER = 0.13        # border width, in Blender units (the outline's radius is about 1)
FLAT_BORDER_DARKEN = 0.18  # border color = gem color scaled by this (so a dark shade of it)


def build_flat_mesh(name, outline):
    """A flat face in the shape of the outline, split into a border ring and an inner fill.

    Material slot 0 is the border and slot 1 the fill (see make_flat_materials).
    """
    bm = bmesh.new()
    face = bm.faces.new([bm.verts.new((x, y, 0.0)) for x, y in outline])
    # A new face's normal isn't calculated until asked for, and inset needs it to know which
    # way is "inward".
    face.normal_update()
    # Inset shrinks the face inward and fills the gap with a ring of new faces, which become
    # the border. The original face is left in the middle as the fill.
    ring = bmesh.ops.inset_individual(bm, faces=[face], thickness=FLAT_BORDER, depth=0.0)
    for border_face in ring["faces"]:
        border_face.material_index = 0
    face.material_index = 1

    mesh = bpy.data.meshes.new(name)
    bm.to_mesh(mesh)
    bm.free()
    return mesh


def make_emission_material(name, color, strength=1.0):
    """A material that just glows a fixed color: lights and shadows have no effect on it,
    so the sprite shows exactly this color (after the linear -> sRGB conversion)."""
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    nodes = mat.node_tree.nodes
    nodes.remove(nodes["Principled BSDF"])
    emission = nodes.new("ShaderNodeEmission")
    emission.inputs["Color"].default_value = (*color, 1.0)
    emission.inputs["Strength"].default_value = strength
    mat.node_tree.links.new(emission.outputs["Emission"], nodes["Material Output"].inputs["Surface"])
    return mat


def make_flat_materials(name, color):
    # The dark border keeps light tokens readable on the light board theme; the bright fill
    # keeps them readable on the dark ones.
    border = tuple(c * FLAT_BORDER_DARKEN for c in color)
    return [make_emission_material(f"{name}_border", border), make_emission_material(f"{name}_fill", color)]


def configure_flat_scene(scene, rig):
    # Unlit colors need no light paths at all, just enough samples to smooth the edges.
    scene.cycles.samples = 16
    scene.cycles.use_denoising = False
    # The darker exposure is there to tame the lit styles' highlights. Unlit colors have no
    # highlights, and darkening them would just make them dull.
    scene.view_settings.exposure = 0.0


# --- Styles -----------------------------------------------------------------------------------


@dataclass
class Style:
    """Everything that differs between token styles."""
    folder: str                    # Assets/Sprites/<folder>/, and Art/Gems/<folder>.blend
    file_prefix: str               # sprites are named <file_prefix><Shape>.png
    build_object: Callable         # (name, outline) -> a new, unlinked Blender object
    make_materials: Callable       # (name, color) -> list of materials, one per mesh slot
    configure_scene: Callable = lambda scene, rig: None  # extra setup after the default scene
    circle_sides: int = 12         # 12 reads as a cut stone; smooth styles want a real circle


def object_from_mesh(build_mesh):
    """Adapts a function that builds a mesh into one that builds an object holding it."""
    return lambda name, outline: bpy.data.objects.new(name, build_mesh(name, outline))


STYLES = {
    "gems": Style("Gems", "Gem", object_from_mesh(build_gem_mesh), make_gem_materials),
    "glass": Style("Glass", "Glass", build_glass_object, make_glass_materials, configure_glass_scene,
                   circle_sides=64),
    "flat": Style("Flat", "Flat", object_from_mesh(build_flat_mesh), make_flat_materials, configure_flat_scene,
                  circle_sides=64),
}

# --- Scene helpers ----------------------------------------------------------------------------


def reset_scene():
    """Start from an empty file so re-running the script always gives the same result."""
    bpy.ops.wm.read_factory_settings(use_empty=True)
    # Don't write <style>.blend1 backup files next to the .blend; git already keeps history.
    bpy.context.preferences.filepaths.save_version = 0


def add_area_light(name, location, energy, size, parent):
    light_data = bpy.data.lights.new(name, type="AREA")
    light_data.energy = energy
    light_data.size = size
    light = bpy.data.objects.new(name, light_data)
    light.location = location
    light.parent = parent
    # Aim the light at the token (the rig's origin).
    direction = Vector((0, 0, 0)) - Vector(location)
    light.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()
    bpy.context.scene.collection.objects.link(light)
    return light


def build_rig():
    """Camera + lights parented to one empty, so moving the empty re-frames any token."""
    scene = bpy.context.scene
    rig = bpy.data.objects.new("Rig", None)
    scene.collection.objects.link(rig)

    # Orthographic camera looking straight down: no perspective, so the sprite shows the
    # token's exact outline, like the flat placeholder shapes did.
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
    # the token and shows up in its reflections. A dim cool gray avoids pure-black facets.
    world = bpy.data.worlds.new("World")
    world.use_nodes = True
    background = world.node_tree.nodes["Background"]
    background.inputs["Color"].default_value = (0.35, 0.38, 0.45, 1.0)
    background.inputs["Strength"].default_value = 0.25
    scene.world = world
    return scene


# --- Main -------------------------------------------------------------------------------------


def build_style(style):
    reset_scene()
    scene = configure_render()
    rig = build_rig()
    style.configure_scene(scene, rig)

    tokens = []
    for index, (shape_id, outline, color) in enumerate(SHAPES):
        if shape_id == "circle":
            outline = regular_polygon(style.circle_sides)  # see the note above SHAPES
        name = f"{style.file_prefix}_{shape_id}"
        obj = style.build_object(name, outline)
        for material in style.make_materials(name, color):
            obj.data.materials.append(material)
        obj.location = (index * TOKEN_SPACING, 0, 0)
        scene.collection.objects.link(obj)
        tokens.append((shape_id, obj))

    # Save before rendering so the .blend exists even if a render fails.
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(ART_DIR, f"{style.folder.lower()}.blend"))

    sprite_dir = os.path.join(SPRITES_DIR, style.folder)
    os.makedirs(sprite_dir, exist_ok=True)
    for shape_id, obj in tokens:
        rig.location = obj.location  # the camera only sees this token; neighbors are out of frame
        scene.render.filepath = os.path.join(sprite_dir, f"{style.file_prefix}{shape_id.capitalize()}.png")
        bpy.ops.render.render(write_still=True)
        print(f"Rendered {scene.render.filepath}")

    rig.location = (0, 0, 0)
    bpy.ops.wm.save_mainfile()


def parse_args():
    # Blender's own arguments come before "--"; ours come after it.
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    parser = argparse.ArgumentParser(prog="build_gems.py")
    parser.add_argument("--style", choices=sorted(STYLES), help="build only this style (default: all)")
    return parser.parse_args(argv)


def main():
    args = parse_args()
    names = [args.style] if args.style else list(STYLES)
    for name in names:
        build_style(STYLES[name])


main()
