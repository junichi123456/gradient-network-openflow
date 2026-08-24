#!/usr/bin/env python3
"""§21/§22「環境メタ（3すくみ）とその対抗構築」シミュレーションの結果を集計する。

  python3 Tools/analyze_meta_results.py <scratchpad_dir>

読むファイル:
  meta_core.csv                    3すくみ内3ペア + メタ×対抗構築90ペア
  meta_core.csv.meta_final.txt     3すくみの最終構築（適応後）
  meta_core.csv.challengers.txt    対抗構築30件の中身とbeats（生成時の予測）
  meta_core.csv.mutations.txt      適応イベントの全ログ
  meta_challengers_shard{0,1,2}.csv  対抗構築どうしの総当たり435ペア
"""
import sys
import statistics
from collections import defaultdict

def main():
    d = sys.argv[1] if len(sys.argv) > 1 else '.'

    # ---- 構築ラベル ----
    meta_names = {}
    with open(f'{d}/meta_core.csv.meta_final.txt', encoding='utf-8') as f:
        for line in f:
            label, rest = line.split(':', 1)
            meta_names[label.strip()] = rest.strip()[:60]

    challenger_beats = {}
    with open(f'{d}/meta_core.csv.challengers.txt', encoding='utf-8') as f:
        for line in f:
            head, rest = line.split(':', 1)
            cid, beats = head.split(' beats=')
            challenger_beats[cid.strip()] = (beats.strip('{}'), rest.strip()[:60])

    # ---- 集計器 ----
    wins = defaultdict(int)
    losses = defaultdict(int)
    draws = defaultdict(int)
    undecided = defaultdict(int)
    all_cycles = []
    meta_vs_challenger = defaultdict(dict)   # meta_vs_challenger['M0']['C5'] = (w,l)

    def record(a, b, wa, wb, dr, un, cycles):
        wins[a] += wa; wins[b] += wb
        losses[a] += wb; losses[b] += wa
        draws[a] += dr; draws[b] += dr
        undecided[a] += un; undecided[b] += un
        all_cycles.extend(cycles)

    with open(f'{d}/meta_core.csv', encoding='utf-8') as f:
        for line in f:
            row = line.rstrip('\n').split(',')
            kind, a, b, wa, wb, dr, un = row[0], row[1], row[2], *map(int, row[3:7])
            cycles = [int(c) for c in row[7].split('|')] if len(row) > 7 and row[7] else []
            la = f'M{a}'
            lb = f'M{b}' if kind == 'meta' else f'C{b}'
            record(la, lb, wa, wb, dr, un, cycles)
            if kind == 'vs':
                meta_vs_challenger[la][lb] = (wa, wb)

    total_pairs_core = 0
    with open(f'{d}/meta_core.csv', encoding='utf-8') as f:
        total_pairs_core = sum(1 for _ in f)

    total_pairs_chal = 0
    for k in range(3):
        with open(f'{d}/meta_challengers_shard{k}.csv', encoding='utf-8') as f:
            for line in f:
                row = line.rstrip('\n').split(',')
                i, j, wa, wb, dr, un = row[0], row[1], *map(int, row[2:6])
                cycles = [int(c) for c in row[6].split('|')] if len(row) > 6 and row[6] else []
                record(f'C{i}', f'C{j}', wa, wb, dr, un, cycles)
                total_pairs_chal += 1

    n_teams = len(wins)
    expected_pairs = n_teams * (n_teams - 1) // 2
    print(f'[マージ] meta_core {total_pairs_core}ペア + challengers {total_pairs_chal}ペア '
          + f'= {total_pairs_core + total_pairs_chal}ペア（期待 {expected_pairs}、{n_teams}構築）')

    total_decided = sum(wins.values())
    total_draws = sum(draws.values()) // 2
    total_undecided = sum(undecided.values()) // 2
    total_matches = total_decided + total_draws + total_undecided
    print(f'[結果] 総試合数: {total_matches}（決着{total_decided} '
          + f'/ 引き分け{total_draws} / 未決着{total_undecided}）')
    if all_cycles:
        print(f'[結果] 決着までのサイクル: 平均{statistics.mean(all_cycles):.1f} '
              + f'/ 最短{min(all_cycles)} / 最長{max(all_cycles)}')

    print()
    print('=== 3すくみ（環境メタ）の総合成績（対32構築、適応込み） ===')
    for m in ['M0', 'M1', 'M2']:
        played = wins[m] + losses[m] + draws[m] + undecided[m]
        rate = wins[m] / played if played else 0
        print(f'  {m} [{meta_names.get(m, "?")}]')
        print(f'    総合: {wins[m]}勝{losses[m]}敗{draws[m]}分{undecided[m]}未決着'
              + f'（{100*rate:.1f}%、{played}試合）')

    print()
    print('=== 各対抗構築 vs 3すくみ個別（狙い通り倒せたか） ===')
    header = f'{"構築":<5}{"予測beats":<10}{"vsM0":<8}{"vsM1":<8}{"vsM2":<8}{"命中数":<6}'
    print(header)
    hit_counts = []
    for cid in sorted(challenger_beats, key=lambda x: int(x[1:])):
        beats_str, desc = challenger_beats[cid]
        predicted = set(int(x) for x in beats_str.split(',') if x != '')
        cells = []
        hits = 0
        for mi, ml in enumerate(['M0', 'M1', 'M2']):
            wl = meta_vs_challenger.get(ml, {}).get(cid)
            if wl is None:
                cells.append('?')
                continue
            metaW, challengerW = wl
            beat = challengerW > metaW
            if beat and mi in predicted:
                hits += 1
            cells.append(f'{challengerW}-{metaW}{"*" if beat else ""}')
        hit_counts.append(hits)
        print(f'{cid:<5}{beats_str:<10}{cells[0]:<8}{cells[1]:<8}{cells[2]:<8}{hits}/{len(predicted)}')

    print()
    print(f'[的中率] 予測（beats>=2/3）のうち実戦で本当に勝ち越した割合: '
          + f'平均 {statistics.mean(hit_counts):.2f} / 3すくみ中')

    print()
    print('=== 33構築 総合勝率ランキング（上位15・下位10） ===')
    ranking = []
    for k in wins:
        played = wins[k] + losses[k] + draws[k] + undecided[k]
        if played == 0:
            continue
        ranking.append((wins[k] / played, k, played))
    ranking.sort(reverse=True)
    for rank, (rate, k, played) in enumerate(ranking[:15], 1):
        label = meta_names.get(k, challenger_beats.get(k, ('', k))[1] if k in challenger_beats else k)
        print(f'  {rank:>2}. {k:<4} {100*rate:5.1f}%  {wins[k]}勝{losses[k]}敗{draws[k]}分{undecided[k]}未決着'
              + f'（{played}試合）  {label}')
    print('  ...')
    for rank, (rate, k, played) in enumerate(ranking[-10:], len(ranking) - 9):
        label = meta_names.get(k, challenger_beats.get(k, ('', k))[1] if k in challenger_beats else k)
        print(f'  {rank:>2}. {k:<4} {100*rate:5.1f}%  {wins[k]}勝{losses[k]}敗{draws[k]}分{undecided[k]}未決着'
              + f'（{played}試合）  {label}')

    print()
    with open(f'{d}/meta_core.csv.mutations.txt', encoding='utf-8') as f:
        mutations = f.readlines()
    print(f'[適応] 総イベント数: {len(mutations)}')
    item_swaps = sum(1 for m in mutations if '持ち物' in m)
    move_swaps = sum(1 for m in mutations if '技' in m)
    print(f'  持ち物変更: {item_swaps} / 技変更: {move_swaps}')


if __name__ == '__main__':
    main()
