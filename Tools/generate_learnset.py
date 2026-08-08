#!/usr/bin/env python3
"""learnset generation (§2-§5). Deterministic: no RNG anywhere.

Every "pick one" is resolved by an explicit sort key, per §4.
"""
import json, math, collections

SPECIES = 'Data/species.json'
MOVES   = 'Data/moves.json'
CHART   = 'Data/type_chart.json'

# ---------------------------------------------------------------- helpers
def spread_take(ordered, k):
    """威力昇順の `ordered` から k 件を等間隔に採る。先頭 k 件を採ると
    上限を超えた分が必ず最強側から捨てられ、誰も高威力技を覚えられなく
    なるため、幅を残す採り方に統一している。"""
    n = len(ordered)
    if k <= 0 or n == 0: return []
    if k >= n: return list(ordered)
    idx = sorted({round(i * (n - 1) / (k - 1)) if k > 1 else 0 for i in range(k)})
    out = [ordered[i] for i in idx]
    for m in ordered:
        if len(out) >= k: break
        if m not in out: out.append(m)
    return out[:k]


def round_half_up(x):
    """四捨五入. Python's round() is banker's rounding, which would send
    x.5 to the nearest EVEN integer - not what 四捨五入 means."""
    return int(math.floor(x + 0.5))

def total_of(s):
    return s['base_hp'] + s['base_atk'] + s['base_def']

# ---------------------------------------------------------------- §1 chart
tc = json.load(open(CHART)); TYPES = tc['types']; MAT = tc['matrix']
tidx = {t: i for i, t in enumerate(TYPES)}
def mult(a, d): return MAT[tidx[a]][tidx[d]]
WEAK = {X: [Y for Y in TYPES if mult(Y, X) == 2.0] for X in TYPES}
COMP = {X: sorted({Z for W in WEAK[X] for Z in WEAK[W]}, key=TYPES.index) for X in TYPES}

# ---------------------------------------------------------------- move pool
moves = json.load(open(MOVES))
# メガデストラクトはクルットリ(053)の専用技。共有プールから外し、
# その1種にだけ直接配る。
SIGNATURE = {'megaton_self_destruct': '053'}

# 攻撃技の使用そのものが禁止される特性。持ち主には攻撃技を1つも配らない
# （ざんきょうのしゅごさは「攻撃技の使用が禁止される代わりに」味方を庇う）。
NO_ATTACK_TRAITS = {'zankyou_no_shugosha'}

# §4: 共有率の上限。設置技は11%未満、単純天候変化技は70%まで。
# 威力105以上は「配りすぎない」ため、1種族あたりの本数と全体の共有率の
# 両方を絞る。
FIELD_SHARE = 0.11
WEATHER_SHARE = 0.70
HIGH_POWER = 105
HIGH_PER_SPECIES = 2
HIGH_SHARE = 0.20

ATTACK = [m for m in moves if m['power'] > 0 and m['id'] not in SIGNATURE]
STATUS = [m for m in moves if m['power'] == 0]
FIELD_MOVES = [m for m in moves if m.get('field_placement', 'None') != 'None']

# §7-2: per-element Physical/Special census of the FULL pool, computed once.
# 4:3:3 で物理型/特殊型/器用型に散らす。species_id 昇順の序数だけで決まる
# ので再現性はそのまま。
PROFILE_CYCLE = ['Physical','Physical','Physical','Physical',
                 'Special','Special','Special',
                 'Versatile','Versatile','Versatile']

ELEM_BIAS = {}
for el in TYPES:
    c = collections.Counter(m['category'] for m in moves if m['type'] == el)
    ELEM_BIAS[el] = 'Physical' if c['Physical'] > c['Special'] else 'Special'

# §3-a-1: traits that push the holder's own offence toward one category.
# Only OFFENSIVE power/crit boosters count - defensive traits (ハードアーマー
# etc.) say nothing about what the holder should learn, and がんばりサポート
# buffs OTHER party members rather than its own holder.
TRAIT_PHYSICAL = {
    'okorinbo',          # 接触技の与ダメージ+10%
    'issen',             # 斬る系+10%
    'tsume_no_kariudo',  # ツメ・こぶし系+10%
    'moeru_kobushi',     # 接触技に炎を加算
    'eisou',             # 接触技の急所ランク+1
    'bakugeki',          # 竜技を物理の固定技へ差し替え
}
TRAIT_SPECIAL = {
    'hatsuen_kikan',     # ブレス系+10 (該当5技はすべてSpecial)
}

