# 生成 Diva 的图标 SVG：Q 版（chibi）机甲大象，紫晶夜色配色（Eva 的 amethyst 涂装：紫 + 黑 + 金，橙色灯光）——大圆脑袋、圆耳朵、挨得近的大眼睛、短短胖胖往上卷的小鼻子、腮红，没有象牙。
import math

def cubic(p0, p1, p2, p3, t):
    u = 1 - t
    return (u*u*u*p0[0] + 3*u*u*t*p1[0] + 3*u*t*t*p2[0] + t*t*t*p3[0],
            u*u*u*p0[1] + 3*u*u*t*p1[1] + 3*u*t*t*p2[1] + t*t*t*p3[1])

def trunk_path(segments, w0, w1, n=48):
    pts = []
    for (p0, p1, p2, p3) in segments:
        for i in range(n + 1):
            if pts and i == 0: continue
            pts.append(cubic(p0, p1, p2, p3, i / n))
    left, right = [], []
    total = len(pts)
    for i, p in enumerate(pts):
        a = pts[max(0, i - 1)]; b = pts[min(total - 1, i + 1)]
        dx, dy = b[0] - a[0], b[1] - a[1]
        l = math.hypot(dx, dy) or 1
        nx, ny = -dy / l, dx / l
        k = i / (total - 1)
        w = (w0 + (w1 - w0) * (k ** 0.8)) / 2
        left.append((p[0] + nx * w, p[1] + ny * w)); right.append((p[0] - nx * w, p[1] - ny * w))
    tipw = w1 / 2
    d = "M %.1f %.1f " % left[0] + " ".join("L %.1f %.1f" % q for q in left[1:])
    d += " A %.1f %.1f 0 0 1 %.1f %.1f" % (tipw, tipw, right[-1][0], right[-1][1])
    d += " " + " ".join("L %.1f %.1f" % q for q in reversed(right[:-1])) + " Z"
    return d, pts

# 短胖的小鼻子：从脸的下中部出来，往下一点点就往右上卷
seg1 = ((512, 596), (508, 660), (530, 712), (574, 724))
seg2 = ((574, 724), (616, 736), (646, 710), (636, 676))
trunk_d, centre = trunk_path([seg1, seg2], 96, 54)
wrinkles = []
for k in (0.22, 0.42, 0.62):
    i = int(k * (len(centre) - 1)); p = centre[i]; a = centre[i - 1]; b = centre[i + 1]
    dx, dy = b[0] - a[0], b[1] - a[1]; l = math.hypot(dx, dy) or 1; nx, ny = -dy / l, dx / l
    w = (96 + (54 - 96) * (k ** 0.8)) / 2 * 0.7
    wrinkles.append('<path d="M %.1f %.1f Q %.1f %.1f %.1f %.1f"/>' % (p[0] + nx * w, p[1] + ny * w, p[0] + dx / l * 7, p[1] + dy / l * 7, p[0] - nx * w, p[1] - ny * w))
tip = centre[-1]

