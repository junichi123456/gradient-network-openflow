#!/usr/bin/env python3
"""species.json に HABCDS（H/A/B/C/D/S）・役割・BST6 を書き込む。

仕様書 docs/design/drafts/2026-10-25-void-type-and-habcds.md §3-b〜3-e。

  python3 Tools/build_habcds.py

入力: Data/species.json（旧HAD。legacy_had があればそれを優先）、
      Data/moves.json、Data/source/paldb_speed.json（Tools/fetch_paldb_speed.py の出力）。
出力: Data/species.json を書き換える。何度実行しても結果は同じ（冪等）。
      旧HAD は初回に legacy_had へ退避する。

計算はすべて fractions.Fraction で行い、四捨五入は floor(x + 1/2)（x.5 は切り上げ）。
"""
import json, os, sys
from fractions import Fraction as F
from math import floor, ceil

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from learnset_rules import profile_of

SPECIES = os.path.join(ROOT, 'Data', 'species.json')
MOVES = os.path.join(ROOT, 'Data', 'moves.json')
SPEED = os.path.join(ROOT, 'Data', 'source', 'paldb_speed.json')

# 3-d: 区分線形（端点の外は端点の値で一定）
P_R = [(F(375), F(90)), (F(1000), F(98)), (F(1400), F(102)), (F(2280), F(104)), (F(2500), F(130))]
P_N = [(F(0), F(35)), (F(1), F(73)), (F(2), F(77)), (F(3), F(82)), (F(8), F(96))]
S_MIN, S_MAX = 35, 130

# 3-c ③: 役割ごとの原型（H, A, B, C, D）を (h, a, d) から作る係数
#   物理型: H=h A=a B=d  C=ca*a D=cd*d / 特殊型: H=h C=a D=d  A=ca*a B=cd*d
PROTO = {
    'PhysicalAttacker': (F(3, 5), F(3, 4)),
    'PhysicalWall':     (F(2, 5), F(19, 20)),
    'SpecialAttacker':  (F(3, 5), F(3, 4)),
    'SpecialWall':      (F(2, 5), F(19, 20)),
}
TIE_ORDER = ['H', 'B', 'D', 'A', 'C']   # 最大剰余法で小数部が同じときの優先順
ABILS = ['H', 'A', 'B', 'C', 'D']


def rnd(x):
    """四捨五入（x.5 は切り上げ）"""
    return floor(F(x) + F(1, 2))


def bst6_of(bst3):
    return 360 + floor((F(bst3 - 180) * 54 + 32) / 64)


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


def largest_remainder(protos, total):
    """protos: {能力: Fraction原型}。合計が total になるよう縮尺して整数化する。"""
    s = sum(protos.values())
    k = F(total) / s
    xs = {a: protos[a] * k for a in protos}
    fl = {a: floor(xs[a]) for a in xs}
    short = total - sum(fl.values())
    order = sorted(xs, key=lambda a: (-(xs[a] - fl[a]), TIE_ORDER.index(a)))
    for a in order[:short]:
        fl[a] += 1
    return fl, k


def role_of(profile, h, a, d):
    if profile == 'Versatile':
        return 'Mixed'
    attacker = a > max(h, d)    # 同値は耐久型
    if profile == 'Physical':
        return 'PhysicalAttacker' if attacker else 'PhysicalWall'
    return 'SpecialAttacker' if attacker else 'SpecialWall'


def mixed_alloc(h, a, d, T, r):
    e_def = h + F(185, 100) * d
    e_atk = F(17, 10) * a
    Dt = rnd(T * e_def / (e_def + e_atk))
    At = T - Dt
    H = rnd(F(Dt) / F(18, 10))
    G = Dt - H
    B = rnd(G * r)
    D = G - B
    A = rnd(At * r)
    A = max(ceil(F(2, 5) * At), min(floor(F(3, 5) * At), A))
    C = At - A
    return {'H': H, 'A': A, 'B': B, 'C': C, 'D': D}


def physical_ratio(learnset, cat):
    """打ち分け型の r。Phase 6 の制限技（id が rs_ で始まる）は数えない。"""
    p = s = 0
    for e in learnset:
        if e['move_id'].startswith('rs_'):
            continue
        c = cat.get(e['move_id'])
        if c == 'Physical':
            p += 1
        elif c == 'Special':
            s += 1
    if p + s == 0:
        return F(1, 2)
    return min(F(3, 5), max(F(2, 5), F(p, p + s)))


