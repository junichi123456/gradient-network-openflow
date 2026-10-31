# -*- coding: utf-8 -*-
"""Prunes attack moves down to the two new rules, then repairs the balance
targets the deletions disturb.

  §A  power >= 95 now obeys the same cap the <=90 band already did: at most
      2 moves per (element, category, power). Excess moves are DELETED.

  §B  Room-range moves must sit at least 10 power apart from every other
      Room move OF THE SAME ELEMENT - across categories, since the rule
      says 同属性 without naming a category. Excess moves are DELETED.
      (No Status move has Room range, so this only ever touches attacks and
      the "power spacing" reading is unambiguous.)

Deleting moves knocks the earlier targets off, so they are re-established
afterwards on the survivors:
  - the 7:2:1 Line/Area/Room split for Special moves
  - 90% contact for Physical moves
  - at most 2 moves per (element, category, power) at EVERY power, which is
    §A and the old <=90 rule stated as one rule

Room powers are pinned once §B has settled, so the de-duplication pass can
never shuffle a Room move back into a spacing violation.
"""
import json, collections

PATH='Data/moves.json'
ELEMENTS=['Neutral','Fire','Water','Grass','Electric','Ground','Ice','Dragon','Dark']
RANGE_COST={'Adjacent':0,'TwoTile':-5,'Line':-10,'Area':-15,'Room':-20,'FullFloor':-20}
TIERS=('Line','Area','Room')
GAP=10
GRID_MIN, GRID_MAX, STEP = 15, 90, 5

# Fields that make a move more than a damage number; used to decide which of
# two otherwise-interchangeable moves is the one worth keeping.
MECH=['rank_effect_stat','ailment_effect','field_effect','field_placement','weather_effect',
      'multi_hit','drain_hp_percent','recoil_hp_percent','weapon_tag','is_guaranteed_hit',
      'crit_rank_bonus','self_guaranteed_death','dragon_multiplier','rank_effects',
      'self_stun_next_turn']
BLANK=(None,'None',0,False,1.0,[])

moves=json.load(open(PATH,encoding='utf-8'))
rng=lambda m: m.get('range','Adjacent')
mech=lambda m: sum(1 for k in MECH if m.get(k) not in BLANK)

# Everything about a move except its identity and PP/accuracy. Two moves with
# the same profile are interchangeable in play, so when a bucket is over the
# cap the redundant twin is the one to drop - NOT the move with the fewest
# mechanics. Dragon/Physical/95 is exactly this case: りゅうそくけん and
# りゅうそうげき are identical, while げきりんつき is the only Def-drop of
# the three, so counting mechanics alone would have deleted the distinctive
# move and kept both copies of the twin.
IDENT=lambda m: tuple(sorted((k,repr(v)) for k,v in m.items()
                             if k not in ('id','name','max_pp','accuracy')))

def rank_for_deletion(group):
    """`group` ordered worst-to-best to keep: exact duplicates of an
    earlier-id sibling first, then the mechanically thinnest, then later ids."""
    seen={}
    dup={}
    for m in sorted(group, key=lambda x:x['id']):
        k=IDENT(m)
        dup[m['id']] = k in seen
        seen[k]=m['id']
    return sorted(group, key=lambda m: (0 if dup[m['id']] else 1, mech(m), m['id']))

# Higher is better to keep: richer moves first, then the earlier id.
keep_key=lambda m: (-mech(m), m['id'])

deleted=[]

# ---------------------------------------------------------------- §A
buckets=collections.defaultdict(list)
for m in moves:
    if m['category'] in ('Physical','Special') and m['power']>=95:
        buckets[(m['type'],m['category'],m['power'])].append(m)
for key, group in sorted(buckets.items()):
    if len(group)<=2: continue
    for m in rank_for_deletion(group)[:len(group)-2]:
        deleted.append(('§A', m, f"{key[0]}/{key[1]}/威力{key[2]} が{len(group)}件"))

