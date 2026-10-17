"""Generate the rug texture (rug.png) and wood floor wallpaper (wood.jpg).

Run: python3 tools/make_assets.py
"""
import math
import os

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

OUT = os.path.join(os.path.dirname(__file__), "..", "rug")
rng = np.random.default_rng(7)

# Vintage Persian palette: faded silver-grey field, navy accents, ivory highlights.
IVORY = (222, 216, 204)
SILVER = (182, 184, 188)
GREY = (138, 142, 150)
SLATE = (102, 108, 120)
NAVY = (38, 48, 72)
BLUSH = (196, 170, 160)
FRINGE = (232, 226, 210)


def lerp(a, b, t):
    return tuple(int(a[i] + (b[i] - a[i]) * t) for i in range(3))


def petal(d, cx, cy, ang, length, width, col):
    pts = []
    for k in range(13):
        t = k / 12
        r = length * t
        w = width * math.sin(math.pi * t)
        pts.append((r, w))
    pts += [(r, -w) for r, w in reversed(pts)]
    ca, sa = math.cos(ang), math.sin(ang)
    d.polygon([(cx + x * ca - y * sa, cy + x * sa + y * ca) for x, y in pts], fill=col)


def rosette(d, cx, cy, r, petals, col, centre, inner=None):
    for k in range(petals):
        petal(d, cx, cy, k * 2 * math.pi / petals, r, r * 0.32, col)
    if inner:
        for k in range(petals):
            petal(d, cx, cy, (k + 0.5) * 2 * math.pi / petals, r * 0.6, r * 0.2, inner)
    d.ellipse([cx - r * 0.28, cy - r * 0.28, cx + r * 0.28, cy + r * 0.28], fill=centre)


