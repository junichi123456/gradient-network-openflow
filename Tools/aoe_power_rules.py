# -*- coding: utf-8 -*-
"""特殊の範囲技（射程 Room/Area/Surrounding の攻撃技）の整理の表。

生成（apply_aoe_power_cut.py）と検証（verify_attack_moves.py・verify_learnset.py）が
同じ表を見るため、表はこのモジュールだけに置く。値の出典は仕様書
docs/design/drafts/2026-10-27-aoe-power.md の付録A（前提コミット b7bb0a6 の Data から機械的に作成）。
"""

AOE_RANGES = ('Room', 'Area', 'Surrounding')
EVAL_BONUS = {'Room': 25, 'Area': 15, 'Surrounding': 10}   # learnset 規則・威力グリッドの評価威力への加算
AOE_EXCLUDED = {'megaton_self_destruct': '専用技', 'rs_scorch_smog': '制限技',
                'MV_170': '物理の範囲技（対象外）'}

# 削除する15件（付録A-2「削除」。闇と虚は対）。
AOE_DELETED = ['mvn_022', 'MV_247', 'MV_231', 'MV_215', 'MV_285', 'mvn_060', 'mvn_040', 'MV_185', 'mvn_055', 'MV_280', 'MV_191', 'MV_205', 'mvn_082', 'mvn_092', 'void_mvn_092']

# 残す38件の基準値 {id: (基準威力, 基準命中)}（前提コミットの値）。
AOE_BASE = {
    'MV_159': (130, 65),  # シャッタースカイ 無 範囲
    'MV_153': (50, 100),  # エナジーブラスト 無 部屋
    'mvn_004': (30, 90),  # ベアスマッシュ 無 範囲
    'MV_160': (25, 100),  # クリティカルエイム 無 部屋
    'MV_244': (120, 65),  # フレイムゲイルブレス 炎 部屋
    'MV_243': (110, 65),  # ヘルファイアヴォルテックス 炎 部屋
    'volcanic_rain': (90, 100),  # ボルカニックレイン 炎 範囲
    'MV_248': (65, 100),  # フレイムバーンブレス 炎 範囲
    'MV_224': (125, 65),  # グレートタイダルボア 水 部屋
    'water_pillar_burst': (115, 100),  # ウォーターピラーバースト 水 範囲
    'MV_223': (100, 100),  # タイダルウェーブ 水 部屋
    'MV_228': (80, 100),  # ウォータートルネード 水 部屋
    'be_quiet': (130, 100),  # ビー・クワイエット 草 部屋
    'MV_292': (125, 65),  # フォレストラース 草 範囲
    'spine_vine': (45, 100),  # スパインヴァイン 草 部屋
    'thunder_rail': (110, 100),  # サンダーレール 電 範囲
    'MV_186': (85, 100),  # ロアリングサンダー 電 部屋
    'all_range_thunder': (70, 100),  # オールレンジサンダー 電 部屋
    'MV_279': (120, 65),  # コラプスロックスマッシュ 地 部屋
    'MV_265': (110, 100),  # テクトニックシフト 地 部屋
    'sand_twister': (100, 100),  # サンドツイスター 地 範囲
    'MV_268': (65, 100),  # サンドストームスマッシュ 地 範囲
    'MV_210': (135, 65),  # フロストゲイルブレス 氷 範囲
    'MV_197': (95, 85),  # アブソリュートゼロウェーブ 氷 部屋
    'MV_196': (85, 100),  # アイスストーム 氷 部屋
    'MV_193': (70, 100),  # フロストバーンブレス 氷 部屋
    'MV_102': (120, 70),  # ドラゴンスレイヤーロア 竜 範囲
    'MV_101': (100, 70),  # アルティメットドラゴンカノン 竜 部屋
    'beam_slicer': (65, 100),  # ビームスライサー 竜 部屋
    'MV_115': (60, 100),  # ヤングドラゴンロア 竜 範囲
    'MV_136': (105, 70),  # ハデスジャッジメント 闇 範囲
    'dark_wisp': (85, 100),  # ダークウィスプ 闇 部屋
    'mvn_099': (55, 100),  # シャドウアレイ 闇 部屋
    'MV_127': (25, 100),  # ダークスケイルスマッシュ 闇 範囲
    'void_MV_136': (105, 70),  # ヌルジャッジメント 虚 範囲
    'void_dark_wisp': (85, 100),  # ホロウウィスプ 虚 部屋
    'void_mvn_099': (55, 100),  # ヴォイドアレイ 虚 部屋
    'void_MV_127': (25, 100),  # ホロウスケイルスマッシュ 虚 範囲
}

