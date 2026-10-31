#!/usr/bin/env python3
"""species.json に HABCDS（H/A/B/C/D/S）・攻撃区分・耐久区分・役割・BST6 を書き込む。

仕様書 docs/design/drafts/2026-10-26-stat-rework.md §3 Phase S1（S1-a〜S1-d・S1-h）。

  python3 Tools/build_habcds.py

入力: Data/species.json（旧HAD。legacy_had があればそれを優先）、
      Data/source/speed_fixed.json（Tools/plan_speed.py --fixed）、Data/source/offense_prev.json（改修前の攻撃区分。初回に凍結）。
      両刀12種の選定は Tools/assign_mixed_traits.py の select_mixed12（仕様書 2026-10-30-speed-and-stat-redo.md）。
出力: Data/species.json を書き換える。何度実行しても結果は同じ（冪等）。
      旧HAD は初回に legacy_had へ退避する。

計算はすべて fractions.Fraction で行い、四捨五入は floor(x + 1/2)（x.5 は切り上げ）。
"""
import json, os, sys
from fractions import Fraction as F
from math import floor, ceil, sqrt
from statistics import median

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from learnset_rules import (defense_of, mixed_side_of, OFFENSE_JA, DEFENSE_JA)

SPECIES = os.path.join(ROOT, 'Data', 'species.json')
MOVES = os.path.join(ROOT, 'Data', 'moves.json')
SPEED = os.path.join(ROOT, 'Data', 'source', 'speed_fixed.json')      # 確定表（Tools/plan_speed.py --fixed）
PREV = os.path.join(ROOT, 'Data', 'source', 'offense_prev.json')      # 2026-10-30 改修前の攻撃区分（初回に凍結）
S_MIN, S_MAX = 20, 130

# S1-d: 配分の比
BETA = {'Physical': F(13, 20), 'Special': F(7, 20), 'Both': F(1, 2)}    # B/(B+D)
ALPHA = {'Physical': F(7, 10), 'Special': F(3, 10)}                      # A/(A+C)
ROLES = [(o, d) for o in ('Physical', 'Special', 'Mixed') for d in ('Physical', 'Special', 'Both')]


def rnd(x):
    """四捨五入（x.5 は切り上げ）"""
    return floor(F(x) + F(1, 2))


def bst6_of(bst3):
    """S1-a: round(320 + (BST3-180)*290/320) = 320 + floor(((BST3-180)*58 + 32)/64)"""
    return 320 + floor((F(bst3 - 180) * 58 + 32) / 64)


MIXED_AC_MIN = 160           # 両刀: A+C の下限
MIXED_H_MIN = 55             # 両刀: H の下限
MIXED_SHARE = F(11, 20)      # 両刀: 主側の目標割合（|A-C|>=10 と 4:6〜6:4 の範囲に丸める）
FLOOR_EACH, FLOOR_PAIR = 20, 49   # 全員: 各能力>=20、最小2能力の和>=49


def floor_fix(st, valid=None):
    """st = {'H','A','B','C','D','S'}。下限規則（各>=20・最小2つの和>=49）を満たすまで、最大の非S能力から
    最小の非S能力へ1点ずつ移す（同値は H,A,B,C,D の順で先）。S は動かさない。戻り値 移した点数。
    valid（両面耐久の H=0.8(B+D) 等）が与えられたときは、移した後も valid を満たす中で最大の能力を移し元にする。"""
    moved = 0
    keys = ['H', 'A', 'B', 'C', 'D']
    for _ in range(1000):
        v = sorted(st.values())
        if v[0] >= FLOOR_EACH and v[0] + v[1] >= FLOOR_PAIR:
            return moved
        dst = min(keys, key=lambda k: (st[k], keys.index(k)))
        srcs = sorted(keys, key=lambda k: (-st[k], keys.index(k)))
        src = srcs[0]
        if valid:
            for k in srcs:
                if k == dst or st[k] <= st[dst]:
                    continue
                t = dict(st)
                t[k] -= 1
                t[dst] += 1
                if valid(t):
                    src = k
                    break
        if src == dst or st[src] <= st[dst]:
            return -1
        st[src] -= 1
        st[dst] += 1
        moved += 1
    return -1


def mixed_ok(H, A, B, C, D, defense):
    return (A + C >= MIXED_AC_MIN and 10 * (A + C) > 13 * (B + D) and abs(A - C) >= 10
            and 5 * min(A, C) >= 2 * (A + C) and H >= MIXED_H_MIN and defense != 'Both')


def both_ok(H, B, D):
    return abs(H - rnd(F(4, 5) * (B + D))) <= 1 and 100 * min(B, D) >= 41 * (B + D)


