"""Render the living-room furniture, seen from above (rug/furniture/*.png).

Corner sofa, floor lamp (off and on), coffee table and a potted monstera.
Like the cat and the props, each piece is a lit height field with real
materials: linen weave, walnut grain, glazed ceramic, glossy leaves. Every
sheet holds the sprite on the left and its soft shadow on the right.

Run: python3 tools/make_furniture.py
"""
import math
import os

import numpy as np
from PIL import Image

from make_props import Canvas, blur, L, SS

OUT = os.path.join(os.path.dirname(__file__), "..", "rug", "furniture")


def superellipse(c, cx, cy, hw, hh, height, n=4.0, puff=0.6, rot=0.0):
    """A plump cushion: flat-ish top, soft rounded edges."""
    cs, sn = math.cos(rot), math.sin(rot)
    u = ((c.X - cx) * cs + (c.Y - cy) * sn) / hw
    v = (-(c.X - cx) * sn + (c.Y - cy) * cs) / hh
    q = 1 - (np.abs(u) ** n + np.abs(v) ** n)
    return np.where(q > 0, height * (1 - puff + puff * np.clip(q, 0, 1) ** 0.35), -1)


def light_and_ao(c, h, sigma_ao=6, ao_strength=0.6):
    inside = h > 0
    hz = np.where(inside, h, 0).astype(np.float32)
    hs = blur(hz, SS * 1.2)
    gy, gx = np.gradient(hs)
    nx, ny, nz = -gx * SS, -gy * SS, np.ones_like(hs)
    nl = np.sqrt(nx * nx + ny * ny + nz * nz)
    ndl = np.clip((nx * L[0] + ny * L[1] + nz * L[2]) / nl, 0, 1)
    ao = np.clip((blur(hz, SS * sigma_ao) - hz + 2) / 14, 0, 1) * inside
    light = (0.42 + 0.72 * ndl) * (1 - ao * ao_strength)
    return light, ndl, (nx, ny, nz, nl), inside


def spec(normals, power, strength):
    nx, ny, nz, nl = normals
    hx, hy, hz = L[0], L[1], L[2] + 1
    hl = math.sqrt(hx * hx + hy * hy + hz * hz)
    return np.clip((nx * hx + ny * hy + nz * hz) / (nl * hl), 0, 1) ** power * 255 * strength


def save(c, col, a, name, shadow_spread=6, shadow_strength=0.5, shadow_alpha=None):
    os.makedirs(OUT, exist_ok=True)
    sprite = c.cell(np.clip(col, 0, 255), np.clip(a, 0, 1))
    shadow = c.shadow(a if shadow_alpha is None else shadow_alpha, shadow_spread, shadow_strength)
    sheet = Image.new("RGBA", (c.cw * 2, c.ch), (0, 0, 0, 0))
    sheet.paste(sprite, (0, 0))
    sheet.paste(shadow, (c.cw, 0))
    sheet.save(os.path.join(OUT, name + ".png"), optimize=True)


def linen(c, base, scale=1.0):
    """Woven linen: fine crossing threads plus slubs."""
    warp = np.sin(c.X * 2.6 * scale) * 0.5 + np.sin(c.Y * 2.6 * scale) * 0.5
    slub = c.noise(2) * 0.6 + c.noise(6) * 0.4
    t = 1 + warp * 0.035 + slub * 0.08
    return np.array(base, np.float32)[None, None] * t[..., None]