# ---------------------------------------------------------------- §B
def spaced_subset(group):
    """Largest subset of `group` whose powers are all >= GAP apart, breaking
    ties toward the moves carrying more mechanics. Straight DP over the
    power-sorted list."""
    g=sorted(group, key=lambda m:(m['power'], keep_key(m)))
    n=len(g)
    best=[None]*(n+1); best[n]=(0,0,[])
    for i in range(n-1,-1,-1):
        skip=best[i+1]
        j=i+1
        while j<n and g[j]['power']-g[i]['power']<GAP: j+=1
        c,s,lst = best[j]
        take=(c+1, s+mech(g[i]), [g[i]]+lst)
        best[i] = take if take[:2]>=skip[:2] else skip
    return best[0][2]

for el in ELEMENTS:
    room=[m for m in moves if m['category'] in ('Physical','Special')
          and rng(m)=='Room' and m['type']==el and m not in [d[1] for d in deleted]]
    keepers=spaced_subset(room)
    kept_ids={m['id'] for m in keepers}
    for m in room:
        if m['id'] not in kept_ids:
            deleted.append(('§B', m, f"{el}の部屋技 威力{m['power']} が間隔{GAP}未満"))

del_ids={m['id'] for _,m,_ in deleted}
moves=[m for m in moves if m['id'] not in del_ids]

print(f"削除: {len(deleted)}技")
for tag,m,why in deleted:
    print(f"  {tag} {m['id']:<12} {m['name']:<16} {m['type']:<9} {m['category']:<8} 威力{m['power']:<4} {rng(m):<8} 機構{mech(m)}  ({why})")

spe=[m for m in moves if m['category']=='Special']
phy=[m for m in moves if m['category']=='Physical']
print(f"\n残存: 物理{len(phy)} 特殊{len(spe)} 合計{len(moves)}")

# ------------------------------------------------- §3 restore 7:2:1
n=len(spe)
want={'Line':round(n*0.7),'Area':round(n*0.2)}
want['Room']=n-want['Line']-want['Area']
have=collections.Counter(rng(m) for m in spe)
print(f"射程 現状 {dict(have)} → 目標 {want}")

room_pw=collections.defaultdict(list)   # element -> powers already committed
for m in moves:
    if m['category'] in ('Physical','Special') and rng(m)=='Room':
        room_pw[m['type']].append(m['power'])

def room_ok(el, p, exclude=None):
    return all(abs(p-q)>=GAP for q in room_pw[el] if q!=exclude)

# Promote Line moves into Room, preferring the elements that just lost Room
# moves so their coverage is restored rather than piled onto some other type.
# Promote Line moves into Room, preferring the elements that just lost Room
# moves, and aiming each promotion at the power band the deletion emptied -
# a promotion that lands at the very bottom of the scale restores the COUNT
# but not the coverage, which is the point of refilling at all.
lost=collections.defaultdict(list)
for _,m,_ in deleted:
    if rng(m)=='Room' and m['category']=='Special': lost[m['type']].append(m['power'])

need_room=want['Room']-have['Room']
promoted=[]
shift=RANGE_COST['Room']-RANGE_COST['Line']

def best_promotion(el, target):
    cands=[m for m in spe if m['type']==el and rng(m)=='Line' and room_ok(el, m['power']+shift)]
    if not cands: return None
    return min(cands, key=lambda x:(abs(x['power']+shift-target), x['id']))

order=[e for e in ELEMENTS if lost[e]]+[e for e in ELEMENTS if not lost[e]]
for el in order:
    targets=sorted(lost[el], reverse=True) or [None]
    for target in targets:
        if need_room<=0: break
        m=best_promotion(el, target if target is not None else 60)
        if m is None: break
        m['power']+=shift
        m['range']='Room'
        room_pw[el].append(m['power'])
        promoted.append((el,target,m)); need_room-=1
while need_room>0:
    picked=None
    for el in ELEMENTS:
        m=best_promotion(el, 60)
        if m is not None: picked=(el,m); break
    if picked is None: break
    el,m=picked
    m['power']+=shift; m['range']='Room'; room_pw[el].append(m['power'])
    promoted.append((el,None,m)); need_room-=1

