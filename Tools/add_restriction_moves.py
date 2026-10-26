#!/usr/bin/env python3
"""制限技12種の追加 (仕様書 docs/design/drafts/2026-10-25-void-type-and-habcds.md Phase 6)。

やること（何度実行しても同じ結果になる）:
  - Data/moves.json に id が rs_ で始まる攻撃技12個を足す（既にあれば作り直す）
  - Data/species.json: 習得個体（RESTRICTION_LEARNERS）の learnset に level 30 で1つずつ足す

全て威力のある攻撃技で、制限は命中してダメージが入ったときの追加効果（NPC は変化技を使わないため）。
威力・射程は仕様書 6-b の値のまま。既存プールでは闇/水/地/草/無の物理の 35〜95 帯がどれも同威力2種で埋まっており、
±5 では空きが無い（仕様書の「触れたら ±5」が成立しない）ので、verify_attack_moves.py 側で rs_ 技を
「同威力は2種まで」「射程上限」の集計から外す（既存プールだけでその規則が保たれていることは引き続き検査する）。
"""
import json, os, sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from learnset_rules import RESTRICTION_LEARNERS

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
D = lambda n: os.path.join(ROOT, 'Data', n)

LEARN_LEVEL = 30

# id, 名前, 属性, 分類, 威力(仕様書), 命中, PP, 射程, 接触, restriction, restriction_turns, 追加キー
SPEC = [
    ('rs_seal_snap',    'シールスナップ',     'Void',     'Special',  40, 100, 8, 'Adjacent', False, 'SealLastMove', 2, {}),
    ('rs_provoke',      'プロヴォーク',       'Dark',     'Physical', 45, 100, 8, 'Adjacent', True,  'NoStatusMoves', 2, {}),
    ('rs_repeat_chant', 'リピートチャント',   'Neutral',  'Special',  40, 100, 8, 'Line',     False, 'Encore',        1, {}),
    ('rs_item_jam',     'グリッチボルト',     'Electric', 'Special',  45, 100, 8, 'TwoTile',  False, 'ItemJam',       3, {}),
    ('rs_tide_hook',    'タイドフック',       'Water',    'Physical', 50, 100, 8, 'TwoTile',  False, 'Pull',          0, {}),
    ('rs_frost_lag',    'フロストラグ',       'Ice',      'Special',  45, 100, 8, 'Line',     False, 'PriorityDown',  1, {}),
    ('rs_sand_eater',   'サンドイーター',     'Ground',   'Physical', 50, 100, 8, 'Adjacent', True,  'PpDrain',       0, {}),
    ('rs_scorch_smog',  'スコーチスモッグ',   'Fire',     'Special',  40, 100, 6, 'Area',     False, 'HealBlock',     3, {}),
    ('rs_dragon_anchor','ドラゴンアンカー',   'Dragon',   'Special',  55, 100, 8, 'Adjacent', False, 'RankUpBlock',   3, {}),
    ('rs_short_leash',  'ショートリーシュ',   'Grass',    'Physical', 45, 100, 8, 'Adjacent', True,  'AdjacentOnly',  2, {}),
    ('rs_nag_jab',      'ナグジャブ',         'Neutral',  'Physical', 40, 100, 8, 'Adjacent', True,  'NoRepeat',      3, {}),
    ('rs_hollow_touch', 'ホロウタッチ',       'Void',     'Special',  50, 100, 6, 'Adjacent', False, None,            0,
     {'ailment_effect': 'Hollow', 'ailment_chance': 100}),
]


def main():
    moves = json.load(open(D('moves.json'), encoding='utf-8'))
    species = json.load(open(D('species.json'), encoding='utf-8'))

    # 冪等性: 既存の rs_ 技と、その learnset 行を取り除く
    moves = [m for m in moves if not m['id'].startswith('rs_')]
    for s in species:
        s['learnset'] = [e for e in s['learnset'] if not e['move_id'].startswith('rs_')]

    names = {m['name'] for m in moves}
    ids = {m['id'] for m in moves}
    new = []
    for mid, name, typ, cat, power, acc, pp, rng, contact, restr, turns, extra in SPEC:
        assert mid not in ids and name not in names, (mid, name)
        m = {'id': mid, 'name': name, 'type': typ, 'category': cat, 'power': power,
             'accuracy': acc, 'max_pp': pp, 'range': rng, 'is_contact': contact}
        m.update(extra)
        if restr:
            m['restriction'] = restr
            if turns:
                m['restriction_turns'] = turns
        new.append(m)
        names.add(name)
        ids.add(mid)
    moves += new

    by_id = {s['species_id']: s for s in species}
    M = {m['id']: m for m in new}
    for mid, learners in RESTRICTION_LEARNERS.items():
        assert mid in M, mid
        for sid in learners:
            by_id[sid]['learnset'].append({'level': LEARN_LEVEL, 'move_id': mid})

    with open(D('moves.json'), 'w', encoding='utf-8') as f:
        f.write(json.dumps(moves, indent=2, ensure_ascii=False) + '\n')
    with open(D('species.json'), 'w', encoding='utf-8') as f:
        f.write(json.dumps(species, indent=2, ensure_ascii=False) + '\n')

    print('制限技', len(new), '種を追加 / 習得個体', sum(len(v) for v in RESTRICTION_LEARNERS.values()), '種')


if __name__ == '__main__':
    main()
