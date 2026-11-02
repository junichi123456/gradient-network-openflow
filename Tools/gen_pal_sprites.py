#!/usr/bin/python3
"""全パルのスプライト生成 v2: パーツ式モデル JSON -> 点群ボクセル -> 8方向レンダ。

Data/sprite_models/{species_id}.json（スキーマは docs/design/pvp_spec.md §34 "v2"）を読み、
numpy/PIL のソフトウェアレンダラ（斜め見下ろし正射影・陰影・足元アンカー）で描く。
モデルが無い種はエラーで止まる（旧ロジックへのフォールバックは無い）。決定的。

  /usr/bin/python3 Tools/gen_pal_sprites.py 001 009 160    # 指定のみ
  /usr/bin/python3 Tools/gen_pal_sprites.py                # 全種（全モデルが揃っていること）
  /usr/bin/python3 Tools/gen_pal_sprites.py --sheet 001 009 160   # 目視用コンタクトシート
                                                         # (SHEET_DIR に pal_sheet.png)

出力 (Assets/Sprites/{id}/): front,back,left,right,fl,fr,bl,br.png (192x192 待機),
walk.png / attack.png (4列x8行, 行順は DIRS)。足元はセル水平中央・下端から 12px 上。
"""
import json
import math
import os
import sys

import numpy as np
from PIL import Image, ImageDraw

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
MODELS = os.path.join(ROOT, "Data", "sprite_models")
CELL = 192
SS = 2                      # スーパーサンプリング倍率
FEET_MARGIN = 12            # 足元がセル下端から何px上か
UNIT_PX = 4.0               # モデル 1 単位 = 4px(最終)。はみ出す種だけ自動縮小
PITCH = math.radians(28)    # カメラの見下ろし角
BASE_STEP = 0.9             # 点群のサンプリング間隔(単位)

# 行順（SpriteTextureLibrary._directionNames と厳密に対応: (x,y)=Godot グリッド, y下=front）
DIRS = [("front", (0, 1)), ("back", (0, -1)), ("left", (-1, 0)), ("right", (1, 0)),
        ("fl", (-1, 1)), ("fr", (1, 1)), ("bl", (-1, -1)), ("br", (1, -1))]

LIGHT = np.array([-0.40, 0.80, 0.45])
LIGHT = LIGHT / np.linalg.norm(LIGHT)
VIEW = np.array([0.0, math.sin(PITCH), math.cos(PITCH)])   # 画面手前(カメラ方向)

SHAPES = ("ellipsoid", "box", "cylinder", "cone", "wedge")
GAITS = ("biped", "quad", "hover", "slither", "none")
MIRROR = {"_l": "_r", "_r": "_l", "fl": "fr", "fr": "fl", "bl": "br", "br": "bl"}


# ---------------------------------------------------------------- モデル読込
def hexcolor(s):
    s = s.lstrip("#")
    return np.array([int(s[i:i + 2], 16) for i in (0, 2, 4)], float)


def mirror_group(g):
    for suf, rep in MIRROR.items():
        if g.endswith(suf):
            return g[: -len(suf)] + rep
    return g


def rotmat(rx, ry, rz):
    cx, sx = math.cos(rx), math.sin(rx)
    cy, sy = math.cos(ry), math.sin(ry)
    cz, sz = math.cos(rz), math.sin(rz)
    mx = np.array([[1, 0, 0], [0, cx, -sx], [0, sx, cx]])
    my = np.array([[cy, 0, sy], [0, 1, 0], [-sy, 0, cy]])
    mz = np.array([[cz, -sz, 0], [sz, cz, 0], [0, 0, 1]])
    return mz @ my @ mx


