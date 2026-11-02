#!/usr/bin/python3
"""280種のパル登場計画 -> Data/story/pal_plan.json と docs/design/scenario/pal_plan_report.md

入力: Data/species.json、docs/design/scenario/script-1-30.md
  (「280種のパル登場計画」表と「踏破ダンジョンとボス」表、「主要登場人物」表を実行時に読む)
規則: docs/design/drafts/2026-10-31-scenario-implementation.md §2-7
  - 伝説7体は本編に初登場しない(first_chart=null)。残り280種をチャート1〜20に割り当てる
  - 目玉・ネームド人物・ボス・野生で出ない9体は指定チャートに固定
  - NNN と NNNB は同じチャート
  - 属性(第一属性)の内訳と平均BST6は台本の表に寄せる(焼きなまし、固定シード、決定的)
  - プレイアブル16体はチャート2〜5へ4体ずつ(住民枠)
実行: /usr/bin/python3 Tools/build_pal_plan.py
"""
import json
import math
import random
import re
import sys
from collections import Counter, defaultdict
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SPECIES = ROOT / "Data" / "species.json"
SCRIPT = ROOT / "docs" / "design" / "scenario" / "script-1-30.md"
OUT_JSON = ROOT / "Data" / "story" / "pal_plan.json"
OUT_MD = ROOT / "docs" / "design" / "scenario" / "pal_plan_report.md"

SEED = 20261031
CHART_COUNTS = [12, 14, 14, 14, 15, 14, 14, 14, 14, 20, 13, 13, 14, 14, 14, 14, 14, 13, 16, 10]
NUM_CHARTS = 20
RESTARTS = 2
PLAYABLE_W = 2

JP2EN = {"無": "Neutral", "炎": "Fire", "水": "Water", "草": "Grass", "電": "Electric",
         "地": "Ground", "氷": "Ice", "竜": "Dragon", "闇": "Dark", "虚": "Void"}
EN2JP = {v: k for k, v in JP2EN.items()}
ALL_TYPES = list(JP2EN.values())

PLAYABLE_NAMES = ["ルナティ", "ネムラム", "ユキツネ", "モグルン", "モコロン", "ンダコアラ", "ポチムネ", "チルテト",
                  "クルリス", "パチグリ", "ニャオテト", "ボルトラ", "エテッパ", "カモノスケ", "ツッパニャン", "チョロゾウ"]
PLAYABLE_CHARTS = [2, 3, 4, 5]          # 4体ずつ

NONWILD9 = ["シャオロン", "ヒョウガオー", "レジェンディア", "ベラルージュ", "ベラノワール",
            "ゼノグリフ", "ゼノドラン", "ゼノベーダ", "ゼノルーダ"]

# 台本に書かれていない固定(決定事項。レポートの「判断」に載せる)
#  ゼノベーダ: 台本は「ゼノルーダの初登場はチャート12」とだけ書く。群れ戦の相方なので12に置く。
EXTRA_PINS = {"ゼノベーダ": 12}
#  ネームド人物が出会うダンジョン(台本のチャート解説「〜は〜に出ます」より)
NAMED_DUNGEON = {
    "ブシガエル": "ハコニワ裏山", "ヘルガルダ": "ハコニワ裏山",
    "ヴィオレッタ": "割れ目の浅層", "ブルフェルノ": "煙の道",
    "ツララジカ": "凍てつく峰の氷穴", "シルフィア": "吹雪の荷運び道",
    "アヌビス": "裁きの庭の地下牢", "ホークウィン": "祭りの裏路", "ミステリア": "祭りの裏路",
    "ナイトロット": "かけらの回廊", "ライトロット": "かけらの回廊",
    "オーメン": "影の森", "マジョルナ": "ささやきの洞", "スザク": "炎山の火口道",
    "レヴィドラ": "竜の空の階", "ゼノグリフ": "世界樹の根の洞・中層",
}
# 補完ダンジョンの挿入点(「このチャート完了後」)。実効チャート = 挿入点
SUPPLEMENT_AFTER = {31: 1, 32: 7, 33: 9, 34: 12, 35: 14}
# ゼノドランは目玉としてチャート14に初登場し、最終戦のボスはチャート20
BOSS_CHART_EXCEPT = {"ゼノドラン"}
LEGENDARY_SLOTS = {}  # species_id -> 追加枠id (後で埋める)


