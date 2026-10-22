"""Cut the game's coat of arms parts back out of its atlases, re-render every vanilla crest from them and compare.

Usage: python tools/coa_parts.py [--out <dir>] [--sheet]

The game describes its arms in Maps/europe/coatOfArmsCrests.def: per crest and mode (shield, flag, banner,
small_shield) a division (texture with two tint channels g and r), ordinaries and charges (texture, one tint
channel r or b, position, scale, rotation). The part textures themselves (Assets/CoatOfArms/*.png) are not
shipped, only the finished atlases (coatOfArmsAtlas<mode>.png), so the parts are recovered from them:

- charge: against its crest painted without it (from the parts cut so far; passes until nothing new comes),
  per pixel colour = background * (1 - a) + a * w * tint gives a (coverage) and w (shade: the dark outlines).
  Other instances of it in the crest are split off by distance, a turned or mirrored instance is turned back.
  Kept at the size of that crest, with the point that the def's position refers to (the anchor) and the scale
  it was drawn at, so it can be drawn at any other. Where each one is cut goes to plugin/coa_recipes.txt: the
  plugin (Coa/CoaParts.cs) cuts the same at runtime, so no game art is shipped.
- division, ordinary: per mode, from every crest that uses it: per pixel the share of the second colour
  (division: r over g; ordinary: tint over what lies under it), median over the crests (charges on top only
  spoil some of them).
- pattern (fills a division channel): per pixel the weights of its r, g, b channels (the rest: black, the
  shading), from the crests where that channel shows.

Then every crest is painted again from the parts and compared with its atlas tile. Mean colour error per
crest and mode goes to <out>/report.txt, --sheet writes contact sheets (atlas tile, re-render, difference).

Writes into <out> (default: tools/out/coa, not committed): parts as PNG (charges: R = shade, A = coverage;
divisions and ordinaries: share as grey), report.txt and the sheets.
"""

from __future__ import annotations

import argparse
import collections
import math
from dataclasses import dataclass, field
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw

import urah_paths  # noqa: F401  (puts URAH_shared on sys.path)
from urah_paths import REPO, VANILLA
import dt  # noqa: E402
from koh2coa import MODES, tile_box  # noqa: E402

EUROPE = VANILLA / "Maps" / "europe"
DEFAULT_TINT = {"r": (255, 0, 0), "g": (0, 255, 0), "b": (0, 0, 255)}  # coatOfArmsMapper in coatOfArms.def


# ---------------------------------------------------------------- crests from the def

@dataclass
class Layer:
    tex: str  # file name without .png
    tints: dict[str, tuple]  # channel -> rgb
    patterns: dict[str, "Layer"] = field(default_factory=dict)  # channel -> pattern filling it (own tints, tiling)
    pos: tuple = (0.0, 0.0)
    scale: tuple = (1.0, 1.0)
    pivot: tuple = (0.5, 0.5)
    rot: float = 0.0
    tile: tuple = (1.0, 1.0)  # tile_x, tile_y (-1: mirrored)
    offset: tuple = (0.0, 0.0)


@dataclass
class Crest:
    cid: int
    division: dict[str, Layer] = field(default_factory=dict)  # mode -> layer
    ordinaries: dict[str, list] = field(default_factory=dict)
    charges: dict[str, list] = field(default_factory=dict)


def load_palette():
    pal = {}
    f = dt.load([VANILLA / "Defs" / "CoatOfArms" / "coatOfArms.def"]).child("CoatOfArmsPaletteColors")
    for k, c in f.children.items():
        pal[k] = tuple(int(x) for x in c.value.strip('"').split(","))
    return pal


def pair(f, key, default):
    c = f.child(key)
    if c is None or c.value is None:
        return default
    v = [float(x) for x in c.value.replace(",", " ").split()]
    return (v[0], v[1] if len(v) > 1 else v[0])


def num(f, key, default):
    c = f.child(key)
    return float(c.value) if c is not None and c.value is not None else default


