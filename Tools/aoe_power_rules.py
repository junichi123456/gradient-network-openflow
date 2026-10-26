# -*- coding: utf-8 -*-
"""範囲技（射程 Room/Area/Surrounding の攻撃技）の威力引き下げの表と割り当て。

生成（apply_aoe_power_cut.py）と検証（verify_attack_moves.py・verify_learnset.py）が
同じ表を見るため、表はこのモジュールだけに置く。仕様書 docs/design/drafts/2026-10-27-aoe-power.md。
"""
import json, os, itertools

AOE_RANGES = ('Room', 'Area', 'Surrounding')
AOE_EXCLUDED = {'megaton_self_destruct': '専用技', 'rs_scorch_smog': '制限技',
                'MV_170': '物理の唯一の範囲技'}

# 前提コミット b7bb0a6 の Data/moves.json の値: {id: (基準威力, 基準命中)}。基準威力の唯一の出典。
AOE_BASE = {
    'MV_153': (50, 100),  # エナジーブラスト Neutral Room
    'MV_160': (25, 100),  # クリティカルエイム Neutral Room
    'MV_159': (130, 65),  # シャッタースカイ Neutral Area
    'mvn_004': (30, 90),  # ベアスマッシュ Neutral Area
    'MV_244': (120, 65),  # フレイムゲイルブレス Fire Room
    'MV_243': (110, 65),  # ヘルファイアヴォルテックス Fire Room
    'mvn_022': (55, 100),  # カーボナイズ Fire Room
    'MV_247': (20, 100),  # ファイアスフィア Fire Room
    'volcanic_rain': (90, 100),  # ボルカニックレイン Fire Area
    'MV_248': (65, 100),  # フレイムバーンブレス Fire Area
    'MV_224': (125, 65),  # グレートタイダルボア Water Room
    'MV_223': (100, 100),  # タイダルウェーブ Water Room
    'MV_228': (80, 100),  # ウォータートルネード Water Room
    'MV_231': (70, 100),  # バブルビーム Water Room
    'water_pillar_burst': (115, 100),  # ウォーターピラーバースト Water Area
    'MV_215': (35, 100),  # ウォーターカッター Water Area
    'be_quiet': (130, 100),  # ビー・クワイエット Grass Room
    'spine_vine': (45, 100),  # スパインヴァイン Grass Room
    'mvn_060': (25, 100),  # グラスリング Grass Room
    'MV_292': (125, 65),  # フォレストラース Grass Area
    'MV_285': (25, 100),  # グラスノット Grass Area
    'MV_186': (85, 100),  # ロアリングサンダー Electric Room
    'all_range_thunder': (70, 100),  # オールレンジサンダー Electric Room
    'mvn_040': (45, 100),  # ボルトトレイル Electric Room
    'thunder_rail': (110, 100),  # サンダーレール Electric Area
    'MV_185': (25, 100),  # ボルトライトバレット Electric Area
    'MV_279': (120, 65),  # コラプスロックスマッシュ Ground Room
    'MV_265': (110, 100),  # テクトニックシフト Ground Room
    'mvn_055': (55, 100),  # ソリッドソイル Ground Room
    'MV_280': (20, 100),  # サンドバインド Ground Room
    'sand_twister': (100, 100),  # サンドツイスター Ground Area
    'MV_268': (65, 100),  # サンドストームスマッシュ Ground Area
    'MV_197': (95, 85),  # アブソリュートゼロウェーブ Ice Room
    'MV_196': (85, 100),  # アイスストーム Ice Room
    'MV_193': (70, 100),  # フロストバーンブレス Ice Room
    'MV_191': (60, 100),  # フリーズウェーブ Ice Room
    'MV_210': (135, 65),  # フロストゲイルブレス Ice Area
    'MV_205': (30, 95),  # フロストネット Ice Area
    'MV_101': (100, 70),  # アルティメットドラゴンカノン Dragon Room
    'beam_slicer': (65, 100),  # ビームスライサー Dragon Room
    'mvn_082': (30, 100),  # ドラゴンクロー Dragon Room
    'MV_102': (120, 70),  # ドラゴンスレイヤーロア Dragon Area
    'MV_115': (60, 100),  # ヤングドラゴンロア Dragon Area
    'dark_wisp': (85, 100),  # ダークウィスプ Dark Room
    'mvn_099': (55, 100),  # シャドウアレイ Dark Room
    'mvn_092': (20, 90),  # ダークネス Dark Room
    'MV_136': (105, 70),  # ハデスジャッジメント Dark Area
    'MV_127': (25, 100),  # ダークスケイルスマッシュ Dark Area
    'void_dark_wisp': (85, 100),  # ホロウウィスプ Void Room
    'void_mvn_099': (55, 100),  # ヴォイドアレイ Void Room
    'void_mvn_092': (20, 90),  # ホロウネス Void Room
    'void_MV_136': (105, 70),  # ヌルジャッジメント Void Area
    'void_MV_127': (25, 100),  # ホロウスケイルスマッシュ Void Area
}

