# -*- coding: utf-8 -*-
"""Attack-move rebalance.

Applied in this order, because each step feeds the next:

  §3  Every Special move takes a reaching range: 70% Line / 20% Area /
      10% Room, computed on the 310 Special moves as a whole. A move that
      already has one of those ranges keeps it (up to that tier's quota);
      the rest are dealt out across the power-sorted list in a 7:2:1 cycle,
      so each tier spans the whole power band instead of clustering at one
      end. Physical ranges are left exactly as they are.

  §4  Reach is priced in power: TwoTile -5, Line -10, Area -15, Room -20
      (FullFloor is priced as Room). Only the DELTA is charged, so a move
      that already had its range pays nothing and a move going
      Adjacent -> Line loses 10 power. Widening reach costs power; the few
      moves whose range narrows are refunded the same way.

  §2  90% of Physical moves are contact. The 10% left non-contact are
      chosen as: every Physical move that does not have Adjacent range
      (a move that strikes from two tiles away cannot be making contact),
      then the moves whose names denote a thrown or fired object
      (弾/砲/礫/つぶて/投げ/飛ばし), then by id to make up the number.

  §1  For power <= 90, at most 2 moves share the same
      (element, category, power). Overflowing moves are moved to the
      nearest free 5-step slot, preferring to move DOWN so the pool does
      not inflate; if the whole <=90 grid for that element is full, the
      move spills above 90, where the rule does not apply.
"""
import json, re, collections

PATH='Data/moves.json'
ELEMENTS=['Neutral','Fire','Water','Grass','Electric','Ground','Ice','Dragon','Dark']
RANGE_COST={'Adjacent':0,'TwoTile':-5,'Line':-10,'Area':-15,'Room':-20,'FullFloor':-20}
TIERS=('Line','Area','Room')
GRID_MIN, GRID_MAX, STEP = 15, 90, 5

moves=json.load(open(PATH,encoding='utf-8'))
by_id={m['id']:m for m in moves}
spe=[m for m in moves if m['category']=='Special']
phy=[m for m in moves if m['category']=='Physical']
rng=lambda m: m.get('range','Adjacent')

# ---------------------------------------------------------------- §3
total=len(spe)
want={'Line':round(total*0.7),'Area':round(total*0.2)}
want['Room']=total-want['Line']-want['Area']

# per-element quotas, then repair rounding so the GLOBAL split is exact
quota={}
for el in ELEMENTS:
    n=sum(1 for m in spe if m['type']==el)
    q={'Room':round(n*0.1),'Area':round(n*0.2)}
    q['Line']=n-q['Room']-q['Area']
    quota[el]=q
for tier in TIERS:
    while sum(quota[e][tier] for e in ELEMENTS) != want[tier]:
        diff = want[tier] - sum(quota[e][tier] for e in ELEMENTS)
        step = 1 if diff>0 else -1
        # take from / give to the tier with the most slack, largest element first
        other = 'Line' if tier!='Line' else 'Area'
        el = max(ELEMENTS, key=lambda e: (quota[e][other], e))
        quota[el][tier]+=step; quota[el][other]-=step

new_range={}
for el in ELEMENTS:
    pool=sorted([m for m in spe if m['type']==el], key=lambda m:(m['power'],m['id']))
    q=dict(quota[el])
    keep={t:[] for t in TIERS}; rest=[]
    for m in pool:
        r=rng(m)
        if r in TIERS and len(keep[r])<q[r]: keep[r].append(m); new_range[m['id']]=r
        else: rest.append(m)
    need={t:q[t]-len(keep[t]) for t in TIERS}
    used=collections.Counter()
    for i,m in enumerate(rest):
        frac=i%10
        pref='Line' if frac<7 else ('Area' if frac<9 else 'Room')
        for cand in (pref,)+TIERS:
            if used[cand]<need[cand]: new_range[m['id']]=cand; used[cand]+=1; break

# ---------------------------------------------------------------- §4
range_changed=power_changed=0
for m in spe:
    old=rng(m); new=new_range[m['id']]
    if new!=old: range_changed+=1
    delta=RANGE_COST[new]-RANGE_COST[old]
    if delta:
        m['power']=max(5, m['power']+delta)
        power_changed+=1
    m['range']=new

