#!/usr/bin/env python3
"""プロテクション (mvs_ice_protection) の追加 (仕様書 docs/design/drafts/2026-10-29-protection.md §3-1・§3-5)。

やること（何度実行しても同じ結果になる）:
  - Data/moves.json の末尾に氷の変化技「プロテクション」を1つ足す（既にあれば取り除いて末尾に作り直す）
  - Data/species.json: 両刀攻撃かつ BST6<=470 の個体（learnset_rules.protection_learner）の learnset に
    level 10 で1行足す。挿入位置は「level が 10 以下の最後の行の直後」（全体は並べ替えない）

generate_learnset.py で learnset を作り直したら、add_restriction_moves.py の後にこれを実行する
（生成し直すとこの技の行と rs_ 制限技の行が消える）。
"""
import json, os, sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from learnset_rules import PROTECTION_ID, PROTECTION_LEVEL, protection_learner

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
D = lambda n: os.path.join(ROOT, 'Data', n)

EXPECTED = ['007', '007B', '016', '021B', '022', '023', '030', '036', '041B', '042', '048B', '064B',
            '073', '073B', '083B', '084', '084B', '096', '100', '100B', '101', '108', '112B', '127',
            '133', '138B', '154', '169B', '178', '188', '188B']

MOVE = {
    'id': PROTECTION_ID,
    'name': 'プロテクション',
    'type': 'Ice',
    'category': 'Status',
    'power': 0,
    'accuracy': 100,
    'max_pp': 10,
    'range': 'FrontArc',
    'priority': 3,
    'grants_protection': True,
}


def main():
    moves = json.load(open(D('moves.json'), encoding='utf-8'))
    species = json.load(open(D('species.json'), encoding='utf-8'))

    moves = [m for m in moves if m['id'] != PROTECTION_ID]
    assert MOVE['name'] not in {m['name'] for m in moves}, 'name collision'
    moves.append(dict(MOVE))

    n = 0
    got = []
    for s in species:
        s['learnset'] = [e for e in s['learnset'] if e['move_id'] != PROTECTION_ID]
        if not protection_learner(s):
            continue
        ls = s['learnset']
        pos = 0
        for i, e in enumerate(ls):
            if e['level'] <= PROTECTION_LEVEL:
                pos = i + 1
        ls.insert(pos, {'level': PROTECTION_LEVEL, 'move_id': PROTECTION_ID})
        got.append(s['species_id'])
        n += 1
    assert sorted(got) == EXPECTED, (sorted(got), EXPECTED)
    assert n == 31

    with open(D('moves.json'), 'w', encoding='utf-8') as f:
        f.write(json.dumps(moves, indent=2, ensure_ascii=False) + '\n')
    with open(D('species.json'), 'w', encoding='utf-8') as f:
        f.write(json.dumps(species, indent=2, ensure_ascii=False) + '\n')
    print('プロテクションを追加 / 習得', n, '種')


if __name__ == '__main__':
    main()