# 現在適用中の案 (削減幅D, 命中-5併用か)。apply_aoe_power_cut.py がこの1行を書き換える。
ADOPTED = (0, False)

_MOVES_PATH = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'Data', 'moves.json')


def base_power(m):
    """対象技なら基準威力、それ以外は moves.json の威力。"""
    return AOE_BASE[m['id']][0] if m['id'] in AOE_BASE else m['power']


def acc_cut(move_id, acc):
    """命中-5 併用のとき、基準威力 80 以上の対象技だけ 5。"""
    return 5 if acc and AOE_BASE[move_id][0] >= 80 else 0


def _max_hits(m):
    mode = m.get('multi_hit', 'None')
    if mode == 'Variable2To5':
        return 5
    if mode == 'RepeatPerHit':
        return m.get('multi_hit_count') or 1
    return 1


def _load_moves():
    return json.load(open(_MOVES_PATH, encoding='utf-8'))


def solve(D, moves=None):
    """{move_id: 削減量}（53件）。D=0 は全件 0。"""
    if D == 0:
        return {i: 0 for i in AOE_BASE}
    if D not in (10, 15, 20):
        raise ValueError('D は 0/10/15/20')
    ms = moves if moves is not None else _load_moves()
    atk = [m for m in ms if m['category'] in ('Physical', 'Special') and m['power'] > 0]
    typ = {m['id']: m['type'] for m in ms}
    cat = {m['id']: m['category'] for m in ms}
    rng = {m['id']: m.get('range') for m in ms}
    out = {}
    # 虚は闇の削減量を写す。闇の解では闇・虚の両方の制約を見る。
    groups = {}
    for i in AOE_BASE:
        if i.startswith('void_'):
            continue
        groups.setdefault(typ[i], []).append(i)
    for el, ids in groups.items():
        els = [el] + (['Void'] if el == 'Dark' else [])
        ids = sorted(ids, key=lambda i: (-AOE_BASE[i][0], i))
        opts = [[d for d in (10, 15, 20) if AOE_BASE[i][0] - d >= 10] for i in ids]
        # 対象外の攻撃技（rs_ を除く）の実効威力カウント
        fixed = {}
        for m in atk:
            if m['id'].startswith('rs_') or m['id'] in AOE_BASE or m['type'] not in els:
                continue
            k = (m['type'], m['category'], m['power'] * _max_hits(m))
            fixed[k] = fixed.get(k, 0) + 1
        fixed_room = {e: [m['power'] for m in atk if m.get('range') == 'Room' and m['type'] == e
                          and m['id'] not in AOE_BASE] for e in els}
        best = None
        for combo in itertools.product(*opts):
            cnt = dict(fixed)
            room = {e: list(v) for e, v in fixed_room.items()}
            ok = True
            for i, d in zip(ids, combo):
                np_ = AOE_BASE[i][0] - d
                for t in ([i] + (['void_' + i] if el == 'Dark' else [])):
                    k = (typ[t], cat[t], np_)
                    cnt[k] = cnt.get(k, 0) + 1
                    if cnt[k] > 2:
                        ok = False
                    if rng[t] == 'Room':
                        room[typ[t]].append(np_)
            if not ok:
                continue
            for pw in room.values():
                pw = sorted(pw)
                if any(pw[j + 1] - pw[j] < 10 for j in range(len(pw) - 1)):
                    ok = False
            if not ok:
                continue
            nom = [10 if AOE_BASE[i][0] <= 35 else D for i in ids]
            key = (sum(abs(d - n) for d, n in zip(combo, nom)),
                   sum(d != n for d, n in zip(combo, nom)),
                   tuple(-d for d in combo))
            if best is None or key < best[0]:
                best = (key, combo)
        if best is None:
            raise RuntimeError(f'属性 {el} に D={D} の解がない')
        for i, d in zip(ids, best[1]):
            out[i] = d
            if el == 'Dark':
                out['void_' + i] = d
    return out
