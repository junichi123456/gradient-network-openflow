#!/usr/bin/python3
"""全パルのスプライト自動生成（8方向・待機・歩行4コマ・攻撃4コマ）。

species.json の各種族から、species_id をシードにボクセル風の箱モデルを決定的に
組み、numpy/PIL のソフトウェアレンダラ（斜め見下ろしの正射影・面ごと3段の明暗）で描く。
仕様: docs/design/drafts/2026-10-29-pal-sprites.md / docs/design/pvp_spec.md §34

  /usr/bin/python3 Tools/gen_pal_sprites.py            # 全種
  /usr/bin/python3 Tools/gen_pal_sprites.py 001 005B   # 指定のみ
  /usr/bin/python3 Tools/gen_pal_sprites.py --sheet 001 005B 010 020   # 目視用コンタクトシート

出力 (Assets/Sprites/{id}/): front,back,left,right,fl,fr,bl,br.png (192x192 待機),
walk.png / attack.png (4列x8行, 行順は DIRS)。足元はセル水平中央・下端から 12px 上。
"""
import json
import math
import os
import random
import sys
import zlib

import numpy as np
from PIL import Image, ImageDraw

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CELL = 192
SS = 2                      # スーパーサンプリング倍率
FEET_MARGIN = 12            # 足元がセル下端から何px上か
PPU = 14.0 * SS             # 1ユニット = 14px(最終)。はみ出す種だけ make_species が自動縮小
PITCH = math.radians(28)    # カメラの見下ろし角
SCALE_TIERS = [0.80, 0.95, 1.10, 1.25]   # BST6 の四分位ごとの体の大きさ

# 行順（SpriteTextureLibrary._directionNames と厳密に対応: (x,y)=Godot グリッド, y下=front）
DIRS = [("front", (0, 1)), ("back", (0, -1)), ("left", (-1, 0)), ("right", (1, 0)),
        ("fl", (-1, 1)), ("fr", (1, 1)), ("bl", (-1, -1)), ("br", (1, -1))]

TYPE_COLORS = {
    "Neutral": (205, 182, 146), "Fire": (232, 96, 52), "Water": (70, 142, 228),
    "Grass": (96, 182, 82), "Electric": (240, 208, 62), "Ground": (172, 128, 78),
    "Ice": (150, 216, 236), "Dragon": (112, 84, 192), "Dark": (86, 74, 108),
    "Void": (156, 64, 178),
}
LIGHT = np.array([-0.40, 0.80, 0.45])
LIGHT = LIGHT / np.linalg.norm(LIGHT)
VIEW = np.array([0.0, math.sin(PITCH), math.cos(PITCH)])   # 画面手前(カメラ方向)


def clamp(c):
    return tuple(max(0, min(255, int(round(v)))) for v in c)


def mul(c, k):
    return clamp([v * k for v in c])


def mix(a, b, t):
    return clamp([a[i] * (1 - t) + b[i] * t for i in range(3)])


# ---------------------------------------------------------------- モデル
class Model:
    def __init__(self):
        self.parts = {}      # name -> dict(kind, pivot, boxes, phase)
        self.hover = 0.0     # 浮遊の高さ
        self.footprint = 3.0
        self.kind = "biped"

    def part(self, name, kind, pivot=(0, 0, 0), phase=0.0):
        self.parts[name] = {"kind": kind, "pivot": np.array(pivot, float), "boxes": [], "phase": phase}
        return name

    def box(self, part, c, s, color):
        self.parts[part]["boxes"].append((np.array(c, float), np.array(s, float), color))


