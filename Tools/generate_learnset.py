#!/usr/bin/env python3
"""learnset generation (§2-§5). Deterministic: no RNG anywhere.

Every "pick one" is resolved by an explicit sort key, per §4.
"""
import json, math, collections

SPECIES = 'Data/species.json'
MOVES   = 'Data/moves.json'
CHART   = 'Data/type_chart.json'

# ---------------------------------------------------------------- helpers
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
ATTACK = [m for m in moves if m['power'] > 0]
STATUS = [m for m in moves if m['power'] == 0]
FIELD_MOVES = [m for m in moves if m.get('field_placement', 'None') != 'None']

# §7-2: per-element Physical/Special census of the FULL pool, computed once.
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

def band_filtered(cands, power_cap, band_floor, band_max, per_element=True):
    """Applies §2-a/§2-b's "威力上位付近の技を1属性あたり何種まで" cap.
    Keeps everything below the band; thins the band itself to band_max per
    element (weakest-of-the-band first, ties by id)."""
    kept, band_count = [], collections.Counter()
    for m in sorted(cands, key=lambda m: (m['power'], m['id'])):
        if power_cap is not None and m['power'] > power_cap: continue
        if m['power'] >= band_floor:
            key = m['type'] if per_element else '*'
            if band_count[key] >= band_max: continue
            band_count[key] += 1
        kept.append(m)
    return kept

# ---------------------------------------------------------------- generate
species = json.load(open(SPECIES))
report = []

for species_index, s in enumerate(sorted(species, key=lambda s: s['species_id'])):   # §4: SpeciesId 昇順
    tot = total_of(s)
    own_types = list(s['types'])
    prime = own_types[0]
    defensive = s['base_atk'] / s['base_def'] < 1 / 1.30   # §3-b

    # --- §2-0 total ceilings (linear interpolation over 180..500) ---
    span = (tot - 180) / 320.0
    own_cap_n = round_half_up(10 - 3 * span)
    all_cap_n = round_half_up(41 - 18 * span)

    # --- §2-a / §2-b power ceilings and high-band counts ---
    minus = 30 if defensive else 0                          # §3-b: -30
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
    if s['trait'] in TRAIT_PHYSICAL:      major, reason = 'Physical', 'trait'
    elif s['trait'] in TRAIT_SPECIAL:     major, reason = 'Special', 'trait'
    else:                                  major, reason = ELEM_BIAS[prime], 'pool'

    # --- gather generously, then normalise ---
    # Own-element moves come first so the own-ceiling is spent on them;
    # complement and the extra element fill whatever the overall ceiling
    # still allows.
    pool_all = own_pool + cmp_pool + ext_pool
    is_own = lambda m: m['type'] in own_types

    maj = sorted([m for m in pool_all if m['category'] == major], key=lambda m: (m['power'], m['id']))
    mnr = sorted([m for m in pool_all if m['category'] != major and m['category'] != 'Status'],
                 key=lambda m: (m['power'], m['id']))

    # (a) §2-0 own-element ceiling, applied per side. Weakest kept, so the
    #     level gate still spreads the learnset across the climb.
    def trim_own(lst, budget):
        out, used = [], 0
        for m in lst:
            if is_own(m):
                if used >= budget: continue
                used += 1
            out.append(m)
        return out, used

    maj, own_used = trim_own(maj, own_cap_n)
    mnr, _ = trim_own(mnr, max(0, own_cap_n - own_used))

    # (b) overall ceiling shared 8:2, leaving room for status/field below.
    reserve = 3 if defensive else 1
    attack_budget = max(1, all_cap_n - reserve)
    maj = maj[:max(1, round_half_up(attack_budget * 0.8))]

    # (c) §3-a's two minority rules: at most a quarter of the majority
    #     count (8:2), and no minority move stronger than 60% of the
    #     majority's own strongest.
    maj_max = max((m['power'] for m in maj), default=0)
    mnr = [m for m in mnr if m['power'] <= maj_max * 0.6]
    mnr = mnr[:int(len(maj) * 0.25)]

    chosen = maj + mnr
    taken = {m['id'] for m in chosen}

    # --- §3-b: defensive species get a field move and extra status moves ---
    if defensive:
        field_ids = [fid for t in own_types for fid in FIELD_BY_ELEMENT.get(t, [])]
        if not field_ids:
            # 炎/雷/無/闇 own no field move - substitute the first by id (§4).
            field_ids = sorted(m['id'] for m in FIELD_MOVES)
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
    status_pool = [m for m in STATUS if m.get('field_placement', 'None') == 'None']
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
        chosen = sorted(chosen, key=lambda m: (m['power'], m['id']))[:all_cap_n]

    own_count = sum(1 for m in chosen if is_own(m) and m['power'] > 0)

    # --- §5: level gate, Lv ~ power/5 ---
    entries = []
    for m in sorted(chosen, key=lambda m: (m['power'], m['id'])):
        lvl = min(100, max(1, round_half_up(m['power'] / 5.0)))
        entries.append({'level': lvl, 'move_id': m['id']})
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
