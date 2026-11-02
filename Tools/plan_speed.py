#!/usr/bin/env python3
"""相対俊敏比較表（ユーザー提供）→ 俊敏の確定値の案を作る。Data は変更しない。
  /usr/bin/python3 Tools/plan_speed.py <list.txt> [out.md]
アンカー行（130/115/105/95/85/75/60/45/30/20）は値固定。アンカー間は、行を並び順のまま、
山(45/75/105)の密度で配分する（ランクは保存、同じ行は同値）。
"""
import json, math, re, sys, collections

PEAKS = (45, 75, 105)
SIGMA = 8.0
BASE = 0.12
FIX = {'アルセーヴツジギリ': ['アルセーヴ', 'ツジギリ'], 'シーペントナイトメアリ': ['シーペント', 'ナイトメアリ'],
       'ニャテミテ': ['ニャニミテ'], 'カブルミ': ['ガブルミ'], 'モエエール': ['モモエール']}
ADD = {'エテッパ': 63, 'アズレーン': 91, 'アズルーナ': 92, 'クルットリ': 85, 'ツキカゲ': 101,
       'ミステリア': 92, 'ノンビリリ': 50, 'ボルゼクス': 80, 'ベラルージュ': 106}


def dens(s):
    return BASE + sum(math.exp(-((s - p) ** 2) / (2 * SIGMA ** 2)) for p in PEAKS)


def inv_cdf_interval(lo, hi, t):
    """[lo,hi] 上で密度 dens の累積が割合 t になる値"""
    n = 2000
    xs = [lo + (hi - lo) * i / n for i in range(n + 1)]
    cs = [0.0]
    for a, b in zip(xs, xs[1:]):
        cs.append(cs[-1] + (dens(a) + dens(b)) / 2)
    target = t * cs[-1]
    for i in range(1, n + 1):
        if cs[i] >= target:
            f = (target - cs[i - 1]) / max(cs[i] - cs[i - 1], 1e-12)
            return xs[i - 1] + (xs[i] - xs[i - 1]) * f
    return hi


def load(path):
    rows, anch = [], {}
    for l in open(path, encoding='utf-8'):
        ts = l.split()
        if not ts:
            continue
        r = []
        for t in ts:
            if t.isdigit():
                anch[len(rows)] = int(t)
                continue
            m = re.fullmatch(r'(.*?)(\d{2,3})', t)
            if m and m.group(1):
                t = m.group(1)
                anch[len(rows)] = int(m.group(2))
            r += FIX.get(t, [t])
        rows.append(r)
    for r in rows:  # ブルフェルノは60側に置く（ユーザー指定）。重複のもう一方を除く
        if 'ツララジカ' in r and 'ブルフェルノ' in r:
            r.remove('ブルフェルノ')
    return rows, anch


def solve(path):
    rows, anch = load(path)
    A = sorted(anch.items())

    def lin(i):
        for (a, va), (b, vb) in zip(A, A[1:]):
            if a <= i <= b:
                return va + (vb - va) * (i - a) / (b - a)
    for name, val in ADD.items():  # 追加9種: 指定値に最も近い行へ入れる
        best = min(range(len(rows)), key=lambda i: abs(lin(i) - val))
        rows[best].append(name)
    value = {}
    for (a, va), (b, vb) in zip(A, A[1:]):
        for i in (a, b):
            for n in rows[i]:
                value[n] = float(anch[i])
        mid = list(range(a + 1, b))
        tot = sum(len(rows[i]) for i in mid)
        cum = 0
        for i in mid:
            t = (cum + len(rows[i]) / 2) / tot
            cum += len(rows[i])
            v = inv_cdf_interval(vb, va, 1 - t)  # 行が下がるほど値は小さい
            for n in rows[i]:
                value[n] = v
    sp = {s['display_name']: s for s in json.load(open('Data/species.json'))}
    miss = [n for n in sp if n not in value]
    extra = [n for n in value if n not in sp]
    assert not miss and not extra, (miss, extra)
    S = {n: round(v) for n, v in value.items()}
    return rows, anch, S


def write_fixed(path, md, out='Data/source/speed_fixed.json'):
    """確定表(md)と完全一致することを確かめて {species_id: S} を出力する。"""
    rows, anch, S = solve(path)
    want = {}
    for l in open(md, encoding='utf-8'):
        m = re.match(r'- +(\d+): (.*?)( ← 指定\d+)?$', l.rstrip('\n'))
        if m and ':' in l[:8]:
            for n in m.group(2).split():
                want[n] = int(m.group(1))
    assert want == S, [(n, want.get(n), S.get(n)) for n in set(want) | set(S) if want.get(n) != S.get(n)][:10]
    sp = json.load(open('Data/species.json', encoding='utf-8'))
    byname = {}
    for s in sp:
        assert s['display_name'] not in byname, s['display_name']
        byname[s['display_name']] = s['species_id']
    fixed = {byname[n]: S[n] for n in sorted(S, key=lambda n: byname[n])}
    assert len(fixed) == 287
    with open(out, 'w', encoding='utf-8') as f:
        json.dump(fixed, f, ensure_ascii=False, indent=1)
        f.write('\n')
    print('speed_fixed.json', len(fixed), '件 / 表と一致 / 最小', min(fixed.values()), '最大', max(fixed.values()))


def main(path, out):
    rows, anch, S = solve(path)
    # 山の評価: ±4 内の数 / 谷（山の中間）の同幅の数
    cnt = collections.Counter(S.values())
    def win(c, w=4): return sum(cnt[v] for v in range(c - w, c + w + 1))
    L = ['# 俊敏の確定案（相対俊敏比較表より）', '',
         f'行数 {len(rows)} / 種数 {len(S)} / 最小 {min(S.values())} / 最大 {max(S.values())}', '',
         '## 山の評価（±4 の幅の頭数）', '',
         '| 位置 | 頭数 |', '|---|---|']
    for c, nm in ((45, '山45'), (60, '谷60'), (75, '山75'), (90, '谷90'), (105, '山105')):
        L.append(f'| {nm} | {win(c)} |')
    L += ['', '## 5刻みの分布', '']
    for k in range(20, 135, 5):
        n = sum(cnt[v] for v in range(k, k + 5))
        L.append(f'- {k:3d}-{k+4:3d}: {n:3d} ' + '#' * n)
    L += ['', '## 行ごとの値（上ほど速い）', '']
    for i, r in enumerate(rows):
        mark = f' ← 指定{anch[i]}' if i in anch else ''
        L.append(f"- {S[r[0]]:3d}: {' '.join(r)}{mark}")
    open(out, 'w', encoding='utf-8').write('\n'.join(L) + '\n')
    print('\n'.join(L[:20]))


if __name__ == '__main__':
    if len(sys.argv) > 1 and sys.argv[1] == '--fixed':
        write_fixed(sys.argv[2] if len(sys.argv) > 2 else 'Tools/speed_list.txt',
                    sys.argv[3] if len(sys.argv) > 3 else 'docs/design/drafts/2026-10-30-speed-table.md')
        sys.exit(0)
    main(sys.argv[1], sys.argv[2] if len(sys.argv) > 2 else 'docs/design/drafts/2026-10-30-speed-table.md')
