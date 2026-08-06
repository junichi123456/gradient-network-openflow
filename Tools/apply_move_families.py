# -*- coding: utf-8 -*-
"""Puts the physical move families on their stated rules, then fills the
power grid so no (element, category, power) slot in 15..120 is empty.

Families are identified by the SUFFIX of the move's name. Each has a cap and
its own rules; the caps are treated as targets, not just ceilings ("可能な限り
上限数を採用できるよう試みる"), so a family that is short is topped up by
renaming suitable moves INTO it, and one that is over is trimmed by renaming
the surplus OUT to an unregulated suffix.

    ストライク  tag Strike  cap 10        power <= 80
    フィスト    tag Fist    cap 8         power <= 80
    ブロー      tag Fist    cap 6         power <= 80
    パンチ      tag Punch   cap 2/element power <= 70, always a side effect
    スラスト    tag Thrust  2 Neutral + 1 each  non-contact, always TwoTile
    クラッシュ  tag Crush   cap 5         knockback (AttackAction)
    レンド      tag Rend    cap 3         clears the field/trap underfoot
    フラッシュ  tag Flash   cap 2         non-contact, never misses, always
                                          crits, power fixed at 50

Moves that are NOT physical but carry a family suffix are renamed out - the
families are a physical-move concept.

スラスト is capped at 10 - two Neutral, one of every other element. The 17
TwoTile moves that no longer fit the family give up ONLY their name and
their weapon tag: they keep TwoTile and keep being non-contact, because
every regulated family is already at its cap so there is nowhere for them
to go, and moving them back to Adjacent+contact would break the 9:1 contact
ratio (31 non-contact of 310 physical) that they make up the bulk of.

Grid fill: every (element, category) needs at least one move at each power
15,20,...,120. New moves are Adjacent, because Adjacent is the only range
without a per-element cap - filling with ranged moves would immediately
break Line<=6 / Area<=2 / Room<=4.
"""
import json, collections, sys

PATH='Data/moves.json'
ELEMENTS=['Neutral','Fire','Water','Grass','Electric','Ground','Ice','Dragon','Dark']
GRID=list(range(15,121,5))
TWOTILE_CAP=3

# suffix -> (weapon tag, cap, max power)
FAMILY={
    'ストライク': ('Strike', 10, 80),
    'フィスト':   ('Fist',    8, 80),
    'ブロー':     ('Fist',    6, 80),
    'パンチ':     ('Punch',  18, 70),   # 2 per element
    'クラッシュ': ('Crush',   5, None),
    'レンド':     ('Rend',    3, None),
    'フラッシュ': ('Flash',   2, 50),
    # cap None keeps スラスト out of the generic sizing pass below - it has
    # its own block, because the generic one also strips TwoTile on the way
    # out and here the surplus is meant to KEEP its range.
    'スラスト':   ('Thrust', None, None),
}
# Where a move goes when it is pushed out of a regulated family. None of
# these are themselves regulated suffixes.
EXILE={
    'ストライク': ['スマッシュ','ビート','スラム','インパクト'],
    'フィスト':   ['ナックル','グリップ','クラッチ','ハンマー'],
    'ブロー':     ['バッシュ','ノック','ドライブ','スイング'],
    'パンチ':     ['ジャブ','アッパー','ストンプ','フック'],
    'スラスト':   ['ピアス','スパイク','ランス','スティング'],
    'クラッシュ': ['シャッター'], 'レンド': ['テアー'], 'フラッシュ': ['グレア'],
}
ELEMENT_WORDS={
    'Neutral':['プレーン','ホワイト','ヴォイド'], 'Fire':['フレイム','ブレイズ','エンバー'],
    'Water':['アクア','タイド','スプレー'],       'Grass':['リーフ','ヴァイン','ブルーム'],
    'Electric':['ボルト','スパーク','アーク'],    'Ground':['ロック','アース','サンド'],
    'Ice':['フロスト','アイス','グレイシャー'],   'Dragon':['ドラゴン','スケイル','ワイバーン'],
    'Dark':['シャドウ','ダーク','アビス'],
}
PHYS_SUFFIX=['スマッシュ','ビート','スラム','インパクト','ハンマー','ドライブ','バッシュ',
             'ノック','ラッシュ','ジャブ','アッパー','スイング','ストンプ','クロー','ファング',
             'ホーン','テイル','キック','ナックル','グリップ']
SPEC_SUFFIX=['ショット','バレット','ウェーブ','レイ','ビーム','パルス','ノヴァ','バースト',
             'ブラスト','スフィア','オーブ','ランス','スピア','アロー','ダート','ミスト',
             'ヴェイル','コメット','サージ','プリズム']