# Area/Line are interchangeable filler: move the surplus tier into the short one.
have=collections.Counter(rng(m) for m in spe)
for src,dst in (('Area','Line'),('Line','Area')):
    while have[src]>want[src] and have[dst]<want[dst]:
        m=min([x for x in spe if rng(x)==src], key=lambda x:x['id'])
        m['power']+=RANGE_COST[dst]-RANGE_COST[src]
        m['range']=dst
        have[src]-=1; have[dst]+=1

print(f"射程 調整後 {dict(collections.Counter(rng(m) for m in spe))} (部屋へ昇格 {len(promoted)}技)")
for el,target,m in promoted:
    print(f"  昇格 {el:<9} {m['name']:<16} 威力{m['power']:<4} (欠けた帯 {target})")

# ------------------------------------------------- §2 restore 90% contact
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
print(f"接触 {sum(1 for m in phy if m['is_contact'])}/{len(phy)}")

# ------------------------------------------------- §1+§A dedup, Room pinned
SLOT_INSTANCES=[s for s in range(GRID_MIN, GRID_MAX+1, STEP) for _ in range(2)]

def assign(powers, cap):
    k,n=len(powers),len(SLOT_INSTANCES); INF=float('inf')
    dp=[[INF]*(n+1) for _ in range(k+1)]; dp[0]=[0]*(n+1)
    back=[[0]*(n+1) for _ in range(k+1)]
    for i in range(1,k+1):
        for j in range(i,n+1):
            skip=dp[i][j-1]
            take=dp[i-1][j-1]+abs(powers[i-1]-SLOT_INSTANCES[j-1]) if cap[j-1] else INF
            if take<=skip: dp[i][j],back[i][j]=take,1
            else: dp[i][j],back[i][j]=skip,0
    out=[None]*k; i,j=k,n
    while i>0:
        if back[i][j]==1: out[i-1]=SLOT_INSTANCES[j-1]; i-=1
        j-=1
    return out

moved=0
for cat in ('Physical','Special'):
    for el in ELEMENTS:
        grp=[m for m in moves if m['category']==cat and m['type']==el]
        pinned=[m for m in grp if rng(m)=='Room']            # §B keeps these put
        free=sorted([m for m in grp if rng(m)!='Room' and m['power']<=GRID_MAX],
                    key=lambda m:(m['power'],m['id']))
        used=collections.Counter(m['power'] for m in pinned)
        cap=[]
        seen=collections.Counter()
        for s in SLOT_INSTANCES:
            seen[s]+=1
            cap.append(seen[s] <= 2-used[s])
        overflow=[]
        while len(free)>sum(cap): overflow.append(free.pop())
        for m in overflow:
            if m['power']!=GRID_MAX+STEP: moved+=1
            m['power']=GRID_MAX+STEP
        if free:
            for m,slot in zip(free, assign([m['power'] for m in free], cap)):
                if m['power']!=slot: moved+=1; m['power']=slot
        # Above the grid there is no fixed slot list, so the cap is enforced
        # by stepping the surplus move up until its power has room.
        high=collections.defaultdict(list)
        for m in grp:
            if m['power']>GRID_MAX and rng(m)!='Room': high[m['power']].append(m)
        for p in sorted(high):
            pinned_here=sum(1 for m in pinned if m['power']==p)
            surplus=rank_for_deletion(high[p])[:max(0, len(high[p])+pinned_here-2)]
            for m in surplus:
                q=m['power']
                while True:
                    q+=STEP
                    if sum(1 for x in grp if x['power']==q and x is not m)<2: break
                m['power']=q; moved+=1

json.dump(moves, open(PATH,'w',encoding='utf-8'), ensure_ascii=False, indent=2)
open(PATH,'a',encoding='utf-8').write('\n')
print(f"威力を動かした技: {moved}")
