#!/usr/bin/env python3
"""俊敏条件で両刀候補から物理攻撃/特殊攻撃へ再分類した種（offense_source == "speed"）の
learnset を、物理攻撃/特殊攻撃の learnset 規則に合うまで削る。

仕様書 docs/design/drafts/2026-10-26-stat-rework.md S1-e。**技は削るだけで、足さない・入れ替えない。**
最後に Data/npc_teams.json を見て、learnset から消えた技を持つ個体の技を置き換える。

  python3 Tools/build_habcds.py
  python3 Tools/conform_offense_learnsets.py     # 2回目の実行では何も削らない・置き換えない

違反が残る・NPC の置き換え先が無いときは非0で終了する。
"""
import json, math, os, sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from learnset_rules import (SIGNATURE, TRAIT_TAG, TRAIT_NAMED, CEIL, legacy_had)

SPECIES = os.path.join(ROOT, 'Data', 'species.json')
MOVES = os.path.join(ROOT, 'Data', 'moves.json')
TRAITS = os.path.join(ROOT, 'Data', 'traits.json')
NPC = os.path.join(ROOT, 'Data', 'npc_teams.json')

ADAPTED_MIN = 6


def rhu(x):
    return int(math.floor(x + 0.5))


def main():
    species = json.load(open(SPECIES, encoding='utf-8'))
    moves = json.load(open(MOVES, encoding='utf-8'))
    M = {m['id']: m for m in moves}
    traits = {t['id']: t for t in json.load(open(TRAITS, encoding='utf-8'))}

    def adapted(x):
        """verify_learnset.py の adapted(s) と同じ集合"""
        t = x['trait']
        if t in TRAIT_NAMED:
            return set(TRAIT_NAMED[t])
        if t in TRAIT_TAG:
            return {i for i, m in M.items() if m.get('weapon_tag') in TRAIT_TAG[t]}
        d = traits.get(t, {})
        if d.get('element') and d.get('template_kind') in ('power', 'stab', 'oshie'):
            return {i for i, m in M.items() if m['type'] == d['element'] and m['power'] > 0}
        return {i for i, m in M.items() if m['type'] in x['types'] and m['power'] > 0}

    def is_rs(mid):
        return mid.startswith('rs_')

    deleted = {}       # species_id -> [(move_id, tag)]
    errors = []

    for x in species:
        if x.get('offense_source') != 'speed':
            continue
        sid = x['species_id']
        new = x['offense']
        own = set(x['types'])
        dual = len(own) > 1
        C = CEIL[('dual' if dual else 'single', new)]
        h, a, d = legacy_had(x)
        span = (h + a + d - 180) / 320.0
        ot = rhu(C['own'][0] + (C['own'][1] - C['own'][0]) * span)
        ac = rhu(C['atk'][0] + (C['atk'][1] - C['atk'][0]) * span)
        per = C['off_per']
        ad = adapted(x)
        named = set(TRAIT_NAMED.get(x['trait'], []))
        ls = x['learnset']                       # 並び順は level, move_id のまま保つ
        ids = lambda: [e['move_id'] for e in ls]
        log = []

        def deletable(mid):
            m = M[mid]
            if m['power'] <= 0:
                return False
            if is_rs(mid) or mid in SIGNATURE or mid in named:
                return False
            if 'Neutral' not in own and m['type'] == 'Neutral':
                n_neu = sum(1 for i in ids() if M[i]['type'] == 'Neutral' and M[i]['power'] > 0 and not is_rs(i))
                if n_neu <= 2:
                    return False
            if len(ad) >= ADAPTED_MIN and mid in ad:
                n_ad = sum(1 for i in ids() if i in ad)
                if n_ad <= ADAPTED_MIN:
                    return False
            return True

        def key(mid):
            m = M[mid]
            return (m['category'] == new, m['power'], mid)

        def drop(mid, tag):
            ls[:] = [e for e in ls if e['move_id'] != mid]
            log.append((mid, tag))

        def attacks(exclude_adapted):
            out = []
            for i in ids():
                m = M[i]
                if m['power'] <= 0 or is_rs(i) or i in SIGNATURE:
                    continue
                if exclude_adapted and i in ad:
                    continue
                out.append(i)
            return out

        # 1. 少数側の威力
        mj = max((M[i]['power'] for i in attacks(False) if M[i]['category'] == new), default=0)
        for mid in [i for i in ids()]:
            m = M[mid]
            if m['power'] <= 0 or is_rs(mid) or mid in SIGNATURE:
                continue
            if m['category'] != new and m['power'] > 0.6 * mj + 1e-6 and deletable(mid):
                drop(mid, '')

        # 2. 自属性の数
        while True:
            o = [i for i in attacks(True) if M[i]['type'] in own]
            if len(o) <= ot:
                break
            cand = sorted([i for i in o if deletable(i)], key=key)
            if not cand:
                errors.append(f'{sid}: 自属性が {len(o)} > {ot} で削れる技が無い')
                break
            drop(cand[0], 'o')

        # 3. 他属性1属性あたり
        while True:
            off = [i for i in attacks(True) if M[i]['type'] not in own]
            cc = {}
            for i in off:
                cc[M[i]['type']] = cc.get(M[i]['type'], 0) + 1
            over = {t for t, n in cc.items() if n > per}
            if not over:
                break
            first = next(i for i in ids() if i in off and M[i]['type'] in over)
            t = M[first]['type']
            cand = sorted([i for i in off if M[i]['type'] == t and deletable(i)], key=key)
            if not cand:
                errors.append(f'{sid}: 他属性 {t} が {cc[t]} > {per} で削れる技が無い')
                break
            drop(cand[0], 'p')

        # 4. 攻撃技合計（rs_ を除く）
        while True:
            atk = [i for i in ids() if M[i]['power'] > 0 and not is_rs(i)]
            if len(atk) <= ac:
                break
            cand = sorted([i for i in atk if M[i]['type'] not in own and deletable(i)], key=key)
            if not cand:
                cand = sorted([i for i in atk if deletable(i)], key=key)
            if not cand:
                errors.append(f'{sid}: 攻撃技合計が {len(atk)} > {ac} で削れる技が無い')
                break
            drop(cand[0], 't')

        # 5. 検査（verify_learnset.py の #5・#9・#10・#12・#13・#14 と同じ規則）
        msx = [M[i] for i in ids()]
        atk_all = [m for m in msx if m['power'] > 0 and not is_rs(m['id'])]
        ex = ad | set(SIGNATURE)
        oo = [m for m in atk_all if m['type'] in own and m['id'] not in ex]
        ff = [m for m in atk_all if m['type'] not in own and m['id'] not in ex]
        if len(oo) > ot: errors.append(f'{sid}: #9 自属性 {len(oo)} > {ot}')
        if len(atk_all) > ac: errors.append(f'{sid}: #10 攻撃技合計 {len(atk_all)} > {ac}')
        ccx = {}
        for m in ff:
            ccx[m['type']] = ccx.get(m['type'], 0) + 1
        if any(v > per for v in ccx.values()): errors.append(f'{sid}: #12 他属性1属性あたり {ccx}')
        if ff and max(m['power'] for m in ff) > C['off_pow']:
            errors.append(f'{sid}: #13 他属性の威力 > {C["off_pow"]}')
        cats = [m for m in atk_all if m['id'] not in SIGNATURE]
        p = sum(1 for m in cats if m['category'] == 'Physical')
        q = sum(1 for m in cats if m['category'] == 'Special')
        major = 'Physical' if p >= q else 'Special'
        if major != new:
            errors.append(f'{sid}: #18 多数側 {major} が攻撃区分 {new} と不一致 ({p}:{q})')
        mjx = [m['power'] for m in cats if m['category'] == major]
        mnx = [m['power'] for m in cats if m['category'] != major]
        if mjx and mnx and max(mnx) > max(mjx) * 0.6 + 1e-6:
            errors.append(f'{sid}: #5 少数側の最高威力 {max(mnx)} > 多数側 {max(mjx)} の60%')
        if 'Neutral' not in own:
            n_neu = sum(1 for m in atk_all if m['type'] == 'Neutral')
            if n_neu < 2: errors.append(f'{sid}: #14 無属性攻撃技 {n_neu} < 2')
        n_st = sum(1 for m in msx if m['power'] == 0)
        if n_st < 2: errors.append(f'{sid}: #11 変化技 {n_st} < 2')
        if len(ad) >= ADAPTED_MIN and sum(1 for i in ids() if i in ad) < ADAPTED_MIN:
            errors.append(f'{sid}: #15 適応技が {ADAPTED_MIN} 未満')
        if log:
            deleted[sid] = (x['display_name'], log, p, q)

    if errors:
        print('conform NG:')
        for e in errors:
            print('  -', e)
        sys.exit(1)

    total = 0
    for sid, (nm, log, p, q) in deleted.items():
        total += len(log)
        desc = ', '.join(f'{mid}({M[mid]["category"][0]}{M[mid]["power"]}){("[" + tag + "]") if tag else ""}'
                         for mid, tag in log)
        print(f'{sid} {nm}: {len(log)}行 {desc} → {p}:{q}')
    by_step = {}
    for sid, (nm, log, p, q) in deleted.items():
        for mid, tag in log:
            by_step[tag or '1'] = by_step.get(tag or '1', 0) + 1
    print(f'削った行: {len(deleted)}種 {total}行 内訳 {by_step}')

    with open(SPECIES, 'w', encoding='utf-8') as f:
        json.dump(species, f, ensure_ascii=False, indent=2)
        f.write('\n')

    # NPC 構築の追従
    teams = json.load(open(NPC, encoding='utf-8'))
    by = {s['species_id']: s for s in species}
    replaced = 0
    for t in teams:
        for e in t['entries']:
            sp = by[e['species_id']]
            lsids = {l['move_id'] for l in sp['learnset']}
            for k, mid in enumerate(list(e['move_ids'])):
                if mid in lsids:
                    continue
                old = M[mid]
                off = sp['offense']
                mine = set(e['move_ids'])
                cands = [M[i] for i in lsids if i not in mine and M[i]['power'] > 0
                         and M[i]['type'] == old['type'] and M[i]['category'] == off]
                if not cands:
                    cands = [M[i] for i in lsids if i not in mine and M[i]['power'] > 0 and M[i]['category'] == off]
                if not cands:
                    print(f'NPC {t["id"]} {e["species_id"]}: {mid} の置き換え先が無い')
                    sys.exit(1)
                new_m = sorted(cands, key=lambda m: (-m['power'], m['id']))[0]
                print(f'NPC {t["id"]} {e["species_id"]}: {mid} -> {new_m["id"]}')
                e['move_ids'][k] = new_m['id']
                replaced += 1
    if replaced:
        with open(NPC, 'w', encoding='utf-8') as f:
            json.dump(teams, f, ensure_ascii=False, indent=2)
            f.write('\n')
    print(f'NPC の置き換え: {replaced}件')
    print('自己検査 OK')


if __name__ == '__main__':
    main()
