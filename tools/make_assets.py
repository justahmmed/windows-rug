"""Generate the rug texture (rug.png) and wood floor wallpaper (wood.jpg).

Run: python3 tools/make_assets.py
"""
import math
import os

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

OUT = os.path.join(os.path.dirname(__file__), "..", "rug")
rng = np.random.default_rng(7)

RED = (128, 22, 26)
DARK_RED = (88, 14, 18)
NAVY = (24, 30, 58)
CREAM = (226, 206, 168)
GOLD = (196, 146, 70)
TEAL = (40, 82, 88)


def lerp(a, b, t):
    return tuple(int(a[i] + (b[i] - a[i]) * t) for i in range(3))


def make_rug(W=960, H=640):
    img = Image.new("RGB", (W, H), RED)
    d = ImageDraw.Draw(img)

    # Borders: outer guard, main border, inner guard
    bands = [
        (0, DARK_RED), (10, CREAM), (16, NAVY), (64, CREAM), (70, DARK_RED),
        (76, GOLD), (80, RED),
    ]
    for inset, col in bands:
        d.rectangle([inset, inset, W - 1 - inset, H - 1 - inset], fill=col)

    # Main border motifs (navy band between 16 and 64)
    def motif(cx, cy, r):
        d.polygon([(cx, cy - r), (cx + r, cy), (cx, cy + r), (cx - r, cy)], fill=RED)
        d.polygon([(cx, cy - r * .6), (cx + r * .6, cy), (cx, cy + r * .6), (cx - r * .6, cy)], fill=CREAM)
        d.ellipse([cx - r * .25, cy - r * .25, cx + r * .25, cy + r * .25], fill=TEAL)
        for a in range(4):
            ang = a * math.pi / 2 + math.pi / 4
            px, py = cx + math.cos(ang) * r * .85, cy + math.sin(ang) * r * .85
            d.ellipse([px - 3, py - 3, px + 3, py + 3], fill=GOLD)

    step = 48
    for x in range(40, W - 30, step):
        motif(x, 40, 17)
        motif(x, H - 41, 17)
    for y in range(40 + step, H - 60, step):
        motif(40, y, 17)
        motif(W - 41, y, 17)

    # Small zig-zag in the cream guards
    for x in range(10, W - 10, 8):
        d.line([(x, 13), (x + 4, 11)], fill=RED, width=1)
        d.line([(x, H - 14), (x + 4, H - 12)], fill=RED, width=1)

    # Field: dense small floral pattern
    fx0, fy0, fx1, fy1 = 82, 82, W - 83, H - 83
    for y in range(fy0 + 14, fy1 - 6, 28):
        off = 14 if (y // 28) % 2 else 0
        for x in range(fx0 + 14 + off, fx1 - 6, 28):
            c = lerp(DARK_RED, NAVY, 0.4)
            d.polygon([(x, y - 6), (x + 6, y), (x, y + 6), (x - 6, y)], fill=c)
            d.ellipse([x - 2, y - 2, x + 2, y + 2], fill=GOLD)

    # Corner spandrels
    cw, ch = 210, 150
    for (sx, sy) in [(0, 0), (1, 0), (0, 1), (1, 1)]:
        ox = fx0 if sx == 0 else fx1
        oy = fy0 if sy == 0 else fy1
        dx = 1 if sx == 0 else -1
        dy = 1 if sy == 0 else -1
        pts = [(ox, oy), (ox + dx * cw, oy), (ox + dx * cw * .55, oy + dy * ch * .45),
               (ox + dx * cw * .3, oy + dy * ch * .55), (ox, oy + dy * ch)]
        d.polygon(pts, fill=NAVY)
        inner = [(ox + dx * 10, oy + dy * 10), (ox + dx * (cw - 40), oy + dy * 10),
                 (ox + dx * cw * .48, oy + dy * ch * .38), (ox + dx * 10, oy + dy * (ch - 30))]
        d.line(inner + [inner[0]], fill=GOLD, width=2)
        for i in range(5):
            px = ox + dx * (30 + i * 28)
            py = oy + dy * (22 + (i % 2) * 14)
            d.ellipse([px - 5, py - 5, px + 5, py + 5], fill=CREAM)
            d.ellipse([px - 2, py - 2, px + 2, py + 2], fill=RED)

    # Central medallion: layered lobed diamonds
    cx, cy = W / 2, H / 2

    def lobed(rx, ry, lobes, amp, col):
        pts = []
        for i in range(360):
            t = i / 360 * 2 * math.pi
            # diamond base shape blended with a circle, plus lobes
            base = 1 / (abs(math.cos(t)) + abs(math.sin(t))) ** 0.75
            r = base * (1 + amp * math.cos(lobes * t))
            pts.append((cx + math.cos(t) * rx * r, cy + math.sin(t) * ry * r))
        d.polygon(pts, fill=col)

    lobed(250, 175, 16, .06, NAVY)
    lobed(228, 158, 16, .06, GOLD)
    lobed(220, 152, 16, .06, DARK_RED)
    lobed(160, 112, 12, .08, NAVY)
    lobed(120, 84, 8, .1, CREAM)
    lobed(96, 66, 8, .1, RED)
    lobed(52, 36, 4, .12, NAVY)
    d.ellipse([cx - 14, cy - 14, cx + 14, cy + 14], fill=GOLD)
    d.ellipse([cx - 6, cy - 6, cx + 6, cy + 6], fill=RED)
    # Pendants on the long axis
    for sgn in (-1, 1):
        px = cx + sgn * 300
        d.polygon([(px, cy - 34), (px + sgn * 46, cy), (px, cy + 34), (px - sgn * 30, cy)], fill=NAVY)
        d.polygon([(px, cy - 16), (px + sgn * 22, cy), (px, cy + 16), (px - sgn * 14, cy)], fill=CREAM)
    # Dots ring
    for i in range(32):
        t = i / 32 * 2 * math.pi
        x, y = cx + math.cos(t) * 190, cy + math.sin(t) * 132
        d.ellipse([x - 3, y - 3, x + 3, y + 3], fill=CREAM)
    for i in range(16):
        t = i / 16 * 2 * math.pi
        x, y = cx + math.cos(t) * 138, cy + math.sin(t) * 96
        d.ellipse([x - 5, y - 5, x + 5, y + 5], fill=RED)
        d.ellipse([x - 2, y - 2, x + 2, y + 2], fill=GOLD)

    # Wool texture: per-pixel noise + slight knot grid + worn vignette
    a = np.asarray(img).astype(np.float32)
    noise = rng.normal(0, 9, (H, W, 1))
    knots = (np.sin(np.arange(W) * 1.9)[None, :, None] * np.sin(np.arange(H) * 1.9)[:, None, None]) * 4
    yy, xx = np.mgrid[0:H, 0:W]
    vign = 1 - 0.18 * (((xx - W / 2) / (W / 2)) ** 2 + ((yy - H / 2) / (H / 2)) ** 2)
    a = (a + noise + knots) * vign[..., None]
    img = Image.fromarray(np.clip(a, 0, 255).astype(np.uint8))
    img = img.filter(ImageFilter.GaussianBlur(0.6))

    # Fringe on the short ends
    F = 26
    out = Image.new("RGBA", (W + 2 * F, H), (0, 0, 0, 0))
    out.paste(img, (F, 0))
    fd = ImageDraw.Draw(out)
    for y in range(4, H - 4, 5):
        for side in (0, 1):
            x0 = F if side == 0 else F + W - 1
            length = F - 2 - int(rng.integers(0, 6))
            x1 = x0 - length if side == 0 else x0 + length
            wob = int(rng.integers(-2, 3))
            shade = int(rng.integers(205, 240))
            fd.line([(x0, y), (x1, y + wob)], fill=(shade, shade - 12, shade - 40, 255), width=2)
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
