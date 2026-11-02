#!/usr/bin/env python3
"""Data/story/events/*.json の並びどおりに Data/story/charts.json の events[] を同期する（冪等）。
 - cNN.json            -> チャート cNN
 - x_misc.json         -> xNN_intro は xNN、cNN_e.. は cNN（スタブ21〜30）、
                          x_mem_cNN は補完チャート cNN の末尾（思い出の回収用・トリガー無し）、
                          x_pillars_hint は c20（chart_end トリガー）
"""
import json, re, glob, os
from collections import OrderedDict
R = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
S = os.path.join(R, "Data/story")
charts = json.load(open(os.path.join(S, "charts.json"), encoding="utf-8"))
by = {c["id"]: c for c in charts}
ev = OrderedDict()
for f in sorted(glob.glob(os.path.join(S, "events/*.json"))):
    name = os.path.basename(f)[:-5]
    data = json.load(open(f, encoding="utf-8"))
    for e in data:
        i = e["id"]
        if name == "x_misc":
            if re.match(r"^x\d\d_", i): ch = i[:3]
            elif i.startswith("x_mem_"): ch = i[6:]
            elif i == "x_pillars_hint": ch = "c20"
            else: ch = i.split("_")[0]
        else:
            ch = name
        ev.setdefault(ch, []).append(i)
for ch, ids in ev.items():
    if ch not in by:
        print("WARN chart missing:", ch); continue
    by[ch]["events"] = ids
with open(os.path.join(S, "charts.json"), "w", encoding="utf-8") as fp:
    json.dump(charts, fp, ensure_ascii=False, indent=2); fp.write("\n")
print("synced", len(ev), "charts")
