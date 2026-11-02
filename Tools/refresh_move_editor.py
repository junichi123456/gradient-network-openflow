#!/usr/bin/env python3
"""技データベース エディタ（Artifact: 975b54fe-5c46-4597-ae30-26d694ac3eb2）
に埋め込んだ moves.json のスナップショット（MOVES_SEED）を、
Tools/artifacts/move_editor.html の中でその場だけ差し替える。

moves.json（技の威力・命中・射程など）を変更したら、このスクリプトを
実行してから Tools/artifacts/move_editor.html を同じURLへ再公開する。
（species.json の learnset 差し替えだけなら、参照する技自体は変わらない
ので、こちらの再生成は不要——build_ls_payload.py だけでよい。）

  python3 Tools/refresh_move_editor.py
"""
import json
import os
import re

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PATH = os.path.join(ROOT, 'Tools', 'artifacts', 'move_editor.html')

html = open(PATH, encoding='utf-8').read()
current = json.load(open(f'{ROOT}/Data/moves.json', encoding='utf-8'))
new_seed = json.dumps(current, ensure_ascii=False, separators=(',', ':'))

pattern = re.compile(r'const MOVES_SEED =\[.*?\];')
if not pattern.search(html):
    raise SystemExit('MOVES_SEED not found in move_editor.html — テンプレートが壊れている可能性')

new_html = pattern.sub(f'const MOVES_SEED ={new_seed};', html, count=1)
open(PATH, 'w', encoding='utf-8').write(new_html)
print(f"技 {len(current)}件 を反映（{len(new_html):,} 文字）")