def sofa():
    # L-shaped sectional for a corner: backs along the bottom and right edges.
    W, H = 600, 430
    c = Canvas(W, H, 101)
    x0, y0 = -W / 2 + 10, -H / 2 + 10
    x1, y1 = W / 2 - 10, H / 2 - 10
    back_d, arm_w, seat_d = 46, 34, 150
    parts = []
    # Backrests (outer edges) and arms.
    parts.append(superellipse(c, (x0 + x1) / 2, y1 - back_d / 2, (x1 - x0) / 2, back_d / 2, 78, 6, 0.35))
    parts.append(superellipse(c, x1 - back_d / 2, (y0 + y1) / 2, back_d / 2, (y1 - y0) / 2, 78, 6, 0.35))
    parts.append(superellipse(c, x0 + arm_w / 2, y1 - (back_d + seat_d) / 2, arm_w / 2, (back_d + seat_d) / 2, 62, 6, 0.4))
    parts.append(superellipse(c, x1 - (back_d + seat_d) / 2, y0 + arm_w / 2, (back_d + seat_d) / 2, arm_w / 2, 62, 6, 0.4))
    # Base frame under the cushions.
    frame_long = superellipse(c, (x0 + arm_w + x1 - back_d) / 2, y1 - back_d - seat_d / 2, (x1 - back_d - x0 - arm_w) / 2, seat_d / 2, 28, 8, 0.2)
    frame_ret = superellipse(c, x1 - back_d - seat_d / 2, (y0 + arm_w + y1 - back_d) / 2, seat_d / 2, (y1 - back_d - y0 - arm_w) / 2, 28, 8, 0.2)
    parts += [frame_long, frame_ret]
    # Seat cushions: three along the long side, two on the return, one in the corner.
    sx0, sx1 = x0 + arm_w + 2, x1 - back_d - seat_d - 2
    n = 3
    cw = (sx1 - sx0) / n
    sy = y1 - back_d - seat_d / 2
    for i in range(n):
        parts.append(superellipse(c, sx0 + cw * (i + 0.5), sy, cw / 2 - 2, seat_d / 2 - 3, 44, 4.5, 0.55))
    parts.append(superellipse(c, x1 - back_d - seat_d / 2, sy, seat_d / 2 - 3, seat_d / 2 - 3, 44, 4.5, 0.55))
    ry0, ry1 = y0 + arm_w + 2, y1 - back_d - seat_d - 2
    rh = (ry1 - ry0) / 2
    for i in range(2):
        parts.append(superellipse(c, x1 - back_d - seat_d / 2, ry0 + rh * (i + 0.5), seat_d / 2 - 3, rh / 2 - 2, 44, 4.5, 0.55))
    # Back cushions leaning on the backrest.
    for i in range(n):
        parts.append(superellipse(c, sx0 + cw * (i + 0.5), y1 - back_d - 14, cw / 2 - 6, 18, 66, 4, 0.7))
    for i in range(2):
        parts.append(superellipse(c, x1 - back_d - 14, ry0 + rh * (i + 0.5), 18, rh / 2 - 6, 66, 4, 0.7))
    parts.append(superellipse(c, x1 - back_d - 16, y1 - back_d - 16, 22, 22, 66, 4, 0.7))
    h = parts[0]
    for p in parts[1:]:
        h = np.maximum(h, p)
    light, ndl, normals, inside = light_and_ao(c, h, sigma_ao=7, ao_strength=0.75)
    col = linen(c, (196, 191, 184))
    # Throw pillows: mustard velvet, navy linen, a knitted cream one.
    pillows = [((sx0 + cw * 0.5 - 10, y1 - back_d - 36), 34, 0.35, (196, 152, 64), 1.0),
               ((x1 - back_d - 40, y1 - back_d - 40), 36, 0.78, (56, 66, 96), 0.0),
               ((x1 - back_d - 34, ry0 + rh * 0.5 + 6), 32, -0.25, (230, 224, 210), 0.0)]
    hp = h.copy()
    pcol = col.copy()
    for (px, py), r, rot, rgb, velvet in pillows:
        p = superellipse(c, px, py, r, r * 0.92, 92, 3.2, 0.85, rot)
        m = p > hp
        hp = np.where(m, p, hp)
        tex = linen(c, rgb, 1.6) if not velvet else np.array(rgb, np.float32)[None, None] * (1 + c.noise(3)[..., None] * 0.06)
        pcol = np.where(m[..., None], tex, pcol)
    light, ndl, normals, inside = light_and_ao(c, hp, sigma_ao=7, ao_strength=0.75)
    col = pcol * light[..., None] + spec(normals, 6, 0.05)[..., None]
    a = blur(inside.astype(np.float32), SS * 0.6)
    save(c, col, np.clip(a * 1.3, 0, 1), "sofa", 8, 0.55)


