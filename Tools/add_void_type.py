#!/usr/bin/env python3
"""虚(Void)属性の導入 (仕様書 docs/design/drafts/2026-10-25-void-type-and-habcds.md Phase 1)。

やること（何度実行しても同じ結果になる）:
  1-a Data/type_chart.json を10属性・10x10にする（既存81マスは変えない）
  1-c Data/species.json: 選定規則で30種を虚にする（二重属性は並び順を保つ）
  1-d Data/moves.json: 闇の攻撃技57個を複製して void_ 技にする（既存の void_ 技は作り直す）
  1-e 虚にした30種の learnset を差し替える（他の個体は変えない）

選定規則の出力は仕様書の表（EXPECTED_VOID）と一致することを assert する。
"""
import json, os, sys, collections

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from learnset_rules import SIGNATURE, profile_of

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
D = lambda n: os.path.join(ROOT, 'Data', n)

TYPE_CHART = '''{
  "types": ["Neutral", "Fire", "Water", "Grass", "Electric", "Ground", "Ice", "Dragon", "Dark", "Void"],
  "matrix": [
    [1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 0.5, 2.0],
    [1.0, 1.0, 0.5, 2.0, 1.0, 1.0, 2.0, 1.0, 1.0, 0.5],
    [1.0, 2.0, 1.0, 1.0, 0.5, 1.0, 1.0, 1.0, 1.0, 1.0],
    [1.0, 0.5, 1.0, 1.0, 1.0, 2.0, 1.0, 1.0, 1.0, 1.0],
    [1.0, 1.0, 2.0, 1.0, 1.0, 0.5, 1.0, 1.0, 1.0, 1.0],
    [1.0, 1.0, 1.0, 0.5, 2.0, 1.0, 1.0, 1.0, 1.0, 1.0],
    [1.0, 0.5, 1.0, 1.0, 1.0, 1.0, 1.0, 2.0, 1.0, 2.0],
    [1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 0.5, 1.0, 2.0, 1.0],
    [2.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 0.5, 1.0, 0.5],
    [0.5, 2.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0]
  ]
}
'''

# 仕様書 1-c の表（検算用）
EXPECTED_VOID = set('''016 022 027 034B 056 057 073 091B 111B 130 145 150 157B 166 189
030 041 041B 064 078B 079B 084 084B 099 115 138B 147 196
047 055'''.split())

# 回帰スイートの試合で使う8種（除外）
EXCLUDE_NAMES = {'モコロン', 'クルリス', 'タマモ', 'ブシガエル', 'カモノスケ', 'ミルフィー', 'ルミカイト', 'タマコッコ'}
# 闇に紐づく特性（テンプレート以外）
DARK_TRAITS_EXTRA = {'akui', 'purple_haze', 'haze', 'jiyuu_no_tsubasa', 'akumu_no_hitomi'}

# 技名の先頭語の置換表（長いものから当てる）
NAME_MAP = [('ダークネス', 'ホロウネス'), ('ダーク', 'ホロウ'), ('シャドウ', 'ヴォイド'),
            ('ブラック', 'ブランク'), ('カース', 'エンプティ'), ('アビス', 'ウロ'),
            ('ナイトメア', 'ネヴァー'), ('ハデス', 'ヌル'), ('ダスク', 'ヌル')]
NAME_MAP.sort(key=lambda p: -len(p[0]))


def load(n):
    return json.load(open(D(n), encoding='utf-8'))


def dump(n, obj):
    with open(D(n), 'w', encoding='utf-8') as f:
        f.write(json.dumps(obj, indent=2, ensure_ascii=False) + '\n')


def bst3(x):
    return x['base_hp'] + x['base_atk'] + x['base_def']


def spaced(cands, k):
    """候補を (BST3, species_id) 昇順に並べ、等間隔に k 個抽出。「ゼノ」は必ず入れる。"""
    c = sorted(cands, key=lambda x: (bst3(x), x['species_id']))
    forced = [x for x in c if 'ゼノ' in x['display_name']][:k]
    rest = [x for x in c if x not in forced]
    m = k - len(forced)
    picked = [rest[int((i + 0.5) * len(rest) / m)] for i in range(m)] if m > 0 else []
    return forced + picked


