#!/usr/bin/python3
"""Data/story/pal_plan.json が §2-7 の制約を満たすかを検査する。

  /usr/bin/python3 Tools/verify_pal_plan.py              # 検査(数秒)
  /usr/bin/python3 Tools/verify_pal_plan.py --rebuild    # さらに生成器を再実行して一致(冪等性)を確認(約2分)

FAIL が1つでもあれば終了コード1。WARN は台本の表との差(目標値からのずれ)で、終了コードに影響しない。
"""
import json
import sys
from collections import Counter, defaultdict
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import build_pal_plan as B  # 台本の表の読み取りと dungeon id の付与だけを共有する

ROOT = B.ROOT
COUNTS = [12, 14, 14, 14, 15, 14, 14, 14, 14, 20, 13, 13, 14, 14, 14, 14, 14, 13, 16, 10]
NONWILD9 = ["シャオロン", "ヒョウガオー", "レジェンディア", "ベラルージュ", "ベラノワール",
            "ゼノグリフ", "ゼノドラン", "ゼノベーダ", "ゼノルーダ"]
PLAYABLE = B.PLAYABLE_NAMES

fails, warns = [], []


def check(ok, msg):
    if not ok:
        fails.append(msg)
    return ok


def main():
    rows = json.loads((ROOT / "Data" / "story" / "pal_plan.json").read_text(encoding="utf-8"))
    species = json.loads((ROOT / "Data" / "species.json").read_text(encoding="utf-8"))
    by_id = {s["species_id"]: s for s in species}
    by_name = {s["display_name"]: s for s in species}
    R = {r["species_id"]: r for r in rows}

    # --- 基本形
    check(len(rows) == 287 and len(R) == 287, "行数が287・重複なしでない: %d" % len(rows))
    check(set(R) == set(by_id), "species.json との種族ID集合が一致しない")
    for r in rows:
        for k in ("species_id", "first_chart", "dungeons", "wild", "category", "recruit_rule"):
            if k not in r:
                check(False, "%s にキー %s が無い" % (r.get("species_id"), k))

    # --- 区分
    leg = {s["species_id"] for s in species if s.get("is_legendary")}
    cat = Counter(r["category"] for r in rows)
    check(cat == Counter(normal=271, legendary=7, nonwild=9), "category の内訳が 271/7/9 でない: %s" % dict(cat))
    check({i for i, r in R.items() if r["category"] == "legendary"} == leg, "legendary が is_legendary と一致しない")
    nw = {by_name[n]["species_id"] for n in NONWILD9}
    check({i for i, r in R.items() if r["category"] == "nonwild"} == nw, "nonwild が台本の9体と一致しない")
    wild_false = {i for i, r in R.items() if not r["wild"]}
    check(len(wild_false) == 16 and wild_false == leg | nw, "wild=false が伝説7+9体の16種でない (%d)" % len(wild_false))
    check(all(R[i]["wild"] for i in R if i not in wild_false), "wild=false 以外に wild=true でない種がいる")

    # --- 280種とチャート別数
    in_charts = {i: r["first_chart"] for i, r in R.items() if r["first_chart"] is not None}
    check(len(in_charts) == 280, "チャート1〜20に初登場する種が280でない: %d" % len(in_charts))
    check(all(isinstance(c, int) and 1 <= c <= 20 for c in in_charts.values()), "first_chart が1〜20の整数でない")
    check(all(R[i]["first_chart"] is None for i in leg), "伝説に first_chart が入っている")
    per = Counter(in_charts.values())
    got = [per[c] for c in range(1, 21)]
    check(got == COUNTS, "チャート別の初登場数が台本と違う: %s" % got)

    # --- 変種ペア
    grp = defaultdict(list)
    for i in R:
        grp["".join(ch for ch in i if ch.isdigit())].append(i)
    pairs = [v for v in grp.values() if len(v) == 2]
    for v in pairs:
        check(R[v[0]]["first_chart"] == R[v[1]]["first_chart"] and R[v[0]]["category"] == R[v[1]]["category"],
              "変種ペア %s が別チャート/別区分: %s" % (v, [R[i]["first_chart"] for i in v]))
    print("変種ペア %d 組を確認" % len(pairs))

    # --- 台本の表を読み直して目玉・ネームド・ボスを照合
    plan, dungeons, named = B.parse_script(set(by_name))
    dtable = []
    for i, d in enumerate(dungeons):
        dd = dict(d)
        dd["id"] = B.dungeon_id(i, d)
        dd["eff"] = d["chart"] if d["chart"] <= 20 else B.SUPPLEMENT_AFTER[d["chart"]]
        dtable.append(dd)
    dids = {d["id"] for d in dtable}
    by_dname = {d["name"]: d for d in dtable}
    check(len(dtable) == 35, "ダンジョン表が35行でない: %d" % len(dtable))

    n_me = 0
    for c, p in plan.items():
        for n in p["highlights"]:
            n_me += 1
            sid = by_name[n]["species_id"]
            check(R[sid]["first_chart"] == c, "目玉 %s がチャート%d にいない (%s)" % (n, c, R[sid]["first_chart"]))
    print("目玉 %d 種を確認" % n_me)
    check(len(named) == 16, "ネームド人物の読み取りが16でない: %d" % len(named))
    for n, v in named.items():
        sid = by_name[n]["species_id"]
        check(R[sid]["first_chart"] == v["chart"], "ネームド %s がチャート%d にいない (%s)" % (n, v["chart"], R[sid]["first_chart"]))
        dn = B.NAMED_DUNGEON[n]
        did = by_dname[dn]["id"]
        if R[sid]["wild"]:
            check(did in R[sid]["dungeons"], "ネームド %s が %s の出現種に無い" % (n, dn))
        else:
            check(did in R[sid].get("boss_dungeons", []), "ネームド(非野生) %s が %s のボス先に無い" % (n, dn))
    for d in dtable:
        for b in d["bosses"]:
            sid = by_name[b]["species_id"]
            fc = R[sid]["first_chart"]
            if d["chart"] <= 20:
                if b not in B.BOSS_CHART_EXCEPT:
                    check(fc == d["chart"], "ボス %s(チャート%d %s)の初登場が %s" % (b, d["chart"], d["name"], fc))
                else:
                    check(fc <= d["chart"], "ボス %s の初登場がボスより後" % b)
            else:
                check(fc <= d["eff"], "補完ボス %s の初登場チャート%s が挿入点%d より後" % (b, fc, d["eff"]))
            if R[sid]["wild"]:
                check(d["id"] in R[sid]["dungeons"], "ボス %s が %s の出現種に無い" % (b, d["name"]))
            else:
                check(d["id"] in R[sid].get("boss_dungeons", []), "ボス(非野生) %s が %s のボス先に無い" % (b, d["name"]))
    # 非野生9体は台本のボス表のどこかに現れる(ゼノベーダは群れ戦)
    for n in NONWILD9:
        check(R[by_name[n]["species_id"]].get("boss_dungeons"), "非野生 %s がボス先を持たない" % n)

    # --- 出現表
    for i, r in R.items():
        for d in r["dungeons"]:
            check(d in dids, "%s: 未知のダンジョンid %s" % (i, d))
        if not r["wild"]:
            check(r["dungeons"] == [], "wild=false の %s が出現表に入っている: %s" % (i, r["dungeons"]))
    dd_by_id = {d["id"]: d for d in dtable}
    for i, r in R.items():
        for d in r["dungeons"]:
            if d in dd_by_id:
                check(r["first_chart"] is not None and r["first_chart"] <= dd_by_id[d]["eff"],
                      "%s がダンジョン %s に、初登場(%s)より前のチャートで出る" % (i, d, r["first_chart"]))
    for d in dtable:
        n = sum(1 for r in R.values() if d["id"] in r["dungeons"])
        check(n >= 8, "ダンジョン %s の出現種が少なすぎる: %d" % (d["id"], n))
    # 新規種は少なくとも1つの本編ダンジョンに出る
    for i, r in R.items():
        if r["wild"] and r["first_chart"] is not None:
            own = [d["id"] for d in dtable if d["chart"] == r["first_chart"]]
            check(any(d in r["dungeons"] for d in own), "%s が初登場チャートのダンジョンのどれにも出ない" % i)

    # --- プレイアブル16体
    pl = [by_name[n]["species_id"] for n in PLAYABLE]
    check(len(set(pl)) == 16, "プレイアブル16体の特定に失敗")
    pc = Counter(R[i]["first_chart"] for i in pl)
    for i in pl:
        r = R[i]
        check(r["first_chart"] in (2, 3, 4, 5), "プレイアブル %s の初登場がチャート2〜5でない: %s" % (r["display_name"], r["first_chart"]))
        check(r.get("playable") is True and r["wild"] and r["category"] == "normal", "プレイアブル %s の区分が想定外" % r["display_name"])
        check(r["recruit_rule"].get("kind") == "resident" and r["recruit_rule"].get("chart") == r["first_chart"],
              "プレイアブル %s の recruit_rule が resident/初登場チャートでない" % r["display_name"])
    check(sum(1 for r in rows if r.get("playable")) == 16, "playable=true が16件でない")
    pcs = [pc[c] for c in (2, 3, 4, 5)]
    check(max(pcs) <= 4 and sum(pcs) == 16, "プレイアブルの各チャート数が不正: %s" % pcs)
    if pcs != [4, 4, 4, 4]:
        warns.append("プレイアブルのチャート別数が 4/4/4/4 でない: %s(チャート2〜5・計16)" % pcs)
    print("プレイアブル チャート2〜5 の人数: %s" % pcs)
    ptypes = {}
    for c in (2, 3, 4, 5):
        ts = [by_id[i]["types"][0] for i in pl if R[i]["first_chart"] == c]
        ptypes[c] = Counter(ts)
        if any(v > 1 for v in ptypes[c].values()):
            warns.append("プレイアブルの第一属性がチャート%dで重複: %s" % (c, {B.EN2JP[t]: v for t, v in ptypes[c].items()}))

    # --- 伝説・非野生の勧誘
    slots = []
    for i in sorted(leg):
        rr = R[i]["recruit_rule"]
        check(rr.get("kind") == "postclear_boss" and abs(rr.get("rate", 0) - 0.30) < 1e-9, "伝説 %s の勧誘が30%%でない" % i)
        slots.append(rr.get("dungeon"))
    check(sorted(slots) == sorted("x%d" % n for n in range(36, 43)), "伝説の追加枠が x36〜x42 の7つでない: %s" % slots)
    for i in sorted(nw):
        rr = R[i]["recruit_rule"]
        check(rr.get("dungeon") == "x43" and abs(rr.get("rate", 0) - 0.20) < 1e-9, "非野生 %s の再挑戦が x43/20%% でない" % i)
    check(R["198"].get("cameo_chart") == 14 and R["203"].get("cameo_chart") == 19, "セイントール(14)・グランジーラ(19)の顔出しが無い")

    # --- 台本の表との近さ(WARN)
    ft = defaultdict(Counter)
    tot = Counter()
    for i, c in in_charts.items():
        ft[c][by_id[i]["types"][0]] += 1
        tot[c] += B.bst6(by_id[i])
    max_dev, bad_t = 0.0, []
    for c in range(1, 21):
        a = tot[c] / COUNTS[c - 1]
        max_dev = max(max_dev, abs(a - plan[c]["avg"]))
        if abs(a - plan[c]["avg"]) > 0.5:
            warns.append("チャート%d 平均BST6 %.1f(台本 %d)" % (c, a, plan[c]["avg"]))
        tt = plan[c]["types"]
        lo = min(tt.values())
        diff = [(B.EN2JP[t], ft[c][t], tt.get(t)) for t in B.ALL_TYPES
                if (t in tt and ft[c][t] != tt[t]) or (t not in tt and ft[c][t] > lo)]
        if diff:
            bad_t.append(c)
            warns.append("チャート%d 属性内訳が台本と差: %s" % (c, ", ".join("%s実%d/台本%s" % (j, a_, b_ if b_ is not None else "<=%d" % lo) for j, a_, b_ in diff)))
    print("平均BST6 台本との最大差 %.2f、属性内訳が台本と違うチャート: %s" % (max_dev, bad_t or "なし"))

    # --- 冪等性(任意)
    if "--rebuild" in sys.argv:
        ctx = B.build()
        same = json.dumps(ctx["rows"], ensure_ascii=False, indent=1) + "\n" == (ROOT / "Data" / "story" / "pal_plan.json").read_text(encoding="utf-8")
        check(same, "生成器を再実行した結果が pal_plan.json と一致しない(冪等でない)")
        print("再生成との一致: %s" % same)

    for w in warns:
        print("WARN", w)
    for f in fails:
        print("FAIL", f)
    print("RESULT: %s (FAIL %d, WARN %d)" % ("ALL PASS" if not fails else "FAILED", len(fails), len(warns)))
    return 1 if fails else 0


if __name__ == "__main__":
    sys.exit(main())