def build_model(sp, rng, tier_scale):
    types = sp["types"]
    c1 = TYPE_COLORS.get(types[0], (180, 180, 180))
    c2 = TYPE_COLORS.get(types[1], None) if len(types) > 1 else None
    k = rng.uniform(0.9, 1.1)
    body = mul(c1, k)
    accent = c2 if c2 else mix(c1, (255, 255, 255) if sum(c1) < 500 else (60, 40, 40), 0.55)
    accent = mul(accent, rng.uniform(0.9, 1.1))
    belly = mix(body, (255, 244, 220), 0.55)
    dark = mul(body, 0.55)
    atk = max(sp["base_atk"], sp["base_spatk"])
    dfn = max(sp["base_def"], sp["base_spdef"])
    spd = sp["base_speed"]

    # 体型の選択（タイプ・速さ・耐久で重みづけ）
    w = {"round": 1.0, "quad": 1.0, "biped": 1.2, "dragon": 0.4, "float": 0.5, "serpent": 0.4}
    if "Dragon" in types:
        w["dragon"] += 3.0
    if "Void" in types:
        w["float"] += 2.0
    if "Ghost" in types or "Ice" in types:
        w["float"] += 0.5
    if spd >= 100:
        w["quad"] += 1.0
        w["biped"] += 0.5
    if dfn >= 90 and spd < 70:
        w["round"] += 1.5
        w["quad"] += 0.5
    if "Water" in types or "Grass" in types:
        w["serpent"] += 0.7
    kinds = sorted(w)
    kind = rng.choices(kinds, weights=[w[x] for x in kinds])[0]

    m = Model()
    m.kind = kind
    leg_len = 1.8 + min(max(spd, 20), 140) / 140.0 * 3.4
    leg_w = 1.9 - min(max(spd, 20), 140) / 140.0 * 0.8
    eye_style = "slit" if atk >= 95 and rng.random() < 0.7 else rng.choice(["dot", "dot", "big"])
    ears = rng.choice(["none", "point", "long", "round"])
    horn = "none"
    if atk >= 80 and rng.random() < 0.6 + (0.2 if sp["offense"] == "Physical" else 0):
        horn = rng.choice(["single", "twin", "twin"])
    claws = sp["base_atk"] >= 85 and rng.random() < 0.7
    shell = dfn >= 90 and rng.random() < 0.7
    spikes = (not shell) and atk >= 90 and rng.random() < 0.4
    tail = rng.choice(["none", "stick", "fat", "fin", "tuft"])
    belly_patch = rng.random() < 0.6

    def head(part, cx, cy, cz, hw, hh, hd, snout=0.0):
        m.box(part, (cx, cy, cz), (hw, hh, hd), body)
        fz = cz + hd / 2
        if snout > 0:
            m.box(part, (cx, cy - hh * 0.2, fz + snout / 2), (hw * 0.55, hh * 0.45, snout), belly)
            m.box(part, (cx, cy - hh * 0.05, fz + snout), (0.5, 0.4, 0.15), dark)
            fz += snout * 0.0
        ez = fz + 0.02
        ex = hw * 0.26
        ey = cy + hh * 0.12
        for sx in (-1, 1):
            if eye_style == "big":
                m.box(part, (cx + sx * ex, ey, ez), (0.95, 1.15, 0.18), (250, 250, 250))
                m.box(part, (cx + sx * ex, ey - 0.05, ez + 0.1), (0.5, 0.75, 0.16), (20, 20, 30))
            elif eye_style == "slit":
                m.box(part, (cx + sx * ex, ey, ez), (0.95, 0.45, 0.18), (250, 240, 120))
                m.box(part, (cx + sx * ex, ey, ez + 0.1), (0.35, 0.45, 0.16), (160, 20, 20))
            else:
                m.box(part, (cx + sx * ex, ey, ez), (0.6, 0.7, 0.18), (20, 20, 30))
                m.box(part, (cx + sx * ex - sx * 0.1, ey + 0.15, ez + 0.1), (0.2, 0.2, 0.14), (255, 255, 255))
            # 耳
            if ears == "point":
                m.box(part, (cx + sx * hw * 0.32, cy + hh / 2 + 0.7, cz - hd * 0.1), (0.9, 1.6, 0.7), accent)
            elif ears == "long":
                m.box(part, (cx + sx * hw * 0.3, cy + hh / 2 + 1.5, cz - hd * 0.15), (0.8, 3.0, 0.7), body)
                m.box(part, (cx + sx * hw * 0.3, cy + hh / 2 + 1.5, cz - hd * 0.15 + 0.1), (0.4, 2.4, 0.7), accent)
            elif ears == "round":
                m.box(part, (cx + sx * hw * 0.42, cy + hh / 2 + 0.3, cz - hd * 0.1), (1.2, 1.0, 0.8), accent)
        if horn == "single":
            m.box(part, (cx, cy + hh / 2 + 0.7, cz + hd * 0.25), (0.8, 1.5, 0.8), accent)
            m.box(part, (cx, cy + hh / 2 + 1.7, cz + hd * 0.25), (0.45, 0.8, 0.45), mul(accent, 1.15))
        elif horn == "twin":
            for sx in (-1, 1):
                m.box(part, (cx + sx * hw * 0.28, cy + hh / 2 + 0.6, cz + hd * 0.1), (0.7, 1.3, 0.7), accent)
                m.box(part, (cx + sx * hw * 0.36, cy + hh / 2 + 1.4, cz + hd * 0.1), (0.45, 0.8, 0.45), mul(accent, 1.15))

    def tail_parts(base_y, base_z, thick):
        if tail == "none":
            return
        pn = m.part("tail", "tail", pivot=(0, base_y, base_z))
        if tail == "stick":
            m.box(pn, (0, base_y + 0.4, base_z - 1.5), (0.8, 0.8, 3.0), body)
            m.box(pn, (0, base_y + 0.9, base_z - 3.3), (1.3, 1.3, 1.0), accent)
        elif tail == "fat":
            m.box(pn, (0, base_y + 0.3, base_z - 1.3), (thick, thick * 0.8, 2.6), body)
            m.box(pn, (0, base_y + 0.5, base_z - 3.0), (thick * 0.6, thick * 0.5, 1.2), accent)
        elif tail == "fin":
            m.box(pn, (0, base_y + 0.8, base_z - 1.4), (0.25, 2.8, 2.6), accent)
        else:  # tuft
            m.box(pn, (0, base_y + 0.6, base_z - 1.0), (1.0, 1.0, 2.0), body)
            m.box(pn, (0, base_y + 1.4, base_z - 2.4), (1.8, 1.8, 1.8), accent)

    def arms(y, x, ln, thick=1.2):
        for i, sx in enumerate((-1, 1)):
            pn = m.part("arm%d" % i, "arm", pivot=(sx * x, y, 0.2), phase=0.0 if sx < 0 else math.pi)
            m.box(pn, (sx * x, y - ln / 2, 0.2), (thick, ln, thick), body)
            m.box(pn, (sx * x, y - ln - 0.15, 0.25), (thick * 1.05, 0.5, thick * 1.1), accent if claws else belly)
            if claws:
                m.box(pn, (sx * x, y - ln - 0.45, 0.65), (thick * 0.6, 0.4, 0.6), (240, 240, 235))

    def back_gear(y, z0, z1, width, top):
        # 甲羅・背びれ
        if shell:
            pn = m.part("shell", "body")
            m.box(pn, (0, top + 0.3, (z0 + z1) / 2), (width * 0.95, 0.9, (z1 - z0) * 0.9), mul(accent, 0.85))
            m.box(pn, (0, top + 1.0, (z0 + z1) / 2), (width * 0.6, 0.8, (z1 - z0) * 0.6), accent)
        elif spikes:
            pn = m.part("spikes", "body")
            n = 3
            for i in range(n):
                z = z1 - (i + 0.5) * (z1 - z0) / n
                m.box(pn, (0, top + 0.5, z), (0.7, 1.2 - 0.15 * i, 0.7), accent)

    if kind == "round":
        r = rng.uniform(5.4, 6.4)
        by = leg_len * 0.45 + 0.6
        b = m.part("torso", "body")
        m.box(b, (0, by + 1.0, 0), (r * 0.8, 2.0, r * 0.8), body)
        m.box(b, (0, by + 3.2, 0), (r, 3.0, r), body)
        m.box(b, (0, by + 5.4, 0), (r * 0.8, 1.8, r * 0.8), mul(body, 1.05))
        if belly_patch:
            m.box(b, (0, by + 2.6, r / 2 + 0.05), (r * 0.5, 2.4, 0.2), belly)
        m.parts["torso"]["boxes"] += []
        h = m.part("head", "head", pivot=(0, by + 3.0, 0))
        head(h, 0, by + 3.9, 0, r * 0.95, 3.2, r * 0.95)  # 顔は胴の上段に重ねる
        for i, sx in enumerate((-1, 1)):
            pn = m.part("leg%d" % i, "leg", pivot=(sx * r * 0.28, by + 0.3, 0.3), phase=0.0 if sx < 0 else math.pi)
            m.box(pn, (sx * r * 0.28, by * 0.5 + 0.15, 0.3), (2.0, by + 0.3, 2.4), dark)
        arms(by + 3.6, r / 2 + 0.5, 2.2)
        tail_parts(by + 1.0, -r / 2, 1.8)
        back_gear(by, -r / 2, r / 2, r, by + 6.3)
        m.footprint = r * 0.62
    elif kind in ("biped", "dragon"):
        torso_h = rng.uniform(4.2, 5.6) if kind == "biped" else rng.uniform(4.8, 6.2)
        tw = rng.uniform(3.8, 5.0)
        td = tw * (0.85 if kind == "biped" else 1.15)
        ly = leg_len
        b = m.part("torso", "body")
        m.box(b, (0, ly + torso_h / 2, 0), (tw, torso_h, td), body)
        if belly_patch:
            m.box(b, (0, ly + torso_h * 0.45, td / 2 + 0.05), (tw * 0.6, torso_h * 0.6, 0.2), belly)
        hy = ly + torso_h + 1.5
        h = m.part("head", "head", pivot=(0, ly + torso_h, 0.3))
        hs = tw * rng.uniform(0.85, 1.05)
        head(h, 0, hy, 0.4, hs, 3.3, hs * 0.95, snout=(1.4 if kind == "dragon" else rng.choice([0, 0, 1.0])))
        for i, sx in enumerate((-1, 1)):
            pn = m.part("leg%d" % i, "leg", pivot=(sx * tw * 0.27, ly, 0), phase=0.0 if sx < 0 else math.pi)
            m.box(pn, (sx * tw * 0.27, ly / 2, 0), (leg_w, ly, leg_w + 0.4), dark if kind == "biped" else body)
            m.box(pn, (sx * tw * 0.27, 0.3, 0.5), (leg_w + 0.2, 0.6, leg_w + 1.2), accent)
        arms(ly + torso_h - 0.7, tw / 2 + 0.6, 3.0 if kind == "biped" else 2.4)
        if kind == "dragon":
            for i, sx in enumerate((-1, 1)):
                pn = m.part("wing%d" % i, "wing", pivot=(sx * tw * 0.4, ly + torso_h * 0.8, -td * 0.3), phase=0.0 if sx < 0 else math.pi)
                m.box(pn, (sx * (tw * 0.4 + 2.2), ly + torso_h * 0.9, -td * 0.4), (4.4, 0.35, 3.4), accent)
                m.box(pn, (sx * (tw * 0.4 + 0.8), ly + torso_h * 0.85, -td * 0.35), (1.6, 0.6, 1.4), mul(accent, 0.8))
            pn = m.part("tail", "tail", pivot=(0, ly + 0.8, -td / 2))
            for j in range(4):
                s = 2.0 - j * 0.35
                m.box(pn, (0, ly + 0.9 - j * 0.05, -td / 2 - 1.2 - j * 1.8), (s, s * 0.9, 2.0), body if j < 3 else accent)
        else:
            tail_parts(ly + 1.0, -td / 2, 1.5)
        back_gear(ly, -td / 2, td / 2, tw, ly + torso_h)
        m.footprint = tw * 0.75
    elif kind == "quad":
        bw = rng.uniform(3.8, 4.8)
        bl = rng.uniform(6.5, 8.0)
        ly = leg_len * 0.85
        bh = rng.uniform(3.6, 4.6)
        b = m.part("torso", "body")
        m.box(b, (0, ly + bh / 2, 0), (bw, bh, bl), body)
        if belly_patch:
            m.box(b, (0, ly + 0.5, 0), (bw * 0.8, 0.2, bl * 0.7), belly)
        h = m.part("head", "head", pivot=(0, ly + bh * 0.6, bl / 2))
        hs = bw * rng.uniform(0.85, 1.0)
        head(h, 0, ly + bh + 0.6, bl / 2 + 0.2, hs, 3.2, hs, snout=rng.choice([1.0, 1.6, 2.0]))
        for i, (sx, sz) in enumerate(((-1, 1), (1, 1), (-1, -1), (1, -1))):
            ph = 0.0 if (sx * sz) < 0 else math.pi
            pn = m.part("leg%d" % i, "leg", pivot=(sx * bw * 0.36, ly, sz * bl * 0.34), phase=ph)
            m.box(pn, (sx * bw * 0.36, ly / 2, sz * bl * 0.34), (leg_w, ly, leg_w), dark)
            m.box(pn, (sx * bw * 0.36, 0.3, sz * bl * 0.34 + 0.3), (leg_w + 0.1, 0.6, leg_w + 0.8), accent)
        tail_parts(ly + bh * 0.8, -bl / 2, 1.6)
        back_gear(ly, -bl / 2, bl / 2, bw, ly + bh)
        m.footprint = bl * 0.5
    elif kind == "float":
        m.hover = rng.uniform(2.0, 3.0)
        r = rng.uniform(4.6, 5.8)
        hy = m.hover
        b = m.part("torso", "body")
        m.box(b, (0, hy + 2.6, 0), (r, 5.2, r), body)
        m.box(b, (0, hy + 0.6, 0), (r * 0.7, 1.6, r * 0.7), mul(body, 0.8))
        m.box(b, (0, hy - 0.4, 0), (r * 0.4, 1.0, r * 0.4), accent)
        if belly_patch:
            m.box(b, (0, hy + 2.0, r / 2 + 0.05), (r * 0.55, 2.4, 0.2), belly)
        h = m.part("head", "head", pivot=(0, hy + 3, 0))
        head(h, 0, hy + 3.4, 0.1, r * 0.98, 3.8, r * 0.95)
        for i, sx in enumerate((-1, 1)):
            pn = m.part("wing%d" % i, "wing", pivot=(sx * r / 2, hy + 2.8, -0.3), phase=0.0 if sx < 0 else math.pi)
            m.box(pn, (sx * (r / 2 + 1.5), hy + 2.8, -0.3), (3.0, 0.5, 2.6), accent)
        for j in range(rng.choice([2, 3, 4])):
            ang = j * 2.0 * math.pi / 3.0 + rng.random()
            pn = m.part("orb%d" % j, "orb", pivot=(0, hy + 2.6, 0), phase=ang)
            m.box(pn, (math.cos(ang) * (r * 0.9), hy + 5.4 + (j % 2), math.sin(ang) * (r * 0.9)), (1.0, 1.0, 1.0), mul(accent, 1.2))
        tail_parts(hy + 1.0, -r / 2, 1.4)
        m.footprint = r * 0.7
    else:  # serpent
        n = rng.choice([5, 6, 7])
        base = rng.uniform(2.6, 3.2)
        hy = 0.0
        for j in range(n):
            pn = m.part("seg%d" % j, "seg", pivot=(0, 0, 0), phase=j)
            sz = base * (1.0 - j * 0.09)
            m.box(pn, (0, sz / 2 + 0.2, -j * (base * 0.85)), (sz, sz, base * 0.95), body if j % 2 == 0 else mix(body, accent, 0.35))
        h = m.part("head", "head", pivot=(0, 2.0, 0.6))
        hs = base * 1.35
        head(h, 0, hs / 2 + 0.7, 0.9, hs, hs * 0.95, hs, snout=rng.choice([0.0, 1.0]))
        if tail in ("fin", "tuft"):
            pn = m.part("tail", "seg", pivot=(0, 0, 0), phase=n)
            m.box(pn, (0, 1.4, -n * base * 0.85), (0.3 if tail == "fin" else 1.4, 2.4, 2.2), accent)
        m.footprint = n * base * 0.4
    m.scale = tier_scale
    return m