def candidate_filters(species, traits):
    dark_tr = {k for k, v in traits.items() if v.get('element') == 'Dark'} | DARK_TRAITS_EXTRA
    water_tr = {k for k, v in traits.items() if v.get('element') == 'Water'}
    excl = {x['species_id'] for x in species if x['display_name'] in EXCLUDE_NAMES}

    def ok(x, tt):
        return not x.get('is_legendary') and x['species_id'] not in excl and x['trait'] not in tt
    return ok, dark_tr, water_tr


# 水→虚の2種。それ以外の虚は元が闇。
WATER_SIDE = ('047', '055')


def main():
    species = load('species.json')
    moves = load('moves.json')
    traits = {t['id']: t for t in load('traits.json')}

    # --- 元の属性へ戻す（冪等性）。虚の個体は、闇側(28種)は Void→Dark、水側(2種)は Void→Water。
    for x in species:
        if x['species_id'] in EXPECTED_VOID:
            back = 'Water' if x['species_id'] in WATER_SIDE else 'Dark'
            x['types'] = [back if t == 'Void' else t for t in x['types']]
            if back == 'Dark':   # 闇側は void_X → X へ戻す（水側は X が水技でないので戻せず、そのまま残す。
                for e in x['learnset']:   # 水側は再実行時に水の攻撃技が無く、差し替えは何も起きない）
                    if e['move_id'].startswith('void_'):
                        e['move_id'] = e['move_id'][len('void_'):]
    moves = [m for m in moves if not m['id'].startswith('void_')]

    # --- 1-c 選定
    ok, dark_tr, water_tr = candidate_filters(species, traits)
    chosen = []
    singles = [x for x in species if x['types'] == ['Dark'] and ok(x, dark_tr)]
    chosen += spaced(singles, 15)
    duals = [x for x in species if 'Dark' in x['types'] and len(x['types']) == 2]
    by_partner = collections.defaultdict(list)
    for x in duals:
        by_partner[[t for t in x['types'] if t != 'Dark'][0]].append(x)
    for p in sorted(by_partner):
        lst = by_partner[p]
        k = int(len(lst) * 0.4 + 0.5)
        cands = [x for x in lst if ok(x, dark_tr)]
        k = min(k, len(cands))
        if k:
            chosen += spaced(cands, k)
    chosen_dark = chosen
    waters = [x for x in species if x['types'] == ['Water'] and ok(x, water_tr)]
    chosen_water = spaced(waters, 2)
    # spaced() は「ゼノ」強制を持つが水には該当しない
    ids = {x['species_id'] for x in chosen_dark} | {x['species_id'] for x in chosen_water}
    assert ids == EXPECTED_VOID, (sorted(ids ^ EXPECTED_VOID))
    assert len(chosen_dark) == 28 and len(chosen_water) == 2

    # --- 1-d 虚技の生成
    dark_atk = [m for m in moves if m['type'] == 'Dark' and m['power'] > 0]
    assert len(dark_atk) == 57, len(dark_atk)
    names = {m['name'] for m in moves}
    void_moves = []
    for m in dark_atk:
        v = dict(m)
        v['id'] = 'void_' + m['id']
        v['type'] = 'Void'
        nm = m['name']
        for a, b in NAME_MAP:
            if nm.startswith(a):
                nm = b + nm[len(a):]
                break
        else:
            raise SystemExit(f'置換表に当たらない技名: {nm}')
        if nm in names:
            nm += '・ウロ'
        assert nm not in names, nm
        names.add(nm)
        v['name'] = nm
        # ailment_effect: 闇の暗闇宣言は虚では虚脱宣言(Hollow)に変える（Phase 2）。Poison/Toxic はそのまま。
        if v.get('ailment_effect') == 'Darkness':
            v['ailment_effect'] = 'Hollow'
        void_moves.append(v)
    moves = moves + void_moves
    M = {m['id']: m for m in moves}

    # --- 1-c 属性の書き換え / 1-e learnset 差し替え
    for x in chosen_dark:
        x['types'] = ['Void' if t == 'Dark' else t for t in x['types']]
        for e in x['learnset']:
            m = M[e['move_id']]
            if m['type'] == 'Dark' and m['power'] > 0:
                e['move_id'] = 'void_' + e['move_id']
    voids = [m for m in void_moves]
    sig_ids = set(SIGNATURE)
    order = {x['species_id']: i for i, x in enumerate(sorted(species, key=lambda y: y['species_id']))}
    for x in chosen_water:
        x['types'] = ['Void' if t == 'Water' else t for t in x['types']]
        used = set()
        replaced = []   # (learnset entry, 差し替え前の水技)
        for e in x['learnset']:
            m = M[e['move_id']]
            if m['type'] == 'Water' and m['power'] > 0:
                cand = [v for v in voids if v['category'] == m['category'] and v['range'] == m['range']
                        and v['id'] not in used]
                cand.sort(key=lambda v: (abs(v['power'] - m['power']), v['power'], v['id']))
                assert cand, (x['species_id'], m['id'])
                e['move_id'] = cand[0]['id']
                used.add(cand[0]['id'])
                replaced.append((e, m))
        # 仕様書の規則どおりに差し替えると verify_learnset #5（少数側の最高威力 <= 多数側の
        # 60%、打ち分け型は90%）を破る個体がある（047 リオリネ: 特殊の最高が 125→100 に落ち、
        # 物理 75 が上限 60 を超える）。その場合に限り、少数側で上限を超えた差し替え先を
        # 「上限以下で最も近い」虚技へ選び直す（仕様書に無い補正。最終報告に記載）。
        atk = [M[e['move_id']] for e in x['learnset']
               if M[e['move_id']]['power'] > 0 and e['move_id'] not in sig_ids]
        pn = sum(1 for m in atk if m['category'] == 'Physical')
        sn = sum(1 for m in atk if m['category'] == 'Special')
        major = 'Physical' if pn >= sn else 'Special'
        r = 0.9 if profile_of(x['trait'], order[x['species_id']])[0] == 'Versatile' else 0.6
        mj = max((m['power'] for m in atk if m['category'] == major), default=0)
        limit = mj * r + 1e-6
        for e, orig in replaced:
            cur = M[e['move_id']]
            if cur['category'] != major and cur['power'] > limit:
                used.discard(cur['id'])
                cand = [v for v in voids if v['category'] == orig['category'] and v['range'] == orig['range']
                        and v['id'] not in used and v['power'] <= limit]
                cand.sort(key=lambda v: (abs(v['power'] - orig['power']), v['power'], v['id']))
                assert cand, (x['species_id'], orig['id'])
                e['move_id'] = cand[0]['id']
                used.add(cand[0]['id'])

    with open(D('type_chart.json'), 'w', encoding='utf-8') as f:
        f.write(TYPE_CHART)
    dump('species.json', species)
    dump('moves.json', moves)

    # --- NPC 構築: 虚にした個体が持っていた闇技は learnset から消えるので、対応する虚技へ置き換える
    #     （仕様書に記述なし。verify_npc_teams「技はすべて learnset 内」を保つための最小の追従）
    teams = load('npc_teams.json')
    learn = {x['species_id']: {e['move_id'] for e in x['learnset']} for x in species}
    swapped = 0
    for t in teams:
        for e in t['entries']:
            ls = learn.get(e['species_id'])
            if ls is None:
                continue
            for i, mid in enumerate(e['move_ids']):
                if mid not in ls and 'void_' + mid in ls:
                    e['move_ids'][i] = 'void_' + mid
                    swapped += 1
    dump('npc_teams.json', teams)
    print('npc_teams.json の技を虚技へ置換', swapped, '件')

    c = collections.Counter(t for x in species for t in x['types'])
    print('虚の個体', len(ids), '/ 個体数', dict(c.most_common()))
    print('虚技', len(void_moves))


if __name__ == '__main__':
    main()
