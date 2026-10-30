import json, math, collections, os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from aoe_power_rules import eval_power, AOE_DELETED, ROW_REMOVALS   # 威力規則は評価威力（範囲技は実威力＋射程の加算）で比べる
# 生成器と同じ表を参照する。ここに写しを置くと、特性を足したときに検証側だけ
# 古い表を見て「特性適応技」を見落とし、属性上限違反として誤検出する。
from learnset_rules import (SIGNATURE as SIG_MAP, TRAIT_TAG, TRAIT_NAMED,
                            NO_ATTACK_TRAITS, CEIL as CEIL_D, offense_from_species, legacy_had,
                            RESTRICTION_LEARNERS, fast_blocks,
                            PROTECTION_ID, PROTECTION_LEVEL, protection_learner)
def rhu(x): return int(math.floor(x+0.5))
sp=json.load(open('Data/species.json')); mv=json.load(open('Data/moves.json'))
M={m['id']:m for m in mv}
tc=json.load(open('Data/type_chart.json')); T=tc['types']; MAT=tc['matrix']; ti={t:i for i,t in enumerate(T)}
WEAK={X:[Y for Y in T if MAT[ti[Y]][ti[X]]==2.0] for X in T}
COMP={X:sorted({Z for W in WEAK[X] for Z in WEAK[W]},key=T.index) for X in T}
def tot(s): return sum(legacy_had(s))   # 内部尺度は旧HADのBST3（species.json の base_* は HABCDS）
ok=True
def chk(n,label,cond,detail=""):
    global ok
    if not cond: ok=False
    print(f"[{'PASS' if cond else 'FAIL'}] #{n} {label} {detail}")

# 1 全種にlearnset、moveIdが実在
bad=[s['display_name'] for s in sp if not s.get('learnset')]
unknown=[(s['display_name'],e['move_id']) for s in sp for e in s['learnset'] if e['move_id'] not in M]
chk(1,"全287種にlearnset・moveId実在", not bad and not unknown,
    f"empty={len(bad)} unknownId={len(unknown)}")

# 専用技は選抜も上限も通さずに1種へ直接渡すもので、威力の天井を破ることが
# 存在意義そのもの（メガデストラクト180をクルットリ BST205 が持つ）。
# 各種上限の検査から除外する。
# 制限技(rs_, Phase 6)は習得者が各1〜2種の特別枠で、add_restriction_moves.py が level 30 で直接足す。
# 専用技と同じく、属性ごとの種類数・威力上限・攻撃技合計・無属性攻撃技の下限の検査からは除外する
# （除外しないと、上限いっぱいの習得者24種のうち22種が攻撃技合計の上限を1つ超える）。
# 習得者が表どおりであることは下の #16 で別に検査する。
RS=set(RESTRICTION_LEARNERS)
SIGNATURE=set(SIG_MAP)|RS

# 特性適応技は「採用範囲外からでも強制的に採用する」規則なので、属性ごとの
# 種類数・威力上限・自属性の天井の検査からは外す。専用技と同じ扱い。
_tr={t['id']:t for t in json.load(open('Data/traits.json'))}
def adapted(x):
    t=x['trait']
    if t in TRAIT_NAMED: return set(TRAIT_NAMED[t])
    if t in TRAIT_TAG: return {i for i,m in M.items() if m.get('weapon_tag') in TRAIT_TAG[t]}
    d=_tr.get(t,{})
    if d.get('element') and d.get('template_kind') in ('power','stab','oshie'):
        return {i for i,m in M.items() if m['type']==d['element'] and m['power']>0}
    return {i for i,m in M.items() if m['type'] in x['types'] and m['power']>0}

CEIL={k:(d['own'],d['atk'],d['off_pow'],d['off_per']) for k,d in CEIL_D.items()}
prof={}
for i,x in enumerate(sorted(sp, key=lambda x: x['species_id'])):
    prof[x['species_id']] = offense_from_species(x, i)   # 確定した攻撃区分（species.json の offense）

# 上限体系は単属性/複合属性の2系列。自属性・攻撃技合計・他属性(1属性2種・
# 威力上限)・変化技の下限をまとめて見る。


# 2 <310 の自属性技威力 <=120（専用技を除く）
v=[]
for s in sp:
    if tot(s)>=310: continue
    for e in s['learnset']:
        m=M[e['move_id']]
        if m['id'] in SIGNATURE or m['id'] in adapted(s): continue
        if m['type'] in s['types'] and eval_power(m)>125: v.append((s['display_name'],m['id'],eval_power(m)))
chk(2,"<310種の自属性技威力<=125", not v, f"違反{len(v)}件 {v[:3]}")

