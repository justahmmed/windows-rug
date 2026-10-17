"""Render a realistic top-down long-haired cat sprite sheet (rug/cat.png).

The cat is modelled as a height field (body, head, ears, paws, tail), lit
with the same top-left light as the rug, and textured with tabby stripes
and long fur. The look follows a fluffy colour-point cat: cream-white
coat, grey plume of a tail, grey ears and face mask, blue eyes. Row 0 holds 8 walk frames and the curled-up sleeping pose;
row 1 holds the matching soft shadows.

Run: python3 tools/make_cat.py
"""
import math
import os

import numpy as np
from PIL import Image, ImageFilter

OUT = os.path.join(os.path.dirname(__file__), "..", "rug", "cat.png")
CW, CH = 180, 120          # final cell size
SS = 3                     # supersampling
W, H = CW * SS, CH * SS
rng = np.random.default_rng(21)

yy, xx = np.mgrid[0:H, 0:W].astype(np.float32)
X = (xx - W / 2) / SS      # cat units ~ final pixels, origin at cell centre
Y = (yy - H / 2) / SS


def ellipsoid(cx, cy, rx, ry, rz, rot=0.0):
    c, s = math.cos(rot), math.sin(rot)
    u = ((X - cx) * c + (Y - cy) * s) / rx
    v = (-(X - cx) * s + (Y - cy) * c) / ry
    q = 1 - u * u - v * v
    return np.where(q > 0, rz * np.sqrt(np.clip(q, 0, 1)), -1)


def ear(bx, by, ax, ay, width, height):
    """Thin triangular ear: base centre (bx, by), apex (ax, ay). Returns height, inner mask."""
    dx, dy = ax - bx, ay - by
    L = math.hypot(dx, dy)
    ux, uy = dx / L, dy / L
    u = ((X - bx) * ux + (Y - by) * uy) / L          # 0 at base, 1 at tip
    v = (-(X - bx) * uy + (Y - by) * ux)
    half = (1 - u) * width / 2
    inside = (u > -0.15) & (u < 1) & (np.abs(v) < half)
    h = np.where(inside, height * (0.55 + 0.45 * (1 - np.abs(v) / np.maximum(half, 1e-3))) * np.clip(1.15 - u, 0, 1), -1)
    inner = inside & (u > 0.12) & (u < 0.8) & (np.abs(v) < half * 0.55)
    return h, inner


def tube(points, r0, r1):
    """Height field of a tapering tube along a polyline."""
    h = np.full(X.shape, -1.0, np.float32)
    n = len(points)
    for k in range(n - 1):
        (ax, ay), (bx, by) = points[k], points[k + 1]
        dx, dy = bx - ax, by - ay
        L2 = dx * dx + dy * dy + 1e-6
        t = np.clip(((X - ax) * dx + (Y - ay) * dy) / L2, 0, 1)
        px, py = ax + t * dx, ay + t * dy
        d = np.sqrt((X - px) ** 2 + (Y - py) ** 2)
        r = r0 + (r1 - r0) * (k + t) / (n - 1)
        q = 1 - (d / r) ** 2
        h = np.maximum(h, np.where(q > 0, r * np.sqrt(np.clip(q, 0, 1)), -1))
    return h


def blur(a, sigma):
    """Separable Gaussian blur for float arrays (PIL can't blur mode F)."""
    r = int(sigma * 3) + 1
    k = np.exp(-(np.arange(-r, r + 1) ** 2) / (2 * sigma * sigma)); k /= k.sum()
    a = np.pad(a, r, mode="edge")
    a = sum(k[i] * a[:, i:i + a.shape[1] - 2 * r] for i in range(2 * r + 1))
    a = sum(k[i] * a[i:i + a.shape[0] - 2 * r, :] for i in range(2 * r + 1))
    return a.astype(np.float32)


def smooth_union(*hs):
    out = hs[0]
    for h in hs[1:]:
        out = np.maximum(out, h)
    return out


def noise(cell, stretch_x=1.0, seed=None):
    r = rng if seed is None else np.random.default_rng(seed)
    gh, gw = H // cell + 3, int(W // (cell * stretch_x)) + 3
    g = Image.fromarray((r.random((gh, gw)) * 255).astype(np.uint8))
    g = g.resize((int(gw * cell * stretch_x), gh * cell), Image.BICUBIC).crop((0, 0, W, H))
    return np.asarray(g, np.float32) / 255 - 0.5


FUR_FINE = noise(2, 4.0) * 0.7 + noise(1, 6.0) * 0.5
FUR_CLUMP = noise(6, 2.0)
EDGE = noise(1, 1.0, seed=3) + noise(2, 1.0, seed=4) * 0.6


