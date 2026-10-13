# 生成 Diva 的图标 SVG（四分之三侧面的机甲大象）。鼻子按中线 + 逐渐变细的宽度算出闭合轮廓，不再用粗描边。
import math

def cubic(p0, p1, p2, p3, t):
    u = 1 - t
    return (u*u*u*p0[0] + 3*u*u*t*p1[0] + 3*u*t*t*p2[0] + t*t*t*p3[0],
            u*u*u*p0[1] + 3*u*u*t*p1[1] + 3*u*t*t*p2[1] + t*t*t*p3[1])

def trunk_path(segments, w0, w1, n=48):
    # segments: 连续的三次贝塞尔；宽度从 w0 线性变到 w1；返回闭合轮廓的 d 和中线采样点
    pts = []
    for (p0, p1, p2, p3) in segments:
        for i in range(n + 1):
            t = i / n
            if pts and i == 0: continue
            pts.append(cubic(p0, p1, p2, p3, t))
    left, right = [], []
    total = len(pts)
    for i, p in enumerate(pts):
        a = pts[max(0, i - 1)]; b = pts[min(total - 1, i + 1)]
        dx, dy = b[0] - a[0], b[1] - a[1]
        l = math.hypot(dx, dy) or 1
        nx, ny = -dy / l, dx / l
        k = i / (total - 1)
        w = (w0 + (w1 - w0) * (k ** 0.9)) / 2
        left.append((p[0] + nx * w, p[1] + ny * w)); right.append((p[0] - nx * w, p[1] - ny * w))
    tip = pts[-1]; tipw = w1 / 2
    d = "M %.1f %.1f " % left[0] + " ".join("L %.1f %.1f" % q for q in left[1:])
    # 鼻尖：半圆收口
    a = pts[-2]; dx, dy = tip[0] - a[0], tip[1] - a[1]; l = math.hypot(dx, dy) or 1
    d += " A %.1f %.1f 0 0 1 %.1f %.1f" % (tipw, tipw, right[-1][0], right[-1][1])
    d += " " + " ".join("L %.1f %.1f" % q for q in reversed(right[:-1])) + " Z"
    return d, pts

# 鼻子中线：从脸的右下方出来，往下往右垂，鼻尖轻轻往上翘（开心的样子）
seg1 = ((586, 560), (596, 690), (650, 790), (735, 812))
seg2 = ((735, 812), (800, 828), (846, 796), (838, 748))
trunk_d, centre = trunk_path([seg1, seg2], 108, 46)
# 褶皱：沿中线每隔一段画一条横跨鼻子的短弧
wrinkles = []
for k in (0.14, 0.28, 0.42, 0.56, 0.70, 0.84):
    i = int(k * (len(centre) - 1)); p = centre[i]; a = centre[i - 1]; b = centre[i + 1]
    dx, dy = b[0] - a[0], b[1] - a[1]; l = math.hypot(dx, dy) or 1; nx, ny = -dy / l, dx / l
    w = (108 + (46 - 108) * (k ** 0.9)) / 2 * 0.78
    wrinkles.append('<path d="M %.1f %.1f Q %.1f %.1f %.1f %.1f"/>' % (p[0] + nx * w, p[1] + ny * w, p[0] + dx / l * 9, p[1] + dy / l * 9, p[0] - nx * w, p[1] - ny * w))
tip = centre[-1]

