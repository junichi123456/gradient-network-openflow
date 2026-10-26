#!/usr/bin/env python3
"""species.json に HABCDS（H/A/B/C/D/S）・攻撃区分・耐久区分・役割・BST6 を書き込む。

仕様書 docs/design/drafts/2026-10-26-stat-rework.md §3 Phase S1（S1-a〜S1-d・S1-h）。

  python3 Tools/build_habcds.py

入力: Data/species.json（旧HAD。legacy_had があればそれを優先）、
      Data/moves.json、Data/source/paldb_speed.json（Tools/fetch_paldb_speed.py の出力）。
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
from learnset_rules import (offense_of, defense_of, mixed_side_of, speed_zone, fast_blocks,
                            MIXED_MAIN_MIN, MIXED_MAIN_MAX, OFFENSE_JA, DEFENSE_JA, SIGNATURE)

SPECIES = os.path.join(ROOT, 'Data', 'species.json')
MOVES = os.path.join(ROOT, 'Data', 'moves.json')
SPEED = os.path.join(ROOT, 'Data', 'source', 'paldb_speed.json')

# S1-b: 区分線形（端点の外は端点の値で一定）
P_R = [(F(375), F(90)), (F(1000), F(98)), (F(1400), F(102)), (F(2280), F(104)), (F(2500), F(130))]
P_N = [(F(0), F(35)), (F(1, 8), F(47)), (F(3, 8), F(51)), (F(7, 8), F(56)), (F(1), F(60)),
       (F(9, 8), F(73)), (F(3, 2), F(75)), (F(5, 2), F(77)), (F(3), F(80)), (F(8), F(94))]
S_MIN, S_MAX = 35, 130


# S1-d: 配分の比
BETA = {'Physical': F(13, 20), 'Special': F(7, 20), 'Both': F(1, 2)}    # B/(B+D)
ALPHA = {'Physical': F(7, 10), 'Special': F(3, 10)}                      # A/(A+C)
ROLES = [(o, d) for o in ('Physical', 'Special', 'Mixed') for d in ('Physical', 'Special', 'Both')]
ROLE_COUNT = {('Physical', 'Physical'): 51, ('Physical', 'Special'): 43, ('Physical', 'Both'): 43,
              ('Special', 'Physical'): 34, ('Special', 'Special'): 42, ('Special', 'Both'): 36,
              ('Mixed', 'Physical'): 13, ('Mixed', 'Special'): 14, ('Mixed', 'Both'): 11}


def rnd(x):
    """四捨五入（x.5 は切り上げ）"""
    return floor(F(x) + F(1, 2))


def bst6_of(bst3):
    """S1-a: round(320 + (BST3-180)*290/320) = 320 + floor(((BST3-180)*58 + 32)/64)"""
    return 320 + floor((F(bst3 - 180) * 58 + 32) / 64)


def interp(points, x):
    if x <= points[0][0]:
        return points[0][1]
    if x >= points[-1][0]:
        return points[-1][1]
    for (x0, y0), (x1, y1) in zip(points, points[1:]):
        if x0 <= x <= x1:
            return y0 + (y1 - y0) * (x - x0) / (x1 - x0)
    raise AssertionError


def v_eff_of(rec):
    k = rec['kind']
    if k == 'none':
        return None
    if k == 'water':
        return F(rec['swim_dash']) * (1 + F(rec['swim_bonus'] or 0) / 100)
    return (F(rec['ride_sprint']) * (1 + F(rec['ride_bonus'] or 0) / 100)
            * (1 + 4 * F(rec['pair_pct'] or 0) / 100))


def speed_of(rec):
    T = rec['transport']
    if rec['kind'] != 'none':
        v = v_eff_of(rec)
        return min(S_MAX, rnd(interp(P_R, v) + T)), v
    f = min(F(1), max(F(0), (F(rec['run']) - 250) / 400))
    return rnd(interp(P_N, T + f)), None


def attack_counts(learnset, cat):
    """learnset の攻撃技（power>0）の Physical / Special の数。rs_ と SIGNATURE は数えない。"""
    p = q = 0
    for e in learnset:
        mid = e['move_id']
        if mid.startswith('rs_') or mid in SIGNATURE:
            continue
        c = cat.get(mid)
        if c is None:
            continue
        if c[1] <= 0:
            continue
        if c[0] == 'Physical':
            p += 1
        elif c[0] == 'Special':
            q += 1
    return p, q


def compute(h, a, d, offense, defense, S, index, p, q):
    """S1-d の配分。戻り値 (H, A, B, C, D, S)。p, q は両刀攻撃でだけ使う。"""
    b3 = h + a + d
    b6 = bst6_of(b3)
    T = b6 - S
    W = F(76, 100) - F(30, 100) * (F(S) - 35) / 95
    w = min(F(80, 100), max(F(45, 100), W + (F(h + d, b3) - F(2, 3))))
    Bulk = rnd(T * w)
    Off = T - Bulk
    H = rnd(Bulk * F(h, h + 2 * d))
    G = Bulk - H
    B = rnd(G * BETA[defense])
    D = G - B
    if offense == 'Mixed':
        main = mixed_side_of(index)
        share = F(1, 2) if p + q == 0 else F(p if main == 'Physical' else q, p + q)
        X = rnd(Off * min(MIXED_MAIN_MAX, max(MIXED_MAIN_MIN, share)))
        X = max(ceil(MIXED_MAIN_MIN * Off), min(floor(MIXED_MAIN_MAX * Off), X))
        A, C = (X, Off - X) if main == 'Physical' else (Off - X, X)
    else:
        A = rnd(Off * ALPHA[offense])
        C = Off - A
    return H, A, B, C, D, S, w


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


def main():
    species = json.load(open(SPECIES, encoding='utf-8'))
    moves = json.load(open(MOVES, encoding='utf-8'))
    cat = {m['id']: (m['category'], m['power']) for m in moves}
    speed = json.load(open(SPEED, encoding='utf-8'))
    order = {s['species_id']: i for i, s in enumerate(sorted(species, key=lambda s: s['species_id']))}

    # (1) 全287種の BST6 と S を先に求めて table を作る
    table = {}
    speed_info = {}
    for s in species:
        sid = s['species_id']
        if sid not in speed:
            sys.exit(f'paldb_speed.json に無い: {sid}')
        had = had_of(s)
        S, v = speed_of(speed[sid])
        table[sid] = (bst6_of(had['hp'] + had['atk'] + had['def']), S)
        speed_info[sid] = v

    infos = {}
    out = []
    for s in species:
        sid = s['species_id']
        idx = order[sid]
        had = had_of(s)
        h, a, d = had['hp'], had['atk'], had['def']
        # (2) 攻撃区分・耐久区分
        offense, osrc = offense_of(sid, s['trait'], idx, table)
        defense = defense_of(s['trait'], idx)
        # (3) 配分
        p, q = attack_counts(s['learnset'], cat)
        S = table[sid][1]
        H, A, B, C, D, S, w = compute(h, a, d, offense, defense, S, idx, p, q)
        v = speed_info[sid]
        infos[sid] = dict(had=had, bst3=h + a + d, bst6=table[sid][0], offense=offense, osrc=osrc,
                          defense=defense, idx=idx, p=p, q=q, w=w, v=v)
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
                new['speed_source'] = {'kind': speed[sid]['kind'],
                                       'v_eff': (None if v is None else (int(v) if v.denominator == 1 else float(v))),
                                       'transport': speed[sid]['transport'], 'run': speed[sid]['run']}
        new['base_hp'] = H
        new['base_atk'] = A
        new['base_def'] = B
        out.append(new)

    errors, warns = self_check(out, infos, table, moves)
    for w_ in warns:
        print('警告:', w_)
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


def peaks_ok(out):
    """S1-b の三峰の判定。戻り値 (エラー一覧, {中心: 度数})。"""
    err = []
    c = {}
    for ctr in range(35, 131, 5):
        c[ctr] = sum(1 for s in out if ctr - 2 <= s['base_speed'] <= ctr + 2)
    mx = lambda lo, hi: max(c[k] for k in range(lo, hi + 1, 5))
    if c[50] != mx(35, 65): err.append('c(50) が 35〜65 の最大でない')
    if c[75] != mx(70, 90): err.append('c(75) が 70〜90 の最大でない')
    if c[100] != mx(95, 130): err.append('c(100) が 95〜130 の最大でない')
    if not any(c[k] <= min(c[50], c[75]) / 2 for k in range(55, 71, 5)): err.append('50 と 75 の間に谷が無い')
    if not any(c[k] <= min(c[75], c[100]) / 2 for k in range(80, 96, 5)): err.append('75 と 100 の間に谷が無い')
    return err, c


def self_check(out, infos, table, moves):
    err = []
    warns = []
    by = {s['species_id']: s for s in out}
    mv = {m['id']: m for m in moves}
    for s in out:
        sid = s['species_id']
        inf = infos[sid]
        H, A, B, C, D, S = (s['base_hp'], s['base_atk'], s['base_def'], s['base_spatk'],
                            s['base_spdef'], s['base_speed'])
        vals = [H, A, B, C, D, S]
        if sum(vals) != inf['bst6']:
            err.append(f'{sid}: 合計 {sum(vals)} != BST6 {inf["bst6"]}')
        if min(vals) < 10:
            err.append(f'{sid}: 能力が10未満 {vals}')
        if not (S_MIN <= S <= S_MAX):
            err.append(f'{sid}: S={S} が範囲外')
        if s['role'] != s['offense'] + '-' + s['defense']:
            err.append(f'{sid}: role が offense-defense でない')
        # 配分の再計算
        G = B + D
        if B != rnd(G * BETA[s['defense']]):
            err.append(f'{sid}: B が round(G×β) と不一致')
        Off = A + C
        if s['offense'] == 'Mixed':
            if not (10 * max(A, C) >= 11 * min(A, C) and 5 * max(A, C) <= 3 * (A + C)):
                err.append(f'{sid}: 両刀攻撃の A:C が範囲外 {A}:{C}')
            main = mixed_side_of(inf['idx'])
            p, q = inf['p'], inf['q']
            share = F(1, 2) if p + q == 0 else F(p if main == 'Physical' else q, p + q)
            X = rnd(Off * min(MIXED_MAIN_MAX, max(MIXED_MAIN_MIN, share)))
            X = max(ceil(MIXED_MAIN_MIN * Off), min(floor(MIXED_MAIN_MAX * Off), X))
            if (A if main == 'Physical' else C) != X:
                err.append(f'{sid}: 両刀攻撃の主側が再計算と不一致')
        else:
            if A != rnd(Off * ALPHA[s['offense']]):
                err.append(f'{sid}: A が round(Off×α) と不一致')
        # 俊敏条件
        zs = [table[t][1] for t in speed_zone(sid, table)]
        fast = F(S) > F(median(zs))
        if s['offense'] == 'Mixed' and not fast:
            err.append(f'{sid}: 両刀攻撃だが俊敏条件を満たさない')
        if s['offense_source'] == 'speed' and fast:
            err.append(f'{sid}: speed 再分類だが俊敏条件を満たす')
        # 優先技（警告のみ）
        for e in s['learnset']:
            m = mv.get(e['move_id'])
            if m and fast_blocks(s, m):
                warns.append(f'{sid} {s["display_name"]}: S={S} で優先度+1以上の攻撃技 {e["move_id"]}')
    ref = {'199': 104, '130B': 130, '196': 130, '201': 130}
    for sid, sv in ref.items():
        if by[sid]['base_speed'] != sv:
            err.append(f'基準点 {sid}: S={by[sid]["base_speed"]} (期待 {sv})')
    ss = [s['base_speed'] for s in out]
    if min(ss) != S_MIN or max(ss) != S_MAX:
        err.append(f'S の最小/最大が {min(ss)}/{max(ss)}（期待 35/130）')
    for sid, b6 in {'003': 320, '186': 474, '203': 610}.items():
        if sum(by[sid][k] for k in ('base_hp', 'base_atk', 'base_def', 'base_spatk', 'base_spdef', 'base_speed')) != b6:
            err.append(f'BST6 基準 {sid} != {b6}')
    cnt = {}
    for s in out:
        cnt[(s['offense'], s['defense'])] = cnt.get((s['offense'], s['defense']), 0) + 1
    for k in ROLES:
        if cnt.get(k, 0) < 10:
            err.append(f'役割 {k} が {cnt.get(k, 0)} 種（10種以上必要）')
    src = {}
    for s in out:
        src[s['offense_source']] = src.get(s['offense_source'], 0) + 1
    if sum(src.values()) != 287 or set(src) - {'trait', 'cycle', 'speed'}:
        err.append(f'offense_source の内訳が不正 {src}')
    sp = [s for s in out if s['offense_source'] == 'speed']
    if len(sp) != 41 or sum(1 for s in sp if s['offense'] == 'Physical') != 13 \
            or sum(1 for s in sp if s['offense'] == 'Special') != 28:
        err.append(f'speed 再分類が 41種（物理13・特殊28）でない: {len(sp)}')
    if sum(1 for s in out if s['offense'] == 'Mixed') != 38:
        err.append('両刀攻撃が38種でない')
    perr, _ = peaks_ok(out)
    err += perr
    xs = [s['base_speed'] for s in out]
    ys = [s['base_hp'] * (s['base_def'] + s['base_spdef']) for s in out]
    zs_ = [infos[s['species_id']]['bst6'] for s in out]
    r = pearson(xs, ys)
    pr = partial(xs, ys, zs_)
    if not r <= -0.40:
        err.append(f'r(S, H(B+D)) = {r:.3f} > -0.40')
    if not pr <= -0.80:
        err.append(f'偏相関 = {pr:.3f} > -0.80')
    if len(out) != 287:
        err.append(f'種数 {len(out)} != 287')
    return err, warns


def report(out, infos):
    n = len(out)
    print('種数', n)
    bst6s = [infos[s['species_id']]['bst6'] for s in out]
    print(f'BST6 最小 {min(bst6s)} 最大 {max(bst6s)} 平均 {sum(bst6s) / n:.1f}')
    ride = {s['species_id'] for s in out if s['speed_source']['kind'] != 'none'}
    print('S ヒストグラム（中心値±2）')
    for c in range(35, 131, 5):
        lo, hi = c - 2, c + 2
        n_r = sum(1 for s in out if lo <= s['base_speed'] <= hi and s['species_id'] in ride)
        n_n = sum(1 for s in out if lo <= s['base_speed'] <= hi and s['species_id'] not in ride)
        print(f'{lo:3d}-{hi:3d} {n_r + n_n:3d} (非ライド {n_n:3d} ライド {n_r:3d}) ' + '#' * n_n + '=' * n_r)
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
            print(f'  {s["species_id"]} {s["display_name"]} BST6 {infos[s["species_id"]]["bst6"]} S{s["base_speed"]}'
                  f' A{s["base_atk"]} C{s["base_spatk"]}')
    print('再分類（speed）:')
    for s in out:
        if s['offense_source'] == 'speed':
            print(f'  {s["species_id"]} {s["display_name"]} → {OFFENSE_JA[s["offense"]]}')
    print('役割別 平均 H A B C D S')
    for o, d in ROLES:
        g = [s for s in out if s['offense'] == o and s['defense'] == d]
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
