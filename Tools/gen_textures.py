"""Generator tekstur lingkungan (tileable + normal map) dan tekstur UI (panel, tombol, ikon).

Pemakaian: python Tools/gen_textures.py <folder Assets>
"""
import math
import os
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

ASSETS = sys.argv[1]
ENV_DIR = os.path.join(ASSETS, "Textures", "Environment")
UI_DIR = os.path.join(ASSETS, "Resources", "UI")
os.makedirs(ENV_DIR, exist_ok=True)
os.makedirs(UI_DIR, exist_ok=True)

rng = np.random.default_rng(11)


# ============================================================
# NOISE HELPERS (tileable)
# ============================================================

def tile_noise(w, h, cells, octaves=4, seed=0):
    """Value noise yang bisa diulang (tileable)."""
    r = np.random.default_rng(seed)
    out = np.zeros((h, w))
    amp, total = 1.0, 0.0
    for o in range(octaves):
        c = cells * 2 ** o
        grid = r.random((c, c))
        ys = np.linspace(0, c, h, endpoint=False)
        xs = np.linspace(0, c, w, endpoint=False)
        y0 = np.floor(ys).astype(int)
        x0 = np.floor(xs).astype(int)
        fy = ys - y0
        fx = xs - x0
        fy = fy * fy * (3 - 2 * fy)
        fx = fx * fx * (3 - 2 * fx)
        y1 = (y0 + 1) % c
        x1 = (x0 + 1) % c
        g00 = grid[np.ix_(y0, x0)]
        g01 = grid[np.ix_(y0, x1)]
        g10 = grid[np.ix_(y1, x0)]
        g11 = grid[np.ix_(y1, x1)]
        top = g00 * (1 - fx) + g01 * fx
        bot = g10 * (1 - fx) + g11 * fx
        out += amp * (top * (1 - fy)[:, None] + bot * fy[:, None])
        total += amp
        amp *= 0.5
    return out / total


def normal_from_height(height, strength=4.0):
    h = height.astype(np.float64)
    dx = (np.roll(h, -1, axis=1) - np.roll(h, 1, axis=1)) * strength
    dy = (np.roll(h, -1, axis=0) - np.roll(h, 1, axis=0)) * strength
    nx, ny, nz = -dx, dy, np.ones_like(h)
    length = np.sqrt(nx * nx + ny * ny + nz * nz)
    n = np.stack([nx / length, ny / length, nz / length], axis=-1)
    return Image.fromarray(((n * 0.5 + 0.5) * 255).astype(np.uint8), "RGB")


def save_env(name, rgb, height, strength=4.0):
    Image.fromarray(np.clip(rgb * 255, 0, 255).astype(np.uint8), "RGB").save(os.path.join(ENV_DIR, name + ".png"))
    normal_from_height(height, strength).save(os.path.join(ENV_DIR, name + "_n.png"))
    print("env", name)


def colorize(base, value):
    return np.clip(np.asarray(base)[None, None, :] * value[:, :, None], 0, 1)


S = 512

