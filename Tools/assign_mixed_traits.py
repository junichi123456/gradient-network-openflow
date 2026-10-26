# -*- coding: utf-8 -*-
"""両刀攻撃(offense == "Mixed")の一部の個体へ「2つめの特性 trait2」を与える。

固定シード(20261028)のデータ生成スクリプト。実行時の乱数は使わず、冪等。
仕様: docs/design/drafts/2026-10-28-mixed-traits.md §3-4・付録A-2。
`assign` は付録A-2 の参照実装そのまま。定数・乱数を引く順番を変えると付録A と一致しなくなる。
import しただけでは何も書き込まない。
"""
import json
import random

SEED = 20261028
RANDOM3 = ['wide_burst', 'kowaza_jouzu', 'shimikomi']
TERRAIN = [('Water', 'entry_puddle'), ('Grass', 'entry_flowerbed'), ('Ice', 'entry_thin_ice')]
PRIORITY_KEYS = ['きずな', 'ちから', 'おしえ', 'まもり', '式']
EXCLUDE_KEY = 'リーダー'
BASE_COUNT, EXTRA_COUNT = 4, 5


def classify(s, traits_by_id):
    name = traits_by_id[s['trait']]['name']
    if EXCLUDE_KEY in name:
        return 'excluded'
    if any(k in name for k in PRIORITY_KEYS):
        return 'priority'
    return 'other'


def room_attack_count(s, moves_by_id):
    ids = {e['move_id'] for e in s['learnset']}
    return sum(1 for i in ids
               if moves_by_id[i]['range'] == 'Room' and moves_by_id[i]['power'] > 0)


def assign(species, traits_by_id, moves_by_id):
    """species_id -> trait2 の dict を返す（trait2 を持たない個体は含めない）。"""
    mixed = sorted((s for s in species if s.get('offense') == 'Mixed'),
                   key=lambda s: s['species_id'])
    if len(mixed) != 38:
        raise SystemExit(f'両刀攻撃が {len(mixed)} 種（38 種を前提にしている）')
    cls = {s['species_id']: classify(s, traits_by_id) for s in mixed}
    room = {s['species_id']: room_attack_count(s, moves_by_id) for s in mixed}
    rng = random.Random(SEED)
    out = {}

    def free(kind):
        return [s for s in mixed if cls[s['species_id']] == kind and s['species_id'] not in out]

    # 手順4: 地形特性（優先候補から。いなければ優先外から）
    for elem, tid in TERRAIN:
        cands = [s for s in free('priority') if elem in s['types']]
        if not cands:
            cands = [s for s in free('other') if elem in s['types']]
        if not cands:
            raise SystemExit(f'{elem}: 地形特性の候補がいない')
        out[rng.choice(cands)['species_id']] = tid

    # 手順5: 5 体になる特性
    five = rng.choice(RANDOM3)
    counts = {t: (EXTRA_COUNT if t == five else BASE_COUNT) for t in RANDOM3}

    # 手順6: ワイドバースト（部屋技を覚える個体。優先候補 → 優先外）
    p = [s for s in free('priority') if room[s['species_id']] > 0]
    rng.shuffle(p)
    o = [s for s in free('other') if room[s['species_id']] > 0]
    rng.shuffle(o)
    wb = (p + o)[:counts['wide_burst']]
    if len(wb) < counts['wide_burst']:
        raise SystemExit('ワイドバーストの候補が足りない')
    for s in wb:
        out[s['species_id']] = 'wide_burst'

    # 手順7: こわざじょうず・しみこみ（優先候補 → 足りなければ優先外から補う）
    need = counts['kowaza_jouzu'] + counts['shimikomi']
    r = free('priority')
    if len(r) < need:
        o = free('other')
        rng.shuffle(o)
        r = r + o[:need - len(r)]
    if len(r) < need:
        raise SystemExit('こわざじょうず・しみこみの候補が足りない')
    r.sort(key=lambda s: s['species_id'])
    rng.shuffle(r)
    r = r[:need]
    for i, s in enumerate(r):
        out[s['species_id']] = 'kowaza_jouzu' if i < counts['kowaza_jouzu'] else 'shimikomi'
    return out


def main():
    species = json.load(open('Data/species.json', encoding='utf-8'))
    traits_by_id = {t['id']: t for t in json.load(open('Data/traits.json', encoding='utf-8'))}
    moves_by_id = {m['id']: m for m in json.load(open('Data/moves.json', encoding='utf-8'))}
    for tid in RANDOM3 + [t for _, t in TERRAIN]:
        if tid not in traits_by_id:
            raise SystemExit(f'未定義の特性: {tid}')

    # 手順1: 既存の trait2 を全種で消す（冪等にするため）
    for s in species:
        s.pop('trait2', None)

    result = assign(species, traits_by_id, moves_by_id)

    # 手順8: trait2 を trait の直後に置く（dict の挿入順を組み直す）
    out = []
    for s in species:
        t2 = result.get(s['species_id'])
        if not t2:
            out.append(s)
            continue
        ns = {}
        for k, v in s.items():
            ns[k] = v
            if k == 'trait':
                ns['trait2'] = t2
        out.append(ns)
    with open('Data/species.json', 'w', encoding='utf-8') as f:
        json.dump(out, f, ensure_ascii=False, indent=2)
        f.write('\n')

    # 手順9: 一覧
    by_name = {s['species_id']: s for s in species}
    order = RANDOM3 + [t for _, t in TERRAIN]
    for tid in order:
        ids = sorted(k for k, v in result.items() if v == tid)
        print(f"{traits_by_id[tid]['name']} ({tid}): {len(ids)}体")
        for sid in ids:
            print(f"    {sid} {by_name[sid]['display_name']}  (1つめ: {by_name[sid]['trait']})")
    none = sorted(s['species_id'] for s in species
                  if s.get('offense') == 'Mixed' and s['species_id'] not in result)
    print(f"\ntrait2 を持たない両刀: {len(none)}体")
    for sid in none:
        print(f"    {sid} {by_name[sid]['display_name']}  (1つめ: {by_name[sid]['trait']})")
    print(f"\n付与合計 {len(result)} 体 / 全 {len(species)} 種")


if __name__ == '__main__':
    main()
