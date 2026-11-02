#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""特殊の範囲技の整理を Data/ に適用する（削除24・learnset 8行除去・NPC 5枠置換・威力の引き下げ）。冪等。

usage: python3 Tools/apply_aoe_power_cut.py --cut 20 [--acc]   （採用案は --cut 20 --acc）
  --acc は基準威力80以上の22件で命中-5。
  入力は前提コミットの Data、または既に一度適用した Data。何度実行しても同じ結果になる。
  元に戻すときは git checkout -- Data/ Tools/aoe_power_rules.py
仕様書: docs/design/drafts/2026-10-27-aoe-power.md §3-2
"""
import argparse, json, os, re, sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import aoe_power_rules as R

DATA = os.path.join(HERE, '..', 'Data')
RULES = os.path.join(HERE, 'aoe_power_rules.py')


def load(name):
    return json.load(open(os.path.join(DATA, name), encoding='utf-8'))


def save(name, obj):
    open(os.path.join(DATA, name), 'w', encoding='utf-8').write(
        json.dumps(obj, indent=2, ensure_ascii=False) + '\n')


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--cut', type=int, required=True, choices=(20,))
    ap.add_argument('--acc', action='store_true')
    a = ap.parse_args()
    if a.acc and a.cut != 20:
        sys.exit('--acc は --cut 20 のときだけ')
    ms, sp, npc = load('moves.json'), load('species.json'), load('npc_teams.json')
    by = {m['id']: m for m in ms}
    missing = [i for i in R.AOE_BASE if i not in by]
    if missing:
        sys.exit(f'moves.json に無い残す技: {missing}')
    print(f'--cut {a.cut}{" --acc" if a.acc else ""}')

    # 1. moves.json から24件を除く
    deleted = set(R.AOE_DELETED)
    gone = [m for m in ms if m['id'] in deleted]
    ms = [m for m in ms if m['id'] not in deleted]
    print(f'削除した技: {len(gone)}件（moves.json {len(ms) + len(gone)} -> {len(ms)}）')
    for m in gone:
        print(f'  - {m["id"]:<20} {m["name"]}')

    # 2. learnset から削除技の行と ROW_REMOVALS の8行を除く
    rowrm = set(R.ROW_REMOVALS)
    n_del = n_row = 0
    species_hit = set()
    for s in sp:
        keep = []
        for e in s['learnset']:
            if e['move_id'] in deleted:
                n_del += 1; species_hit.add(s['species_id']); continue
            if (s['species_id'], e['move_id']) in rowrm:
                n_row += 1; species_hit.add(s['species_id']); continue
            keep.append(e)
        s['learnset'] = keep
    print(f'learnset から除いた行: 削除技 {n_del}行 + ROW_REMOVALS {n_row}行（影響 {len(species_hit)}種）')

    # 3. NPC 構築の置換
    teams = {t['id']: t for t in npc}
    for tid, sid, old, new in R.NPC_REPLACEMENTS:
        ent = [e for e in teams[tid]['entries'] if e['species_id'] == sid]
        if len(ent) != 1:
            sys.exit(f'NPC 枠が見つからない: {tid} {sid}')
        mids = ent[0]['move_ids']
        if old in mids:
            mids[mids.index(old)] = new; st = '置換'
        elif new in mids:
            st = '済み'
        else:
            sys.exit(f'NPC 枠が想定外の状態: {tid} {sid} {mids}')
        print(f'  NPC {tid} {sid}: {old} -> {new} ({st})')

    # 4. 残す29件の威力・命中
    red = R.CUT_TABLE[a.cut]
    odd = []
    for n, (i, (bp, ba)) in enumerate(R.AOE_BASE.items(), 1):
        m = by[i]
        m['power'] = bp - red[i]
        m['accuracy'] = ba - R.acc_cut(i, a.acc)
        nom = 10 if bp <= 35 else a.cut
        mark = ''
        if red[i] != nom:
            mark = ' ※'; odd.append(i)
        print(f'{n:>2} {i:<20} {m["name"]:<14} {bp:>3} -> {m["power"]:>3} (-{red[i]}) 命中 {ba} -> {m["accuracy"]}{mark}')
    if odd:
        print('公称からずれた技: ' + ', '.join(f'{i}(-{red[i]})' for i in odd))

    save('moves.json', ms); save('species.json', sp); save('npc_teams.json', npc)
    src = open(RULES, encoding='utf-8').read()
    new, n = re.subn(r'^ADOPTED = \(.*\)[ \t]*$', f'ADOPTED = ({a.cut}, {a.acc})', src, flags=re.M)
    if n != 1:
        sys.exit('ADOPTED の行が見つからない')
    open(RULES, 'w', encoding='utf-8').write(new)
    # 同じ秒・同じ長さで書き換えると古い .pyc が読まれて検証が採用案を取り違えるので、キャッシュを消す。
    import glob
    for f in glob.glob(os.path.join(HERE, '__pycache__', 'aoe_power_rules*.pyc')):
        os.remove(f)


main()