def find_in_children(f, key):
    """CoatOfArmsUtility.FindInChildren: a child named key, else the first one found depth-first."""
    if key in f.children:
        return f.children[key]
    for c in f.children.values():
        if (r := find_in_children(c, key)) is not None:
            return r
    return None


def layer(f, pal, mode=None) -> Layer | None:
    """CoatOfArmsTexture(def, mode): texture and mapper from f, the transform from f's block for the mode if any."""
    t = f.child("t")
    if t is None or not t.value:
        return None
    tf = (find_in_children(f, mode) if mode else None) or f
    tints, patterns = {}, {}
    mp = f.child("mapper")
    for ch, m in (mp.children.items() if mp else ()):
        if m.child("tint") is not None:
            v = m.child("tint").value.strip('"')
            tints[ch] = pal[v] if v in pal else tuple(int(x) for x in v.split(","))
        if m.child("texture") is not None and m.child("texture").child("t") is not None:
            patterns[ch] = layer(m.child("texture"), pal, mode)
    if not tints and not patterns:  # no mapper: the channel keeps its own colour (TextureMapper.GetTintColor)
        tints = {"r": DEFAULT_TINT["r"]}
    return Layer(tex_name(t.value), tints, patterns, pair(tf, "position", (0.0, 0.0)), pair(tf, "scale", (1.0, 1.0)),
                 pair(tf, "pivot", (0.5, 0.5)), num(tf, "rotation", 0.0),
                 (num(tf, "tile_x", 1.0), num(tf, "tile_y", 1.0)), (num(tf, "offset_x", 0.0), num(tf, "offset_y", 0.0)))


def tex_name(v: str) -> str:
    return v.strip('"').split("/")[-1].removesuffix(".png")


def load_crests(pal) -> dict[int, Crest]:
    """Per crest and mode the layers the game draws, as CoatOfArmsCrest(DT.Field) builds them."""
    import copy
    root = dt.load([EUROPE / "coatOfArmsCrests.def"]).child("CoatOfArmsCrests.europe")
    crests = {}
    modes = list(MODES)  # CoatOfArmsModes order: shield first
    for key, f in root.children.items():
        c = Crest(int(key))
        base_div = f.child("division")
        for m in modes:
            d = f.child("division_" + m)
            if d is not None:
                lay = layer(d, pal, m)
            elif base_div is not None and find_in_children(f, m) is not None:
                lay = layer(base_div, pal, m)
            else:
                lay = copy.copy(c.division.get(modes[0])) if m != modes[0] else (layer(base_div, pal) if base_div else None)
            if lay:
                c.division[m] = lay
        for grp, out in (("ordinaries", c.ordinaries), ("charges", c.charges)):
            base_f = f.child(grp)
            base = [lay for x in (base_f.children.values() if base_f else ()) if (lay := layer(x, pal))]
            for i, m in enumerate(modes):
                g = f.child(f"{grp}_{m}")
                if g is not None and g.children:
                    lays = [lay for x in g.children.values() if (lay := layer(x, pal, m))]
                elif base_f is not None and base_f.children and \
                        any(find_in_children(x, m) is not None for x in base_f.children.values()):
                    lays = [layer(x, pal, m) for x in base_f.children.values() if find_in_children(x, m) is not None]
                    lays = [x for x in lays if x]
                    if grp == "ordinaries":  # the game keeps the base list here
                        lays = base
                else:
                    lays = None
                if i == 0 and lays is not None:
                    base = lays
                if lays is None:
                    lays = []
                    for x in base:
                        x = copy.copy(x)
                        if grp == "charges":  # CoatOfArmsUtility.ConvertChargeTransform
                            r = MODES[m][0] / MODES[modes[0]][0]
                            x.scale = (x.scale[0] * r, x.scale[1] * r)
                        lays.append(x)
                out[m] = lays
        crests[c.cid] = c
    return crests


# ---------------------------------------------------------------- atlases