moves=json.load(open(PATH,encoding='utf-8'))
used_names={m['name'] for m in moves}
used_ids={m['id'] for m in moves}
rng=lambda m: m.get('range','Adjacent')
is_multi=lambda m: m.get('multi_hit','None') not in (None,'None')
def fam_of(name):
    for f in FAMILY:
        if name.endswith(f): return f
    return None

def rename(m, new):
    used_names.discard(m['name']); m['name']=new; used_names.add(new)

def exile(m):
    """Push a move out of its family onto an unregulated suffix. Leaving
    スラスト also gives up TwoTile - the range belongs to the family, and
    leaving it behind was what pushed Water to 5 TwoTile moves."""
    f=fam_of(m['name'])
    if f=='スラスト' and m.get('range')=='TwoTile':
        m['range']='Adjacent'
        if m['category']=='Physical': m['is_contact']=True
    stem=m['name'][:-len(f)]
    for suf in EXILE[f]:
        if stem+suf not in used_names and fam_of(stem+suf) is None:
            rename(m, stem+suf); return
    for suf in PHYS_SUFFIX:
        if stem+suf not in used_names and fam_of(stem+suf) is None:
            rename(m, stem+suf); return
    raise RuntimeError(f"逃がし先が尽きた: {m['name']}")

ALL_SUFFIX=None   # filled in below, once the suffix pools exist

def strip_suffix(name):
    """Drop a trailing move-suffix so an adopted move becomes
    <stem>+<family> rather than <whole name>+<family>, which produced
    ウォーターヴェインナックルフラッシュ on the first run."""
    for suf in ALL_SUFFIX:
        if name.endswith(suf) and len(name)>len(suf): return name[:-len(suf)]
    return name

def adopt(m, family):
    """Rename a move INTO a family so an under-filled family reaches its cap.

    Prefers a short <element word>+<family> when swapping the suffix would
    give something unwieldy - appending to a full name produced
    ドラゴンゴッドブレイククラッシュ on an earlier run."""
    old=m['name']
    stem=strip_suffix(old)
    short=[stem+family] if len(stem+family)<=10 else []
    elemental=[w+family for w in ELEMENT_WORDS[m['type']]]
    for cand in short+elemental+[stem+family, old+family]:
        if cand not in used_names:
            rename(m, cand); return True
    return False

def spread(sorted_moves, k):
    """k of `sorted_moves` (already power-sorted) at even intervals, so a
    family keeps a weak/mid/strong spread instead of clustering."""
    n=len(sorted_moves)
    if k<=0 or n==0: return []
    if k>=n: return list(sorted_moves)
    return [sorted_moves[round(i*(n-1)/(k-1)) if k>1 else 0] for i in range(k)]

ALL_SUFFIX=sorted(set(list(FAMILY)+PHYS_SUFFIX+SPEC_SUFFIX
                      +[x for v in EXILE.values() for x in v]), key=len, reverse=True)

log=collections.Counter()

# ---------------- 1. non-physical moves leave the families ----------------
for m in moves:
    if m['category']!='Physical' and fam_of(m['name']):
        exile(m); log['非物理を系統外へ']+=1

# ---------------- 2. size every capped family to its cap -----------------
def eligible(m, tag, maxpw, family):
    if m['category']!='Physical' or is_multi(m): return False
    if maxpw is not None and family!='フラッシュ' and m['power']>maxpw: return False
    return True

