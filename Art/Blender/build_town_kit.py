"""Team Diva town kit: detailed stylised buildings, tech props, targets and gates.

Run headless:
  Blender -b --factory-startup --python build_town_kit.py -- <fbx_out_dir> <blend_out_path>

Every asset is built from bevelled parts so edges catch light, gets box-projected UVs at
1 UV unit per metre (textures tile at real scale), and uses standard material names that the
Unity builder maps to textured URP materials. Assets face Blender -Y ("front"), stand on z = 0,
and are exported one FBX each. Layout is deterministic.
"""
import bpy, bmesh, math, os, random, sys
from mathutils import Matrix, Vector

argv = sys.argv[sys.argv.index('--') + 1:]
OUT, BLEND = argv[0], argv[1]
os.makedirs(OUT, exist_ok=True)

bpy.ops.wm.read_factory_settings(use_empty=True)
COLOURS = {}  # material name -> viewport colour, only for previewing in Blender


def rot(x=0, y=0, z=0):
    return (Matrix.Rotation(math.radians(z), 4, 'Z') @ Matrix.Rotation(math.radians(y), 4, 'Y')
            @ Matrix.Rotation(math.radians(x), 4, 'X'))


class Asset:
    """Accumulates parts into one bmesh with per-face material slots."""

    def __init__(self, name):
        self.name, self.bm, self.slots = name, bmesh.new(), []

    def _mat(self, faces, material):
        if material not in self.slots:
            self.slots.append(material)
        index = self.slots.index(material)
        for f in faces:
            f.material_index = index

    @staticmethod
    def _faces_of(verts):
        # New primitives are separate pieces, so the faces touching their verts are exactly their faces.
        # (Slicing bm.faces by count is unreliable: bmesh does not guarantee new faces come last.)
        return {f for v in verts for f in v.link_faces}

    def box(self, centre, size, material, r=None):
        m = Matrix.Translation(Vector(centre)) @ (r or Matrix()) @ Matrix.Diagonal((*size, 1))
        made = bmesh.ops.create_cube(self.bm, size=1, matrix=m)
        self._mat(self._faces_of(made['verts']), material)

    def cyl(self, centre, radius, depth, material, r=None, sides=16, radius2=None):
        m = Matrix.Translation(Vector(centre)) @ (r or Matrix())
        made = bmesh.ops.create_cone(self.bm, cap_ends=True, cap_tris=False, segments=sides, radius1=radius,
                                     radius2=radius if radius2 is None else radius2, depth=depth, matrix=m)
        self._mat(self._faces_of(made['verts']), material)

    def sphere(self, centre, radius, material, scale=(1, 1, 1), subdiv=2):
        m = Matrix.Translation(Vector(centre)) @ Matrix.Diagonal((*scale, 1))
        made = bmesh.ops.create_icosphere(self.bm, subdivisions=subdiv, radius=radius, matrix=m)
        self._mat(self._faces_of(made['verts']), material)

    def poly_prism(self, outline, y0, y1, material):
        """Extrude a polygon given in the XZ plane (front elevation) between y0 and y1 (depth)."""
        front = [self.bm.verts.new((x, y0, z)) for x, z in outline]
        back = [self.bm.verts.new((x, y1, z)) for x, z in outline]
        faces = [self.bm.faces.new(list(reversed(front))), self.bm.faces.new(back)]
        n = len(outline)
        for i in range(n):
            j = (i + 1) % n
            faces.append(self.bm.faces.new((front[i], front[j], back[j], back[i])))
        self._mat(faces, material)

    def torus_arc(self, centre, radius, tube, material, a0=0, a1=180, steps=36, tube_steps=10, plane_y=0):
        """Arc of a torus in the XZ plane (an arch seen from the front)."""
        rings, faces = [], []
        for i in range(steps + 1):
            a = math.radians(a0 + (a1 - a0) * i / steps)
            c = Vector((math.cos(a) * radius, plane_y, math.sin(a) * radius))
            out = Vector((math.cos(a), 0, math.sin(a)))
            ring = []
            for j in range(tube_steps):
                b = 2 * math.pi * j / tube_steps
                ring.append(self.bm.verts.new(Vector(centre) + c + (out * math.cos(b) + Vector((0, 1, 0)) * math.sin(b)) * tube))
            rings.append(ring)
        for i in range(steps):
            for j in range(tube_steps):
                k = (j + 1) % tube_steps
                faces.append(self.bm.faces.new((rings[i][j], rings[i][k], rings[i + 1][k], rings[i + 1][j])))
        if a1 - a0 < 360:  # a full ring is closed by remove_doubles; caps only for open arcs
            faces += [self.bm.faces.new(rings[0]), self.bm.faces.new(list(reversed(rings[-1])))]
        self._mat(faces, material)

    def text(self, body, centre, size, material, depth=.08, r=None):
        curve = bpy.data.curves.new('t', 'FONT')
        curve.body, curve.size, curve.extrude, curve.align_x, curve.align_y = body, size, depth, 'CENTER', 'CENTER'
        curve.bevel_depth = .01
        obj = bpy.data.objects.new('t', curve)
        bpy.context.scene.collection.objects.link(obj)
        depsgraph = bpy.context.evaluated_depsgraph_get()
        mesh = bpy.data.meshes.new_from_object(obj.evaluated_get(depsgraph))
        m = Matrix.Translation(Vector(centre)) @ (r or Matrix()) @ rot(x=90)  # text faces -Y
        mesh.transform(m)
        # from_mesh does not reliably append faces in order, so mark the existing ones instead of slicing.
        mark = self.bm.faces.layers.int.get('existing') or self.bm.faces.layers.int.new('existing')
        for f in self.bm.faces:
            f[mark] = 1
        self.bm.from_mesh(mesh)
        mark = self.bm.faces.layers.int.get('existing')
        self._mat([f for f in self.bm.faces if f[mark] == 0], material)
        bpy.data.objects.remove(obj); bpy.data.curves.remove(curve); bpy.data.meshes.remove(mesh)

    def finish(self, bevel=.025):
        bmesh.ops.remove_doubles(self.bm, verts=self.bm.verts, dist=1e-5)
        bmesh.ops.recalc_face_normals(self.bm, faces=self.bm.faces)  # outward, so Unity's back-face culling keeps them
        uv = self.bm.loops.layers.uv.new('UVMap')
        for f in self.bm.faces:  # box projection, 1 UV unit per metre
            n = f.normal
            ax = max(range(3), key=lambda i: abs(n[i]))
            for loop in f.loops:
                p = loop.vert.co
                loop[uv].uv = (p.y, p.z) if ax == 0 else (p.x, p.z) if ax == 1 else (p.x, p.y)
        mesh = bpy.data.meshes.new(self.name)
        self.bm.to_mesh(mesh); self.bm.free()
        obj = bpy.data.objects.new(self.name, mesh)
        bpy.context.scene.collection.objects.link(obj)
        for s in self.slots:
            mat = bpy.data.materials.get(s) or bpy.data.materials.new(s)
            mat.diffuse_color = COLOURS.get(s.split('_')[0], (.7, .7, .7, 1))
            mesh.materials.append(mat)
        if bevel:
            mod = obj.modifiers.new('Bevel', 'BEVEL')
            mod.width, mod.segments, mod.limit_method = bevel, 2, 'ANGLE'
            mod.angle_limit = math.radians(40)
            mod.harden_normals = True
        mesh.use_auto_smooth = True
        mesh.auto_smooth_angle = math.radians(35)
        return obj