class Atlases:
    def __init__(self):
        self.img = {m: np.asarray(Image.open(EUROPE / f"coatOfArmsAtlas{m}.png").convert("RGB")).astype(float)
                    for m in MODES}

    def tile(self, cid, m):
        w, h = MODES[m]
        x0, y0, x1, y1 = tile_box_np(self.img[m], cid, w, h)
        return self.img[m][y0:y1, x0:x1]


def tile_box_np(a, idx, w, h):
    cols = a.shape[1] // w
    c, r = idx % cols, idx // cols
    top = a.shape[0] - (r + 1) * h
    return c * w, top, c * w + w, top + h


def ratio(m):
    w, h = MODES[m]
    return min(w, h) / max(w, h)


# ---------------------------------------------------------------- charges

@dataclass
class Sprite:
    a: np.ndarray  # coverage
    w: np.ndarray  # shade
    anchor: tuple  # pixel (x, y) in the sprite where the def position points
    k: tuple  # draw scale (x, y) the sprite was cut at
    source: str = ""


def contrast(bg, tint):
    bg, tint = np.asarray(bg, float), np.asarray(tint, float)
    # how well a dark outline (w low) and the tint can be told apart from the field
    return min(np.linalg.norm(bg - tint), np.linalg.norm(bg)) * (1 - abs(np.dot(bg, tint)) /
                                                             (np.linalg.norm(bg) * np.linalg.norm(tint) + 1e-6) * 0.5)


def centre_px(lay: Layer, m):
    w, h = MODES[m]
    return lay.pos[0] * w, (1 - lay.pos[1]) * h


def decompose_bg(px, bg, tint):
    """px (..., 3) = bg * (1 - a) + a * w * tint with a background per pixel: least squares for p = a, q = a * w."""
    tint = np.asarray(tint, float)
    d = px - bg
    # normal equations of d = q * tint - p * bg per pixel
    tt = tint @ tint
    tb = bg @ tint
    bb = (bg * bg).sum(-1)
    dt_ = d @ tint
    db = (d * bg).sum(-1)
    det = tt * bb - tb * tb
    det = np.where(np.abs(det) < 1e-3, 1e-3, det)
    q = (dt_ * bb - db * tb) / det
    p = (dt_ * tb - db * tt) / det
    a = np.clip(p, 0, 1)
    w = np.where(a > 0.02, np.clip(q / np.maximum(a, 1e-6), 0, 1), 0)
    return a, w


def general_candidates(crests, sprites, masks):
    """Occurrences of charges not cut yet where everything else in the crest can be painted
    (other instances of the same charge are left out and split off by distance)."""
    out = collections.defaultdict(list)
    for c in crests.values():
        for m in MODES:
            ch = c.charges.get(m, [])
            for i, lay in enumerate(ch):
                if lay.tex in sprites or lay.pivot != (0.5, 0.5) or lay.patterns or len(lay.tints) != 1:
                    continue
                others = [x for x in ch if x.tex != lay.tex]
                if any(x.tex not in sprites or x.pivot != (0.5, 0.5) or x.patterns or not x.tints for x in others):
                    continue
                bg, missing = paint(c, m, sprites, masks, upto=i, skip=lay.tex)
                if missing:
                    continue
                occ = np.zeros(bg.shape[:2])
                for x in ch[i + 1:]:
                    if x.tex != lay.tex:
                        occ = np.maximum(occ, draw_charge(np.zeros(bg.shape[:2]), sprites[x.tex], x, m))
                twins = [centre_px(x, m) for j, x in enumerate(ch) if j != i and x.tex == lay.tex]
                tint = next(iter(lay.tints.values()))
                k = lay.scale[0] * ratio(m)
                score = k * min(MODES[m]) * contrast(bg.reshape(-1, 3).mean(0), tint) * (1 - occ.mean() * 3) \
                    * (0.5 if twins else 1) * (0.7 if lay.rot else 1)
                out[lay.tex].append((score, c.cid, m, lay, bg, tint, occ, twins, i))
    return out


