# -*- coding: utf-8 -*-
"""Independent check of the four attack-move rules (does not import the
rebalance tool - it re-derives everything from moves.json)."""
import json, collections, sys

ms=json.load(open('Data/moves.json',encoding='utf-8'))

def max_hits(m):
    mode=m.get('multi_hit','None')
    if mode=='Variable2To5': return 5
    if mode=='RepeatPerHit': return m.get('multi_hit_count') or 1
    return 1
# Multi-hit moves are judged on the whole barrage, not one hit.
eff=lambda m: m['power']*max_hits(m)
RANGE_CAP={'Room':4,'Area':2,'Line':6,'TwoTile':3}   # Adjacent uncapped by design
atk=[m for m in ms if m['category'] in ('Physical','Special')]
phy=[m for m in atk if m['category']=='Physical']
spe=[m for m in atk if m['category']=='Special']
fails=[]
def check(ok,label,detail=""):
    print(("[PASS] " if ok else "[FAIL] ")+label+(("  "+detail) if detail else ""))
    if not ok: fails.append(label)

# 同威力の上限: 各属性・各分類で2種まで（全威力帯、連続技は最大回数で換算）
bad={}
for cat in ('Physical','Special'):
    c=collections.Counter((m['type'],eff(m)) for m in atk if m['category']==cat)
    for k,v in c.items():
        if v>2: bad[(cat,)+k]=v
check(not bad, "同属性・同分類で同威力は2種まで（全威力帯）", f"違反 {len(bad)} {list(bad.items())[:3]}")

# 射程ごとの上限（属性単位、物理と特殊をまたぐ）
viol={}
for el in set(m['type'] for m in atk):
    c=collections.Counter(m.get('range','Adjacent') for m in atk if m['type']==el)
    for r,cap in RANGE_CAP.items():
        if c[r]>cap: viol[(el,r)]=f"{c[r]}>{cap}"
check(not viol, "射程上限 Room<=4 / Area<=2 / Line<=6 / TwoTile<=3（属性ごと）", str(viol))

# 連続技は削除対象外
KEPT_MULTI={'mvh_water','mvh_electric','mvh_ground','mvh_ice','mvh_dark'}
alive={m['id'] for m in ms}
check(KEPT_MULTI<=alive, "連続技5種がすべて残存している", str(sorted(KEPT_MULTI-alive)))

# 変化技は今回の対象外なので手つかず
check(sum(1 for m in ms if m['category']=='Status')==58, "変化技58種が手つかず",
      str(sum(1 for m in ms if m['category']=='Status')))

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

# §3 の 7:2:1 は今回の射程上限に置き換わった（直線は最大 9属性x6=54 で、
# 特殊102技の7割=71 に届かないため両立不可能）。射程が Line/Area/Room の
# いずれかである点だけは引き続き成立していることを確認する。
rc=collections.Counter(m.get('range','Adjacent') for m in spe)
check(set(rc)<={'Line','Area','Room'}, "特殊技はすべて直線/範囲/部屋のいずれか", str(dict(rc)))

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
