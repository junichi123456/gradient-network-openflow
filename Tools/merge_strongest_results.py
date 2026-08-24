#!/usr/bin/env python3
"""「最も強い構築」総当たり（BattleMatchScene --strongest, shard分割）の
結果CSVをマージし、構築ごとの勝率ランキングと全体統計を出す。

  python3 Tools/merge_strongest_results.py <out_prefix> [--shards N]

<out_prefix> は --out に渡したパスと同じもの（例: strongest_shard を
渡したなら、実際には strongest_shard0.csv 〜 strongest_shard(N-1).csv と
strongest_shard0.csv.teams.txt を読む——shard0だけがteamsを書く仕様）。
"""

import argparse
import csv
import statistics
import sys


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('prefix')
    ap.add_argument('--shards', type=int, default=4)
    args = ap.parse_args()

    teams_path = f'{args.prefix}0.csv.teams.txt'
    try:
        with open(teams_path, encoding='utf-8') as f:
            teams = {}
            for line in f:
                idx, desc = line.rstrip('\n').split(': ', 1)
                teams[int(idx)] = desc
    except FileNotFoundError:
        print(f'警告: {teams_path} が無い（構築の中身は番号のみで表示）', file=sys.stderr)
        teams = {}

    wins = {}
    losses = {}
    draws = {}
    undecided = {}
    all_cycles = []
    seen_pairs = set()
    total_rows = 0

    for k in range(args.shards):
        path = f'{args.prefix}{k}.csv'
        try:
            f = open(path, encoding='utf-8')
        except FileNotFoundError:
            print(f'警告: {path} が無い（そのshardはまだ完走していない?）', file=sys.stderr)
            continue
        with f:
            for row in csv.reader(f):
                if not row:
                    continue
                i, j, wi, wj, dr, un = (int(x) for x in row[:6])
                cycles = [int(c) for c in row[6].split('|')] if len(row) > 6 and row[6] else []

                pair = (i, j)
                if pair in seen_pairs:
                    print(f'警告: ペア{pair}が重複して記録されている（shard分割が壊れている疑い）',
                          file=sys.stderr)
                seen_pairs.add(pair)
                total_rows += 1

                for idx in (i, j):
                    wins.setdefault(idx, 0)
                    losses.setdefault(idx, 0)
                    draws.setdefault(idx, 0)
                    undecided.setdefault(idx, 0)
                wins[i] += wi; wins[j] += wj
                losses[i] += wj; losses[j] += wi
                draws[i] += dr; draws[j] += dr
                undecided[i] += un; undecided[j] += un
                all_cycles.extend(cycles)

    n_teams = len(wins)
    expected_pairs = n_teams * (n_teams - 1) // 2
    print(f'[マージ] {total_rows}ペア読み込み（期待 {expected_pairs}ペア、'
          + f'{n_teams}構築） {"OK" if total_rows == expected_pairs else "件数が一致しない"}')

    # 勝ち負けは1試合につきどちらか一方だけに+1、引き分け/未決着は両陣営に
    # +1ずつ入るため、試合数は「勝ちの合計」+「引き分けの合計/2」+「未決着の合計/2」。
    total_decided = sum(wins.values())
    total_draws_2x = sum(draws.values())
    total_undecided_2x = sum(undecided.values())
    total_matches = total_decided + total_draws_2x // 2 + total_undecided_2x // 2

    print(f'[結果] 総試合数: {total_matches}（決着{total_decided} '
          + f'/ 引き分け{total_draws_2x // 2} / 未決着{total_undecided_2x // 2}）')
    if all_cycles:
        print(f'[結果] 決着までのサイクル: 平均{statistics.mean(all_cycles):.1f} '
              + f'/ 最短{min(all_cycles)} / 最長{max(all_cycles)}'
              + f'（{len(all_cycles)}試合ぶん）')

    print()
    print('[ランキング] 勝率上位20構築:')
    ranking = []
    for idx in wins:
        played = wins[idx] + losses[idx] + draws[idx] + undecided[idx]
        if played == 0:
            continue
        rate = wins[idx] / played
        ranking.append((rate, idx, played))
    ranking.sort(reverse=True)

    for rank, (rate, idx, played) in enumerate(ranking[:20], 1):
        desc = teams.get(idx, f'(構築#{idx})')
        print(f'  {rank:>3}. #{idx:<3} {100*rate:5.1f}%  '
              + f'{wins[idx]}勝{losses[idx]}敗{draws[idx]}分{undecided[idx]}未決着'
              + f'（{played}試合）  {desc}')

    print()
    print('[ランキング] 勝率下位10構築:')
    for rank, (rate, idx, played) in enumerate(ranking[-10:], len(ranking) - 9):
        desc = teams.get(idx, f'(構築#{idx})')
        print(f'  {rank:>3}. #{idx:<3} {100*rate:5.1f}%  '
              + f'{wins[idx]}勝{losses[idx]}敗{draws[idx]}分{undecided[idx]}未決着'
              + f'（{played}試合）  {desc}')

    rates = [r for r, _, _ in ranking]
    if rates:
        print()
        print(f'[分布] 勝率: 平均{100*statistics.mean(rates):.1f}% '
              + f'/ 標準偏差{100*statistics.pstdev(rates):.1f}pt '
              + f'/ 最高{100*max(rates):.1f}% / 最低{100*min(rates):.1f}%')


if __name__ == '__main__':
    main()
