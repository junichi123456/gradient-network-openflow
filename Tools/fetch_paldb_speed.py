#!/usr/bin/env python3
"""paldb.cc から287種の移動速度・ライド種別・運搬Lvを取得して Data/source/paldb_speed.json に保存する。

仕様書 docs/design/drafts/2026-10-25-void-type-and-habcds.md §0-4・§3-d・§3-e。
palworld-lab.com は Cloudflare で 403 のため、同じゲームデータを載せた paldb.cc を使う。

  python3 Tools/fetch_paldb_speed.py            # 未取得の種だけ取得（取得済みは再取得しない）
  python3 Tools/fetch_paldb_speed.py --refresh  # 全件取り直す

取得したHTML（生）はコミットしない。キャッシュは Data/source/.cache/（.gitignore 対象ではないが
コミットしないこと。--keep-cache を付けなければ終了時に消す）。
"""
import argparse, json, os, re, sys, time, urllib.request
from fractions import Fraction

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SPECIES = os.path.join(ROOT, 'Data', 'species.json')
OUT = os.path.join(ROOT, 'Data', 'source', 'paldb_speed.json')
CACHE = os.path.join(ROOT, 'Data', 'source', '.cache')
BASE = 'https://paldb.cc/ja/'
UA = 'Mozilla/5.0 (X11; Linux x86_64) HABCDS-fetch'


def get(url, path):
    if os.path.exists(path):
        return open(path, encoding='utf-8').read()
    last = None
    for attempt in range(4):
        try:
            req = urllib.request.Request(url, headers={'User-Agent': UA})
            with urllib.request.urlopen(req, timeout=60) as r:
                body = r.read().decode('utf-8')
            os.makedirs(os.path.dirname(path), exist_ok=True)
            open(path, 'w', encoding='utf-8').write(body)
            time.sleep(0.3)
            return body
        except Exception as e:   # 再試行
            last = e
            time.sleep(1.5 * (attempt + 1))
    raise RuntimeError(f'fetch failed: {url}: {last}')


def normalize(html):
    t = re.sub(r'<[^>]+>', ' | ', html)
    t = re.sub(r'\s+', ' ', t)
    return re.sub(r'\s*\|\s*(\|\s*)*', ' | ', t)


def num(s):
    s = s.strip()
    return int(s) if re.fullmatch(r'-?\d+', s) else float(s)


def stat(t, name):
    m = re.search(r'\| ' + name + r' \| (-?[\d.]+) \|', t)
    return num(m.group(1)) if m else None


def last_value(t, pattern):
    """Lv別効果表の行 `<名前> | <値>` のうち最後（Lv5=ランク4）の値。"""
    # 値は `6` または `6, 6`（セレスディアの「攻撃%, 移動%」。どちらも同値）。先頭の数を取る。
    vals = re.findall(r'\| (' + pattern + r') \| (-?[\d.]+)(?:, [\d.]+)? \|', t)
    return num(vals[-1][1]) if vals else None


def parse_list(html):
    cards = re.findall(r'<div class="col" data-filters="([^"]*)">(.*?)(?=<div class="col" data-filters=|</div></div><script>|$)', html, re.S)
    out = {}
    for filt, body in cards:
        m = re.search(r'class="itemname"[^>]*href="([^"]+)"[^>]*>([^<]+)<', body)
        if not m:
            continue
        mt = re.search(r'運搬(\d+)', filt)
        out[m.group(2)] = {'slug': m.group(1), 'transport': int(mt.group(1)) if mt else 0}
    return out


def v_eff_of(rec):
    """3-d の実効速度。ライド個体のみ。Fraction を float(整数なら int)にして返す。"""
    k = rec['kind']
    if k == 'none':
        return None
    if k == 'water':
        v = Fraction(rec['swim_dash']) * (1 + Fraction(rec['swim_bonus'] or 0) / 100)
    else:
        v = (Fraction(rec['ride_sprint']) * (1 + Fraction(rec['ride_bonus'] or 0) / 100)
             * (1 + 4 * Fraction(rec['pair_pct'] or 0) / 100))
    return int(v) if v.denominator == 1 else float(v)


def parse_page(html, transport):
    t = normalize(html)
    i = t.find('パートナースキル')
    j = t.find('作業適性', i)
    ps = t[i:j] if i >= 0 and j > i else ''
    if '乗って' in ps:
        kind = 'fly' if '空を飛' in ps else ('water' if '水上' in ps else 'ground')
    else:
        kind = 'none'
    rec = {
        'kind': kind,
        'run': stat(t, 'RunSpeed'),
        'ride_sprint': stat(t, 'RideSprintSpeed'),
        'swim_dash': stat(t, 'SwimDashSpeed'),
        'ride_bonus': last_value(t, r'MoveSpeed_up_PartnerSkill_Ride_\d+'),
        'swim_bonus': last_value(t, r'SwimSpeed_up_PartnerSkill_\d+'),
        'pair_pct': last_value(t, r'(?:[A-Za-z]+_)?MoveSpeed_PartnerSkill_SpecificElement'),
        'transport': transport,
    }
    rec['v_eff'] = v_eff_of(rec)
    return rec


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--refresh', action='store_true')
    ap.add_argument('--keep-cache', action='store_true')
    a = ap.parse_args()
    sp = json.load(open(SPECIES, encoding='utf-8'))
    if a.refresh and os.path.isdir(CACHE):
        for f in os.listdir(CACHE):
            os.remove(os.path.join(CACHE, f))
    lst = parse_list(get(BASE + 'Pals', os.path.join(CACHE, '_list.html')))
    out = {}
    for n, s in enumerate(sorted(sp, key=lambda s: s['species_id']), 1):
        info = lst.get(s['display_name'])
        if info is None:
            sys.exit(f"paldb 一覧に無い: {s['species_id']} {s['display_name']}")
        html = get(BASE + info['slug'], os.path.join(CACHE, info['slug'] + '.html'))
        rec = parse_page(html, info['transport'])
        if rec['run'] is None:
            sys.exit(f"RunSpeed が取れない: {s['species_id']} {info['slug']}")
        if rec['kind'] != 'none' and rec['v_eff'] is None:
            sys.exit(f"v_eff が計算できない: {s['species_id']}")
        rec['paldb'] = info['slug']
        out[s['species_id']] = rec
        if n % 40 == 0:
            print(f'  {n}/{len(sp)}', flush=True)
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    with open(OUT, 'w', encoding='utf-8') as f:
        json.dump(out, f, ensure_ascii=False, indent=1, sort_keys=True)
        f.write('\n')
    kinds = {}
    for r in out.values():
        kinds[r['kind']] = kinds.get(r['kind'], 0) + 1
    print('saved', OUT, len(out), kinds)
    if not a.keep_cache and os.path.isdir(CACHE):
        for fn in os.listdir(CACHE):
            os.remove(os.path.join(CACHE, fn))
        os.rmdir(CACHE)


if __name__ == '__main__':
    main()