def cut_general(atl: Atlases, cid, m, lay: Layer, bg, tint, occ, twins) -> Sprite | None:
    t = atl.tile(cid, m)
    a, w = decompose_bg(t, bg, tint)
    resid = np.abs(t - bg).sum(-1)
    keep = (resid > 20) & (occ < 0.1)
    cx, cy = centre_px(lay, m)
    yy, xx = np.mgrid[0:t.shape[0], 0:t.shape[1]] + 0.5
    for tx, ty in twins:  # pixels nearer another instance of this charge belong to that one
        keep &= np.hypot(xx - cx, yy - cy) <= np.hypot(xx - tx, yy - ty)
    a = np.where(keep, a, 0)
    a = drop_specks(a > 0.04) * a
    if not (a > 0.04).any():
        return None
    return unwarp(a, w, lay, m, f"{cid}/{m}*")


def unwarp(a, w, lay: Layer, m, source) -> Sprite:
    """Sprite in the charge's own frame (no rotation, no mirroring) from coverage and shade in the tile."""
    cx, cy = centre_px(lay, m)
    ys, xs = np.nonzero(a > 0.04)
    r = int(math.ceil(np.hypot(xs + 0.5 - cx, ys + 0.5 - cy).max())) + 2
    if lay.rot == 0:  # keep the tile's pixel grid: no resampling blur
        x0, y0 = int(math.floor(cx)) - r, int(math.floor(cy)) - r
        H, W = a.shape
        pa, pw = np.zeros((2 * r + 1, 2 * r + 1)), np.zeros((2 * r + 1, 2 * r + 1))
        sx0, sy0, sx1, sy1 = max(x0, 0), max(y0, 0), min(x0 + 2 * r + 1, W), min(y0 + 2 * r + 1, H)
        pa[sy0 - y0:sy1 - y0, sx0 - x0:sx1 - x0] = a[sy0:sy1, sx0:sx1]
        pw[sy0 - y0:sy1 - y0, sx0 - x0:sx1 - x0] = w[sy0:sy1, sx0:sx1]
        ax, ay = cx - x0, cy - y0
        if lay.tile[0] < 0:
            pa, pw, ax = pa[:, ::-1], pw[:, ::-1], pa.shape[1] - ax
        if lay.tile[1] < 0:
            pa, pw, ay = pa[::-1], pw[::-1], pa.shape[0] - ay
    else:
        n = 2 * r + 1
        ax = ay = r + 0.5
        sy, sx = np.mgrid[0:n, 0:n] + 0.5
        rx, ry = sx - ax, sy - ay
        if lay.tile[0] < 0:
            rx = -rx
        if lay.tile[1] < 0:
            ry = -ry
        th = math.radians(lay.rot)  # inverse of draw_charge's rotation
        dx = rx * math.cos(th) + ry * math.sin(th)
        dy = -rx * math.sin(th) + ry * math.cos(th)
        pa, pw = bilinear(a, cx + dx - 0.5, cy + dy - 0.5), bilinear(w, cx + dx - 0.5, cy + dy - 0.5)
    ys, xs = np.nonzero(pa > 0.04)
    x0, x1, y0, y1 = xs.min(), xs.max() + 1, ys.min(), ys.max() + 1
    k = (lay.scale[0] * ratio(m), lay.scale[1] * ratio(m))
    return Sprite(np.ascontiguousarray(pa[y0:y1, x0:x1]), np.ascontiguousarray(pw[y0:y1, x0:x1]),
                  (ax - x0, ay - y0), k, source)


def drop_specks(mask, min_px=4):
    """Mask without 8-connected components smaller than min_px (render noise along edges)."""
    lab = np.zeros(mask.shape, int)
    keep = np.zeros(mask.shape, bool)
    n = 0
    for y, x in zip(*np.nonzero(mask)):
        if lab[y, x]:
            continue
        n += 1
        stack, comp = [(y, x)], []
        lab[y, x] = n
        while stack:
            cy, cx = stack.pop()
            comp.append((cy, cx))
            for dy in (-1, 0, 1):
                for dx in (-1, 0, 1):
                    yy, xx = cy + dy, cx + dx
                    if 0 <= yy < mask.shape[0] and 0 <= xx < mask.shape[1] and mask[yy, xx] and not lab[yy, xx]:
                        lab[yy, xx] = n
                        stack.append((yy, xx))
        if len(comp) >= min_px:
            for cy, cx in comp:
                keep[cy, cx] = True
    return keep


