"""Diva VR cockpit: builds the cockpit frame in Blender and writes it as JSON for Unity.

Run:  /Applications/Blender.app/Contents/MacOS/Blender --background --python build_cockpit.py -- <out.json>
Unity loads Assets/DivaVR/Resources/DivaVR Cockpit.json (DivaVRCockpit builds the meshes at runtime).

Units are metres, origin at the seat, eye 0.78 m above it. Blender: x right, y forward, z up.
Unity gets (x, z, y). Each part has a material slot: body, glowA (skin primary), glowB (skin accent), screen.
Bevels use harden_normals, so the exported corner normals give crisp edges with soft bevels.
"""
import bpy, bmesh, json, math, sys

EYE = .78
out_path = sys.argv[sys.argv.index("--") + 1] if "--" in sys.argv else "cockpit.json"

bpy.ops.wm.read_factory_settings(use_empty=True)
coll = bpy.context.collection
parts = []   # (object, material slot)


def polar(r, deg, z):
    a = math.radians(deg)
    return (math.sin(a) * r, math.cos(a) * r, z)


def sector(name, r0, r1, a0, a1, z_in, z_out, seg, slot, bevel=.012):
    """A curved solid around the seat: inner radius r0 / outer r1, angles a0..a1 (0 = forward, degrees);
    z_in = (bottom, top) at the inner edge, z_out the same at the outer edge (so the top can slope)."""
    bm = bmesh.new()
    rows = []
    for i in range(seg + 1):
        a = a0 + (a1 - a0) * i / seg
        rows.append([bm.verts.new(polar(r0, a, z_in[0])), bm.verts.new(polar(r1, a, z_out[0])),
                     bm.verts.new(polar(r1, a, z_out[1])), bm.verts.new(polar(r0, a, z_in[1]))])
    for i in range(seg):
        a, b = rows[i], rows[i + 1]
        for k in range(4):
            k2 = (k + 1) % 4
            bm.faces.new((a[k], b[k], b[k2], a[k2]))
    bm.faces.new(rows[0][::-1])
    bm.faces.new(rows[-1])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    for p in me.polygons:
        p.use_smooth = True
    ob = bpy.data.objects.new(name, me)
    coll.objects.link(ob)
    if bevel > 0:
        m = ob.modifiers.new("Bevel", "BEVEL")
        m.width = bevel
        m.segments = 3
        m.limit_method = "ANGLE"
        m.harden_normals = True
    parts.append((ob, slot))
    return ob


def tube(name, points, radius, slot, resolution=3):
    cu = bpy.data.curves.new(name, "CURVE")
    cu.dimensions = "3D"
    sp = cu.splines.new("NURBS")
    sp.points.add(len(points) - 1)
    for i, p in enumerate(points):
        sp.points[i].co = (p[0], p[1], p[2], 1)
    sp.use_endpoint_u = True
    sp.order_u = min(4, len(points))
    sp.resolution_u = 10
    cu.bevel_depth = radius
    cu.bevel_resolution = resolution
    cu.use_fill_caps = True
    ob = bpy.data.objects.new(name, cu)
    coll.objects.link(ob)
    parts.append((ob, slot))
    return ob


# ---------------------------------------------------------------- dash
# Inner edge faces the rider; the top slopes down toward the outside.
dash_in, dash_out = (EYE - .52, EYE - .36), (EYE - .54, EYE - .395)
sector("Dash", .50, .74, -58, 58, dash_in, dash_out, 40, "body", .014)
tube("Dash Trim", [polar(.497, a, EYE - .358) for a in range(-56, 57, 8)], .0055, "glowA")
tube("Dash Under Trim", [polar(.505, a, EYE - .505) for a in range(-56, 57, 8)], .004, "glowB")


def dash_top(r):
    t = (r - .50) / .24
    return dash_in[1] + (dash_out[1] - dash_in[1]) * t


# two side screens set into the dash top
for side in (-1, 1):
    a = side * 31
    sector("Screen L" if side < 0 else "Screen R", .565, .675, a - 8, a + 8,
           (dash_top(.565) - .004, dash_top(.565) + .006), (dash_top(.675) - .004, dash_top(.675) + .006), 8, "screen", .004)
    sector("Screen Bezel L" if side < 0 else "Screen Bezel R", .555, .685, a - 9, a + 9,
           (dash_top(.555) - .006, dash_top(.555) + .003), (dash_top(.685) - .006, dash_top(.685) + .003), 8, "body", .003)

# ---------------------------------------------------------------- canopy posts and brow arc
for side in (-1, 1):
    s = "L" if side < 0 else "R"
    post = [polar(.745, side * 61, EYE - .5), polar(.75, side * 61, EYE - .1), polar(.73, side * 59, EYE + .2),
            polar(.68, side * 54, EYE + .4), polar(.62, side * 46, EYE + .47)]
    tube("Post " + s, post, .03, "body")
    tube("Post Trim " + s, [(x * .955, y * .955, z) for (x, y, z) in post[:-1]], .006, "glowB")
brow = [polar(.62, a, EYE + .47 + .03 * math.cos(math.radians(a * 1.8))) for a in range(-46, 47, 8)]
tube("Brow", brow, .02, "body")
tube("Brow Trim", [polar(.60, a, EYE + .455 + .03 * math.cos(math.radians(a * 1.8))) for a in range(-44, 45, 8)], .005, "glowA")

# ---------------------------------------------------------------- arm rests (seen when looking down)
for side in (-1, 1):
    s = "L" if side < 0 else "R"
    a0, a1 = (side * 72, side * 112) if side > 0 else (side * 112, side * 72)
    sector("Arm " + s, .30, .40, a0, a1, (EYE - .62, EYE - .55), (EYE - .64, EYE - .56), 10, "body", .015)
    tube("Arm Trim " + s, [polar(.298, a, EYE - .548) for a in range(min(a0, a1) + 2, max(a0, a1) - 1, 6)], .004, "glowB")

# ---------------------------------------------------------------- export
deps = bpy.context.evaluated_depsgraph_get()
out = {"parts": []}
for ob, slot in parts:
    ev = ob.evaluated_get(deps)
    me = ev.to_mesh()
    me.calc_loop_triangles()
    normals = me.corner_normals
    mw = ob.matrix_world
    nm = mw.to_3x3()
    v, n, t = [], [], []
    for tri in me.loop_triangles:
        idx = []
        for li in tri.loops:
            co = mw @ me.vertices[me.loops[li].vertex_index].co
            no = (nm @ normals[li].vector).normalized()
            v += [round(co.x, 5), round(co.z, 5), round(co.y, 5)]      # Unity: (x, z, y)
            n += [round(no.x, 4), round(no.z, 4), round(no.y, 4)]
            idx.append(len(v) // 3 - 1)
        t += [idx[0], idx[2], idx[1]]                                  # swapping y/z flips the winding
    out["parts"].append({"name": ob.name, "material": slot, "v": v, "n": n, "t": t})
    ev.to_mesh_clear()
with open(out_path, "w") as f:
    json.dump(out, f, separators=(",", ":"))
print("COCKPIT wrote", out_path, sum(len(p["t"]) // 3 for p in out["parts"]), "triangles in", len(out["parts"]), "parts")
