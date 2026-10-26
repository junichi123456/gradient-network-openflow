#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""範囲技（部屋・範囲）の威力を基準威力から引く。冪等。

usage: python3 Tools/apply_aoe_power_cut.py --cut {0|10|15|20} [--acc]
  --cut 0 で前提コミットの値に戻る。--acc は --cut 20 のときだけ（基準威力80以上の命中-5）。
仕様書: docs/design/drafts/2026-10-27-aoe-power.md
"""
import argparse, json, os, re, sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import aoe_power_rules as R

MOVES = os.path.join(HERE, '..', 'Data', 'moves.json')
RULES = os.path.join(HERE, 'aoe_power_rules.py')


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--cut', type=int, required=True, choices=(0, 10, 15, 20))
    ap.add_argument('--acc', action='store_true')
    a = ap.parse_args()
    if a.acc and a.cut != 20:
        sys.exit('--acc は --cut 20 のときだけ')
    ms = json.load(open(MOVES, encoding='utf-8'))
    red = R.solve(a.cut, ms)
    by = {m['id']: m for m in ms}
    missing = [i for i in R.AOE_BASE if i not in by]
    if missing:
        sys.exit(f'moves.json に無い対象技: {missing}')
    print(f'--cut {a.cut}{" --acc" if a.acc else ""}')
    odd = []
    for n, (i, (bp, ba)) in enumerate(R.AOE_BASE.items(), 1):
        m = by[i]
        m['power'] = bp - red[i]
        m['accuracy'] = ba - R.acc_cut(i, a.acc)
        nom = 0 if a.cut == 0 else (10 if bp <= 35 else a.cut)
        mark = ''
        if red[i] != nom:
            mark = ' ※'; odd.append(i)
        print(f'{n:>2} {i:<20} {m["name"]:<14} {bp:>3} -> {m["power"]:>3} (-{red[i]}) 命中 {ba} -> {m["accuracy"]}{mark}')
    if odd:
        print('公称からずれた技: ' + ', '.join(f'{i}(-{red[i]})' for i in odd))
    open(MOVES, 'w', encoding='utf-8').write(json.dumps(ms, indent=2, ensure_ascii=False) + '\n')
    src = open(RULES, encoding='utf-8').read()
    new, n = re.subn(r'^ADOPTED = \(.*\)[ \t]*$', f'ADOPTED = ({a.cut}, {a.acc})', src, flags=re.M)
    if n != 1:
        sys.exit('ADOPTED の行が見つからない')
    open(RULES, 'w', encoding='utf-8').write(new)


main()