def lamp():
    # Floor lamp seen from above: a linen drum shade, open on top.
    W = H = 180
    for on in (False, True):
        c = Canvas(W, H, 202)
        r = np.sqrt(c.X ** 2 + c.Y ** 2)
        shade = np.where(r < 62, 60 + np.clip((62 - r) / 3, 0, 1) * 4, -1)
        opening = r < 44
        h = np.where(opening, 40, shade)
        light, ndl, normals, inside = light_and_ao(c, h, sigma_ao=5, ao_strength=0.5)
        base = linen(c, (232, 222, 200), 1.3)
        if on:
            # Lit from inside: the fabric glows, brightest near the rim it's thinnest at.
            glow = np.clip(1 - np.abs(r - 53) / 12, 0, 1)
            col = base * (0.95 + 0.25 * glow)[..., None] + np.array([40, 26, 0])[None, None]
            inner = np.array([255, 236, 190], np.float32) * (1.05 - np.clip(r / 44, 0, 1) * 0.3)[..., None]
            bulb = np.clip(1.3 - r / 10, 0, 1)
            inner = inner + bulb[..., None] * 60
            col = np.where(opening[..., None], inner, col)
        else:
            col = base * light[..., None]
            inner = np.array([96, 90, 80], np.float32) * (0.6 + 0.4 * np.clip(r / 44, 0, 1))[..., None]
            bulb = np.clip(1.2 - r / 9, 0, 1)
            inner = inner * (1 - bulb[..., None]) + np.array([210, 210, 204])[None, None] * bulb[..., None]
            col = np.where(opening[..., None], inner, col)
        # Thin brass rim on top.
        rim = np.abs(r - 62) < 1.4
        col = np.where(rim[..., None], np.array([180, 146, 80], np.float32) * (light[..., None] if not on else 1.1), col)
        a = blur(inside.astype(np.float32), SS * 0.5)
        save(c, col, np.clip(a * 1.3, 0, 1), "lamp-on" if on else "lamp-off", 7, 0.25 if on else 0.5)


def table(with_mug=True, name="table"):
    # Oval walnut coffee table with a mug of coffee and two books.
    W, H = 340, 220
    c = Canvas(W, H, 303)
    u, v = c.X / 150, c.Y / 90
    q = 1 - (np.abs(u) ** 2.6 + np.abs(v) ** 2.6)
    top = np.where(q > 0, 50 + np.clip(q * 8, 0, 1) * 4, -1)
    light, ndl, normals, inside = light_and_ao(c, top)
    ring = c.X * 0.12 + c.noise(30) * 4 + c.noise(8) * 0.8
    grain = 0.5 + 0.5 * np.sin(ring * 2 * np.pi * 0.6 + c.Y * 0.02)
    streak = c.noise(1) * 0.08
    wood = np.array([96, 62, 40], np.float32)[None, None] * (0.85 + 0.25 * grain[..., None] + streak[..., None])
    col = wood * light[..., None] + spec(normals, 30, 0.35)[..., None]
    # Books, stacked and slightly turned.
    hcol, hh = col, top.copy()
    for (bx, by, bw, bh_, rot, rgb, z) in [(-62, 18, 46, 34, 0.12, (128, 52, 48), 58), (-58, 14, 40, 30, -0.1, (52, 84, 92), 63)]:
        b = c.box(bx, by, bw, bh_, z, rot, bevel=1.2)
        m = b > hh
        hh = np.where(m, b, hh)
        cs, sn = math.cos(rot), math.sin(rot)
        uu = (c.X - bx) * cs + (c.Y - by) * sn
        pages = (uu > bw - 4)
        tint = np.where(pages[..., None], np.array([236, 230, 214], np.float32), np.array(rgb, np.float32))
        hcol = np.where(m[..., None], tint, hcol)
    # Mug.
    mr = np.sqrt((c.X - 58) ** 2 + (c.Y + 10) ** 2)
    mug = np.where(mr < 17, 70, -1)
    handle = (np.abs(np.sqrt((c.X - 78) ** 2 + (c.Y + 10) ** 2) - 7) < 2.4) & (c.X > 72)
    mug = np.where(handle, 66, mug)
    if not with_mug:
        mug = np.full(mug.shape, -1.0)
        mr = np.full(mr.shape, 1e9)
    m = mug > hh
    hh = np.where(m, mug, hh)
    hcol = np.where(m[..., None], np.array([238, 236, 232], np.float32), hcol)
    light2, ndl2, normals2, inside2 = light_and_ao(c, hh, sigma_ao=4, ao_strength=0.6)
    col2 = hcol * light2[..., None]
    col = np.where((hh > top + 0.5)[..., None], col2 + spec(normals2, 40, 0.5)[..., None], col)
    coffee = mr < 13.5
    crema = np.clip(1 - mr / 13.5, 0, 1)
    col = np.where(coffee[..., None], np.array([70, 40, 22], np.float32)[None, None] * (0.8 + 0.5 * crema[..., None])
                   + (np.clip(1 - np.abs(mr - 10) / 2, 0, 1) * 30)[..., None], col)
    # Steam-light highlight on the coffee surface.
    hl = np.exp(-(((c.X - 54) / 3) ** 2 + ((c.Y + 15) / 2) ** 2))
    col += (hl * 140)[..., None]
    a = blur(inside2.astype(np.float32), SS * 0.5)
    save(c, col, np.clip(a * 1.3, 0, 1), name, 7, 0.5)