# ------------------------------------------------------------------ 読み込み
def bst6(s):
    return sum(s[k] for k in ["base_hp", "base_atk", "base_def", "base_spatk", "base_spdef", "base_speed"])


def load_species():
    with open(SPECIES, encoding="utf-8") as f:
        return json.load(f)


def parse_script(names):
    """台本の3つの表を読む。names は display_name の集合。"""
    lines = SCRIPT.read_text(encoding="utf-8").split("\n")
    by_len = sorted(names, key=lambda n: -len(n))

    def find_names(text):
        text = re.sub(r"[(（][^)）]*[)）]", "", text)
        found, rest = [], text
        for n in by_len:
            if n in rest:
                found.append((text.index(n), n))
                rest = rest.replace(n, "\0" * len(n))
        return [n for _, n in sorted(found)]

    def section(title):
        i = next(k for k, l in enumerate(lines) if l.startswith("## ") and title in l)
        j = next((k for k in range(i + 1, len(lines)) if lines[k].startswith("## ")), len(lines))
        return lines[i:j]

    def rows(sec):
        out = []
        for l in sec:
            if l.startswith("|") and not re.match(r"^\|\s*-", l):
                out.append([c.strip() for c in l.strip().strip("|").split("|")])
        return out[1:]

    plan = {}
    for r in rows(section("280種のパル登場計画")):
        if not r[0].isdigit():
            continue
        types = {}
        for m in re.finditer(r"([無炎水草電地氷竜闇虚])(\d+)", r[4]):
            types[JP2EN[m.group(1)]] = int(m.group(2))
        plan[int(r[0])] = dict(stage=r[1], count=int(r[2]), avg=int(r[3]), types=types,
                               highlights=[x.strip() for x in r[5].split("、")])
    dungeons = []
    for r in rows(section("踏破ダンジョンとボス")):
        m = re.match(r"(\d+)", r[0])
        if not m:
            continue
        ch = int(m.group(1))
        tys = [JP2EN[c] for c in r[3] if c in JP2EN] if r[3] != "全属性" else list(ALL_TYPES)
        dungeons.append(dict(chart=ch, name=r[1], floors=int(r[2]), types=tys, all_types=(r[3] == "全属性"),
                             bosses=find_names(r[4]), boss_text=r[4]))
    named = {}
    for r in rows(section("主要登場人物と登場タイミング")):
        if r[0].isdigit() and r[1] in names:
            named[r[1]] = dict(chart=int(r[0]), mbti=r[2], attr=r[3], role=r[4])
    return plan, dungeons, named


def dungeon_id(i, d):
    return "d%02d_%s" % (i + 1, "s%d" % d["chart"] if d["chart"] > 20 else "c%d" % d["chart"])