def field(shape, q):
    """局所正規化座標 q(n,3)（-1..1 が部品の範囲）での内外関数。>=0 が内側。"""
    x, y, z = q[:, 0], q[:, 1], q[:, 2]
    if shape == "ellipsoid":
        return 1.0 - np.sqrt(x * x + y * y + z * z)
    if shape == "box":
        return 1.0 - (x ** 8 + y ** 8 + z ** 8) ** 0.125
    if shape == "cylinder":
        r = np.sqrt(x * x + z * z)
        return 1.0 - (r ** 8 + y ** 8) ** 0.125
    if shape == "cone":     # 頂点 +y、底面 -y
        r = np.sqrt(x * x + z * z)
        return np.minimum((1.0 - y) / 2.0 - r, 1.0 - np.abs(y))
    # wedge: 箱を斜面 qy <= -qz で切る（高い側が -z）
    return np.minimum(np.minimum(1 - np.abs(x), 1 - np.abs(z)),
                      np.minimum(1 - np.abs(y), (-z - y) / 2.0))


def sample_part(p, pal):
    """部品 -> 表面点群 (P, N, C, step)。単位座標・モデル空間。"""
    shape = p["shape"]
    size = np.array(p["size"], float)
    half = size / 2.0
    step = float(np.clip(size.min() / 3.0, 0.4, BASE_STEP))
    ax = [np.arange(-h + step / 2, h, step) if h > step else np.array([0.0]) for h in half]
    gx, gy, gz = np.meshgrid(*ax, indexing="ij")
    loc = np.stack([gx.ravel(), gy.ravel(), gz.ravel()], 1)
    q = loc / half
    g = field(shape, q)
    eps = 0.08
    grad = np.zeros_like(q)
    for i in range(3):
        d = np.zeros(3)
        d[i] = eps
        grad[:, i] = (field(shape, q + d) - field(shape, q - d)) / (2 * eps) / half[i]
    gm = np.linalg.norm(grad, axis=1) + 1e-9
    keep = (g >= 0) & (g / gm <= step * 0.95)
    loc, n = loc[keep], -grad[keep] / gm[keep, None]
    rx, ry, rz = [math.radians(v) for v in p.get("rot", [0, 0, 0])]
    R = rotmat(rx, ry, rz)
    pts = loc @ R.T + np.array(p["center"], float)
    nrm = n @ R.T
    c = p["color"]
    if c in pal:
        c = pal[c]
    elif not c.startswith("#"):
        raise ValueError("color %r not in palette" % c)
    col = np.tile(hexcolor(c), (len(pts), 1))
    return pts, nrm, col, step


class Model:
    pass


def load_model(sid):
    path = os.path.join(MODELS, sid + ".json")
    if not os.path.exists(path):
        raise SystemExit("モデルが無い: %s（旧ロジックへのフォールバックは無い）" % path)
    with open(path, encoding="utf-8") as f:
        d = json.load(f)
    if d.get("gait") not in GAITS:
        raise SystemExit("%s: gait は %s のどれか" % (sid, GAITS))
    pal = d.get("palette", {})
    parts = []
    for i, p in enumerate(d["parts"]):
        if p["shape"] not in SHAPES:
            raise SystemExit("%s: part %s: shape %r" % (sid, p.get("name"), p["shape"]))
        parts.append(p)
        if p.get("mirror_x"):
            q = dict(p)
            q["name"] = p["name"] + "_m"
            c = list(p["center"]); c[0] = -c[0]; q["center"] = c
            r = list(p.get("rot", [0, 0, 0])); q["rot"] = [r[0], -r[1], -r[2]]
            q["group"] = mirror_group(p["group"])
            q["mirror_x"] = False
            parts.append(q)
    groups = {}
    for i, p in enumerate(parts):
        P, N, C, st = sample_part(p, pal)
        if len(P) == 0:
            raise SystemExit("%s: part %s は点が無い（小さすぎ）" % (sid, p["name"]))
        gname = p.get("group", "body")
        g = groups.setdefault(gname, {"P": [], "N": [], "C": [], "S": [], "B": []})
        g["P"].append(P); g["N"].append(N); g["C"].append(C)
        g["S"].append(np.full(len(P), st)); g["B"].append(np.full(len(P), i * 1e-3))
    m = Model()
    m.sid = sid
    m.gait = d["gait"]
    m.groups = {}
    allp = []
    for gname, g in groups.items():
        e = {k: np.concatenate(v) for k, v in g.items()}
        m.groups[gname] = e
        allp.append(e["P"])
    allp = np.concatenate(allp)
    lo = allp.min(0)
    if abs(lo[1]) > 0.6:
        print("warn %s: 最下点 y=%.2f（足元は y=0）" % (sid, lo[1]), file=sys.stderr)
    m.pivots = {}
    ov = d.get("pivots", {})
    for gname, e in m.groups.items():
        P = e["P"]
        mn, mx = P.min(0), P.max(0)
        c = (mn + mx) / 2
        if gname in ov:
            pv = np.array(ov[gname], float)
        elif gname.startswith(("leg", "arm")):
            pv = np.array([c[0], mx[1], c[2]])
        elif gname.startswith("wing"):
            pv = np.array([mn[0] if c[0] > 0 else mx[0], c[1], c[2]])
        elif gname.startswith("tail"):
            pv = np.array([c[0], c[1], mx[2]])
        elif gname.startswith(("head", "ear")):
            pv = np.array([c[0], mn[1], c[2]])
        else:
            pv = c
        m.pivots[gname] = pv
    low = allp[allp[:, 1] < allp[:, 1].min() + 0.2 * (allp[:, 1].max() - allp[:, 1].min())]
    span = max(np.ptp(low[:, 0]), np.ptp(low[:, 2]))
    m.footprint = span / 2 * 0.9
    m.hover = d.get("hover", 0.0)
    return m


