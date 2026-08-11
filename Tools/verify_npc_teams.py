#!/usr/bin/env python3
"""Data/npc_teams.json が対戦の構築規則を満たしているか検証する。

生成器（generate_npc_teams.py）とは独立に、出来上がったデータだけを見る。
規則の実体は C# 側の BattleTeam.Validate にあるので、ここはその写しになる
——写しである以上ずれうるので、対戦開始時に BattleTeam.Validate も通す
（BattleTestScene がNPC8人ぶんを実際に読んで検証している）。

  python3 Tools/verify_npc_teams.py
"""

import json
import os

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DATA = os.path.join(ROOT, 'Data')

ROSTER_SIZE = 6
MOVES_PER_PAL = 4


def load(name):
    with open(os.path.join(DATA, name), encoding='utf-8') as f:
        return json.load(f)


def main():
    teams = load('npc_teams.json')
    species = {s['species_id']: s for s in load('species.json')}
    moves = {m['id']: m for m in load('moves.json')}
    items = {i['id']: i for i in load('items.json')}
    held = {i['id'] for i in items.values() if i['type'] == 'BattleHeld'}

    checks = []

    def check(label, bad):
        checks.append((label, bad))
        head = f'[{"PASS" if not bad else "FAIL"}] {label}'
        print(f'{head}  違反{len(bad)}件 {bad[:5]}')

    check('相手が1人以上いる', [] if teams else ['npc_teams.json が空'])
    check('IDが重複しない',
          [t['id'] for t in teams if [x['id'] for x in teams].count(t['id']) > 1])

    bad = [f"{t['name']}: {len(t['entries'])}匹" for t in teams
           if len(t['entries']) != ROSTER_SIZE]
    check(f'{ROSTER_SIZE}匹ちょうど', bad)

    bad = []
    for t in teams:
        ids = [e['species_id'] for e in t['entries']]
        dup = {i for i in ids if ids.count(i) > 1}
        if dup:
            bad.append(f"{t['name']}: {sorted(dup)}")
        for i in ids:
            if i not in species:
                bad.append(f"{t['name']}: 未知の種族 {i}")
    check('同一種族の重複なし・実在する種族', bad)

    bad = []
    for t in teams:
        for e in t['entries']:
            if len(e['move_ids']) != MOVES_PER_PAL:
                bad.append(f"{t['name']}/{e['species_id']}: {len(e['move_ids'])}技")
            if len(set(e['move_ids'])) != len(e['move_ids']):
                bad.append(f"{t['name']}/{e['species_id']}: 同じ技が重複")
    check(f'各自ちょうど{MOVES_PER_PAL}技（重複なし）', bad)

    bad = []
    for t in teams:
        for e in t['entries']:
            sp = species.get(e['species_id'])
            if not sp:
                continue
            learn = {l['move_id'] for l in sp['learnset']}
            for m in e['move_ids']:
                if m not in moves:
                    bad.append(f"{t['name']}/{e['species_id']}: 未知の技 {m}")
                elif m not in learn:
                    bad.append(f"{t['name']}/{sp['display_name']}: learnset外 {moves[m]['name']}")
    check('技はすべて learnset 内', bad)

    bad = []
    for t in teams:
        ids = [e['item_id'] for e in t['entries']]
        dup = {i for i in ids if ids.count(i) > 1}
        if dup:
            bad.append(f"{t['name']}: 持ち物の重複 {sorted(dup)}")
        for i in ids:
            if i not in held:
                bad.append(f"{t['name']}: 対戦用の持ち物ではない {i}")
    check('持ち物は対戦用・チーム内で重複なし', bad)

    # 攻撃手段が無いと相手として成立しない。1匹でも無攻撃だと、その匹は
    # 一生わざを撃たない置物になる。
    bad = []
    for t in teams:
        for e in t['entries']:
            if not any(moves.get(m, {}).get('power', 0) > 0 for m in e['move_ids']):
                bad.append(f"{t['name']}/{e['species_id']}: 攻撃技なし")
    check('全員が攻撃技を1つ以上持つ', bad)

    print()
    failed = [c for c in checks if c[1]]
    print('総合: ' + ('ALL PASS' if not failed else f'{len(failed)}件 FAIL'))
    return 1 if failed else 0


if __name__ == '__main__':
    raise SystemExit(main())