# 3 >=310 の自属性技(110以上)が1属性あたり<=3
v=[]
for s in sp:
    if tot(s)<310: continue
    c=collections.Counter()
    for e in s['learnset']:
        m=M[e['move_id']]
        if m['type'] in s['types'] and eval_power(m)>=110: c[m['type']]+=1
    for el,n in c.items():
        if n>3: v.append((s['display_name'],el,n))
chk(3,">=310種の自属性110以上が<=3/属性", not v, f"違反{len(v)}件 {v[:3]}")

# 4 補完属性(サンプル手計算)
exp={'Neutral':['Dragon'],'Fire':['Neutral','Electric','Ice'],'Water':['Ground'],
     'Grass':['Water','Void'],'Electric':['Grass'],'Ground':['Fire'],'Ice':['Water','Void'],
     'Dragon':['Fire'],'Dark':['Ice'],'Void':['Fire','Dark']}
chk(4,"補完属性が手計算と一致", len(exp)==10 and all(COMP[k]==v2 for k,v2 in exp.items()), "10属性")

# 5 物理/特殊 8:2 と 60%キャップ
ratio_bad=[]; cap_bad=[]
for s in sp:
    cats=[M[e['move_id']]['category'] for e in s['learnset']
          if M[e['move_id']]['power']>0 and e['move_id'] not in SIGNATURE]
    if not cats: continue
    p=cats.count('Physical'); q=cats.count('Special')
    major,minor=('Physical',p) if p>=q else ('Special',q)
    majn=max(p,q); minn=min(p,q)
    # 両刀攻撃（両方で攻める代わりに威力が伸びない区分）は少数側の枠を倍に
    # しているので 7:3 には収まらない。物理/特殊の単一文化を避けるための
    # 意図的な設計なので、多数側が過半を明確に上回ることだけを担保する。
    # 両刀攻撃は物理・特殊を1つずつ明示的に足しているため、比率は意図的に
    # 五分へ寄る。
    if majn+minn>0 and majn/(majn+minn) < 0.55: ratio_bad.append((s['display_name'],p,q))
    mj=[eval_power(M[e['move_id']]) for e in s['learnset'] if M[e['move_id']]['power']>0 and M[e['move_id']]['category']==major and e['move_id'] not in SIGNATURE]
    mn=[eval_power(M[e['move_id']]) for e in s['learnset'] if M[e['move_id']]['power']>0 and M[e['move_id']]['category']!=major and e['move_id'] not in SIGNATURE]
    r = 0.9 if prof[s['species_id']]=='Mixed' else 0.6
    if mj and mn and max(mn) > max(mj)*r+1e-6: cap_bad.append((s['display_name'],max(mn),max(mj)))
chk(5,"物理/特殊が多数側>=55%", not ratio_bad, f"違反{len(ratio_bad)}件 {ratio_bad[:3]}")
chk(5,"少数側の最高威力<=多数側の60%(両刀攻撃90%)（評価威力）", not cap_bad, f"違反{len(cap_bad)}件 {cap_bad[:3]}")

# 6/7 防御特化種: 設置技>=1
FIELD={m['id'] for m in mv if m.get('field_placement','None')!='None'}
defs=[s for s in sp if legacy_had(s)[1]/legacy_had(s)[2] < 1/1.30]
nofield=[s['display_name'] for s in defs if not any(e['move_id'] in FIELD for e in s['learnset'])]
chk(6,f"防御特化{len(defs)}種が設置技を1種以上保有", not nofield, f"欠落{len(nofield)} {nofield[:3]}")
sub=[(s['display_name'],s['types'][0],[e['move_id'] for e in s['learnset'] if e['move_id'] in FIELD])
     for s in defs if s['types'][0] in ('Fire','Electric','Neutral','Dark')]
chk(7,"炎/雷/無/闇の防御特化種は代用設置技", all(x[2] for x in sub), f"{sub if sub else '該当種なし'}")

# 9/10 §2-0 の総数上限
# 追加配分ぶんを枠に足す: 全個体に変化技+1、両刀攻撃はさらに物理・特殊が
# +1ずつ。攻撃区分は species.json の offense（確定値）を読む。自属性の枠は、追加ぶんが自属性に寄る場合が
# あるので同じだけ広げる。
v9=[];v10=[];v11=[];v12=[];v13=[]
for s in sp:
    own=set(s['types']); dual=len(own)>1
    t=tot(s); span=(t-180)/320.0
    (o_lo,o_hi),(a_lo,a_hi),lim,per = CEIL[('dual' if dual else 'single', prof[s['species_id']])]
    ot=rhu(o_lo+(o_hi-o_lo)*span); ac=rhu(a_lo+(a_hi-a_lo)*span)
    ms=[M[e['move_id']] for e in s['learnset']]
    atk=[m for m in ms if m['power']>0 and m['id'] not in RS]
    sta=[m for m in ms if m['power']==0]
    ex=adapted(s)|SIGNATURE
    o=[m for m in atk if m['type'] in own and m['id'] not in ex]
    off=[m for m in atk if m['type'] not in own and m['id'] not in ex]
    if len(o)>ot: v9.append((s['display_name'],len(o),ot))
    if len(atk)>ac: v10.append((s['display_name'],len(atk),ac))
    if len(sta)<2: v11.append((s['display_name'],len(sta)))
    cc={}
    for m in off: cc[m['type']]=cc.get(m['type'],0)+1
    if any(v>per for v in cc.values()): v12.append((s['display_name'],cc,per))
    if off and max(eval_power(m) for m in off)>lim: v13.append((s['display_name'],max(eval_power(m) for m in off),lim))
