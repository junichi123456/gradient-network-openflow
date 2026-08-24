#!/usr/bin/env python3
"""learnset インスペクタ（Artifact: 188eb9b1-d49d-4c39-bf77-895a30185e99）
のペイロードを Data/ から作り直す。

種族・技・learnsetのいずれかを変更したら、このスクリプトを実行してから
Tools/artifacts/learnset_viewer.html を同じURLへ再公開する。

  python3 Tools/build_ls_payload.py
"""
import json
import os
import sys
import collections

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, 'Tools', 'artifacts')
sys.path.insert(0, os.path.join(ROOT, 'Tools'))
from learnset_rules import TRAIT_TAG, TRAIT_NAMED, profile_of

sp = json.load(open(f'{ROOT}/Data/species.json', encoding='utf-8'))
mv = json.load(open(f'{ROOT}/Data/moves.json', encoding='utf-8'))
tr = {t['id']: t for t in json.load(open(f'{ROOT}/Data/traits.json', encoding='utf-8'))}
M = {m['id']: m for m in mv}
ATT = [m for m in mv if m['power'] > 0]

EL = {'Neutral': '無', 'Fire': '炎', 'Water': '水', 'Grass': '草', 'Electric': '電',
      'Ground': '地', 'Ice': '氷', 'Dragon': '竜', 'Dark': '闇'}
CAT = {'Physical': 'P', 'Special': 'S', 'Status': 'T'}
RG = {'Adjacent': '単体', 'Line': '直線', 'TwoTile': '2マス', 'Area': '範囲',
      'Room': '部屋', 'FullFloor': '全体', 'Surrounding': '周囲'}


def adapted(x):
    t = x['trait']
    if t in TRAIT_NAMED:
        return set(TRAIT_NAMED[t])
    if t in TRAIT_TAG:
        return {m['id'] for m in ATT if m.get('weapon_tag') in TRAIT_TAG[t]}
    d = tr.get(t, {})
    if d.get('element') and d.get('template_kind') in ('power', 'stab', 'oshie'):
        return {m['id'] for m in ATT if m['type'] == d['element']}
    return {m['id'] for m in ATT if m['type'] in x['types']}


order = sorted(sp, key=lambda x: x['species_id'])
idx = {s['species_id']: i for i, s in enumerate(order)}

share = collections.Counter()
for s in sp:
    for e in s['learnset']:
        share[e['move_id']] += 1
N = len(sp)

out = []
for s in order:
    ms = [M[e['move_id']] for e in s['learnset']]
    ad = adapted(s)
    atk = [m for m in ms if m['power'] > 0]
    pf, why = profile_of(s['trait'], idx[s['species_id']])
    t = tr.get(s['trait'], {})
    out.append({
        'id': s['species_id'], 'n': s['display_name'],
        'ty': [EL[x] for x in s['types']],
        'hp': s['base_hp'], 'a': s['base_atk'], 'd': s['base_def'],
        'bst': s['base_hp'] + s['base_atk'] + s['base_def'],
        'pf': pf, 'pb': '特性' if why == 'trait' else '型',
        'tr': t.get('name', s['trait']), 'td': t.get('description', ''),
        'p': sum(1 for m in atk if m['category'] == 'Physical'),
        's': sum(1 for m in atk if m['category'] == 'Special'),
        'st': sum(1 for m in ms if m['power'] == 0),
        'ad': sum(1 for e in s['learnset'] if e['move_id'] in ad),
        'mx': max((m['power'] for m in atk), default=0),
        'nt': len({m['type'] for m in atk}),
        'mv': [[e['level'], M[e['move_id']]['name'], EL[M[e['move_id']]['type']],
                CAT[M[e['move_id']]['category']], M[e['move_id']]['power'],
                RG.get(M[e['move_id']].get('range', 'Adjacent'), '単体'),
                '' if M[e['move_id']].get('weapon_tag') in (None, 'None') else M[e['move_id']]['weapon_tag'],
                round(share[e['move_id']] / N * 100),
                1 if e['move_id'] in ad else 0]
               for e in s['learnset']],
    })

# 威力帯ごとの到達性: 分母は moves.json 全体、分子は誰かが覚えられる技。
learnable = {e['move_id'] for s in sp for e in s['learnset']}
BANDS = [15, 30, 45, 60, 75, 90, 105, 120, 135, 180]


def band(p):
    for lo in reversed(BANDS):
        if p >= lo:
            return lo
    return None


tot = collections.Counter()
rch = collections.Counter()
for m in ATT:
    b = band(m['power'])
    if b is None:
        continue
    tot[b] += 1
    if m['id'] in learnable:
        rch[b] += 1

payload = json.dumps(out, ensure_ascii=False, separators=(',', ':'))
head = open(f'{OUT}/ls_head.html', encoding='utf-8').read()
tail = open(f'{OUT}/ls_tail.html', encoding='utf-8').read()
tail = tail.replace(
    "const TOTALS = " + tail.split("const TOTALS = ")[1].split(";\n")[0],
    "const TOTALS = " + json.dumps({str(b): tot[b] for b in BANDS}))
tail = tail.replace(
    "const REACH  = " + tail.split("const REACH  = ")[1].split(";\n")[0],
    "const REACH  = " + json.dumps({str(b): rch[b] for b in BANDS}))
open(f'{OUT}/ls_tail.html', 'w', encoding='utf-8').write(tail)
page = head + payload + tail
open(f'{OUT}/learnset_viewer.html', 'w', encoding='utf-8').write(page)
print(f"種族 {len(out)} / 行 {sum(len(x['mv']) for x in out)} / ページ {len(page):,} 文字")
print("到達性:", {b: f"{rch[b]}/{tot[b]}" for b in BANDS})