def bilinear(img, x, y):
    h, w = img.shape
    x0, y0 = np.floor(x).astype(int), np.floor(y).astype(int)
    fx, fy = x - x0, y - y0
    out = np.zeros_like(x, float)
    for dx, dy, wt in ((0, 0, (1 - fx) * (1 - fy)), (1, 0, fx * (1 - fy)), (0, 1, (1 - fx) * fy), (1, 1, fx * fy)):
        xi, yi = x0 + dx, y0 + dy
        ok = (xi >= 0) & (xi < w) & (yi >= 0) & (yi < h)
        out += np.where(ok, img[np.clip(yi, 0, h - 1), np.clip(xi, 0, w - 1)], 0) * wt
    return out


def canvas_only_alpha(canvas):
    return canvas.ndim == 2


def draw_charge(canvas, sp: Sprite, lay: Layer, m):
    H, W = canvas.shape[:2]
    cx, cy = centre_px(lay, m)
    kx, ky = lay.scale[0] * ratio(m), lay.scale[1] * ratio(m)
    yy, xx = np.mgrid[0:H, 0:W] + 0.5
    dx, dy = xx - cx, yy - cy
    th = math.radians(lay.rot)
    # the def's rotation turns counter-clockwise on screen (GL, y up); undo it in y-down pixels
    rx = dx * math.cos(th) - dy * math.sin(th)
    ry = dx * math.sin(th) + dy * math.cos(th)
    if lay.tile[0] < 0:
        rx = -rx
    if lay.tile[1] < 0:
        ry = -ry
    sx = rx * sp.k[0] / kx + sp.anchor[0] - 0.5
    sy = ry * sp.k[1] / ky + sp.anchor[1] - 0.5
    a, w = bilinear(sp.a, sx, sy), bilinear(sp.w, sx, sy)
    if canvas_only_alpha(canvas):
        return a
    tint = np.asarray(next(iter(lay.tints.values()), DEFAULT_TINT["r"]), float)
    canvas[:] = canvas * (1 - a[..., None]) + (a * w)[..., None] * tint


# ---------------------------------------------------------------- divisions and ordinaries

def mask_key(lay: Layer, m):
    return lay.tex, m, abs(lay.tile[0]), abs(lay.tile[1]), lay.offset


def share(px, c0, c1):
    """Share of c1 on the segment c0 -> c1 and the distance of px from that segment."""
    c0, c1 = np.asarray(c0, float), np.asarray(c1, float)
    d = c1 - c0
    t = np.clip(((px - c0) @ d) / max(d @ d, 1e-6), 0, 1)
    off = np.linalg.norm(px - (c0 + t[..., None] * d), axis=-1)
    return t, off


def unflip(arr, lay: Layer):
    if lay.tile[0] < 0:
        arr = arr[:, ::-1]
    if lay.tile[1] < 0:
        arr = arr[::-1]
    return arr


