# -*- coding: utf-8 -*-
"""Caps the attack-move list per element and deletes whatever overflows.

  - Physical: at most 2 moves at the same power within an element
  - Special:  at most 2 moves at the same power within an element
  - per element: Room <= 4, Area <= 2, Line <= 6, TwoTile <= 3
    (Adjacent is deliberately uncapped - no limit was given for it)

Status moves are out of scope and are not touched.

Multi-hit moves are exempt from deletion, but they still occupy a slot and
still count toward the caps, and their power is read as power x MAX HITS
(Variable2To5 = 5, RepeatPerHit = multi_hit_count) rather than per-hit.

Which move survives a cull, when the cap forces a choice:
  1. multi-hit moves are pinned first - they cannot be deleted
  2. exact duplicates (same effect profile as an earlier-id sibling) go first
  3. the rest are chosen to keep the power SPREAD - the survivors are picked
     at even intervals across the power-sorted group so an element keeps a
     weak/mid/strong option instead of six moves bunched at one end
  4. at equal power the mechanically richer move is the one kept
"""
import json, collections, sys

PATH='Data/moves.json'
ELEMENTS=['Neutral','Fire','Water','Grass','Electric','Ground','Ice','Dragon','Dark']
RANGE_CAP={'Room':4,'Area':2,'Line':6,'TwoTile':3}      # Adjacent: uncapped
POWER_CAP=2

MECH=['rank_effect_stat','ailment_effect','field_effect','field_placement','weather_effect',
      'multi_hit','drain_hp_percent','recoil_hp_percent','weapon_tag','is_guaranteed_hit',
      'crit_rank_bonus','self_guaranteed_death','dragon_multiplier','rank_effects',
      'self_stun_next_turn']
BLANK=(None,'None',0,False,1.0,[])

moves=json.load(open(PATH,encoding='utf-8'))
rng=lambda m: m.get('range','Adjacent')
mech=lambda m: sum(1 for k in MECH if m.get(k) not in BLANK)
is_multi=lambda m: m.get('multi_hit','None') not in (None,'None')

def max_hits(m):
    mode=m.get('multi_hit','None')
    if mode=='Variable2To5': return 5
    if mode=='RepeatPerHit': return m.get('multi_hit_count') or 1
    return 1

def eff_power(m):
    """Power as the caps see it: a multi-hit move is judged on its full
    barrage, not on one hit."""
    return m['power']*max_hits(m)

IDENT=lambda m: tuple(sorted((k,repr(v)) for k,v in m.items()
                             if k not in ('id','name','max_pp','accuracy')))

def cull_order(group):
    """`group` ordered worst-to-best to keep. Duplicates of an earlier-id
    sibling first, then the mechanically thinnest, then later ids."""
    seen=set(); dup={}
    for m in sorted(group, key=lambda x:x['id']):
        k=IDENT(m); dup[m['id']]=k in seen; seen.add(k)
    return sorted(group, key=lambda m:(0 if dup[m['id']] else 1, mech(m), m['id']))

def keep_spread(group, k):
    """Choose k of `group` keeping the power spread wide. Multi-hit moves are
    pinned; the remaining slots are taken at even intervals over the
    power-sorted list, which keeps both ends and samples the middle."""
    pinned=[m for m in group if is_multi(m)]
    rest=sorted([m for m in group if not is_multi(m)],
                key=lambda m:(eff_power(m), -mech(m), m['id']))
    slots=k-len(pinned)
    if slots<=0: return pinned
    if slots>=len(rest): return pinned+rest
    n=len(rest)
    picked=[]
    used=set()
    for i in range(slots):
        target=round(i*(n-1)/(slots-1)) if slots>1 else 0
        j=target
        step=0
        while j in used:                      # nearest free index
            step+=1
            j=target+step if target+step<n and target+step not in used else target-step
            if j<0 or j>=n: j=target+step
        used.add(j); picked.append(rest[j])
    return pinned+picked

deleted=[]

# --------------------------------------------------- range caps (per element)
for el in ELEMENTS:
    for r,cap in RANGE_CAP.items():
        group=[m for m in moves if m['category'] in ('Physical','Special')
               and m['type']==el and rng(m)==r]
        if len(group)<=cap: continue
        keep={m['id'] for m in keep_spread(group, cap)}
        for m in group:
            if m['id'] not in keep:
                deleted.append((f"{r}上限{cap}", m, f"{el}の{r}が{len(group)}件"))

gone={m['id'] for _,m,_ in deleted}
moves=[m for m in moves if m['id'] not in gone]

# --------------------------------------------------- power cap (per element)
for cat in ('Physical','Special'):
    for el in ELEMENTS:
        buckets=collections.defaultdict(list)
        for m in moves:
            if m['category']==cat and m['type']==el: buckets[eff_power(m)].append(m)
        for p,g in sorted(buckets.items()):
            if len(g)<=POWER_CAP: continue
            protected=[m for m in g if is_multi(m)]
            droppable=[m for m in cull_order(g) if not is_multi(m)]
            for m in droppable[:len(g)-POWER_CAP]:
                deleted.append((f"同威力上限{POWER_CAP}", m, f"{el}/{cat}/威力{p} が{len(g)}件"))

gone2={m['id'] for _,m,_ in deleted}
moves=[m for m in moves if m['id'] not in gone2]

print(f"削除: {len(deleted)}技")
by=collections.Counter(t for t,_,_ in deleted)
for k,v in sorted(by.items()): print(f"  {k}: {v}")
print(f"  うち特殊 {sum(1 for _,m,_ in deleted if m['category']=='Special')} / "
      f"物理 {sum(1 for _,m,_ in deleted if m['category']=='Physical')}")
print(f"  連続技の削除: {sum(1 for _,m,_ in deleted if is_multi(m))} (0であるべき)")

phy=[m for m in moves if m['category']=='Physical']
spe=[m for m in moves if m['category']=='Special']
print(f"\n残存: 物理{len(phy)} 特殊{len(spe)} 変化{sum(1 for m in moves if m['category']=='Status')} 合計{len(moves)}")

# --------------------------------------------------- restore 90% contact
want_plain=len(phy)-round(len(phy)*0.9)
plain=[m for m in phy if rng(m)!='Adjacent']
for m in sorted(phy,key=lambda m:m['id']):
    if len(plain)>=want_plain: break
    if m not in plain and not m.get('is_contact'): plain.append(m)
for m in sorted(phy,key=lambda m:m['id']):
    if len(plain)>=want_plain: break
    if m not in plain: plain.append(m)
plain_ids={m['id'] for m in plain[:want_plain]}
for m in phy: m['is_contact'] = m['id'] not in plain_ids
print(f"接触: {sum(1 for m in phy if m['is_contact'])}/{len(phy)}")

json.dump(moves, open(PATH,'w',encoding='utf-8'), ensure_ascii=False, indent=2)
open(PATH,'a',encoding='utf-8').write('\n')

print("\n属性ごとの射程内訳:")
for el in ELEMENTS:
    c=collections.Counter(rng(m) for m in moves
                          if m['category'] in ('Physical','Special') and m['type']==el)
    print(f"  {el:<9} " + "  ".join(f"{r}{c[r]}" for r in ('Adjacent','TwoTile','Line','Area','Room')))