# ---------------------------------------------------------------- 姿勢
def rotmat(rx, ry, rz):
    cx, sx = math.cos(rx), math.sin(rx)
    cy, sy = math.cos(ry), math.sin(ry)
    cz, sz = math.cos(rz), math.sin(rz)
    mx = np.array([[1, 0, 0], [0, cx, -sx], [0, sx, cx]])
    my = np.array([[cy, 0, sy], [0, 1, 0], [-sy, 0, cy]])
    mz = np.array([[cz, -sz, 0], [sz, cz, 0], [0, 0, 1]])
    return mz @ my @ mx


def pose_walk(m, f):
    """歩行 f=0..3。戻り値 (body(lean,dy,dz), {part: (rx,ry,rz,dx,dy,dz)})"""
    ph = f / 4.0 * 2 * math.pi
    s = math.sin(ph)
    bob = abs(math.sin(ph)) * 0.45
    pose = {}
    for name, p in m.parts.items():
        k, q = p["kind"], p["phase"]
        sw = math.sin(ph + q)
        if k == "leg":
            pose[name] = (-sw * 0.7, 0, 0, 0, max(0.0, math.cos(ph + q)) * 0.25, 0)
        elif k == "arm":
            pose[name] = (sw * 0.6, 0, 0, 0, 0, 0)
        elif k == "tail":
            pose[name] = (0, s * 0.35, 0, 0, 0, 0)
        elif k == "wing":
            pose[name] = (0, 0, math.sin(ph * 2 + q) * 0.45 * (1 if q == 0 else -1), 0, 0, 0)
        elif k == "orb":
            pose[name] = (0, ph + q * 0.0, 0, 0, 0, 0)
        elif k == "seg":
            pose[name] = (0, 0, 0, math.sin(ph - q * 0.9) * 0.9, 0, 0)
        elif k == "head":
            pose[name] = (0.05 * math.sin(ph * 2), 0, 0, 0, 0, 0)
    if m.kind == "float":
        bob = 0.5 + 0.5 * math.sin(ph) * 1.0
    elif m.kind == "serpent":
        bob = 0.0
    return (0.0, bob, 0.0), pose


