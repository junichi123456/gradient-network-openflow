#!/usr/bin/env python3
"""種族値 S が 95 以上の個体の learnset から、優先度 +1 以上の攻撃技を外す。

仕様書 docs/design/drafts/2026-10-26-stat-rework.md S1-f。

  python3 Tools/apply_fast_priority_rule.py     # 何度実行しても同じ結果（冪等）

該当する行の技 X を、候補 =「X と同じ type・category・range・is_contact、priority <= 0、power > 0、
id が rs_ で始まらず、SIGNATURE に無く、その個体の learnset に未登録」の技のうち
(|power - X.power|, power, id) 昇順の先頭へ、同じ level のまま置き換える。
候補が無ければ置き換えずに非0終了。最後に「S>=95 の個体に優先度+1以上の攻撃技が無い」を検査し、
違反があれば非0終了する。
"""
import json, os, sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from learnset_rules import SIGNATURE, fast_blocks, FAST_SPEED_MIN

SPECIES = os.path.join(ROOT, 'Data', 'species.json')
MOVES = os.path.join(ROOT, 'Data', 'moves.json')


def main():
    species = json.load(open(SPECIES, encoding='utf-8'))
    moves = json.load(open(MOVES, encoding='utf-8'))
    M = {m['id']: m for m in moves}
    changed = []
    for s in species:
        have = {e['move_id'] for e in s['learnset']}
        for e in s['learnset']:
            x = M[e['move_id']]
            if not fast_blocks(s, x):
                continue
            cands = [m for m in moves
                     if m['type'] == x['type'] and m['category'] == x['category']
                     and m['range'] == x['range'] and m.get('is_contact') == x.get('is_contact')
                     and (m.get('priority') or 0) <= 0 and m['power'] > 0
                     and not m['id'].startswith('rs_') and m['id'] not in SIGNATURE
                     and m['id'] not in have]
            if not cands:
                print(f'{s["species_id"]} {s["display_name"]}: {x["id"]} の置き換え先が無い')
                sys.exit(1)
            new = sorted(cands, key=lambda m: (abs(m['power'] - x['power']), m['power'], m['id']))[0]
            have.discard(x['id'])
            have.add(new['id'])
            e['move_id'] = new['id']
            changed.append((s['species_id'], s['display_name'], s['base_speed'], e['level'], x['id'], new['id']))

    bad = [(s['species_id'], e['move_id']) for s in species for e in s['learnset']
           if fast_blocks(s, M[e['move_id']])]
    if bad:
        print('違反が残っている:', bad)
        sys.exit(1)
    for sid, nm, sv, lv, old, new in changed:
        print(f'{sid} {nm} (S{sv}) Lv{lv}: {old} -> {new}')
    with open(SPECIES, 'w', encoding='utf-8') as f:
        json.dump(species, f, ensure_ascii=False, indent=2)
        f.write('\n')
    n_fast = sum(1 for s in species if s['base_speed'] >= FAST_SPEED_MIN)
    print(f'置き換え {len(changed)}件。S>={FAST_SPEED_MIN} の {n_fast}種に優先度+1以上の攻撃技は無い')
    print('自己検査 OK')


if __name__ == '__main__':
    main()
