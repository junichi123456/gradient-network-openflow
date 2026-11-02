# 全パルのスプライト自動生成（8方向・歩行・攻撃）

## 目的
`Data/species.json` の全287種に、同一品質で 8方向静止画・歩行4コマ・攻撃4コマを用意し、ゲームで再生する。外部API・追加費用なし（コンテナ内の Python のみ）。既存3種(001/003/009)の手作り画像も置き換える（ユーザー確定）。

## 現状
- `Assets/Sprites/{id}/{front,back,left,right,fl,fr,bl,br}.png`（960x800 RGBA、ボクセル調レンダ、静止のみ。001/003 は一部 lb/rf 等の別名）。
- 読込は `Scripts/Visuals/SpriteTextureLibrary.cs`（`GetTexture(spriteId, direction, color, size)`）。`Scripts/Entities/Entity.cs` が `Sprite2D _visual` を作り、向き変更時 (`UpdateFacingDirection`→テクスチャ差替) と `ComputeFeetOffset` で足元合わせ。移動/攻撃は同ファイル ~L460/L483。`sprite_key` は species.json に既にある（= species_id）。
- Python は `PIL`/`numpy` あり（`python3 -c "import numpy"` 可。PIL が壊れていたら `pip install pillow` ※PyPI取得は無料）。

## 変更内容
1. `Tools/gen_pal_sprites.py`（新規）: 種族データから決定的（species_id をシード）にボクセルモデルを生成し、numpy のソフトウェアレンダラ（斜め見下ろしの正射影、面ごと明暗3段、白背景ではなく**透過**）で描画。
   - 入力: types（主タイプ配色パレット、2タイプ目はアクセント）, role/offense/defense（体型: 攻撃型→角/爪/牙、耐久型→厚い殻/丸み）, base_speed（脚の長さ・細さ）, BST6（体の大きさ 3〜4段階）。
   - 体型テンプレ 5〜6種（丸型・四足・二足・竜型・浮遊・蛇/多足 など）× パーツ（頭・胴・脚/足・腕・尾・耳/角/ヒレなど）をボクセルで組み、体型・パーツ・色・目の形を乱数で選ぶ。目・顔は必ず前面に付けて向きが判別できること。
   - パーツ独立で関節回転/平行移動できる簡易リグ: 歩行=脚の前後スイング＋体の上下動4コマ、攻撃=溜め→突進（前傾＋前方へ移動）→命中→戻り の4コマ。浮遊型は脚の代わりに上下動。
   - 8方向 = モデルをY軸回りに 45°刻みで回して描画（front=手前向き、back=奥向き、left/right/斜めも同様）。方向名と向きの対応は `SpriteTextureLibrary` の `_directionNames`（Y下=front, (-1,0)=left, (1,0)=right, fl=(-1,1), fr=(1,1), bl=(-1,-1), br=(1,-1)）に厳密に合わせる。
   - 出力（セル 192x192、足元がセル下端から約12px上・水平中央に固定。体の大小は同一スケール上での大きさで表現し、セルからはみ出さない）:
     - `Assets/Sprites/{id}/{front,back,left,right,fl,fr,bl,br}.png` … 待機1コマ（各192x192）
     - `Assets/Sprites/{id}/walk.png` … 4列×8行（行順 front,back,left,right,fl,fr,bl,br）768x1536
     - `Assets/Sprites/{id}/attack.png` … 同形式
   - 既存 001/003/009 の画像は削除し（`.png.import` も）、同じ規格で再生成。
   - 生成は決定的で、同じ入力から同じ画像になること。287種で数分以内に終わらせる（遅ければ numpy でベクトル化）。
2. `SpriteTextureLibrary.cs`: 新規 `GetSheet(spriteId, "walk"|"attack")`（`res://Assets/Sprites/{id}/{name}.png`、既存の2段読込 TryLoadImageTexture を流用、キャッシュ）と、セルを切り出す `AtlasTexture` を返す `GetFrame(spriteId, action, direction, frame)`。無ければ null。方向→行番号は上の行順。
3. `Entity.cs`: 移動（既存の移動処理中）に walk の4コマを移動時間で均等再生、攻撃（~L483 の処理中）に attack の4コマ、終了後は待機画像へ戻す。シートが無い種は従来どおり静止表示（フォールバック）。画面上の見かけサイズが従来の既定（`default_visual_scale`・`VisualSize`）から大きく変わらないよう、192pxセル前提で `ComputeFeetOffset`/スケールを確認・調整する。**戦闘ロジック・ターン処理・乱数消費には一切触れない**（固定シード再現性維持）。ヘッドレスでは再生処理が落ちないこと。
4. `godot --headless --import` を試し、生成される `*.png.import` を可能ならコミット（無理なら Image.Load フォールバックで動く旨を報告）。
5. `docs/design/pvp_spec.md` に §34 として方式と規格を短く追記。CLAUDE.md に「スプライトは `Tools/gen_pal_sprites.py` で再生成」と1行追記。

## 判断が必要だった点（採った解釈）
- 品質: 手描きより簡素だが全種同一レンダラで統一（ユーザー確定）。
- 画像サイズ: 960x800 → 192セル。容量とシート化のため。
- 画像生成AIは費用発生のため不使用。

## 検証方法
- `python3 Tools/gen_pal_sprites.py` が全287種を生成し、`Assets/Sprites/*/` 各に 8+2 ファイル。サイズ・チャンネル（RGBA、四隅透過）を全件検証するスクリプト `Tools/verify_sprites.py`（全件PASS）。
- 同一種の再実行でバイト一致（決定性）。
- 代表数種（丸型/四足/竜/浮遊 各1）のコンタクトシート（8方向＋歩行＋攻撃）を目視確認し、向きが方向名と合っていること、種ごとに見分けがつくこと。
- `dotnet build` エラー0、回帰スイート186項目 NG 0 を6回連続、既存 verify_*.py が PASS のまま。
- 追加で `Scenes` を headless 起動して walk/attack 再生コードが例外を出さないこと。

## やらないこと
- 戦闘バランス・データの変更、手描き品質の追求、サウンド、被弾/状態異常モーション、エフェクト、UI アイコン。
