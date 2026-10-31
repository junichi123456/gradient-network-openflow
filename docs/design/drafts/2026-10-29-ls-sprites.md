# learnset インスペクタにスプライトを載せる

## 目的
Artifact「learnset インスペクタ」(188eb9b1-d49d-4c39-bf77-895a30185e99) の一覧と詳細に、全287種のスプライトを表示する。**今後 BST（種族値）を作り直す予定**なので、スプライトのデータは種族値と切り離し、`species_id` だけで引く（スプライトの再生成と payload の再生成が独立に回せること）。

## 現状
`Tools/build_ls_payload.py` が Data/ から `Tools/artifacts/ls_head.html` + JSON + `ls_tail.html` を連結して `learnset_viewer.html` を作る。一覧は `ls_tail.html` の `render()`（`.row` ボタン）、詳細は `detail(s)`（`.dh` ヘッダ）。スプライトは `Assets/Sprites/{id}/{front,back,left,right,fl,fr,bl,br}.png`（192x192 RGBA 透過、`/usr/bin/python3 Tools/gen_pal_sprites.py` で生成、規格は pvp_spec §34）。Python は `/usr/bin/python3`（PIL/numpy あり）。

## 変更内容
1. `Tools/build_ls_payload.py`: 各種の8方向PNGを読み、透過部をトリミングせずセル全体を 72x72 に縮小（LANCZOS）、可能なら 64色パレット化（透過維持）した PNG を base64 data URI にし、`SPR = {"001": [front,back,left,right,fl,fr,bl,br], ...}` を別の `<script>` 定数として出力（payload の `SP` には入れない）。スプライトが無い種は省略。ページ全体が 16MB に収まること（目標 6MB 以下。超えるなら 56px/32色に下げる）。payload 生成は従来どおり決定的。
2. `ls_tail.html`: 一覧の各行の左に 40px 角の front 画像（`image-rendering: pixelated` は不要、smooth でよい。無ければ空き枠）。詳細ヘッダに 96px の大きい画像と、8方向ボタン（前/後/左/右/左前/右前/左後/右後、押すとその向きに切替、`aria-pressed`）。ダーク/ライト両対応の背景（既存のトークンに合わせる）。スマホ幅で崩れないこと。選択種を変えると向きは front に戻る。
3. Artifact を `learnset_viewer.html` と同じ file_path・url で再公開するのは私（Opus）が行う。実装者は公開しない。
4. CLAUDE.md の「Artifact の同期」に「スプライトは `Assets/Sprites` から自動で取り込まれる。スプライトを再生成したら build_ls_payload.py を再実行」を1行追記。

## 検証方法
- `/usr/bin/python3 Tools/build_ls_payload.py` が成功しページサイズを表示、`SPR` に287件×8枚。
- Playwright で開き、一覧の画像が287件読み込めること（naturalWidth>0）、方向ボタンで `src` が切り替わること、390px幅で横スクロールが出ないこと、コンソールエラー0。スクリーンショットを1枚確認。
- 既存の verify_*.py と回帰スイートに影響しない（Data/コードは変えない）。

## やらないこと
歩行/攻撃アニメ表示（シートが重いため）、move_editor の変更、Data/ゲームコードの変更。
