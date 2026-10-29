#!/usr/bin/python3
"""参照アイコンの取得。palworld.gg の一覧から display_name -> 内部名 を引き、
Tools/.refcache/{species_id}/icon.png に保存する(追加の参照画像も同じ dir に手で集める)(gitignore。参照画像はコミットしない)。
対応表 Tools/pal_ref_map.json (species_id -> {icon: 内部名, en: 英名}) はコミットする。

  /usr/bin/python3 Tools/fetch_pal_refs.py
"""
import html
import json
import os
import re
import sys
import time
import urllib.parse
import urllib.request

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CACHE = os.path.join(ROOT, "Tools", ".refcache")
MAP = os.path.join(ROOT, "Tools", "pal_ref_map.json")
BASE = "https://palworld.gg"
UA = {"User-Agent": "Mozilla/5.0 (sprite-ref-fetch)"}


def get(url):
    req = urllib.request.Request(url, headers=UA)
    with urllib.request.urlopen(req, timeout=60) as r:
        return r.read()


def main():
    sps = json.load(open(os.path.join(ROOT, "Data", "species.json"), encoding="utf-8"))
    page = get(BASE + "/ja/pals").decode("utf-8")
    name2icon = {}
    for m in re.finditer(r'<a href="/ja/pal/([^"]+)"(.*?)</a>', page, re.S):
        name = urllib.parse.unquote(html.unescape(m.group(1)))
        ic = re.search(r"full_palicon/(T_[^/\"&\s]+?)_icon_normal\.png", m.group(2))
        if ic and name not in name2icon:
            name2icon[name] = ic.group(1)
    en = {}
    try:
        epage = get(BASE + "/en/pals").decode("utf-8")
        for m in re.finditer(r'<a href="/en/pal/([^"]+)"(.*?)</a>', epage, re.S):
            ic = re.search(r"full_palicon/(T_[^/\"&\s]+?)_icon_normal\.png", m.group(2))
            if ic:
                en.setdefault(ic.group(1), urllib.parse.unquote(html.unescape(m.group(1))))
    except Exception as e:  # 英名は補助情報
        print("en list failed:", e)
    mp, missing = {}, []
    for sp in sps:
        ic = name2icon.get(sp["display_name"])
        if ic is None:
            missing.append(sp["species_id"] + ":" + sp["display_name"])
        else:
            mp[sp["species_id"]] = {"icon": ic, "en": en.get(ic, "")}
    if missing:
        sys.exit("対応が見つからない: " + ", ".join(missing))
    with open(MAP, "w", encoding="utf-8") as f:
        json.dump(mp, f, ensure_ascii=False, indent=1, sort_keys=True)
        f.write("\n")
    os.makedirs(CACHE, exist_ok=True)
    got = 0
    for sp in sps:
        os.makedirs(os.path.join(CACHE, sp["species_id"]), exist_ok=True)
        out = os.path.join(CACHE, sp["species_id"], "icon.png")
        if os.path.exists(out) and os.path.getsize(out) > 0:
            continue
        data = get("%s/images/full_palicon/%s_icon_normal.png" % (BASE, mp[sp["species_id"]]["icon"]))
        with open(out, "wb") as f:
            f.write(data)
        got += 1
        time.sleep(0.2)
    print("mapped %d, downloaded %d" % (len(mp), got))


if __name__ == "__main__":
    main()