# ------------------------------------------------------------------ 割り当て
def build():
    species = load_species()
    by_name = {s["display_name"]: s for s in species}
    by_id = {s["species_id"]: s for s in species}
    plan, dungeons, named = parse_script(set(by_name))
    assert len(plan) == 20 and [plan[c]["count"] for c in range(1, 21)] == CHART_COUNTS, "台本の表の読み取りに失敗"
    legendary = sorted(s["species_id"] for s in species if s.get("is_legendary"))
    assert len(legendary) == 7

    # 変種グループ(NNN と NNNB)
    groups = defaultdict(list)
    for s in species:
        if not s.get("is_legendary"):
            groups["".join(c for c in s["species_id"] if c.isdigit())].append(s["species_id"])
    group_of = {sid: g for g, ids in groups.items() for sid in ids}
    assert len(group_of) == 280

    # ---- 固定(pin)の収集: sid -> (chart, 理由)
    pins = {}

    def pin(sid, chart, why):
        if sid in pins and pins[sid][0] != chart:
            raise SystemExit("固定の衝突: %s %s -> %d と %d (%s / %s)" % (
                sid, by_id[sid]["display_name"], pins[sid][0], chart, pins[sid][1], why))
        pins.setdefault(sid, (chart, why))

    for c, p in plan.items():
        for n in p["highlights"]:
            pin(by_name[n]["species_id"], c, "目玉")
    for n, v in named.items():
        pin(by_name[n]["species_id"], v["chart"], "ネームド")
    for n, c in EXTRA_PINS.items():
        pin(by_name[n]["species_id"], c, "決定(ゼノベーダ)")
    for d in dungeons:
        if d["chart"] <= 20:
            for b in d["bosses"]:
                if b not in BOSS_CHART_EXCEPT:
                    pin(by_name[b]["species_id"], d["chart"], "ボス")
    # ゼノドラン: 目玉(14)。最終戦ボス(20)は boss_dungeons 側で扱う

    # 変種相棒への伝播
    gchart = {}
    for sid, (c, why) in list(pins.items()):
        g = group_of[sid]
        if g in gchart and gchart[g][0] != c:
            raise SystemExit("変種グループの衝突: %s" % g)
        gchart.setdefault(g, (c, why))

    # 補完ボスは「挿入点までに登場済み」でなければならない
    max_chart = {}
    for d in dungeons:
        if d["chart"] > 20:
            for b in d["bosses"]:
                g = group_of[by_name[b]["species_id"]]
                max_chart[g] = min(max_chart.get(g, 99), SUPPLEMENT_AFTER[d["chart"]])
    for g, m in max_chart.items():
        if g in gchart and gchart[g][0] > m:
            raise SystemExit("補完ボスの登場が挿入点より後: %s" % g)

    playable_groups = {group_of[by_name[n]["species_id"]] for n in PLAYABLE_NAMES}
    playable_sids = {by_name[n]["species_id"] for n in PLAYABLE_NAMES}
    for g in playable_groups:
        if g in gchart and gchart[g][0] not in PLAYABLE_CHARTS:
            raise SystemExit("プレイアブルが住民枠チャート外に固定された: %s" % g)

    # ---- グループ情報
    ginfo = {}
    for g, ids in groups.items():
        ginfo[g] = dict(ids=ids, size=len(ids), bst=sum(bst6(by_id[i]) for i in ids),
                        first=[by_id[i]["types"][0] for i in ids],
                        pins=gchart.get(g), playable=sum(1 for i in ids if i in playable_sids),
                        maxc=max_chart.get(g, 20))
        ginfo[g]["domain"] = ([gchart[g][0]] if g in gchart else
                              [c for c in (PLAYABLE_CHARTS if g in playable_groups else range(1, 21))
                               if c <= ginfo[g]["maxc"]])
    highlight_sids = {c: [by_name[n]["species_id"] for n in plan[c]["highlights"]] for c in plan}

    # ---- 目的関数
    tgt_avg = {c: plan[c]["avg"] for c in plan}
    tgt_types = {c: plan[c]["types"] for c in plan}

    def chart_cost(c, members, phase=2):
        sids = [i for g in members for i in ginfo[g]["ids"]]
        n = len(sids)
        cost = 0.0
        if n != CHART_COUNTS[c - 1]:
            cost += 1000 * abs(n - CHART_COUNTS[c - 1])
        ft = Counter(by_id[i]["types"][0] for i in sids)
        tt = tgt_types[c]
        lo = min(tt.values())
        for t in ALL_TYPES:
            if t in tt:
                cost += 12 * abs(ft.get(t, 0) - tt[t])
            else:
                cost += 12 * max(0, ft.get(t, 0) - lo)
        if c in PLAYABLE_CHARTS:
            pl = [i for i in sids if i in playable_sids]
            cost += PLAYABLE_W * abs(len(pl) - 4)
            pc = Counter(by_id[i]["types"][0] for i in pl)
            cost += 3 * sum(v - 1 for v in pc.values() if v > 1)
        if phase == 1:
            return cost
        cost *= 12
        tot = sum(bst6(by_id[i]) for i in sids)
        avg = tot / max(n, 1)
        cost += 6.0 * max(0.0, abs(avg - tgt_avg[c]) - 0.5) + 0.5 * abs(avg - tgt_avg[c])
        hl = [bst6(by_id[i]) for i in highlight_sids[c]]
        if hl:
            floor = min(hl)
            cost += 2.0 * sum(1 for i in sids if i not in highlight_sids[c] and bst6(by_id[i]) > floor)
        return cost

    def signature(g):
        gi = ginfo[g]
        return (gi["size"], tuple(sorted(gi["first"])), gi["playable"], tuple(gi["domain"]) if len(gi["domain"]) < 20 else 0)

    def anneal(seed):
        rng = random.Random(seed)
        free = sorted(g for g in groups if g not in gchart)
        members = {c: [] for c in range(1, 21)}
        for g in gchart:
            members[gchart[g][0]].append(g)
        # 初期解: 空きを満たすように貪欲に詰める
        order = sorted(free, key=lambda g: (len(ginfo[g]["domain"]), -ginfo[g]["size"], g))
        size_of = lambda c: sum(ginfo[g]["size"] for g in members[c])
        pl_of = lambda c: sum(ginfo[g]["playable"] for g in members[c])
        for g in order:
            dom = ginfo[g]["domain"]
            cand = [c for c in dom if size_of(c) + ginfo[g]["size"] <= CHART_COUNTS[c - 1]
                   ]
            if not cand:
                raise SystemExit("初期配置に失敗: %s" % g)
            w = [(CHART_COUNTS[c - 1] - size_of(c)) for c in cand]
            c = rng.choices(cand, weights=w)[0]
            members[c].append(g)
        where = {g: c for c, ms in members.items() for g in ms}
        free_by_size = defaultdict(list)
        by_sig = defaultdict(list)
        for g in free:
            free_by_size[ginfo[g]["size"]].append(g)
            by_sig[signature(g)].append(g)

        def run(phase, iters, T0, T1):
            cur = {c: chart_cost(c, members[c], phase) for c in members}
            for it in range(iters):
                T = T0 * (T1 / T0) ** (it / iters)
                if True:
                    r = rng.random()
                    if r < 0.85:
                        pool = free_by_size[1 if rng.random() < 0.85 else 2]
                        if len(pool) < 2:
                            continue
                        ga, gb = rng.sample(pool, 2)
                        a_list, b_list = [ga], [gb]
                    else:
                        twos = free_by_size[2]
                        ga = rng.choice(twos)
                        cb = rng.randint(1, 20)
                        singles = [g for g in members[cb] if g in ginfo and ginfo[g]["size"] == 1 and g not in gchart]
                        if len(singles) < 2:
                            continue
                        a_list, b_list = [ga], rng.sample(singles, 2)
                ca, cb = where[a_list[0]], where[b_list[0]]
                if ca == cb:
                    continue
                if any(ca not in ginfo[g]["domain"] for g in b_list) or any(cb not in ginfo[g]["domain"] for g in a_list):
                    continue
                na = [g for g in members[ca] if g not in a_list] + b_list
                nb = [g for g in members[cb] if g not in b_list] + a_list
                ka, kb = chart_cost(ca, na, phase), chart_cost(cb, nb, phase)
                d = ka + kb - cur[ca] - cur[cb]
                if d <= 0 or rng.random() < math.exp(-d / T):
                    members[ca], members[cb] = na, nb
                    cur[ca], cur[cb] = ka, kb
                    for g in a_list:
                        where[g] = cb
                    for g in b_list:
                        where[g] = ca
            return sum(cur.values())

        c1 = run(1, 600000, 10.0, 0.8)
        c2 = run(2, 1100000, 14.0, 0.1)
        return members, c2

    best = None
    for k in range(RESTARTS):
        m, fcst = anneal(SEED + k)
        print("restart %d seed %d cost %.2f" % (k, SEED + k, fcst), file=sys.stderr)
        if best is None or fcst < best[1]:
            best = (m, fcst)
        if fcst < 1e-9:
            break
    members, final_cost = best

    # ---- 結果を種族ごとの first_chart に
    first_chart = {}
    for c, ms in members.items():
        for g in ms:
            for i in ginfo[g]["ids"]:
                first_chart[i] = c
    assert len(first_chart) == 280

    # ---- ダンジョン表(id付与)とプール
    dtable = []
    for i, d in enumerate(dungeons):
        dd = dict(d)
        dd["id"] = dungeon_id(i, d)
        dd["eff_chart"] = d["chart"] if d["chart"] <= 20 else SUPPLEMENT_AFTER[d["chart"]]
        dtable.append(dd)
    by_dname = {}
    for d in dtable:
        by_dname.setdefault(d["name"], d)

    def matches(d, sid):
        return d["all_types"] or any(t in d["types"] for t in by_id[sid]["types"])

    nonwild_sids = {by_name[n]["species_id"] for n in NONWILD9}
    pool = defaultdict(list)    # sid -> [dungeon id]
    no_match_fallback = []
    forced_by = {}
    for sid, c in sorted(first_chart.items()):
        if sid in nonwild_sids:
            continue
        # 同チャートのダンジョン(本編のみ)に、属性が合えば入る。1つも合わなければ全部に入る
        same = [d for d in dtable if d["chart"] == c]
        hit = [d for d in same if matches(d, sid)]
        if not hit:
            hit = same
            no_match_fallback.append(sid)
        for d in hit:
            pool[sid].append(d["id"])
        # 前のチャートまでに登場した属性の合うパル
        for d in dtable:
            if d["eff_chart"] > c and d["chart"] != c and matches(d, sid):
                if d["id"] not in pool[sid]:
                    pool[sid].append(d["id"])
        # (補完ダンジョンは「挿入点までに登場済み」= eff_chart >= c の属性一致)
    # 補完は eff_chart の方が小さい場合があるため、条件を eff_chart > c ではなく >= c で再確認
    for sid, c in first_chart.items():
        if sid in nonwild_sids:
            continue
        for d in dtable:
            if d["chart"] > 20 and d["eff_chart"] >= c and matches(d, sid) and d["id"] not in pool[sid]:
                pool[sid].append(d["id"])
    # 固有パル(ネームド)・ボスは指定ダンジョンの出現種に必ず入れる
    for n, dn in NAMED_DUNGEON.items():
        sid = by_name[n]["species_id"]
        if sid in nonwild_sids:
            continue
        did = by_dname[dn]["id"]
        if did not in pool[sid]:
            pool[sid].append(did)
        forced_by[(sid, did)] = "ネームド"
    boss_dungeons = defaultdict(list)
    for d in dtable:
        for b in d["bosses"]:
            sid = by_name[b]["species_id"]
            boss_dungeons[sid].append(d["id"])
            if sid not in nonwild_sids:
                if d["id"] not in pool[sid]:
                    pool[sid].append(d["id"])
                forced_by.setdefault((sid, d["id"]), "ボス")
    order_of = {d["id"]: i for i, d in enumerate(dtable)}

    # ---- 出力行
    legend_slots = {}
    for k, sid in enumerate(legendary):
        legend_slots[sid] = "x%d" % (36 + k)
    rows = []
    for s in sorted(species, key=lambda s: s["species_id"]):
        sid = s["species_id"]
        row = dict(species_id=sid, display_name=s["display_name"], types=s["types"], bst6=bst6(s))
        if s.get("is_legendary"):
            row.update(first_chart=None, dungeons=[], wild=False, category="legendary",
                       recruit_rule=dict(kind="postclear_boss", dungeon=legend_slots[sid], rate=0.30))
            if sid == "198":
                row["cameo_chart"] = 14
                row["cameo_note"] = "チャート14のシナリオ戦(x_root_base)"
            if sid == "203":
                row["cameo_chart"] = 19
                row["cameo_note"] = "チャート19・20の会話(顔出し)"
        elif sid in nonwild_sids:
            row.update(first_chart=first_chart[sid], dungeons=[], wild=False, category="nonwild",
                       recruit_rule=dict(kind="postclear_boss", dungeon="x43", rate=0.20),
                       boss_dungeons=sorted(boss_dungeons.get(sid, []), key=order_of.get))
        else:
            row.update(first_chart=first_chart[sid], dungeons=sorted(pool[sid], key=order_of.get),
                       wild=True, category="normal")
            if sid in playable_sids:
                row["playable"] = True
                row["recruit_rule"] = dict(kind="resident", chart=first_chart[sid])
            else:
                row["recruit_rule"] = dict(kind="wild", rate=None)
            if boss_dungeons.get(sid):
                row["boss_dungeons"] = sorted(boss_dungeons[sid], key=order_of.get)
        nm = [n for n, v in named.items() if by_name[n]["species_id"] == sid]
        if nm:
            row["named_character"] = True
        rows.append(row)
    ctx = dict(species=species, by_id=by_id, by_name=by_name, plan=plan, dtable=dtable, named=named,
               pins=pins, first_chart=first_chart, rows=rows, final_cost=final_cost, groups=groups,
               no_match_fallback=no_match_fallback, forced_by=forced_by, max_chart=max_chart,
               playable_sids=playable_sids, legend_slots=legend_slots, nonwild_sids=nonwild_sids,
               gchart=gchart, group_of=group_of, highlight_sids=highlight_sids)
    return ctx