for family,(tag,cap,maxpw) in FAMILY.items():
    if cap is None: continue
    members=[m for m in moves if m['name'].endswith(family)]
    keep=[m for m in members if eligible(m, tag, maxpw, family)]
    per_el = cap//len(ELEMENTS) if family=='パンチ' else None

    if family=='パンチ':                      # cap is 2 PER ELEMENT
        chosen=[]
        for el in ELEMENTS:
            chosen += spread(sorted([m for m in keep if m['type']==el],
                                    key=lambda m:(m['power'], m['id'])), 2)
    else:
        # One per element first so no element is shut out, then fill the
        # remaining slots spreading over the power band - picking purely by
        # power put every フィスト at 80 and every ブロー at 75.
        chosen=[]
        by_el=collections.defaultdict(list)
        for m in sorted(keep, key=lambda m:(m['power'], m['id'])): by_el[m['type']].append(m)
        for el in ELEMENTS:
            if len(chosen)<cap and by_el[el]:
                pick=by_el[el][len(by_el[el])//2]      # that element's median
                chosen.append(pick); by_el[el].remove(pick)
        rest=sorted([m for g in by_el.values() for m in g], key=lambda m:(m['power'], m['id']))
        chosen += spread(rest, cap-len(chosen))
    chosen_ids={m['id'] for m in chosen}
    for m in members:
        if m['id'] not in chosen_ids:
            exile(m); log[f'{family}を系統外へ']+=1

    # top up an under-filled family by adopting suitable moves into it
    short=cap-len(chosen_ids)
    if short>0:
        want_el=collections.Counter(m['type'] for m in chosen)
        pool=[m for m in moves
              if m['category']=='Physical' and not is_multi(m) and fam_of(m['name']) is None
              and (maxpw is None or family=='フラッシュ' or m['power']<=maxpw)]
        if family=='パンチ':
            pool=[m for m in pool if want_el[m['type']]<2]
        pool.sort(key=lambda m:(want_el[m['type']], -m['power'], m['id']))
        for m in pool:
            if short<=0: break
            if family=='パンチ' and want_el[m['type']]>=2: continue
            if adopt(m, family):
                want_el[m['type']]+=1; short-=1; log[f'{family}へ編入']+=1


# ---------------- 3. スラスト: every physical TwoTile move joins ----------
# TwoTile is capped at 3 per element, and スラスト demands TwoTile, so the
# two rules collide unless the TwoTile slots belong to スラスト. The three
# pre-existing Water TwoTile moves are adopted in rather than left to
# squat on Water's entire TwoTile budget.
for m in moves:
    if m['category']=='Physical' and rng(m)=='TwoTile' and not m['name'].endswith('スラスト'):
        if adopt(m, 'スラスト'): log['スラストへ編入']+=1

# フラッシュ's fixed power lands before the grid is measured, since it
# moves a slot.
for m in moves:
    if m['name'].endswith('フラッシュ'): m['power']=50

def grid_gaps():
    gaps=[]
    for cat in ('Physical','Special'):
        for el in ELEMENTS:
            have={m['power'] for m in moves
                  if m['type']==el and m['category']==cat and not is_multi(m)}
            gaps += [(el,cat,p) for p in GRID if p not in have]
    return gaps

# Two stages. First pick the 27 moves that occupy the TwoTile slots (3 per
# element, the per-element TwoTile cap); everything beyond that leaves the
# family outright and goes back to Adjacent+contact. Then, of those 27, only
# 10 keep the スラスト name and the Thrust tag - 2 Neutral and 1 of each
# other element. The other 17 change name and weapon tag ONLY: they stay
# TwoTile and stay non-contact, which is what holds the 9:1 contact ratio
# (31 non-contact of 310 physical) together.
KEEP_QUOTA={el:(2 if el=='Neutral' else 1) for el in ELEMENTS}   # 10 total

thrust=[m for m in moves if m['name'].endswith('スラスト')]
twotile=[]
for el in ELEMENTS:
    pool=sorted([m for m in thrust if m['type']==el], key=lambda m:(m['power'], m['id']))
    twotile += spread(pool, min(TWOTILE_CAP, len(pool)))
twotile_ids={m['id'] for m in twotile}
for m in thrust:
    if m['id'] not in twotile_ids:
        exile(m); log['スラストを系統外へ(射程も返上)']+=1

named=[]
for el in ELEMENTS:
    pool=sorted([m for m in twotile if m['type']==el], key=lambda m:(m['power'], m['id']))
    named += spread(pool, KEEP_QUOTA[el])
named_ids={m['id'] for m in named}
for m in twotile:
    m['range']='TwoTile'
    m['is_contact']=False
    if m['id'] in named_ids: continue
    stem=m['name'][:-len('スラスト')]
    placed=False
    for suf in EXILE['スラスト']+PHYS_SUFFIX:
        if stem+suf not in used_names and fam_of(stem+suf) is None:
            rename(m, stem+suf); placed=True; break
    if not placed:
        for w in ELEMENT_WORDS[m['type']]:
            for suf in EXILE['スラスト']+PHYS_SUFFIX:
                if w+suf not in used_names and fam_of(w+suf) is None:
                    rename(m, w+suf); placed=True; break
            if placed: break
    if not placed: raise RuntimeError(f"逃がし先が尽きた: {m['name']}")
    m['weapon_tag']='None'
    log['スラスト系統外へ(名称+タグのみ)']+=1

# ---------------- 4. apply each family's rules ---------------------------
AILMENT_BY_ELEMENT={'Neutral':'Stun','Fire':'Burn','Water':'Soaked','Grass':'VineBound',
                    'Electric':'Paralyze','Ground':'MudCaked','Ice':'Freeze',
                    'Dragon':'Stun','Dark':'Darkness'}
# "ツメ/こぶしのウェポンタグをフィスト系に名称変更" with a cap of 8 only
# makes sense if the tag ends up on the フィスト family (and ブロー, which
# is told to take the same tag) rather than on all 54 of the old ClawFist
# holders - otherwise the cap would mean nothing. The legacy tag is cleared
# first and re-applied from the families below.
for m in moves:
    if m.get('weapon_tag')=='ClawFist': m['weapon_tag']='None'; log['旧ClawFistを解除']+=1

for m in moves:
    f=fam_of(m['name'])
    if f is None or m['category']!='Physical': continue
    tag=FAMILY[f][0]
    m['weapon_tag']=tag
    if f=='スラスト':
        m['range']='TwoTile'; m['is_contact']=False
    elif f=='フラッシュ':
        m['power']=50; m['is_contact']=False; m['range']='Adjacent'
        m['is_guaranteed_hit']=True; m['guaranteed_crit']=True
    elif f=='パンチ':
        # "すべてに追加効果を付与" - give the ones without one their
        # element's ailment rather than inventing a new mechanic.
        has=(m.get('ailment_effect','None') not in (None,'None')
             or m.get('rank_effect_stat','None') not in (None,'None')
             or m.get('rank_effects'))
        if not has:
            m['ailment_effect']=AILMENT_BY_ELEMENT[m['type']]
            m['ailment_chance']=20
            m['ailment_target']='Enemy'
            log['パンチに追加効果を付与']+=1
        m['is_contact']=True
    else:
        m['is_contact']=True

# ---------------- 4b. repair powers crowded by フラッシュ ----------------
# Pinning フラッシュ to 50 can push its element's power-50 bucket to three.
# The family move stays put; a non-family neighbour is nudged to the nearest
# power with room. Any slot this empties is refilled by the grid pass below.
for el in ELEMENTS:
    for cat in ('Physical','Special'):
        while True:
            group=[m for m in moves if m['type']==el and m['category']==cat
                   and not is_multi(m)]
            counts=collections.Counter(m['power'] for m in group)
            crowded=[p for p,n in counts.items() if n>2]
            if not crowded: break
            p=crowded[0]
            movable=[m for m in group if m['power']==p and fam_of(m['name']) is None]
            if not movable:
                movable=[m for m in group if m['power']==p and not m['name'].endswith('フラッシュ')]
            if not movable: break
            m=sorted(movable, key=lambda x:x['id'])[0]
            free=[q for q in GRID if counts[q]<2]
            if not free: break
            m['power']=min(free, key=lambda q:(abs(q-p), q))
            log['威力の衝突を退避']+=1

# ---------------- 5. fill the 15..120 grid -------------------------------
def pp_for(power):
    for lim,pp in ((20,36),(40,30),(60,28),(80,18),(100,8),(120,6)):
        if power<lim: return pp
    return 6

def new_name(el, cat, power):
    pool=PHYS_SUFFIX if cat=='Physical' else SPEC_SUFFIX
    for w in ELEMENT_WORDS[el]:
        for suf in pool:
            if w+suf not in used_names and fam_of(w+suf) is None:
                return w+suf
    for w in ELEMENT_WORDS[el]:
        for suf in pool:
            for n in range(2, 9):
                cand=f"{w}{suf}{'Ⅱ' if n==2 else ''}"
                if cand not in used_names and fam_of(cand) is None: return cand
    raise RuntimeError(f"命名候補が尽きた: {el}/{cat}/{power}")

added=0
for el,cat,power in grid_gaps():
    name=new_name(el,cat,power)
    used_names.add(name)
    mid=f"gap_{el.lower()}_{'p' if cat=='Physical' else 's'}_{power}"
    entry={'id':mid,'name':name,'type':el,'category':cat,'power':power,
           'accuracy':100,'max_pp':pp_for(power),'range':'Adjacent'}
    if cat=='Physical': entry['is_contact']=True
    moves.append(entry); used_ids.add(mid); added+=1
log['グリッド補充で新規追加']=added

# ---------------- 6. contact ratio 9:1 -----------------------------------
for m in moves:
    if m['category']!='Physical': continue
    if rng(m)!='Adjacent' or m['name'].endswith('フラッシュ'): m['is_contact']=False
    else: m['is_contact']=True

json.dump(moves, open(PATH,'w',encoding='utf-8'), ensure_ascii=False, indent=2)
open(PATH,'a',encoding='utf-8').write('\n')
for k,v in sorted(log.items()): print(f"  {k}: {v}")
print()
for family in FAMILY:
    g=[m for m in moves if m['name'].endswith(family)]
    pw=[m['power'] for m in g]
    print(f"  {family:<7} {len(g):>2}件" + (f"  威力 {min(pw)}-{max(pw)}" if pw else ""))