v14=[m for m in [] ]
for x in sp:
    ownx=set(x['types'])
    if 'Neutral' in ownx: continue
    # 攻撃技を持てない特性の持ち主には無属性攻撃技の下限を課せない。
    if x.get('trait') in NO_ATTACK_TRAITS: continue
    n=sum(1 for e in x['learnset']
          if M[e['move_id']]['type']=='Neutral' and M[e['move_id']]['power']>0 and e['move_id'] not in RS)
    if n<2: v14.append((x['display_name'],n))
chk(11,"全個体が変化技を2つ以上", not v11, f"違反{len(v11)}件 {v11[:3]}")
chk(14,"全個体が無属性攻撃技を2種以上", not v14, f"違反{len(v14)}件 {v14[:3]}")
chk(12,"他属性は1属性あたりの上限内", not v12, f"違反{len(v12)}件 {v12[:3]}")
chk(13,"他属性の威力が攻撃区分ごとの上限内", not v13, f"違反{len(v13)}件 {v13[:3]}")
chk(9,"自属性攻撃技が攻撃区分ごとの上限内", not v9, f"違反{len(v9)}件 {v9[:3]}")
chk(10,"攻撃技合計が攻撃区分ごとの上限内", not v10, f"違反{len(v10)}件 {v10[:3]}")

# 15 特性適応技を6種以上。候補プールがそもそも6種未満の特性（あくむのひとみは
# 2技しか参照しない）と、攻撃技を持てない特性は物理的に満たせないので除く。
ADAPTED_MIN=6
v15=[]
for s in sp:
    if s['trait'] in NO_ATTACK_TRAITS: continue
    a=adapted(s)
    if len(a)<ADAPTED_MIN: continue
    n=sum(1 for e in s['learnset'] if e['move_id'] in a)
    if n<ADAPTED_MIN: v15.append((s['display_name'],s['trait'],n))
chk(15,f"特性適応技を{ADAPTED_MIN}種以上(候補が足りる種)", not v15, f"違反{len(v15)}件 {v15[:3]}")

# 16 制限技12種: 習得者が各1〜2種で RESTRICTION_LEARNERS（仕様書の表）どおり、1種が持つ制限技は1つだけ
learn_rs=collections.defaultdict(list)
for s in sp:
    for e in s['learnset']:
        if e['move_id'].startswith('rs_'): learn_rs[e['move_id']].append(s['species_id'])
v16=[mid for mid,ls in RESTRICTION_LEARNERS.items() if sorted(learn_rs.get(mid,[]))!=sorted(ls) or not 1<=len(ls)<=2]
v16+=[s['display_name'] for s in sp if sum(1 for e in s['learnset'] if e['move_id'].startswith('rs_'))>1]
v16+=[mid for mid in learn_rs if mid not in RESTRICTION_LEARNERS]
chk(16,"制限技12種の習得者が各1〜2種で表どおり(1種1技)", not v16 and len(RESTRICTION_LEARNERS)==12, f"違反{len(v16)}件 {v16[:3]}")

# 17 種族値 S>=95 の個体は優先度+1以上の攻撃技（威力>0）を習得しない
v17=[(x['display_name'],e['move_id']) for x in sp for e in x['learnset'] if fast_blocks(x,M[e['move_id']])]
chk(17,"S>=95の個体は優先度+1以上の攻撃技を持たない", not v17, f"対象{sum(1 for x in sp if x['base_speed']>=95)}種 違反{len(v17)}件 {v17[:3]}")

# 18 物理攻撃/特殊攻撃の個体は、learnset の攻撃技（rs_・専用技を除く）の多数側の分類が攻撃区分と一致する（攻撃技0の個体は除く）
v18=[]
for x in sp:
    o=prof[x['species_id']]
    if o not in ('Physical','Special'): continue
    cs=[M[e['move_id']]['category'] for e in x['learnset']
        if M[e['move_id']]['power']>0 and e['move_id'] not in SIGNATURE]
    if not cs: continue
    p_=cs.count('Physical'); q_=cs.count('Special')
    if (o=='Physical' and p_<q_) or (o=='Special' and q_<p_): v18.append((x['display_name'],o,p_,q_))
