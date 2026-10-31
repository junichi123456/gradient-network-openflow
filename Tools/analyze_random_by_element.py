#!/usr/bin/env python3
"""--random --out の CSV を属性別に集計する。

usage: analyze_random_by_element.py <csv>... [--species Data/species.json] [--by element|offense|defense|role|speed|trait2|species|protection] [--only-mixed] [--bootstrap B]

--by: 集計の区分（既定 element）。offense/defense/role は species.json の同名キー、
speed は種族値 S の帯 (<=62 / 63-87 / 88-99 / >=100)。element 以外は1個体につき1区分（二重計上なし）。
trait2 は2つめの特性の id（持たない個体は 'なし:' + 攻撃区分。両刀で持たない個体は 'なし:Mixed' = 対照群）。
protection はプロテクション（仕様書 docs/design/drafts/2026-10-29-protection.md §3-5 の 8）の群別:
learnset に mvs_ice_protection を持つ個体は '習得:trait2あり' / '習得:trait2なし'、持たない個体は 'なし:' + 攻撃区分。
そのあとに合算行 '習得:計'（2群を合わせた出場単位の勝率・期待・補正後pt。--bootstrap 時は同じリサンプルで SE も出す）。
species は species_id + 表示名（個体別）。--only-mixed を付けると両刀38種の行だけ表示（species 用）。

CSV 列（ヘッダ無し）: match,outcome,home_sel,away_sel,home_bst,away_bst,cycles
 - 属性 X の勝率: 選出に X を持つ個体（二重属性は両方に計上）の出場1回ごと。
   同じ側に X が2体いれば2回数える。U・D は分母から除く。
 - --bootstrap B (既定0=出さない): 試合(CSVの行)を復元抽出でB回リサンプルし、各区分の補正後ptの
   標準偏差を SE 列に出す。k は元の標本の値を固定、乱数は random.Random(20261027)。
 - 期待勝率: 全試合で P(自陣勝ち)=1/(1+exp(-k*(home_bst-away_bst))) を最尤で当て、
   その出場の試合の期待値の平均。補正後差 = 実勝率 - 期待勝率 (pt)。
"""
import sys, json, math, os, random

def main():
    args = sys.argv[1:]
    sp_path = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'Data', 'species.json')
    if '--species' in args:
        i = args.index('--species'); sp_path = args[i + 1]; del args[i:i + 2]
    by = 'element'
    if '--by' in args:
        i = args.index('--by'); by = args[i + 1]; del args[i:i + 2]
    only_mixed = '--only-mixed' in args
    if only_mixed: args.remove('--only-mixed')
    boot = 0
    if '--bootstrap' in args:
        i = args.index('--bootstrap'); boot = int(args[i + 1]); del args[i:i + 2]
    if by not in ('element', 'offense', 'defense', 'role', 'speed', 'trait2', 'species', 'protection'):
        sys.exit('--by は element|offense|defense|role|speed|trait2|species|protection')
    def speed_band(v):
        return '<=62' if v <= 62 else '63-87' if v <= 87 else '88-99' if v <= 99 else '>=100'
    def keys_of(s):
        if by == 'element': return s['types']
        if by == 'speed': return [speed_band(s['base_speed'])]
        if by == 'trait2': return [s.get('trait2') or 'なし:' + s['offense']]
        if by == 'species': return [s['species_id'] + ' ' + s['display_name']]
        if by == 'protection':
            if any(e['move_id'] == 'mvs_ice_protection' for e in s['learnset']):
                return ['習得:trait2あり' if s.get('trait2') else '習得:trait2なし', '習得:計']
            return ['なし:' + s['offense']]
        return [s[by]]
    sp_all = json.load(open(sp_path, encoding='utf-8'))
    types = {s['species_id']: keys_of(s) for s in sp_all}
    mixed_keys = {k for s in sp_all if s['offense'] == 'Mixed' for k in keys_of(s)}
    trait2_of = {s['species_id'] + ' ' + s['display_name']: (s.get('trait2') or '—') for s in sp_all}
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
    neq = [r for r in dec if r[3] != r[4]]
    hw = sum((r[0] == 'H') == (r[3] > r[4]) for r in neq)
    print(f'BST6が高い側の勝ち: {hw}/{len(neq)} = {100*hw/len(neq):.1f}%（同値 {len(dec)-len(neq)} 試合を除く）')
    def tally(sample, k):
        stat = {}
        for oc, hs, as_, hb, ab in sample:
            if oc not in ('H', 'A'): continue
            ph = 1 / (1 + math.exp(-k * (hb - ab)))
            for side, sel in (('H', hs), ('A', as_)):
                won = (oc == side)
                exp = ph if side == 'H' else 1 - ph
                for sid in sel:
                    for t in types[sid]:
                        s = stat.setdefault(t, [0, 0, 0.0])
                        s[0] += 1; s[1] += won; s[2] += exp
        return stat
    stat = tally(dec, k)
    se = {}
    if boot > 0:
        rng = random.Random(20261027)
        samples = {}
        for _ in range(boot):
            st = tally([rows[rng.randrange(len(rows))] for _ in rows], k)
            for t, (n, w, e) in st.items():
                if n: samples.setdefault(t, []).append(100 * (w - e) / n)
        for t, v in samples.items():
            mu = sum(v) / len(v)
            se[t] = math.sqrt(sum((x - mu) ** 2 for x in v) / (len(v) - 1)) if len(v) > 1 else 0.0
    if by == 'element':
        order = ['Neutral', 'Fire', 'Water', 'Grass', 'Electric', 'Ground', 'Ice', 'Dragon', 'Dark', 'Void']
    elif by == 'speed':
        order = ['<=62', '63-87', '88-99', '>=100']
    elif by == 'trait2':
        order = ['wide_burst', 'kowaza_jouzu', 'shimikomi', 'entry_puddle', 'entry_flowerbed', 'entry_thin_ice',
                 'なし:Mixed', 'なし:Physical', 'なし:Special']
    elif by == 'protection':
        order = ['習得:trait2あり', '習得:trait2なし', 'なし:Mixed', 'なし:Physical', 'なし:Special', '習得:計']
    elif by == 'species':
        order = sorted(stat)
    else:
        O = ['Physical', 'Special', 'Mixed']; D = ['Physical', 'Special', 'Both']
        order = {'offense': O, 'defense': D, 'role': [f'{o}-{d}' for o in O for d in D]}[by]
    label = {'element': '属性', 'offense': '攻撃区分', 'defense': '耐久区分', 'role': '役割', 'speed': '俊敏帯', 'trait2': '2つめの特性', 'species': '個体', 'protection': 'プロテクション'}[by]
    print(f'{label:<17}{"出場数":>8}{"勝率%":>8}{"期待%":>8}{"補正後pt":>9}' + (f'{"SE":>7}' if boot > 0 else ''))
    for t in order + [t for t in stat if t not in order]:
        if t not in stat: continue
        if by == 'species' and only_mixed and t not in mixed_keys: continue
        n, w, e = stat[t]
        extra = f'  trait2={trait2_of[t]}' if by == 'species' else ''
        print(f'{t:<17}{n:>8}{100*w/n:>8.1f}{100*e/n:>8.1f}{100*(w-e)/n:>+9.1f}' + (f'{se.get(t, 0.0):>7.2f}' if boot > 0 else '') + extra)

main()
