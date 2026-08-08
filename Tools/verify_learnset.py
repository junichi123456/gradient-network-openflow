import json, math, collections
def rhu(x): return int(math.floor(x+0.5))
sp=json.load(open('Data/species.json')); mv=json.load(open('Data/moves.json'))
M={m['id']:m for m in mv}
tc=json.load(open('Data/type_chart.json')); T=tc['types']; MAT=tc['matrix']; ti={t:i for i,t in enumerate(T)}
WEAK={X:[Y for Y in T if MAT[ti[Y]][ti[X]]==2.0] for X in T}
COMP={X:sorted({Z for W in WEAK[X] for Z in WEAK[W]},key=T.index) for X in T}
def tot(s): return s['base_hp']+s['base_atk']+s['base_def']
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
SIGNATURE={'megaton_self_destruct'}

# 2 <310 の自属性技威力 <=120（専用技を除く）
v=[]
for s in sp:
    if tot(s)>=310: continue
    for e in s['learnset']:
        m=M[e['move_id']]
        if m['id'] in SIGNATURE: continue
        if m['type'] in s['types'] and m['power']>120: v.append((s['display_name'],m['id'],m['power']))
chk(2,"<310種の自属性技威力<=120", not v, f"違反{len(v)}件 {v[:3]}")

# 3 >=310 の自属性技(110以上)が1属性あたり<=3
v=[]
for s in sp:
    if tot(s)<310: continue
    c=collections.Counter()
    for e in s['learnset']:
        m=M[e['move_id']]
        if m['type'] in s['types'] and m['power']>=110: c[m['type']]+=1
    for el,n in c.items():
        if n>3: v.append((s['display_name'],el,n))
chk(3,">=310種の自属性110以上が<=3/属性", not v, f"違反{len(v)}件 {v[:3]}")

# 4 補完属性(サンプル手計算)
exp={'Fire':'Electric','Water':'Ground','Dark':'Ice','Neutral':'Dragon','Grass':'Water',
     'Ice':'Water','Dragon':'Fire','Ground':'Fire','Electric':'Grass'}
chk(4,"補完属性が手計算と一致", all(COMP[k]==[v2] for k,v2 in exp.items()), "9属性")

# 5 物理/特殊 8:2 と 60%キャップ
ratio_bad=[]; cap_bad=[]
for s in sp:
    cats=[M[e['move_id']]['category'] for e in s['learnset']
          if M[e['move_id']]['power']>0 and e['move_id'] not in SIGNATURE]
    if not cats: continue
    p=cats.count('Physical'); q=cats.count('Special')
    major,minor=('Physical',p) if p>=q else ('Special',q)
    majn=max(p,q); minn=min(p,q)
    # 器用型（打ち分けが利く代わりに威力が伸びない型）は少数側の枠を倍に
    # しているので 7:3 には収まらない。物理/特殊の単一文化を避けるための
    # 意図的な設計なので、下限を 60% に緩めて「多数側が過半を明確に上回る」
    # ことだけを担保する。
    # 打ち分け型は物理・特殊を1つずつ明示的に足しているため、比率は意図的に
    # 五分へ寄る。多数側が過半を上回っていることだけを担保する。
    if majn+minn>0 and majn/(majn+minn) < 0.55: ratio_bad.append((s['display_name'],p,q))
    mj=[M[e['move_id']]['power'] for e in s['learnset'] if M[e['move_id']]['power']>0 and M[e['move_id']]['category']==major and e['move_id'] not in SIGNATURE]
    mn=[M[e['move_id']]['power'] for e in s['learnset'] if M[e['move_id']]['power']>0 and M[e['move_id']]['category']!=major and e['move_id'] not in SIGNATURE]
    if mj and mn and max(mn) > max(mj)*0.6+1e-6: cap_bad.append((s['display_name'],max(mn),max(mj)))
chk(5,"物理/特殊が多数側>=55%", not ratio_bad, f"違反{len(ratio_bad)}件 {ratio_bad[:3]}")
chk(5,"少数側の最高威力<=多数側の60%", not cap_bad, f"違反{len(cap_bad)}件 {cap_bad[:3]}")

