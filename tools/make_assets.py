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

    img = img.resize((W, H), Image.LANCZOS)

    # Age: faded patches toward the warp colour, slight colour wobble (abrash), wool grain.
    a = np.asarray(img).astype(np.float32)
    wear = fbm(H, W, 32, 4, rnd)
    wear = np.clip((wear + 0.1) * 2.5, 0, 1)[..., None] * 0.28
    a = a * (1 - wear) + np.array(IVORY, np.float32) * wear
    abrash = fbm_long(H, W, 40, 3, rnd, stretch=6)[..., None] * np.array([6, 5, 9])
    a += abrash
    a += rnd.normal(0, 7, (H, W, 1))
    yy, xx = np.mgrid[0:H, 0:W]
    a *= (1 + np.sin(xx * 2.2) * np.sin(yy * 2.2) * 0.025)[..., None]
    img = Image.fromarray(np.clip(a, 0, 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(0.45))

    # Fringe on the short ends.
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
            col = tuple(int(c) + sh for c in FRINGE) + (255,)
            fd.line([(x0, y), (x1, y + wob)], fill=col, width=2)
    return out


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


def make_wood(W=2560, H=1440):
    """Oak-style floorboards: distorted growth rings, fibres, knots, bevels."""
    plank_h = 110
    out = np.zeros((H, W, 3), np.float32)
    light = np.array([154, 126, 102], np.float32)   # early wood
    dark = np.array([128, 98, 75], np.float32)      # late wood (grain lines)
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
    os.makedirs(OUT, exist_ok=True)
    make_rug().save(os.path.join(OUT, "rug.png"))
    make_wood().save(os.path.join(OUT, "wood.jpg"), quality=90)
    print("ok")