def compute(h, a, d, offense, defense, S, index, b6):
    """配分。h,a,d は旧HAD、b6 は BST6、S は確定表の値。
    戻り値 (H, A, B, C, D, S, w, moved)。moved は下限規則で移した点数（-1 は直せなかった）。"""
    b3 = h + a + d
    T = b6 - S
    W = F(76, 100) - F(30, 100) * (F(S) - 35) / 95
    w = min(F(80, 100), max(F(45, 100), W + (F(h + d, b3) - F(2, 3))))
    Bulk = rnd(T * w)
    Off = T - Bulk
    if defense == 'Both':
        # H = 0.8(B+D) を目指す: B+D = Bulk/1.8、B = D（B:D = 1:1。41:59〜59:41 に収める）
        G = rnd(Bulk * F(5, 9))
        H = Bulk - G
        B = min(floor(F(59, 100) * G), max(ceil(F(41, 100) * G), rnd(G * F(1, 2))))
        D = G - B
    else:
        H = rnd(Bulk * F(h, h + 2 * d))
        G = Bulk - H
        if offense == 'Mixed':
            if H < MIXED_H_MIN:
                G -= MIXED_H_MIN - H
                H = MIXED_H_MIN
            # A+C>=160 かつ A+C>1.3(B+D) になるまで Bulk から削って Off へ回す（H は 55 未満にしない）
            while Off < MIXED_AC_MIN or 10 * Off <= 13 * G:
                if H > MIXED_H_MIN and (H - MIXED_H_MIN >= G - 2 * FLOOR_EACH or G <= 2 * FLOOR_EACH):
                    H -= 1
                elif G > 2 * FLOOR_EACH:
                    G -= 1
                else:
                    break
                Off += 1
        B = rnd(G * BETA[defense])
        D = G - B
    if offense == 'Mixed':
        main = mixed_side_of(index)
        X = rnd(Off * MIXED_SHARE)
        X = max(ceil(F(Off + 10, 2)), min(floor(F(3, 5) * Off), X))
        A, C = (X, Off - X) if main == 'Physical' else (Off - X, X)
    else:
        A = rnd(Off * ALPHA[offense])
        C = Off - A
    st = dict(H=H, A=A, B=B, C=C, D=D, S=S)
    valid = (lambda t: both_ok(t['H'], t['B'], t['D'])) if defense == 'Both' else None
    moved = floor_fix(st, valid)
    return st['H'], st['A'], st['B'], st['C'], st['D'], S, w, moved


def had_of(s):
    return s['legacy_had'] if 'legacy_had' in s else {'hp': s['base_hp'], 'atk': s['base_atk'], 'def': s['base_def']}


def pearson(xs, ys):
    n = len(xs)
    mx, my = sum(xs) / n, sum(ys) / n
    sxy = sum((x - mx) * (y - my) for x, y in zip(xs, ys))
    sxx = sum((x - mx) ** 2 for x in xs)
    syy = sum((y - my) ** 2 for y in ys)
    return sxy / sqrt(sxx * syy)


def partial(xs, ys, zs):
    rxy, rxz, ryz = pearson(xs, ys), pearson(xs, zs), pearson(ys, zs)
    return (rxy - rxz * ryz) / sqrt((1 - rxz ** 2) * (1 - ryz ** 2))


def load_prev(species):
    """改修前の攻撃区分。無ければ現在の species.json から作って凍結する（以後は再実行しても変わらない）。"""
    if not os.path.exists(PREV):
        prev = {s['species_id']: {'offense': s['offense'], 'offense_source': s['offense_source'],
                                  'a_ge_c': s['base_atk'] >= s['base_spatk']} for s in species}
        with open(PREV, 'w', encoding='utf-8') as f:
            json.dump(prev, f, ensure_ascii=False, indent=1)
            f.write('\n')
    return json.load(open(PREV, encoding='utf-8'))