def compute(s, idx, cat, speed):
    had = s['legacy_had'] if 'legacy_had' in s else {'hp': s['base_hp'], 'atk': s['base_atk'], 'def': s['base_def']}
    h, a, d = had['hp'], had['atk'], had['def']
    bst3 = h + a + d
    bst6 = bst6_of(bst3)
    profile = profile_of(s['trait'], idx)[0]
    role = role_of(profile, h, a, d)
    S, v = speed_of(speed)
    T = bst6 - S
    info = {'profile': profile, 'bst3': bst3, 'bst6': bst6, 'T': T, 'r': None}
    if role == 'Mixed':
        r = physical_ratio(s['learnset'], cat)
        v6 = mixed_alloc(F(h), F(a), F(d), T, r)
        info['r'] = r
    else:
        ca, cd = PROTO[role]
        if profile == 'Physical':
            protos = {'H': F(h), 'A': F(a), 'B': F(d), 'C': ca * a, 'D': cd * d}
        else:
            protos = {'H': F(h), 'C': F(a), 'D': F(d), 'A': ca * a, 'B': cd * d}
        v6, k = largest_remainder(protos, T)
        info['k'] = k
    v6['S'] = S
    return had, role, v6, v, info


def main():
    species = json.load(open(SPECIES, encoding='utf-8'))
    moves = json.load(open(MOVES, encoding='utf-8'))
    cat = {m['id']: m['category'] for m in moves}
    speed = json.load(open(SPEED, encoding='utf-8'))
    order = {s['species_id']: i for i, s in enumerate(sorted(species, key=lambda s: s['species_id']))}

    infos = {}
    out = []
    for s in species:
        sid = s['species_id']
        if sid not in speed:
            sys.exit(f'paldb_speed.json に無い: {sid}')
        had, role, v6, v, info = compute(s, order[sid], cat, speed[sid])
        infos[sid] = (had, role, v6, v, info)
        n = {}
        for k, val in s.items():
            if k in ('base_spatk', 'base_spdef', 'base_speed', 'role', 'legacy_had', 'speed_source'):
                continue
            n[k] = val
            if k == 'base_def':
                pass
        new = {}
        for k, val in n.items():
            new[k] = val
            if k == 'base_def':
                new['base_spatk'] = v6['C']
                new['base_spdef'] = v6['D']
                new['base_speed'] = v6['S']
                new['role'] = role
                new['legacy_had'] = had
                ss = {'kind': speed[sid]['kind'],
                      'v_eff': (None if v is None else (int(v) if v.denominator == 1 else float(v))),
                      'transport': speed[sid]['transport'], 'run': speed[sid]['run']}
                new['speed_source'] = ss
        new['base_hp'] = v6['H']
        new['base_atk'] = v6['A']
        new['base_def'] = v6['B']
        out.append(new)

    errors = self_check(out, infos)
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


