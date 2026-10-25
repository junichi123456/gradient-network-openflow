#!/usr/bin/env python3
"""--random --out の CSV を属性別に集計する。

usage: analyze_random_by_element.py <csv>... [--species Data/species.json]

CSV 列（ヘッダ無し）: match,outcome,home_sel,away_sel,home_bst,away_bst,cycles
 - 属性 X の勝率: 選出に X を持つ個体（二重属性は両方に計上）の出場1回ごと。
   同じ側に X が2体いれば2回数える。U・D は分母から除く。
 - 期待勝率: 全試合で P(自陣勝ち)=1/(1+exp(-k*(home_bst-away_bst))) を最尤で当て、
   その出場の試合の期待値の平均。補正後差 = 実勝率 - 期待勝率 (pt)。
"""
import sys, json, math, os

def main():
    args = sys.argv[1:]
    sp_path = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'Data', 'species.json')
    if '--species' in args:
        i = args.index('--species'); sp_path = args[i + 1]; del args[i:i + 2]
    types = {s['species_id']: s['types'] for s in json.load(open(sp_path, encoding='utf-8'))}
    rows = []
    for p in args:
        for line in open(p, encoding='utf-8'):
            line = line.strip()
            if not line: continue
            m, oc, hs, as_, hb, ab, cy = line.split(',')
            rows.append((oc, hs.split('|'), as_.split('|'), int(hb), int(ab)))
    dec = [r for r in rows if r[0] in ('H', 'A')]
    print(f'試合数 {len(rows)} / 決着 {len(dec)} / 引分 {sum(r[0]=="D" for r in rows)} / 未決着 {sum(r[0]=="U" for r in rows)}')
    # 最尤 k（対数尤度は k について凹なので二分法で勾配0を探す）
    diffs = [(r[3] - r[4], 1 if r[0] == 'H' else 0) for r in dec]
    def grad(k):
        g = 0.0
        for d, y in diffs:
            p = 1 / (1 + math.exp(-max(-50, min(50, k * d))))
            g += (y - p) * d
        return g
    lo, hi = 0.0, 1.0
    while grad(hi) > 0 and hi < 1e3: hi *= 2
    for _ in range(80):
        mid = (lo + hi) / 2
        if grad(mid) > 0: lo = mid
        else: hi = mid
    k = (lo + hi) / 2
    print(f'BST差ロジスティック k = {k:.6f}')
    stat = {}
    for oc, hs, as_, hb, ab in dec:
        ph = 1 / (1 + math.exp(-k * (hb - ab)))
        for side, sel in (('H', hs), ('A', as_)):
            won = (oc == side)
            exp = ph if side == 'H' else 1 - ph
            for sid in sel:
                for t in types[sid]:
                    s = stat.setdefault(t, [0, 0, 0.0])
                    s[0] += 1; s[1] += won; s[2] += exp
    order = ['Neutral', 'Fire', 'Water', 'Grass', 'Electric', 'Ground', 'Ice', 'Dragon', 'Dark', 'Void']
    print(f'{"属性":<9}{"出場数":>8}{"勝率%":>8}{"期待%":>8}{"補正後pt":>9}')
    for t in order + [t for t in stat if t not in order]:
        if t not in stat: continue
        n, w, e = stat[t]
        print(f'{t:<9}{n:>8}{100*w/n:>8.1f}{100*e/n:>8.1f}{100*(w-e)/n:>+9.1f}')

main()