def vine(d, x0, y0, x1, y1, bend, col, width, leaves, rnd):
    pts = []
    for k in range(25):
        t = k / 24
        x = x0 + (x1 - x0) * t + math.sin(t * math.pi) * bend * (y1 - y0) / 100
        y = y0 + (y1 - y0) * t - math.sin(t * math.pi) * bend * (x1 - x0) / 100
        pts.append((x, y))
    d.line(pts, fill=col, width=width)
    for k in range(2, 24, max(2, 24 // max(1, leaves))):
        (xa, ya), (xb, yb) = pts[k], pts[k + 1]
        ang = math.atan2(yb - ya, xb - xa) + (1 if k % 4 else -1) * 0.9
        petal(d, xa, ya, ang, width * 5, width * 1.6, col)


def make_rug(W=960, H=640):
    S = 2                                   # draw at 2x, then downsample
    w, h = W * S, H * S
    img = Image.new("RGB", (w, h), GREY)
    d = ImageDraw.Draw(img)
    rnd = np.random.default_rng(11)

    # Borders, outside in.
    bands = [(0, SLATE), (8, IVORY), (22, SILVER), (118, IVORY), (128, SLATE), (140, IVORY), (148, GREY)]
    for inset, col in bands:
        d.rectangle([inset, inset, w - 1 - inset, h - 1 - inset], fill=col)

    # Main border: a row of faded rosette discs on silver, linked by little vines.
    step = 104
    for edge in range(4):
        horiz = edge < 2
        length = w if horiz else h
        for k in range(int(70), length - 60, step):
            if horiz:
                cx, cy = k, 70 if edge == 0 else h - 71
            else:
                cx, cy = 70 if edge == 2 else w - 71, k
            if not horiz and (k < 120 or k > h - 120):
                continue
            d.ellipse([cx - 40, cy - 40, cx + 40, cy + 40], fill=GREY)
            rosette(d, cx, cy, 34, 8, SILVER, SLATE, inner=IVORY)
            if horiz:
                vine(d, cx + 40, cy, cx + step - 40, cy, 25, SLATE, 3, 3, rnd)
            else:
                vine(d, cx, cy + 40, cx, cy + step - 40, 25, SLATE, 3, 3, rnd)

    # Field: scattered flowers and vines, low contrast like a worn rug.
    fx0, fy0, fx1, fy1 = 150, 150, w - 151, h - 151
    for _ in range(650):
        x0, y0 = rnd.uniform(fx0, fx1), rnd.uniform(fy0, fy1)
        ang = rnd.uniform(0, 2 * math.pi)
        L = rnd.uniform(40, 120)
        vine(d, x0, y0, x0 + math.cos(ang) * L, y0 + math.sin(ang) * L, rnd.uniform(-40, 40),
             lerp(GREY, SILVER, rnd.uniform(0.15, 0.55)), 2, 4, rnd)
    for _ in range(700):
        x, y = rnd.uniform(fx0 + 10, fx1 - 10), rnd.uniform(fy0 + 10, fy1 - 10)
        r = rnd.uniform(6, 16)
        rosette(d, x, y, r, int(rnd.integers(5, 9)), lerp(GREY, IVORY, rnd.uniform(0.25, 0.7)),
                lerp(GREY, SLATE, rnd.uniform(0.3, 1)))

    # Corner spandrels: navy quarter-medallions with ivory filigree.
    cw, ch = 440, 300
    for sx in (0, 1):
        for sy in (0, 1):
            ox = fx0 if sx == 0 else fx1
            oy = fy0 if sy == 0 else fy1
            box = [ox - cw, oy - ch, ox + cw, oy + ch]
            corner = Image.new("L", (w, h), 0)
            ImageDraw.Draw(corner).ellipse(box, fill=255)
            field = Image.new("L", (w, h), 0)
            ImageDraw.Draw(field).rectangle([fx0, fy0, fx1, fy1], fill=255)
            mask = Image.fromarray(np.minimum(np.asarray(corner), np.asarray(field)))
            layer = Image.new("RGB", (w, h), NAVY)
            ld = ImageDraw.Draw(layer)
            dx, dy = (1 if sx == 0 else -1), (1 if sy == 0 else -1)
            for _ in range(180):
                rr = rnd.uniform(0.15, 0.95)
                ang = rnd.uniform(0, math.pi / 2)
                x = ox + dx * math.cos(ang) * cw * rr
                y = oy + dy * math.sin(ang) * ch * rr
                vine(ld, x, y, x + dx * rnd.uniform(-50, 50), y + dy * rnd.uniform(-50, 50), 30,
                     lerp(NAVY, SILVER, rnd.uniform(0.35, 0.6)), 2, 3, rnd)
            for _ in range(70):
                rr = rnd.uniform(0.2, 0.9)
                ang = rnd.uniform(0, math.pi / 2)
                rosette(ld, ox + dx * math.cos(ang) * cw * rr, oy + dy * math.sin(ang) * ch * rr,
                        rnd.uniform(6, 14), 6, lerp(NAVY, IVORY, rnd.uniform(0.5, 0.75)), NAVY)
            ld.ellipse(box, outline=IVORY, width=5)
            img.paste(layer, (0, 0), mask)
    d = ImageDraw.Draw(img)
    d.rectangle([fx0, fy0, fx1, fy1], outline=IVORY, width=4)

    # Central medallion: concentric rings of petals.
    cx, cy = w / 2, h / 2
    d.ellipse([cx - 300, cy - 300, cx + 300, cy + 300], fill=lerp(GREY, SILVER, 0.4))
    d.ellipse([cx - 270, cy - 270, cx + 270, cy + 270], fill=IVORY)
    for ring, n, rr, col in [(240, 40, 34, SILVER), (190, 32, 30, lerp(SILVER, IVORY, 0.4)), (140, 24, 26, GREY)]:
        for k in range(n):
            a = k * 2 * math.pi / n
            rosette(d, cx + math.cos(a) * ring, cy + math.sin(a) * ring, rr * 0.55, 6, col, SLATE)
    d.ellipse([cx - 110, cy - 110, cx + 110, cy + 110], fill=lerp(GREY, SLATE, 0.3))
    rosette(d, cx, cy, 95, 12, SILVER, BLUSH, inner=IVORY)
    rosette(d, cx, cy, 40, 8, BLUSH, NAVY)
    # Pendant ovals on the long axis.
    for sg in (-1, 1):
        px = cx + sg * 420
        d.ellipse([px - 70, cy - 42, px + 70, cy + 42], fill=NAVY)
        rosette(d, px, cy, 34, 8, lerp(NAVY, IVORY, 0.7), BLUSH)

    return finish_rug(img, W, H, IVORY, FRINGE, rnd)


def finish_rug(img, W, H, warp, fringe, rnd, wear_amt=0.28, grain=7):
    """Downsample a 2x drawing, age it (wear, abrash, wool grain) and add fringe."""
    img = img.resize((W, H), Image.LANCZOS)
    a = np.asarray(img).astype(np.float32)
    wear = fbm(H, W, 32, 4, rnd)
    wear = np.clip((wear + 0.1) * 2.5, 0, 1)[..., None] * wear_amt
    a = a * (1 - wear) + np.array(warp, np.float32) * wear
    abrash = fbm_long(H, W, 40, 3, rnd, stretch=6)[..., None] * np.array([6, 5, 9])
    a += abrash
    a += rnd.normal(0, grain, (H, W, 1))
    yy, xx = np.mgrid[0:H, 0:W]
    a *= (1 + np.sin(xx * 2.2) * np.sin(yy * 2.2) * 0.025)[..., None]
    img = Image.fromarray(np.clip(a, 0, 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(0.45))

    F = 28
    out = Image.new("RGBA", (W + 2 * F, H), (0, 0, 0, 0))
    out.paste(img, (F, 0))
    fd = ImageDraw.Draw(out)
    r = np.random.default_rng(5)
    for y in range(3, H - 3, 4):
        for side in (0, 1):
            x0 = F if side == 0 else F + W - 1
            length = F - 2 - int(r.integers(0, 7))
            x1 = x0 - length if side == 0 else x0 + length
            wob = int(r.integers(-2, 3))
            sh = int(r.integers(-14, 6))
            col = tuple(int(c) + sh for c in fringe) + (255,)
            fd.line([(x0, y), (x1, y + wob)], fill=col, width=2)
    return out


def make_rug_classic(W=960, H=640):
    """Classic red Persian with a navy border and a lobed central medallion."""
    RED, DARK_RED, NAVY_, CREAM, GOLD, TEAL = (128, 22, 26), (88, 14, 18), (24, 30, 58), (226, 206, 168), (196, 146, 70), (40, 82, 88)
    S = 2
    w, h = W * S, H * S
    img = Image.new("RGB", (w, h), RED)
    d = ImageDraw.Draw(img)
    for inset, col in [(0, DARK_RED), (20, CREAM), (32, NAVY_), (128, CREAM), (140, DARK_RED), (152, GOLD), (160, RED)]:
        d.rectangle([inset, inset, w - 1 - inset, h - 1 - inset], fill=col)

    def motif(cx, cy, r):
        d.polygon([(cx, cy - r), (cx + r, cy), (cx, cy + r), (cx - r, cy)], fill=RED)
        d.polygon([(cx, cy - r * .6), (cx + r * .6, cy), (cx, cy + r * .6), (cx - r * .6, cy)], fill=CREAM)
        d.ellipse([cx - r * .25, cy - r * .25, cx + r * .25, cy + r * .25], fill=TEAL)
    for x in range(80, w - 60, 96):
        motif(x, 80, 34); motif(x, h - 81, 34)
    for y in range(176, h - 120, 96):
        motif(80, y, 34); motif(w - 81, y, 34)
    fx0, fy0, fx1, fy1 = 164, 164, w - 165, h - 165
    for y in range(fy0 + 28, fy1 - 12, 56):
        off = 28 if (y // 56) % 2 else 0
        for x in range(fx0 + 28 + off, fx1 - 12, 56):
            d.polygon([(x, y - 12), (x + 12, y), (x, y + 12), (x - 12, y)], fill=lerp(DARK_RED, NAVY_, 0.4))
            d.ellipse([x - 4, y - 4, x + 4, y + 4], fill=GOLD)
    cw, ch = 420, 300
    for sx in (0, 1):
        for sy in (0, 1):
            ox = fx0 if sx == 0 else fx1; oy = fy0 if sy == 0 else fy1
            dx = 1 if sx == 0 else -1; dy = 1 if sy == 0 else -1
            d.polygon([(ox, oy), (ox + dx * cw, oy), (ox + dx * cw * .55, oy + dy * ch * .45),
                       (ox + dx * cw * .3, oy + dy * ch * .55), (ox, oy + dy * ch)], fill=NAVY_)
    cx, cy = w / 2, h / 2

    def lobed(rx, ry, lobes, amp, col):
        pts = []
        for i in range(360):
            t = i / 360 * 2 * math.pi
            base = 1 / (abs(math.cos(t)) + abs(math.sin(t))) ** 0.75
            r = base * (1 + amp * math.cos(lobes * t))
            pts.append((cx + math.cos(t) * rx * r, cy + math.sin(t) * ry * r))
        d.polygon(pts, fill=col)
    for args in [(500, 350, 16, .06, NAVY_), (456, 316, 16, .06, GOLD), (440, 304, 16, .06, DARK_RED),
                 (320, 224, 12, .08, NAVY_), (240, 168, 8, .1, CREAM), (192, 132, 8, .1, RED), (104, 72, 4, .12, NAVY_)]:
        lobed(*args)
    d.ellipse([cx - 28, cy - 28, cx + 28, cy + 28], fill=GOLD)
    for sg in (-1, 1):
        px = cx + sg * 600
        d.polygon([(px, cy - 68), (px + sg * 92, cy), (px, cy + 68), (px - sg * 60, cy)], fill=NAVY_)
        d.polygon([(px, cy - 32), (px + sg * 44, cy), (px, cy + 32), (px - sg * 28, cy)], fill=CREAM)
    return finish_rug(img, W, H, CREAM, (230, 214, 178), np.random.default_rng(12), wear_amt=0.12, grain=9)


def make_rug_kilim(W=960, H=640):
    """Flat-woven kilim: bands of stepped diamonds in terracotta, ochre and indigo."""
    TERRA, OCHRE, INDIGO, CREAM, BROWN = (176, 82, 52), (204, 152, 72), (52, 62, 102), (232, 220, 196), (92, 56, 40)
    S = 2
    w, h = W * S, H * S
    img = Image.new("RGB", (w, h), CREAM)
    d = ImageDraw.Draw(img)
    d.rectangle([0, 0, w, h], fill=BROWN)
    d.rectangle([24, 24, w - 25, h - 25], fill=CREAM)
    band_cols = [TERRA, INDIGO, OCHRE, TERRA, INDIGO]
    bh = (h - 48) / 5
    for b, col in enumerate(band_cols):
        y0 = 24 + b * bh
        d.rectangle([24, y0, w - 25, y0 + bh], fill=col)
        d.rectangle([24, y0, w - 25, y0 + 10], fill=CREAM)
        # Stepped diamonds (stair-stepped edges, as weaving makes them).
        cy = y0 + bh / 2
        step = 16
        for k, x in enumerate(range(140, w - 100, 220)):
            inner = [CREAM, BROWN, OCHRE if col != OCHRE else TERRA][k % 3]
            for lvl, c in [(7, CREAM), (5, inner), (3, col), (1, CREAM)]:
                for i in range(-lvl, lvl + 1):
                    half = (lvl - abs(i)) * step
                    d.rectangle([x - half - step / 2, cy + i * step - step / 2, x + half + step / 2, cy + i * step + step / 2], fill=c)
        for x in range(30, w - 30, 40):
            d.polygon([(x, y0 + 10), (x + 20, y0 + 26), (x + 40, y0 + 10)], fill=BROWN)
    return finish_rug(img, W, H, CREAM, (224, 210, 184), np.random.default_rng(13), wear_amt=0.14, grain=10)


def lantern(d, cx, cy, s, body, glass, glow):
    """A Ramadan fanous seen flat: cap, ring, faceted glass body, base."""
    d.ellipse([cx - 4 * s, cy - 30 * s, cx + 4 * s, cy - 22 * s], outline=body, width=max(1, int(2 * s)))
    d.polygon([(cx - 8 * s, cy - 22 * s), (cx + 8 * s, cy - 22 * s), (cx + 14 * s, cy - 14 * s), (cx - 14 * s, cy - 14 * s)], fill=body)
    d.polygon([(cx - 14 * s, cy - 14 * s), (cx + 14 * s, cy - 14 * s), (cx + 18 * s, cy + 6 * s), (cx, cy + 22 * s), (cx - 18 * s, cy + 6 * s)], fill=body)
    d.polygon([(cx - 10 * s, cy - 10 * s), (cx + 10 * s, cy - 10 * s), (cx + 13 * s, cy + 4 * s), (cx, cy + 16 * s), (cx - 13 * s, cy + 4 * s)], fill=glass)
    d.ellipse([cx - 5 * s, cy - 4 * s, cx + 5 * s, cy + 6 * s], fill=glow)
    d.line([(cx, cy - 10 * s), (cx, cy + 16 * s)], fill=body, width=max(1, int(1.5 * s)))
    d.polygon([(cx - 6 * s, cy + 22 * s), (cx + 6 * s, cy + 22 * s), (cx + 3 * s, cy + 30 * s), (cx - 3 * s, cy + 30 * s)], fill=body)


def star8(d, cx, cy, r, col):
    pts = []
    for k in range(16):
        a = k * math.pi / 8
        rr = r if k % 2 == 0 else r * 0.62
        pts.append((cx + math.cos(a) * rr, cy + math.sin(a) * rr))
    d.polygon(pts, fill=col)


def make_rug_ramadan(W=960, H=640):
    """Ramadan: night-blue field, golden crescent and star, a border of lanterns."""
    NIGHT, DEEP, GOLD_, PALE, AMBER, TEALG = (26, 34, 70), (16, 22, 48), (212, 168, 82), (238, 222, 180), (250, 196, 96), (46, 96, 106)
    S = 2
    w, h = W * S, H * S
    img = Image.new("RGB", (w, h), NIGHT)
    d = ImageDraw.Draw(img)
    rnd = np.random.default_rng(14)
    for inset, col in [(0, DEEP), (16, GOLD_), (26, DEEP), (150, GOLD_), (160, NIGHT)]:
        d.rectangle([inset, inset, w - 1 - inset, h - 1 - inset], fill=col)
    # Lantern border.
    for x in range(110, w - 80, 150):
        for y in (88, h - 88):
            lantern(d, x, y, 1.6, GOLD_, TEALG, AMBER)
    for y in range(240, h - 200, 150):
        for x in (88, w - 88):
            lantern(d, x, y, 1.6, GOLD_, TEALG, AMBER)
    # Starry field.
    fx0, fy0, fx1, fy1 = 170, 170, w - 171, h - 171
    for _ in range(420):
        x, y = rnd.uniform(fx0, fx1), rnd.uniform(fy0, fy1)
        r = rnd.uniform(3, 9)
        star8(d, x, y, r, lerp(NIGHT, PALE, rnd.uniform(0.3, 0.8)))
    # Geometric corners.
    for sx in (0, 1):
        for sy in (0, 1):
            ox = fx0 if sx == 0 else fx1; oy = fy0 if sy == 0 else fy1
            dx = 1 if sx == 0 else -1; dy = 1 if sy == 0 else -1
            for i in range(4):
                for j in range(4 - i):
                    star8(d, ox + dx * (40 + i * 70), oy + dy * (40 + j * 70), 28, GOLD_)
                    star8(d, ox + dx * (40 + i * 70), oy + dy * (40 + j * 70), 14, NIGHT)
    # Central crescent and star medallion.
    cx, cy = w / 2, h / 2
    d.ellipse([cx - 330, cy - 330, cx + 330, cy + 330], fill=DEEP)
    d.ellipse([cx - 318, cy - 318, cx + 318, cy + 318], outline=GOLD_, width=8)
    for k in range(48):
        a = k * 2 * math.pi / 48
        star8(d, cx + math.cos(a) * 290, cy + math.sin(a) * 290, 12, GOLD_)
    d.ellipse([cx - 200, cy - 200, cx + 200, cy + 200], fill=GOLD_)
    d.ellipse([cx - 120, cy - 230, cx + 250, cy + 140], fill=DEEP)
    star8(d, cx + 90, cy + 60, 60, GOLD_)
    star8(d, cx + 90, cy + 60, 30, AMBER)
    return finish_rug(img, W, H, (70, 74, 100), (220, 196, 140), rnd, wear_amt=0.12, grain=8)


def make_rug_eid(W=960, H=640):
    """Eid: emerald and gold eight-point star lattice with a cream border."""
    EMER, DEEPG, GOLD_, CREAM, ROSE = (24, 104, 84), (14, 66, 54), (214, 172, 84), (238, 228, 204), (196, 96, 104)
    S = 2
    w, h = W * S, H * S
    img = Image.new("RGB", (w, h), EMER)
    d = ImageDraw.Draw(img)
    rnd = np.random.default_rng(15)
    for inset, col in [(0, DEEPG), (16, CREAM), (120, GOLD_), (130, DEEPG), (142, EMER)]:
        d.rectangle([inset, inset, w - 1 - inset, h - 1 - inset], fill=col)
    for x in range(60, w - 40, 70):
        for y in (68, h - 68):
            star8(d, x, y, 26, EMER); star8(d, x, y, 12, ROSE)
    for y in range(138, h - 110, 70):
        for x in (68, w - 68):
            star8(d, x, y, 26, EMER); star8(d, x, y, 12, ROSE)
    fx0, fy0, fx1, fy1 = 150, 150, w - 151, h - 151
    step = 120
    for y in range(fy0 + 60, fy1, step):
        for x in range(fx0 + 60, fx1, step):
            star8(d, x, y, 50, GOLD_)
            star8(d, x, y, 38, DEEPG)
            star8(d, x, y, 18, CREAM)
            if x + step // 2 < fx1 and y + step // 2 < fy1:
                d.polygon([(x + 60, y + 30), (x + 90, y + 60), (x + 60, y + 90), (x + 30, y + 60)], fill=lerp(EMER, GOLD_, 0.35))
    cx, cy = w / 2, h / 2
    star8(d, cx, cy, 250, CREAM)
    star8(d, cx, cy, 225, GOLD_)
    star8(d, cx, cy, 190, DEEPG)
    for k in range(8):
        a = k * math.pi / 4
        star8(d, cx + math.cos(a) * 120, cy + math.sin(a) * 120, 34, GOLD_)
    star8(d, cx, cy, 70, ROSE)
    star8(d, cx, cy, 34, CREAM)
    return finish_rug(img, W, H, CREAM, (236, 226, 200), rnd, wear_amt=0.12, grain=8)


def noise(h, w, cell, seed_rng):
    """Smooth value noise: random grid upsampled with bicubic filtering."""
    gh, gw = max(2, h // cell + 2), max(2, w // cell + 2)
    g = Image.fromarray((seed_rng.random((gh, gw)) * 255).astype(np.uint8))
    g = g.resize((gw * cell, gh * cell), Image.BICUBIC).crop((0, 0, w, h))
    return np.asarray(g).astype(np.float32) / 255 - 0.5


def fbm(h, w, cell, octaves, r):
    out = np.zeros((h, w), np.float32)
    amp = 1.0
    for _ in range(octaves):
        out += noise(h, w, max(2, cell), r) * amp
        cell //= 2
        amp *= 0.5
    return out


def fbm_long(h, w, cell, octaves, r, stretch=12):
    """fbm stretched along x, like grain running down a board."""
    n = fbm(h, max(8, w // stretch), cell, octaves, r)
    img = Image.fromarray(((n + 1) * 127).clip(0, 255).astype(np.uint8)).resize((w, h), Image.BICUBIC)
    return np.asarray(img, np.float32) / 127 - 1


def make_wood(W=2560, H=1440, light=(154, 126, 102), dark=(128, 98, 75), plank_h=110, seed=7):
    """Floorboards: distorted growth rings, fibres, knots, bevels."""
    global rng
    rng = np.random.default_rng(seed)
    out = np.zeros((H, W, 3), np.float32)
    light = np.array(light, np.float32)   # early wood
    dark = np.array(dark, np.float32)     # late wood (grain lines)
    yy, xx = np.mgrid[0:plank_h, 0:W].astype(np.float32)
    for row in range((H + plank_h - 1) // plank_h):
        y0 = row * plank_h
        offset = int(rng.integers(0, 1100))
        x = 0 - offset
        while x < W:
            length = int(rng.integers(900, 1600))
            xa, xb = max(0, x), min(W, x + length)
            if xb > xa:
                w = xb - xa
                py, px = yy[:, :w], xx[:, :w]
                # Growth rings cut lengthwise: a ring "distance" that wanders slowly
                # along the board, with irregular spacing.
                warp = fbm(plank_h, w, 320, 3, rng) * 30 + fbm_long(plank_h, w, 24, 3, rng) * 6
                centre = rng.uniform(-150, plank_h + 150)
                d = np.abs(py - centre + warp + np.sin(px * rng.uniform(0.0008, 0.002) + rng.uniform(0, 6)) * 18)
                if rng.random() < 0.25:
                    kx, ky = rng.uniform(80, max(81, w - 80)), rng.uniform(30, plank_h - 30)
                    dist = np.sqrt(((px - kx) / 3.0) ** 2 + (py - ky) ** 2)
                    d = d + 22 * np.exp(-(dist / 20) ** 2)
                phase = d * rng.uniform(0.07, 0.12) + fbm_long(plank_h, w, 16, 3, rng) * 1.2
                rings = 0.5 + 0.5 * np.sin(phase * 2 * np.pi)
                late = rings ** 6                      # thin dark late-wood bands
                # Fine streaks running along the grain (pores and fibres).
                def streaks(scale, cell):
                    n = noise(plank_h, max(4, w // scale), cell, rng)
                    return np.asarray(Image.fromarray(((n + 0.5) * 255).clip(0, 255).astype(np.uint8))
                                      .resize((w, plank_h), Image.BICUBIC), np.float32) / 255 - 0.5
                fine = streaks(60, 1) * 0.9 + streaks(25, 2) * 0.6
                t = late * 0.35 + fine * 0.3 + 0.3
                t = np.clip(t, 0, 1)[..., None]
                col = light * (1 - t) + dark * t
                tone = rng.uniform(0.9, 1.07)
                hue = np.array([1.0, rng.uniform(0.98, 1.01), rng.uniform(0.95, 1.02)])
                col = col * tone * hue
                col *= (1 + fbm(plank_h, w, 160, 3, rng) * 0.05)[..., None]
                # Bevelled edges: light top edge, dark gap and shadow at the bottom.
                edge = np.ones((plank_h, w), np.float32)
                edge[0:1] *= 0.72
                edge[1:2] *= 0.9
                edge[-1:] *= 0.92
                edge[:, :1] *= 0.75
                edge[:, -1:] *= 0.9
                col *= edge[..., None]
                ys = slice(y0, min(H, y0 + plank_h))
                out[ys, xa:xb] = col[: ys.stop - y0]
            x += length
    # Soft room light and a satin finish sheen.
    gy, gx = np.mgrid[0:H, 0:W].astype(np.float32)
    shade = 1.03 - 0.08 * (((gx - W * 0.4) / W) ** 2 + ((gy - H * 0.35) / H) ** 2) * 2
    sheen = np.exp(-(((gx - W * 0.3) / (W * 0.35)) ** 2 + ((gy - H * 0.2) / (H * 0.4)) ** 2)) * 5
    out = out * shade[..., None] + sheen[..., None]
    out += rng.normal(0, 1.0, (H, W, 1))
    img = Image.fromarray(np.clip(out, 0, 255).astype(np.uint8))
    return img.filter(ImageFilter.GaussianBlur(0.8))


if __name__ == "__main__":
    os.makedirs(os.path.join(OUT, "rugs"), exist_ok=True)
    os.makedirs(os.path.join(OUT, "floors"), exist_ok=True)
    for name, fn in [("vintage", make_rug), ("classic", make_rug_classic), ("kilim", make_rug_kilim),
                     ("ramadan", make_rug_ramadan), ("eid", make_rug_eid)]:
        fn().save(os.path.join(OUT, "rugs", name + ".png"), optimize=True)
    for name, kw in [("oak", {}),
                     ("walnut", dict(light=(112, 80, 60), dark=(84, 56, 40), plank_h=120, seed=8)),
                     ("ash", dict(light=(206, 190, 168), dark=(182, 164, 142), plank_h=100, seed=9))]:
        make_wood(**kw).save(os.path.join(OUT, "floors", name + ".jpg"), quality=90)
    print("ok")