# ---------- Building parts (facade on the y = y0 plane, facing -Y) ----------

def window(a, x, z, w, h, y0, wall, lit, shutters=None, arch=False):
    a.box((x, y0 + .02, z), (w, .06, h), lit)                                 # glass, slightly inset
    for dx in (-1, 1):
        a.box((x + dx * (w / 2 + .05), y0 - .05, z), (.1, .14, h + .2), 'Trim_Stone')
    a.box((x, y0 - .05, z + h / 2 + .06), (w + .2, .14, .12), 'Trim_Stone')
    a.box((x, y0 - .1, z - h / 2 - .06), (w + .35, .24, .1), 'Trim_Stone')   # sill
    a.box((x, y0 - .03, z), (.05, .06, h), 'Wood_Dark')                       # mullion
    a.box((x, y0 - .03, z + h * .15), (w, .06, .05), 'Wood_Dark')             # transom
    if arch:
        a.cyl((x, y0 - .02, z + h / 2), w / 2, .1, 'Trim_Stone', r=rot(x=90), sides=12)
    if shutters:
        for dx in (-1, 1):
            a.box((x + dx * (w / 2 + .32), y0 - .08, z), (.42, .06, h + .1), shutters)
            for k in range(4):
                a.box((x + dx * (w / 2 + .32), y0 - .12, z - h / 2 + .2 + k * h / 4), (.36, .03, .04), shutters)


def balcony(a, x, z, w, y0):
    a.box((x, y0 - .45, z), (w, .9, .14), 'Trim_Stone')
    for k in range(int(w / .22) + 1):
        a.cyl((x - w / 2 + .11 + k * .22, y0 - .85, z + .45), .025, .8, 'Metal_Dark', sides=6)
    a.box((x, y0 - .85, z + .87), (w, .06, .06), 'Metal_Dark')
    for dx in (-1, 1):
        a.box((x + dx * w / 2, y0 - .45, z + .87), (.06, .9, .06), 'Metal_Dark')


def awning(a, x, z, w, y0, material):
    a.box((x, y0 - .55, z), (w, 1.1, .06), material, r=rot(x=-18))
    for k in range(int(w / .5)):
        a.cyl((x - w / 2 + .25 + k * .5, y0 - 1.06, z - .26), .25, .05, material, r=rot(x=90), sides=10)
    a.box((x, y0 - .02, z + .2), (w + .1, .08, .08), 'Metal_Dark')


