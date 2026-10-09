"""Candy-pink version of the elephant diffuse texture: tint only the skin, keep tusks and eyes as they are.

Usage: python make_candy_texture.py Elephant_D.png out.jpg
The tusk and eye UV islands sit in the outer columns of the lower half of the layout (u < .135 or u > .865, v < .48).
"""
import sys
import numpy as np
import cv2

src, out = sys.argv[1:3]
img = cv2.imread(src, cv2.IMREAD_COLOR).astype(np.float32) / 255  # BGR
h, w = img.shape[:2]
u = (np.arange(w) + .5) / w
v = 1 - (np.arange(h) + .5) / h
U, V = np.meshgrid(u, v)
keep = ((U < .135) | (U > .865)) & (V < .48)
mask = (~keep).astype(np.float32)
mask = cv2.GaussianBlur(mask, (0, 0), w / 400)[..., None]
tint = np.array([1.0, .90, 1.12], np.float32)          # BGR multiplier: a little more red/blue, less green
lift = np.array([.02, .0, .025], np.float32)           # a touch lighter so it reads as candy, not dusky
candy = np.clip(img * tint + lift, 0, 1)
res = img * (1 - mask) + candy * mask
cv2.imwrite(out, (res * 255 + .5).astype(np.uint8), [cv2.IMWRITE_JPEG_QUALITY, 90])
print('ok', out, img.shape)
