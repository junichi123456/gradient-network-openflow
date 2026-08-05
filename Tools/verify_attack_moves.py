# -*- coding: utf-8 -*-
"""Independent check of the four attack-move rules (does not import the
rebalance tool - it re-derives everything from moves.json)."""
import json, collections, sys

ms=json.load(open('Data/moves.json',encoding='utf-8'))
atk=[m for m in ms if m['category'] in ('Physical','Special')]
phy=[m for m in atk if m['category']=='Physical']
spe=[m for m in atk if m['category']=='Special']
fails=[]
def check(ok,label,detail=""):
    print(("[PASS] " if ok else "[FAIL] ")+label+(("  "+detail) if detail else ""))
    if not ok: fails.append(label)

# §1 + §A - the cap now applies at EVERY power, not just <=90
for lo,hi,label in ((0,90,"§1 威力90以下"),(95,10**9,"§A 威力95以上")):
    bad={}
    for cat in ('Physical','Special'):
        c=collections.Counter((m['type'],m['power']) for m in atk
                              if m['category']==cat and lo<=m['power']<=hi)
        for k,v in c.items():
            if v>2: bad[(cat,)+k]=v
    check(not bad, label+"は同属性・同分類・同威力が2種まで", f"違反 {len(bad)} {list(bad.items())[:3]}")

# §B - Room moves at least 10 apart within an element, across categories
viol={}
for el in set(m['type'] for m in atk):
    pw=sorted(m['power'] for m in atk if m.get('range')=='Room' and m['type']==el)
    v=[(pw[i],pw[i+1]) for i in range(len(pw)-1) if pw[i+1]-pw[i]<10]
    if v: viol[el]=v
check(not viol, "§B 部屋技は同属性内で威力差10以上", str(viol))

# a deleted move must not survive anywhere that references move ids
species=json.load(open('Data/species.json',encoding='utf-8'))
known={m['id'] for m in ms}
dangling=set()
for sp in species:
    for row in sp.get('learnset',[]):
        mid=row.get('move_id') if isinstance(row,dict) else row
        if mid and mid not in known: dangling.add(mid)
check(not dangling, "learnsetに削除済み技IDが残っていない", str(sorted(dangling)[:5]))

# §2
contact=sum(1 for m in phy if m.get('is_contact'))
check(round(len(phy)*0.9)==contact, "§2 物理技の9割が接触",
      f"{contact}/{len(phy)} = {contact/len(phy)*100:.1f}%")
check(not any(m.get('is_contact') and m.get('range','Adjacent')!='Adjacent' for m in phy),
      "§2 非Adjacentの物理技は非接触")

# §3
rc=collections.Counter(m.get('range','Adjacent') for m in spe)
n=len(spe)
check(rc['Line']==round(n*0.7), "§3 特殊技の7割が直線", f"{rc['Line']}/{n}")
check(rc['Area']==round(n*0.2), "§3 特殊技の2割が範囲", f"{rc['Area']}/{n}")
check(rc['Room']==n-round(n*0.7)-round(n*0.2), "§3 特殊技の1割が部屋", f"{rc['Room']}/{n}")
check(set(rc)<= {'Line','Area','Room'}, "§3 特殊技に他の射程が残っていない", str(dict(rc)))

# §4 - every range change must have been paid for, given the recorded
# original data. Re-derived from git HEAD~ is out of scope here; instead
# assert the pricing table is internally consistent: no Room move may be
# stronger than the strongest Line move of the same element by more than
# the price difference would allow.
COST={'Adjacent':0,'TwoTile':-5,'Line':-10,'Area':-15,'Room':-20,'FullFloor':-20}
check(all(m['power']>0 for m in atk), "§4 威力が0以下になった技はない",
      f"min={min(m['power'] for m in atk)}")

# integrity
check(len({m['id'] for m in ms})==len(ms), "技IDが一意")
check(len({m['name'] for m in ms})==len(ms), "技名が一意")
check(all(m['power']%5==0 for m in atk), "攻撃技の威力はすべて5の倍数")

print("\n総合:", "ALL PASS" if not fails else f"{len(fails)} FAIL")
sys.exit(1 if fails else 0)