def shopfront(a, w, y0, sign_text, awning_mat, neon):
    a.box((0, y0 + .05, 1.5), (w - .6, .1, 2.4), 'Glass')                     # display glass
    for x in (-w / 2 + .3, w / 2 - .3):
        a.box((x, y0 - .08, 1.6), (.35, .2, 3.2), 'Trim_Stone')                # pilasters
    a.box((0, y0 - .1, .15), (w, .25, .3), 'Trim_Stone')                       # plinth
    for k in range(1, 4):
        a.box((-w / 2 + .3 + k * (w - .6) / 4, y0 - .02, 1.5), (.08, .1, 2.4), 'Metal_Dark')
    a.box((w / 2 - 1.3, y0 - .04, 1.15), (1.1, .1, 2.2), 'Wood_Door')          # door
    a.cyl((w / 2 - 1.0, y0 - .12, 1.1), .05, .1, 'Metal_Light', r=rot(x=90), sides=8)
    a.box((0, y0 - .14, 3.25), (w - .2, .2, .7), 'Sign_Board')                # fascia
    a.text(sign_text, (0, y0 - .26, 3.25), .5, neon)
    awning(a, 0, 2.95, w - .8, y0, awning_mat)


def gable_roof(a, w, d, z, pitch, material, wall, overhang=.35):
    h = (w / 2 + overhang) * math.tan(math.radians(pitch))
    a.poly_prism([(-w / 2, z), (w / 2, z), (0, z + h - .15)], -d / 2, d / 2, wall)
    half = w / 2 + overhang
    slope = math.hypot(half, h)
    for s in (-1, 1):
        r = rot(y=s * pitch)  # right slab (s = +1) falls towards +X
        a.box((s * half / 2, 0, z + h / 2 - .02), (slope + .05, d + overhang * 2, .16), material, r=r)
        rows = int(slope / .35)
        for k in range(rows):  # tile courses
            t = (k + .5) / rows
            a.box((s * (half - t * half), 0, z + t * h + .1), (.06, d + overhang * 2, .05), material, r=r)
    a.box((0, 0, z + h + .05), (.3, d + overhang * 2 + .05, .22), 'Roof_Ridge')
    return h


def chimney(a, x, y, z, h=1.6):
    a.box((x, y, z + h / 2), (.7, .7, h), 'Brick_Dark')
    a.box((x, y, z + h + .06), (.85, .85, .12), 'Trim_Stone')
    for dx in (-.15, .15):
        a.cyl((x + dx, y, z + h + .3), .1, .4, 'Metal_Dark', sides=8)


def antenna(a, x, y, z):
    a.cyl((x, y, z + 1.2), .03, 2.4, 'Metal_Dark', sides=6)
    for k in range(4):
        a.box((x, y, z + 1.4 + k * .28), (1.2 - k * .2, .03, .03), 'Metal_Dark')


def ac_unit(a, x, y, z, r=None):
    a.box((x, y, z), (.9, .6, .6), 'Metal_Light', r=r)
    a.cyl((x, y - .31, z), .22, .04, 'Metal_Dark', r=(r or Matrix()) @ rot(x=90), sides=14)


def dish(a, x, y, z):
    a.cyl((x, y, z + .3), .04, .6, 'Metal_Dark', sides=6)
    a.cyl((x, y - .1, z + .7), .45, .08, 'Metal_Light', r=rot(x=70), sides=18, radius2=.3)


def neon_strip(a, x, y, z, w, material, vertical=False):
    a.box((x, y, z), (.08, .08, w) if vertical else (w, .08, .08), material)


# ---------- Buildings ----------

