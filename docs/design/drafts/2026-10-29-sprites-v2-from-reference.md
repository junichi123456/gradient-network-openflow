# スプライト v2（改訂）: 複数参照画像を見て組む「パーツ式モデル」

## 目的
現行スプライト（`Tools/gen_pal_sprites.py`、箱組みの自動生成。pvp_spec §34）は全種が似通っていて不評。ユーザー指示: palworld.gg のアイコン1枚では全身が分からないので、**web上のあらゆるシーン（複数アングル・ゲーム内スクショ・wiki等）を確認**し、**全287種が見た目で区別できる品質**にする。「アイコンを押し出すだけ」では同程度の品質に留まる恐れがあるため、**種ごとに参照画像を見て、パーツ構成・色・特徴を明示したモデル定義 JSON を作り、共通レンダラで描く**方式にする。規格（ファイル名・192x192セル・8方向静止＋walk/attack 4コマ×8行・足元アンカー）は §34 のまま。ゲームコード・Data は変更しない。

## 事実（調査済み）
- `Data/species.json` の `display_name`（287件）は全件 `https://palworld.gg/ja/pals` に同名で存在。各 `<a href="/ja/pal/{URLエンコード名}">` ブロック内に `full_palicon/T_{内部名}_icon_normal.png` があり、実体は `https://palworld.gg/images/full_palicon/T_{内部名}_icon_normal.png`（128px, 取得可）。
- 到達可: palworld.gg、paldb.cc（`/ja/{英名}`、CDN `cdn.paldb.cc`）、palworld.wiki.gg（`/wiki/{英名}`）、game8.jp（`/pal-world/{id}`、`img.game8.jp`/`assets.game8.jp` に画像）、gamepedia.jp、i.ytimg.com サムネ。fandom は 403、palworld.th.gg は 502。WebSearch（標準/extended）も使える。
- 取得は curl（プロキシ設定済み）、画像の閲覧は Read ツール。Python は `/usr/bin/python3`（numpy/PIL あり）。

## 変更内容（段階1: 基盤）
1. `Tools/fetch_pal_refs.py`: 名前→アイコン名の対応表 `Tools/pal_ref_map.json`（species_id→内部名/英名）を作り、アイコンを `Tools/.refcache/{species_id}/icon.png` に保存。追加の参照画像は各種ごとに `Tools/.refcache/{species_id}/` に集める（wiki.gg / paldb / game8 / 検索で見つけた別アングル・ゲーム内シーン）。`Tools/.refcache/` は `.gitignore`（参照画像はコミットしない）。
2. モデル定義スキーマ `Data/sprite_models/{species_id}.json`（コミットする）:
   - `palette`: 名前→#RRGGBB（参照画像から読んだ実色）。
   - `parts`: 配列。各 `{name, shape: ellipsoid|box|cylinder|cone|wedge, center:[x,y,z], size:[sx,sy,sz], color, group, rot?, mirror_x?}`。単位は 1/16 タイル程度のグリッド（スキーマ仕様は実装者が決めて `docs/design/pvp_spec.md` §34 に書く）。足元が y=0、前方が +z。
   - `group`: body/head/arm_l/arm_r/leg_fl/leg_fr/leg_bl/leg_br/tail/wing_l/wing_r/ear/… アニメ用。`gait`: biped|quad|hover|slither|none。
   - `features`: 目・口・模様（顔面上の色パッチ）を小パーツとして明示。
   - `refs`: 見た参照画像のURL/ファイル名と、確認した点のメモ1〜2行。
3. `Tools/gen_pal_sprites.py` を「モデルJSON → ボクセル化 → 8方向レンダ（既存のレンダラ・陰影・足元アンカー・はみ出し防止を流用）→ walk/attack をグループ変形（脚の前後スイング・胴の上下・腕振り・翼羽ばたき・尾振り、攻撃は溜め→突進→命中→戻り）」に作り替える。モデルJSONが無い種はエラーで止める（旧ロジックへフォールバックしない）。決定的。
4. 試作3種（形の異なる 001 モコロン / 009 ブシガエル / 竜型1種）を**参照画像を実際に見て**作り、コンタクトシート（8方向＋walk＋attack）で確認。ここで段階1は完了とし、**実装者は止まって報告する**（Opus がレビューしてから段階2へ）。

## 変更内容（段階2: 全種制作）
段階1の合格後、Opus が残り284種を分割して別エージェントに配る。各担当は担当種ごとに:
① 参照画像を**最低3枚**集めて見る（アイコン＋wiki/ゲーム内の別アングルを可能な限り。背面・側面が見えるものを優先）、② モデルJSONを作る、③ 生成して自分の出力と参照を見比べ、色・形・特徴パーツ（角・耳・尾・翼・模様・持ち物）が一致するまで直す。B付き変種は元種と別個体として、色・特徴の違いを必ず反映する。

## 判断が必要だった点
- 完全自動（アイコン押し出し）は低コストだが同程度の品質に留まる→パーツ式の手作りモデルを採用（トークン消費は増える。ユーザーは品質を優先）。
- 参照画像は著作物のため、コミットは生成物とモデルJSON（数値・色・URLメモ）のみ。

## 検証方法
- 段階1: 試作3種のシートを目視し、参照と形・色が合い、8方向の向きが方向名と合うこと。`verify_sprites.py` PASS。
- 段階2完了後: 287種すべて `Data/sprite_models/` あり、`verify_sprites.py` 全PASS、再生成でバイト一致、**全ペアの front 特徴量（色ヒストグラム＋シルエット）距離の下位ペアを列挙し**近似ペアを手直し、目視は各担当が参照との照合を報告。
- `dotnet build` 0エラー、回帰スイート NG 0（6回）、既存 verify_*.py PASS、`godot --headless --import` の生成物整理、learnset ビューア再生成＋Playwright で287件読込。

## やらないこと
参照画像のコミット、ゲームコード・戦闘データの変更、被弾等の追加モーション。