def plant():
    # Monstera in a terracotta pot: big split leaves radiating out.
    W = H = 300
    c = Canvas(W, H, 404)
    r = np.sqrt(c.X ** 2 + c.Y ** 2)
    pot = np.where(r < 52, 60 + np.clip((52 - r) / 4, 0, 1) * 5, -1)
    soil = r < 44
    h = np.where(soil, 50, pot)
    col = np.where(soil[..., None], np.array([58, 40, 30], np.float32)[None, None] * (1 + c.noise(1)[..., None] * 0.4),
                   np.array([178, 96, 64], np.float32)[None, None] * (1 + c.noise(3)[..., None] * 0.1))
    rnd = np.random.default_rng(5)
    leaves = 8
    for k in range(leaves):
        ang = k / leaves * 2 * math.pi + rnd.uniform(-0.25, 0.25)
        dist = rnd.uniform(52, 82)
        lx, ly = math.cos(ang) * dist, math.sin(ang) * dist
        lw, lh = rnd.uniform(46, 62), rnd.uniform(36, 48)
        cs, sn = math.cos(ang), math.sin(ang)
        uu = ((c.X - lx) * cs + (c.Y - ly) * sn) / lw
        vv = (-(c.X - lx) * sn + (c.Y - ly) * cs) / lh
        q = 1 - uu * uu - vv * vv
        # Heart-shaped notch at the stem and fenestration splits from the edge.
        theta = np.arctan2(vv, uu)
        splits = (np.sin(theta * 9 + k) > 0.75) & (np.sqrt(uu * uu + vv * vv) > 0.55)
        notch = (uu < -0.6) & (np.abs(vv) < 0.18)
        leaf = (q > 0) & ~splits & ~notch
        z = 70 + k * 3 + np.clip(q, 0, 1) * 6 + vv * 0  # higher leaves overlap lower ones
        z = np.where(leaf, z + np.abs(vv) * 6, -1)     # a slight fold along the midrib
        m = z > h
        vein = (np.abs(vv) < 0.035) | (np.abs(np.sin(theta * 9 + k)) < 0.06) & (np.sqrt(uu * uu + vv * vv) < 0.55)
        green = np.array([38, 92, 52], np.float32) * (0.9 + 0.2 * np.clip(1 - np.abs(vv), 0, 1))[..., None]
        green = np.where(vein[..., None], green * 1.35, green)
        h = np.where(m, z, h)
        col = np.where(m[..., None], green, col)
        # Stem from the pot.
        for t in np.linspace(0.15, 0.95, 18):
            sx, sy = lx * t - cs * lw * 0.6 * t, ly * t - sn * lw * 0.6 * t
            sm = np.sqrt((c.X - sx) ** 2 + (c.Y - sy) ** 2) < 2.4
            sz = 52 + t * 18
            mm = sm & (sz > h)
            h = np.where(mm, sz, h)
            col = np.where(mm[..., None], np.array([60, 110, 60], np.float32), col)
    light, ndl, normals, inside = light_and_ao(c, h, sigma_ao=4, ao_strength=0.7)
    is_leaf = col[..., 1] > col[..., 0] + 20
    col = col * light[..., None] + np.where(is_leaf, spec(normals, 20, 0.45), spec(normals, 6, 0.05))[..., None]
    a = blur(inside.astype(np.float32), SS * 0.4)
    save(c, col, np.clip(a * 1.4, 0, 1), "plant", 6, 0.45)


if __name__ == "__main__":
    sofa()
    lamp()
    table()
    table(False, "table-nomug")
    plant()
    print("ok")