# ---------------------------------------------------------------- 姿勢
def pose_idle(m):
    return (0.0, 0.0, 0.0), {}, 0.0


def pose_walk(m, f):
    ph = f / 4.0 * 2 * math.pi
    s = math.sin(ph)
    bob = abs(s) * 0.9
    pose = {}
    amp = 0.7 if m.gait == "biped" else 0.55
    for g in m.groups:
        if g.startswith("leg") and m.gait in ("biped", "quad"):
            phase = 0.0 if g[-2:] in ("fl", "br") or g.endswith("_l") else math.pi
            sw = math.sin(ph + phase)
            pose[g] = (-sw * amp, 0, 0, 0, max(0.0, math.cos(ph + phase)) * 0.9, 0)
        elif g.startswith("arm"):
            phase = 0.0 if g.endswith(("_l", "fl")) else math.pi
            pose[g] = (math.sin(ph + phase) * 0.6, 0, 0, 0, 0, 0)
        elif g.startswith("tail"):
            pose[g] = (0, s * 0.35, 0, 0, 0, 0)
        elif g.startswith("wing"):
            sgn = 1 if g.endswith(("_l", "fl")) else -1
            k = 0.45 if m.gait != "hover" else 0.7
            pose[g] = (0, 0, math.sin(ph * 2) * k * sgn, 0, 0, 0)
        elif g.startswith(("head", "ear")):
            pose[g] = (0.05 * math.sin(ph * 2), 0, 0, 0, 0, 0)
    if m.gait == "hover":
        bob = 0.5 + 1.2 * (0.5 + 0.5 * s)
    elif m.gait == "slither":
        bob = 0.0
    shear = 1.6 * math.sin(ph) if m.gait == "slither" else 0.0
    return (0.0, bob, 0.0), pose, shear


ATTACK_BODY = [(-0.14, 0.0, -1.5), (0.22, 0.3, 3.0), (0.32, 0.0, 4.5), (0.08, 0.0, 1.0)]
ATTACK_LIMB = [-1.0, 1.2, 1.0, 0.3]
ATTACK_HEAD = [-0.3, 0.35, 0.5, 0.0]


def pose_attack(m, f):
    lean, dy, dz = ATTACK_BODY[f]
    if m.gait in ("hover", "slither"):
        lean *= 0.6
    pose = {}
    for g in m.groups:
        right = not g.endswith(("_l", "fl", "bl"))
        if g.startswith("arm"):
            pose[g] = (-ATTACK_LIMB[f] * 1.1, 0, 0, 0, 0, 0)
        elif g.startswith("leg") and m.gait in ("biped", "quad"):
            pose[g] = ([0.3, -0.5, -0.2, 0.0][f] * (-1 if right else 1), 0, 0, 0, 0, 0)
        elif g.startswith(("head", "ear")):
            pose[g] = (ATTACK_HEAD[f], 0, 0, 0, 0, 0)
        elif g.startswith("tail"):
            pose[g] = (0, 0, 0, 0, [0.5, 1.4, 2.0, 0.5][f], 0)
        elif g.startswith("wing"):
            sgn = -1 if right else 1
            pose[g] = (0, 0, [0.7, -0.5, -0.2, 0.2][f] * sgn, 0, 0, 0)
    return (lean, dy, dz), pose, 0.0