def townhouse(name, w, d, floors, wall, roof, shutters, awning_mat, neon, sign, timber=False, seed=0):
    rnd = random.Random(seed)
    a = Asset(name)
    fh, ground = 2.9, 3.8
    top = ground + floors * fh
    y0 = -d / 2
    a.box((0, 0, ground / 2), (w, d, ground), 'Plaster_Stone')
    a.box((0, .05, ground + floors * fh / 2), (w, d - .1, floors * fh), wall)
    a.box((0, -.02, ground + .06), (w + .12, d + .12, .16), 'Trim_Stone')      # string course
    a.box((0, -.02, top + .1), (w + .3, d + .3, .22), 'Trim_Stone')            # cornice
    shopfront(a, w, y0, sign, awning_mat, neon)
    cols = max(2, int(w / 2.1))
    for f in range(floors):
        z = ground + f * fh + 1.45
        for c in range(cols):
            x = -w / 2 + (c + .5) * w / cols
            window(a, x, z, .95, 1.5, y0 + .05, wall, 'Glass_Lit' if rnd.random() < .35 else 'Glass',
                   shutters if f % 2 == 0 else None, arch=f == floors - 1 and not timber)
        if f == 0 and w > 6:
            balcony(a, 0, ground + .1, w * .5, y0)
        if timber:
            a.box((0, y0 - .02, ground + f * fh + .08), (w, .1, .16), 'Wood_Dark')
            for c in range(cols + 1):
                a.box((-w / 2 + c * w / cols, y0 - .02, ground + f * fh + fh / 2), (.16, .1, fh), 'Wood_Dark')
            for c in range(cols):
                x = -w / 2 + (c + .5) * w / cols
                for s in (-1, 1):
                    a.box((x + s * w / cols * .33, y0 - .02, ground + f * fh + .45), (.12, .08, .9), 'Wood_Dark', r=rot(y=s * 40))
    h = gable_roof(a, w, d, top + .2, 38 + rnd.random() * 10, roof, wall)
    chimney(a, w * .28, d * .15, top + .2 + h * .35)
    if rnd.random() < .7:
        antenna(a, -w * .2, d * .2, top + .2 + h * .6)
    ac_unit(a, -w / 2 - .3, 0, ground + fh * .6, r=rot(z=90))
    # A dormer on bigger roofs.
    if w > 6.5:
        a.box((0, -d * .25, top + .2 + h * .35 + .5), (1.4, 1.2, 1.3), wall)
        window(a, 0, top + .2 + h * .35 + .55, .7, .8, -d * .25 - .6, wall, 'Glass_Lit')
        a.box((0, -d * .25, top + .2 + h * .35 + 1.25), (1.7, 1.5, .14), roof, r=rot(x=12))
    return a.finish()


def tech_block(name, w, d, floors, panel, neon_a, neon_b, screen_text, seed=0):
    rnd = random.Random(seed)
    a = Asset(name)
    fh = 3.3
    top = floors * fh
    y0 = -d / 2
    a.box((0, 0, top / 2), (w, d, top), panel)
    a.box((0, 0, .3), (w + .2, d + .2, .6), 'Concrete')
    for f in range(1, floors):
        a.box((0, y0 - .08, f * fh), (w + .1, .2, .25), 'Metal_Dark')          # floor bands
        neon_strip(a, 0, y0 - .2, f * fh - .18, w - .4, neon_a if f % 2 else neon_b)
    cols = max(2, int(w / 2.6))
    for f in range(floors):
        for c in range(cols):
            x = -w / 2 + (c + .5) * w / cols
            a.box((x, y0 + .02, f * fh + 1.7), (w / cols - .5, .08, 2.1), 'Glass_Lit' if rnd.random() < .5 else 'Glass')
            a.box((x - (w / cols - .5) / 2 - .1, y0 - .06, f * fh + 1.7), (.18, .2, 2.3), 'Metal_Dark')
    # Big screen frame across the upper floors.
    sw, sh = w * .62, fh * 1.6
    a.box((0, y0 - .25, top - sh / 2 - .6), (sw + .4, .3, sh + .4), 'Metal_Dark')
    a.box((0, y0 - .42, top - sh / 2 - .6), (sw, .06, sh), 'Screen')
    a.text(screen_text, (0, y0 - .5, top - sh / 2 - .6), sh * .28, 'Letter_Glow')
    for s in (-1, 1):
        neon_strip(a, s * (sw / 2 + .3), y0 - .45, top - sh / 2 - .6, sh + .5, neon_b, vertical=True)
        neon_strip(a, s * (w / 2 + .05), y0 - .05, top / 2, top - .4, neon_a, vertical=True)
    # Rooftop: parapet, AC units, dishes, an antenna mast with a beacon.
    a.box((0, 0, top + .4), (w + .2, d + .2, .8), 'Metal_Dark')
    a.box((0, 0, top + .78), (w - .4, d - .4, .1), 'Concrete')
    for k in range(3):
        ac_unit(a, -w / 3 + k * w / 3, d / 4, top + 1.1)
    dish(a, w / 3, -d / 5, top + .8)
    a.cyl((-w / 3, -d / 5, top + 3), .08, 4.4, 'Metal_Dark', sides=8)
    a.sphere((-w / 3, -d / 5, top + 5.3), .22, neon_a)
    a.box((0, y0 - .1, 1.5), (3, .2, 3), 'Glass')                              # lobby
    a.box((0, y0 - .5, 3.2), (4, 1, .15), 'Metal_Light')                       # canopy
    neon_strip(a, 0, y0 - 1.0, 3.12, 3.9, neon_b)
    return a.finish(bevel=.02)