def self_check(out, infos):
    err = []
    by = {s['species_id']: s for s in out}
    for s in out:
        sid = s['species_id']
        had, role, v6, v, info = infos[sid]
        vals = [s['base_hp'], s['base_atk'], s['base_def'], s['base_spatk'], s['base_spdef'], s['base_speed']]
        if sum(vals) != info['bst6']:
            err.append(f'{sid}: 合計 {sum(vals)} != BST6 {info["bst6"]}')
        if min(vals) < 10:
            err.append(f'{sid}: 能力が10未満 {vals}')
        if not (S_MIN <= s['base_speed'] <= S_MAX):
            err.append(f'{sid}: S={s["base_speed"]} が範囲外')
        H, A, B, C, D = (s['base_hp'], s['base_atk'], s['base_def'], s['base_spatk'], s['base_spdef'])
        prof = info['profile']
        if (prof == 'Versatile') != (s['role'] == 'Mixed'):
            err.append(f'{sid}: 型と役割が不整合')
        if s['role'] in ('PhysicalAttacker', 'PhysicalWall'):
            if (A > max(H, B)) != (s['role'] == 'PhysicalAttacker'):
                err.append(f'{sid}: 物理の役割と変換後の能力が不整合 {s["role"]} {H}/{A}/{B}')
        if s['role'] in ('SpecialAttacker', 'SpecialWall'):
            if (C > max(H, D)) != (s['role'] == 'SpecialAttacker'):
                err.append(f'{sid}: 特殊の役割と変換後の能力が不整合 {s["role"]} {H}/{C}/{D}')
        if s['role'] == 'Mixed':
            if abs((B + D) - F(8, 10) * H) > 1:
                err.append(f'{sid}: |B+D-0.8H|>1 ({B}+{D} vs {H})')
            if not (F(35, 100) <= F(B, B + D) <= F(65, 100)):
                err.append(f'{sid}: B:D が範囲外 {B}:{D}')
            if not (F(40, 100) <= F(A, A + C) <= F(60, 100)):
                err.append(f'{sid}: A:C が範囲外 {A}:{C}')
    ref = {'199': 104, '130B': 130, '196': 130, '201': 130}
    for sid, sv in ref.items():
        if by[sid]['base_speed'] != sv:
            err.append(f'基準点 {sid}: S={by[sid]["base_speed"]} (期待 {sv})')
    ss = [s['base_speed'] for s in out]
    if min(ss) != S_MIN or max(ss) != S_MAX:
        err.append(f'S の最小/最大が {min(ss)}/{max(ss)}（期待 35/130）')
    bsts = {}
    for s in out:
        b = infos[s['species_id']][4]['bst3']
        bsts.setdefault(b, set()).add(infos[s['species_id']][4]['bst6'])
    tb = {180: 360, 350: 503, 500: 630}
    for b3, b6 in tb.items():
        if bsts.get(b3) and bsts[b3] != {b6}:
            err.append(f'BST3={b3} の BST6 が {bsts[b3]}（期待 {b6}）')
    if len(out) != 287:
        err.append(f'種数 {len(out)} != 287')
    return err


def report(out, infos):
    print('種数', len(out))
    bst6s = [infos[s['species_id']][4]['bst6'] for s in out]
    print(f'BST6 最小 {min(bst6s)} 最大 {max(bst6s)} 平均 {sum(bst6s) / len(bst6s):.1f}')
    roles = {}
    for s in out:
        roles[s['role']] = roles.get(s['role'], 0) + 1
    print('役割内訳', roles)
    print('役割別 BST6 平均 / H A B C D S 平均')
    for rl in ['PhysicalWall', 'PhysicalAttacker', 'SpecialWall', 'SpecialAttacker', 'Mixed']:
        g = [s for s in out if s['role'] == rl]
        b = sum(infos[s['species_id']][4]['bst6'] for s in g) / len(g)
        av = [sum(s[k] for s in g) / len(g) for k in
              ('base_hp', 'base_atk', 'base_def', 'base_spatk', 'base_spdef', 'base_speed')]
        print(f'  {rl:17s} {len(g):3d}種 BST6 {b:.1f}  ' + ' / '.join(f'{x:.1f}' for x in av))
    ride = {s['species_id'] for s in out if s['speed_source']['kind'] != 'none'}
    print('S ヒストグラム（中心値±2）')
    for c in range(35, 131, 5):
        lo, hi = c - 2, c + 2
        n_r = sum(1 for s in out if lo <= s['base_speed'] <= hi and s['species_id'] in ride)
        n_n = sum(1 for s in out if lo <= s['base_speed'] <= hi and s['species_id'] not in ride)
        print(f'{lo:3d}-{hi:3d} {n_r + n_n:3d} (非ライド {n_n:3d} ライド {n_r:3d}) ' + '#' * n_n + '=' * n_r)
    ss = sorted(s['base_speed'] for s in out)
    print(f'S 最小 {ss[0]} 最大 {ss[-1]} 平均 {sum(ss) / len(ss):.1f} 中央値 {ss[len(ss) // 2]}')
    for nm, key in (('H', 'base_hp'), ('A', 'base_atk'), ('B', 'base_def'),
                    ('C', 'base_spatk'), ('D', 'base_spdef'), ('S', 'base_speed')):
        v = [s[key] for s in out]
        print(f'  {nm}: {min(v)}〜{max(v)} 平均 {sum(v) / len(v):.1f}')


if __name__ == '__main__':
    main()