# Field moves that "match" an element, for §3-b's 設置技 requirement.
FIELD_BY_ELEMENT = collections.defaultdict(list)
for m in FIELD_MOVES:
    FIELD_BY_ELEMENT[m['type']].append(m['id'])
for el in FIELD_BY_ELEMENT:
    FIELD_BY_ELEMENT[el].sort()

def pick_moves(cands, quota, taken_ids):
    """Deterministic take: weakest first, ties by move id."""
    out = []
    for m in sorted(cands, key=lambda m: (m['power'], m['id'])):
        if len(out) >= quota: break
        if m['id'] in taken_ids: continue
        out.append(m); taken_ids.add(m['id'])
    return out

def take_in_order(cands, quota, taken_ids):
    """Like pick_moves but honours the order it is handed, instead of
    re-sorting. Used for the rotated status list below, where the order IS
    the rule."""
    out = []
    for m in cands:
        if len(out) >= quota: break
        if m['id'] in taken_ids: continue
        out.append(m); taken_ids.add(m['id'])
    return out

# ---------------------------------------------------------------- §5-b
# 変化技の「強さ」。攻撃技は威力が強さの尺度になるが変化技は power=0 で
# 揃っており、そのままでは全部が最低レベルに落ちる。効果そのものを点数化
# して、弱い技ほど低いレベルで覚えるようにする。
RANK_WEIGHT = {
    # 属性威力ランクは+2で同属性技が威力x2.0になる。他のランク1段とは
    # 桁が違うので倍の重みを与える。
    'ElementPower': 2.0,
}

def rank_effect_list(m):
    """rank_effects 配列と旧来の単一スロットを1つの形に揃える。"""
    if m.get('rank_effects'):
        return m['rank_effects']
    if m.get('rank_effect_stat', 'None') != 'None' and m.get('rank_effect_delta', 0) != 0:
        return [{'stat': m['rank_effect_stat'], 'delta': m['rank_effect_delta'],
                 'target': m.get('rank_effect_target', 'Self'),
                 'chance': m.get('rank_effect_chance', 1.0)}]
    return []

def status_score(m):
    score = 0.0
    for e in rank_effect_list(m):
        w = RANK_WEIGHT.get(e['stat'], 1.0)
        # 自分を上げるのも相手を下げるのも「強さ」。自分にかかるマイナスは
        # 技のコストなので、強さを打ち消しきらないよう半分だけ差し引く。
        signed = e['delta'] if e.get('target', 'Self') == 'Self' else -e['delta']
        if signed < 0: signed *= 0.5
        score += w * signed * e.get('chance', 1.0)

    if m.get('ailment_effect', 'None') != 'None':
        score += 3.0 * (m.get('ailment_chance', 100) / 100.0)
    if m.get('weather_effect', 'None') != 'None':
        score += 3.0          # フロア全体・長時間
    if m.get('field_placement', 'None') != 'None':
        score += 2.0
    if m.get('range', 'Adjacent') in ('Area', 'Room', 'FullFloor'):
        score += 1.0          # 複数を巻き込める分だけ強い
    return score

def status_level(m):
    """最弱の変化技(単一ランク±1)が Lv1、そこから点数に比例して上がる。"""
    return max(1, round_half_up(status_score(m) * 3) - 2)

def band_filtered(cands, power_cap, band_floor, band_max, per_element=True):
    """Applies §2-a/§2-b's "威力上位付近の技を1属性あたり何種まで" cap.
    Keeps everything below the band; thins the band itself to band_max per
    element, taking them spread across the band rather than weakest-first.

    Weakest-first here meant the >=110 band was always filled by 110/110/115
    and every move at 120+ was discarded before any species could see it -
    the last of the ascending truncations that left the top of the move list
    unreachable."""
    ok = [m for m in sorted(cands, key=lambda m: (m['power'], m['id']))
          if power_cap is None or m['power'] <= power_cap]
    below = [m for m in ok if m['power'] < band_floor]
    band = collections.defaultdict(list)
    for m in ok:
        if m['power'] >= band_floor:
            band[m['type'] if per_element else '*'].append(m)
    kept = below + [m for g in band.values() for m in spread_take(g, band_max)]
    return sorted(kept, key=lambda m: (m['power'], m['id']))