# ---------------------------------------------------------------- 描画
def project(m, body, pose, shear, dirvec):
    """全点を変換して (sx, sy, dep, rgb, step) を返す（単位は最終px/UNIT_PX。y上）。"""
    lean, bdy, bdz = body
    th = math.atan2(dirvec[0], dirvec[1])
    cyaw, syaw = math.cos(th), math.sin(th)
    Rl = rotmat(lean, 0, 0)
    Ry = np.array([[cyaw, 0, syaw], [0, 1, 0], [-syaw, 0, cyaw]])
    out = []
    for gname, e in m.groups.items():
        P, N = e["P"], e["N"]
        if gname in pose:
            rx, ry, rz, tx, ty, tz = pose[gname]
            Rp = rotmat(rx, ry, rz)
            pv = m.pivots[gname]
            P = (P - pv) @ Rp.T + pv + np.array([tx, ty, tz])
            N = N @ Rp.T
        if shear:
            P = P + np.stack([np.sin(P[:, 2] * 0.35) * shear, 0 * P[:, 0], 0 * P[:, 0]], 1)
        P = P + np.array([0, m.hover, 0])
        P = P @ Rl.T + np.array([0, bdy, bdz])
        N = N @ Rl.T
        P = P @ Ry.T
        N = N @ Ry.T
        vis = N @ VIEW > -0.25
        if not vis.any():
            continue
        P, N = P[vis], N[vis]
        t = np.clip(N @ LIGHT, 0, 1)
        k = 0.80 + 0.45 * t
        rgb = np.clip(e["C"][vis] * k[:, None], 0, 255)
        sx = P[:, 0]
        sy = P[:, 1] * math.cos(PITCH) - P[:, 2] * math.sin(PITCH)
        dep = P[:, 1] * math.sin(PITCH) + P[:, 2] * math.cos(PITCH) + e["B"][vis]
        out.append((sx, sy, dep, rgb, e["S"][vis]))
    return [np.concatenate(c) for c in zip(*out)]


def fits(views, s):
    for sx, sy, dep, rgb, st in views:
        half = st.max() * 0.65 * UNIT_PX * s
        if np.abs(sx).max() * UNIT_PX * s + half > CELL / 2 - 3:
            return False
        lift = max(0.0, -sy.min() * UNIT_PX * s - 7.0)
        if sy.max() * UNIT_PX * s + lift + half > CELL - FEET_MARGIN - 3:
            return False
    return True


def raster(m, view, s):
    sx, sy, dep, rgb, st = view
    W = CELL * SS
    k = UNIT_PX * s * SS
    lift = max(0.0, -sy.min() * UNIT_PX * s - 7.0) * SS
    ox, oy = W / 2, (CELL - FEET_MARGIN) * SS
    px = ox + sx * k
    py = oy - (sy * k + lift)
    idx, dps, cols = [], [], []
    for stv in np.unique(st):
        sel = st == stv
        bs = max(2, int(round(stv * 1.3 * k)))
        off = np.arange(bs) - bs // 2
        ix = np.round(px[sel])[:, None, None].astype(int) + off[None, :, None]
        iy = np.round(py[sel])[:, None, None].astype(int) + off[None, None, :]
        ok = (ix >= 0) & (ix < W) & (iy >= 0) & (iy < W)
        flat = np.where(ok, iy * W + ix, -1).reshape(-1)
        idx.append(flat)
        dps.append(np.repeat(dep[sel], bs * bs))
        cols.append(np.repeat(rgb[sel], bs * bs, axis=0))
    idx, dps, cols = np.concatenate(idx), np.concatenate(dps), np.concatenate(cols)
    m_ = idx >= 0
    idx, dps, cols = idx[m_], dps[m_], cols[m_]
    order = np.argsort(dps, kind="stable")
    buf = np.zeros((W * W, 4), np.uint8)
    buf[idx[order], :3] = np.round(cols[order]).astype(np.uint8)
    buf[idx[order], 3] = 255
    img = Image.fromarray(buf.reshape(W, W, 4), "RGBA")
    # 影（足元・半透明）
    sh = Image.new("RGBA", (W, W), (0, 0, 0, 0))
    rx = min(m.footprint * UNIT_PX * s, 40) * SS
    ry = min(rx * 0.42, (FEET_MARGIN - 3) * SS)
    ImageDraw.Draw(sh).ellipse([ox - rx, oy - ry, ox + rx, oy + ry], fill=(0, 0, 0, 70))
    sh.alpha_composite(img)
    return sh.resize((CELL, CELL), Image.BOX)