# ---------------------------------------------------------------- §2
want_contact=round(len(phy)*0.9)
want_plain=len(phy)-want_contact
PROJECTILE=re.compile(r'だん$|だん[おうげきだとつ]|ほう$|ほう[げきだ]|つぶて|なげ$|とばし|れき$|しずく')
plain=[m for m in phy if rng(m)!='Adjacent']
for m in sorted(phy,key=lambda m:m['id']):
    if len(plain)>=want_plain: break
    if m in plain: continue
    if PROJECTILE.search(m['name']): plain.append(m)
for m in sorted(phy,key=lambda m:m['id']):
    if len(plain)>=want_plain: break
    if m not in plain: plain.append(m)
plain_ids={m['id'] for m in plain}
for m in phy: m['is_contact'] = m['id'] not in plain_ids

# ---------------------------------------------------------------- §1
SLOTS=list(range(GRID_MIN, GRID_MAX+1, STEP))
SLOT_INSTANCES=[s for s in SLOTS for _ in range(2)]   # 2 moves allowed per power

def assign_slots(powers):
    """Monotone minimum-displacement assignment of `powers` (sorted) onto the
    2-per-value grid. A greedy nearest-free pass looks cheaper but strands the
    last movers at the far end of the grid - it produced a 95 -> 15 jump - so
    this solves it properly: dp[i][j] = cheapest way to place the first i moves
    into the first j slot instances, each move taking at most one slot and the
    order preserved."""
    k, n = len(powers), len(SLOT_INSTANCES)
    INF = float('inf')
    dp=[[INF]*(n+1) for _ in range(k+1)]
    dp[0]=[0]*(n+1)
    back=[[0]*(n+1) for _ in range(k+1)]
    for i in range(1,k+1):
        for j in range(i,n+1):
            skip = dp[i][j-1]
            take = dp[i-1][j-1] + abs(powers[i-1]-SLOT_INSTANCES[j-1])
            if take <= skip: dp[i][j], back[i][j] = take, 1
            else:            dp[i][j], back[i][j] = skip, 0
    out=[None]*k
    i,j=k,n
    while i>0:
        if back[i][j]==1: out[i-1]=SLOT_INSTANCES[j-1]; i-=1
        j-=1
    return out

moved=[]
for cat in ('Physical','Special'):
    for el in ELEMENTS:
        group=sorted([m for m in moves if m['category']==cat and m['type']==el and m['power']<=GRID_MAX],
                     key=lambda m:(m['power'],m['id']))
        # More moves than the grid can hold: the strongest spill just above 90,
        # where the "2 per power" rule does not apply.
        overflow=[]
        while len(group)>len(SLOT_INSTANCES):
            overflow.append(group.pop())
        for m in overflow:
            moved.append((m,m['power'],GRID_MAX+STEP)); m['power']=GRID_MAX+STEP
        if not group: continue
        for m,slot in zip(group, assign_slots([m['power'] for m in group])):
            if m['power']!=slot: moved.append((m,m['power'],slot)); m['power']=slot

json.dump(moves, open(PATH,'w',encoding='utf-8'), ensure_ascii=False, indent=2)
open(PATH,'a',encoding='utf-8').write('\n')

# ---------------------------------------------------------------- report
print(f"§3 特殊技の射程: {dict(collections.Counter(m['range'] for m in spe))} (変更 {range_changed}技)")
print(f"§4 威力を調整した特殊技: {power_changed}")
print(f"§2 物理技の接触: {sum(1 for m in phy if m['is_contact'])}/{len(phy)} "
      f"({sum(1 for m in phy if m['is_contact'])/len(phy)*100:.1f}%) 非接触 {len(plain_ids)}")
print(f"§1 威力を動かした技: {len(moved)}")
bad=0
for cat in ('Physical','Special'):
    c=collections.Counter((m['type'],m['power']) for m in moves
                          if m['category']==cat and m['power']<=GRID_MAX)
    bad+=sum(1 for v in c.values() if v>2)
print(f"§1 違反バケツ(3件以上): {bad}")
pw=[m['power'] for m in moves if m['category'] in ('Physical','Special')]
print(f"攻撃技の威力: min={min(pw)} max={max(pw)}")