def clock_tower(name):
    a = Asset(name)
    a.box((0, 0, 4), (5.2, 5.2, 8), 'Brick_Red')
    a.box((0, 0, 8.15), (5.6, 5.6, .3), 'Trim_Stone')
    a.box((0, 0, 12.5), (4.4, 4.4, 8.4), 'Plaster_Cream')
    for f in range(3):  # half-timbered upper shaft
        z = 8.3 + f * 2.8
        for side in range(4):
            r = rot(z=side * 90)
            off = r @ Vector((0, -2.25, 0))
            a.box((off.x, off.y, z + .08), (4.4, .12, .16) if side % 2 == 0 else (.12, 4.4, .16), 'Wood_Dark')
            for k in range(5):
                p = r @ Vector((-2.2 + k * 1.1, -2.25, z + 1.4))
                a.box((p.x, p.y, p.z), (.14, .12, 2.8) if side % 2 == 0 else (.12, .14, 2.8), 'Wood_Dark')
    for side in range(4):  # clock faces
        r = rot(z=side * 90)
        c = r @ Vector((0, -2.32, 15.2))
        a.cyl((c.x, c.y, c.z), 1.15, .12, 'Trim_Stone', r=r @ rot(x=90), sides=32)
        c2 = r @ Vector((0, -2.4, 15.2))
        a.cyl((c2.x, c2.y, c2.z), 1.0, .06, 'Clock_Face', r=r @ rot(x=90), sides=32)
        for k in range(12):
            t = r @ Vector((math.cos(k * math.pi / 6) * .82, -2.45, 15.2 + math.sin(k * math.pi / 6) * .82))
            a.box((t.x, t.y, t.z), (.08, .04, .18), 'Metal_Dark', r=r @ rot(y=-k * 30 + 90))
        h1 = r @ Vector((.2, -2.48, 15.45)); h2 = r @ Vector((-.25, -2.48, 15.05))
        a.box((h1.x, h1.y, h1.z), (.07, .04, .6), 'Metal_Dark', r=r @ rot(y=-35))
        a.box((h2.x, h2.y, h2.z), (.07, .04, .8), 'Metal_Dark', r=r @ rot(y=50))
    a.box((0, 0, 16.9), (5.4, 5.4, .3), 'Trim_Stone')
    a.cyl((0, 0, 19.8), 3.6, 5.6, 'Roof_Slate', sides=4, radius2=.05, r=rot(z=45))
    a.cyl((0, 0, 23.2), .06, 1.6, 'Metal_Dark', sides=6)
    a.sphere((0, 0, 24.1), .25, 'Neon_Magenta')
    a.box((0, -2.65, 1.6), (1.6, .2, 3.2), 'Wood_Door')
    a.cyl((0, -2.66, 3.2), .8, .2, 'Wood_Door', r=rot(x=90), sides=16)
    return a.finish()


# ---------- Props ----------

def street_lamp(name):
    a = Asset(name)
    a.cyl((0, 0, .2), .22, .4, 'Metal_Dark', sides=10)
    a.cyl((0, 0, 2.6), .07, 4.8, 'Metal_Dark', sides=10)
    a.box((0, -.5, 4.95), (.08, 1.1, .08), 'Metal_Dark')
    a.box((0, -1.0, 4.8), (.5, .5, .2), 'Metal_Dark')
    a.box((0, -1.0, 4.66), (.42, .42, .06), 'Lamp_Glow')
    a.box((0, 0, 3.4), (.04, .04, .9), 'Neon_Cyan')                           # tech light strip
    return a.finish(bevel=.012)


def holo_billboard(name, text, neon):
    a = Asset(name)
    a.cyl((0, 0, 2.5), .14, 5, 'Metal_Dark', sides=10)
    a.box((0, 0, 6.2), (4.4, .3, 2.6), 'Metal_Dark')
    a.box((0, -.17, 6.2), (4.1, .04, 2.3), 'Screen')
    a.text(text, (0, -.22, 6.2), .7, 'Letter_Glow')
    for s in (-1, 1):
        neon_strip(a, s * 2.25, -.12, 6.2, 2.6, neon, vertical=True)
    neon_strip(a, 0, -.12, 7.55, 4.5, neon)
    return a.finish(bevel=.015)


def kiosk(name, text, awning_mat, neon):
    a = Asset(name)
    a.box((0, 0, .55), (2.6, 1.6, 1.1), 'Wood_Dark')
    a.box((0, -.1, 1.14), (2.8, 1.9, .08), 'Trim_Stone')
    for x in (-1.25, 1.25):
        a.cyl((x, .6, 1.8), .05, 1.4, 'Metal_Dark', sides=8)
    a.box((0, .15, 2.55), (3, 2.1, .1), awning_mat, r=rot(x=-10))
    a.box((0, .7, 2.0), (2.4, .06, .6), 'Sign_Board')
    a.text(text, (0, .66, 2.0), .3, neon)  # faces the front (-Y), where the customers are
    for k in range(3):
        a.box((-0.8 + k * .8, -.3, 1.3), (.5, .4, .25), 'Crate')
    return a.finish(bevel=.015)


