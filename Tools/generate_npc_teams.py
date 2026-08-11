#!/usr/bin/env python3
"""NPC対戦相手の編成を生成する。

マッチング（相手を探す仕組み）はまだ実装できる段階にないので、対戦の相手は
NPCが務める。その6匹・技4つ・持ち物1つを、対戦仕様の構築規則を満たす形で
ここで作り、Data/npc_teams.json へ落とす。

**手で書かない理由。** 1体につき技4つを選ぶので、8人ぶんで6x4x8=192件になる。
手書きでは learnset 外の技を混ぜる事故が必ず起きるし、learnset を作り直す
たびに全部が腐る。生成すれば規則違反はそもそも作れない。

構築規則（BattleTeam.Validate と同じもの）:
  - 6匹ちょうど、同一種族の重複なし
  - 各自 learnset 内の技ちょうど4つ
  - 持ち物は1匹1つ、チーム内で重複なし

生成の方針:
  - 相手ごとに主属性を決め、その属性を持つ種から6匹を選ぶ
  - 技は「自属性の最大威力 → 他属性で打点の重ならないもの → 残りの威力順」
  - 持ち物はチーム内で重複しないよう、役割（耐久/火力/補助）から配る

  python3 Tools/generate_npc_teams.py
"""

import json
import os

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DATA = os.path.join(ROOT, 'Data')

MOVES_PER_PAL = 4
ROSTER_SIZE = 6

# 1人目と8人目が狙う1匹あたりの合計種族値。初期のパルが BST210〜265 なので、
# 下端はそこへ合わせる。上端は最上位帯。
TARGET_BST_MIN = 230
TARGET_BST_MAX = 380

# 相手8人。主属性と、その相手の性格づけになる持ち物の並び。
# 持ち物はチーム内で重複しない（規則）ので、6匹ぶんをそのまま順に配る。
OPPONENTS = [
    ('npc_fire',     'ほのおつかい コウ',   'Fire',
     ['power_lens', 'iron_plate', 'regen_band', 'guard_tonic_50', 'focus_lens', 'endure_charm']),
    ('npc_water',    'みずつかい ナギ',     'Water',
     ['focus_lens', 'mind_plate', 'guard_tonic_25', 'purge_band', 'power_lens', 'rank_anchor']),
    ('npc_grass',    'くさつかい モエ',     'Grass',
     ['regen_band', 'iron_plate', 'cure_bell', 'focus_lens', 'guard_tonic_25', 'wide_ward']),
    ('npc_electric', 'でんきつかい ライ',   'Electric',
     ['power_lens', 'focus_lens', 'crit_shell', 'guard_tonic_50', 'mind_plate', 'regen_band']),
    ('npc_ice',      'こおりつかい ユキ',   'Ice',
     ['mind_plate', 'focus_lens', 'weakness_shell', 'purge_band', 'iron_plate', 'guard_tonic_25']),
    ('npc_ground',   'じめんつかい ダイ',   'Ground',
     ['iron_plate', 'power_lens', 'endure_charm', 'room_mirror', 'regen_band', 'guard_tonic_50']),
    ('npc_dark',     'あくつかい クロ',     'Dark',
     ['power_lens', 'crit_shell', 'focus_lens', 'cure_bell', 'area_aegis', 'mind_plate']),
    ('npc_dragon',   'ドラゴンつかい リュウ', 'Dragon',
     ['power_lens', 'focus_lens', 'iron_plate', 'mind_plate', 'guard_tonic_50', 'endure_charm']),
]

# 攻撃技を1つも持たない特性。持ち主は対戦相手として成立しないので外す。
NO_ATTACK_TRAITS = {'zankyou_no_shugosha'}


def load(name):
    with open(os.path.join(DATA, name), encoding='utf-8') as f:
        return json.load(f)


def bst(sp):
    return sp['base_hp'] + sp['base_atk'] + sp['base_def']