# ------------------------------------------------------------------ レポート
def jp(t):
    return EN2JP[t]


def report(ctx):
    rows, by_id, plan = ctx["rows"], ctx["by_id"], ctx["plan"]
    fc = ctx["first_chart"]
    per = defaultdict(list)
    for sid, c in fc.items():
        per[c].append(sid)
    L = []
    w = L.append
    w("# パル登場計画 生成レポート")
    w("")
    w("`Tools/build_pal_plan.py`(固定シード %d、決定的)が `Data/story/pal_plan.json` と同時に生成する。手で編集しない。" % SEED)
    w("仕様: `docs/design/drafts/2026-10-31-scenario-implementation.md` §2-7。台本: `docs/design/scenario/script-1-30.md`。")
    w("")
    w("## 1. 台本の表との比較")
    w("")
    w("平均BST6と第一属性の内訳。台本の表は「主な第一属性」の上位3つだけを載せているので、**台本の3つ**は個数の一致を、それ以外の属性は「台本の3つの最小個数以下」を目標にした。")
    w("")
    w("| チャート | 数(台本/実) | 平均BST6 台本 | 平均BST6 実 | 差 | 台本の第一属性 | 実際の第一属性内訳(多い順) | 属性の一致 |")
    w("| --- | --- | --- | --- | --- | --- | --- | --- |")
    dev_avg, dev_type = [], []
    for c in range(1, 21):
        sids = per[c]
        avg = sum(bst6(by_id[i]) for i in sids) / len(sids)
        ft = Counter(by_id[i]["types"][0] for i in sids)
        tt = plan[c]["types"]
        lo = min(tt.values())
        ok = all(ft.get(t, 0) == n for t, n in tt.items()) and all(ft.get(t, 0) <= lo for t in ALL_TYPES if t not in tt)
        if not ok:
            dev_type.append(c)
        if abs(avg - plan[c]["avg"]) > 0.5:
            dev_avg.append((c, avg))
        tt_s = "・".join("%s%d" % (jp(t), n) for t, n in sorted(tt.items(), key=lambda x: -x[1]))
        ft_s = "・".join("%s%d" % (jp(t), n) for t, n in sorted(ft.items(), key=lambda x: (-x[1], ALL_TYPES.index(x[0]))))
        w("| %d | %d/%d | %d | %.1f | %+.1f | %s | %s | %s |" % (
            c, CHART_COUNTS[c - 1], len(sids), plan[c]["avg"], avg, avg - plan[c]["avg"], tt_s, ft_s, "一致" if ok else "**差あり**"))
    allavg = sum(bst6(by_id[i]) for i in fc) / 280
    tavg = sum(plan[c]["avg"] * CHART_COUNTS[c - 1] for c in plan) / 280
    w("")
    w("全体の平均BST6: 台本の表の加重平均 %.2f、実際 %.2f(280種の合計は%d。台本の表の合計 %d。差は台本の平均が整数に丸められているため)。" % (
        tavg, allavg, sum(bst6(by_id[i]) for i in fc), sum(plan[c]["avg"] * CHART_COUNTS[c - 1] for c in plan)))
    w("")
    w("## 2. チャート別の一覧")
    w("")
    w("凡例: ★=台本の目玉、☆=ネームド人物、◆=ボス(本編ダンジョン)、非=野生で出ない9体、住=プレイアブル(住民枠)。")
    pins = ctx["pins"]
    boss_sids = {sid for r in rows for sid in [r["species_id"]] if r.get("boss_dungeons")}
    for c in range(1, 21):
        sids = sorted(per[c], key=lambda i: (-bst6(by_id[i]), i))
        w("")
        w("### チャート%d %s(%d種、平均BST6 %.1f)" % (c, plan[c]["stage"], len(sids), sum(bst6(by_id[i]) for i in sids) / len(sids)))
        w("")
        w("| 図鑑 | 名前 | 属性 | BST6 | 印 | 出現ダンジョン |")
        w("| --- | --- | --- | --- | --- | --- |")
        rmap = {r["species_id"]: r for r in rows}
        for i in sids:
            r = rmap[i]
            marks = []
            if i in ctx["highlight_sids"][c]:
                marks.append("★")
            if r.get("named_character"):
                marks.append("☆")
            if r.get("boss_dungeons"):
                marks.append("◆")
            if i in ctx["nonwild_sids"]:
                marks.append("非")
            if i in ctx["playable_sids"]:
                marks.append("住")
            dg = ",".join(r["dungeons"]) if r["dungeons"] else ("(ボス専用: %s)" % ",".join(r.get("boss_dungeons", [])) if i in ctx["nonwild_sids"] else "")
            w("| %s | %s | %s | %d | %s | %s |" % (i, r["display_name"], "・".join(jp(t) for t in r["types"]), r["bst6"], "".join(marks), dg))
    w("")
    w("## 3. 伝説7体(本編に初登場しない)")
    w("")
    for r in rows:
        if r["category"] == "legendary":
            w("- %s %s(%s) -> 追加枠 %s、勧誘 %d%%%s" % (r["species_id"], r["display_name"], "・".join(jp(t) for t in r["types"]),
                                                   r["recruit_rule"]["dungeon"], round(r["recruit_rule"]["rate"] * 100),
                                                   "、顔出し: " + r["cameo_note"] if r.get("cameo_note") else ""))
    w("")
    w("## 4. 野生で出ない9体(ボス専用、チャート1〜20に初登場)")
    w("")
    for r in rows:
        if r["category"] == "nonwild":
            w("- %s %s: 初登場チャート%d、ボス先 %s、再挑戦は x43(勧誘20%%)" % (r["species_id"], r["display_name"], r["first_chart"], ",".join(r["boss_dungeons"]) or "(なし: 目玉のみ)"))
    w("")
    w("## 5. ダンジョン別の出現種数")
    w("")
    w("| id | チャート | 名前 | 階 | 属性 | ボス | 出現種数 |")
    w("| --- | --- | --- | --- | --- | --- | --- |")
    cnt = Counter(d for r in rows for d in r["dungeons"])
    for d in ctx["dtable"]:
        w("| %s | %s | %s | %d | %s | %s | %d |" % (d["id"], d["chart"] if d["chart"] <= 20 else "%d(補完、チャート%d完了後)" % (d["chart"], d["eff_chart"]),
                                                  d["name"], d["floors"], "全属性" if d["all_types"] else "・".join(jp(t) for t in d["types"]),
                                                  "、".join(d["bosses"]), cnt[d["id"]]))
    w("")
    w("## 6. 台本の制約からの逸脱・台本内の矛盾・判断")
    w("")
    for line in DEVIATIONS(ctx, dev_avg, dev_type):
        w(line)
    OUT_MD.write_text("\n".join(L) + "\n", encoding="utf-8")


