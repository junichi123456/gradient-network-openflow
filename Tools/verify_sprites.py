#!/usr/bin/python3
"""Assets/Sprites/{id}/ の規格検証（全種族）。仕様書 2026-10-29-pal-sprites.md。

  /usr/bin/python3 Tools/verify_sprites.py      # 全件 PASS なら終了コード 0
"""
import json
import os
import sys

import numpy as np
from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CELL = 192
DIRS = ["front", "back", "left", "right", "fl", "fr", "bl", "br"]


def check_cell(a, label, errs):
    if a.shape != (CELL, CELL, 4):
        errs.append("%s: shape %s" % (label, a.shape)); return
    al = a[:, :, 3]
    for (y, x) in ((0, 0), (0, CELL - 1), (CELL - 1, 0), (CELL - 1, CELL - 1)):
        if al[y, x] != 0:
            errs.append("%s: corner not transparent" % label); return
    if al.max() == 0:
        errs.append("%s: empty" % label); return
    if al[0].any() or al[-1].any() or al[:, 0].any() or al[:, -1].any():
        errs.append("%s: touches cell border" % label)
    ys, xs = np.nonzero(al > 0)
    if ys.max() < CELL - 30:
        errs.append("%s: feet too high (bottom y=%d)" % (label, ys.max()))
    cx = (xs.min() + xs.max()) / 2
    if abs(cx - CELL / 2) > 70:
        errs.append("%s: far off-center (cx=%.0f)" % (label, cx))


def main():
    with open(os.path.join(ROOT, "Data", "species.json"), encoding="utf-8") as f:
        ids = [s["species_id"] for s in json.load(f)]
    bad = 0
    for sid in ids:
        d = os.path.join(ROOT, "Assets", "Sprites", sid)
        errs = []
        want = {x + ".png" for x in DIRS} | {"walk.png", "attack.png"}
        have = {fn for fn in os.listdir(d) if fn.endswith(".png")} if os.path.isdir(d) else set()
        if have != want:
            errs.append("files: missing=%s extra=%s" % (sorted(want - have), sorted(have - want)))
        for fn in sorted(want & have):
            im = Image.open(os.path.join(d, fn))
            if im.mode != "RGBA":
                errs.append("%s: mode %s" % (fn, im.mode)); continue
            a = np.asarray(im)
            if fn in ("walk.png", "attack.png"):
                if im.size != (CELL * 4, CELL * 8):
                    errs.append("%s: size %s" % (fn, im.size)); continue
                for r in range(8):
                    for c in range(4):
                        check_cell(a[r * CELL:(r + 1) * CELL, c * CELL:(c + 1) * CELL], "%s[r%d,c%d]" % (fn, r, c), errs)
            else:
                if im.size != (CELL, CELL):
                    errs.append("%s: size %s" % (fn, im.size)); continue
                check_cell(a, fn, errs)
        if errs:
            bad += 1
            print("FAIL", sid, "; ".join(errs[:4]))
    print("species: %d  PASS: %d  FAIL: %d" % (len(ids), len(ids) - bad, bad))
    sys.exit(1 if bad else 0)


if __name__ == "__main__":
    main()
