#!/usr/bin/python3
"""Data/story の P3/P4 項目を検査する（台本の表との突き合わせ・参照切れ・語尾の粗い検査）。

  /usr/bin/python3 Tools/verify_story.py

FAIL が1つでもあれば終了コード1。WARN は台本の表との差（属性の食い違いなど）で、終了コードに影響しない。
ヘッドレスでの「生成→最終階到達」は回帰スイート（StoryDungeonTests.cs）側で検査する。
"""
import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
S = ROOT / "Data" / "story"
SCRIPT = (ROOT / "docs" / "design" / "scenario" / "script-1-30.md").read_text(encoding="utf-8")
fails, warns = [], []


def check(ok, msg):
    if not ok:
        fails.append(msg)
    return ok


def load(p):
    return json.loads(Path(p).read_text(encoding="utf-8"))


def section(title):
    m = re.search(r"^## %s\n(.*?)(?=^## )" % re.escape(title), SCRIPT, re.S | re.M)
    return m.group(1) if m else ""


def rows(text):
    for ln in text.splitlines():
        if ln.startswith("|") and not re.match(r"^\|\s*-", ln):
            yield [c.strip() for c in ln.strip().strip("|").split("|")]


EL = {"無": "Neutral", "水": "Water", "炎": "Fire", "草": "Grass", "地": "Ground", "闇": "Dark", "虚": "Void",
      "電": "Electric", "氷": "Ice", "竜": "Dragon"}
O = ["INFJ", "INFP", "INTJ", "INTP", "ISFJ", "ISFP", "ISTJ", "ISTP", "ENFJ", "ENFP", "ENTJ", "ENTP", "ESFJ", "ESFP", "ESTJ", "ESTP"]


