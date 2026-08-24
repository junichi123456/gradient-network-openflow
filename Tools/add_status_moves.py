# -*- coding: utf-8 -*-
"""Adds the status-move expansion to Data/moves.json (598 -> 628).

  A. 9 moves - one per element - raising the user's own ElementPower rank
     by +2 (that rank tops out at +2, so one use reaches the ceiling).
  B. 18 moves - two per element - raising 2 of
     {Atk, Def, Accuracy, Evasion, Crit} by +1 each. The 10 possible pairs
     are dealt out cyclically so every pair is used at least once.
  C. 2 moves - Water and Neutral - raising 2 ranks by +2 and dropping Def
     by 2. The raised pair never includes Def, which the move itself lowers.
  D. 1 Dark move inflicting 猛毒. ailment_chance 100 becomes +1000 in the
     accumulation system, i.e. it fires on the spot.

Moves with more than one rank change use the "rank_effects" array; the
single-effect ones keep the legacy rank_effect_* fields so the existing
tooling round-trips them unchanged.
"""
import json, collections

PATH = 'Data/moves.json'
ELEMENTS = ['Neutral', 'Fire', 'Water', 'Grass', 'Electric', 'Ground', 'Ice', 'Dragon', 'Dark']

# Element name fragments used to build move names.
EL_WORD = {
    'Neutral': 'むそう', 'Fire': 'ほむら', 'Water': 'みなも', 'Grass': 'しんりょく',
    'Electric': 'らいこう', 'Ground': 'だいち', 'Ice': 'ひょうが', 'Dragon': 'りゅうき',
    'Dark': 'やみよ',
}

# A: element-power move names.
EL_POWER_NAME = {
    'Neutral': 'むそうのきわみ', 'Fire': 'ごうかのきわみ', 'Water': 'しんすいのきわみ',
    'Grass': 'ばんりょくのきわみ', 'Electric': 'らいめいのきわみ', 'Ground': 'こんりんのきわみ',
    'Ice': 'ひょうけつのきわみ', 'Dragon': 'りゅうしんのきわみ', 'Dark': 'しんえんのきわみ',
}

# B: the 10 pairs, each with the name fragment that follows the element word.
PAIRS = [
    (('Atk', 'Def'),        'のかまえ'),
    (('Atk', 'Accuracy'),   'のねらい'),
    (('Atk', 'Evasion'),    'のさばき'),
    (('Atk', 'Crit'),       'のきば'),
    (('Def', 'Accuracy'),   'のそなえ'),
    (('Def', 'Evasion'),    'のころも'),
    (('Def', 'Crit'),       'のよろい'),
    (('Accuracy', 'Evasion'), 'のまなこ'),
    (('Accuracy', 'Crit'),  'のみとおし'),
    (('Evasion', 'Crit'),   'のかげろう'),
]

def status_move(mid, name, element, pp=15):
    return {"id": mid, "name": name, "type": element, "category": "Status",
            "power": 0, "accuracy": 100, "max_pp": pp, "range": "Adjacent"}

new = []

# ---- A: own-element power +2 ----
for el in ELEMENTS:
    m = status_move(f"mvs_ep_{el.lower()}", EL_POWER_NAME[el], el, pp=10)
    m["rank_effect_stat"] = "ElementPower"
    m["rank_effect_delta"] = 2
    m["rank_effect_target"] = "Self"
    new.append(m)

# ---- B: two ranks +1 ----
pair_i = 0
for el in ELEMENTS:
    for _ in range(2):
        (a, b), suffix = PAIRS[pair_i % len(PAIRS)]
        pair_i += 1
        m = status_move(f"mvs_{el.lower()}_{a.lower()}_{b.lower()}", EL_WORD[el] + suffix, el)
        m["rank_effects"] = [
            {"stat": a, "delta": 1, "target": "Self", "chance": 1.0},
            {"stat": b, "delta": 1, "target": "Self", "chance": 1.0},
        ]
        new.append(m)

# ---- C: two ranks +2, Def -2 ----
BIG = [("Neutral", "すてみのかまえ", "Atk", "Crit"),
       ("Water",   "きゅうりゅうのかまえ", "Atk", "Evasion")]
for el, name, a, b in BIG:
    m = status_move(f"mvs_big_{el.lower()}", name, el, pp=10)
    m["rank_effects"] = [
        {"stat": a, "delta": 2, "target": "Self", "chance": 1.0},
        {"stat": b, "delta": 2, "target": "Self", "chance": 1.0},
        {"stat": "Def", "delta": -2, "target": "Self", "chance": 1.0},
    ]
    new.append(m)

# ---- D: Dark, inflicts 猛毒 ----
m = status_move("mvs_toxic_dark", "もうどくのしずく", "Dark", pp=10)
m["ailment_effect"] = "Toxic"
m["ailment_chance"] = 100
m["ailment_target"] = "Enemy"
new.append(m)

moves = json.load(open(PATH, encoding='utf-8'))
have_ids = {x['id'] for x in moves}
have_names = {x['name'] for x in moves}
dup_id = [x['id'] for x in new if x['id'] in have_ids]
dup_name = [x['name'] for x in new if x['name'] in have_names]
inner = [n for n, c in collections.Counter(x['name'] for x in new).items() if c > 1]
if dup_id or dup_name or inner:
    raise SystemExit(f"衝突 id={dup_id} name={dup_name} 新規内重複={inner}")

moves.extend(new)
json.dump(moves, open(PATH, 'w', encoding='utf-8'), ensure_ascii=False, indent=2)
open(PATH, 'a', encoding='utf-8').write('\n')

print(f"追加 {len(new)} 技 → 総数 {len(moves)}")
print("  内訳:", dict(collections.Counter(
    'A:属性ランク' if m['id'].startswith('mvs_ep_') else
    'C:大型' if m['id'].startswith('mvs_big_') else
    'D:猛毒' if m['id'].startswith('mvs_toxic') else 'B:2ランク' for m in new)))
print("  変化技合計:", sum(1 for m in moves if m['category'] == 'Status'))
used = collections.Counter()
for m in new:
    for e in m.get('rank_effects', []): used[e['stat']] += 1
print("  rank_effects 内訳:", dict(used))