# 案ごとの削減量 {D: {id: 削減}}。探索はしない。付録A-2 の各案の列（※のずらし込み）。
CUT_TABLE = {
    10: {
        'MV_159': 10,
        'MV_153': 20,
        'mvn_004': 10,
        'MV_160': 10,
        'MV_244': 10,
        'MV_243': 10,
        'volcanic_rain': 15,
        'MV_248': 15,
        'MV_224': 10,
        'water_pillar_burst': 10,
        'MV_223': 10,
        'MV_228': 10,
        'be_quiet': 10,
        'MV_292': 10,
        'spine_vine': 15,
        'thunder_rail': 10,
        'MV_186': 10,
        'all_range_thunder': 20,
        'MV_279': 10,
        'MV_265': 10,
        'sand_twister': 15,
        'MV_268': 15,
        'MV_210': 10,
        'MV_197': 15,
        'MV_196': 15,
        'MV_193': 10,
        'MV_102': 10,
        'MV_101': 10,
        'beam_slicer': 10,
        'MV_115': 10,
        'MV_136': 10,
        'dark_wisp': 10,
        'mvn_099': 10,
        'MV_127': 10,
        'void_MV_136': 10,
        'void_dark_wisp': 10,
        'void_mvn_099': 10,
        'void_MV_127': 10,
    },
    15: {
        'MV_159': 15,
        'MV_153': 20,
        'mvn_004': 10,
        'MV_160': 10,
        'MV_244': 15,
        'MV_243': 15,
        'volcanic_rain': 15,
        'MV_248': 15,
        'MV_224': 15,
        'water_pillar_burst': 15,
        'MV_223': 15,
        'MV_228': 15,
        'be_quiet': 15,
        'MV_292': 15,
        'spine_vine': 15,
        'thunder_rail': 15,
        'MV_186': 15,
        'all_range_thunder': 20,
        'MV_279': 15,
        'MV_265': 15,
        'sand_twister': 15,
        'MV_268': 15,
        'MV_210': 15,
        'MV_197': 15,
        'MV_196': 15,
        'MV_193': 15,
        'MV_102': 15,
        'MV_101': 15,
        'beam_slicer': 15,
        'MV_115': 15,
        'MV_136': 15,
        'dark_wisp': 20,
        'mvn_099': 15,
        'MV_127': 10,
        'void_MV_136': 15,
        'void_dark_wisp': 20,
        'void_mvn_099': 15,
        'void_MV_127': 10,
    },
    20: {
        'MV_159': 20,
        'MV_153': 20,
        'mvn_004': 10,
        'MV_160': 10,
        'MV_244': 20,
        'MV_243': 20,
        'volcanic_rain': 15,
        'MV_248': 20,
        'MV_224': 20,
        'water_pillar_burst': 20,
        'MV_223': 20,
        'MV_228': 20,
        'be_quiet': 20,
        'MV_292': 10,
        'spine_vine': 20,
        'thunder_rail': 20,
        'MV_186': 15,
        'all_range_thunder': 20,
        'MV_279': 20,
        'MV_265': 20,
        'sand_twister': 20,
        'MV_268': 20,
        'MV_210': 15,
        'MV_197': 20,
        'MV_196': 20,
        'MV_193': 20,
        'MV_102': 20,
        'MV_101': 20,
        'beam_slicer': 20,
        'MV_115': 20,
        'MV_136': 20,
        'dark_wisp': 20,
        'mvn_099': 20,
        'MV_127': 10,
        'void_MV_136': 20,
        'void_dark_wisp': 20,
        'void_mvn_099': 20,
        'void_MV_127': 10,
    },
}