def main():
    species = json.load(open(SPECIES, encoding='utf-8'))
    speed = json.load(open(SPEED, encoding='utf-8'))
    prev = load_prev(species)
    order = {s['species_id']: i for i, s in enumerate(sorted(species, key=lambda s: s['species_id']))}
    import assign_mixed_traits as AMT
    mixed12 = AMT.select_mixed12(species)

    table = {}
    for s in species:
        sid = s['species_id']
        if sid not in speed:
            sys.exit(f'speed_fixed.json に無い: {sid}')
        had = had_of(s)
        table[sid] = (bst6_of(had['hp'] + had['atk'] + had['def']), speed[sid])

    infos = {}
    out = []
    for s in species:
        sid = s['species_id']
        idx = order[sid]
        had = had_of(s)
        h, a, d = had['hp'], had['atk'], had['def']
        pv = prev[sid]
        if sid in mixed12:
            offense, osrc = 'Mixed', 'mixed12'
        elif pv['offense'] == 'Mixed':
            offense, osrc = ('Physical' if pv['a_ge_c'] else 'Special'), 'mixed_demoted'
        else:
            offense, osrc = pv['offense'], pv['offense_source']
        defense = defense_of(s['trait'], idx)
        S = table[sid][1]
        H, A, B, C, D, S, w, moved = compute(h, a, d, offense, defense, S, idx, table[sid][0])
        infos[sid] = dict(had=had, bst3=h + a + d, bst6=table[sid][0], offense=offense, osrc=osrc,
                          defense=defense, idx=idx, w=w, moved=moved, was_mixed=pv['offense'] == 'Mixed')
        new = {}
        for k, val in s.items():
            if k in ('base_spatk', 'base_spdef', 'base_speed', 'offense', 'offense_source',
                     'defense', 'role', 'legacy_had', 'speed_source'):
                continue
            new[k] = val
            if k == 'base_def':
                new['base_spatk'] = C
                new['base_spdef'] = D
                new['base_speed'] = S
                new['offense'] = offense
                new['offense_source'] = osrc
                new['defense'] = defense
                new['role'] = f'{offense}-{defense}'
                new['legacy_had'] = had
                new['speed_source'] = {'kind': 'table'}
        new['base_hp'] = H
        new['base_atk'] = A
        new['base_def'] = B
        out.append(new)

    errors = self_check(out, infos, speed, len(mixed12))
    if errors:
        print('自己検査 NG:')
        for e in errors:
            print('  -', e)
        sys.exit(1)

    with open(SPECIES, 'w', encoding='utf-8') as f:
        json.dump(out, f, ensure_ascii=False, indent=2)
        f.write('\n')
    report(out, infos)
    print('自己検査 OK')


def win(out, c, w=4):
    return sum(1 for s in out if c - w <= s['base_speed'] <= c + w)


def peaks_ok(out):
    """山45/75/105 の ±4 の頭数が、谷60/90 の同幅より多い。戻り値 (エラー一覧, {位置: 頭数})。"""
    err = []
    c = {k: win(out, k) for k in (45, 60, 75, 90, 105)}
    if not (c[45] > c[60] and c[75] > c[60] and c[75] > c[90] and c[105] > c[90]):
        err.append(f'山が谷より多くない {c}')
    return err, c


def self_check(out, infos, speed, n_mixed):
    err = []
    for s in out:
        sid = s['species_id']
        inf = infos[sid]
        H, A, B, C, D, S = (s['base_hp'], s['base_atk'], s['base_def'], s['base_spatk'],
                            s['base_spdef'], s['base_speed'])
        vals = sorted([H, A, B, C, D, S])
        if sum(vals) != inf['bst6']:
            err.append(f'{sid}: 合計 {sum(vals)} != BST6 {inf["bst6"]}')
        if S != speed[sid]:
            err.append(f'{sid}: S={S} が確定表 {speed[sid]} と不一致')
        if vals[0] < FLOOR_EACH or vals[0] + vals[1] < FLOOR_PAIR or inf['moved'] < 0:
            err.append(f'{sid}: 下限規則を満たさない {vals}')
        if s['role'] != s['offense'] + '-' + s['defense']:
            err.append(f'{sid}: role が offense-defense でない')
        if s['offense'] == 'Mixed':
            if not mixed_ok(H, A, B, C, D, s['defense']):
                err.append(f'{sid}: 両刀の条件を満たさない H{H} A{A} B{B} C{C} D{D}')
        if s['defense'] == 'Both':
            if not both_ok(H, B, D):
                err.append(f'{sid}: 両面耐久の条件を満たさない H{H} B{B} D{D}')
    if sum(1 for s in out if s['offense'] == 'Mixed') != n_mixed or not 9 <= n_mixed <= 14:
        err.append(f'両刀攻撃が {n_mixed} 種でない/9〜14 の範囲外')
    if any(s['offense'] == 'Mixed' and s['defense'] == 'Both' for s in out):
        err.append('両刀かつ両面耐久がいる')
    ss = [s['base_speed'] for s in out]
    if min(ss) != S_MIN or max(ss) != S_MAX:
        err.append(f'S の最小/最大が {min(ss)}/{max(ss)}（期待 {S_MIN}/{S_MAX}）')
    for sid, b6 in {'003': 320, '186': 474, '203': 610}.items():
        s = next(x for x in out if x['species_id'] == sid)
        if sum(s[k] for k in ('base_hp', 'base_atk', 'base_def', 'base_spatk', 'base_spdef', 'base_speed')) != b6:
            err.append(f'BST6 基準 {sid} != {b6}')
    src = {}
    for s in out:
        src[s['offense_source']] = src.get(s['offense_source'], 0) + 1
    if sum(src.values()) != 287 or set(src) - {'trait', 'cycle', 'speed', 'mixed12', 'mixed_demoted'}:
        err.append(f'offense_source の内訳が不正 {src}')
    perr, _ = peaks_ok(out)
    err += perr
    if len(out) != 287:
        err.append(f'種数 {len(out)} != 287')
    return err