def planter_tree(name, seed):
    rnd = random.Random(seed)
    a = Asset(name)
    a.box((0, 0, .35), (1.6, 1.6, .7), 'Concrete')
    a.box((0, 0, .72), (1.4, 1.4, .06), 'Soil')
    a.cyl((0, 0, 1.8), .14, 2.4, 'Bark', sides=8, radius2=.09)
    for k in range(6):  # foliage clusters
        ang = k * 60 + rnd.random() * 30
        rr = .55 + rnd.random() * .3
        a.sphere((math.cos(math.radians(ang)) * rr, math.sin(math.radians(ang)) * rr, 3.1 + rnd.random() * .5),
                 .7 + rnd.random() * .25, 'Foliage', scale=(1, 1, .8))
    a.sphere((0, 0, 3.7), .85, 'Foliage', scale=(1, 1, .85))
    return a.finish(bevel=0)


def crates(name):
    a = Asset(name)
    for i, (x, y, z, r) in enumerate([(0, 0, .5, 0), (1.05, .1, .5, 8), (.5, .05, 1.5, -6), (-1.0, -.2, .4, 15)]):
        s = .8 if i == 3 else 1
        a.box((x, y, z * s), (s, s, s), 'Crate', r=rot(z=r))
        for e in (-1, 1):
            a.box((x, y - s / 2 - .01, z * s + e * s * .4), (s * 1.02, .04, .1), 'Metal_Dark', r=rot(z=r))
    return a.finish(bevel=.02)


def barrier(name):
    a = Asset(name)
    a.poly_prism([(-1.6, 0), (1.6, 0), (1.6, .25), (1.2, .45), (1.2, .9), (-1.2, .9), (-1.2, .45), (-1.6, .25)], -.2, .2, 'Concrete')
    a.box((0, -.21, .7), (2.3, .02, .25), 'Hazard')
    a.box((0, .21, .7), (2.3, .02, .25), 'Hazard')
    return a.finish(bevel=.02)


def bench(name):
    a = Asset(name)
    for x in (-.9, .9):
        a.box((x, 0, .25), (.08, .5, .5), 'Metal_Dark')
    for k in range(3):
        a.box((0, -.18 + k * .18, .5), (2.1, .14, .05), 'Wood_Dark')
    for k in range(2):
        a.box((0, .28, .7 + k * .18), (2.1, .05, .12), 'Wood_Dark')
    return a.finish(bevel=.01)


def stone_rail(name):
    """Perimeter segment, 8 m: low stone wall with railing, posts and small lights."""
    a = Asset(name)
    a.box((0, 0, .3), (8, .7, .6), 'Plaster_Stone')
    a.box((0, 0, .64), (8.1, .8, .08), 'Trim_Stone')
    for k in range(5):
        x = -4 + k * 2
        a.box((x, 0, .95), (.3, .3, .65), 'Trim_Stone')
        a.box((x, 0, 1.32), (.36, .36, .1), 'Trim_Stone')
        a.box((x, -.16, 1.0), (.12, .03, .12), 'Neon_Cyan')
    for k in range(40):
        a.cyl((-3.9 + k * .2, 0, 1.0), .018, .6, 'Metal_Dark', sides=6)
    a.box((0, 0, 1.3), (8, .05, .05), 'Metal_Dark')
    return a.finish(bevel=.015)


def banner(name, text, length, cloth):
    a = Asset(name)
    for s in (-1, 1):
        a.cyl((s * length / 2, 0, 3.5), .07, 7, 'Metal_Dark', sides=8)
    steps = 8
    for i in range(steps):  # gently sagging cloth
        t0, t1 = i / steps, (i + 1) / steps
        x0, x1 = -length / 2 + .2 + t0 * (length - .4), -length / 2 + .2 + t1 * (length - .4)
        sag = lambda t: -math.sin(t * math.pi) * .35
        z0, z1 = 6.1 + sag(t0), 6.1 + sag(t1)
        ang = math.degrees(math.atan2(z1 - z0, x1 - x0))
        a.box(((x0 + x1) / 2, 0, (z0 + z1) / 2), (x1 - x0 + .02, .04, 1.1), cloth, r=rot(y=-ang))
    a.text(text, (0, -.06, 5.85), .62, 'Letter_Cream', depth=.03)
    a.box((0, 0, 6.6), (length, .02, .02), 'Metal_Dark')
    return a.finish(bevel=0)


def cable_pole(name):
    a = Asset(name)
    a.cyl((0, 0, 3.5), .1, 7, 'Wood_Dark', sides=8)
    a.box((0, 0, 6.6), (1.4, .1, .1), 'Wood_Dark')
    for x in (-.55, 0, .55):
        a.cyl((x, 0, 6.75), .05, .18, 'Trim_Stone', sides=8)
    a.box((0, -.2, 5.6), (.35, .25, .45), 'Metal_Light')
    return a.finish(bevel=.01)


# ---------- Game pieces ----------