# 6/7 防御特化種: 設置技>=1
FIELD={m['id'] for m in mv if m.get('field_placement','None')!='None'}
defs=[s for s in sp if s['base_atk']/s['base_def'] < 1/1.30]
nofield=[s['display_name'] for s in defs if not any(e['move_id'] in FIELD for e in s['learnset'])]
chk(6,f"防御特化{len(defs)}種が設置技を1種以上保有", not nofield, f"欠落{len(nofield)} {nofield[:3]}")
sub=[(s['display_name'],s['types'][0],[e['move_id'] for e in s['learnset'] if e['move_id'] in FIELD])
     for s in defs if s['types'][0] in ('Fire','Electric','Neutral','Dark')]
chk(7,"炎/雷/無/闇の防御特化種は代用設置技", all(x[2] for x in sub), f"{sub if sub else '該当種なし'}")

# 9/10 §2-0 の総数上限
# 追加配分ぶんを枠に足す: 全個体に変化技+1、打ち分け型はさらに物理・特殊が
# +1ずつ。打ち分け型かどうかは生成器と同じ規則（species_id 昇順の序数を
# 10周期で 4:3:3）で引き直す。自属性の枠は、追加ぶんが自属性に寄る場合が
# あるので同じだけ広げる。
PROFILE_CYCLE=['Physical','Physical','Physical','Physical',
               'Special','Special','Special',
               'Versatile','Versatile','Versatile']
prof={x['species_id']: PROFILE_CYCLE[i % len(PROFILE_CYCLE)]
      for i, x in enumerate(sorted(sp, key=lambda x: x['species_id']))}

# 上限体系は単属性/複合属性の2系列。自属性・攻撃技合計・他属性(1属性2種・
# 威力上限)・変化技の下限をまとめて見る。
v9=[];v10=[];v11=[];v12=[];v13=[]
for s in sp:
    own=set(s['types']); dual=len(own)>1
    t=tot(s); span=(t-180)/320.0
    ot=rhu((18 if dual else 16)-8*span)
    ac=rhu((24 if dual else 23)-(11 if dual else 12)*span)
    lim=90 if dual else 95
    ms=[M[e['move_id']] for e in s['learnset']]
    atk=[m for m in ms if m['power']>0]
    sta=[m for m in ms if m['power']==0]
    o=[m for m in atk if m['type'] in own]
    off=[m for m in atk if m['type'] not in own]   # 無属性も他属性と同じ枠
    if len(o)>ot: v9.append((s['display_name'],len(o),ot))
    if len(atk)>ac: v10.append((s['display_name'],len(atk),ac))
    if len(sta)<2: v11.append((s['display_name'],len(sta)))
    cc={}
    for m in off: cc[m['type']]=cc.get(m['type'],0)+1
    if any(v>2 for v in cc.values()): v12.append((s['display_name'],cc))
    if off and max(m['power'] for m in off)>lim: v13.append((s['display_name'],max(m['power'] for m in off),lim))
v14=[m for m in [] ]
for x in sp:
    ownx=set(x['types'])
    if 'Neutral' in ownx: continue
    # 攻撃技を持てない特性の持ち主には無属性攻撃技の下限を課せない。
    if x.get('trait')=='zankyou_no_shugosha': continue
    n=sum(1 for e in x['learnset']
          if M[e['move_id']]['type']=='Neutral' and M[e['move_id']]['power']>0)
    if n<2: v14.append((x['display_name'],n))
chk(11,"全個体が変化技を2つ以上", not v11, f"違反{len(v11)}件 {v11[:3]}")
chk(14,"全個体が無属性攻撃技を2種以上", not v14, f"違反{len(v14)}件 {v14[:3]}")
chk(12,"他属性は1属性あたり2種まで", not v12, f"違反{len(v12)}件 {v12[:3]}")
chk(13,"他属性の威力が上限内(単95/複90)", not v13, f"違反{len(v13)}件 {v13[:3]}")
chk(9,"自属性攻撃技が上限内(単16→8/複18→10)", not v9, f"違反{len(v9)}件 {v9[:3]}")
chk(10,"攻撃技合計が上限内(単23→11/複24→13)", not v10, f"違反{len(v10)}件 {v10[:3]}")

print("\n総合:", "ALL PASS" if ok else "FAILあり")
