"""Render small top-down props: things lost under the rug (rug/items.png) and
the robot vacuum (rug/robot.png).

Each prop is a lit height field with a material colour, like the cat, so it
sits in the same light as the rug. Sheets hold the sprite in row 0 and its
soft shadow in row 1.

Run: python3 tools/make_props.py
"""
import math
import os

import numpy as np
from PIL import Image

OUT = os.path.join(os.path.dirname(__file__), "..", "rug")
SS = 3
L = np.array([-0.35, -0.5, 0.8]); L /= np.linalg.norm(L)


def blur(a, sigma):
    r = int(sigma * 3) + 1
    k = np.exp(-(np.arange(-r, r + 1) ** 2) / (2 * sigma * sigma)); k /= k.sum()
    a = np.pad(a, r, mode="edge")
    a = sum(k[i] * a[:, i:i + a.shape[1] - 2 * r] for i in range(2 * r + 1))
    a = sum(k[i] * a[i:i + a.shape[0] - 2 * r, :] for i in range(2 * r + 1))
    return a.astype(np.float32)


class Canvas:
    def __init__(self, cw, ch, seed):
        self.cw, self.ch = cw, ch
        self.W, self.H = cw * SS, ch * SS
        yy, xx = np.mgrid[0:self.H, 0:self.W].astype(np.float32)
        self.X = (xx - self.W / 2) / SS
        self.Y = (yy - self.H / 2) / SS
        self.rng = np.random.default_rng(seed)

    def ellipsoid(self, cx, cy, rx, ry, rz, rot=0.0, flat=0.0):
        c, s = math.cos(rot), math.sin(rot)
        u = ((self.X - cx) * c + (self.Y - cy) * s) / rx
        v = (-(self.X - cx) * s + (self.Y - cy) * c) / ry
        q = 1 - u * u - v * v
        h = rz * np.sqrt(np.clip(q, 0, 1)) ** (1 - flat)
        return np.where(q > 0, h, -1)

    def cylinder(self, cx, cy, r, height, bevel=1.5):
        d = np.sqrt((self.X - cx) ** 2 + (self.Y - cy) ** 2)
        edge = np.clip((r - d) / bevel, 0, 1)
        return np.where(d < r, height * (0.6 + 0.4 * np.sqrt(edge)), -1)

    def box(self, cx, cy, hw, hh, height, rot=0.0, bevel=1.5):
        c, s = math.cos(rot), math.sin(rot)
        u = (self.X - cx) * c + (self.Y - cy) * s
        v = -(self.X - cx) * s + (self.Y - cy) * c
        d = np.minimum(hw - np.abs(u), hh - np.abs(v))
        return np.where(d > 0, height * (0.6 + 0.4 * np.sqrt(np.clip(d / bevel, 0, 1))), -1)

    def noise(self, cell):
        gh, gw = self.H // cell + 3, self.W // cell + 3
        g = Image.fromarray((self.rng.random((gh, gw)) * 255).astype(np.uint8))
        g = g.resize((gw * cell, gh * cell), Image.BICUBIC).crop((0, 0, self.W, self.H))
        return np.asarray(g, np.float32) / 255 - 0.5

    def render(self, h, albedo, shine=0.0, rough=0.0):
        inside = h > 0
        hz = np.where(inside, h, 0).astype(np.float32)
        hs = blur(hz, SS * 0.8)
        gy, gx = np.gradient(hs)
        nx, ny, nz = -gx * SS, -gy * SS, np.ones_like(hs)
        nl = np.sqrt(nx * nx + ny * ny + nz * nz)
        ndl = np.clip((nx * L[0] + ny * L[1] + nz * L[2]) / nl, 0, 1)
        light = 0.4 + 0.75 * ndl
        if rough:
            light *= 1 + self.noise(1) * rough
        col = albedo * light[..., None]
        if shine:
            # Blinn-ish highlight for metal and plastic.
            hx, hy, hz2 = L[0], L[1], L[2] + 1
            hl = math.sqrt(hx * hx + hy * hy + hz2 * hz2)
            spec = np.clip((nx * hx + ny * hy + nz * hz2) / (nl * hl), 0, 1) ** 40
            col += (spec * 255 * shine)[..., None]
        a = blur(inside.astype(np.float32), SS * 0.5)
        return np.clip(col, 0, 255), np.clip(a * 1.2, 0, 1)

    def cell(self, col, a):
        pm = np.dstack([col * a[..., None], a * 255]).astype(np.uint8)
        small = np.asarray(Image.fromarray(pm, "RGBA").resize((self.cw, self.ch), Image.LANCZOS), np.float32)
        al = small[..., 3:4]
        small[..., :3] = np.where(al > 0, small[..., :3] * 255 / np.maximum(al, 1), 0)
        return Image.fromarray(np.clip(small, 0, 255).astype(np.uint8), "RGBA")

    def shadow(self, a, spread=3.0, strength=0.45):
        m = blur(a.astype(np.float32), SS * spread) * strength
        rgba = np.zeros((self.H, self.W, 4), np.uint8)
        rgba[..., 3] = (np.clip(m, 0, 1) * 255).astype(np.uint8)
        return Image.fromarray(rgba, "RGBA").resize((self.cw, self.ch), Image.LANCZOS)