def target_board(name):
    """Hinged at its base (z = 0); faces -Y. Rim and bull glow."""
    a = Asset(name)
    c = (0, 0, 1.0)
    a.cyl(c, .95, .1, 'Target_Rim', r=rot(x=90), sides=32)
    a.cyl((0, -.06, 1.0), .82, .04, 'Target_Face', r=rot(x=90), sides=32)
    a.cyl((0, -.09, 1.0), .6, .04, 'Target_Ring', r=rot(x=90), sides=32)
    a.cyl((0, -.12, 1.0), .42, .04, 'Target_Face', r=rot(x=90), sides=32)
    a.cyl((0, -.15, 1.0), .22, .04, 'Target_Bull', r=rot(x=90), sides=24)
    a.torus_arc((0, 0, 1.0), 1.06, .04, 'Neon_Target', a0=0, a1=360, steps=48, plane_y=-.06)   # neon halo ring
    a.torus_arc((0, 0, 1.0), .71, .02, 'Neon_Target', a0=0, a1=360, steps=40, plane_y=-.12)   # inner neon line
    for k in range(8):  # tech notches on the rim
        ang = k * 45
        p = rot(y=-ang) @ Vector((0, 0, .95))
        a.box((p.x, -.07, 1.0 + p.z), (.12, .08, .2), 'Metal_Dark', r=rot(y=-ang))
    a.box((0, .08, .5), (.12, .08, 1.0), 'Metal_Dark')
    return a.finish(bevel=.01)


def target_stand(name):
    a = Asset(name)
    a.cyl((0, 0, .06), .55, .12, 'Metal_Dark', sides=20)
    a.cyl((0, 0, .14), .45, .04, 'Hazard', sides=20)
    a.cyl((0, 0, .6), .08, .9, 'Metal_Light', sides=10)
    a.box((0, 0, 1.08), (.5, .2, .12), 'Metal_Dark')
    a.box((0, -.11, 1.08), (.4, .02, .04), 'Target_Rim')
    return a.finish(bevel=.01)


def drone(name):
    """Hovering target drone, centred on the origin, eye facing -Y."""
    a = Asset(name)
    a.sphere((0, 0, 0), .55, 'Drone_Shell', scale=(1, 1, .8), subdiv=3)
    a.cyl((0, 0, 0), .78, .1, 'Metal_Dark', sides=24)
    a.cyl((0, 0, 0), .74, .12, 'Target_Rim', sides=24, radius2=.74)
    a.cyl((0, -.5, .05), .2, .1, 'Metal_Dark', r=rot(x=90), sides=16)
    a.cyl((0, -.56, .05), .14, .06, 'Target_Bull', r=rot(x=90), sides=16)
    for s in (-1, 1):
        a.box((s * .9, 0, .1), (.5, .3, .05), 'Drone_Shell', r=rot(y=s * 15))
        a.cyl((s * 1.12, 0, .16), .2, .04, 'Metal_Light', sides=16)
    a.cyl((0, 0, -.55), .03, .3, 'Metal_Dark', sides=6)
    return a.finish(bevel=.01)


def neon_gate(name):
    """Arch over the path: tech pylons at x = +/-3.6 and a light-banded arch. Faces -Y."""
    a = Asset(name)
    for s in (-1, 1):
        x = s * 3.6
        a.box((x, 0, .2), (1.3, 1.3, .4), 'Concrete')
        a.box((x, 0, 1.9), (.9, .9, 3.2), 'Metal_Dark')
        a.box((x, -.46, 1.9), (.6, .04, 2.6), 'Panel_Grey')
        a.box((x - s * .46, 0, 1.9), (.04, .5, 2.8), 'Neon_Cyan')
        a.box((x, -.47, .7), (.9, .04, .25), 'Hazard')
        a.box((x, 0, 3.55), (1.1, 1.1, .1), 'Trim_Stone')
    a.torus_arc((0, 0, 3.6), 3.6, .32, 'Metal_Dark')
    for i, m in enumerate(('Neon_Magenta', 'Neon_Orange', 'Neon_Yellow', 'Neon_Cyan')):
        a.torus_arc((0, 0, 3.6), 3.15 - i * .16, .055, m, plane_y=-.18)
    a.box((0, -.05, 7.6), (2.4, .3, .8), 'Metal_Dark')
    a.box((0, -.21, 7.6), (2.2, .04, .62), 'Screen')
    a.text('DIVA', (0, -.27, 7.6), .45, 'Letter_Glow')
    return a.finish(bevel=.015)


