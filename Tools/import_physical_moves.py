#!/usr/bin/env python3
"""Imports the 180-move physical pack into Data/moves.json.

The source pack uses a different schema from moves.json, so three of the
conversions below are semantic rather than cosmetic - see the comments.
"""
import json, sys

SRC = sys.argv[1]
DST = 'Data/moves.json'

TYPE_JA = {'無':'Neutral','炎':'Fire','水':'Water','雷':'Electric','地':'Ground',
           '草':'Grass','氷':'Ice','竜':'Dragon','闇':'Dark'}
CAT_JA   = {'物理':'Physical','特殊':'Special','変化':'Status'}
RANGE_JA = {'単体':'Adjacent','直線':'Line','2マス':'TwoTile','範囲':'Area',
            '部屋':'Room','フロア全体':'FullFloor'}

src = json.load(open(SRC))
moves = json.load(open(DST))
existing = {m['id'] for m in moves}

added, skipped, notes = 0, [], []
for m in src:
    if m['id'] in existing:
        skipped.append(m['id']); continue

    out = {
        'id': m['id'],
        'name': m['name'],
        'type': TYPE_JA[m['type']],
        'category': CAT_JA[m['category']],
        'power': m['power'],
        'accuracy': m['accuracy'],
        'max_pp': m['max_pp'],
        'range': RANGE_JA[m['range']],
    }

    if m.get('IsContact'):        out['is_contact'] = True
    if m.get('IsGuaranteedHit'):  out['is_guaranteed_hit'] = True
    if m.get('CritRankBonus'):    out['crit_rank_bonus'] = m['CritRankBonus']
    if m.get('RecoilHpPercent'):  out['recoil_hp_percent'] = m['RecoilHpPercent']
    if m.get('SelfStunNextTurn'): out['self_stun_next_turn'] = True

    # (1) AilmentChance is a FRACTION in the pack (0.3) but an integer
    #     percent in moves.json (30). Multiplying is required, not cosmetic:
    #     the accumulation system reads chance*10, so 0.3 would contribute
    #     3 instead of 300 and the rider would effectively never land.
    ail = m.get('AilmentEffect', 'None')
    if ail and ail != 'None':
        out['ailment_effect'] = ail
        out['ailment_chance'] = int(round(m.get('AilmentChance', 0) * 100))

    # (2) DragonMultiplier is 0.0 in the pack where "no multiplier" is
    #     meant, but moves.json's default (and DamageCalculator's identity)
    #     is 1.0. Importing 0.0 verbatim would multiply every such move's
    #     damage to zero.
    dm = m.get('DragonMultiplier', 0.0)
    if dm and dm != 1.0: out['dragon_multiplier'] = dm

    # (3) RankEffectStat carries "DrainHalf", which is NOT a member of the
    #     RankStat enum (None/Atk/Def/Accuracy/Evasion/ElementPower/Crit).
    #     It is the drain mechanic wearing a rank-effect field, so it maps
    #     onto drain_hp_percent instead - "half" = 50, matching the
    #     existing DrainHalf kit. Parsed as a rank stat it would silently
    #     fall back to None and the drain would be lost entirely.
    stat = m.get('RankEffectStat')
    if stat == 'DrainHalf':
        out['drain_hp_percent'] = 50
        notes.append(m['id'])
    elif stat and stat != 'None':
        out['rank_effect_stat'] = stat
        out['rank_effect_delta'] = m.get('RankDelta', 0)
        tgt = m.get('RankEffectTarget')
        out['rank_effect_target'] = tgt if tgt and tgt != 'None' else 'Self'
        chance = m.get('RankEffectChance', 1.0)
        if chance != 1.0: out['rank_effect_chance'] = chance

    moves.append(out); existing.add(m['id']); added += 1

json.dump(moves, open(DST, 'w'), ensure_ascii=False, indent=2)
open(DST, 'a').write('\n')
print(f"imported {added} moves (skipped {len(skipped)}), total now {len(moves)}")
print(f"  DrainHalf -> drain_hp_percent=50 に変換: {len(notes)}件")