svg = f'''<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 1024 1024" width="1024" height="1024">
  <defs>
    <clipPath id="squircle">
      <path d="M512 24 C 820 24 1000 204 1000 512 C 1000 820 820 1000 512 1000 C 204 1000 24 820 24 512 C 24 204 204 24 512 24 Z"/>
    </clipPath>
    <linearGradient id="bg" x1="0" y1="0" x2="1" y2="1">
      <stop offset="0" stop-color="#ff9ad6"/><stop offset="0.55" stop-color="#ff55a8"/><stop offset="1" stop-color="#b8228a"/>
    </linearGradient>
    <linearGradient id="sheen" x1="0" y1="0" x2="1" y2="1">
      <stop offset="0" stop-color="#ffffff" stop-opacity="0.32"/><stop offset="0.5" stop-color="#ffffff" stop-opacity="0"/>
    </linearGradient>
    <linearGradient id="skin" x1="0" y1="0" x2="0" y2="1">
      <stop offset="0" stop-color="#ffdcf0"/><stop offset="1" stop-color="#ffa6d8"/>
    </linearGradient>
    <linearGradient id="skinSide" x1="0" y1="0" x2="1" y2="1">
      <stop offset="0" stop-color="#ffc4e5"/><stop offset="1" stop-color="#ff8fcb"/>
    </linearGradient>
    <linearGradient id="skinDark" x1="0" y1="0" x2="0" y2="1">
      <stop offset="0" stop-color="#ff8fcb"/><stop offset="1" stop-color="#e2409a"/>
    </linearGradient>
    <linearGradient id="earInner" x1="0" y1="0" x2="0" y2="1">
      <stop offset="0" stop-color="#ffc2e4"/><stop offset="1" stop-color="#ff7fc6"/>
    </linearGradient>
    <linearGradient id="armor" x1="0" y1="0" x2="0" y2="1">
      <stop offset="0" stop-color="#ffffff"/><stop offset="1" stop-color="#ffc7e6"/>
    </linearGradient>
    <linearGradient id="glow" x1="0" y1="0" x2="1" y2="0">
      <stop offset="0" stop-color="#4dfff0"/><stop offset="1" stop-color="#8cffe6"/>
    </linearGradient>
    <radialGradient id="bubble" cx="0.35" cy="0.3" r="0.75">
      <stop offset="0" stop-color="#ffffff" stop-opacity="0.9"/><stop offset="0.45" stop-color="#ffffff" stop-opacity="0.12"/>
      <stop offset="0.9" stop-color="#8cffe6" stop-opacity="0.55"/><stop offset="1" stop-color="#ffffff" stop-opacity="0.9"/>
    </radialGradient>
    <filter id="soft" x="-20%" y="-20%" width="140%" height="140%"><feGaussianBlur stdDeviation="12"/></filter>
    <filter id="shadow" x="-20%" y="-20%" width="140%" height="140%">
      <feDropShadow dx="0" dy="18" stdDeviation="18" flood-color="#5a0a40" flood-opacity="0.45"/>
    </filter>
  </defs>

  <g clip-path="url(#squircle)">
    <rect width="1024" height="1024" fill="url(#bg)"/>
    <circle cx="512" cy="1180" r="620" fill="#ffffff" fill-opacity="0.10"/>
    <circle cx="512" cy="1140" r="520" fill="none" stroke="#8cffe6" stroke-opacity="0.55" stroke-width="22"/>
    <circle cx="512" cy="1140" r="470" fill="none" stroke="#ffe066" stroke-opacity="0.45" stroke-width="16"/>
    <rect width="1024" height="1024" fill="url(#sheen)"/>

    <!-- 飘着的泡泡（泡泡炮） -->
    <g>
      <circle cx="150" cy="210" r="58" fill="url(#bubble)"/>
      <circle cx="238" cy="118" r="30" fill="url(#bubble)"/>
      <circle cx="880" cy="170" r="46" fill="url(#bubble)"/>
      <circle cx="930" cy="300" r="26" fill="url(#bubble)"/>
      <circle cx="110" cy="640" r="24" fill="url(#bubble)"/>
    </g>

    <!-- 大象（四分之三侧面，脸朝右）：左边一只大扇耳、右后方露一点远耳，圆脑袋，鼻子从脸的右下垂下来、鼻尖轻轻上翘，一颗近侧象牙 -->
    <g filter="url(#shadow)">
      <!-- 耳朵：远耳（头后面）和近耳（大扇形） -->
      <path d="M690 360 C 760 330 830 370 836 450 C 840 530 790 580 724 566 Z" fill="url(#skinDark)"/>
      <path d="M702 388 C 752 368 800 400 804 456 C 806 512 772 548 730 538 Z" fill="url(#earInner)"/>
      <path d="M330 320 C 180 270 70 400 96 560 C 116 690 242 740 338 666 Z" fill="url(#skinDark)"/>
      <path d="M338 366 C 230 334 150 430 170 556 C 186 650 272 694 336 636 Z" fill="url(#earInner)"/>
      <!-- 头：四分之三侧面的蛋形，额头宽、脸往右下收 -->
      <path d="M300 440 C 300 320 400 246 520 250 C 660 254 742 350 734 470 C 728 590 646 672 530 676 C 408 680 300 600 300 480 Z" fill="url(#skin)"/>
      <path d="M560 262 C 666 276 742 360 734 470 C 728 590 646 672 530 676 C 600 640 650 560 652 470 C 654 380 620 300 560 262 Z" fill="url(#skinSide)" fill-opacity="0.55"/>
      <!-- 额头的机甲装甲片 + 兔耳小天线 + 装甲下沿的青光 -->
      <path d="M318 408 C 350 312 600 262 716 392 C 700 416 340 432 318 408 Z" fill="url(#armor)"/>
      <path d="M334 404 Q 530 346 700 396" fill="none" stroke="#ff7fc6" stroke-opacity="0.8" stroke-width="8" stroke-linecap="round"/>
      <path d="M456 290 C 428 232 420 190 444 146 C 474 186 488 240 480 282 Z" fill="url(#skinDark)"/>
      <path d="M576 276 C 604 220 612 178 590 134 C 560 174 548 230 554 270 Z" fill="url(#skinDark)"/>
      <path d="M350 426 Q 530 372 696 414" fill="none" stroke="url(#glow)" stroke-width="10" stroke-linecap="round"/>
      <path d="M350 426 Q 530 372 696 414" fill="none" stroke="#4dfff0" stroke-opacity="0.6" stroke-width="18" stroke-linecap="round" filter="url(#soft)"/>
      <!-- 眼睛：近眼大、远眼小，青色发光的虹膜，白高光 -->
      <ellipse cx="440" cy="486" rx="42" ry="48" fill="#1f2340"/>
      <ellipse cx="626" cy="478" rx="26" ry="34" fill="#1f2340"/>
      <circle cx="444" cy="494" r="24" fill="url(#glow)"/>
      <circle cx="628" cy="484" r="15" fill="url(#glow)"/>
      <circle cx="444" cy="494" r="24" fill="#4dfff0" fill-opacity="0.5" filter="url(#soft)"/>
      <circle cx="430" cy="470" r="12" fill="#ffffff"/>
      <circle cx="620" cy="466" r="7" fill="#ffffff"/>
      <!-- 腮红 -->
      <ellipse cx="372" cy="566" rx="34" ry="22" fill="#ff8fcb" fill-opacity="0.55"/>
      <!-- 近侧的象牙 -->
      <path d="M520 606 C 486 650 482 700 508 736 C 500 696 510 652 540 618 Z" fill="#fff6e9"/>
      <!-- 鼻子：闭合的渐细轮廓 + 褶皱 -->
      <path d="{trunk_d}" fill="url(#skin)"/>
      <path d="{trunk_d}" fill="url(#skinSide)" fill-opacity="0.35"/>
      <g fill="none" stroke="#e2409a" stroke-opacity="0.32" stroke-width="8" stroke-linecap="round">
        {"".join(wrinkles)}
      </g>
      <!-- 鼻尖前的一滴水 -->
      <circle cx="{tip[0] + 34:.0f}" cy="{tip[1] - 46:.0f}" r="26" fill="url(#bubble)"/>
      <circle cx="{tip[0] + 34:.0f}" cy="{tip[1] - 46:.0f}" r="26" fill="#8cffe6" fill-opacity="0.35"/>
    </g>

    <!-- 底部的小标 DIVA -->
    <text x="512" y="958" text-anchor="middle" font-family="Helvetica Neue, Arial, sans-serif" font-weight="900" font-size="74" letter-spacing="14" fill="#ffffff" fill-opacity="0.92" font-style="italic">DIVA</text>
  </g>
</svg>
'''
open('diva_icon.svg', 'w', encoding='utf-8').write(svg)
print('svg written; trunk tip at', tip)
