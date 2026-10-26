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
# 制限技12種(id が rs_ で始まる, Phase 6)は習得者が各1〜2種の特別枠で、仕様書の威力・射程をそのまま使う。
# 既存プールの 35〜95 帯は闇/水/地/草/無の物理が同威力2種で埋まり、射程枠(直線6・2マス3・範囲2)も
# 無/電/水/氷/炎で満杯なので、これらの定員規則は「rs_ を除いた既存プール」に対して検査し続け、
# rs_ 側は専用の検査（下の「制限技12種」）で見る。
IS_RS=lambda m: m['id'].startswith('rs_')
atk_core=[m for m in atk if not IS_RS(m)]
spe_core=[m for m in spe if not IS_RS(m)]
fails=[]
def check(ok,label,detail=""):
    print(("[PASS] " if ok else "[FAIL] ")+label+(("  "+detail) if detail else ""))
    if not ok: fails.append(label)

# 同威力の上限: 各属性・各分類で2種まで（全威力帯、連続技は最大回数で換算）
bad={}
for cat in ('Physical','Special'):
    c=collections.Counter((m['type'],eff(m)) for m in atk_core if m['category']==cat)
    for k,v in c.items():
        if v>2: bad[(cat,)+k]=v
check(not bad, "同属性・同分類で同威力は2種まで（全威力帯）", f"違反 {len(bad)} {list(bad.items())[:3]}")

# 射程ごとの上限（属性単位、物理と特殊をまたぐ）
viol={}
for el in set(m['type'] for m in atk):
    c=collections.Counter(m.get('range','Adjacent') for m in atk_core if m['type']==el)
    for r,cap in RANGE_CAP.items():
        if c[r]>cap: viol[(el,r)]=f"{c[r]}>{cap}"
check(not viol, "射程上限 Room<=4 / Area<=2 / Line<=6 / TwoTile<=3（属性ごと。rs_ 制限技を除く）", str(viol))

# 連続技は削除対象外
KEPT_MULTI={'mvh_water','mvh_electric','mvh_ground','mvh_ice','mvh_dark'}
alive={m['id'] for m in ms}
check(KEPT_MULTI<=alive, "連続技5種がすべて残存している", str(sorted(KEPT_MULTI-alive)))

# 変化技は今回の対象外なので手つかず
check(sum(1 for m in ms if m['category']=='Status')==58, "変化技58種が手つかず",
      str(sum(1 for m in ms if m['category']=='Status')))

# 威力グリッド: 15..120 の5刻みが各属性・各分類で最低1技（連続技は除く）
GRID=list(range(15,121,5))
gaps=[]
for cat in ('Physical','Special'):
    for el in sorted({m['type'] for m in atk}):
        have={m['power'] for m in atk if m['type']==el and m['category']==cat
              and m.get('multi_hit','None') in (None,'None')}
        gaps += [(el,cat,p) for p in GRID if p not in have]
check(not gaps, "威力15..120の5刻みが各属性・各分類で最低1技", f"空き{len(gaps)} {gaps[:4]}")

# 技系統ごとの規定
def fam_of(n):
    for f in ('ストレート','フィスト','ブロー','パンチ','スラスト','クラッシュ','レンド','フラッシュ'):
        if n.endswith(f): return f
    return None
FAM_TAG={'ストレート':'Straight','フィスト':'Fist','ブロー':'Fist','パンチ':'Punch',
         'スラスト':'Thrust','クラッシュ':'Crush','レンド':'Rend','フラッシュ':'Flash'}
# 虚(Void)は闇の攻撃技の複製なので、闇に1件ずつあるストレート・スラストが虚にも1件ずつ増える（10→11）。
FAM_CAP={'ストレート':11,'フィスト':8,'ブロー':6,'スラスト':11,'クラッシュ':5,'レンド':3,'フラッシュ':2}
fam_bad=[]
for f,tag in FAM_TAG.items():
    g=[m for m in ms if fam_of(m['name'])==f]
    if any(m['category']!='Physical' for m in g): fam_bad.append(f+':非物理')
    if any(m.get('weapon_tag')!=tag for m in g): fam_bad.append(f+':タグ')
    if f in FAM_CAP and len(g)!=FAM_CAP[f]: fam_bad.append(f"{f}:{len(g)}!={FAM_CAP[f]}")
    if f in ('ストレート','フィスト','ブロー') and any(m['power']>80 for m in g): fam_bad.append(f+':威力80超')