def DEVIATIONS(ctx, dev_avg, dev_type):
    by_id, plan, rows = ctx["by_id"], ctx["plan"], ctx["rows"]
    fc = ctx["first_chart"]
    out = []
    a = out.append
    # 目玉がBST上位か
    bad = []
    for c in range(1, 21):
        hl = ctx["highlight_sids"][c]
        floor = min(bst6(by_id[i]) for i in hl)
        over = [i for i in fc if fc[i] == c and i not in hl and bst6(by_id[i]) > floor]
        if over:
            bad.append("チャート%d: %s" % (c, "、".join("%s(%d)" % (by_id[i]["display_name"], bst6(by_id[i])) for i in over)))
    a("### 台本の制約と照合した結果")
    a("")
    if dev_avg:
        a("- 平均BST6が台本の表から±0.5を超えた章: " + "、".join("チャート%d(%.1f 対 %d)" % (c, v, plan[c]["avg"]) for c, v in dev_avg))
    else:
        a("- 平均BST6: 全20チャートが台本の表の値から±0.5以内(表が整数に丸められているため、これ以上は合わせられない)。")
    if dev_type:
        a("- 第一属性の内訳が台本の表と一致しなかった章: " + "、".join(str(c) for c in dev_type) + "(上の表の「差あり」行)")
    else:
        a("- 第一属性の内訳: 台本が載せている上位3属性は全20チャートで個数が一致し、載っていない属性は上位3つの最小個数を超えない。")
    if bad:
        a("- **台本は目玉を「BST上位」としているが、目玉より BST6 の高い別種がいる章**(目玉の固定、属性内訳、平均値を満たしたうえでの結果):")
        for b in bad:
            a("  - " + b)
    else:
        a("- 目玉(台本で「BST上位」とされる3種)は、全チャートでその章の BST6 上位3種に入っている。")
    a("")
    a("- 残った差の理由: 目玉・ネームド・ボス・変種ペア・プレイアブル(チャート2〜5に4体ずつ)の固定と、台本の属性内訳・平均BST6・「目玉がBST上位」を**同時に**満たす配置は、焼きなましでは見つからなかった(不可能の証明はしていない)。優先順位は 数・固定 > 属性内訳 > 平均BST6(±0.5を目標) > 目玉のBST上位。")
    a("- 台本の平均BST6は整数に丸められた値で、280種の合計は台本の表の合計と4(115510 対 115506)しか違わない。つまり台本の表は実在の割り当てから作られており、種族値(BST6)の現行データとはほぼ整合している。")
    a("")
    a("### 台本内の矛盾・曖昧さ(決めずに残したものは「未決」)")
    a("")
    a("1. **ゼノドランは目玉(チャート14)だが、ボスはチャート20**(最終戦)。初登場=14、ボス先=世界樹・梢(`boss_dungeons`)として両立させた。")
    a("2. **「野生で出ない9体は一般出現表に入れない」と「ボス・ネームドは出現ダンジョンの出現種に含む」**がぶつかる。野生で出ない9体は `dungeons[]` を空にし、ボス先を拡張キー `boss_dungeons[]` に分けた。検証器はこの解釈で検査する。")
    a("3. **ゼノベーダの初登場チャート**: 台本は「ゼノルーダの初登場はチャート12」としか書かず、ゼノベーダの章は不明。群れ戦の相方なのでチャート12にした。**未決**(台本の意図が11以前の可能性)。")
    a("4. **プレイアブル16体の数え方**: 台本は「4体ずつ(チャート2〜5)」、基本仕様 §2-4 の補足は選ばれなかった14体を「4/4/3/3」とする。計画は16体全部を 4/4/4/4 でチャート2〜5の初登場として数え(280に含む)、選ばれた2体を実行時に除く前提。主人公・相棒としてチャート1に出る分は `first_chart` に反映していない。")
    a("5. **プレイアブルの野生出現**: 台本は「住民枠として別管理で重複させない」とだけ書く。`recruit_rule.kind=\"resident\"` と `playable=true` を付け、`wild=true`(非野生16体の外)のまま属性の合う本編ダンジョンの出現種にも入れた。住民として加入した個体を野生と重複させたくなければ、実装側で `playable` を見て外す。**未決**。")
    a("6. **補完ダンジョンの出現種**: 台本は「挿入点までに登場済みのパルから選ぶ」。実効チャート=挿入点(31→1、32→7、33→9、34→12、35→14、基本仕様の決定 §2-2)とし、`first_chart` ≤ 挿入点で属性が合う種を入れた。補完ボス(ガブルミ・カバネドリ・バニザラシ・プラムルリム・ウゴクゾー)が挿入点までに登場済みであることは検証器が確認する。プラムルリムは台本で章の指定が無いので、チャート12以前になるよう制約した(結果は上の一覧)。")
    a("7. **新規種の出現先**: 台本は「初登場パルごとの出現ダンジョンはCSVにある」が、CSVが無い。同チャートの本編ダンジョンのうち属性(第一・第二のどちらか)が合うものに入れ、1つも合わなければそのチャートの全ダンジョンに入れた。該当した種: %s。" % (
        "、".join("%s(チャート%d)" % (by_id[i]["display_name"], fc[i]) for i in sorted(ctx["no_match_fallback"])) or "なし"))
    a("8. **「全属性」ダンジョン**(祭りの裏路・世界樹への道): 属性条件なしで、そのチャートまでの全種が入る。")
    a("9. **ダンジョンの id** は仮(`dNN_cM` / `dNN_sM`、NN=台本表の行番号)。`Data/story/dungeons.json`(P4)で決まる id と対応づけること。**未決**。")
    a("10. **伝説7体の追加枠**: 36〜42 に species_id 順で 198,199,200,200B,201,202,203 を割り当てた(台本に順序の指定なし)。**未決**。再挑戦回廊 x43 は野生で出ない9体。勧誘確率は伝説30%、ボスパル(9体)20%(§1-5)。")
    a("11. **野生の勧誘確率**: 一般種の `recruit_rule` は `{kind: wild, rate: null}`(確率の指定が仕様に無い。既存の勧誘ロジックの既定を使う想定)。**未決**。")
    a("12. **JSON の形**: 基本仕様 §3 の列をそのまま持つ 287 行の配列。`display_name`・`types`・`bst6`・`boss_dungeons`・`playable`・`named_character`・`cameo_chart` は検証と確認のための追加キー(読み手は無視してよい)。")
    return out


def main():
    ctx = build()
    OUT_JSON.parent.mkdir(parents=True, exist_ok=True)
    with open(OUT_JSON, "w", encoding="utf-8", newline="\n") as f:
        json.dump(ctx["rows"], f, ensure_ascii=False, indent=1)
        f.write("\n")
    report(ctx)
    print("wrote", OUT_JSON, OUT_MD, "cost=%.1f" % ctx["final_cost"])


if __name__ == "__main__":
    main()
