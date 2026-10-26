"""learnset 生成と検証が共有する規則テーブル。

generate_learnset.py と verify_learnset.py が同じ表をそれぞれ持っていたため、
片方だけ更新して検証が誤判定する事故が繰り返し起きた（特性を追加すると、
検証側が「特性適応技」を認識できずに属性上限違反として弾く）。
表の実体はこのモジュールだけに置き、両方から import する。
"""
from fractions import Fraction
from statistics import median

# 専用技: 選抜も上限も通さず、指定の1種へ直接渡す。威力の天井を破ることが
# 存在意義そのものなので（メガデストラクト180をクルットリ BST205 が持つ）、
# 各種上限の検査からは除外する。move_id -> species_id
SIGNATURE = {'megaton_self_destruct': '053'}

# 制限技 (Phase 6): 攻撃技だが制限効果を追加効果に持つ12種。習得できるのは各1〜2種だけで、
# generate_learnset.py の選抜には載せず add_restriction_moves.py が level 30 で直接足す。
# SIGNATURE と同様に、属性ごとの種類数・威力上限の検査からは除外する。
# move_id -> [species_id, ...]
RESTRICTION_LEARNERS = {
    'rs_seal_snap':     ['189', '145'],    # ゼノグリフ・ゼノベーダ
    'rs_provoke':       ['080', '168'],    # ヘルガルダ・ウラミィ
    'rs_repeat_chant':  ['082', '170'],    # ミステリア・ネモフィ
    'rs_item_jam':      ['185', '161'],    # エレパンダ・オーマサンダ
    'rs_tide_hook':     ['075', '128'],    # シーペント・オオタチウオ
    'rs_frost_lag':     ['119', '095'],    # チルウィッチ・フブキツネ
    'rs_sand_eater':    ['139', '140'],    # アヌビス・セクメト
    'rs_scorch_smog':   ['104', '176'],    # イグニクス・ラピエール
    'rs_dragon_anchor': ['160', '124'],    # アイギルガ・フェスキー
    'rs_short_leash':   ['066', '152'],    # ヴィオレッタ・ラベロット
    'rs_nag_jab':       ['074', '033'],    # ゴリレイジ・ガウルフ
    'rs_hollow_touch':  ['157B', '150'],   # コスモディア・オーメン
}

# 規則4: 特性 → その特性が効果を及ぼすウェポンタグ。
# 生成側は該当タグの技をプール先頭へ寄せ、さらに特性適応技の下限
# (ADAPTED_MIN) を満たすまで採用範囲外からでも強制採用する。
# 検証側は、その強制採用ぶんを属性ごとの種類数・威力上限の検査から外す。
TRAIT_TAG = {
    'issen':            ('Slash', 'Thrust'),
    'tsume_no_kariudo': ('Fist', 'Punch'),
    'hatsuen_kikan':    ('Breath',),
    'body_press':       ('Crush', 'Rend'),
    'poker_face':       ('Straight', 'Flash'),
}

# 特性が技名を直接参照するもの。規則を無視して必ず習得させる。
TRAIT_NAMED = {'akumu_no_hitomi': ['nightmare_ball', 'nightmare_pulse']}

# 攻撃技の使用そのものが禁止される特性。持ち主には攻撃技を1つも配らない
# （ざんきょうのしゅごしゃは「攻撃技の使用が禁止される代わりに」味方を庇う）。
NO_ATTACK_TRAITS = {'zankyou_no_shugosha'}

# §3-a-1: 持ち主の攻撃を片方の分類へ寄せる特性。
# 攻撃側の強化のみ数える。防御特性（ハードアーマー等）は何を覚えるべきかを
# 示さないし、がんばりサポートは自分ではなく味方を強化する。
TRAIT_PHYSICAL = {
    'okorinbo',          # 接触技の与ダメージ+10%
    'issen',             # Slash・Thrustの与ダメージ+10%
    'tsume_no_kariudo',  # Fist・Punch系の与ダメージ+10%
    'moeru_kobushi',     # 接触技に炎を加算
    'eisou',             # 接触技の急所ランク+1
    'bakugeki',          # 竜技を物理の固定技へ差し替え
    'body_press',        # Crush・Rendが必中（該当技はすべてPhysical）
    'poker_face',        # Straight・Flashの威力1.5倍（該当技はすべてPhysical）
}
TRAIT_SPECIAL = {
    'hatsuen_kikan',     # ブレス系+10（該当9技はすべてSpecial）
}

# 4:3:3 で 物理攻撃/特殊攻撃/両刀攻撃 の「攻撃区分の候補」に散らす。species_id 昇順の
# 序数だけで決まるので再現性はそのまま。両刀攻撃になるのは、このうち俊敏条件を満たす種だけ
# （offense_of）。
OFFENSE_CYCLE = ['Physical', 'Physical', 'Physical', 'Physical',
                 'Special', 'Special', 'Special',
                 'Mixed', 'Mixed', 'Mixed']