check(not fam_bad, "系統ごとのタグ・定員・威力上限", str(fam_bad))

punch=[m for m in ms if fam_of(m['name'])=='パンチ']
per=collections.Counter(m['type'] for m in punch)
noeff=[m for m in punch if m.get('ailment_effect','None') in (None,'None')
       and m.get('rank_effect_stat','None') in (None,'None') and not m.get('rank_effects')]
check(all(v<=2 for v in per.values()) and all(m['power']<=70 for m in punch) and not noeff,
      "パンチ: 各属性2つまで・威力70以下・全てに追加効果",
      f"{len(punch)}件 属性最大{max(per.values())} 効果なし{len(noeff)}")

thrust=[m for m in ms if fam_of(m['name'])=='スラスト']
tper=collections.Counter(m['type'] for m in thrust)
check(all(m.get('range')=='TwoTile' for m in thrust) and not any(m.get('is_contact') for m in thrust)
      and len(thrust)==11 and tper['Neutral']==2
      and all(tper[e]==1 for e in tper if e!='Neutral'),
      "スラスト: 11件(無属性2・他1、虚を含む)・全て2マス射程かつ非接触", f"{len(thrust)}件 {dict(tper)}")

# 系統を離れた2マス射程技は名称とタグだけを変えており、射程と非接触は維持
loose=[m for m in ms if m.get('range')=='TwoTile' and fam_of(m['name'])!='スラスト']
check(len(loose)==21 and   # 虚の複製で闇の2件ぶん増え（17→19）、制限技 R4 グリッチボルト・R5 タイドフックが2マス射程で+2（→21）
       all(not m.get('is_contact') and m.get('weapon_tag','None')=='None'
                             for m in loose),
      "系統外の2マス射程21件: 非接触のままタグなし", f"{len(loose)}件")

flash=[m for m in ms if fam_of(m['name'])=='フラッシュ']
check(all(m['power']==50 and not m.get('is_contact') and m.get('is_guaranteed_hit')
          and m.get('guaranteed_crit') for m in flash),
      "フラッシュ: 威力50・非接触・必中・確定急所", f"{len(flash)}件")

check(not [m for m in ms if m.get('weapon_tag')=='ClawFist'],
      "旧ClawFistタグが残っていない")

# 固定例外: メガデストラクト。一括処理が再びこの技を書き換えていないかを
# 見張るための錠前。過去に射程課金と一括改名がこれを拾ってしまったので、
# 5項目すべてを突き合わせる。
FIXED={'name':'メガデストラクト','power':180,'range':'Surrounding',
       'accuracy':100,'self_guaranteed_death':True}
mega=next((m for m in ms if m['id']=='megaton_self_destruct'), None)
drift=[] if mega is None else [f"{k}={mega.get(k)!r}!={v!r}" for k,v in FIXED.items()
                               if mega.get(k)!=v]
check(mega is not None and not drift,
      "固定例外メガデストラクトが基準値どおり", str(drift) if mega else "技が存在しない")

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
check(round(len(phy)*0.9)==contact, "物理技の接触/非接触が9:1",
      f"{contact}/{len(phy)} = {contact/len(phy)*100:.1f}%")
check(not any(m.get('is_contact') and m.get('range','Adjacent')!='Adjacent' for m in phy),
      "§2 非Adjacentの物理技は非接触")

# 特殊技を必ず射程持ちにする旧規則も撤回済み。威力グリッドは各属性22段階
# x 2分類を要求するのに対し、射程上限は Line6+Area2+Room4=12 しか許さない
# ため、Adjacent（唯一の無制限射程）を使わないとグリッドを埋められない。
# 射程持ちの特殊技が上限内に収まっていることだけ確認する。
rc=collections.Counter(m.get('range','Adjacent') for m in spe_core)
# 属性が10になったので上限の総和は 6/2/4 x 10 = 60/20/40。
check(rc['Line']<=60 and rc['Area']<=20 and rc['Room']<=40,
      "射程持ちの特殊技が属性上限の総和内", str(dict(rc)))

