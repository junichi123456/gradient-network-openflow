# -*- coding: utf-8 -*-
"""両刀攻撃(offense == "Mixed")12種の選定と、その2つめの特性 trait2 の付与。

固定シード(20261030)のデータ生成スクリプト。実行時の乱数は使わず、冪等。
仕様: docs/design/drafts/2026-10-30-speed-and-stat-redo.md §3-5。
  select_mixed12(species) : 両刀12種の species_id 集合（build_habcds.py が使う）
  assign(species, traits_by_id, moves_by_id) : species_id -> trait2
import しただけでは何も書き込まない。
"""
import json
import os
import random
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import build_habcds as BH
from learnset_rules import defense_of

SEED = 20261030
MIXED_COUNT = 12
PER_ELEMENT_MAX = 2
RANDOM3 = ['wide_burst', 'kowaza_jouzu', 'shimikomi']
TERRAIN = [('Water', 'entry_puddle'), ('Grass', 'entry_flowerbed'), ('Ice', 'entry_thin_ice')]
CAND_TRAIT = re.compile(r'(_bond|_power|_guard|_oshie)$|^keikaikan$|^koufun$')
MIN_T = 255      # BST6 - S の下限（予算）
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


def classify(s, traits_by_id=None):
    """'priority'（候補の特性）/ 'other'。リーダー系は CAND_TRAIT に含まれないので other。"""
    return 'priority' if CAND_TRAIT.search(s['trait']) else 'other'


def room_attack_count(s, moves_by_id):
    ids = {e['move_id'] for e in s['learnset']}
    return sum(1 for i in ids
               if moves_by_id[i]['range'] == 'Room' and moves_by_id[i]['power'] > 0)


def candidates(species):
    """候補: 特性が CAND_TRAIT、defense != Both、BST6-S >= 255、両刀として配分して条件を満たせる種。"""
    speed = json.load(open(os.path.join(ROOT, 'Data', 'source', 'speed_fixed.json'), encoding='utf-8'))
    order = {s['species_id']: i for i, s in enumerate(sorted(species, key=lambda s: s['species_id']))}
    out = []
    for s in sorted(species, key=lambda s: s['species_id']):
        sid = s['species_id']
        if classify(s) != 'priority':
            continue
        idx = order[sid]
        if defense_of(s['trait'], idx) == 'Both':
            continue
        had = BH.had_of(s)
        b6 = BH.bst6_of(had['hp'] + had['atk'] + had['def'])
        if b6 - speed[sid] < MIN_T:
            continue
        H, A, B, C, D, S, w, moved = BH.compute(had['hp'], had['atk'], had['def'], 'Mixed',
                                               defense_of(s['trait'], idx), speed[sid], idx, b6)
        if moved < 0 or not BH.mixed_ok(H, A, B, C, D, defense_of(s['trait'], idx)):
            continue
        if min(H, A, B, C, D, S) < BH.FLOOR_EACH:
            continue
        out.append(s)
    return out


def select_mixed12(species):
    """両刀12種の species_id の集合。旧両刀（offense_prev.json）の候補を優先し、主属性ごと最大2種。"""
    prev = BH.load_prev(species)
    cands = candidates(species)
    rng = random.Random(SEED)
    pri = [s for s in cands if prev[s['species_id']]['offense'] == 'Mixed']
    rest = [s for s in cands if prev[s['species_id']]['offense'] != 'Mixed']
    rng.shuffle(pri)
    rng.shuffle(rest)
    chosen, cnt = [], {}
    for s in pri + rest:
        e = s['types'][0]
        if cnt.get(e, 0) >= PER_ELEMENT_MAX:
            continue
        chosen.append(s['species_id'])
        cnt[e] = cnt.get(e, 0) + 1
        if len(chosen) == MIXED_COUNT:
            break
    if len(chosen) < 9:
        raise SystemExit(f'両刀が {len(chosen)} 種しか選べない（9未満）')
    return set(chosen)


def assign(species, traits_by_id, moves_by_id):
    """species_id -> trait2 の dict（両刀12種すべて）。"""
    mixed_ids = select_mixed12(species)
    mixed = sorted((s for s in species if s['species_id'] in mixed_ids), key=lambda s: s['species_id'])
    rng = random.Random(SEED + 1)
    out = {}
    # 地形特性: 主属性(types[0])が合う種に各1体まで
    for elem, tid in TERRAIN:
        c = [s for s in mixed if s['types'][0] == elem and s['species_id'] not in out]
        if c:
            out[rng.choice(c)['species_id']] = tid
    rem = [s for s in mixed if s['species_id'] not in out]
    n = len(rem)
    order = RANDOM3[:]
    rng.shuffle(order)
    counts = {t: n // 3 + (1 if i < n % 3 else 0) for i, t in enumerate(order)}
    room = {s['species_id']: room_attack_count(s, moves_by_id) for s in rem}
    rng.shuffle(rem)
    # wide_burst は部屋技を覚える種だけ（前回と同じ制約）。足りないときは、その分を kowaza_jouzu・shimikomi が引き受ける
    elig = [s for s in rem if room[s['species_id']] > 0]
    nwb = min(counts['wide_burst'], len(elig))
    for s in elig[:nwb]:
        out[s['species_id']] = 'wide_burst'
    others = [s for s in rem if s['species_id'] not in out]
    rng.shuffle(others)
    k = len(others) // 2 + (len(others) % 2 if order.index('kowaza_jouzu') < order.index('shimikomi') else 0)
    for i, s in enumerate(others):
        out[s['species_id']] = 'kowaza_jouzu' if i < k else 'shimikomi'
    return out


def main():
    species = json.load(open('Data/species.json', encoding='utf-8'))
    traits_by_id = {t['id']: t for t in json.load(open('Data/traits.json', encoding='utf-8'))}
    moves_by_id = {m['id']: m for m in json.load(open('Data/moves.json', encoding='utf-8'))}
    for tid in RANDOM3 + [t for _, t in TERRAIN]:
        if tid not in traits_by_id:
            raise SystemExit(f'未定義の特性: {tid}')
    for s in species:
        s.pop('trait2', None)
    result = assign(species, traits_by_id, moves_by_id)
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
    by_name = {s['species_id']: s for s in species}
    for tid in RANDOM3 + [t for _, t in TERRAIN]:
        ids = sorted(k for k, v in result.items() if v == tid)
        print(f"{traits_by_id[tid]['name']} ({tid}): {len(ids)}体")
        for sid in ids:
            print(f"    {sid} {by_name[sid]['display_name']} {'/'.join(by_name[sid]['types'])} (1つめ: {by_name[sid]['trait']})")
    print(f"\n付与合計 {len(result)} 体 / 全 {len(species)} 種")


if __name__ == '__main__':
    main()