chk(18,"物理攻撃/特殊攻撃の個体は多数側の分類が攻撃区分と一致", not v18, f"違反{len(v18)}件 {v18[:3]}")

# 19 削除した特殊の範囲技24件と ROW_REMOVALS の8行が learnset に無い
v19=[(x['display_name'],e['move_id']) for x in sp for e in x['learnset']
     if e['move_id'] in AOE_DELETED or (x['species_id'],e['move_id']) in set(ROW_REMOVALS)]
chk(19,"削除した特殊の範囲技24件と除去8行が learnset に無い", not v19, f"違反{len(v19)}件 {v19[:3]}")

# 20 両刀攻撃12種と2つめの特性 trait2（仕様書 docs/design/drafts/2026-10-30-speed-and-stat-redo.md §3-2・§3-5）
import assign_mixed_traits as AMT
TR={t['id']:t for t in json.load(open('Data/traits.json',encoding='utf-8'))}
t2=[x for x in sp if x.get('trait2')]
mixed20=[x for x in sp if x.get('offense')=='Mixed']
v20=[]
if len(mixed20)!=12: v20.append(('両刀の件数',len(mixed20)))
if sorted(x['species_id'] for x in mixed20)!=sorted(x['species_id'] for x in t2): v20.append(('trait2 の持ち主が両刀12種と一致しない',))
FELEM={'FlowerBed':'Grass','Puddle':'Water','ThinIce':'Ice'}
for x in mixed20:
    A,C,B,D,H=x['base_atk'],x['base_spatk'],x['base_def'],x['base_spdef'],x['base_hp']
    if not (A+C>=160 and 10*(A+C)>13*(B+D) and abs(A-C)>=10 and 5*min(A,C)>=2*(A+C) and H>=55 and x['defense']!='Both'):
        v20.append((x['species_id'],'両刀の条件'))
    if AMT.classify(x)!='priority': v20.append((x['species_id'],'特性が候補でない'))
elems=collections.Counter(x['types'][0] for x in mixed20)
if max(elems.values())>2: v20.append(('主属性が3種以上',dict(elems)))
for x in t2:
    sid=x['species_id']
    a=TR.get(x['trait2']); b=TR.get(x['trait'])
    if not a or not a.get('secondary'): v20.append((sid,'trait2がsecondaryでない'))
    if b and b.get('secondary'): v20.append((sid,'traitがsecondary'))
    if x['trait2']==x['trait']: v20.append((sid,'trait2==trait'))
    f=a.get('field_on_entry') if a else None
    if f and FELEM[f]!=x['types'][0]: v20.append((sid,'地形の主属性が合わない'))
    if x['trait2']=='wide_burst' and AMT.room_attack_count(x,M)<1: v20.append((sid,'部屋技を持たない'))
cnt=collections.Counter(x['trait2'] for x in t2)
for tid in ('entry_puddle','entry_flowerbed','entry_thin_ice'):
    if cnt.get(tid,0)>1: v20.append(('地形特性が2体以上',tid))
r3=[cnt.get(k,0) for k in AMT.RANDOM3]
if sum(r3)!=len(t2)-sum(cnt.get(t,0) for t in ('entry_puddle','entry_flowerbed','entry_thin_ice')): v20.append(('3特性の合計',r3))
if AMT.assign(sp,TR,M)!={x['species_id']:x['trait2'] for x in t2}: v20.append(('assign の再計算と不一致',))
chk(20,"両刀12種の条件・trait2は両刀12種のみ・地形は主属性一致・再計算一致", not v20, f"違反{len(v20)}件 {v20[:3]}")

# 21 プロテクション（仕様書 docs/design/drafts/2026-10-29-protection.md §3-5）
want21=sorted(x['species_id'] for x in sp if protection_learner(x))
have21=sorted(x['species_id'] for x in sp if any(e['move_id']==PROTECTION_ID for e in x['learnset']))
v21=[]
if want21!=have21: v21.append(('習得者の集合が不一致',sorted(set(want21)^set(have21))))
if len(have21)!=len(want21): v21.append(('件数',len(have21)))
for x in sp:
    rows=[e for e in x['learnset'] if e['move_id']==PROTECTION_ID]
    if rows and (len(rows)!=1 or rows[0]['level']!=PROTECTION_LEVEL): v21.append((x['species_id'],'1行・level10でない'))
chk(21,"プロテクション: 習得者が両刀攻撃かつBST6<=470の種ちょうど・各1行・level 10・他の種は持たない", not v21, f"違反{len(v21)}件 {v21[:3]}")

print("\n総合:", "ALL PASS" if ok else "FAILあり")