# 現在適用中の案 (削減幅D, 命中-5併用か)。apply_aoe_power_cut.py がこの1行を書き換える。
ADOPTED = (15, False)

# 付録A-3: learnset から除く9行 (species_id, move_id)。
ROW_REMOVALS = [
    ('036', 'MV_196'),  # ドンモップ アイスストーム
    ('039', 'MV_153'),  # メリポップ エナジーブラスト
    ('042', 'MV_186'),  # ボルトラ ロアリングサンダー
    ('062', 'MV_101'),  # パチマル アルティメットドラゴンカノン
    ('114', 'MV_193'),  # クジャンナ フロストバーンブレス
    ('134', 'MV_193'),  # ヒエティ フロストバーンブレス
    ('152', 'MV_101'),  # ラベロット アルティメットドラゴンカノン
    ('191', 'MV_193'),  # ヒョウガオー フロストバーンブレス
    ('200', 'MV_193'),  # グレイシャル フロストバーンブレス
]

# 付録A-4: NPC 構築の置換 (team_id, species_id, 旧move_id, 新move_id)。
NPC_REPLACEMENTS = [
    ('npc_water', '046', 'MV_191', 'gap_water_s_60'),
    ('npc_electric', '083B', 'MV_231', 'all_range_thunder'),
    ('npc_dark', '186B', 'mvn_022', 'mvn_098'),
    ('npc_dark', '171', 'mvn_022', 'mvn_099'),
]

# 付録A-5: 威力グリッドの免除40点 (属性, 分類, 威力)。
GRID_EXEMPT = {
    ('Dark', 'Special', 20),
    ('Dark', 'Special', 25),
    ('Dark', 'Special', 55),
    ('Dark', 'Special', 85),
    ('Dark', 'Special', 105),
    ('Dragon', 'Special', 60),
    ('Dragon', 'Special', 65),
    ('Dragon', 'Special', 100),
    ('Dragon', 'Special', 120),
    ('Electric', 'Special', 25),
    ('Electric', 'Special', 85),
    ('Electric', 'Special', 110),
    ('Fire', 'Special', 20),
    ('Fire', 'Special', 55),
    ('Fire', 'Special', 65),
    ('Fire', 'Special', 90),
    ('Fire', 'Special', 110),
    ('Fire', 'Special', 120),
    ('Grass', 'Special', 25),
    ('Ground', 'Special', 20),
    ('Ground', 'Special', 55),
    ('Ground', 'Special', 65),
    ('Ground', 'Special', 100),
    ('Ground', 'Special', 120),
    ('Ice', 'Special', 60),
    ('Ice', 'Special', 70),
    ('Ice', 'Special', 85),
    ('Ice', 'Special', 95),
    ('Neutral', 'Special', 25),
    ('Neutral', 'Special', 50),
    ('Void', 'Special', 20),
    ('Void', 'Special', 25),
    ('Void', 'Special', 55),
    ('Void', 'Special', 85),
    ('Void', 'Special', 105),
    ('Water', 'Special', 35),
    ('Water', 'Special', 70),
    ('Water', 'Special', 80),
    ('Water', 'Special', 100),
    ('Water', 'Special', 115),
}


def eval_power(m):
    """learnset 規則と威力グリッドで使う評価威力。範囲技（威力>0）は実際の威力＋射程の加算。"""
    if m['power'] > 0 and m.get('range') in AOE_RANGES:
        return m['power'] + EVAL_BONUS[m['range']]
    return m['power']


def acc_cut(move_id, acc):
    """命中-5 併用のとき、基準威力 80 以上の対象技だけ 5。"""
    return 5 if acc and AOE_BASE[move_id][0] >= 80 else 0