def solid(c, rgb):
    return np.broadcast_to(np.array(rgb, np.float32), c.X.shape + (3,)).copy()


def coin(c):
    h = c.cylinder(0, 0, 13, 2.2, bevel=2.5)
    ring = np.abs(np.sqrt(c.X ** 2 + c.Y ** 2) - 10.5) < 0.8
    h = np.where(ring & (h > 0), h + 0.5, h)
    relief = c.ellipsoid(0, 0, 5, 6, 1.2)
    h = np.where(relief > 0, np.maximum(h, 2.2 + relief), h)
    return h, solid(c, (204, 160, 72)), 1.0, 0.15


def key(c):
    bow = c.cylinder(-14, 0, 8, 2.5)
    hole = np.sqrt((c.X + 16) ** 2 + c.Y ** 2) < 3
    bow = np.where(hole, -1, bow)
    shaft = c.box(4, 0, 16, 2.2, 2.2)
    teeth = np.maximum(c.box(13, 3.5, 2, 2, 2), c.box(17, 3, 1.5, 1.5, 2))
    teeth = np.maximum(teeth, c.box(8, 3, 1.5, 1.8, 2))
    h = np.maximum(np.maximum(bow, shaft), teeth)
    return h, solid(c, (196, 172, 104)), 1.0, 0.1


def sock(c):
    t = np.clip((c.X + 18) / 38, 0, 1)
    body = c.box(-6, 0, 14, 7, 4, bevel=4)
    foot = c.ellipsoid(10, 4, 10, 7, 4, 0.6, flat=0.5)
    h = np.maximum(body, foot)
    stripes = (np.sin(c.X * 0.9) > 0.3)
    alb = np.where(stripes[..., None], np.array([186, 64, 60], np.float32), np.array([228, 222, 210], np.float32))
    cuff = (c.X < -16)
    alb = np.where(cuff[..., None], np.array([186, 64, 60], np.float32), alb)
    return h, alb, 0.0, 0.35


def button(c):
    h = c.cylinder(0, 0, 8, 2.5, bevel=2)
    rim = np.sqrt(c.X ** 2 + c.Y ** 2) > 6.5
    h = np.where((h > 0) & rim, h + 0.6, h)
    for (hx, hy) in [(-2, -2), (2, -2), (-2, 2), (2, 2)]:
        h = np.where(np.sqrt((c.X - hx) ** 2 + (c.Y - hy) ** 2) < 1.1, 0.8, h)
    return h, solid(c, (52, 96, 150)), 0.5, 0.05


def candy(c):
    body = c.ellipsoid(0, 0, 8, 6, 5)
    wings = [c.box(s * 12, 0, 4, 5.5 - 0, 1.5, 0, bevel=1) for s in (-1, 1)]
    h = np.maximum(body, np.maximum(*wings))
    stripes = np.sin((c.X + c.Y) * 1.2) > 0
    alb = np.where(stripes[..., None], np.array([214, 60, 84], np.float32), np.array([244, 236, 228], np.float32))
    return h, alb, 0.8, 0.05


def paper(c):
    h = c.ellipsoid(0, 0, 14, 12, 7, flat=0.6)
    h = np.where(h > 0, h + c.noise(4) * 5 + c.noise(2) * 2, h)
    return h, solid(c, (236, 234, 226)), 0.0, 0.1