ATTACK_BODY = [(-0.14, 0.0, -0.4), (0.22, 0.1, 0.8), (0.32, 0.0, 1.2), (0.08, 0.0, 0.3)]
ATTACK_LIMB = [-1.0, 1.2, 1.0, 0.3]    # 腕: 正=前方へ振る（回転は -rx）
ATTACK_HEAD = [-0.3, 0.35, 0.5, 0.0]


def pose_attack(m, f):
    lean, dy, dz = ATTACK_BODY[f]
    if m.kind in ("float", "serpent"):
        lean *= 0.6
    pose = {}
    for name, p in m.parts.items():
        k, q = p["kind"], p["phase"]
        if k == "arm":
            pose[name] = (-ATTACK_LIMB[f] * 1.1, 0, 0, 0, 0, 0)
        elif k == "leg":
            stride = [0.3, -0.5, -0.2, 0.0][f] * (1 if q == 0 else -1)
            pose[name] = (stride, 0, 0, 0, 0, 0)
        elif k == "head":
            pose[name] = (ATTACK_HEAD[f], 0, 0, 0, 0, 0)
        elif k == "tail":
            pose[name] = (0, 0, 0, 0, [0.2, 0.6, 0.8, 0.2][f], 0)
        elif k == "wing":
            pose[name] = (0, 0, [0.7, -0.5, -0.2, 0.2][f] * (1 if q == 0 else -1), 0, 0, 0)
        elif k == "seg":
            pose[name] = (0, 0, 0, 0, 0, 0)
    return (lean, dy, dz), pose


