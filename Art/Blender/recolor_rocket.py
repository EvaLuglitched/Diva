import bpy, sys, numpy as np
args = sys.argv[sys.argv.index('--') + 1:]
src_tex, fbx, out_dir = args
# --- Recolour the base colour atlas into the Diva candy palette (sRGB 0..1) ---
img = bpy.data.images.load(src_tex)
w, h = img.size
px = np.empty(w * h * 4, dtype=np.float32); img.pixels.foreach_get(px); px = px.reshape(h, w, 4)
rgb = px[..., :3]; mx = rgb.max(-1); mn = rgb.min(-1); sat = (mx - mn) / np.maximum(mx, 1e-4)
red = (rgb[..., 0] > .5) & (sat > .5)
dark = (~red) & (mx > .08) & (mx < .32)
grey = (~red) & (sat < .2) & (mx >= .5) & (mx < .78)
light = (~red) & (sat < .2) & (mx >= .78)
# Rivet rows: small light dots in two UV bands (image rows are bottom-up in Blender).
v = np.arange(h)[:, None] / h; u = np.arange(w)[None, :] / w
rivets = light & (((v > .385) & (v < .43) & (u < .66)) | ((u > .82) & (u < .88) & (v < .27)))
def put(mask, colour): rgb[mask] = colour
put(red, (1, .40, .68))            # hot pink nose and fins
put(light, (1, .95, .97))           # pinkish white body
put(rivets, (.50, .90, .85))        # mint rivets (the stand-in's mint stripes)
put(grey, (.94, .92, .99))          # pearl lilac metal
put(dark, (1, .72, .88))            # pink glass portholes
px[..., :3] = rgb
out = bpy.data.images.new('rocket_candy_albedo', w, h, alpha=False)
out.pixels.foreach_set(px.ravel()); out.filepath_raw = out_dir + '/rocket_candy_albedo.png'; out.file_format = 'PNG'; out.save()
em = np.zeros_like(px); em[..., 3] = 1; em[dark, 0] = 1; em[dark, 1] = .55; em[dark, 2] = .82
eo = bpy.data.images.new('rocket_candy_emission', w, h, alpha=False)
eo.pixels.foreach_set(em.ravel()); eo.filepath_raw = out_dir + '/rocket_candy_emission.png'; eo.file_format = 'PNG'; eo.save()
print('RECOLOUR', {k: int(m.sum()) for k, m in dict(red=red, light=light, rivets=rivets, grey=grey, dark=dark).items()})
# --- Which end of the rocket is the nose? The narrower cross-section. ---
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=fbx)
o = next(o for o in bpy.data.objects if o.type == 'MESH')
co = np.array([(o.matrix_world @ vv.co)[:] for vv in o.data.vertices])
ax = int(np.argmax(co.max(0) - co.min(0)))
lo, hi = co[:, ax].min(), co[:, ax].max(); span = hi - lo
other = [i for i in range(3) if i != ax]
def width(sel): s = co[sel][:, other]; return float((s.max(0) - s.min(0)).max()) if len(s) else 0
print('NOSE', 'axis', 'xyz'[ax], 'width_low', round(width(co[:, ax] < lo + .1 * span), 3), 'width_high', round(width(co[:, ax] > hi - .1 * span), 3))
