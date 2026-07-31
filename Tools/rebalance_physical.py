#!/usr/bin/env python3
"""Adds the multi-hit moves and converts Special -> Physical until every
element sits below 55% Special. Deterministic: sorts, no RNG."""
import json, collections

P = 'Data/moves.json'
moves = json.load(open(P))
JA = {'Neutral':'無','Fire':'炎','Water':'水','Grass':'草','Electric':'雷',
      'Ground':'地','Ice':'氷','Dragon':'竜','Dark':'闇'}
ORDER = ['Neutral','Fire','Water','Grass','Electric','Ground','Ice','Dragon','Dark']

def counts(el):
    c = collections.Counter(m['category'] for m in moves if m['type'] == el)
    return c['Physical'], c['Special']

# ---------------------------------------------------------------- 1. multi-hit
# Shape (1) 2-5 hits x25 @acc95, shape (2) 3 hits x40 @acc90 (per-hit accuracy).
# Alternated over the qualifying elements in chart order so both shapes ship and
# no element is favoured by judgement.
MULTI = [
    ('Water',    1, 'mvh_water',    '乱流連打',  'Soaked'),
    ('Electric', 2, 'mvh_electric', '三連雷撃',  'Paralyze'),
    ('Ground',   1, 'mvh_ground',   '岩石乱打',  'MudCaked'),
    ('Ice',      2, 'mvh_ice',      '三連氷刃',  'Freeze'),
    ('Dark',     1, 'mvh_dark',     '影連打',    'Darkness'),
]
existing = {m['id'] for m in moves}
added_multi = []
for el, shape, mid, name, ail in MULTI:
    if mid in existing: continue
    if shape == 1:
        entry = {'id': mid, 'name': name, 'type': el, 'category': 'Physical',
                 'power': 25, 'accuracy': 95, 'max_pp': 15, 'range': 'Adjacent',
                 'is_contact': True, 'multi_hit': 'Variable2To5',
                 'weapon_tag': 'ClawFist'}
    else:
        entry = {'id': mid, 'name': name, 'type': el, 'category': 'Physical',
                 'power': 40, 'accuracy': 90, 'max_pp': 10, 'range': 'Adjacent',
                 'is_contact': True, 'multi_hit': 'RepeatPerHit', 'multi_hit_count': 3,
                 'weapon_tag': 'Slash'}
    moves.append(entry); added_multi.append((el, shape, mid, name))

# ---------------------------------------------------------------- 2. conversion
def sig(m):
    """What "same move" means for the differentiation rule."""
    return (m['power'], m['accuracy'], m.get('ailment_effect', 'None'),
            m.get('ailment_chance', 100), bool(m.get('is_contact', False)))

converted, differentiated = [], []
TAG_CYCLE = ['Slash', 'ClawFist']

for el in ORDER:
    p, s = counts(el)
    if s / (p + s) < 0.55: continue

    phys_sigs = {sig(m) for m in moves if m['type'] == el and m['category'] == 'Physical'}
    specials = sorted([m for m in moves if m['type'] == el and m['category'] == 'Special'],
                      key=lambda m: m['id'])
    # Prefer converting specials whose signature does NOT already exist among
    # this element's physical moves - fewer forced differentiations.
    specials.sort(key=lambda m: (sig(m) in phys_sigs, m['id']))

    need = 0
    while s / (p + s) >= 0.55:
        p += 1; s -= 1; need += 1

    for i, m in enumerate(specials[:need]):
        m['category'] = 'Physical'
        converted.append((el, m['id'], m['name']))
        if sig(m) in phys_sigs:
            # Same power/accuracy/ailment as a physical move that already
            # exists here - differentiate. A WeaponTag is the cleanest lever:
            # it is a real mechanical difference (いっせん/ツメのかりうど key
            # off it) rather than a cosmetic nudge, and those two traits have
            # had zero tagged moves to work with until now.
            m['weapon_tag'] = TAG_CYCLE[i % len(TAG_CYCLE)]
            # If the collision is with an equally-tagged move, nudge the
            # rider instead so the pair still differs.
            if any(sig(o) == sig(m) and o is not m
                   and o.get('weapon_tag') == m['weapon_tag']
                   for o in moves if o['type'] == el and o['category'] == 'Physical'):
                if m.get('ailment_effect', 'None') != 'None':
                    m['ailment_chance'] = min(100, m.get('ailment_chance', 10) + 10)
                else:
                    m['is_contact'] = not bool(m.get('is_contact', False))
            differentiated.append((el, m['id'], m['name'], m['weapon_tag']))
        phys_sigs.add(sig(m))

json.dump(moves, open(P, 'w'), ensure_ascii=False, indent=2)
open(P, 'a').write('\n')

print(f"多段技を追加: {len(added_multi)}件")
for el, shape, mid, name in added_multi:
    print(f"   {JA[el]}  形式{'①' if shape==1 else '②'}  {mid:14} {name}")
print(f"\n特殊→物理へ振替: {len(converted)}件")
for el in ORDER:
    ids = [c for c in converted if c[0] == el]
    if ids: print(f"   {JA[el]}: {len(ids)}件  " + ', '.join(f"{i[2]}({i[1]})" for i in ids))
print(f"\n差別化(WeaponTag付与): {len(differentiated)}件")
for el, mid, name, tag in differentiated:
    print(f"   {JA[el]} {mid:12} {name:10} -> {tag}")