def shade(h, points, belly_light, eyes=None, closed_eyes=None, nose=None, ear_inner=None):
    """Light the height field and paint fur. `along` is the fur direction field (radians)."""
    inside = h > 0
    hz = np.where(inside, h, 0)
    # Soften the silhouette a little so the shading reads round.
    hs = blur(hz.astype(np.float32), SS * 2.4)
    gy, gx = np.gradient(hs)
    nx, ny, nz = -gx * SS, -gy * SS, np.ones_like(hs)
    nl = np.sqrt(nx * nx + ny * ny + nz * nz)
    L = np.array([-0.35, -0.5, 0.8]); L /= np.linalg.norm(L)
    ndl = np.clip((nx * L[0] + ny * L[1] + nz * L[2]) / nl, 0, 1)
    light = 0.42 + 0.75 * ndl
    # Creases where parts meet (head on body, tail around legs) get darker.
    ao = np.clip((blur(hz.astype(np.float32), SS * 4) - hz + 1.5) / 7, 0, 1) * (h > 0)
    light = light * (1 - ao * 0.55)

    # Colour-point long hair: cream-white coat, soft grey points.
    coat = np.array([238, 233, 226], np.float32)
    point = np.array([132, 128, 136], np.float32)
    shade_tint = np.array([214, 210, 212], np.float32)
    pts = np.clip(points + FUR_CLUMP * 0.5 * (points > 0.05), 0, 1)
    col = coat[None, None] * (1 - pts[..., None]) + point[None, None] * pts[..., None]
    col = col * (1 - belly_light[..., None] * 0.3) + shade_tint[None, None] * belly_light[..., None] * 0.3
    # Long fur: clumps and fine strands.
    fur = FUR_FINE * 0.16 + FUR_CLUMP * 0.22
    col *= (1 + fur)[..., None]
    col *= light[..., None]
    col += (np.clip(ndl - 0.8, 0, 1) * 120)[..., None]

    if eyes is not None:
        for (ex, ey, er) in eyes:
            d = ((X - ex) / (er * 1.25)) ** 2 + ((Y - ey) / er) ** 2
            m = np.clip(1.4 - d * 1.4, 0, 1)
            col = col * (1 - m[..., None]) + np.array([70, 125, 196])[None, None] * m[..., None]
            pupil = np.clip(1.2 - (((X - ex) / (er * 0.35)) ** 2 + ((Y - ey) / (er * 0.9)) ** 2), 0, 1)
            col = col * (1 - pupil[..., None]) + np.array([12, 10, 8])[None, None] * pupil[..., None]
            hl = np.clip(1 - (((X - ex + er * 0.3) ** 2 + (Y - ey - er * 0.3) ** 2) / (er * 0.25) ** 2), 0, 1)
            col += (hl * 200)[..., None]
    if closed_eyes is not None:
        for (ex, ey, er, ang) in closed_eyes:
            c, s = math.cos(ang), math.sin(ang)
            u = (X - ex) * c + (Y - ey) * s
            v = -(X - ex) * s + (Y - ey) * c
            m = np.clip(1 - (u / er) ** 2 - (v / 0.9) ** 2, 0, 1)
            col *= (1 - m * 0.7)[..., None]
    if nose is not None:
        nx0, ny0, nr = nose
        m = np.clip(1.3 - (((X - nx0) / nr) ** 2 + ((Y - ny0) / (nr * 1.2)) ** 2) * 1.3, 0, 1)
        col = col * (1 - m[..., None]) + np.array([196, 120, 110])[None, None] * m[..., None]

    if ear_inner is not None:
        m = blur(ear_inner.astype(np.float32), SS * 0.8)[..., None]
        col = col * (1 - m * 0.7) + np.array([214, 160, 168])[None, None] * light[..., None] * m * 0.7
    # Furry silhouette from the softened shape, with a ragged, hairy edge.
    hb = blur(np.where(h > 0, h, -6).astype(np.float32), SS * 1.0)
    near = blur((h > 0).astype(np.float32), SS * 2.5)
    a = np.clip((hb + 1.6 + EDGE * 3.0) / 2.6, 0, 1) * np.clip(near * 4, 0, 1)   # long, wispy fur at the edge
    return np.clip(col, 0, 255), a


def to_cell(col, a):
    rgba = np.dstack([col, a * 255]).astype(np.uint8)
    img = Image.fromarray(rgba, "RGBA")
    # Premultiply-safe downsample.
    pm = np.asarray(img, np.float32)
    pm[..., :3] *= pm[..., 3:4] / 255
    small = Image.fromarray(pm.astype(np.uint8), "RGBA").resize((CW, CH), Image.LANCZOS)
    s = np.asarray(small, np.float32)
    alpha = s[..., 3:4]
    s[..., :3] = np.where(alpha > 0, s[..., :3] * 255 / np.maximum(alpha, 1), 0)
    return Image.fromarray(np.clip(s, 0, 255).astype(np.uint8), "RGBA")


def shadow_cell(a):
    m = blur(a.astype(np.float32), SS * 5) * 0.42
    rgba = np.zeros((H, W, 4), np.uint8)
    rgba[..., 3] = (m * 255).astype(np.uint8)
    return Image.fromarray(rgba, "RGBA").resize((CW, CH), Image.LANCZOS)