def make_species(m):
    views = {}
    for dname, dv in DIRS:
        views[dname] = [project(m, *pose_idle(m)[:2], pose_idle(m)[2], dv)]
        views[dname + "_w"] = [project(m, *pose_walk(m, f), dv) for f in range(4)]
        views[dname + "_a"] = [project(m, *pose_attack(m, f), dv) for f in range(4)]
    flat = [v for vs in views.values() for v in vs]
    s = 1.0
    while not fits(flat, s) and s > 0.2:
        s *= 0.94
    return {k: [raster(m, v, s) for v in vs] for k, vs in views.items()}


def write_species(sid, frames, root):
    d = os.path.join(root, "Assets", "Sprites", sid)
    os.makedirs(d, exist_ok=True)
    for dname, _ in DIRS:
        frames[dname][0].save(os.path.join(d, dname + ".png"), optimize=False)
    for suf, name in (("_w", "walk"), ("_a", "attack")):
        sheet = Image.new("RGBA", (CELL * 4, CELL * 8), (0, 0, 0, 0))
        for r, (dname, _) in enumerate(DIRS):
            for c, im in enumerate(frames[dname + suf]):
                sheet.paste(im, (c * CELL, r * CELL))
        sheet.save(os.path.join(d, name + ".png"), optimize=False)


def all_ids():
    with open(os.path.join(ROOT, "Data", "species.json"), encoding="utf-8") as f:
        return [s["species_id"] for s in json.load(f)]


def contact_sheet(ids, out):
    H = 144
    rows = []
    for sid in ids:
        fr = make_species(load_model(sid))
        cols = [fr[d][0] for d in ("front", "back", "left", "right", "fl", "fr", "bl", "br")]
        cols += [fr["left_w"][0], fr["left_w"][2]] + [fr["fr_a"][i] for i in (0, 1, 2)]
        row = Image.new("RGBA", (H * len(cols), H), (235, 235, 235, 255))
        for i, im in enumerate(cols):
            row.alpha_composite(im.resize((H, H), Image.LANCZOS), (i * H, 0))
        rows.append(row)
    sheet = Image.new("RGB", (rows[0].width, H * len(rows)), (255, 255, 255))
    for i, r in enumerate(rows):
        sheet.paste(r.convert("RGB"), (0, i * H))
    sheet.save(out)


def _job(sid):
    write_species(sid, make_species(load_model(sid)), ROOT)


def main(argv):
    if argv and argv[0] == "--sheet":
        out = os.path.join(os.environ.get("SHEET_DIR", "/tmp"), "pal_sheet.png")
        contact_sheet(argv[1:], out)
        print(out)
        return
    ids = argv or all_ids()
    missing = [i for i in ids if not os.path.exists(os.path.join(MODELS, i + ".json"))]
    if missing:
        raise SystemExit("モデルが無い %d 種: %s" % (len(missing), " ".join(missing[:20])))
    from multiprocessing import Pool
    with Pool() as pool:
        for _ in pool.imap_unordered(_job, ids):
            pass
    print("generated", len(ids))


if __name__ == "__main__":
    main(sys.argv[1:])