svg = f'''<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 1024 1024" width="1024" height="1024">
  <defs>
    <clipPath id="squircle">
      <path d="M512 24 C 820 24 1000 204 1000 512 C 1000 820 820 1000 512 1000 C 204 1000 24 820 24 512 C 24 204 204 24 512 24 Z"/>
    </clipPath>
    <linearGradient id="bg" x1="0" y1="0" x2="1" y2="1">
      <stop offset="0" stop-color="#b48cff"/><stop offset="0.55" stop-color="#7a3fd6"/><stop offset="1" stop-color="#2f1458"/>
    </linearGradient>
    <linearGradient id="sheen" x1="0" y1="0" x2="1" y2="1">
      <stop offset="0" stop-color="#ffffff" stop-opacity="0.32"/><stop offset="0.5" stop-color="#ffffff" stop-opacity="0"/>
    </linearGradient>
    <linearGradient id="skin" x1="0" y1="0" x2="0" y2="1">
      <stop offset="0" stop-color="#efe8ff"/><stop offset="1" stop-color="#c9b6f2"/>
    </linearGradient>
    <linearGradient id="skinDark" x1="0" y1="0" x2="0" y2="1">
      <stop offset="0" stop-color="#a27ae6"/><stop offset="1" stop-color="#6e34b8"/>
    </linearGradient>
    <linearGradient id="earInner" x1="0" y1="0" x2="0" y2="1">
      <stop offset="0" stop-color="#d9c8ff"/><stop offset="1" stop-color="#a985ea"/>
    </linearGradient>
    <linearGradient id="armor" x1="0" y1="0" x2="0" y2="1">
      <stop offset="0" stop-color="#ffffff"/><stop offset="1" stop-color="#dcd0f5"/>
    </linearGradient>
    <linearGradient id="glow" x1="0" y1="0" x2="1" y2="0">
      <stop offset="0" stop-color="#ff9a2e"/><stop offset="1" stop-color="#ffd58a"/>
    </linearGradient>
    <radialGradient id="iris" cx="0.5" cy="0.45" r="0.6">
      <stop offset="0" stop-color="#ffe9bd"/><stop offset="0.55" stop-color="#ffb347"/><stop offset="1" stop-color="#d9711a"/>
    </radialGradient>
    <radialGradient id="bubble" cx="0.35" cy="0.3" r="0.75">
      <stop offset="0" stop-color="#ffffff" stop-opacity="0.9"/><stop offset="0.45" stop-color="#ffffff" stop-opacity="0.12"/>
      <stop offset="0.9" stop-color="#ffd58a" stop-opacity="0.6"/><stop offset="1" stop-color="#ffffff" stop-opacity="0.9"/>
    </radialGradient>
    <filter id="soft" x="-20%" y="-20%" width="140%" height="140%"><feGaussianBlur stdDeviation="12"/></filter>
    <filter id="shadow" x="-20%" y="-20%" width="140%" height="140%">
      <feDropShadow dx="0" dy="18" stdDeviation="18" flood-color="#1a0d33" flood-opacity="0.42"/>
    </filter>
  </defs>

  <g clip-path="url(#squircle)">
    <rect width="1024" height="1024" fill="url(#bg)"/>
    <circle cx="512" cy="1180" r="620" fill="#ffffff" fill-opacity="0.10"/>
    <circle cx="512" cy="1140" r="520" fill="none" stroke="#ffb347" stroke-opacity="0.55" stroke-width="22"/>
    <circle cx="512" cy="1140" r="470" fill="none" stroke="#c9b6f2" stroke-opacity="0.45" stroke-width="16"/>
    <rect width="1024" height="1024" fill="url(#sheen)"/>

    <!-- 飘着的泡泡（泡泡炮） -->
    <g>
      <circle cx="150" cy="210" r="58" fill="url(#bubble)"/>
      <circle cx="238" cy="118" r="30" fill="url(#bubble)"/>
      <circle cx="880" cy="170" r="46" fill="url(#bubble)"/>
      <circle cx="930" cy="300" r="26" fill="url(#bubble)"/>
      <circle cx="110" cy="640" r="24" fill="url(#bubble)"/>
      <circle cx="900" cy="700" r="20" fill="url(#bubble)"/>
    </g>

    <!-- 大象（Q 版正面）：圆耳朵贴在大圆脑袋两边，大眼睛，短鼻子往上卷 -->
    <g filter="url(#shadow)">
      <!-- 耳朵：圆圆的，大 -->
      <circle cx="262" cy="500" r="168" fill="url(#skinDark)"/>
      <circle cx="762" cy="500" r="168" fill="url(#skinDark)"/>
      <circle cx="268" cy="508" r="122" fill="url(#earInner)"/>
      <circle cx="756" cy="508" r="122" fill="url(#earInner)"/>
      <!-- 头：大圆，脸颊微微鼓 -->
      <path d="M512 262 C 668 262 776 372 776 512 C 776 650 664 744 512 744 C 360 744 248 650 248 512 C 248 372 356 262 512 262 Z" fill="url(#skin)"/>
      <!-- 额头的小头盔（只盖头顶）+ 兔耳天线 + 头盔边缘一道青光 -->
      <path d="M300 430 C 330 300 694 300 724 430 C 700 448 324 448 300 430 Z" fill="url(#armor)"/>
      <path d="M318 426 Q 512 376 706 426" fill="none" stroke="#a27ae6" stroke-opacity="0.8" stroke-width="8" stroke-linecap="round"/>
      <path d="M436 318 C 404 262 396 212 420 160 C 452 206 466 262 460 308 Z" fill="url(#skinDark)"/>
      <path d="M588 318 C 620 262 628 212 604 160 C 572 206 558 262 564 308 Z" fill="url(#skinDark)"/>
      <path d="M336 446 Q 512 400 688 446" fill="none" stroke="url(#glow)" stroke-width="10" stroke-linecap="round"/>
      <path d="M336 446 Q 512 400 688 446" fill="none" stroke="#ff9a2e" stroke-opacity="0.55" stroke-width="20" stroke-linecap="round" filter="url(#soft)"/>
      <!-- 眼睛：大、圆、挨得近；青色虹膜，两点高光 -->
      <ellipse cx="446" cy="520" rx="56" ry="62" fill="#1f1a26"/>
      <ellipse cx="578" cy="520" rx="56" ry="62" fill="#1f1a26"/>
      <circle cx="450" cy="530" r="34" fill="url(#iris)"/>
      <circle cx="582" cy="530" r="34" fill="url(#iris)"/>
      <circle cx="428" cy="496" r="16" fill="#ffffff"/>
      <circle cx="560" cy="496" r="16" fill="#ffffff"/>
      <circle cx="468" cy="550" r="7" fill="#ffffff" fill-opacity="0.9"/>
      <circle cx="600" cy="550" r="7" fill="#ffffff" fill-opacity="0.9"/>
      <!-- 腮红 -->
      <ellipse cx="352" cy="600" rx="40" ry="24" fill="#e08ad8" fill-opacity="0.55"/>
      <ellipse cx="672" cy="600" rx="40" ry="24" fill="#e08ad8" fill-opacity="0.55"/>
      <!-- 小笑脸：鼻子两边各一小截嘴角 -->
      <path d="M452 626 Q 468 648 490 640" fill="none" stroke="#6e34b8" stroke-opacity="0.75" stroke-width="7" stroke-linecap="round"/>
      <path d="M572 626 Q 556 648 534 640" fill="none" stroke="#6e34b8" stroke-opacity="0.75" stroke-width="7" stroke-linecap="round"/>
      <!-- 鼻子：短胖、往上卷 -->
      <path d="{trunk_d}" fill="url(#skin)"/>
      <g fill="none" stroke="#6e34b8" stroke-opacity="0.3" stroke-width="7" stroke-linecap="round">
        {"".join(wrinkles)}
      </g>
      <!-- 鼻尖吹出来的小泡泡 -->
      <circle cx="{tip[0] + 44:.0f}" cy="{tip[1] - 52:.0f}" r="26" fill="url(#bubble)"/>
      <circle cx="{tip[0] + 44:.0f}" cy="{tip[1] - 52:.0f}" r="26" fill="#ffd58a" fill-opacity="0.3"/>
      <circle cx="{tip[0] + 78:.0f}" cy="{tip[1] - 94:.0f}" r="14" fill="url(#bubble)"/>
    </g>

    <!-- 底部的小标 DIVA -->
    <text x="512" y="958" text-anchor="middle" font-family="Helvetica Neue, Arial, sans-serif" font-weight="900" font-size="74" letter-spacing="14" fill="#ffffff" fill-opacity="0.92" font-style="italic">DIVA</text>
  </g>
</svg>
'''
open('diva_icon.svg', 'w', encoding='utf-8').write(svg)
print('svg written; trunk tip at', tip)
