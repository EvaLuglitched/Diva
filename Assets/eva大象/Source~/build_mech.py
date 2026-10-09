"""Build a cute D.Va-style mech suit around the DigiPhant elephant (rest pose) and render previews.

Blender rest-pose space: +z up (feet ~2.44), -y forward (trunk), +x = elephant's own left.
Every armour object gets a custom property 'bone' naming the elephant bone it should follow in Unity.
Usage: blender -b --python build_mech.py -- elephant.fbx Elephant_D.png Elephant_N.png font.ttf outdir [views]
"""
import bpy, bmesh, sys, math
import numpy as np
from mathutils import Vector, Matrix

argv = sys.argv[sys.argv.index('--') + 1:]
FBX, TEX_D, TEX_N, FONT, OUT = argv[:5]
VIEWS = argv[5].split(',') if len(argv) > 5 else ['hero', 'front', 'side', 'back']

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=FBX)
sc = bpy.context.scene
ele = next(o for o in sc.objects if o.type == 'MESH')
for m in ele.modifiers:
    m.show_viewport = m.show_render = False  # rest pose; the imported armature does not line up with the mesh
for o in sc.objects:
    if o.type != 'MESH':
        o.hide_render = True
        o.hide_viewport = True

# ---------------------------------------------------------------- materials
def lin(h):
    c = [int(h[i:i + 2], 16) / 255 for i in (1, 3, 5)]
    return tuple(((x + .055) / 1.055) ** 2.4 if x > .04045 else x / 12.92 for x in c) + (1.0,)

MATS = {}
def mat(name, col, rough=.35, metal=0., coat=0., emit=None, estr=0., alpha=1.):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    b = m.node_tree.nodes['Principled BSDF']
    b.inputs['Base Color'].default_value = lin(col)
    b.inputs['Roughness'].default_value = rough
    b.inputs['Metallic'].default_value = metal
    if coat:
        b.inputs['Coat Weight'].default_value = coat
        b.inputs['Coat Roughness'].default_value = .15
    if emit:
        b.inputs['Emission Color'].default_value = lin(emit)
        b.inputs['Emission Strength'].default_value = estr
    if alpha < 1:
        b.inputs['Alpha'].default_value = alpha
        m.blend_method = 'BLEND'
        m.shadow_method = 'HASHED'
        m.show_transparent_back = False
    m['unity'] = dict(color=col, rough=rough, metal=metal, emit=emit or '', estr=estr, alpha=alpha)
    MATS[name] = m
    return m

PINK = mat('Mech Pink', '#EE7CC0', .32, coat=.6)
WHITE = mat('Mech White', '#F4EBDD', .38, coat=.3)
PLUM = mat('Mech Plum', '#4A3A5E', .45)
TEAL = mat('Mech Glow Teal', '#45F0C8', .3, emit='#45F0C8', estr=6.)
YELLOW = mat('Mech Yellow', '#F7B53E', .32, coat=.4)
GLASS = mat('Mech Glass', '#2FB89B', .05, alpha=.55)
GUN = mat('Mech Gunmetal', '#8C919E', .32, metal=.85)
WELL = mat('Mech Cockpit Well', '#1D4F55', .5)
CORE = mat('Mech Glow Core', '#E8FFFB', .3, emit='#BFFFF4', estr=10.)
STICKER_PINK = mat('Mech Sticker Pink', '#F5A9C8', .4)
CREAM = mat('Mech Cream', '#F1E3CB', .45)
ORANGE = mat('Mech Sticker Orange', '#FF9A3C', .4)
SEAM = mat('Mech Seam', '#5B3B6E', .6)
LAVENDER = mat('Mech Lavender', '#9B7BC6', .45)
MAGENTA = mat('Mech Magenta', '#D23C93', .4)
MINT = mat('Mech Sticker Mint', '#7EE6A8', .45)
DARK_GREEN = mat('Mech Sticker Ink', '#1F3A2E', .5)

em = bpy.data.materials.new('Elephant')
em.use_nodes = True
nt = em.node_tree
bsdf = nt.nodes['Principled BSDF']
td = nt.nodes.new('ShaderNodeTexImage'); td.image = bpy.data.images.load(TEX_D)
nt.links.new(td.outputs['Color'], bsdf.inputs['Base Color'])
tn = nt.nodes.new('ShaderNodeTexImage'); tn.image = bpy.data.images.load(TEX_N); tn.image.colorspace_settings.name = 'Non-Color'
nm = nt.nodes.new('ShaderNodeNormalMap')
nt.links.new(tn.outputs['Color'], nm.inputs['Color']); nt.links.new(nm.outputs['Normal'], bsdf.inputs['Normal'])
bsdf.inputs['Roughness'].default_value = .75
ele.data.materials.clear(); ele.data.materials.append(em)

# ---------------------------------------------------------------- mesh helpers
ARMOR = []
EMITTERS = []  # (name, bone, position, direction) in rest-pose space; Unity puts effects (water, bubbles, flames) here
LOW_POLY = True  # game version: no subdivision, fewer segments (~25k triangles total)
def obj(name, verts, faces, mats, bone, mat_idx=None, smooth=True, subsurf=0, solid=0., solid_off=1., inner=None):
    me = bpy.data.meshes.new(name)
    me.from_pydata([tuple(map(float, v)) for v in verts], [], [tuple(int(i) for i in f) for f in faces])
    me.update()
    for m in mats:
        me.materials.append(m)
    if mat_idx is not None:
        me.polygons.foreach_set('material_index', [int(i) for i in mat_idx])
    me.polygons.foreach_set('use_smooth', [smooth] * len(me.polygons))
    o = bpy.data.objects.new(name, me)
    sc.collection.objects.link(o)
    if solid:
        s = o.modifiers.new('solid', 'SOLIDIFY'); s.thickness = solid; s.offset = solid_off; s.use_even_offset = True
        s.material_offset_rim = 0
        if inner is not None:
            me.materials.append(inner); s.material_offset = 99  # clamped to the last slot

    if subsurf and not LOW_POLY:
        d = o.modifiers.new('sub', 'SUBSURF'); d.levels = subsurf; d.render_levels = subsurf
    o['bone'] = bone
    ARMOR.append(o)
    return o

def grid_faces(nu, nv, close_v=False):
    faces = []
    for i in range(nu - 1):
        for j in range(nv - (0 if close_v else 1)):
            j2 = (j + 1) % nv
            faces.append((i * nv + j, (i + 1) * nv + j, (i + 1) * nv + j2, i * nv + j2))
    return faces