def spilled_mug(c):
    # The mug knocked off the table: on its side, coffee spreading on the floor.
    puddle = c.ellipsoid(-8, 4, 20, 13, 0.8, 0.3, flat=0.9)
    puddle = np.where(puddle > 0, puddle + c.noise(4) * 0.4, puddle)
    body = c.box(4, -2, 11, 9, 14, 0.3, bevel=5)
    rim = c.ellipsoid(-6, -5.5, 4, 9, 16, 0.3)
    handle = (np.abs(np.sqrt((c.X - 10) ** 2 + (c.Y - 11) ** 2) - 5) < 1.8) & (c.Y > 8)
    h = np.maximum(np.maximum(puddle, body), rim)
    h = np.where(handle, 9, h)
    alb = solid(c, (238, 236, 232))
    wet = (puddle > 0) & (body <= 0) & (rim <= 0) & ~handle
    alb = np.where(wet[..., None], np.array([62, 36, 20], np.float32), alb)
    inside = np.sqrt(((c.X + 7) / 2.6) ** 2 + ((c.Y + 6) / 7) ** 2) < 1
    alb = np.where(inside[..., None], np.array([40, 26, 16], np.float32), alb)
    return h, alb, 0.9, 0.04


def make_items():
    makers = [coin, key, sock, button, candy, paper, spilled_mug]
    cw = ch = 64
    sheet = Image.new("RGBA", (cw * len(makers), ch * 2), (0, 0, 0, 0))
    for i, fn in enumerate(makers):
        c = Canvas(cw, ch, 30 + i)
        h, alb, shine, rough = fn(c)
        col, a = c.render(h, alb, shine, rough)
        sheet.paste(c.cell(col, a), (i * cw, 0))
        sheet.paste(c.shadow(a, 1.5, 0.5), (i * cw, ch))
    sheet.save(os.path.join(OUT, "items.png"))


def make_robot():
    """A round robot vacuum seen from above, facing +x."""
    cw = ch = 128
    c = Canvas(cw, ch, 50)
    r = np.sqrt(c.X ** 2 + c.Y ** 2)
    body = c.cylinder(0, 0, 44, 9, bevel=6)
    bumper = (np.abs(np.arctan2(c.Y, c.X)) < 1.2) & (r > 38) & (r < 44)
    lid = c.cylinder(0, 0, 36, 10.5, bevel=3)
    dome = c.cylinder(16, 0, 9, 14, bevel=4)
    h = np.maximum(np.maximum(body, lid), dome)
    alb = solid(c, (54, 56, 62))
    alb = np.where((r < 36)[..., None], np.array([34, 35, 40], np.float32), alb)
    alb = np.where(bumper[..., None], np.array([24, 24, 28], np.float32), alb)
    alb = np.where((np.sqrt((c.X - 16) ** 2 + c.Y ** 2) < 9)[..., None], np.array([70, 74, 82], np.float32), alb)
    # Status light and a brushed ring.
    led = np.sqrt((c.X + 20) ** 2 + c.Y ** 2) < 2.4
    ring = np.abs(r - 26) < 0.6
    alb = np.where(ring[..., None], np.array([90, 92, 100], np.float32), alb)
    col, a = c.render(h, alb, shine=0.6, rough=0.04)
    col = np.where(led[..., None], np.array([90, 220, 255], np.float32), col)
    # Side brush (front right), drawn as thin bristles.
    for k in range(3):
        ang = k * 2 * math.pi / 3
        bx, by = 30, 28
        for t in np.linspace(0, 1, 40):
            x = bx + math.cos(ang) * 14 * t
            y = by + math.sin(ang) * 14 * t
            m = np.sqrt((c.X - x) ** 2 + (c.Y - y) ** 2) < 0.7
            col = np.where(m[..., None], np.array([30, 30, 34], np.float32), col)
            a = np.where(m, 1.0, a)
    sheet = Image.new("RGBA", (cw, ch * 2), (0, 0, 0, 0))
    sheet.paste(c.cell(col, a), (0, 0))
    sheet.paste(c.shadow(a, 3.0, 0.45), (0, ch))
    sheet.save(os.path.join(OUT, "robot.png"))


if __name__ == "__main__":
    make_items()
    make_robot()
    print("ok")