def pick_moves(sp, moves):
    """1匹ぶんの技4つ。自属性の主力 → 打点の重ならない他属性 → 威力順。"""
    ids = []
    for entry in sp['learnset']:                 # learnset は重複を含む
        if entry['move_id'] not in ids:
            ids.append(entry['move_id'])
    known = [moves[i] for i in ids if i in moves]

    attacks = sorted([m for m in known if m['power'] > 0],
                     key=lambda m: -m['power'])
    own = set(sp['types'])

    chosen, covered = [], set()

    # ① 自属性の最大威力（タイプ一致で伸びるので、これが主力になる）
    for m in attacks:
        if m['type'] in own:
            chosen.append(m)
            covered.add(m['type'])
            break

    # ② 打点の重ならない他属性を威力順に。技だけ強くても通らない相手が
    #    出るので、種類を散らすほうを優先する。
    for m in attacks:
        if len(chosen) >= MOVES_PER_PAL:
            break
        if m in chosen or m['type'] in covered:
            continue
        chosen.append(m)
        covered.add(m['type'])

    # ③ それでも埋まらなければ威力順、最後に変化技で埋める。
    for pool in (attacks, known):
        for m in pool:
            if len(chosen) >= MOVES_PER_PAL:
                break
            if m not in chosen:
                chosen.append(m)

    return [m['id'] for m in chosen[:MOVES_PER_PAL]]


def build(species, moves, main_type, items, used_species, target_bst):
    """主属性を持つ種から6匹。狙った種族値帯の周りから採る。

    強い順に採ると、いちばん弱い相手でさえ平均BST350超になり、初戦から
    手が出ない。相手ごとに狙う種族値を変え、8人で下から上への階段にする。
    """
    pool = [s for s in species
            if main_type in s['types']
            and s['trait'] not in NO_ATTACK_TRAITS
            and s['species_id'] not in used_species
            and sum(1 for e in {e['move_id'] for e in s['learnset']}
                    if moves.get(e, {}).get('power', 0) > 0) >= MOVES_PER_PAL]
    pool.sort(key=lambda s: (bst(s), s['species_id']))

    # 狙った種族値にいちばん近いところを中心に、その前後から等間隔で6匹。
    # 属性ごとに層の厚さが違うので、窓が端に寄ったら内側へ寄せ直す。
    center = min(range(len(pool)), key=lambda i: abs(bst(pool[i]) - target_bst)) if pool else 0
    window = ROSTER_SIZE * 2
    lo = max(0, min(center - window // 2, len(pool) - window))
    picked = pool[lo:lo + window][::2][:ROSTER_SIZE]
    if len(picked) < ROSTER_SIZE:                # 層が薄い属性の保険
        picked = pool[max(0, center - ROSTER_SIZE):][:ROSTER_SIZE] or pool[:ROSTER_SIZE]

    entries = []
    for i, sp in enumerate(picked):
        entries.append({
            'species_id': sp['species_id'],
            'move_ids': pick_moves(sp, moves),
            'item_id': items[i],
        })
    return picked, entries


def main():
    species = load('species.json')
    moves = {m['id']: m for m in load('moves.json')}
    items = {i['id']: i for i in load('items.json')}

    held = {i['id'] for i in items.values() if i['type'] == 'BattleHeld'}

    teams, used = [], set()
    for rank, (npc_id, name, main_type, item_ids) in enumerate(OPPONENTS):
        missing = [i for i in item_ids if i not in held]
        if missing:
            raise SystemExit(f'{npc_id}: 対戦用の持ち物ではない {missing}')

        # 1人目は手持ちの初期パル（BST210〜265）と噛み合う帯から、
        # 8人目は最上位帯から。等間隔に上げて階段にする。
        target = TARGET_BST_MIN + rank * (TARGET_BST_MAX - TARGET_BST_MIN) // (len(OPPONENTS) - 1)

        # 同じ種族が別の相手に出てもよいが、続けて同じ顔が並ぶと
        # 相手ごとの個性が薄まる。使った種は次の相手では避ける。
        picked, entries = build(species, moves, main_type, item_ids, used, target)
        used.update(s['species_id'] for s in picked)

        teams.append({
            'id': npc_id,
            'name': name,
            'main_type': main_type,
            'total_bst': sum(bst(s) for s in picked),
            'entries': entries,
        })

    # 合計種族値の低い順＝おおよその弱い順に並べる。選択画面はこの順で出す。
    teams.sort(key=lambda t: t['total_bst'])

    out = os.path.join(DATA, 'npc_teams.json')
    with open(out, 'w', encoding='utf-8') as f:
        json.dump(teams, f, ensure_ascii=False, indent=2)
        f.write('\n')

    print(f'{len(teams)}人ぶんを書き出した → {out}')
    names = {s['species_id']: s['display_name'] for s in species}
    for t in teams:
        who = '/'.join(names[e['species_id']] for e in t['entries'])
        print(f"  {t['name']:<22} {t['main_type']:<9} 合計BST{t['total_bst']:>5}  {who}")


if __name__ == '__main__':
    main()