# 上限表: (単属性/複合属性) x (物理攻撃/特殊攻撃/両刀攻撃)
#   own     自属性攻撃技 (BST180時→BST500時)
#   atk     攻撃技合計   (同上)
#   off_pow 他属性の威力上限
#   off_per 他属性の1属性あたり種類数
#   st      変化技下限への加算
CEIL = {
    ('single', 'Physical'):  dict(own=(20, 11), atk=(26, 15), off_pow=110, off_per=2, st=0),
    ('single', 'Special'):   dict(own=(16,  9), atk=(23, 16), off_pow=100, off_per=2, st=0),
    ('single', 'Mixed'): dict(own=(18, 11), atk=(26, 17), off_pow= 90, off_per=3, st=2),
    ('dual',   'Physical'):  dict(own=(22, 13), atk=(27, 17), off_pow=100, off_per=2, st=0),
    ('dual',   'Special'):   dict(own=(21, 12), atk=(26, 16), off_pow= 90, off_per=2, st=0),
    ('dual',   'Mixed'): dict(own=(24, 13), atk=(29, 19), off_pow= 80, off_per=2, st=2),
}


def legacy_had(s):
    """旧HAD (h, a, d)。HABCDS 導入後の species.json では base_* が新しい値なので、
    learnset 系ツールの内部尺度（BST3 = h+a+d、BST180〜500 前提の閾値）は
    legacy_had から読む。無ければ（HABCDS 導入前の species.json）base_* が旧HAD。"""
    h = s.get('legacy_had')
    if h:
        return h['hp'], h['atk'], h['def']
    return s['base_hp'], s['base_atk'], s['base_def']


def base_offense_of(trait_id, species_index):
    """攻撃区分の候補。特性による指定が最優先、無ければ 4:3:3 の周期。
    戻り値 (offense, 'trait'|'cycle')。offense は 'Physical'|'Special'|'Mixed'。"""
    if trait_id in TRAIT_PHYSICAL: return 'Physical', 'trait'
    if trait_id in TRAIT_SPECIAL:  return 'Special', 'trait'
    return OFFENSE_CYCLE[species_index % len(OFFENSE_CYCLE)], 'cycle'


def mixed_side_of(species_index):
    """両刀攻撃の主側（learnset の多数側）/ 両刀候補から再分類するときの行き先。
    序数が偶数なら Physical、奇数なら Special。"""
    return 'Physical' if species_index % 2 == 0 else 'Special'


# 両刀攻撃の条件（仕様書 2026-10-26-stat-rework.md S1-c / S1-d）
MIXED_ZONE = Fraction(4, 100)       # 俊敏ゾーン: BST6 ±4%
MIXED_ZONE_MIN = 10                 # 10種未満なら近い順に10種まで広げる
MIXED_MAIN_MIN = Fraction(11, 21)   # 主側比率の下限（max(A,C) >= 1.10*min(A,C)）
MIXED_MAIN_MAX = Fraction(3, 5)     # 主側比率の上限（40:60）


def speed_zone(sid, table):
    """俊敏ゾーン。table は {species_id: (BST6, S)}（全287種）。自分は含めない。"""
    b = table[sid][0]
    z = [t for t in table if t != sid and abs(table[t][0] - b) <= MIXED_ZONE * b]
    if len(z) >= MIXED_ZONE_MIN:
        return z
    near = sorted((abs(table[t][0] - b), t) for t in table if t != sid)
    lim = near[MIXED_ZONE_MIN - 1][0]
    return [t for dist, t in near if dist <= lim]


def offense_of(sid, trait_id, species_index, table):
    """確定した攻撃区分。戻り値 (offense, offense_source)。
    offense_source: 'trait' / 'cycle'（候補のまま）/ 'speed'（両刀候補を俊敏条件で再分類）。
    両刀候補は S がゾーンの S の中央値より大きければ 'Mixed'（等しいときは満たさない）。"""
    base, why = base_offense_of(trait_id, species_index)
    if base != 'Mixed':
        return base, why
    zs = [table[t][1] for t in speed_zone(sid, table)]
    if Fraction(table[sid][1]) > Fraction(median(zs)):
        return 'Mixed', 'cycle'
    return mixed_side_of(species_index), 'speed'


def offense_from_species(s, species_index):
    """learnset 系ツールが見る攻撃区分。species.json に offense があればそれ、無ければ候補。"""
    o = s.get('offense')
    if o:
        return o
    return base_offense_of(s['trait'], species_index)[0]


# 耐久区分: ハードアーマー/ハードブロックは向きを強める側、残りは序数の周期で散らす。
DEFENSE_CYCLE = ['Physical', 'Special', 'Both']


def defense_of(trait_id, species_index):
    if trait_id == 'hard_armor': return 'Physical'
    if trait_id == 'hard_block': return 'Special'
    return DEFENSE_CYCLE[species_index % len(DEFENSE_CYCLE)]


# 種族値 S が 95 以上の個体は、優先度 +1 以上の攻撃技（威力>0）を習得しない。
FAST_SPEED_MIN = 95


def fast_blocks(species, move):
    return (species['base_speed'] >= FAST_SPEED_MIN
            and move.get('priority', 0) >= 1 and move['power'] > 0)


OFFENSE_JA = {'Physical': '物理攻撃', 'Special': '特殊攻撃', 'Mixed': '両刀攻撃'}
DEFENSE_JA = {'Physical': '物理耐久', 'Special': '特殊耐久', 'Both': '両面耐久'}