def vote_masks(crests, atl: Atlases):
    """Division masks (share of r over g) and ordinary masks (share of the tint) per texture, mode, tiling."""
    votes = collections.defaultdict(list)
    for c in crests.values():
        for m in MODES:
            d = c.division.get(m)
            if d is None or d.patterns:
                continue
            t = atl.tile(c.cid, m)
            g, r = d.tints.get("g", DEFAULT_TINT["g"]), d.tints.get("r", DEFAULT_TINT["r"])
            if d.tex != "Solid_Color" and np.linalg.norm(np.subtract(g, r)) > 40:
                s, off = share(t, g, r)
                votes[("div",) + mask_key(d, m)].append(np.where(off < 12, unflip(s, d), np.nan))
            ords = c.ordinaries.get(m, [])
            if len(ords) == 1 and d.tex == "Solid_Color" and not ords[0].patterns and ords[0].rot == 0:
                o = ords[0]
                tint = next(iter(o.tints.values()), DEFAULT_TINT["r"])
                if np.linalg.norm(np.subtract(g, tint)) > 40:
                    s, off = share(t, g, tint)
                    votes[("ord",) + mask_key(o, m)].append(np.where(off < 12, unflip(s, o), np.nan))
    masks = median_masks(votes)
    # patterns fill a division channel: cut where that channel shows
    votes = collections.defaultdict(list)
    for c in crests.values():
        for m in MODES:
            d = c.division.get(m)
            if d is None or not d.patterns:
                continue
            t = atl.tile(c.cid, m)
            if d.tex == "Solid_Color":
                M = np.zeros(t.shape[:2])
            elif ("div",) + mask_key(d, m) in masks:
                M = unflip(masks[("div",) + mask_key(d, m)][0], d)
            else:
                continue
            for ch, pat in d.patterns.items():
                region = (M < 0.05) if ch == "g" else (M > 0.95) if ch == "r" else None
                if region is None or pat is None or not region.any():
                    continue
                wts, off = channel_weights(t, pat)
                votes[("pat",) + mask_key(pat, m)].append(np.where(((off < 12) & region)[..., None], unflip(wts, pat), np.nan))
    masks.update(median_masks(votes))
    return masks


def tints3(lay: Layer):
    return np.array([lay.tints.get(ch, DEFAULT_TINT[ch]) for ch in "rgb"], float)  # rows r, g, b


def channel_weights(px, lay: Layer):
    """px = w_r * tint_r + w_g * tint_g + w_b * tint_b (1 - sum: black, the shading), weights >= 0 with sum <= 1:
    best of the fits on every subset of the channels (fewer channels win ties). Weights and fit error."""
    T = tints3(lay)
    best_w, best_e = np.zeros(px.shape[:-1] + (3,)), np.linalg.norm(px, axis=-1)
    for sub in ((0,), (1,), (2,), (0, 1), (0, 2), (1, 2), (0, 1, 2)):
        Ts = T[list(sub)]
        ws = px @ np.linalg.pinv(Ts)
        ok = (ws >= -1e-3).all(-1) & (ws.sum(-1) <= 1 + 1e-3)
        w = np.zeros_like(best_w)
        w[..., list(sub)] = np.clip(ws, 0, 1)
        e = np.linalg.norm(px - w @ T, axis=-1) + 0.5 * len(sub)
        better = ok & (e < best_e)
        best_w[better], best_e[better] = w[better], e[better]
    return best_w, np.linalg.norm(px - best_w @ T, axis=-1)


def median_masks(votes):
    masks = {}
    for key, vs in votes.items():
        st = np.stack(vs)
        if st.ndim == 4:  # channel weights: a median per channel would mix different splits; best vote, gaps from others
            order = np.argsort([np.isnan(v[..., 0]).sum() for v in vs])
            med = st[order[0]].copy()
            for i in order[1:]:
                gap = np.isnan(med[..., 0])
                med[gap] = st[i][gap]
        else:
            with np.errstate(all="ignore"):
                med = np.nanmedian(st, 0) if len(vs) > 1 else st[0]
        masks[key] = (inpaint(med), len(vs))
    return masks


def channel_fill(d: Layer, ch, m, masks, missing):
    """What a division channel shows: its tint, or its pattern in the pattern's two tints."""
    pat = d.patterns.get(ch)
    if pat is None:
        return np.asarray(d.tints.get(ch, DEFAULT_TINT[ch]), float)
    key = ("pat",) + mask_key(pat, m)
    if key not in masks:
        missing.append(f"pattern {pat.tex}")
        return tints3(pat)[1]
    return unflip(masks[key][0], pat) @ tints3(pat)