def report(out, infos):
    n = len(out)
    print('種数', n)
    bst6s = [infos[s['species_id']]['bst6'] for s in out]
    print(f'BST6 最小 {min(bst6s)} 最大 {max(bst6s)} 平均 {sum(bst6s) / n:.1f}')
    print('S 山の評価（±4 の頭数）', {k: win(out, k) for k in (45, 60, 75, 90, 105)})
    ss = sorted(s['base_speed'] for s in out)
    print(f'S 最小 {ss[0]} 最大 {ss[-1]} 平均 {sum(ss) / n:.1f} 中央値 {median(ss)}')
    print('役割の内訳（行 攻撃区分 × 列 耐久区分: 物理耐久 / 特殊耐久 / 両面耐久）')
    for o in ('Physical', 'Special', 'Mixed'):
        row = [sum(1 for s in out if s['offense'] == o and s['defense'] == d) for d in ('Physical', 'Special', 'Both')]
        print(f'  {OFFENSE_JA[o]} {row} 計 {sum(row)}')
    src = {}
    for s in out:
        src[s['offense_source']] = src.get(s['offense_source'], 0) + 1
    print('offense_source', src)
    print('両刀攻撃:')
    for s in out:
        if s['offense'] == 'Mixed':
            print(f'  {s["species_id"]} {s["display_name"]} {"/".join(s["types"])} BST6 {infos[s["species_id"]]["bst6"]}'
                  f' H{s["base_hp"]} A{s["base_atk"]} B{s["base_def"]} C{s["base_spatk"]} D{s["base_spdef"]} S{s["base_speed"]}')
    print('両刀 旧→新: 旧両刀', sum(1 for i in infos.values() if i['was_mixed']),
          '種のうち降格', sum(1 for i in infos.values() if i['osrc'] == 'mixed_demoted'),
          '種 / 新規に両刀', sum(1 for i in infos.values() if i['osrc'] == 'mixed12' and not i['was_mixed']), '種')
    print('下限規則で直した種:')
    for s in out:
        m = infos[s['species_id']]['moved']
        if m:
            print(f'  {s["species_id"]} {s["display_name"]} {m}点 -> H{s["base_hp"]} A{s["base_atk"]} B{s["base_def"]}'
                  f' C{s["base_spatk"]} D{s["base_spdef"]} S{s["base_speed"]}')
    print('役割別 平均 H A B C D S')
    for o, d in ROLES:
        g = [s for s in out if s['offense'] == o and s['defense'] == d]
        if not g:
            continue
        av = [sum(s[k] for s in g) / len(g) for k in
              ('base_hp', 'base_atk', 'base_def', 'base_spatk', 'base_spdef', 'base_speed')]
        print(f'  {OFFENSE_JA[o]}/{DEFENSE_JA[d]} {len(g):3d}種 ' + ' / '.join(f'{x:.1f}' for x in av))
    xs = [s['base_speed'] for s in out]
    ti = [s['base_hp'] * (s['base_def'] + s['base_spdef']) for s in out]
    zs_ = bst6s
    print(f'r(S, H(B+D)) = {pearson(xs, ti):.3f}  偏相関(BST6統制) = {partial(xs, ti, zs_):.3f}'
          f'  r(S, H(B+D)/BST6^2) = {pearson(xs, [t / (b * b) for t, b in zip(ti, zs_)]):.3f}')
    print(f'r(S, H*B) = {pearson(xs, [s["base_hp"] * s["base_def"] for s in out]):.3f}'
          f'  r(S, H*D) = {pearson(xs, [s["base_hp"] * s["base_spdef"] for s in out]):.3f}')
    print(f'総合耐久指数 最小 {min(ti)} 最大 {max(ti)} 平均 {sum(ti) / n:.0f}')
    for lo, hi in ((35, 62), (63, 87), (88, 130)):
        g = [t for t, x in zip(ti, xs) if lo <= x <= hi]
        print(f'  S {lo}-{hi}: {len(g)}種 平均 {sum(g) / len(g):.0f} 中央値 {median(g)}')
    for nm, key in (('H', 'base_hp'), ('A', 'base_atk'), ('B', 'base_def'),
                    ('C', 'base_spatk'), ('D', 'base_spdef'), ('S', 'base_speed')):
        v = [s[key] for s in out]
        print(f'  {nm}: {min(v)}〜{max(v)} 平均 {sum(v) / len(v):.1f}')


if __name__ == '__main__':
    main()