def rocket(name):
    """Stand-in sky rocket (nose up, +Z in Blender), until the team imports a downloaded rocket."""
    a = Asset(name)
    a.cyl((0, 0, 3.6), 1.2, 5.2, 'Rocket_Body', sides=24)
    a.cyl((0, 0, 7.5), 1.2, 2.6, 'Rocket_Nose', sides=24, radius2=.06)
    for z in (1.4, 5.8):
        a.cyl((0, 0, z), 1.24, .35, 'Rocket_Stripe', sides=24)
    a.cyl((0, -1.17, 4.4), .5, .16, 'Trim_Stone', r=rot(x=90), sides=20)
    a.cyl((0, -1.25, 4.4), .38, .06, 'Glass_Lit', r=rot(x=90), sides=20)
    for k in range(4):  # fins
        r = rot(z=k * 90)
        p = r @ Vector((0, -1.55, 1.6))
        a.box((p.x, p.y, p.z), (.14, 1.1, 2.2), 'Rocket_Nose', r=r @ rot(x=-12))
    a.cyl((0, 0, .55), .9, 1.1, 'Metal_Light', sides=20, radius2=.65)  # engine bell
    a.cyl((0, 0, 9.0), .05, .9, 'Metal_Dark', sides=6)
    a.sphere((0, 0, 9.5), .18, 'Neon_Magenta')
    return a.finish(bevel=.02)


# ---------- Build everything ----------

ASSETS = [
    lambda: townhouse('House_A', 7, 7, 2, 'Plaster_Teal', 'Roof_Slate', 'Wood_Green', 'Awning_Red', 'Neon_Orange', 'PEANUT BAR', seed=1),
    lambda: townhouse('House_B', 6, 7, 3, 'Plaster_Orange', 'Roof_Slate', 'Wood_Blue', 'Awning_Teal', 'Neon_Cyan', 'TRUNK & CO', timber=True, seed=2),
    lambda: townhouse('House_C', 8, 8, 2, 'Brick_Red', 'Roof_Terracotta', 'Wood_Green', 'Awning_Yellow', 'Neon_Magenta', 'ARCADE', seed=3),
    lambda: townhouse('House_D', 6, 6, 3, 'Plaster_Mauve', 'Roof_Teal', 'Wood_Dark', 'Awning_Red', 'Neon_Yellow', 'NOODLES', timber=True, seed=4),
    lambda: townhouse('House_E', 7, 7, 4, 'Plaster_Cream', 'Roof_Slate', 'Wood_Blue', 'Awning_Teal', 'Neon_Magenta', 'HOTEL', seed=5),
    lambda: townhouse('House_F', 6.5, 6.5, 2, 'Plaster_Blue', 'Roof_Terracotta', 'Wood_Dark', 'Awning_Yellow', 'Neon_Cyan', 'CAFE', timber=True, seed=6),
    lambda: tech_block('Tech_A', 10, 9, 5, 'Panel_Navy', 'Neon_Cyan', 'Neon_Magenta', 'DIVA', seed=7),
    lambda: tech_block('Tech_B', 9, 8, 4, 'Panel_Grey', 'Neon_Magenta', 'Neon_Orange', 'SAFARI', seed=8),
    lambda: clock_tower('Clock_Tower'),
    lambda: street_lamp('Street_Lamp'),
    lambda: holo_billboard('Billboard_A', 'TARGET 100', 'Neon_Magenta'),
    lambda: holo_billboard('Billboard_B', 'GO GO GO', 'Neon_Cyan'),
    lambda: kiosk('Kiosk_A', 'SNACKS', 'Awning_Red', 'Neon_Yellow'),
    lambda: kiosk('Kiosk_B', 'JUICE', 'Awning_Teal', 'Neon_Magenta'),
    lambda: planter_tree('Planter_Tree', 9),
    lambda: crates('Crates'),
    lambda: barrier('Barrier'),
    lambda: bench('Bench'),
    lambda: stone_rail('Stone_Rail'),
    lambda: banner('Banner_Welcome', 'WELCOME', 9, 'Cloth_Purple'),
    lambda: banner('Banner_Safari', 'DIVA SAFARI', 9, 'Cloth_Teal'),
    lambda: cable_pole('Cable_Pole'),
    lambda: target_board('Target_Board'),
    lambda: target_stand('Target_Stand'),
    lambda: drone('Target_Drone'),
    lambda: neon_gate('Neon_Gate'),
    lambda: rocket('Rocket'),
]

objects = []
for i, make in enumerate(ASSETS):
    obj = make()
    obj.location.x = (i % 8) * 14
    obj.location.y = (i // 8) * 14
    objects.append(obj)

for obj in objects:
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    keep = obj.location.copy()
    obj.location = (0, 0, 0)
    bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, obj.name + '.fbx'), use_selection=True,
                             apply_unit_scale=True, apply_scale_options='FBX_SCALE_UNITS',
                             axis_forward='-Z', axis_up='Y', bake_space_transform=True,
                             use_mesh_modifiers=True, mesh_smooth_type='FACE', add_leaf_bones=False)
    obj.location = keep
    tris = sum(len(p.vertices) - 2 for p in obj.evaluated_get(bpy.context.evaluated_depsgraph_get()).data.polygons)
    print(f'EXPORTED {obj.name}: {tris} tris, {len(obj.data.materials)} materials')

bpy.ops.wm.save_as_mainfile(filepath=BLEND)
print('KIT_DONE', len(objects), 'assets')