def inpaint(a):
    """Fill NaN pixels (covered by charges in every crest) with the mean of their known neighbours, outwards."""
    if a.ndim == 3:
        return np.stack([inpaint(a[..., i]) for i in range(a.shape[-1])], -1)
    a = a.copy()
    while np.isnan(a).any():
        known = ~np.isnan(a)
        if not known.any():
            return np.zeros_like(a)
        p = np.pad(np.where(known, a, 0), 1)
        n = np.pad(known.astype(float), 1)
        s = sum(np.roll(np.roll(p, dy, 0), dx, 1) for dy in (-1, 0, 1) for dx in (-1, 0, 1))[1:-1, 1:-1]
        c = sum(np.roll(np.roll(n, dy, 0), dx, 1) for dy in (-1, 0, 1) for dx in (-1, 0, 1))[1:-1, 1:-1]
        fill = ~known & (c > 0)
        a[fill] = s[fill] / c[fill]
    return a


def draw_mask(canvas, mask, lay: Layer, c0, c1):
    """c0 and c1: colours or images; c0 None: draw c1 over the canvas."""
    m = unflip(mask, lay)[..., None]  # flipping twice = identity, so this applies the layer's own flip
    canvas[:] = (canvas if c0 is None else np.asarray(c0, float)) * (1 - m) + m * np.asarray(c1, float)


# ---------------------------------------------------------------- painting a crest

def paint(c: Crest, m, sprites, masks, upto=None, skip=None):
    """Paint crest c in mode m from the parts (only the charges before index upto, if given);
    returns (image, list of what could not be painted)."""
    w, h = MODES[m]
    img = np.zeros((h, w, 3))
    missing = []
    d = c.division.get(m)
    if d is None:
        missing.append("no division")
    else:
        g, r = channel_fill(d, "g", m, masks, missing), channel_fill(d, "r", m, masks, missing)
        if d.tex == "Solid_Color":
            img[:] = g
        elif ("div",) + mask_key(d, m) in masks:
            draw_mask(img, masks[("div",) + mask_key(d, m)][0], d, g, r)
        else:
            img[:] = g
            missing.append(f"division {d.tex} {d.tile} {d.offset}")
    for o in c.ordinaries.get(m, []):
        key = ("ord",) + mask_key(o, m)
        if key in masks and not o.patterns and o.rot == 0:
            draw_mask(img, masks[key][0], o, None, next(iter(o.tints.values()), DEFAULT_TINT["r"]))
        else:
            missing.append(f"ordinary {o.tex}")
    for lay in c.charges.get(m, [])[:upto]:
        if lay.tex == skip:
            continue
        if lay.tex in sprites and lay.pivot == (0.5, 0.5) and not lay.patterns and lay.tints:
            draw_charge(img, sprites[lay.tex], lay, m)
        else:
            missing.append(f"charge {lay.tex}" + (" pivot" if lay.pivot != (0.5, 0.5) else "")
                           + (" pattern" if lay.patterns else "") + ("" if lay.tints else " untinted"))
    return np.clip(img, 0, 255), missing


# ---------------------------------------------------------------- main

def write_recipes(path: Path, recipes):
    """Where the plugin cuts each charge, in order (each one is cut against a crest painted from the ones before):
    one line per charge: texture name, crest id, mode, index in that crest's charges of that mode."""
    lines = ["# URAH_Kingdoms: charges cut from the game's coat of arms atlases, written by tools/coa_parts.py",
             "# texture<TAB>crest<TAB>mode<TAB>charge index"]
    lines += [f"{tex}\t{cid}\t{m}\t{i}" for tex, cid, m, i in recipes]
    path.write_text("\n".join(lines) + "\n", encoding="utf-8")


def save_parts(out: Path, sprites, masks):
    (out / "charges").mkdir(parents=True, exist_ok=True)
    (out / "masks").mkdir(parents=True, exist_ok=True)
    for name, sp in sprites.items():
        img = np.stack([sp.w * 255, sp.w * 255, sp.w * 255, sp.a * 255], -1).astype(np.uint8)
        Image.fromarray(img, "RGBA").save(out / "charges" / f"{name}.png")
    for key, (mask, n) in masks.items():
        kind, tex, m, tx, ty, off = key
        Image.fromarray((mask * 255).astype(np.uint8), "RGB" if mask.ndim == 3 else "L").save(out / "masks" / f"{kind}_{tex}_{m}_{tx}_{ty}_{off[0]}_{off[1]}.png")


