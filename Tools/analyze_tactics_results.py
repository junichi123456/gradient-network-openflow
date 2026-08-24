#!/usr/bin/env python3
"""§25「5戦術・35チームの適応総当たり」の結果を集計する。

  python3 Tools/analyze_tactics_results.py <tactics.csv のパス>

読むファイル:
  tactics.csv             loop,i,j,勝ちi,勝ちj,引分,未決着
  tactics.csv.builds.txt  loop,team,戦術A,戦術B,種族:持ち物:技|技|技|技 ×6
"""
import collections
import json
import os
import statistics
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

JA = {'Guardian': '仁王立ち', 'Burst': 'ワンサイクル', 'Control': 'コントロール',
      'HitAway': 'ヒットアンドアウェイ', 'Weather': '天候'}


def main():
    csv_path = sys.argv[1] if len(sys.argv) > 1 else 'tactics.csv'
    builds_path = csv_path + '.builds.txt'

    species = {s['species_id']: s for s in
               json.load(open(f'{ROOT}/Data/species.json', encoding='utf-8'))}
    traits = {t['id']: t for t in
              json.load(open(f'{ROOT}/Data/traits.json', encoding='utf-8'))}
    name = lambda sid: species.get(sid, {}).get('display_name', sid)
    bst = lambda sid: (species[sid]['base_hp'] + species[sid]['base_atk']
                       + species[sid]['base_def']) if sid in species else 0

    # ---- builds: loop -> team -> (A, B, [entries]) ----
    builds = collections.defaultdict(dict)
    tactic_of = {}
    for line in open(builds_path, encoding='utf-8'):
        p = line.rstrip('\n').split(',')
        loop, team, a, b = int(p[0]), int(p[1]), p[2], p[3]
        entries = [x.split(':') for x in p[4:]]
        builds[loop][team] = (a, b, entries)
        tactic_of[team] = (a, b)

    loops = sorted(builds)
    teams = sorted(tactic_of)

    # ---- per-team tallies ----
    wins = collections.Counter()
    losses = collections.Counter()
    draws = collections.Counter()
    undec = collections.Counter()
    # loop 別の戦術ペア成績
    pair_loop = collections.defaultdict(lambda: [0, 0])   # (loop, pairkey) -> [win, played]

    for line in open(csv_path, encoding='utf-8'):
        loop, i, j, wi, wj, dr, un = (int(x) for x in line.strip().split(','))
        wins[i] += wi; wins[j] += wj
        losses[i] += wj; losses[j] += wi
        draws[i] += dr; draws[j] += dr
        undec[i] += un; undec[j] += un
        for t, w in ((i, wi), (j, wj)):
            k = (loop, tactic_of[t])
            pair_loop[k][0] += w
            pair_loop[k][1] += wi + wj + dr + un

    total = sum(wins.values()) + sum(draws.values()) // 2 + sum(undec.values()) // 2
    print(f'[結果] 総試合数 {total:,} / {len(teams)}チーム / {len(loops)}ループ')
    print(f'        決着 {sum(wins.values()):,} / 引き分け {sum(draws.values())//2:,} '
          f'/ 未決着 {sum(undec.values())//2:,}')

    # ---- 戦術ペア別の通算勝率 ----
    print()
    print('=== 戦術ペア別の通算勝率 ===')
    agg = collections.defaultdict(lambda: [0, 0])
    for t in teams:
        played = wins[t] + losses[t] + draws[t] + undec[t]
        agg[tactic_of[t]][0] += wins[t]
        agg[tactic_of[t]][1] += played
    rows = sorted(agg.items(), key=lambda kv: -kv[1][0] / max(1, kv[1][1]))
    for (a, b), (w, pl) in rows:
        print(f'  {JA[a]}＋{JA[b]:<12} {100*w/max(1,pl):5.1f}%  ({w:,}/{pl:,})')

    # ---- 戦術単体の勝率（2戦術のどちらかに含まれるチームをまとめる） ----
    print()
    print('=== 戦術単体（その戦術を含むチーム全体） ===')
    single = collections.defaultdict(lambda: [0, 0])
    for t in teams:
        played = wins[t] + losses[t] + draws[t] + undec[t]
        for k in tactic_of[t]:
            single[k][0] += wins[t]
            single[k][1] += played
    for k, (w, pl) in sorted(single.items(), key=lambda kv: -kv[1][0] / max(1, kv[1][1])):
        print(f'  {JA[k]:<12} {100*w/max(1,pl):5.1f}%  ({w:,}/{pl:,})')

    # ---- チーム別ランキング ----
    print()
    print('=== チーム別ランキング（上位10 / 下位5） ===')
    order = sorted(teams, key=lambda t: -wins[t] / max(1, wins[t] + losses[t] + draws[t] + undec[t]))
    def show(t):
        pl = wins[t] + losses[t] + draws[t] + undec[t]
        a, b = tactic_of[t]
        last = builds[loops[-1]][t][2]
        avg = statistics.mean(bst(e[0]) for e in last)
        print(f'  T{t:<3} {JA[a]}＋{JA[b]:<12} {100*wins[t]/pl:5.1f}%  '
              f'{wins[t]}勝{losses[t]}敗  最終平均BST{avg:.0f}  '
              + ' / '.join(name(e[0]) for e in last))
    for t in order[:10]:
        show(t)
    print('  ...')
    for t in order[-5:]:
        show(t)

    # ---- 5段階の構築遷移: 収束したのか ----
    print()
    print('=== 構築の遷移（種族の入れ替わりと収束） ===')
    print(f'{"loop":<6}{"延べ種族数":<12}{"実種族数":<10}{"最頻種(採用チーム数)":<28}{"平均BST":<8}{"前loopからの残留率"}')
    prev = None
    for L in loops:
        rosters = {t: {e[0] for e in builds[L][t][2]} for t in teams}
        allsp = [e[0] for t in teams for e in builds[L][t][2]]
        uniq = len(set(allsp))
        top = collections.Counter(allsp).most_common(3)
        avg = statistics.mean(bst(s) for s in allsp)
        if prev is None:
            carry = '—'
        else:
            keep = sum(len(rosters[t] & prev[t]) for t in teams)
            carry = f'{100*keep/(6*len(teams)):.0f}%'
        topstr = ', '.join(f'{name(s)}({n})' for s, n in top)
        print(f'{L:<6}{len(allsp):<12}{uniq:<10}{topstr:<28}{avg:<8.0f}{carry}')
        prev = rosters

    # ---- 天候型が実際に天候を握れているか ----
    print()
    print('=== 最終ループの構築に含まれる天候特性・攻撃後移動特性 ===')
    wtrait = {t['id']: t['weather_on_entry'] for t in traits.values() if t.get('weather_on_entry')}
    move_trait = {'fuwafuwa', 'yukisuberi'}
    last = builds[loops[-1]]
    for key in sorted(agg, key=lambda k: (JA[k[0]], JA[k[1]])):
        ts = [t for t in teams if tactic_of[t] == key]
        nw = sum(1 for t in ts for e in last[t][2]
                 if species.get(e[0], {}).get('trait') in wtrait)
        nm = sum(1 for t in ts for e in last[t][2]
                 if species.get(e[0], {}).get('trait') in move_trait)
        print(f'  {JA[key[0]]}＋{JA[key[1]]:<12} 天候特性 {nw:>2}体 / 攻撃後移動 {nm:>2}体 '
              f'（{len(ts)}チーム×6匹={len(ts)*6}体中）')


if __name__ == '__main__':
    main()