# ---------------------------------------------------------------- generate
species = json.load(open(SPECIES))
report = []

FIELD_CAP = int(len(species) * FIELD_SHARE)          # 11%未満
WEATHER_CAP = int(len(species) * WEATHER_SHARE)      # 70%まで
HIGH_CAP = int(len(species) * HIGH_SHARE)
share = collections.Counter()   # move_id -> 配布済み種族数

def share_ok(m):
    """§4 の共有率上限。設置技・単純天候変化技・高威力技だけが対象。"""
    mid = m['id']
    if m.get('field_placement', 'None') != 'None': return share[mid] < FIELD_CAP
    if m.get('weather_effect', 'None') != 'None':  return share[mid] < WEATHER_CAP
    if m['power'] >= HIGH_POWER:                   return share[mid] < HIGH_CAP
    return True

for species_index, s in enumerate(sorted(species, key=lambda s: s['species_id'])):   # §4: SpeciesId 昇順
    tot = total_of(s)
    own_types = list(s['types'])
    prime = own_types[0]
    defensive = s['base_atk'] / s['base_def'] < 1 / 1.30   # §3-b

    # --- §2-0 total ceilings (linear interpolation over 180..500) ---
    span = (tot - 180) / 320.0
    own_cap_n = round_half_up(10 - 3 * span)
    all_cap_n = round_half_up(41 - 18 * span)
    # 専用技は選抜を通さず後から足すので、その1枠を先に空けておく。
    # 空けないと自属性の上限を1つ超える（クルットリ 11 > 10 で検出）。
    sig = next((mid for mid, sid in SIGNATURE.items() if sid == s['species_id']), None)
    if sig is not None:
        all_cap_n -= 1
        if next(m for m in moves if m['id'] == sig)['type'] in s['types']:
            own_cap_n -= 1

    # --- §2-a / §2-b power ceilings and high-band counts ---
    # 種族ごとの攻撃型。威力天井にも効くので、天井を決める前に確定させる。
    if s['trait'] in TRAIT_PHYSICAL:    profile, reason = 'Physical', 'trait'
    elif s['trait'] in TRAIT_SPECIAL:   profile, reason = 'Special', 'trait'
    else:                               profile, reason = PROFILE_CYCLE[species_index % len(PROFILE_CYCLE)], 'profile'

    minus = 30 if defensive else 0                          # §3-b: -30
    minus += 20 if profile == 'Versatile' else 0            # 器用型は威力天井-20
    if tot < 310:
        own_pow, own_band_floor, own_band_max = 120 - minus, 110 - minus, 2
        cmp_pow, cmp_band_floor, cmp_band_max = 90 - minus, 80 - minus, 2
        ext_pow = None
    else:
        own_pow, own_band_floor, own_band_max = None, 110, 3   # 威力上限なし
        cmp_pow, cmp_band_floor, cmp_band_max = 95 - minus, 85 - minus, 1
        ext_pow = 70 - minus

    # Complement chains, with the species' OWN types removed: a dual-typed
    # species can have one of its own elements come back as the complement
    # of the other (Water/Ice -> COMP[Ice] is Water), and counting those as
    # "complement" moves would smuggle extra own-element moves past §2-0's
    # own-element ceiling.
    comp_types = sorted({c for t in own_types for c in COMP[t]} - set(own_types), key=TYPES.index)
    weak_types = sorted({w for t in own_types for w in WEAK[t]}, key=TYPES.index)

    own_pool = band_filtered([m for m in ATTACK if m['type'] in own_types],
                             own_pow, own_band_floor, own_band_max)
    cmp_pool = band_filtered([m for m in ATTACK if m['type'] in comp_types],
                             cmp_pow, cmp_band_floor, cmp_band_max)

    ext_pool, ext_element = [], None
    if tot >= 310:
        # §4: first element in chart order that is neither own, weakness nor complement.
        excluded = set(own_types) | set(weak_types) | set(comp_types)
        for el in TYPES:
            if el not in excluded: ext_element = el; break
        if ext_element:
            ext_pool = band_filtered([m for m in ATTACK if m['type'] == ext_element],
                                     ext_pow, ext_pow - 10, 2)

    # --- §3-a: bias direction ---
    # ELEM_BIAS は「その属性に物理技と特殊技のどちらが多いか」で決まるため、
    # 技プールが物理310/特殊213になった時点でほぼ全属性が Physical に倒れ、
    # 287種中282種が物理寄りという単一文化になっていた。属性ではなく種族
    # ごとの型で決めるように変える。
    #
    #   Physical  物理で殴る型
    #   Special   特殊で削る型
    #   Versatile 威力は伸びないが打ち分けが利く型（威力天井-20、少数側の
    #             枠を倍、変化技+1）
    major = 'Special' if profile == 'Special' else (
        'Physical' if profile == 'Physical' else
        ('Physical' if species_index % 2 == 0 else 'Special'))

    # --- gather generously, then normalise ---
    # Own-element moves come first so the own-ceiling is spent on them;
    # complement and the extra element fill whatever the overall ceiling
    # still allows.
    pool_all = [m for m in own_pool + cmp_pool + ext_pool if share_ok(m)]
    is_own = lambda m: m['type'] in own_types

    maj = sorted([m for m in pool_all if m['category'] == major], key=lambda m: (m['power'], m['id']))
    mnr = sorted([m for m in pool_all if m['category'] != major and m['category'] != 'Status'],
                 key=lambda m: (m['power'], m['id']))

    # (a) §2-0 own-element ceiling, applied per side. Weakest kept, so the
    #     level gate still spreads the learnset across the climb.
    def trim_own(lst, budget):
        # 自属性の枠も等間隔に採る。昇順で先頭 budget 件を残すと、自属性の
        # 高威力技が maj の切り詰めに届く前に消えてしまう（これが威力90以上
        # が誰にも配られなかった最後の原因）。
        own = [m for m in lst if is_own(m)]
        keep = {m['id'] for m in spread_take(own, budget)}
        out = [m for m in lst if not is_own(m) or m['id'] in keep]
        return out, min(budget, len(own))

    maj, own_used = trim_own(maj, own_cap_n)
    mnr, _ = trim_own(mnr, max(0, own_cap_n - own_used))

    # (b) overall ceiling shared 8:2, leaving room for status/field below.
    reserve = 3 if defensive else 1
    attack_budget = max(1, all_cap_n - reserve)
    maj = spread_take(maj, max(1, round_half_up(attack_budget * 0.8)))
    # 威力105以上は1種族あたり HIGH_PER_SPECIES 本まで（弱い側から残す）
    high = [m for m in maj if m['power'] >= HIGH_POWER]
    if len(high) > HIGH_PER_SPECIES:
        # ここも等間隔で。先頭2件を残すと105と110しか配られず、120以上が
        # どの種族にも届かない。
        keep = {m['id'] for m in spread_take(high, HIGH_PER_SPECIES)}
        maj = [m for m in maj if m['power'] < HIGH_POWER or m['id'] in keep]

    # (c) §3-a's two minority rules: at most a quarter of the majority
    #     count (8:2), and no minority move stronger than 60% of the
    #     majority's own strongest.
    maj_max = max((m['power'] for m in maj), default=0)
    mnr = [m for m in mnr if m['power'] <= maj_max * 0.6]
    # 器用型は少数側の枠を倍にして、打ち分けの器用さを本数で表現する。
    mnr = spread_take(mnr, int(len(maj) * (0.5 if profile == 'Versatile' else 0.25)))

    chosen = maj + mnr
    taken = {m['id'] for m in chosen}

    # --- §3-b: defensive species get a field move and extra status moves ---
    if defensive:
        field_ids = [fid for t in own_types for fid in FIELD_BY_ELEMENT.get(t, [])]
        if not field_ids:
            # 炎/雷/無/闇 own no field move - substitute the first by id (§4).
            field_ids = sorted(m['id'] for m in FIELD_MOVES)
        field_ids = [f for f in field_ids
                     if share[f] < FIELD_CAP] or field_ids
        fid = field_ids[0]
        if fid not in taken:
            chosen.append(next(m for m in moves if m['id'] == fid)); taken.add(fid)
        status_quota = max(2, all_cap_n - len(chosen))
    else:
        status_quota = max(0, min(2, all_cap_n - len(chosen)))

    # §3-c: 変化技も他の分類と同じく自属性を優先する。元の実装は power/id
    # 順に取るだけで、変化技はすべて power=0 のため全種が同じ「ID順の先頭
    # 2件」を覚えていた（属性が一切効かない唯一の分類だった）。自属性の
    # 候補を先に、足りない分だけ従来どおり全体から補う。決定的な取り方
    # （weakest first, ties by id）は変えていない。
    # 自属性の候補は (power, id) 順に並べたうえで、種族の並び順ぶんだけ開始
    # 位置を回転させる。回転がないと同属性の全種が「ID順の先頭2件」だけを
    # 取り続け、3件目以降の変化技はどの種族にも配られない。RNGは使わず、
    # species_id 昇順の序数だけで決まるので再現性はそのまま。
    status_pool = [m for m in STATUS
                   if m.get('field_placement', 'None') == 'None' and share_ok(m)]
    own_status = sorted([m for m in status_pool if m['type'] in own_types],
                        key=lambda m: (m['power'], m['id']))
    if own_status:
        off = species_index % len(own_status)
        own_status = own_status[off:] + own_status[:off]

    before_status = len(chosen)
    chosen += take_in_order(own_status, status_quota, taken)
    remaining = status_quota - (len(chosen) - before_status)
    if remaining > 0:
        chosen += pick_moves(status_pool, remaining, taken)

    if len(chosen) > all_cap_n:
        # 威力昇順に並べて先頭から採ると、上限を超えた分は必ず「最も強い技」
        # から捨てられる。生成器自身は威力上限を120（BST310以上なら上限なし）
        # と定めているのに、この一行のせいで誰も威力85超を覚えられず、581技
        # 中289技が死にデータになっていた。等間隔に間引いて威力の幅を残す。
        chosen = spread_take(sorted(chosen, key=lambda m: (m['power'], m['id'])), all_cap_n)

    # 攻撃技を使えない特性の持ち主（グランジーラ）は、攻撃技を1つも持たない。
    # 空いた枠は変化技で埋める。4vs4のグリッド戦では、弱点属性の部屋対象技で
    # 一掃されうる的でもあるので、庇う役に専念させる形にしている。
    if s['trait'] in NO_ATTACK_TRAITS:
        chosen = [m for m in chosen if m['power'] == 0]
        extra = [m for m in STATUS if m['id'] not in {x['id'] for x in chosen} and share_ok(m)]
        extra.sort(key=lambda m: (0 if m['type'] in own_types else 1, m['power'], m['id']))
        chosen += extra[:max(0, all_cap_n - len(chosen))]

    # 専用技はプールを通さず、指定された1種にだけ直接渡す。
    for sig_id, sig_species in SIGNATURE.items():
        if s['species_id'] == sig_species:
            chosen.append(next(m for m in moves if m['id'] == sig_id))

    for m in chosen: share[m['id']] += 1

    own_count = sum(1 for m in chosen if is_own(m) and m['power'] > 0)

    # --- §5: level gate, Lv ~ power/5 ---
    entries = []
    for m in sorted(chosen, key=lambda m: (m['power'], m['id'])):
        # 攻撃技は威力、変化技は効果の点数（§5-b）でレベルを決める。
        lvl = status_level(m) if m['power'] == 0 else round_half_up(m['power'] / 5.0)
        entries.append({'level': min(100, max(1, lvl)), 'move_id': m['id']})
    entries.sort(key=lambda e: (e['level'], e['move_id']))
    s['learnset'] = entries

    report.append((s['species_id'], s['display_name'], tot, prime, major, reason,
                   own_count, own_cap_n, len(entries), all_cap_n, defensive))

json.dump(species, open(SPECIES, 'w'), ensure_ascii=False, indent=2)
open(SPECIES, 'a').write('\n')

print(f"generated learnsets for {len(report)} species")
print("  偏り: " + str(dict(collections.Counter(r[4] for r in report))))
print("  決定根拠: " + str(dict(collections.Counter(r[5] for r in report))))
print(f"  防御特化種: {sum(1 for r in report if r[10])}")
sizes = [r[8] for r in report]
print(f"  learnset行数: min={min(sizes)} max={max(sizes)} avg={sum(sizes)/len(sizes):.1f} total={sum(sizes)}")
owns = [r[6] for r in report]
print(f"  自属性技数:   min={min(owns)} max={max(owns)} avg={sum(owns)/len(owns):.1f}")
