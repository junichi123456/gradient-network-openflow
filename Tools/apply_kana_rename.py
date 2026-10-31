# -*- coding: utf-8 -*-
"""Bulk edit for Data/moves.json.

1. WeaponTag assignment, keyed on the ORIGINAL (kanji) names - it has to run
   before the rename or the 拳/爪/斬 markers it keys on are gone.
2. Every move name becomes kana: hiragana for Japanese-origin words,
   katakana kept for loanwords (メガトン自爆 -> メガトンじばく).
"""
import json, re, sys, collections
sys.path.insert(0, 'Tools')
from kana_rename_map import READINGS

PATH = 'Data/moves.json'
KANJI = re.compile(r'[一-鿿々〆ヶ]')
KANA_OK = re.compile(r'^[ぁ-ゟァ-ヿー・]+$')

moves = json.load(open(PATH, encoding='utf-8'))
before = {m['id']: m['name'] for m in moves}
before_tag = {m['id']: m.get('weapon_tag', 'None') for m in moves}

# ---------- 1. weapon tags (on original names) ----------
CLAW = re.compile(r'拳|爪|パンチ')
SLASH = re.compile(r'ブレード|斬')
tagged = {'ClawFist': [], 'Slash': []}
overwritten = []
for m in moves:
    name = m['name']
    # 竜爪連斬 matches both; the claw is the weapon, the slash only the motion.
    new = 'ClawFist' if CLAW.search(name) else ('Slash' if SLASH.search(name) else None)
    if new is None:
        continue
    old = m.get('weapon_tag', 'None')
    if old not in ('None', new):
        overwritten.append((m['id'], name, old, new))
    m['weapon_tag'] = new
    tagged[new].append((m['id'], name))

# ---------- 2. names -> kana ----------
missing, badkana = [], []
for m in moves:
    r = READINGS.get(m['id'])
    if r is None:
        if KANJI.search(m['name']):
            missing.append((m['id'], m['name']))
        continue
    if not KANA_OK.match(r):
        badkana.append((m['id'], r))
    m['name'] = r

leftover = [(m['id'], m['name']) for m in moves if KANJI.search(m['name'])]
notkana = [(m['id'], m['name']) for m in moves if not KANA_OK.match(m['name'])]

print(f"ClawFist 付与 {len(tagged['ClawFist'])} 件 / Slash 付与 {len(tagged['Slash'])} 件")
print(f"既存タグの上書き: {len(overwritten)} 件 {overwritten}")
print(f"読みマップ未登録の漢字名: {len(missing)} {missing}")
print(f"かな以外を含む読み: {len(badkana)} {badkana}")
print(f"変換後に残った漢字: {len(leftover)} {leftover}")
print(f"変換後にかな以外を含む技名: {len(notkana)} {notkana}")

# duplicate names: report only the ones this rename newly created
def dups(mapping):
    c = collections.defaultdict(list)
    for i, n in mapping.items(): c[n].append(i)
    return {n: ids for n, ids in c.items() if len(ids) > 1}
after = {m['id']: m['name'] for m in moves}
pre, post = dups(before), dups(after)
pre_groups = {frozenset(v) for v in pre.values()}
new_dups = {n: ids for n, ids in post.items() if frozenset(ids) not in pre_groups}
print(f"\n変換前からの同名: {len(pre)} 件 {pre}")
print(f"変換で新たに生じた同名: {len(new_dups)} 件 {new_dups}")

fail = missing or badkana or leftover or notkana or new_dups or overwritten
if fail:
    print("\n*** FAIL - 書き込みを中止 ***"); sys.exit(1)

json.dump(moves, open(PATH, 'w', encoding='utf-8'), ensure_ascii=False, indent=2)
open(PATH, 'a', encoding='utf-8').write('\n')
print(f"\n書き込み完了: {len(moves)} 技")