def walk_frame(k):
    ph = k / 8 * 2 * math.pi
    sw = math.sin(ph)
    sway = math.sin(ph + 0.6)
    # Body slightly bends with the gait.
    bend = sw * 0.06
    hy = sw * 0.8
    body = ellipsoid(-2, 0, 35, 20, 18, bend)
    hips = ellipsoid(-20, 0, 17, 19.5, 17.5)
    chest = ellipsoid(13, 0, 17, 19, 17)
    neck = ellipsoid(27, hy * 0.5, 10, 15, 16)    # big ruff
    head = ellipsoid(38, hy, 13.5, 14, 15)
    cheeks = ellipsoid(40, hy, 10, 17, 12)
    muzzle = ellipsoid(49, hy, 5.5, 7, 11)
    ear_l, in_l = ear(36, -9 + hy, 32, -21 + hy, 11, 15)
    ear_r, in_r = ear(36, 9 + hy, 32, 21 + hy, 11, 15)
    paws = []
    for side in (-1, 1):
        f = sw * side
        paws.append(ellipsoid(22 + f * 8, side * 14.5, 5.5, 4, 5))
        paws.append(ellipsoid(-24 - f * 8, side * 15, 6, 4.5, 5))
    tail_pts = []
    for j in range(9):
        t = j / 8
        tail_pts.append((-34 - t * 40, sway * 13 * t * t + math.sin(t * 3 + ph) * 3 * t))
    tail = tube(tail_pts, 8.5, 10.5)               # bushy plume
    h = smooth_union(body, hips, chest, neck, head, cheeks, muzzle, ear_l, ear_r, tail, *paws)

    belly = np.clip(1 - np.clip(h, 0, None) / 7, 0, 1) * 0.5 * (h > 0)
    # Grey points: tail (darkening to the tip), ears, mask around the eyes, a hint on the hips.
    points = np.clip((-34 - X) / 14, 0, 1) * (0.75 + 0.25 * np.clip((-60 - X) / 20, 0, 1))
    points = np.maximum(points, in_l | in_r) * 1.0
    ears_out = (ear_l > 0) | (ear_r > 0)
    points = np.maximum(points, ears_out * 0.85)
    mask = np.exp(-(((X - 44) / 5) ** 2 + ((np.abs(Y - hy) - 6) / 4) ** 2)) * 0.7
    mask += np.exp(-(((X - 37) / 5) ** 2 + ((Y - hy) / 6) ** 2)) * 0.3
    points = np.maximum(points, np.clip(mask, 0, 0.8))
    points = np.maximum(points, np.exp(-(((X + 18) / 16) ** 2 + (Y / 9) ** 2)) * 0.3)
    col, a = shade(h, blur(points.astype(np.float32), SS * 1.5), np.clip(belly, 0, 0.8),
                   eyes=[(45.5, -6 + hy, 2.3), (45.5, 6 + hy, 2.3)],
                   nose=(54, hy, 1.6), ear_inner=in_l | in_r)
    return col, a


def sleep_frame():
    # Curled up: round body, head tucked on the side, tail wrapped around.
    body = ellipsoid(-2, 3, 31, 28, 18)
    haunch = ellipsoid(-10, 8, 20, 19, 20)
    head = ellipsoid(13, -14, 13, 12, 21, 0.6)
    muzzle = ellipsoid(22, -8, 6, 6.5, 17, 0.6)
    ear_l, in_l = ear(7, -21, 0, -30, 10, 22)
    ear_r, in_r = ear(17, -24, 19, -35, 10, 22)
    paw = ellipsoid(26, -2, 6, 4.5, 7, 0.4)
    tail_pts = []
    for j in range(14):
        t = j / 13
        ang = math.radians(-60 + t * 210)
        r = 33 - t * 2
        tail_pts.append((math.cos(ang) * r, 3 + math.sin(ang) * r * 0.92))
    tail = [np.where(t > 0, t + 3, t) for t in [tube(tail_pts, 9, 10)]][0]
    tail_mask = tube(tail_pts, 9, 10) > 0
    h = smooth_union(body, haunch, head, muzzle, ear_l, ear_r, paw, tail)
    belly = np.clip(1 - np.clip(h, 0, None) / 6, 0, 1) * 0.5 * (h > 0)
    points = tail_mask * 0.85
    points = np.maximum(points, ((ear_l > 0) | (ear_r > 0)) * 0.85)
    points = np.maximum(points, np.exp(-(((X - 18) / 7) ** 2 + ((Y + 16) / 6) ** 2)) * 0.75)
    col, a = shade(h, blur(points.astype(np.float32), SS * 1.5), np.clip(belly, 0, 0.8),
                   closed_eyes=[(15, -19, 2.6, 0.9), (21, -13, 2.6, 0.9)],
                   nose=(26.5, -7.5, 1.5), ear_inner=in_l | in_r)
    return col, a


if __name__ == "__main__":
    frames = [walk_frame(k) for k in range(8)] + [sleep_frame()]
    sheet = Image.new("RGBA", (CW * len(frames), CH * 2), (0, 0, 0, 0))
    for i, (col, a) in enumerate(frames):
        sheet.paste(to_cell(col, a), (i * CW, 0))
        sheet.paste(shadow_cell(a), (i * CW, CH))
    sheet.save(OUT)
    print("ok", sheet.size)
