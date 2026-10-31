# -*- coding: utf-8 -*-
"""Assigns the 11 weather traits to species of the matching element.

Selection rule (deliberately mechanical and re-runnable):
  - candidates are species whose types include the trait's element AND whose
    current trait is a TEMPLATE trait (〇〇のきずな/ちから/…). Species holding
    a hand-authored UNIQUE trait are never touched, so stage 9's curated
    assignments survive intact.
  - candidates are taken in species_id order and dealt round-robin across
    the traits of that element, so two traits sharing an element (e.g.
    フェーン and かげろうボディ) get disjoint, interleaved sets.
  - PER_TRAIT species each, matching the catalogue's own "a unique trait is
    shared by a small 1-5 species group" convention.
"""
import json, collections

PER_TRAIT = 4

ELEMENT_TRAITS = {
    "Fire":   ["foehn", "kagerou_body"],
    "Water":  ["amagumo"],
    "Ice":    ["tamayuki", "yukisuberi"],
    "Ground": ["haboob", "sandbag"],
    "Grass":  ["maikaze", "fuwafuwa"],
    "Dark":   ["haze", "purple_haze"],
}

species = json.load(open('Data/species.json', encoding='utf-8'))
if isinstance(species, dict): species = species['species']
traits = {t['id']: t for t in json.load(open('Data/traits.json', encoding='utf-8'))}

for tid in (t for ts in ELEMENT_TRAITS.values() for t in ts):
    if tid not in traits: raise SystemExit(f"未定義の特性: {tid}")

taken = set()
assigned = collections.defaultdict(list)

for element, tids in ELEMENT_TRAITS.items():
    cands = [s for s in sorted(species, key=lambda x: x['species_id'])
             if element in s.get('types', [])
             and s['species_id'] not in taken
             and traits.get(s.get('trait', ''), {}).get('category') == 'template']
    need = PER_TRAIT * len(tids)
    if len(cands) < need:
        raise SystemExit(f"{element}: 候補 {len(cands)} < 必要 {need}")
    for i, s in enumerate(cands[:need]):
        tid = tids[i % len(tids)]          # round-robin, so the sets interleave
        assigned[tid].append((s['species_id'], s['display_name'], s['trait']))
        s['trait'] = tid
        taken.add(s['species_id'])

for tid in (t for ts in ELEMENT_TRAITS.values() for t in ts):
    rows = assigned[tid]
    print(f"{traits[tid]['name']} ({tid}): {len(rows)}種")
    for sid, name, old in rows:
        print(f"    {sid} {name}  ({old} -> {tid})")

total = sum(len(v) for v in assigned.values())
print(f"\n付与合計 {total} 種 / 全 {len(species)} 種")
uniq = sum(1 for s in species if traits[s['trait']]['category'] == 'unique')
print(f"unique特性保有 {uniq} 種 / template {len(species) - uniq} 種")

json.dump(species, open('Data/species.json', 'w', encoding='utf-8'), ensure_ascii=False, indent=2)
open('Data/species.json', 'a', encoding='utf-8').write('\n')