def main():
    species = load(ROOT / "Data" / "species.json")
    by_id = {s["species_id"]: s for s in species}
    names = sorted({s["display_name"] for s in species}, key=len, reverse=True)
    by_name = {}
    for s in species:
        by_name.setdefault(s["display_name"], s)
    charts, dungeons = load(S / "charts.json"), load(S / "dungeons.json")
    chars, voice = load(S / "characters.json"), load(S / "voice.json")
    rules = {r["id"]: r for r in load(ROOT / "Data" / "dungeons.json")}
    plan = {r["species_id"]: r for r in load(S / "pal_plan.json")}
    curve = load(S / "level_curve.json")
    D = {d["id"]: d for d in dungeons}
    C = {c["id"]: c for c in charts}

    # --- チャートの形
    ids = [c["id"] for c in charts]
    want = ["c%02d" % n for n in range(1, 36)] + ["x%d" % n for n in range(36, 45)]
    check(len(set(ids)) == len(ids) and set(ids) == set(want), "チャートは c01〜c35 と x36〜x44 の44件でない")
    check(all(C["c%02d" % n]["kind"] == "main" and C["c%02d" % n]["unlock"]["charts"] == (["c%02d" % (n - 1)] if n > 1 else [])
              for n in range(1, 21)), "本編1〜20の unlock が前チャート完了になっていない")
    check(all(C["c%d" % n]["kind"] == "resident" for n in range(21, 31)), "チャート21〜30が resident でない")
    check(all(C["c%d" % n]["kind"] == "supplement" and C["c%d" % n]["insert_after"] for n in range(31, 36)), "補完31〜35に insert_after が無い")
    ia = {n: (C["c%d" % n]["insert_after"]["chart"], C["c%d" % n]["insert_after"]["event"]) for n in range(31, 36)}
    check(ia == {31: ("c01", "chart_end"), 32: ("c07", "chart_end"), 33: ("c09", "chart_end"), 34: ("c12", "chart_end"), 35: ("c14", "c14_e04")},
          "補完の挿入点が台本（1/7/9/12 完了後・14の④）と違う: %s" % ia)
    check(C["c20"].get("ends_game") is True, "c20 に ends_game が無い")
    check(all(C["x%d" % n]["kind"] == "extra" and C["x%d" % n]["unlock"].get("post_clear") for n in range(36, 45)), "追加枠が PostClear 限定でない")

    # --- ダンジョン: 台本の表と突き合わせる
    tbl = []
    for r in rows(section("踏破ダンジョンとボス")):
        if len(r) == 5 and re.match(r"^\d+", r[0]):
            tbl.append(r)
    check(len(tbl) == 35, "台本のダンジョン表が35行でない: %d" % len(tbl))
    main_d = [d for d in dungeons if d["id"][0] == "d"]
    check(len(main_d) == 35, "dungeons.json の本編ダンジョンが35本でない: %d" % len(main_d))
    for r, d in zip(tbl, main_d):
        chart = int(re.match(r"\d+", r[0]).group())
        tag = "%s(%s)" % (d["id"], d["name"])
        check(d["chart"] == "c%02d" % chart, "%s: チャート %s != 台本 %d" % (tag, d["chart"], chart))
        check(d["name"] == r[1], "%s: 名前が台本 '%s' と違う" % (tag, r[1]))
        check(d["floors"] == int(r[2]), "%s: 階数 %d != 台本 %s" % (tag, d["floors"], r[2]))
        wantn = []
        for n in names:
            if n in re.sub(r"\(.*\)", "", r[4]) and not any(n in w for w in wantn):
                wantn.append(n)
        # 「ゼノベーダ複数とゼノルーダ複数」→ 主ボス+群れ。species を集合で比べる
        have = {d["boss"]["species_id"]} | {g["species_id"] for g in d["boss"].get("group", [])}
        check({by_name[n]["species_id"] for n in wantn} == have, "%s: ボス %s != 台本 %s" % (tag, sorted(have), wantn))
        wt = r[3]
        types = ["All"] if wt == "全属性" else [EL[c] for c in wt.replace("・", "")]
        rule = rules.get(d["rule_id"])
        check(rule is not None and rule["floors"] == d["floors"] and rule["main_types"] == types,
              "%s: 生成ルールの階数/主な属性が台本と違う %s" % (tag, rule and (rule["floors"], rule["main_types"])))
        check(plan.get(d["boss"]["species_id"], {}).get("boss_dungeons", []).count(d["id"]) == 1 or d["id"] == "d30_c20",
              "%s: pal_plan の boss_dungeons にボスが載っていない" % tag)
    check(sum(1 for d in main_d if d["chart"] in ["c%02d" % n for n in range(1, 21)]) == 30, "本編ダンジョンが30本でない")
    check(D["d02_c2"]["boss"]["defeat_ok"] and D["d02_c2"]["end_type"] == "StoryBoss", "ヘルガルダ戦が StoryBoss/defeat_ok でない")
    rb = D["x_root_base"]
    check(rb["floors"] == 1 and rb["end_type"] == "StoryBoss" and rb["boss"]["defeat_ok"] and rb["hidden"]
          and rb["boss"]["species_id"] == by_name["セイントール"]["species_id"], "x_root_base の定義が仕様と違う")
    check(D["d17_c12"]["end_type"] == "StoryMultiEnemyBattle" and len(D["d17_c12"]["boss"]["group"]) >= 2, "ささやきの洞が群れ戦でない")
    check(len(D["d16_c12"]["boss"]["group"]) == 1, "影の森が2体ボスでない")
    ex = [d for d in dungeons if d["id"].startswith("x") and d["id"] != "x_root_base"]
    check([d["id"] for d in ex] == ["x%d" % n for n in range(36, 44)] and all(d["post_clear"] for d in ex), "追加枠36〜43の枠が揃っていない")
    leg = [by_name[n]["species_id"] for n in ["セイントール", "ベイントール", "グレイシャル", "グレイシャドウ", "ネプティオス", "ジェッドラン", "グランジーラ"]]
    check([D["x%d" % n]["boss"]["species_id"] for n in range(36, 43)] == leg, "x36〜42 のボスが伝説7体（pal_plan の順）でない")
    for d in dungeons:
        check(d["rule_id"] in rules, "%s: rule_id が Data/dungeons.json に無い" % d["id"])
        check(d["chart"] in C, "%s: chart が無い" % d["id"])
        if d.get("boss"):
            for sp in [d["boss"]["species_id"]] + [g["species_id"] for g in d["boss"].get("group", [])]:
                check(sp in by_id, "%s: 種族 %s が無い" % (d["id"], sp))
    for c in charts:
        for d in c["dungeons"]:
            check(d in D, "%s: dungeon %s が無い" % (c["id"], d))

    # --- レベル曲線
    def lv(n):
        return int(round(curve["first_level"] + (n - curve["first_chart"]) * (curve["last_level"] - curve["first_level"]) / (curve["last_chart"] - curve["first_chart"]) + 1e-9))
    check(lv(1) == 5 and lv(20) == 60, "曲線の端がチャート1=Lv5・20=Lv60でない")
    for d in main_d:
        c = C[d["chart"]]
        n = c.get("level_chart") or int(d["chart"][1:])
        check(d["boss"]["level"] == lv(n), "%s: ボスLv %d != 曲線 %d" % (d["id"], d["boss"]["level"], lv(n)))
    check(all(D["x%d" % n]["boss"]["level"] == curve["legendary_level"] for n in range(36, 43)), "追加枠の伝説が Lv70 でない")
    check(rb["boss"]["level"] == lv(14), "根もとのセイントールがチャート14のレベルでない")

    # --- 野生出現（pal_plan）
    for sid, r in plan.items():
        for d in r.get("dungeons", []) + r.get("boss_dungeons", []):
            check(d in D or d.startswith("x"), "pal_plan %s: dungeon %s が無い" % (sid, d))
        if not r["wild"]:
            check(not r.get("dungeons"), "pal_plan %s: wild=false なのに dungeons がある" % sid)
    for d in main_d:
        pool = [sid for sid, r in plan.items() if r["wild"] and d["id"] in r.get("dungeons", []) and d["id"] not in r.get("boss_dungeons", [])]
        check(len(pool) >= 3, "%s: 野生の出現表が3種未満 (%d)" % (d["id"], len(pool)))

    # --- 人物
    named = {}
    for r in rows(section("主要登場人物と登場タイミング")):
        if r[0].isdigit() and r[1] in by_name:
            named[r[1]] = (int(r[0]), r[2], r[3])
    check(len(named) == 16, "台本のネームド人物が16人でない: %d" % len(named))
    byname = {c["display_name"]: c for c in chars}
    for n, (chart, mbti, attr) in named.items():
        c = byname.get(n)
        if not check(c is not None, "characters.json に %s が無い" % n):
            continue
        check(c["mbti"] == mbti and c["first_chart"] == "c%02d" % chart and c["species_id"] == by_name[n]["species_id"],
              "%s: mbti/初登場/種族が台本と違う" % n)
        want_t = [EL[x] for x in attr.replace("・", "") if x in EL]
        have_t = by_id[c["species_id"]]["types"]
        if want_t != have_t:
            warns.append("属性の食い違い %s: 台本 %s / データ %s（characters には種族データ側が載る）" % (n, want_t, have_t))
    check(sum(1 for c in chars if c["role"] == "playable") == 16, "プレイアブルが16体でない")
    check(len(chars) == 32 and all(c["mbti"] in voice for c in chars), "characters が32件でない/ mbti が voice に無い")
    fp = {c["id"]: c.get("first_person") or voice[c["mbti"]]["first_person"] for c in chars}
    check(fp["bushigaeru"] == "拙者" and fp["levidra"] == "我" and fp["xenoglyph"] == "わたしたち", "一人称の上書き（拙者/我/わたしたち）が無い")

    # --- イベント
    evs = []
    for f in sorted((S / "events").glob("*.json")):
        evs += load(f)
    eids = [e["id"] for e in evs]
    check(len(set(eids)) == len(eids), "イベントIDが重複")
    check(all(re.match(r"^c\d\d_e\d\d(_\w+)?$", i) for i in eids), "イベントIDが cNN_eMM[_slug] でない")
    check(all(i in eids for c in charts for i in c["events"]), "charts.events に未定義のイベントがある")
    check(all(e["id"].split("_")[0] in C and e["id"] in C[e["id"].split("_")[0]]["events"] for e in evs), "どのチャートにも載っていないイベントがある")
    speakers = {"protagonist", "partner", "narrator"} | set(fp)
    firsts = {k: v for k, v in fp.items() if v}
    keyn = 0
    nonkey_by = 0

    def walk(steps, ev):
        nonlocal keyn
        for s in steps:
            op = s.get("op")
            if op == "say":
                who = s["who"]
                check(who in speakers, "%s: who '%s' が解決しない" % (ev["id"], who))
                bt = s.get("by_type")
                if s.get("key"):
                    keyn += 1
                    check(who in ("protagonist", "partner") and bt and set(bt) == set(O), "%s: key 行が16タイプ揃っていない" % ev["id"])
                texts = ([s["text"]] if "text" in s else []) + list((bt or {}).values())
                if who in fp and who not in ("protagonist", "partner"):
                    mine = fp[who]
                    for t in texts:
                        for oid, p in firsts.items():
                            if oid != who and p in ("拙者", "我", "わたしたち") and p != mine and p in t:
                                check(False, "%s: %s の台詞に別人の一人称 '%s'" % (ev["id"], who, p))
                    if mine in ("拙者", "我", "わたしたち"):
                        pass
                for t in texts:
                    for m in re.finditer(r"\{(\w+)(?::([^}]*))?\}", t):
                        check(m.group(1) in ("I", "catch", "player_name", "protagonist", "partner", "arrivals", "name"), "%s: 未知の差し込み %s" % (ev["id"], m.group(0)))
            for k in ("then", "else"):
                if isinstance(s.get(k), list):
                    walk(s[k], ev)
            for o in s.get("options", []) or []:
                walk(o.get("steps", []), ev)
    for e in evs:
        t = e.get("trigger") or {}
        check(t.get("type") in ("hub_enter", "dungeon_clear", "dungeon_floor", "boss_defeated", "chart_start", "chart_end", "time_passed"), "%s: trigger が不正" % e["id"])
        if t.get("dungeon"):
            check(t["dungeon"] in D, "%s: trigger.dungeon が無い" % e["id"])
        walk(e["steps"], e)
    check(keyn >= 6, "key 行（16タイプ手書き）が6本未満: %d" % keyn)
    c1 = [e for e in evs if e["id"].startswith("c01_")]
    c2 = [e for e in evs if e["id"].startswith("c02_")]
    check(sum(len(e["steps"]) for e in c1) >= 55 and sum(len(e["steps"]) for e in c2) >= 55, "チャート1/2の本文が短すぎる（各55ステップ未満）")
    check(any(s.get("op") == "name_input" for e in c1 for s in e["steps"]), "c01 に name_input が無い")
    check(any(s.get("who") == "bushigaeru" for e in c2 for s in e["steps"]) and any(s.get("who") == "helgalda" for e in c2 for s in e["steps"]), "c02 にブシガエル/ヘルガルダが登場しない")
    print("events=%d key行=%d c01=%dステップ c02=%dステップ" % (len(evs), keyn, sum(len(e["steps"]) for e in c1), sum(len(e["steps"]) for e in c2)))

    for w in warns:
        print("WARN", w)
    for f in fails:
        print("FAIL", f)
    print("RESULT: %s (FAIL %d, WARN %d)" % ("ALL PASS" if not fails else "FAILED", len(fails), len(warns)))
    return 1 if fails else 0


if __name__ == "__main__":
    sys.exit(main())