def orient_outward(verts, faces, center):
    """Flip winding so face normals point away from center."""
    v = np.asarray(verts)
    score = 0.
    for f in faces[::max(1, len(faces) // 200)]:
        a, b, c = v[f[0]], v[f[1]], v[f[2]]
        n = np.cross(b - a, c - a)
        score += np.dot(n, a - center)
    return faces if score > 0 else [tuple(reversed(f)) for f in faces]

def frame_for(t):
    t = Vector(t).normalized()
    up = Vector((0, 0, 1)) if abs(t.z) < .9 else Vector((1, 0, 0))
    a = t.cross(up).normalized(); b = t.cross(a).normalized()
    return a, b

def tube(name, path, radius, mat_, bone, closed=True, seg=6, caps=False):
    path = [Vector(p) for p in path]
    n = len(path)
    verts, faces = [], []
    for i, p in enumerate(path):
        if closed:
            t = path[(i + 1) % n] - path[i - 1]
        else:
            t = path[min(i + 1, n - 1)] - path[max(i - 1, 0)]
        a, b = frame_for(t)
        r = radius[i] if hasattr(radius, '__len__') else radius
        for k in range(seg):
            ang = 2 * math.pi * k / seg
            verts.append(p + r * (math.cos(ang) * a + math.sin(ang) * b))
    rows = n + (1 if closed else 0)
    for i in range(rows - 1):
        i1, i2 = i % n, (i + 1) % n
        for k in range(seg):
            k2 = (k + 1) % seg
            faces.append((i1 * seg + k, i1 * seg + k2, i2 * seg + k2, i2 * seg + k))
    if caps and not closed:
        c0 = len(verts); verts.append(path[0]); c1 = len(verts); verts.append(path[-1])
        for k in range(seg):
            k2 = (k + 1) % seg
            faces.append((c0, k2, k)); faces.append((c1, (n - 1) * seg + k, (n - 1) * seg + k2))
    return obj(name, verts, faces, [mat_], bone)

def lathe(name, profile, origin, axis, mats, bone, seg=14, mat_per_span=None, subsurf=0):
    """profile: [(radius, distance along axis)], rotated around axis through origin."""
    axis = Vector(axis).normalized(); a, b = frame_for(axis); o = Vector(origin)
    verts, faces, mi = [], [], []
    for r, d in profile:
        for k in range(seg):
            ang = 2 * math.pi * k / seg
            verts.append(o + axis * d + r * (math.cos(ang) * a + math.sin(ang) * b))
    for i in range(len(profile) - 1):
        for k in range(seg):
            k2 = (k + 1) % seg
            faces.append((i * seg + k, i * seg + k2, (i + 1) * seg + k2, (i + 1) * seg + k))
            mi.append(mat_per_span[i] if mat_per_span else 0)
    return obj(name, verts, faces, mats, bone, mat_idx=mi, subsurf=subsurf)

def rbox(name, center, size, mat_, bone, bevel=.06, rot=None):
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.)
    bmesh.ops.scale(bm, vec=Vector(size), verts=bm.verts)
    if rot is not None:
        bmesh.ops.rotate(bm, matrix=rot, verts=bm.verts)
    bmesh.ops.translate(bm, vec=Vector(center), verts=bm.verts)
    verts = [v.co.copy() for v in bm.verts]; faces = [[v.index for v in f.verts] for f in bm.faces]
    bm.free()
    o = obj(name, verts, faces, [mat_], bone, smooth=True)
    bv = o.modifiers.new('bevel', 'BEVEL'); bv.width = bevel; bv.segments = 2; bv.limit_method = 'NONE'
    bv.harden_normals = True
    return o

def disc(name, center, normal, r, thick, mat_, bone, seg=10):
    n = Vector(normal).normalized(); c = Vector(center)
    return lathe(name, [(0, -thick / 2), (r, -thick / 2), (r, thick / 2), (0, thick / 2)],
                 c, n, [mat_], bone, seg=seg)

def ring(name, center, normal, r, tube_r, mat_, bone, rx=None, ry=None, seg=14):
    n = Vector(normal).normalized(); a, b = frame_for(n); c = Vector(center)
    rx = rx or r; ry = ry or r
    path = [c + rx * math.cos(2 * math.pi * k / seg) * a + ry * math.sin(2 * math.pi * k / seg) * b for k in range(seg)]
    return tube(name, path, tube_r, mat_, bone, closed=True, seg=4)

def ellipse_ring_xy(name, cx, cy, z, rx, ry, tube_r, mat_, bone, seg=16):
    path = [(cx + rx * math.cos(2 * math.pi * k / seg), cy + ry * math.sin(2 * math.pi * k / seg), z) for k in range(seg)]
    return tube(name, path, tube_r, mat_, bone, closed=True, seg=4)

def catmull(xs, ys, x):
    """Smooth interpolation through control points (clamped ends)."""
    xs, ys = np.asarray(xs, float), np.asarray(ys, float)
    x = np.clip(x, xs[0], xs[-1])
    i = np.clip(np.searchsorted(xs, x) - 1, 0, len(xs) - 2)
    t = (x - xs[i]) / (xs[i + 1] - xs[i])
    p0 = ys[np.maximum(i - 1, 0)]; p1 = ys[i]; p2 = ys[i + 1]; p3 = ys[np.minimum(i + 2, len(ys) - 1)]
    return .5 * ((2 * p1) + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t ** 2 + (-p0 + 3 * p1 - 3 * p2 + p3) * t ** 3)

# ---------------------------------------------------------------- 1. back shell
CY, CZ = .62, 4.30
RX, RY, RZ = 1.24, 2.15, 2.00
SHELL_T = .075
RIM_Y = [-.75, -.40, -.10, .25, .70, 1.10, 1.50, 1.85, 2.25, 2.90]
RIM_Z = [6.9, 6.05, 5.10, 4.62, 4.32, 4.30, 4.48, 4.62, 4.52, 4.50]
def zrim(y):
    return catmull(RIM_Y, RIM_Z, y)

def shell_point(al, be):
    sa = math.sin(al)
    return (RX * sa * math.sin(be), CY - RY * math.cos(al), CZ + RZ * sa * math.cos(be))

def shell_surface(x, y, z_side):
    """Outer x of the shell at (y, z) on one side (for decals)."""
    q = 1 - ((y - CY) / RY) ** 2 - ((z_side - CZ) / RZ) ** 2
    return RX * math.sqrt(max(q, 0))

al_all = np.linspace(.02, math.pi - .02, 600)
valid = []
for al in al_all:
    y = CY - RY * math.cos(al)
    s = (zrim(y) - CZ) / (RZ * math.sin(al))
    valid.append(s < .995)
valid = np.array(valid)
a0, a1 = al_all[valid.argmax()], al_all[len(valid) - 1 - valid[::-1].argmax()]
NA = 32
alphas = np.linspace(a0, a1, NA)
BAND = [0., .06, .13, .175, .205, .23]  # arc distance from rim: white band, plum line, pink
NINNER = 4
verts, rows_mat = [], []
for al in alphas:
    y = CY - RY * math.cos(al); sa = math.sin(al)
    s = np.clip((zrim(y) - CZ) / (RZ * sa), -1, .999)
    bmax = math.acos(s)
    g = max(sa * math.hypot(RX * math.cos(bmax), RZ * math.sin(bmax)), 1e-4)
    side = []
    for k, dd in enumerate(BAND):
        b = max(bmax - dd / g, bmax * (1 - .5 * k / len(BAND)))  # narrow ends: band takes at most half
        side.append(min(b, side[-1] - 1e-5) if side else b)
    inner_start = side[-1]
    inner = list(np.linspace(inner_start, 0, NINNER + 1)[1:])
    half = list(side) + inner                      # from rim (bmax) to top (0)
    betas = half + [-b for b in reversed(half[:-1])]  # rim -> top -> other rim
    verts.extend(shell_point(al, b) for b in betas)
NB = len(betas)
faces = grid_faces(NA, NB)
mi = []
nb_band = len(BAND)
for i in range(NA - 1):
    for j in range(NB - 1):
        jj = min(j, NB - 2 - j)  # distance in rows from nearest rim
        mi.append(1 if jj < 3 else (2 if jj == 3 else 0))
faces = orient_outward(verts, faces, np.array([0, CY, CZ]))
shell = obj('Shell', verts, faces, [PINK, WHITE, PLUM], 'elephant_Spine2_bone', mat_idx=mi, solid=SHELL_T, subsurf=1, inner=LAVENDER)

def shell_top_z(x, y):
    q = 1 - (x / RX) ** 2 - ((y - CY) / RY) ** 2
    return CZ + RZ * math.sqrt(max(q, 0)) + SHELL_T

# ---------------------------------------------------------------- 2. cockpit
cy_c = -.02
ztop = shell_top_z(0, cy_c)
bm = bmesh.new(); bmesh.ops.create_uvsphere(bm, u_segments=16, v_segments=8, radius=1.)
vs = [Vector((v.co.x * .42, v.co.y * .86 + cy_c, v.co.z * .44 + ztop - .10)) for v in bm.verts]
fs = [[v.index for v in f.verts] for f in bm.faces]; bm.free()
obj('Cockpit Glass', vs, fs, [GLASS], 'elephant_Spine2_bone')
bm = bmesh.new(); bmesh.ops.create_uvsphere(bm, u_segments=12, v_segments=6, radius=1.)
vs = [Vector((v.co.x * .34, v.co.y * .72 + cy_c, v.co.z * .10 + ztop - .02)) for v in bm.verts]
fs = [[v.index for v in f.verts] for f in bm.faces]; bm.free()
obj('Cockpit Well', vs, fs, [WELL], 'elephant_Spine2_bone')
path = []
for k in range(20):
    t = 2 * math.pi * k / 20
    x, y = .43 * math.cos(t), cy_c + .88 * math.sin(t)
    path.append((x, y, shell_top_z(x, y) - .005))
tube('Cockpit Frame', path, .055, WHITE, 'elephant_Spine2_bone')

# ---------------------------------------------------------------- 3. fins
def fin(sx):
    """Faceted blade (diamond cross-section, flat shaded) with a white tip, like MEKA's fins."""
    K = 8
    root_y0, root_y1, tip_y = 1.00, 1.95, 2.60
    xr = .30 * sx
    zr = shell_top_z(xr, 1.5) - .06
    h = 1.05
    verts, faces, mi = [], [], []
    for k in range(K + 1):
        t = k / K
        chord = (root_y1 - root_y0) * (1 - t) ** 1.15 + .06 * t
        lead = root_y0 + (tip_y - .04 - root_y0) * t ** .8
        zc = zr + h * t
        th = .16 * (1 - t) + .03 * t
        # leading edge, upper flank, trailing edge, lower flank (blade is thickest 35% back from the leading edge)
        verts += [(xr, lead, zc), (xr + th / 2, lead + chord * .35, zc), (xr, lead + chord, zc), (xr - th / 2, lead + chord * .35, zc)]
    for k in range(K):
        for p in range(4):
            p2 = (p + 1) % 4
            faces.append((k * 4 + p, k * 4 + p2, (k + 1) * 4 + p2, (k + 1) * 4 + p))
            mi.append(1 if k >= K * .70 else 0)
    c = len(verts); verts.append((xr, tip_y, zr + h + .05))
    for p in range(4):
        faces.append((K * 4 + p, K * 4 + (p + 1) % 4, c)); mi.append(1)
    faces = orient_outward(verts, faces, np.array([xr, 1.6, zr + .4]))
    o = obj('Fin ' + ('L' if sx > 0 else 'R'), verts, faces, [PINK, WHITE], 'elephant_Spine2_bone', mat_idx=mi, smooth=False)
    piv = Vector((xr, 1.5, zr))
    o.data.transform(Matrix.Translation(piv) @ Matrix.Rotation(math.radians(-13 * sx), 4, 'Y') @ Matrix.Translation(-piv))
fin(1); fin(-1)

# ---------------------------------------------------------------- 4. thrusters
for sx in (1, -1):
    c = Vector((.48 * sx, 0, 5.32)); ax = (0, 1, 0)
    tag = 'L' if sx > 0 else 'R'
    lathe('Thruster Body ' + tag, [(.0, 1.95), (.30, 1.97), (.34, 2.07), (.34, 2.35), (.37, 2.38), (.37, 2.48), (.34, 2.51), (.34, 2.78)],
          c, ax, [GUN], 'elephant_Spine2_bone', subsurf=0)
    lathe('Thruster Rim ' + tag, [(.335, 2.76), (.41, 2.78), (.44, 2.86), (.415, 2.94), (.35, 2.96), (.29, 2.95), (.275, 2.89), (.275, 2.80)],
          c, ax, [YELLOW], 'elephant_Spine2_bone', subsurf=1)
    lathe('Thruster Nozzle ' + tag, [(.276, 2.81), (.26, 2.70), (.0, 2.68)], c, ax, [GUN], 'elephant_Spine2_bone')
    disc('Thruster Glow ' + tag, c + Vector((0, 2.74, 0)), ax, .245, .02, TEAL, 'elephant_Spine2_bone')
    disc('Thruster Core ' + tag, c + Vector((0, 2.76, 0)), ax, .12, .02, CORE, 'elephant_Spine2_bone')
    EMITTERS.append(('Thruster ' + tag, 'elephant_Spine2_bone', tuple(c + Vector((0, 2.80, 0))), (0., 1., 0.)))

# ---------------------------------------------------------------- 5. shoulder cannons (rounded fusion-cannon pods)
def pod(name, profile, origin, axis, up, bone, seg=14, belly=.36, mats=None):
    """Lathe with a pink back and a dark belly: faces whose angle from 'up' is beyond (1-belly)*180 deg use mats[1]."""
    axis = Vector(axis).normalized(); a = Vector(up).normalized(); b = axis.cross(a).normalized(); o = Vector(origin)
    verts, faces, mi = [], [], []
    for r, d in profile:
        for k in range(seg):
            ang = 2 * math.pi * k / seg
            verts.append(o + axis * d + r * (math.cos(ang) * a + math.sin(ang) * b))
    for i in range(len(profile) - 1):
        for k in range(seg):
            k2 = (k + 1) % seg
            faces.append((i * seg + k, i * seg + k2, (i + 1) * seg + k2, (i + 1) * seg + k))
            mid = 2 * math.pi * (k + .5) / seg
            mi.append(1 if math.cos(mid) < math.cos(math.pi * (1 - belly)) else 0)
    return obj(name, verts, faces, mats or [PINK, LAVENDER], bone, mat_idx=mi)

CANNON_X, CANNON_Y0, CANNON_Z, POD_R = 1.04, .98, 4.25, .25    # top tucked into the shell rim, behind the front legs
PK = POD_R / .218
for sx in (1, -1):
    tag = 'L' if sx > 0 else 'R'
    bone = 'elephant_Spine2_bone'   # rides with the shell
    o = Vector((CANNON_X * sx, CANNON_Y0, CANNON_Z)); fwd = Vector((0, -1, 0)); up = Vector((0, 0, 1))
    pod('Cannon Pod ' + tag, [(r * PK, d * 1.12) for r, d in [(0, 0), (.11, .006), (.175, .04), (.208, .12), (.218, .30), (.218, .70), (.208, .86),
                             (.19, .93), (.205, .95), (.205, 1.03), (.175, 1.05), (0, 1.055)]], o, fwd, up, bone)
    lathe('Cannon Band ' + tag, [(.219 * PK, .83), (.228 * PK, .84), (.228 * PK, .92), (.219 * PK, .93)], o, fwd, [WHITE], bone, seg=14)
    ring('Cannon Light ' + tag, o + fwd * .18, fwd, .222 * PK, .018, TEAL, bone)
    for dx, dz in ((-.085, .085), (.085, .085), (-.085, -.085), (.085, -.085)):
        bc = o + fwd * 1.12 + Vector((dx, 0, dz))
        lathe('Cannon Barrel ' + tag, [(r * 1.12, d * 1.2) for r, d in [(0, 0), (.056, 0), (.056, .17), (.064, .175), (.064, .21), (.042, .215), (.042, .17), (0, .165)]],
              bc, fwd, [GUN], bone, seg=8)
    ring('Cannon Muzzle Glow ' + tag, o + fwd * 1.175, fwd, .172 * PK, .022, TEAL, bone)
    EMITTERS.append(('Bubble ' + tag, bone, tuple(o + fwd * 1.42), tuple(fwd)))
    # strut from the flank to the pod, hidden under the shell
    lathe('Cannon Mount ' + tag, [(0, 0), (.09, 0), (.09, .30), (0, .30)], Vector((.74 * sx, CANNON_Y0 - .45, CANNON_Z + .08)), (sx, 0, 0), [GUN], bone, seg=12)
    jc = Vector(((CANNON_X + POD_R * .95) * sx, CANNON_Y0 - .14, CANNON_Z))
    disc('Cannon Joint ' + tag, jc, (sx, 0, 0), .11, .05, GUN, bone)
    ring('Cannon Joint Glow ' + tag, jc + Vector((.028 * sx, 0, 0)), (sx, 0, 0), .075, .016, TEAL, bone)

# ---------------------------------------------------------------- 6. chest plate (projected onto the chest)
nu, nv = 8, 5
verts = []
for i in range(nu):
    for j in range(nv):
        u = -1 + 2 * i / (nu - 1); v = j / (nv - 1)
        width = .30 * (1 - .45 * (1 - v) ** 2)  # narrower toward the bottom (shield shape)
        verts.append((u * width, -1.25, 3.96 + .40 * v))
cp = obj('Chest Plate', verts, grid_faces(nu, nv), [WHITE], 'elephant_Spine3_bone', solid=.045, subsurf=1)
sw = cp.modifiers.new('wrap', 'SHRINKWRAP'); sw.target = ele; sw.wrap_method = 'PROJECT'
sw.use_project_y = True; sw.use_negative_direction = False; sw.use_positive_direction = True; sw.offset = .05
cp.modifiers.move(cp.modifiers.find('wrap'), 0)

# ---------------------------------------------------------------- 7. leg armour
LEGS = {'Front L': (.36, -.17, .255, .34, 3.52, 'elephant_l_Radius_bone'),
        'Front R': (-.36, -.17, .255, .34, 3.52, 'elephant_r_Radius_bone'),
        'Rear L': (.37, 1.78, .25, .36, 3.46, 'elephant_l_Tibia_bone'),
        'Rear R': (-.37, 1.78, .25, .36, 3.46, 'elephant_r_Tibia_bone')}
for name, (cx, cyl, rx, ry, ztop_l, bone) in LEGS.items():
    sx = 1 if cx > 0 else -1
    prof = [(2.66, 1.06), (2.90, 1.04), (3.20, 1.04), (ztop_l, 1.0)]
    seg = 16
    verts = []
    for z, sc_ in prof:
        for k in range(seg):
            t = 2 * math.pi * k / seg
            verts.append((cx + rx * sc_ * math.cos(t), cyl + ry * sc_ * math.sin(t), z))
    faces = orient_outward(verts, grid_faces(len(prof), seg, close_v=True), np.array([cx, cyl, 3.1]))
    obj('Greave ' + name, verts, faces, [PINK], bone, solid=.055, subsurf=1)
    ellipse_ring_xy('Greave Glow ' + name, cx, cyl, 2.69, rx * 1.06 + .04, ry * 1.06 + .04, .03, TEAL, bone)
    ellipse_ring_xy('Greave Band ' + name, cx, cyl, 2.80, rx * 1.06 + .045, ry * 1.06 + .045, .02, PLUM, bone)
    ellipse_ring_xy('Greave Top ' + name, cx, cyl, ztop_l + .01, rx * 1.01 + .05, ry * 1.01 + .05, .045, PLUM, bone)
    # white shin plate over the front 120 degrees
    pv = []
    for z in np.linspace(2.92, 3.38, 2):
        for t in np.linspace(math.radians(-150), math.radians(-30), 6):
            sc2 = 1.04 + .055 / max(rx, ry) + .02
            pv.append((cx + rx * sc2 * math.cos(t), cyl + ry * sc2 * math.sin(t), z))
    pf = orient_outward(pv, grid_faces(2, 6), np.array([cx, cyl, 3.1]))
    obj('Shin Plate ' + name, pv, pf, [WHITE], bone, solid=.03)
    for wy in (-.17, .17):
        wc = Vector((cx + sx * (rx * 1.06 + .075), cyl + wy, 2.74))
        disc('Wheel ' + name, wc, (sx, 0, 0), .075, .06, PLUM, bone)
        disc('Wheel Hub ' + name, wc + Vector((.032 * sx, 0, 0)), (sx, 0, 0), .03, .012, TEAL, bone)
    kc = Vector((cx, cyl - ry * 1.04 - .135, 3.18))
    disc('Knee Pad ' + name, kc, (0, -1, 0), .11, .05, PLUM, bone)
    ring('Knee Glow ' + name, kc + Vector((0, -.028, 0)), (0, -1, 0), .085, .016, TEAL, bone)
    jc = Vector((cx + sx * (rx * 1.05 + .075), cyl, 3.18))
    disc('Leg Joint ' + name, jc, (sx, 0, 0), .13, .05, PLUM, bone)
    ring('Leg Joint Glow ' + name, jc + Vector((.028 * sx, 0, 0)), (sx, 0, 0), .095, .018, TEAL, bone)

# ---------------------------------------------------------------- 8. helmet, headset, antenna
HCX, HCY, HCZ = 0., -1.05, 5.40
HRX, HRY, HRZ = .43, .70, .60
def helmet_cut(y):
    return catmull([-1.9, -1.55, -1.25, -.9, -.4], [5.42, 5.50, 5.62, 5.70, 5.72], y)
al_all = np.linspace(.02, math.pi - .02, 400)
ok = []
for al in al_all:
    y = HCY - HRY * math.cos(al)
    ok.append((helmet_cut(y) - HCZ) / (HRZ * math.sin(al)) < .995)
ok = np.array(ok)
ha0, ha1 = al_all[ok.argmax()], al_all[len(ok) - 1 - ok[::-1].argmax()]
verts = []
NH, MH = 18, 11
for al in np.linspace(ha0, ha1, NH):
    y = HCY - HRY * math.cos(al); sa = math.sin(al)
    bmax = math.acos(np.clip((helmet_cut(y) - HCZ) / (HRZ * sa), -1, .999))
    for be in np.linspace(-bmax, bmax, MH):
        verts.append((HRX * sa * math.sin(be), y, HCZ + HRZ * sa * math.cos(be)))
faces = orient_outward(verts, grid_faces(NH, MH), np.array([HCX, HCY, HCZ]))
mi = [1 if min(j, MH - 2 - j) < 2 else 0 for i in range(NH - 1) for j in range(MH - 1)]
obj('Helmet', verts, faces, [PINK, PLUM], 'elephant_Head_bone', mat_idx=mi, solid=.05, subsurf=1)
# forehead light strip, following the helmet front
vz = 5.66
vy = HCY - HRY * math.sqrt(max(0, 1 - ((vz - HCZ) / HRZ) ** 2)) - .045
rbox('Helmet Visor Light', (0, vy, vz), (.20, .03, .05), TEAL, 'elephant_Head_bone', bevel=.012,
     rot=Matrix.Rotation(math.radians(-38), 3, 'X'))
rbox('Helmet Visor Frame', (0, vy + .015, vz), (.27, .03, .09), PLUM, 'elephant_Head_bone', bevel=.015,
     rot=Matrix.Rotation(math.radians(-38), 3, 'X'))
for sx in (1, -1):
    tag = 'L' if sx > 0 else 'R'
    ec = Vector((.535 * sx, -1.17, 5.13))
    disc('Earcup ' + tag, ec, (sx, 0, 0), .135, .09, PINK, 'elephant_Head_bone')
    ring('Earcup Rim ' + tag, ec + Vector((.045 * sx, 0, 0)), (sx, 0, 0), .125, .02, WHITE, 'elephant_Head_bone')
    ring('Earcup Glow ' + tag, ec + Vector((.05 * sx, 0, 0)), (sx, 0, 0), .075, .016, TEAL, 'elephant_Head_bone')
    disc('Earcup Cap ' + tag, ec + Vector((.045 * sx, 0, 0)), (sx, 0, 0), .05, .03, PLUM, 'elephant_Head_bone')
    band = [(.51 * sx, -1.17, 5.26), (.50 * sx, -1.16, 5.42), (.44 * sx, -1.14, 5.58), (.36 * sx, -1.12, 5.70)]
    tube('Headset Band ' + tag, band, .028, PLUM, 'elephant_Head_bone', closed=False, seg=10, caps=True)
ant = [(.16, -0.85, 5.95), (.20, -0.78, 6.20), (.24, -0.70, 6.42)]
tube('Antenna', ant, .018, PLUM, 'elephant_Head_bone', closed=False, seg=8, caps=True)
bm = bmesh.new(); bmesh.ops.create_uvsphere(bm, u_segments=8, v_segments=4, radius=.045)
vs = [v.co + Vector(ant[-1]) for v in bm.verts]; fs = [[v.index for v in f.verts] for f in bm.faces]; bm.free()
obj('Antenna Tip', vs, fs, [PINK], 'elephant_Head_bone')

# ---------------------------------------------------------------- 9. decals: DIVA lettering and our own winking bunny
def map_to_shell(u, v, side, y0, z0, lift):
    y = y0 + u * side  # viewer's right is +y on the elephant's left (+x) side
    z = z0 + v
    x = shell_surface(0, y, z)
    q = Vector((x, (y - CY) * (RX / RY) ** 2, (z - CZ) * (RX / RZ) ** 2)).normalized()  # ellipsoid normal (x>0 side)
    p = Vector((x, y, z)) + q * (SHELL_T + lift)
    if side < 0:
        p.x = -p.x
    return p

def flat_decal(name, polys2d, side, y0, z0, lift, mat_, thick=.012, scale=1.):
    """polys2d: list of (verts2d, faces) in sticker space; extruded slightly off the shell."""
    verts, faces = [], []
    for v2, f2 in polys2d:
        base = len(verts)
        n = len(v2)
        for u, v in v2:
            verts.append(map_to_shell(u * scale, v * scale, side, y0, z0, lift + thick))
        for f in f2:
            faces.append(tuple(base + i for i in f))  # winding stays outward on both sides
    return obj(name, verts, faces, [mat_], 'elephant_Spine2_bone', smooth=False)

def circle2d(cx, cy, r, ry=None, n=14, rot=0.):
    ry = ry or r
    pts = []
    for k in range(n):
        t = 2 * math.pi * k / n
        u, v = r * math.cos(t), ry * math.sin(t)
        pts.append((cx + u * math.cos(rot) - v * math.sin(rot), cy + u * math.sin(rot) + v * math.cos(rot)))
    c = len(pts); pts.append((cx, cy))
    return pts, [(k, (k + 1) % n, c) for k in range(n)]

def heart2d(cx, cy, r, n=18):
    pts = []
    for k in range(n):
        t = 2 * math.pi * k / n
        u = 16 * math.sin(t) ** 3; v = 13 * math.cos(t) - 5 * math.cos(2 * t) - 2 * math.cos(3 * t) - math.cos(4 * t)
        pts.append((cx + r * u / 17, cy + r * v / 17))
    c = len(pts); pts.append((cx, cy))
    return pts, [(k, (k + 1) % n, c) for k in range(n)]

def arc_strip(cx, cy, r, a0, a1, w, n=8):
    pts, faces = [], []
    for k in range(n + 1):
        t = a0 + (a1 - a0) * k / n
        pts.append((cx + (r + w / 2) * math.cos(t), cy + (r + w / 2) * math.sin(t)))
        pts.append((cx + (r - w / 2) * math.cos(t), cy + (r - w / 2) * math.sin(t)))
    for k in range(n):
        faces.append((2 * k, 2 * k + 2, 2 * k + 3, 2 * k + 1))
    return pts, faces

# text -> mesh in the XY plane
bpy.ops.object.text_add(location=(0, 0, 0))
txt = bpy.context.object
txt.data.body = __import__('os').environ.get('LETTERING', 'DVA')
txt.data.font = bpy.data.fonts.load(FONT)
txt.data.size = float(__import__('os').environ.get('LETTER_SIZE', '.92'))
txt.data.align_x = 'CENTER'; txt.data.align_y = 'CENTER'
txt.data.fill_mode = 'FRONT'
bpy.ops.object.convert(target='MESH')
tm = txt.data
_sh = math.tan(math.radians(float(__import__('os').environ.get('OBLIQUE', '12'))))  # Overwatch-style italic slant
text_poly = ([(v.co.x + v.co.y * _sh, v.co.y) for v in tm.vertices], [tuple(p.vertices) for p in tm.polygons])
bpy.data.objects.remove(txt)

for side in (1, -1):
    tag = 'L' if side > 0 else 'R'
    flat_decal('Decal Lettering ' + tag, [text_poly], side, .55, 5.55, .004, MAGENTA)
    by, bz = 1.30, 4.98  # bunny sticker centre (toward the tail, below the lettering)
    flat_decal('Decal Bunny ' + tag, [circle2d(0, 0, .19, .165), circle2d(-.085, .21, .055, .15, rot=.25), circle2d(.09, .21, .055, .15, rot=-.25)],
               side, by, bz, .004, WHITE, scale=1.45)
    flat_decal('Decal Bunny Inner ' + tag, [circle2d(-.085, .22, .025, .10, rot=.25), circle2d(.09, .22, .025, .10, rot=-.25),
                                         circle2d(-.11, -.045, .035, .022), circle2d(.11, -.045, .035, .022), heart2d(.27, -.08, .07)],
               side, by, bz, .018, STICKER_PINK, scale=1.45)
    flat_decal('Decal Bunny Face ' + tag, [circle2d(-.065, .025, .022), arc_strip(.065, .01, .028, .3, math.pi - .3, .014),
                                        circle2d(0, -.035, .018, .012)], side, by, bz, .018, PLUM, scale=1.45)

# ---------------------------------------------------------------- 9b. MEKA-style surface details
def shell_normal(pt):
    g = Vector((pt[0] / RX ** 2, (pt[1] - CY) / RY ** 2, (pt[2] - CZ) / RZ ** 2))
    return g.normalized()

def shell_at(al, be, lift):
    p = Vector(shell_point(al, be))
    return p + shell_normal(p) * (SHELL_T + lift)

def al_of_y(y):
    return math.acos(max(-1, min(1, (CY - y) / RY)))

def bmax_of(al):
    y = CY - RY * math.cos(al)
    return math.acos(float(np.clip((zrim(y) - CZ) / (RZ * math.sin(al)), -1, .999)))

def seam(name, pts, r=.011):
    tube(name, pts, r, SEAM, 'elephant_Spine2_bone', closed=False, seg=3, caps=False)

# top panel seams: two lines along the back, joined by two cross seams (sides stay clean for the lettering)
FRAC = .40
ya, yb = .95, 2.05
for sgn in (1, -1):
    pts = []
    for y in np.linspace(ya, yb, 12):
        al = al_of_y(y); pts.append(shell_at(al, sgn * FRAC * bmax_of(al), .004))
    seam('Shell Seam ' + ('L' if sgn > 0 else 'R'), pts)
for y in (ya, 1.55, yb):
    al = al_of_y(y); bm = FRAC * bmax_of(al)
    seam('Shell Seam X %.2f' % y, [shell_at(al, b, .004) for b in np.linspace(-bm, bm, 9)])
# front seam just behind the cockpit frame, across the whole top
al = al_of_y(-.32 + .02)
# side seams: a short vertical line in front of the lettering and one behind the bunny
for sgn in (1, -1):
    for y in (-.05,):
        al = al_of_y(y); bm = bmax_of(al)
        seam('Shell Side Seam %s %.2f' % ('L' if sgn > 0 else 'R', y), [shell_at(al, sgn * b, .004) for b in np.linspace(FRAC * bm, bm - .26 / max(.3, math.sin(al)) / RX, 6)])

def surface_decal(name, polys2d, y0, be_frac, side, lift, mat_, scale=1.):
    """Map sticker-space (u right, v toward the front) onto the shell around (y0, beta = side*be_frac*bmax)."""
    al0 = al_of_y(y0); be0 = side * be_frac * bmax_of(al0)
    p0 = Vector(shell_point(al0, be0))
    sa = math.sin(al0)
    da = math.sqrt((RX * math.cos(al0) * math.sin(be0)) ** 2 + (RY * sa) ** 2 + (RZ * math.cos(al0) * math.cos(be0)) ** 2)
    db = sa * math.sqrt((RX * math.cos(be0)) ** 2 + (RZ * math.sin(be0)) ** 2)
    verts, faces = [], []
    for v2, f2 in polys2d:
        base = len(verts)
        for u, v in v2:
            verts.append(shell_at(al0 - v * scale / da, be0 + u * scale / db, lift))
        faces += [tuple(base + i for i in f) for f in f2]
    faces = orient_outward(verts, faces, np.array([0, CY, CZ]))
    return obj(name, verts, faces, [mat_], 'elephant_Spine2_bone', smooth=False)

def hex2d(r, rot=math.pi / 6):
    pts = [(r * math.cos(rot + k * math.pi / 3), r * math.sin(rot + k * math.pi / 3)) for k in range(6)]
    c = len(pts); pts.append((0, 0))
    return pts, [(k, (k + 1) % 6, c) for k in range(6)]

def rect2d(cx, cy, w, h):
    return [(cx - w / 2, cy - h / 2), (cx + w / 2, cy - h / 2), (cx + w / 2, cy + h / 2), (cx - w / 2, cy + h / 2)], [(0, 1, 2, 3)]

def rrect2d(w, h, r, n=4):
    pts = []
    for cx, cy, a0 in ((w / 2 - r, h / 2 - r, 0), (-w / 2 + r, h / 2 - r, 90), (-w / 2 + r, -h / 2 + r, 180), (w / 2 - r, -h / 2 + r, 270)):
        for k in range(n + 1):
            a = math.radians(a0 + 90 * k / n); pts.append((cx + r * math.cos(a), cy + r * math.sin(a)))
    c = len(pts); pts.append((0, 0))
    return pts, [(k, (k + 1) % (len(pts) - 1), c) for k in range(len(pts) - 1)]

def hex_vent(name, y0, be_frac, side, size):
    surface_decal(name + ' Frame', [hex2d(size)], y0, be_frac, side, .006, CREAM)
    surface_decal(name + ' Hole', [hex2d(size * .72)], y0, be_frac, side, .010, SEAM)
    surface_decal(name + ' Slats', [rect2d(0, k * size * .28, size * 1.05, size * .09) for k in (-1, 0, 1)], y0, be_frac, side, .014, CREAM)

hex_vent('Vent Top', 1.25, 0, 1, .17)
for side in (1, -1):
    hex_vent('Vent Front ' + ('L' if side > 0 else 'R'), .05, .62, side, .11)
    for y, f in ((1.0, FRAC), (2.0, FRAC), (1.55, FRAC)):
        surface_decal('Bolt %s %.2f' % ('L' if side > 0 else 'R', y), [circle2d(0, 0, .028, n=10)], y + .07, f * 1.12, side, .008, GUN)
# "GG" sticker (orange, white border) on the top-left behind the vent, and a heart on the right
bpy.ops.object.text_add(location=(0, 0, 0))
gtxt = bpy.context.object
gtxt.data.body = 'GG'; gtxt.data.font = bpy.data.fonts.load(FONT); gtxt.data.size = .24
gtxt.data.align_x = 'CENTER'; gtxt.data.align_y = 'CENTER'; gtxt.data.fill_mode = 'FRONT'
bpy.ops.object.convert(target='MESH')
gg_poly = ([(v.co.x + v.co.y * .2, v.co.y) for v in gtxt.data.vertices], [tuple(q.vertices) for q in gtxt.data.polygons])
bpy.data.objects.remove(gtxt)
surface_decal('Sticker GG Border', [rrect2d(.44, .30, .07)], 1.62, .24, 1, .006, WHITE)
surface_decal('Sticker GG', [rrect2d(.39, .25, .055)], 1.62, .24, 1, .010, ORANGE)
surface_decal('Sticker GG Text', [gg_poly], 1.62, .24, 1, .014, PLUM)
surface_decal('Sticker Heart', [heart2d(0, 0, .13)], 1.62, .24, -1, .008, STICKER_PINK)

# mint name tag on the right flank toward the rear (our own text, in the spirit of MEKA's green tag)
bpy.ops.object.text_add(location=(0, 0, 0))
ttxt = bpy.context.object
ttxt.data.body = 'DIVA'; ttxt.data.font = bpy.data.fonts.load(FONT); ttxt.data.size = .22
ttxt.data.align_x = 'CENTER'; ttxt.data.align_y = 'CENTER'; ttxt.data.fill_mode = 'FRONT'
bpy.ops.object.convert(target='MESH')
tag_poly = ([(v.co.x + v.co.y * .2, v.co.y) for v in ttxt.data.vertices], [tuple(q.vertices) for q in ttxt.data.polygons])
bpy.data.objects.remove(ttxt)
def pod_decal(name, polys2d, sx, y_mid, z_mid, lift, mat_):
    """Wrap a sticker around the outer side of a cannon pod (cylinder along y)."""
    verts, faces = [], []
    for v2, f2 in polys2d:
        base = len(verts)
        for u, v in v2:
            y = y_mid + u * sx                     # viewer's right is +y on the left (+x) side
            dz = (z_mid - CANNON_Z) + v
            x = math.sqrt(max(POD_R ** 2 - dz ** 2, 1e-6)) + lift
            verts.append((sx * (CANNON_X + x), y, CANNON_Z + dz))
        faces += [tuple(base + i for i in f) for f in f2]
    faces = orient_outward(verts, faces, np.array([CANNON_X * sx, y_mid, CANNON_Z]))
    return obj(name, verts, faces, [mat_], 'elephant_Spine2_bone', smooth=True)

for sx in (1, -1):
    tg = 'L' if sx > 0 else 'R'
    ym = CANNON_Y0 - .55
    pod_decal('Tag Border ' + tg, [rrect2d(.50, .22, .05)], sx, ym, CANNON_Z + .02, .004, WHITE)
    pod_decal('Tag ' + tg, [rrect2d(.46, .18, .04)], sx, ym, CANNON_Z + .02, .009, MINT)
    pod_decal('Tag Text ' + tg, [([(x * .78, y * .78) for x, y in tag_poly[0]], tag_poly[1])], sx, ym, CANNON_Z + .02, .014, DARK_GREEN)

# helmet: white centre stripe from the visor over the top
hp = []
for y in np.linspace(-1.55, -.55, 16):
    q = 1 - ((y - HCY) / HRY) ** 2
    if q <= 0 or HCZ + HRZ * math.sqrt(q) < helmet_cut(y) + .02:
        continue
    hp.append((0, y, HCZ + HRZ * math.sqrt(q) + .055))
if len(hp) > 3:
    tube('Helmet Stripe', hp, .03, WHITE, 'elephant_Head_bone', closed=False, seg=6, caps=True)

# ---------------------------------------------------------------- 10. trunk water blaster
def trunk_blaster():
    me = ele.data; Me = ele.matrix_world
    gi = {g.name: g.index for g in ele.vertex_groups}
    def w(v, name):
        for g in v.groups:
            if g.group == gi[name]:
                return g.weight
        return 0.
    P = np.array([tuple(Me @ v.co) for v in me.vertices])
    W7 = np.array([w(v, 'elephant_Trunk7_bone') for v in me.vertices])
    W6 = np.array([w(v, 'elephant_Trunk6_bone') for v in me.vertices])
    W5 = np.array([w(v, 'elephant_Trunk5_bone') for v in me.vertices])
    c7 = P[W7 > .5].mean(0); c6 = P[W6 > .5].mean(0); c5 = P[W5 > .5].mean(0)
    d = c7 - (c6 + c5) / 2; d /= np.linalg.norm(d)            # direction the trunk tip points
    trunk = (W7 + W6 + W5) > .2
    q = (P - c7) @ d
    tip = float(q[W7 > .3].max())
    perp = (P - c7) - np.outer(q, d)
    def slice_r(sv):
        m = trunk & (np.abs(q - sv) < .035)
        return np.linalg.norm(perp[m] - perp[m].mean(0), axis=1).max() if m.sum() > 3 else None
    centre = c7 + perp[(W7 > .3) & (q > tip - .25)].mean(0)
    rs = [r for r in (slice_r(tip - k * .04) for k in range(1, 8)) if r]
    r0 = float(max(rs) + .035)
    o = Vector(centre); ax = Vector(d)
    bone = 'elephant_Trunk7_bone'
    # Sleeve over the last trunk segment
    s0 = tip - .34
    lathe('Trunk Sleeve', [(r0 * .92, s0 - .01), (r0 * 1.04, s0 + .03), (r0 * 1.04, tip - .02), (r0 * .98, tip + .02)], o, ax, [PINK], bone, seg=24)
    lathe('Trunk Sleeve Band', [(r0 * 1.05, tip - .22), (r0 * 1.10, tip - .21), (r0 * 1.10, tip - .14), (r0 * 1.05, tip - .13)], o, ax, [WHITE], bone, seg=24)
    ring('Trunk Sleeve Rim', o + ax * (s0 + .02), ax, r0 * 1.02, .03, PLUM, bone)
    # Elbow barrel: straight out of the tip, then bent away from the trunk's curl so it points forward when the trunk hangs
    side = Vector((0, 0, 1)).cross(ax).cross(ax).normalized() * -1   # in the trunk's bending plane
    bend_dir = side * float(__import__('os').environ.get('BARREL_BEND_SIGN', '1'))
    ang = math.radians(float(__import__('os').environ.get('BARREL_BEND_DEG', '80')))
    rb = r0 * .78
    path, radii = [], []
    p0 = o + ax * (tip + .0)
    for k in range(4):                                   # straight stub
        path.append(p0 + ax * (.03 * k)); radii.append(rb)
    pc = path[-1]
    R = .20
    for k in range(1, 9):                                # arc
        a = ang * k / 8
        path.append(pc + ax * (R * math.sin(a)) + bend_dir * (R * (1 - math.cos(a)))); radii.append(rb)
    out = (ax * math.cos(ang) + bend_dir * math.sin(ang)).normalized()
    pe = path[-1]
    for k in range(1, 6):                                # straight barrel
        path.append(pe + out * (.055 * k)); radii.append(rb)
    tube('Trunk Barrel', path, radii, PINK, bone, closed=False, seg=12, caps=False)
    disc('Trunk Sleeve Cap', o + ax * (tip + .025), ax, r0 * .99, .03, PLUM, bone)  # closes the sleeve end around the barrel
    ring('Trunk Barrel Joint', o + ax * (tip + .045), ax, rb * 1.04, .03, PLUM, bone)
    muzzle = path[-1]
    lathe('Trunk Barrel Band', [(rb * 1.0, -.20), (rb * 1.07, -.19), (rb * 1.07, -.12), (rb * 1.0, -.11)], muzzle, out, [WHITE], bone, seg=18)
    lathe('Trunk Muzzle Collar', [(rb * .98, -.06), (rb * 1.22, -.04), (rb * 1.26, .02), (rb * 1.22, .07), (rb * .80, .08)], muzzle, out, [PLUM], bone, seg=20)
    lathe('Trunk Muzzle Bore', [(rb * .80, .08), (rb * .70, .03), (0, .02)], muzzle, out, [GUN], bone, seg=20)
    ring('Trunk Muzzle Glow', muzzle + out * .065, out, rb * 1.06, .03, TEAL, bone)
    disc('Trunk Muzzle Lens', muzzle + out * .035, out, rb * .62, .012, TEAL, bone)
    disc('Trunk Muzzle Core', muzzle + out * .04, out, rb * .28, .01, CORE, bone)
    EMITTERS.append(('Trunk Blaster', bone, tuple(muzzle + out * .1), tuple(out)))
    print('TRUNK_BLASTER muzzle', tuple(round(x, 3) for x in muzzle), 'dir', tuple(round(x, 3) for x in out), 'r', round(r0, 3), 'barrel r', round(rb, 3))
trunk_blaster()

print('ARMOR_OBJECTS', len(ARMOR))
dg = bpy.context.evaluated_depsgraph_get()
tris = 0
for o in ARMOR:
    me = o.evaluated_get(dg).to_mesh(); me.calc_loop_triangles(); tris += len(me.loop_triangles); o.evaluated_get(dg).to_mesh_clear()
print('ARMOR_TRIS', tris)
if __import__('os').environ.get('TRI_REPORT'):
    import collections
    per = collections.Counter()
    for o in ARMOR:
        me = o.evaluated_get(dg).to_mesh(); me.calc_loop_triangles()
        key = o.name.rsplit(' ', 1)[0] if o.name.split(' ')[-1] in ('L', 'R') or o.name.split(' ')[-1].replace('.', '').isdigit() else o.name
        for k in ('Front', 'Rear'):
            key = key.replace(' ' + k, '')
        per[key] += len(me.loop_triangles); o.evaluated_get(dg).to_mesh_clear()
    for k, v in per.most_common(30):
        print('TRI', v, k)

# ---------------------------------------------------------------- export for Unity
import struct
def vertex_shade(P, N, tris):
    """0..1 per vertex: darker in creases and low down (grime), lighter on convex edges (worn paint) and top faces.
    Unity uses it as the V coordinate into a small gradient palette, so there is no UV unwrapping."""
    pk = np.round(P, 4)
    _, pid = np.unique(pk, axis=0, return_inverse=True)
    pid = pid.reshape(-1)
    npos = pid.max() + 1
    ppos = np.zeros((npos, 3)); ppos[pid] = P
    e = np.concatenate([tris[:, [0, 1]], tris[:, [1, 2]], tris[:, [2, 0]]])
    a, b = pid[e[:, 0]], pid[e[:, 1]]
    keep = a != b
    a, b = a[keep], b[keep]
    a, b = np.concatenate([a, b]), np.concatenate([b, a])
    d = ppos[b] - ppos[a]
    d /= np.maximum(np.linalg.norm(d, axis=1, keepdims=True), 1e-9)
    acc = np.zeros((npos, 3)); np.add.at(acc, a, d)
    cnt = np.bincount(a, minlength=npos).astype(float)
    dirs = acc[pid] / np.maximum(cnt[pid], 1)[:, None]
    c = np.einsum('ij,ij->i', N, dirs)                 # >0 crease, <0 convex edge
    grime = np.clip((3.35 - P[:, 2]) / .8, 0, 1)
    v = .5 - 1.6 * c + .10 * N[:, 2] - .28 * grime
    return np.clip(v, .03, .97)

def export_bin(path):
    """Binary file read by DivaMechBuilder.cs. Positions are Blender rest-pose world space; Unity aligns them
    to the elephant mesh through the reference (position, uv) pairs, so no axis convention is assumed here."""
    names = list(MATS.keys())
    groups = {}
    dg = bpy.context.evaluated_depsgraph_get()
    for o in ARMOR:
        ev = o.evaluated_get(dg); me = ev.to_mesh(); me.calc_loop_triangles()
        M = o.matrix_world; N3 = M.to_3x3().inverted().transposed()
        cn = me.corner_normals if hasattr(me, 'corner_normals') else None
        g = groups.setdefault(o['bone'], {'P': [], 'N': [], 'T': []})
        for t in me.loop_triangles:
            mname = me.materials[t.material_index].name
            mi = names.index(mname)
            for l in t.loops:
                v = me.loops[l].vertex_index
                g['P'].append(tuple(M @ me.vertices[v].co))
                n = cn[l].vector if cn is not None else (me.vertices[v].normal if t.use_smooth else t.normal)
                g['N'].append(tuple((N3 @ n).normalized()))
            g['T'].append(mi)
        ev.to_mesh_clear()
    with open(path, 'wb') as f:
        def wstr(x):
            b = x.encode(); f.write(struct.pack('<i', len(b))); f.write(b)
        f.write(b'DVM2')
        f.write(struct.pack('<i', len(names)))
        for n in names:
            u = MATS[n]['unity']
            col = [int(u['color'][i:i + 2], 16) / 255 for i in (1, 3, 5)]
            em_ = [int(u['emit'][i:i + 2], 16) / 255 for i in (1, 3, 5)] if u['emit'] else [0, 0, 0]
            wstr(n); f.write(struct.pack('<11f', *col, u['rough'], u['metal'], *em_, u['estr'], u['alpha'], 0.))
        me = ele.data; uvl = me.uv_layers.active.data; Me = ele.matrix_world
        f.write(struct.pack('<i', len(me.loops)))
        for l in me.loops:
            p = Me @ me.vertices[l.vertex_index].co; uv = uvl[l.index].uv
            f.write(struct.pack('<5f', p.x, p.y, p.z, uv.x, uv.y))
        f.write(struct.pack('<i', len(groups)))
        total = 0
        for bone, g in groups.items():
            P = np.round(np.array(g['P'], np.float64), 5); Nn = np.round(np.array(g['N'], np.float64), 3)
            key = np.concatenate([P, Nn], 1)
            uniq, inv = np.unique(key, axis=0, return_inverse=True)
            inv = inv.reshape(-1).reshape(-1, 3)
            T = np.array(g['T'])
            wstr(bone)
            f.write(struct.pack('<i', len(uniq)))
            f.write(uniq[:, :3].astype('<f4').tobytes()); f.write(uniq[:, 3:].astype('<f4').tobytes())
            f.write(vertex_shade(uniq[:, :3], uniq[:, 3:], inv).astype('<f4').tobytes())
            subs = sorted(set(T.tolist()))
            f.write(struct.pack('<i', len(subs)))
            for mi in subs:
                idx = inv[T == mi].reshape(-1).astype('<i4')
                f.write(struct.pack('<ii', mi, len(idx))); f.write(idx.tobytes())
            total += len(T)
            print('EXPORT_GROUP', bone, 'verts', len(uniq), 'tris', len(T))
        f.write(struct.pack('<i', len(EMITTERS)))
        for name, bone, pos, dirn in EMITTERS:
            wstr(name); wstr(bone); f.write(struct.pack('<6f', *pos, *dirn))
    print('EXPORTED', path, 'tris', total)

if 'EXPORT' in __import__('os').environ:
    export_bin(__import__('os').environ['EXPORT'])

# ---------------------------------------------------------------- render
sc.render.engine = 'BLENDER_EEVEE'
ee = sc.eevee
ee.use_bloom = True; ee.bloom_intensity = .04; ee.bloom_threshold = 1.2
ee.use_gtao = True; ee.gtao_distance = .6
ee.use_soft_shadows = True; ee.shadow_cube_size = '2048'; ee.shadow_cascade_size = '4096'
ee.taa_render_samples = 64
sc.view_settings.view_transform = 'Filmic'
sc.view_settings.look = 'None'
world = bpy.data.worlds.new('w'); sc.world = world; world.use_nodes = True
world.node_tree.nodes['Background'].inputs['Color'].default_value = lin('#F3EEE6')
world.node_tree.nodes['Background'].inputs['Strength'].default_value = .9
bpy.ops.mesh.primitive_plane_add(size=60, location=(0, 0, 2.44))
ground = bpy.context.active_object
gm = bpy.data.materials.new('ground'); gm.use_nodes = True
gm.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value = lin('#EDE6DA')
gm.node_tree.nodes['Principled BSDF'].inputs['Roughness'].default_value = .9
ground.data.materials.append(gm)
sun = bpy.data.lights.new('sun', 'SUN'); sun.energy = 3.2; sun.angle = math.radians(8)
so = bpy.data.objects.new('sun', sun); sc.collection.objects.link(so)
so.rotation_euler = (math.radians(42), math.radians(-8), math.radians(-38))
fill = bpy.data.lights.new('fill', 'AREA'); fill.energy = 900; fill.size = 10
fo = bpy.data.objects.new('fill', fill); sc.collection.objects.link(fo)
fo.location = (6, -9, 9); fo.rotation_euler = (Vector((0, 0, 4)) - fo.location).to_track_quat('-Z', 'Y').to_euler()
rim = bpy.data.lights.new('rim', 'AREA'); rim.energy = 500; rim.size = 8
ro = bpy.data.objects.new('rim', rim); sc.collection.objects.link(ro)
ro.location = (-6, 9, 8); ro.rotation_euler = (Vector((0, 0, 4)) - ro.location).to_track_quat('-Z', 'Y').to_euler()

cam = bpy.data.cameras.new('cam'); cam.lens = 50
co = bpy.data.objects.new('cam', cam); sc.collection.objects.link(co); sc.camera = co
sc.render.resolution_x, sc.render.resolution_y = 1200, 900
views = {
    'hero': ((8.2, -9.6, 6.9), (0, .05, 4.35)),
    'front': ((0, -13.5, 5.0), (0, 0, 4.35)),
    'side': ((13.5, .2, 4.9), (0, .2, 4.35)),
    'back': ((0, 13.5, 5.4), (0, .2, 4.4)),
    'face': ((3.2, -5.0, 5.9), (0, -1.0, 5.1)),
    'trunk': ((2.2, -5.4, 6.3), (0, -3.3, 5.75)),
    'top': ((5.5, -2.5, 9.8), (0, .8, 5.6)),
    'pod': ((4.6, -3.2, 4.6), (1.0, .3, 4.15)),
}
for name in VIEWS:
    loc, tgt = views[name]
    co.location = loc
    co.rotation_euler = (Vector(tgt) - Vector(loc)).to_track_quat('-Z', 'Y').to_euler()
    sc.render.filepath = f'{OUT}/mech_{name}.png'
    bpy.ops.render.render(write_still=True)
bpy.ops.wm.save_as_mainfile(filepath=f'{OUT}/diva_mech_preview.blend')
print('RENDERED')
