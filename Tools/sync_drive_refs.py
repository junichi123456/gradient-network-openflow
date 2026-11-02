#!/usr/bin/env python3
"""共有 Drive フォルダ「windows」(A〜G/図鑑番号/スクショ) を Tools/.refshots/{図鑑番号}/ に同期する。
埋め込みフォルダビューを curl で列挙するので API 不要。再実行すると不足分だけ取る。
  /usr/bin/python3 Tools/sync_drive_refs.py [ROOT_FOLDER_ID]
"""
import os, re, subprocess, sys
from concurrent.futures import ThreadPoolExecutor

ROOT = sys.argv[1] if len(sys.argv) > 1 else '1LN3O5GUAyzv8vhbE1py3pt7fxHi5HL0l'
OUT = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), 'Tools', '.refshots')


def sh(args, **kw):
    return subprocess.run(args, capture_output=True, text=True, **kw)


def listing(fid):
    h = sh(['curl', '-sSL', '-m', '60', f'https://drive.google.com/embeddedfolderview?id={fid}']).stdout
    ids = re.findall(r'id="entry-([^"]+)"', h)
    names = re.findall(r'flip-entry-title">([^<]+)', h)
    return list(zip(ids, names))


def fetch(job):
    fid, path = job
    if os.path.exists(path) and os.path.getsize(path) > 0:
        return 0
    os.makedirs(os.path.dirname(path), exist_ok=True)
    tmp = path + '.part'
    for _ in range(3):
        r = sh(['curl', '-sSL', '-m', '120', '-o', tmp, '-w', '%{http_code}',
                f'https://drive.usercontent.google.com/download?id={fid}&export=download&confirm=t'])
        if r.stdout.strip() == '200' and os.path.getsize(tmp) > 1000:
            os.replace(tmp, path)
            return 1
    return 0


jobs = []
for gid, g in listing(ROOT):
    if not re.fullmatch(r'[A-G]', g):
        continue
    for sid, sname in listing(gid):
        for fid, fname in listing(sid):
            jobs.append((fid, os.path.join(OUT, sname, fname)))
print('files listed:', len(jobs), 'species:', len({os.path.basename(os.path.dirname(p)) for _, p in jobs}))
with ThreadPoolExecutor(6) as ex:
    got = sum(ex.map(fetch, jobs))
print('downloaded now:', got)