def pose_idle(m):
    return (0.0, 0.0, 0.0), {}


# ---------------------------------------------------------------- 描画
CORNER = np.array([[sx, sy, sz] for sx in (-.5, .5) for sy in (-.5, .5) for sz in (-.5, .5)])
FACES = [  # (corner idx quad, normal)  CORNER index = sx*4 + sy*2 + sz (0/1 -> -/+)
    ((4, 5, 7, 6), (1, 0, 0)), ((0, 2, 3, 1), (-1, 0, 0)),
    ((2, 6, 7, 3), (0, 1, 0)), ((0, 1, 5, 4), (0, -1, 0)),
    ((1, 3, 7, 5), (0, 0, 1)), ((0, 4, 6, 2), (0, 0, -1)),
]


def render(m, body, pose, dirvec, scale=None):
    scale = (m.scale if scale is None else scale)
    lean, bdy, bdz = body
    dx, dy = dirvec
    th = math.atan2(dx, dy)
    cyaw, syaw = math.cos(th), math.sin(th)
    R_lean = rotmat(lean, 0, 0)
    faces = []
    for name, p in m.parts.items():
        rx, ry, rz, tx, ty, tz = pose.get(name, (0, 0, 0, 0, 0, 0))
        Rp = rotmat(rx, ry, rz)
        for (c, s, col) in p["boxes"]:
            pts = CORNER * s + c
            pts = (pts - p["pivot"]) @ Rp.T + p["pivot"] + np.array([tx, ty, tz])
            nrm_rot = Rp
            pts = pts + np.array([0, m.hover * 0, 0])
            pts = pts @ R_lean.T + np.array([0, bdy, bdz])
            nrm_rot = R_lean @ nrm_rot
            # ヨー(向き)
            x = pts[:, 0] * cyaw + pts[:, 2] * syaw
            z = -pts[:, 0] * syaw + pts[:, 2] * cyaw
            Ry = np.array([[cyaw, 0, syaw], [0, 1, 0], [-syaw, 0, cyaw]])
            nrm_rot = Ry @ nrm_rot
            y = pts[:, 1]
            sxs = x * scale
            sys_ = y * math.cos(PITCH) * scale - z * scale * math.sin(PITCH)
            dep = (y * math.sin(PITCH) + z * math.cos(PITCH)) * scale
            for idx, n in FACES:
                nw = nrm_rot @ np.array(n, float)
                if nw @ VIEW <= 0.02:
                    continue
                t = nw @ LIGHT
                k = 1.14 if t > 0.62 else (0.92 if t > 0.12 else 0.68)
                quad = [(sxs[i], sys_[i]) for i in idx]
                faces.append((dep[list(idx)].mean(), quad, mul(col, k), mul(col, k * 0.62)))
    # 前傾・突進で足元が下がりすぎた分は持ち上げる（セル下端にはみ出さないよう、足元の許容は 7px）
    smin = min(qy for _, q, _, _ in faces for _, qy in q) if faces else 0.0
    lift = max(0.0, -smin - 7.0 * SS / PPU)
    faces = [(dp, [(qx, qy + lift) for qx, qy in q], c1, c2) for dp, q, c1, c2 in faces]
    W = CELL * SS
    img = Image.new("RGBA", (W, W), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    ox, oy = W / 2, (CELL - FEET_MARGIN) * SS
    # 影（足元・半透明）
    fr = m.footprint * scale * PPU
    rx = min(fr, 40 * SS)
    ry = min(rx * 0.42, (FEET_MARGIN - 3) * SS)   # セル下端にはみ出さない
    d.ellipse([ox - rx, oy - ry, ox + rx, oy + ry], fill=(0, 0, 0, 70))
    faces.sort(key=lambda f: f[0])
    for _, quad, col, edge in faces:
        pts = [(ox + qx * PPU, oy - qy * PPU) for qx, qy in quad]
        d.polygon(pts, fill=col + (255,), outline=edge + (255,))
    return img.resize((CELL, CELL), Image.BOX)


def render_all(m):
    out = {}
    for dname, dv in DIRS:
        out[dname] = [render(m, *pose_idle(m), dv)]
        out[dname + "_w"] = [render(m, *pose_walk(m, f), dv) for f in range(4)]
        out[dname + "_a"] = [render(m, *pose_attack(m, f), dv) for f in range(4)]
    return out


def touches_edge(imgs, margin=1):
    for im in imgs:
        a = np.asarray(im)[:, :, 3]
        if a[:margin].any() or a[-margin:].any() or a[:, :margin].any() or a[:, -margin:].any():
            return True
    return False


def make_species(sp, tier_scale):
    seed = zlib.crc32(sp["species_id"].encode("utf-8")) & 0xFFFFFFFF
    m = build_model(sp, random.Random(seed), tier_scale)
    for _ in range(30):
        frames = render_all(m)
        flat = [im for v in frames.values() for im in v]
        if not touches_edge(flat, 2):
            return frames
        m.scale *= 0.92
    return frames


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


def load_species():
    with open(os.path.join(ROOT, "Data", "species.json"), encoding="utf-8") as f:
        sps = json.load(f)
    bst = sorted(sum(s[k] for k in ("base_hp", "base_atk", "base_def", "base_spatk", "base_spdef", "base_speed")) for s in sps)
    qs = [bst[len(bst) * i // 4] for i in (1, 2, 3)]
    def tier(s):
        b = sum(s[k] for k in ("base_hp", "base_atk", "base_def", "base_spatk", "base_spdef", "base_speed"))
        return SCALE_TIERS[sum(b >= q for q in qs)]
    return sps, tier


def contact_sheet(ids, out):
    sps, tier = load_species()
    by = {s["species_id"]: s for s in sps}
    H = 144
    rows = []
    for sid in ids:
        fr = make_species(by[sid], tier(by[sid]))
        cols = [fr[d][0] for d in ("front", "back", "left", "fr")]
        cols += [fr["left_w"][0], fr["left_w"][2]] + [fr["fr_a"][i] for i in (0, 1, 2)]
        row = Image.new("RGBA", (H * len(cols), H), (235, 235, 235, 255))
        for i, im in enumerate(cols):
            row.alpha_composite(im.resize((H, H), Image.LANCZOS), (i * H, 0))
        rows.append(row)
    sheet = Image.new("RGB", (rows[0].width, H * len(rows)), (255, 255, 255))
    for i, r in enumerate(rows):
        sheet.paste(r.convert("RGB"), (0, i * H))
    sheet.save(out)


def main(argv):
    if argv and argv[0] == "--sheet":
        out = os.path.join(os.environ.get("SHEET_DIR", "/tmp"), "pal_sheet.png")
        contact_sheet(argv[1:], out)
        print(out)
        return
    sps, tier = load_species()
    only = set(argv)
    todo = [sp for sp in sps if not only or sp["species_id"] in only]
    from multiprocessing import Pool
    with Pool() as pool:
        for _ in pool.imap_unordered(_job, [(sp, tier(sp)) for sp in todo]):
            pass
    print("generated", len(todo))


def _job(arg):
    sp, ts = arg
    old = os.path.join(ROOT, "Assets", "Sprites", sp["species_id"])
    if os.path.isdir(old):   # 旧規格(960x800・別名ファイル)を一掃
        for fn in os.listdir(old):
            if fn.endswith(".png") or fn.endswith(".png.import"):
                os.remove(os.path.join(old, fn))
    write_species(sp["species_id"], make_species(sp, ts), ROOT)


if __name__ == "__main__":
    main(sys.argv[1:])