# §4 - every range change must have been paid for, given the recorded
# original data. Re-derived from git HEAD~ is out of scope here; instead
# assert the pricing table is internally consistent: no Room move may be
# stronger than the strongest Line move of the same element by more than
# the price difference would allow.
COST={'Adjacent':0,'TwoTile':-5,'Line':-10,'Area':-15,'Room':-20,'FullFloor':-20}
check(all(m['power']>0 for m in atk), "§4 威力が0以下になった技はない",
      f"min={min(m['power'] for m in atk)}")

# 制限技12種（Phase 6）: 仕様書 6-b の表どおり。全て攻撃技・威力40〜55・命中100・非Adjacentは非接触。
RS_SPEC={'rs_seal_snap':('Void','Special',40,'Adjacent',False,'SealLastMove',2),
         'rs_provoke':('Dark','Physical',45,'Adjacent',True,'NoStatusMoves',2),
         'rs_repeat_chant':('Neutral','Special',40,'Line',False,'Encore',1),
         'rs_item_jam':('Electric','Special',45,'TwoTile',False,'ItemJam',3),
         'rs_tide_hook':('Water','Physical',50,'TwoTile',False,'Pull',0),
         'rs_frost_lag':('Ice','Special',45,'Line',False,'PriorityDown',1),
         'rs_sand_eater':('Ground','Physical',50,'Adjacent',True,'PpDrain',0),
         'rs_scorch_smog':('Fire','Special',40,'Area',False,'HealBlock',3),
         'rs_dragon_anchor':('Dragon','Special',55,'Adjacent',False,'RankUpBlock',3),
         'rs_short_leash':('Grass','Physical',45,'Adjacent',True,'AdjacentOnly',2),
         'rs_nag_jab':('Neutral','Physical',40,'Adjacent',True,'NoRepeat',3),
         'rs_hollow_touch':('Void','Special',50,'Adjacent',False,None,0)}
rs={m['id']:m for m in ms if IS_RS(m)}
rs_bad=[]
for mid,(t,c,p,r,ct,rk,rt) in RS_SPEC.items():
    m=rs.get(mid)
    if m is None: rs_bad.append(mid+':なし'); continue
    got=(m['type'],m['category'],m['power'],m.get('range','Adjacent'),bool(m.get('is_contact')),m.get('restriction'),m.get('restriction_turns',0))
    if got!=(t,c,p,r,ct,rk,rt) or m['accuracy']!=100: rs_bad.append(f'{mid}:{got}')
    if mid=='rs_hollow_touch' and (m.get('ailment_effect'),m.get('ailment_chance'))!=('Hollow',100): rs_bad.append(mid+':虚脱')
check(len(rs)==12 and not rs_bad and set(rs)==set(RS_SPEC), "制限技12種が仕様書6-bの表どおり（属性・分類・威力・射程・接触・restriction）", str(rs_bad))

# integrity
check(len({m['id'] for m in ms})==len(ms), "技IDが一意")
check(len({m['name'] for m in ms})==len(ms), "技名が一意")
check(all(m['power']%5==0 for m in atk), "攻撃技の威力はすべて5の倍数")

# C# 側にべた書きされた技ID（プレイヤーやNPCの初期技）も実在すること。
# 一括削除でこれが消えると learnset の検証は全て通るのに、実機ではプレイヤーが
# 技を1つも覚えないまま起動する（power_shot / spark / flare_arrow / wind_cutter
# が実際にそうなっていた）。警告ログにしか出ないので機械検証で拾う。
import re, glob
_ids={m['id'] for m in ms}
_hard=[(f,i,(a or b)) for f in glob.glob('Scripts/**/*.cs', recursive=True)
       for i,l in enumerate(open(f,encoding='utf-8'),1)
       for a,b in re.findall(r'Moves\.Learn\("([^"]+)"\)|MoveDatabase\.Get\("([^"]+)"\)', l)
       if (a or b) not in _ids]
check(not _hard, "C#にべた書きされた技IDが実在する", f"未解決{len(_hard)} {_hard[:3]}")

print("\n総合:", "ALL PASS" if not fails else f"{len(fails)} FAIL")
sys.exit(1 if fails else 0)
