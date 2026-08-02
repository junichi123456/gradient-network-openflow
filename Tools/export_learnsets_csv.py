# -*- coding: utf-8 -*-
"""Regenerates docs/export/learnsets.csv from species.json + moves.json.

Written as a committed tool so the CSV can be rebuilt whenever move names or
learnsets change, instead of being a one-off. BOM-prefixed UTF-8 so Excel
opens it without mojibake.
"""
import json, csv, io

TYPE_JA = {"Neutral":"無","Fire":"炎","Water":"水","Grass":"草","Electric":"雷",
           "Ground":"地","Ice":"氷","Dragon":"竜","Dark":"闇"}
CAT_JA  = {"Physical":"物理","Special":"特殊","Status":"変化"}
MULTI_JA= {"Variable2To5":"2〜5発","RepeatPerHit":"3発(発ごと命中)"}

species = json.load(open('Data/species.json', encoding='utf-8'))
if isinstance(species, dict): species = species['species']
moves = {m['id']: m for m in json.load(open('Data/moves.json', encoding='utf-8'))}

def name_index(path, key=None):
    d = json.load(open(path, encoding='utf-8'))
    if isinstance(d, dict): d = d[key]
    return {x['id']: x.get('name', x['id']) for x in d}

# The CSV shows display names, not ids - resolve them the same way the game does.
trait_name = name_index('Data/traits.json', 'traits')
eco_name   = name_index('Data/ecology.json')

rows = []
for s in species:
    types = "/".join(TYPE_JA.get(t, t) for t in s.get('types', []))
    bst = s.get('base_hp',0) + s.get('base_atk',0) + s.get('base_def',0)
    for e in s.get('learnset', []):
        m = moves.get(e['move_id'])
        if m is None: raise SystemExit(f"未知の move_id: {e['move_id']} ({s['species_id']})")
        mh = m.get('multi_hit', 'None')
        rows.append([
            s['species_id'], s['display_name'], types, bst,
            trait_name.get(s.get('trait_id') or s.get('trait') or '', ''),
            eco_name.get(s.get('ecology_id') or s.get('ecology') or '', ''),
            e['level'], m['id'], m['name'], TYPE_JA.get(m['type'], m['type']),
            CAT_JA.get(m['category'], m['category']), m.get('power', 0),
            m.get('accuracy', 100), m.get('max_pp', 0),
            MULTI_JA.get(mh, '') if mh != 'None' else '',
        ])

buf = io.StringIO()
w = csv.writer(buf, lineterminator='\n')
w.writerow(["species_id","種族名","属性","種族値合計","特性","生態","Lv","move_id",
            "技名","技属性","分類","威力","命中","PP","多段"])
w.writerows(rows)
open('docs/export/learnsets.csv', 'w', encoding='utf-8-sig', newline='').write(buf.getvalue())
print(f"{len(rows)} 行を書き出しました（{len(species)} 種）")