# ---------- Lantai ubin (4x4 ubin per tekstur = 2 m) ----------
n = tile_noise(S, S, 8, 5, 1)
yy, xx = np.mgrid[0:S, 0:S]
tile = S // 4
gx = np.minimum(xx % tile, tile - 1 - xx % tile)
gy = np.minimum(yy % tile, tile - 1 - yy % tile)
grout = np.clip(np.minimum(gx, gy) / 3.0, 0, 1)
tile_id = (yy // tile) * 4 + (xx // tile)
tile_var = rng.uniform(0.9, 1.08, 16)[tile_id]
value = (0.55 + 0.25 * n) * tile_var * (0.45 + 0.55 * grout)
save_env("floor_tile", colorize([0.62, 0.65, 0.7], value), grout * 0.8 + n * 0.2)

# ---------- Beton ----------
n = tile_noise(S, S, 6, 6, 2)
stain = tile_noise(S, S, 3, 3, 3)
value = 0.5 + 0.35 * n - 0.15 * np.clip(stain - 0.55, 0, 1) * 3
save_env("floor_concrete", colorize([0.6, 0.6, 0.62], value), n, 2.5)

# ---------- Lantai lab (ubin besar 2x2, terang) ----------
n = tile_noise(S, S, 8, 4, 4)
tile = S // 2
gx = np.minimum(xx % tile, tile - 1 - xx % tile)
gy = np.minimum(yy % tile, tile - 1 - yy % tile)
seam = np.clip(np.minimum(gx, gy) / 2.0, 0, 1)
value = (0.78 + 0.1 * n) * (0.55 + 0.45 * seam)
save_env("floor_lab", colorize([0.85, 0.9, 0.93], value), seam * 0.7 + n * 0.1)

# ---------- Lantai kayu (papan) ----------
plank_h = S // 8
row = yy // plank_h
offset = (rng.integers(0, S, 8))[row]
grain = tile_noise(S, S, 4, 5, 5)
streak = np.sin((xx + offset) / S * math.pi * 2 * 6 + grain * 8) * 0.5 + 0.5
py = np.minimum(yy % plank_h, plank_h - 1 - yy % plank_h)
px = (xx + offset) % (S // 2)
px = np.minimum(px, S // 2 - 1 - px)
edge = np.clip(np.minimum(py, px) / 2.0, 0, 1)
plank_var = rng.uniform(0.85, 1.1, 8)[row]
value = (0.55 + 0.2 * streak + 0.15 * grain) * plank_var * (0.5 + 0.5 * edge)
save_env("floor_wood", colorize([0.62, 0.42, 0.28], value), edge * 0.6 + streak * 0.15, 3)

# ---------- Panel dinding logam (512 = 2 m lebar, 768 = 3 m tinggi) ----------
H = 768
yy2, xx2 = np.mgrid[0:H, 0:S]
n = tile_noise(S, S, 8, 5, 6)
n = np.vstack([n, n[: H - S]])
panel_w = S // 2
panel_h = H // 3
px = np.minimum(xx2 % panel_w, panel_w - 1 - xx2 % panel_w)
py = np.minimum(yy2 % panel_h, panel_h - 1 - yy2 % panel_h)
seam = np.clip(np.minimum(px, py) / 3.0, 0, 1)
bevel = np.clip(np.minimum(px, py) / 12.0, 0, 1)
value = (0.62 + 0.18 * n) * (0.5 + 0.5 * seam) * (0.9 + 0.1 * bevel)
# baseboard gelap di bawah (y gambar = atas, jadi baris terakhir = bawah dinding)
base = yy2 > H - 64
value = np.where(base, value * 0.45, value)
height = seam * 0.6 + bevel * 0.3 + n * 0.1
# paku keling
for cx in range(16, S, panel_w // 2):
    for cy in (12, panel_h - 12, panel_h + 12, 2 * panel_h - 12, 2 * panel_h + 12):
        d = np.sqrt((xx2 - cx) ** 2 + (yy2 - cy) ** 2)
        rivet = np.clip(1 - d / 4.0, 0, 1)
        height += rivet * 0.5
        value += rivet * 0.15
rgb = colorize([0.55, 0.6, 0.68], value)
Image.fromarray(np.clip(rgb * 255, 0, 255).astype(np.uint8), "RGB").save(os.path.join(ENV_DIR, "wall_panel.png"))
normal_from_height(height, 4).save(os.path.join(ENV_DIR, "wall_panel_n.png"))
print("env wall_panel")

# ---------- Garis hazard (dinding rendah / cover) ----------
n = tile_noise(S, S, 8, 5, 7)
stripe = ((xx + yy) // (S // 8)) % 2
yellow = np.array([0.95, 0.72, 0.12])
black = np.array([0.08, 0.08, 0.09])
rgb = np.where(stripe[:, :, None] == 0, yellow, black)
wear = np.clip((n - 0.62) * 4, 0, 1)
rgb = rgb * (1 - wear[:, :, None]) + np.array([0.35, 0.35, 0.36]) * wear[:, :, None]
rgb *= (0.85 + 0.15 * n)[:, :, None]
save_env("hazard", rgb, n * 0.3 + stripe * 0.1, 2)

# ---------- Logam ventilasi (pelat dengan lubang-lubang) ----------
n = tile_noise(S, S, 8, 4, 8)
cell = S // 16
cx = (xx % cell) - cell / 2
cy = (yy % cell) - cell / 2
hole = np.clip((np.sqrt(cx * cx + cy * cy) - cell * 0.22) / 2.0, 0, 1)
value = (0.45 + 0.15 * n) * (0.25 + 0.75 * hole)
save_env("vent_metal", colorize([0.6, 0.63, 0.66], value), hole * 0.8, 3)


# ---------- Rak server (dengan LED, + emission map) ----------
n = tile_noise(S, S, 8, 4, 9)
unit_h = S // 8
uy = np.minimum(yy % unit_h, unit_h - 1 - yy % unit_h)
edge = np.clip(uy / 3.0, 0, 1)
side = np.clip(np.minimum(xx, S - 1 - xx) / 10.0, 0, 1)
vent = ((xx // 6) % 2 == 0) & (xx > S * 0.45) & (xx < S * 0.9) & (uy > 8)
value = (0.16 + 0.06 * n) * (0.6 + 0.4 * edge) * (0.7 + 0.3 * side)
value = np.where(vent, value * 0.55, value)
rgb = colorize([0.75, 0.8, 0.9], value)
emit = np.zeros((S, S, 3))
r2 = np.random.default_rng(21)
for unit in range(8):
    cy0 = unit * unit_h + unit_h // 2
    for k in range(4):
        cx0 = int(S * 0.1) + k * 22
        col = [(0.2, 1.0, 0.4), (0.3, 0.7, 1.0), (1.0, 0.7, 0.2)][r2.integers(0, 3)]
        if r2.random() < 0.8:
            d = np.sqrt((xx - cx0) ** 2 + (yy - cy0) ** 2)
            glow = np.clip(1 - d / 5.0, 0, 1)
            emit += glow[:, :, None] * np.array(col)
rgb = np.clip(rgb + emit * 0.8, 0, 1)
save_env("server_rack", rgb, edge * 0.5 + n * 0.1, 3)
Image.fromarray(np.clip(emit * 255, 0, 255).astype(np.uint8), "RGB").save(os.path.join(ENV_DIR, "server_rack_e.png"))
print("env server_rack_e")


# ============================================================
# UI
# ============================================================

def rounded(size, radius, fill, border=None, border_width=0, scale=4):
    w, h = size
    big = Image.new("RGBA", (w * scale, h * scale), (0, 0, 0, 0))
    d = ImageDraw.Draw(big)
    if border:
        d.rounded_rectangle([0, 0, w * scale - 1, h * scale - 1], radius * scale, fill=border)
        inset = border_width * scale
        d.rounded_rectangle([inset, inset, w * scale - 1 - inset, h * scale - 1 - inset], max(0, (radius - border_width) * scale), fill=fill)
    else:
        d.rounded_rectangle([0, 0, w * scale - 1, h * scale - 1], radius * scale, fill=fill)
    img = big.resize((w, h), Image.LANCZOS)
    return img


def save_ui(name, img):
    img.save(os.path.join(UI_DIR, name + ".png"))
    print("ui", name)


save_ui("panel", rounded((64, 64), 12, (14, 19, 28, 230), (120, 140, 170, 70), 1.5))
save_ui("panel_solid", rounded((64, 64), 12, (12, 16, 24, 248), (255, 201, 74, 120), 1.5))
save_ui("button", rounded((64, 64), 8, (26, 33, 46, 240), (255, 255, 255, 45), 1.5))
save_ui("button_hover", rounded((64, 64), 8, (58, 48, 20, 245), (255, 201, 74, 230), 2))
save_ui("button_active", rounded((64, 64), 8, (255, 201, 74, 255), (255, 230, 150, 255), 2))
save_ui("bar", rounded((32, 16), 6, (255, 255, 255, 255)))
save_ui("pill", rounded((32, 32), 15, (255, 255, 255, 255)))
save_ui("slot", rounded((64, 64), 10, (18, 24, 34, 235), (255, 255, 255, 40), 1.5))

# Vignette radial (dipakai untuk alarm merah & efek gelap)
V = 256
yv, xv = np.mgrid[0:V, 0:V]
d = np.sqrt(((xv - V / 2) / (V / 2)) ** 2 + ((yv - V / 2) / (V / 2)) ** 2)
alpha = np.clip((d - 0.55) / 0.6, 0, 1) ** 1.6
vig = np.zeros((V, V, 4), np.uint8)
vig[:, :, :3] = 255
vig[:, :, 3] = (alpha * 255).astype(np.uint8)
save_ui("vignette", Image.fromarray(vig, "RGBA"))

# Gradien horizontal (latar menu)
G = 256
grad = np.zeros((4, G, 4), np.uint8)
grad[:, :, :3] = 0
grad[:, :, 3] = (np.clip(1 - np.linspace(0, 1, G) ** 1.3, 0, 1) * 255).astype(np.uint8)
save_ui("gradient_left", Image.fromarray(grad, "RGBA"))


# ---------- Ikon (putih, di-tint di game) ----------
def icon(draw_fn, name, size=128, scale=4):
    big = Image.new("RGBA", (size * scale, size * scale), (0, 0, 0, 0))
    d = ImageDraw.Draw(big)
    draw_fn(d, size * scale)
    img = big.resize((size, size), Image.LANCZOS)
    save_ui("icon_" + name, img)


W = (255, 255, 255, 255)
T = (0, 0, 0, 0)


def i_keycard(d, s):
    d.rounded_rectangle([s * .12, s * .25, s * .88, s * .75], s * .07, fill=W)
    d.rounded_rectangle([s * .2, s * .36, s * .38, s * .52], s * .03, fill=T)
    d.rectangle([s * .12, s * .6, s * .88, s * .66], fill=T)
    d.ellipse([s * .66, s * .33, s * .8, s * .47], fill=T)


def i_screwdriver(d, s):
    d.rounded_rectangle([s * .1, s * .38, s * .45, s * .62], s * .1, fill=W)
    d.rectangle([s * .45, s * .46, s * .82, s * .54], fill=W)
    d.polygon([(s * .82, s * .44), (s * .92, s * .5), (s * .82, s * .56)], fill=W)
    for k in range(3):
        x = s * (.17 + k * .08)
        d.rectangle([x, s * .42, x + s * .03, s * .58], fill=T)


def i_can(d, s):
    d.rectangle([s * .3, s * .22, s * .7, s * .8], fill=W)
    d.ellipse([s * .3, s * .14, s * .7, s * .3], fill=W)
    d.ellipse([s * .3, s * .72, s * .7, s * .88], fill=W)
    d.ellipse([s * .36, s * .17, s * .64, s * .27], fill=T)
    d.rectangle([s * .3, s * .42, s * .7, s * .5], fill=T)


def i_prototype(d, s):
    pts = [(s * .5 + s * .38 * math.cos(math.radians(a)), s * .5 + s * .38 * math.sin(math.radians(a))) for a in range(30, 390, 60)]
    d.polygon(pts, fill=W)
    pts2 = [(s * .5 + s * .2 * math.cos(math.radians(a)), s * .5 + s * .2 * math.sin(math.radians(a))) for a in range(30, 390, 60)]
    d.polygon(pts2, fill=T)
    d.ellipse([s * .43, s * .43, s * .57, s * .57], fill=W)


def i_eye(d, s):
    d.ellipse([s * .08, s * .28, s * .92, s * .72], fill=W)
    d.ellipse([s * .16, s * .34, s * .84, s * .66], fill=T)
    d.ellipse([s * .36, s * .32, s * .64, s * .68], fill=W)
    d.ellipse([s * .45, s * .41, s * .55, s * .51], fill=T)


def i_sound(d, s):
    d.polygon([(s * .12, s * .38), (s * .3, s * .38), (s * .5, s * .2), (s * .5, s * .8), (s * .3, s * .62), (s * .12, s * .62)], fill=W)
    for k, r in enumerate((.18, .3)):
        d.arc([s * .5 - s * r, s * .5 - s * r, s * .5 + s * r, s * .5 + s * r], -50, 50, fill=W, width=int(s * .06))


def person(d, s, head, body_pts, width):
    d.ellipse(head, fill=W)
    d.line(body_pts, fill=W, width=int(s * width), joint="curve")


def i_stand(d, s):
    person(d, s, [s * .42, s * .08, s * .58, s * .24], [(s * .5, s * .28), (s * .5, s * .58), (s * .38, s * .9)], .1)
    d.line([(s * .5, s * .58), (s * .62, s * .9)], fill=W, width=int(s * .1))
    d.line([(s * .32, s * .45), (s * .5, s * .34), (s * .68, s * .45)], fill=W, width=int(s * .08))


def i_crouch(d, s):
    person(d, s, [s * .5, s * .3, s * .66, s * .46], [(s * .52, s * .5), (s * .4, s * .66), (s * .58, s * .76), (s * .5, s * .92)], .1)
    d.line([(s * .4, s * .66), (s * .3, s * .92)], fill=W, width=int(s * .1))
    d.line([(s * .48, s * .56), (s * .7, s * .62)], fill=W, width=int(s * .08))


def i_run(d, s):
    person(d, s, [s * .55, s * .08, s * .71, s * .24], [(s * .58, s * .3), (s * .48, s * .58), (s * .3, s * .7), (s * .2, s * .9)], .1)
    d.line([(s * .48, s * .58), (s * .66, s * .72), (s * .74, s * .92)], fill=W, width=int(s * .1))
    d.line([(s * .3, s * .38), (s * .55, s * .36), (s * .74, s * .5)], fill=W, width=int(s * .08))


def i_diamond(d, s):
    d.polygon([(s * .5, s * .06), (s * .9, s * .5), (s * .5, s * .94), (s * .1, s * .5)], fill=W)
    d.polygon([(s * .5, s * .26), (s * .7, s * .5), (s * .5, s * .74), (s * .3, s * .5)], fill=T)


def i_arrow(d, s):
    d.polygon([(s * .9, s * .5), (s * .2, s * .12), (s * .36, s * .5), (s * .2, s * .88)], fill=W)


def i_check(d, s):
    d.line([(s * .18, s * .52), (s * .42, s * .76), (s * .84, s * .26)], fill=W, width=int(s * .14), joint="curve")


def i_dot(d, s):
    d.ellipse([s * .3, s * .3, s * .7, s * .7], fill=W)


def i_ring(d, s):
    d.ellipse([s * .2, s * .2, s * .8, s * .8], outline=W, width=int(s * .1))


def i_alert(d, s):
    d.polygon([(s * .5, s * .08), (s * .94, s * .88), (s * .06, s * .88)], fill=W)
    d.rectangle([s * .45, s * .33, s * .55, s * .62], fill=T)
    d.ellipse([s * .44, s * .7, s * .56, s * .8], fill=T)


def i_lock(d, s):
    d.arc([s * .3, s * .1, s * .7, s * .55], 180, 360, fill=W, width=int(s * .09))
    d.line([(s * .345, s * .33), (s * .345, s * .45)], fill=W, width=int(s * .09))
    d.line([(s * .655, s * .33), (s * .655, s * .45)], fill=W, width=int(s * .09))
    d.rounded_rectangle([s * .2, s * .42, s * .8, s * .9], s * .06, fill=W)
    d.ellipse([s * .44, s * .55, s * .56, s * .67], fill=T)


def i_bulb(d, s):
    d.ellipse([s * .25, s * .08, s * .75, s * .58], fill=W)
    d.rectangle([s * .37, s * .5, s * .63, s * .72], fill=W)
    d.rectangle([s * .37, s * .76, s * .63, s * .82], fill=W)
    d.rectangle([s * .42, s * .86, s * .58, s * .92], fill=W)


for fn, name in [(i_keycard, "keycard"), (i_screwdriver, "screwdriver"), (i_can, "can"), (i_prototype, "prototype"),
                 (i_eye, "eye"), (i_sound, "sound"), (i_stand, "stand"), (i_crouch, "crouch"), (i_run, "run"),
                 (i_diamond, "diamond"), (i_arrow, "arrow"), (i_check, "check"), (i_dot, "dot"), (i_ring, "ring"),
                 (i_alert, "alert"), (i_lock, "lock"), (i_bulb, "bulb")]:
    icon(fn, name)

print("done")
