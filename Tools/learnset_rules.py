"""learnset 生成と検証が共有する規則テーブル。

generate_learnset.py と verify_learnset.py が同じ表をそれぞれ持っていたため、
片方だけ更新して検証が誤判定する事故が繰り返し起きた（特性を追加すると、
検証側が「特性適応技」を認識できずに属性上限違反として弾く）。
表の実体はこのモジュールだけに置き、両方から import する。
"""

# 専用技: 選抜も上限も通さず、指定の1種へ直接渡す。威力の天井を破ることが
# 存在意義そのものなので（メガデストラクト180をクルットリ BST205 が持つ）、
# 各種上限の検査からは除外する。move_id -> species_id
SIGNATURE = {'megaton_self_destruct': '053'}

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
    'poker_face',        # Straight・Flashの威力+10（該当技はすべてPhysical）
}
TRAIT_SPECIAL = {
    'hatsuen_kikan',     # ブレス系+10（該当9技はすべてSpecial）
}

# 4:3:3 で物理型/特殊型/打ち分け型に散らす。species_id 昇順の序数だけで
# 決まるので再現性はそのまま。
PROFILE_CYCLE = ['Physical', 'Physical', 'Physical', 'Physical',
                 'Special', 'Special', 'Special',
                 'Versatile', 'Versatile', 'Versatile']

# 上限表: (単属性/複合属性) x (物理型/特殊型/打ち分け型)
#   own     自属性攻撃技 (BST180時→BST500時)
#   atk     攻撃技合計   (同上)
#   off_pow 他属性の威力上限
#   off_per 他属性の1属性あたり種類数
#   st      変化技下限への加算
CEIL = {
    ('single', 'Physical'):  dict(own=(20, 11), atk=(26, 15), off_pow=110, off_per=2, st=0),
    ('single', 'Special'):   dict(own=(16,  9), atk=(23, 16), off_pow=100, off_per=2, st=0),
    ('single', 'Versatile'): dict(own=(18, 11), atk=(26, 17), off_pow= 90, off_per=3, st=2),
    ('dual',   'Physical'):  dict(own=(22, 13), atk=(27, 17), off_pow=100, off_per=2, st=0),
    ('dual',   'Special'):   dict(own=(21, 12), atk=(26, 16), off_pow= 90, off_per=2, st=0),
    ('dual',   'Versatile'): dict(own=(24, 13), atk=(29, 19), off_pow= 80, off_per=2, st=2),
}


def profile_of(trait_id, species_index):
    """種族の攻撃型。特性による指定が最優先、無ければ 4:3:3 の周期。"""
    if trait_id in TRAIT_PHYSICAL: return 'Physical', 'trait'
    if trait_id in TRAIT_SPECIAL:  return 'Special', 'trait'
    return PROFILE_CYCLE[species_index % len(PROFILE_CYCLE)], 'profile'