def sheet(path: Path, rows, mode):
    w, h = MODES[mode]
    z = 3 if w < 100 else 2
    cell = w * z * 3 + 30
    cols = 4
    nrows = (len(rows) + cols - 1) // cols
    img = Image.new("RGB", (cols * cell, nrows * (h * z + 18)), "white")
    dr = ImageDraw.Draw(img)
    for i, (cid, orig, mine, err) in enumerate(rows):
        x, y = (i % cols) * cell, (i // cols) * (h * z + 18)
        diff = np.clip(np.abs(orig - mine) * 3, 0, 255)
        for j, a in enumerate((orig, mine, diff)):
            img.paste(Image.fromarray(a.astype(np.uint8)).resize((w * z, h * z), Image.NEAREST), (x + j * w * z, y))
        dr.text((x, y + h * z + 2), f"{cid}  err {err:.1f}", fill=(0, 0, 0))
    img.save(path)


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", type=Path, default=REPO / "tools" / "out" / "coa")
    ap.add_argument("--sheet", action="store_true")
    args = ap.parse_args()
    args.out.mkdir(parents=True, exist_ok=True)

    pal = load_palette()
    crests = load_crests(pal)
    atl = Atlases()

    sprites, recipes = {}, []
    masks = vote_masks(crests, atl)
    while True:  # each pass: the charges whose crest is otherwise paintable with what is cut so far
        new = 0
        for tex, cands in sorted(general_candidates(crests, sprites, masks).items()):
            for cand in sorted(cands, key=lambda c: -c[0]):
                sp = cut_general(atl, *cand[1:8])
                if sp is not None:
                    sprites[tex] = sp
                    recipes.append((tex, cand[1], cand[2], cand[8]))
                    new += 1
                    break
        print(f"  pass: {new} charges")
        if not new:
            break
    write_recipes(REPO / "plugin" / "coa_recipes.txt", recipes)
    save_parts(args.out, sprites, masks)

    used = {lay.tex for c in crests.values() for ls in c.charges.values() for lay in ls}
    errs = collections.defaultdict(list)
    reasons = collections.Counter()
    worst = collections.defaultdict(list)
    lines = []
    for c in crests.values():
        for m in MODES:
            mine, missing = paint(c, m, sprites, masks)
            orig = atl.tile(c.cid, m)
            err = float(np.abs(orig - mine).mean())
            for r in missing:
                reasons[r.split(" ")[0] + (" " + r.split(" ")[-1] if r.split(" ")[-1] in ("pivot", "pattern", "untinted") else "")] += 1
            (errs[m] if not missing else errs[m + " (parts missing)"]).append(err)
            if not missing:
                worst[m].append((c.cid, orig, mine, err))
            lines.append(f"{c.cid}\t{m}\t{err:.1f}\t{'; '.join(missing)}")
    (args.out / "report.txt").write_text("\n".join(lines) + "\n", encoding="utf-8")

    print(f"charges: {len(sprites)} of {len(used)} cut; division/ordinary masks: {len(masks)}")
    for k, v in sorted(errs.items()):
        v = np.array(v)
        print(f"  {k:28s} {len(v):4d} crests  mean err {v.mean():5.1f}  median {np.median(v):5.1f}  <4: {(v < 4).mean():.0%}")
    print("not painted:", ", ".join(f"{k} {n}" for k, n in reasons.most_common()))
    if args.sheet:
        for m, rows in worst.items():
            rows.sort(key=lambda r: -r[3])
            sheet(args.out / f"worst_{m}.png", rows[:48], m)
            sheet(args.out / f"first_{m}.png", sorted(rows, key=lambda r: r[0])[:48], m)
        print("sheets in", args.out)


if __name__ == "__main__":
    main()
