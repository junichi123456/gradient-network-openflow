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
# 補完属性は1種・威力40〜60まで。無属性は全種族が候補にできる汎用枠。
OFF_PER_ELEMENT = 2      # 他属性(無属性以外)は1属性あたり2種まで
OFF_POW_SINGLE = 95      # 単属性のパルが持てる他属性技の威力上限
OFF_POW_DUAL = 85        # 複合属性のパルは1属性ぶん狭い
STATUS_MIN = 2           # 全個体が最低2つの変化技を持つ

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

    # --- 上限体系（単属性 / 複合属性で別系列）------------------------
    # 種族値180=最低 / 500=最高 を両端に線形補間する。
    #   単属性   自属性 16→8  攻撃技合計 23→11  他属性の威力上限 95
    #   複合属性 自属性 18→10 攻撃技合計 24→13  他属性の威力上限 85
    # 他属性は1属性あたり2種まで。無属性はこの上限に含めない。
    # 変化技は攻撃技の枠とは別で、全個体が最低2つ持つ。
    dual = len(s['types']) > 1
    span = (tot - 180) / 320.0
    own_target = round_half_up((18 if dual else 16) - 8 * span)
    atk_cap    = round_half_up((24 if dual else 23) - (11 if dual else 12) * span)
    off_pow    = OFF_POW_DUAL if dual else OFF_POW_SINGLE

    # 専用技は選抜を通さず後から足すので、その1枠を先に空けておく。
    sig = next((mid for mid, sid in SIGNATURE.items() if sid == s['species_id']), None)
    if sig is not None:
        atk_cap -= 1
        if next(m for m in moves if m['id'] == sig)['type'] in s['types']:
            own_target -= 1

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

    # 他属性(無属性以外)は1属性あたり OFF_PER_ELEMENT 種まで、威力は off_pow 以下。
    # 属性ごとに候補列を種族の序数で回して、同属性の全種が同じ組を取らない
    # ようにする。補完属性/追加属性という区分は廃止し、扱いを一本化した。
    off_pool = []
    for el in TYPES:
        if el in own_types or el == 'Neutral': continue
        cands = sorted([m for m in ATTACK if m['type'] == el and m['power'] <= off_pow],
                       key=lambda m: (m['power'], m['id']))
        if not cands: continue
        offset = (species_index + TYPES.index(el)) % len(cands)
        cands = cands[offset:] + cands[:offset]
        off_pool += cands[:OFF_PER_ELEMENT]

    # 無属性は1属性2種の上限の対象外。攻撃技合計の枠だけが効く。
    neu_all = [] if 'Neutral' in own_types else sorted(
        [m for m in ATTACK if m['type'] == 'Neutral' and m['power'] <= off_pow],
        key=lambda m: (m['power'], m['id']))
    if neu_all:
        offset = species_index % len(neu_all)
        neu_all = neu_all[offset:] + neu_all[:offset]
    neu_pool = neu_all

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
    seen_pool = set()
    pool_all = []
    for m in own_pool + off_pool + neu_pool:
        if m['id'] in seen_pool or not share_ok(m): continue
        seen_pool.add(m['id']); pool_all.append(m)
    is_own = lambda m: m['type'] in own_types

    # --- 選抜: 自属性を own_target まで、残りを他属性+無属性で atk_cap まで ----
    # 型(major)の比率は選抜の中で保つ。打ち分け型は少数側を厚くする。
    maj_share = 0.67 if profile == 'Versatile' else 0.8

    def by_profile(pool, k):
        """pool から k 件を、多数側 maj_share の比率・威力は等間隔で採る。"""
        if k <= 0: return []
        # 同威力どうしの並びを種族ごとに回す。威力順の等間隔抜きは常に同じ
        # 端点を拾うため、回さないと最弱の無属性技(エアーキャノン)が全287種に
        # 配られて共有率100%になった。威力の並び自体は保つ。
        def key(m):
            grp = [x for x in pool if x['power'] == m['power']]
            grp.sort(key=lambda x: x['id'])
            return (m['power'], (grp.index(m) + species_index) % max(1, len(grp)))
        a_ = sorted([m for m in pool if m['category'] == major], key=key)
        b_ = sorted([m for m in pool if m['category'] != major and m['power'] > 0], key=key)
        na = min(len(a_), max(1, round_half_up(k * maj_share)))
        out = spread_take(a_, na)
        # §3-a: 少数側は多数側の最高威力の60%まで。書き換えの際に落として
        # しまい、全287種で少数側が多数側と同じ威力帯に届いていた。
        cap = max((m['power'] for m in out), default=0) * 0.6
        b_ = [m for m in b_ if m['power'] <= cap + 1e-6]
        out += spread_take(b_, k - len(out))
        if len(out) < k:                       # 片側が足りなければもう片方で埋める
            rest = [m for m in a_ + b_ if m not in out]
            out += spread_take(rest, k - len(out))
        return out

    own_avail = [m for m in pool_all if is_own(m)]
    chosen = by_profile(own_avail, min(own_target, len(own_avail)))

    fill_avail = [m for m in pool_all if not is_own(m) and m['id'] not in {x['id'] for x in chosen}]
    chosen += by_profile(fill_avail, atk_cap - len(chosen))

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
        status_quota = STATUS_MIN + 2      # 防御特化は変化技が多い
    else:
        status_quota = STATUS_MIN

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

    if sum(1 for m in chosen if m['power'] > 0) > atk_cap:
        # 威力昇順に並べて先頭から採ると、上限を超えた分は必ず「最も強い技」
        # から捨てられる。生成器自身は威力上限を120（BST310以上なら上限なし）
        # と定めているのに、この一行のせいで誰も威力85超を覚えられず、581技
        # 中289技が死にデータになっていた。等間隔に間引いて威力の幅を残す。
        atk_part = [m for m in chosen if m['power'] > 0]
        keep = {m['id'] for m in spread_take(
            sorted(atk_part, key=lambda m: (m['power'], m['id'])), atk_cap)}
        chosen = [m for m in chosen if m['power'] == 0 or m['id'] in keep]

    # --- 確定配布 -------------------------------------------------------
    # 全個体に変化技を1つ、打ち分け型にはさらに物理・特殊を1つずつ。総枠を
    # 広げるだけでは攻撃技の予算が一緒に増えて吸収されてしまい、287種中149種
    # で変化技が増えなかった。トリムのあとに足して確実に配る。
    # プールも共有率の判定も通常の選抜と同じものを使うので、威力天井・設置技
    # 11%・天候技70%・高威力20%の枠はそのまま効く。
    got = {m['id'] for m in chosen}

    def grant(cands, rotate=False):
        ordered = sorted(cands, key=lambda m: (m['power'], m['id']))
        # 変化技の確定配布は候補列を種族ごとにずらす。ずらさないと全287種が
        # 同じ1件を取り、共有率100%の技ができてしまう（差別化の逆行）。
        if rotate and ordered:
            off = species_index % len(ordered)
            ordered = ordered[off:] + ordered[:off]
        for m in ordered[:1]:
            chosen.append(m); got.add(m['id'])

    grant([m for m in status_pool if m['id'] not in got and share_ok(m)]
          or [m for m in STATUS if m['id'] not in got and share_ok(m)], rotate=True)

    # 打ち分け型への「物理・特殊を1つずつ追加」は、攻撃技の上限が入ったことで
    # 上限の外側に足す形になり80種で超過した。上限が優先されるので、その意図は
    # 選抜の中の比率（少数側を厚くする maj_share=0.67）で表現している。

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
                   own_count, own_target, len(entries), atk_cap, defensive))

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
