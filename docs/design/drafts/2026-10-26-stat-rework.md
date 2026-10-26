# 能力配分の再設計（BST6 320〜610・攻撃×耐久の直積・俊敏三峰・突破値と気質）— 仕様書草案

- **状態: 草案（ユーザー確認前）**。末尾 §8「ユーザーに確認すべき点」の回答で確定させる。
  本文は §8 の**推奨案を採った前提**で書いてある（回答が推奨と違えば、§8 の各項目に書いた差し替え箇所を直す）。
- 作成: 2026-10-26
- 対象ブランチ: `claude/mystery-dungeon-roguelike-egnzjt`
- **前提コミット: Phase 6（制限技12種、`docs/design/drafts/2026-10-25-void-type-and-habcds.md` §3 Phase 6）の完了後のコミット。**
  この草案を書いた時点では Phase 6 が作業ツリーで実装中（`Data/moves.json` に `rs_` 技12種、`Data/species.json` の24種の learnset に `rs_` 技、
  `Tools/learnset_rules.py` に `RESTRICTION_LEARNERS` 等）。**本書の実装は Phase 6 がコミットされてから着手し、Phase 6 の内容（`rs_` 技・その習得者）には触れない。**
  本書の試算は Phase 6 の作業ツリーの `species.json`（learnset 以外は HEAD と同一であることを確認済み）で行った。
- 読み手: 会話履歴を持たない実装者。**この文書だけで実装できるように書いてある。**
  書かれていないことは勝手に決めず、報告すること（裁量を認める範囲は末尾「実装時に判断してよい範囲」に限る）。

---

## 0. 前提（この文書を読むのに必要な事実）

### 0-1. ユーザー要求（原文）

```
* 物理型/特殊型/打ち分け型 の表記をなくします
* BST6の最大値を320〜610に下方修正します
* 物理攻撃/特殊耐久や 特殊攻撃/物理耐久のパターンも作ります
* 傾向として俊敏性が95以上だと優先度+1以上の攻撃技は習得しません
* また、俊敏性が低いほど物理/特殊耐久指数(HP*BまたはHP*D、HP*(B+D)を総合耐久指数と呼ぶこととする)
* 俊敏性のピークを細分化し、50、75、100の3つを山とします
* たとえば耐久がペラペラだけど俊敏性が高く、初手に何らかの仕事を確実に遂行できるパル、足はそれほど速くないが
  物理受け気質の配分でそこそこ火力のある特殊技で返り討ちにできるパルなどの活躍を試みます。
* すべてのパルは突破値と呼ばれる能力補正が可能であり、1つのステータスに対し25まで振ることができ、全体で55ポイントを
  振り分け可能。1ポイントあたり実数値で能力値が1上がります。気質補正により、HP以外の5つのステータスのうちいずれかを
  0.9倍するかわりに1.1倍に補正することが可能。(ポケモンの性格と同様に)
* 突破値の補正は気質補正のあとに加算するものとする。
```

### 0-2. 用語

| 用語 | 意味 |
|---|---|
| H/A/B/C/D/S | HP / 攻撃 / 防御 / 魔力 / 精神 / 俊敏（HABCDS）。物理技は A 対 B、特殊技は C 対 D（現行どおり） |
| 旧HAD (h, a, d) | `species.json` の `legacy_had`（HABCDS 導入前の HP/攻撃/防御）。**配分の入力** |
| BST3 | h + a + d（180〜500、全287種が5の倍数） |
| BST6 | 6能力の合計 = 「合計種族値」。本書で式を変える（§3 S1-a） |
| 攻撃区分 (offense) | `Physical` 物理攻撃 / `Special` 特殊攻撃 / `Mixed` 両刀攻撃。**習得する攻撃技の分類の傾向**。現行の「型(profile)」（物理型/特殊型/打ち分け型）を改名したもの。決め方は変えない |
| 耐久区分 (defense) | `Physical` 物理耐久（B寄り） / `Special` 特殊耐久（D寄り） / `Both` 両面耐久（B=D）。本書で新設 |
| 役割 (role) | 攻撃区分 × 耐久区分 の 9 通り。表記は「物理攻撃/特殊耐久」のように `攻撃区分/耐久区分`。JSON の `role` は `"Physical-Special"` のように `<offense>-<defense>` |
| 物理耐久指数 | H × B |
| 特殊耐久指数 | H × D |
| 総合耐久指数 | H × (B + D)。**役割の「両面耐久」と混同しないこと**（両面耐久は B:D の配分の名前、総合耐久指数は指標の名前） |
| 突破値 (breakthrough, BT) | 構築時に振る個体値。各能力 0〜25、6能力合計 0〜55。**1ポイント = 実数値 +1**（§3 S2） |
| 気質 (nature) | HP 以外の5能力（A・B・C・D・S）のうち1つを ×1.1、別の1つを ×0.9。無補正もある |
| 実数値 | 対戦で使う値（Lv50 固定）。§3 S2-a の式 |
| round(x) | 四捨五入（x.5 は切り上げ）= floor(x + 1/2)。比率計算は Python `fractions.Fraction` で厳密に行ってから丸める（現行 `build_habcds.py` と同じ） |

### 0-3. 現行の数値（Phase 6 完了時点の想定）

- 種族 287 / 技 651（Phase 6 の `rs_` 12種込み）/ 属性10（虚を含む）。
- BST6 = round(360 + (BST3 − 180) × 270/320)、360〜630（タマコッコ 360・リリクイン 503・グランジーラ 630）。
- S は 35〜130、75 付近と 100 付近の二峰（現 `Tools/build_habcds.py` の `P_R`・`P_N`）。
- 型: 物理型124・特殊型84・打ち分け型79（`Tools/learnset_rules.py` `profile_of`）。役割5種（物理耐久76・物理攻撃48・特殊耐久53・特殊攻撃31・打ち分け79）。
- 行動順: 優先度 → **S 種族値**の高い順 → BST6 の低い順 → 乱数（`BattleScheduler.Order`）。
- 回帰スイート `[検証]`: 現在 132、**Phase 6 完了で 145**（本書の基準値）。
- Python 検証: `Tools/verify_learnset.py`・`Tools/verify_attack_moves.py`・`Tools/verify_npc_teams.py` が ALL PASS（Phase 6 完了時の PASS 数を基準 N_L・N_A・N_N と書く）。
- 直近の 10,000 試合（`pvp_spec.md` §28 の Phase 5 計測）: BST 補正後で 炎 +2.9pt・無 −3.5pt、全属性 ±5pt 以内。

### 0-4. 既存の「突破値」の正体（確認済み）

`Scripts/Entities/EntityStats.cs` に既に `BreakthroughHP/Atk/Def/SpAtk/SpDef/Speed`（int、既定0）がある。中身は**ポケモンの努力値に相当する、種族値の2倍に足す値**:

```
MaxHp   = floor((BaseMaxHp*2 + BreakthroughHP) * Level / 100) + Level + 10
Attack  = floor((BaseAtk  *2 + BreakthroughAtk) * Level / 100) + 5      // Defense/SpAttack/SpDefense/Speed も同形
```

- Lv50 では「突破値 +2 で実数値 +1」。
- **ゲーム中で 0 以外を設定している経路は無い**（`grep -rn Breakthrough Scripts` の結果、書き込みは `BattleTestScene.cs` 702–706 行の検証だけ。
  そこでは「突破値 +2 = 実数値 +1」を利用して種族値%加算の検証をしている）。迷宮側（Player/AllyEntity 等）も常に 0。
- `BattleEntry`（構築）・`npc_teams.json`・通信の直列化（`BattleNetwork.EncodeFull`）には突破値も気質も無い。

→ 本書では**同じプロパティ名のまま意味を「実数値への直接加算（気質の後）」に置き換える**（§4-7）。全員 0 の現状では、置き換え前後で全実数値が一致する。

---

## 1. 目的（達成条件）

1. **表記**: 「物理型」「特殊型」「打ち分け型」（および「器用型」）という語が、ゲーム UI・Artifact（learnset インスペクタ）・
   `Scripts/`・`Tools/` のコードとコメント・`docs/design/pvp_spec.md` から無くなっている（過去の草案ファイル `docs/design/drafts/*` は記録なので触らない）。
   代わりに「攻撃区分（物理攻撃/特殊攻撃/両刀攻撃）」「耐久区分（物理耐久/特殊耐久/両面耐久）」「役割（攻撃区分/耐久区分）」を使う。
2. **BST6**: `BST6 = round(320 + (BST3 − 180) × 290/320)`。全287種が 320〜610 に収まり、タマコッコ(003) 320・リリクイン(186) 474・グランジーラ(203) 610。
3. **役割の直積**: 攻撃区分3 × 耐久区分3 = **9役割すべてに個体が実在**する（各25種以上。§3 S1-c の内訳表）。
   「物理攻撃/特殊耐久」41種、「特殊攻撃/物理耐久」27種を含む。
4. **俊敏の三峰**: S は 35〜130、ヒストグラム（中心±2 の5刻み）が **48–52・73–77・98–102 の三峰**になる（§3 S1-b の表）。
5. **俊敏と耐久**: S が低いほど総合耐久指数が高い。全287種で Pearson 相関 r(S, H(B+D)) ≤ −0.40、BST6 を統制した偏相関 ≤ −0.80、
   S の帯（≤62 / 63–87 / ≥88）ごとの平均総合耐久指数が単調減少（期待値: −0.475 / −0.892 / 11903 > 8732 > 8103）。
6. **俊敏と優先技**: 種族値 S ≥ 95 の個体（111種）の learnset に、優先度 +1 以上の攻撃技（威力>0）が1つも無い。
7. **突破値・気質**: すべての構築個体が 突破値（各0〜25・合計≤55）と気質を持ち、
   **実数値 = floor(素の実数値 × 気質) + 突破値**（HP は気質なし）で対戦に反映される。構築検証・通信・NPC 構築・構築画面に組み込まれている。
8. **行動順**: 優先度 → **俊敏の実数値（気質・突破値込み）**の高い順 → BST6 の低い順 → 乱数（§8 Q3 推奨）。
9. 回帰スイートが全フェーズで NG 0、項目数が §7 の値。Python 検証3本が ALL PASS。Artifact 2つが同じ URL で更新。
10. 10,000 試合のランダム構築で、属性別・役割別の BST 補正後勝率差が全て ±5pt 以内（判断材料。§5-4）。

---

## 2. 現状（関係するコード）

### 2-1. データ生成（Python）

| 場所 | 内容 |
|---|---|
| `Tools/build_habcds.py` `bst6_of(bst3)` | `360 + floor(((bst3−180)×54 + 32)/64)` |
| 同 `P_R`, `P_N`, `speed_of(rec)` | ライド: `S = min(130, round(P_R(v_eff) + 運搬Lv))`、非ライド: `q = 運搬Lv + clamp((RunSpeed−250)/400, 0, 1)`, `S = round(P_N(q))` |
| 同 `role_of(profile,h,a,d)`, `PROTO`, `largest_remainder`, `mixed_alloc`, `physical_ratio`, `compute`, `self_check`, `report`, `main` | 現行5役割の配分と自己検査。`main` は species.json のキー順を保って `base_spatk/base_spdef/base_speed/role/legacy_had/speed_source` を `base_def` の後に差し込む |
| `Tools/learnset_rules.py` `profile_of(trait_id, species_index)` | `TRAIT_PHYSICAL`→Physical、`TRAIT_SPECIAL`→Special、他は `PROFILE_CYCLE[index % 10]`（物4:特3:打3）。戻り値 `(profile, 'trait'|'profile')` |
| 同 `CEIL` | キー `('single'|'dual', 'Physical'|'Special'|'Versatile')` の上限表 |
| `Tools/generate_learnset.py` | `profile_of` で major（多数側の分類）・威力天井（Versatile は −20）・`maj_share`（Versatile 0.67／他 0.8）を決めて learnset を生成。**本書でも再実行しない** |
| `Tools/verify_learnset.py` | `profile_of` を import して #5 等で型ごとの規則を検査（コメントに「打ち分け型」「器用型」） |
| `Tools/build_ls_payload.py` | payload に `pf`（型）・`pb`（'特性'/'型'）・`role` を出力 |
| `Tools/artifacts/ls_tail.html` 7–8行 | `PFJA = {Physical:'物理型', Special:'特殊型', Versatile:'打ち分け型'}`、`ROLEJA`（5役割）。`pfs` と `roles` の2つの絞り込みチップ |
| `Tools/artifacts/ls_head.html` 161–162行 | `<div class="chipset" id="pfs">` と `id="roles"` |
| `Tools/generate_npc_teams.py` 34–38行 | `TARGET_BST_MIN = 402`, `TARGET_BST_MAX = 529`（旧BST3 230/380 を旧BST6式で写した値） |
| `Data/npc_teams.json` | 8チーム。`total_bst` は BST6 の合計。**ヘッドバット(MV_140)を使う編成は無い**（確認済み） |
| `Data/source/paldb_speed.json` | 287種の `kind`(none/ground/fly/water)・`run`・`ride_sprint`・`swim_dash`・`ride_bonus`・`swim_bonus`・`pair_pct`・`transport`・`v_eff`。**本書でも再取得しない** |

### 2-2. 対戦（C#）

| 場所 | 内容 |
|---|---|
| `Scripts/Entities/EntityStats.cs` | §0-4 の実数値式。`BaseTotal`（BST6）、`FillFromSpecies` |
| `Scripts/Species/SpeciesDatabase.cs` `SpeciesJson` / `SpeciesData.cs` | `Role`（string）を読むが、**ロジックでは未使用**（`BattleTestScene` の検証だけが読む） |
| `Scripts/Battle/BattleScheduler.cs` `Order(a, b)`（206–228行付近）, `Bst(e)`, `EffectivePriority` | 優先度 → `Stats.BaseSpeed` 降順 → `Bst` 昇順 → `GD.Randf()` |
| `Scripts/Battle/BattleTeam.cs` `BattleEntry`（SpeciesId・MoveIds・ItemId）, `BattleTeam.SetSpecies/SetItem/Validate` | 構築。検証は 6匹・種族重複・持ち物重複・伝説1体・技 learnset 内4つまで |
| `Scripts/Battle/BattlePal.cs` `_Ready`, `ApplyLoadout(entry)` | `Stats.Level = 50` の後に技と持ち物を載せる |
| `Scripts/Battle/BattleNetwork.cs` `EncodeFull/DecodeFull`（195–230行） | `{"s","m","i"}` の JSON。`EncodePublic` は種族のみ |
| `Scripts/Battle/BattleNetProtocol.cs` `PublicEntryView` | 相手に開示するのは種族のみ |
| `Scripts/Battle/NpcTeamDatabase.cs` `NpcEntryJson` | `species_id/move_ids/item_id` |
| `Scripts/Battle/NpcOpponent.cs` 24–25行 `Bst6Weight = 32f/27f`、`SelectionScore` | 選出採点 `bst×Bst6Weight + power×2 + jitter` |
| `Scripts/Battle/BattleArena.cs` `ApplyEntryWeather` | 陣営の BST6 合計の高い側から天候発動（低い側が最後に上書きして勝つ） |
| `Scripts/Battle/MetaScenario.cs` `StrongPool`（BST6 降順）, `StrongestTeam`（91行 `new BattleEntry`） | シード `20260822UL` の Fisher-Yates |
| `Scripts/Battle/TacticalBuilder.cs` `BuildProfile`（`Hp=H, Atk=max(A,C), Def=(B+D)/2, Bst`）、278行 `new BattleEntry` | 戦術構築 |
| `Scripts/Battle/BattleMatchScene.cs` `RandomTeam`（1059行）, `ForceCarrier`（366行）, `TeamOf`（1121行）, `StripItems`（1131行） | ランダム構築など。`StripItems` は BattleEntry を詰め直す |
| `Scripts/Battle/BattleFlowScene.cs` `BuildTeam`（106行） | フロー確認用の既定編成 |
| `Scripts/UI/Battle/TeamBuildScreen.cs` `SpeciesPanel`（種族値7チップ）, `MovePanel`, `ItemPanel`, `BuildDetail` | 構築画面 |
| `Scripts/UI/Battle/SpeciesPickScreen.cs` 15行コメント「炎の物理型で…」 | 表記の残り |
| `Scripts/UI/Battle/BattleHud.cs` `RefreshRail`（262行〜） | 行動順の目安レール。**現状 `OrderBy(BattleScheduler.Bst)`（BST 昇順）で並べており、Phase 4 以降の行動順（S 優先）とずれている** |
| `Scripts/Turn/AttackAction.cs` | ダメージは `Stats.Attack/Defense/SpAttack/SpDefense`（実数値）を使う。種族値%の平坦加算（きずな・まもり・リーダー系）は `Stats.BaseAtk/BaseSpAtk/BaseDef/BaseSpDef`（種族値）を使う |

### 2-3. 回帰スイート（`Scripts/Battle/BattleTestScene.cs`）で本書が触る項目

- `VerifyEntryWeather`（454行付近）: マグマンダー(105)・ユキツネ(029B) の BST6 コメント。
- `VerifyTurnOrder`（600–628行付近）: タマコッコ(003)/ブシガエル(009) で `lowBst == 360 && highBst == 432` を見ている。
- `VerifyHabcdsWiring`（631–720行付近）: #2 ベンチマーク（モコロン同士 物理13・特殊3）、#3 S 順（コメント S74/S47）、#4 BST6（360/432）、
  #5 種族値%加算（`Breakthrough*2` で実数値を合わせる）。
- `VerifyHabcdsData`（803–856行付近）: #1 BST6 範囲 360〜630、#2 打ち分け79種の配分、#3 5役割の整合と内訳、#4 S 35〜130 と基準点。

---

## 3. 変更内容

フェーズ S1〜S5 の順に実装する（§7 に完了条件）。ファイル名は repo 相対。

### Phase S1 — データ（BST6・俊敏・役割・配分・優先技の制約・表記の改名）

#### S1-a. BST6 の式（`Tools/build_habcds.py` `bst6_of`）

```
BST6 = round(320 + (BST3 − 180) × 290/320)          # 傾き 29/32 = 0.90625
     = 320 + floor(((BST3 − 180) × 58 + 32) / 64)   # 整数だけで書いた同値式
```

- 端点: BST3 180 → **320**、500 → **610**（ユーザー指定の範囲の両端）。範囲外が将来現れてもクランプしない（現行方針どおり）。
- 主な値: 180→320, 200→338, 210→347, 215→352, 230→365, 265→397, 300→429, 310→438, 315→442, **350→474（リリクイン）**,
  370→492, 375→497, 395→515, 400→519, 450→565, **500→610（グランジーラ）**。
- 287種の平均 415.3（旧 448.7）。BST3 が5違うと BST6 は 4.53 違うので、**BST3 の異なる2種が同値になることは無く、BST による並び順はすべての箇所で現行と同じ**（狭義単調増加）。
- .5 ちょうどになるのは BST3 − 180 ≡ 16 (mod 32) かつ5の倍数のとき（例 BST3=260 → 392.5 → 393）。

#### S1-b. 俊敏 S（`Tools/build_habcds.py` `P_N`。`P_R`・`speed_of` の式・`S_MIN=35`・`S_MAX=130` は変えない）

**ライド（115種）は変えない**: `P_R = [(375,90), (1000,98), (1400,102), (2280,104), (2500,130)]`。ライドの峰が 100 付近（98–102 に68種）にある。
**非ライド（172種）の写像 `P_N` を次に置き換える**（点を結ぶ区分線形、端点の外は一定）:

| q（運搬Lv + 走行係数 f） | 0 | 1/8 | 3/8 | 7/8 | 1 | 9/8 | 3/2 | 5/2 | 3 | 8 |
|---|---|---|---|---|---|---|---|---|---|---|
| P_N | 35 | 47 | 51 | 56 | 60 | 73 | 75 | 77 | 80 | 94 |

```python
P_N = [(F(0), F(35)), (F(1, 8), F(47)), (F(3, 8), F(51)), (F(7, 8), F(56)), (F(1), F(60)),
       (F(9, 8), F(73)), (F(3, 2), F(75)), (F(5, 2), F(77)), (F(3), F(80)), (F(8), F(94))]
```

- 意図: **運搬0（61種）→ 50 の峰**（RunSpeed 300 → 47、400 → 51、600 → 56、650以上 → 60）、
  **運搬1〜2（88種）→ 75 の峰**（運搬1・RunSpeed 300 → 73、400 → 74、運搬2・RunSpeed 400 → 77）、
  運搬3以上（23種）→ 80〜93、ライド → 100 の峰。
- 結果（全287種）: 最小35（035 のみ）・最大130（130B, 171, 171B, 192, 196, 201, 202, 203 の8種）・平均 82.0・中央値 79。
  群ごと: 非ライド運搬0 35〜60（平均52.4）、運搬1〜2 60〜80（74.7）、運搬3+ 80〜93（84.5）、ライド 90〜130（102.8）。
  **非ライドで S ≥ 95 の個体は無い**（非ライドの最大は 93）。
- 基準点は不変: ベイントール(199)=104、ライトロット(130B)・ゼノドラン(196)・ネプティオス(201)=130。
- 現行から S が変わるのは非ライドの 160 種（ライドは0種）。

**S のヒストグラム**（中心±2。`#`=非ライド1種、`=`=ライド1種。`build_habcds.py` の `report` が出す表と一致すること）:

```
 33- 37   1 (非ライド   1 ライド   0) #
 38- 42   0 (非ライド   0 ライド   0)
 43- 47  11 (非ライド  11 ライド   0) ###########
 48- 52  23 (非ライド  23 ライド   0) #######################
 53- 57  16 (非ライド  16 ライド   0) ################
 58- 62  17 (非ライド  17 ライド   0) #################
 63- 67   0 (非ライド   0 ライド   0)
 68- 72   0 (非ライド   0 ライド   0)
 73- 77  62 (非ライド  62 ライド   0) ##############################################################
 78- 82  27 (非ライド  27 ライド   0) ###########################
 83- 87  13 (非ライド  13 ライド   0) #############
 88- 92   4 (非ライド   0 ライド   4) ====
 93- 97  10 (非ライド   2 ライド   8) ##========
 98-102  68 (非ライド   0 ライド  68) ====================================================================
103-107  25 (非ライド   0 ライド  25) =========================
108-112   0
113-117   0
118-122   0
123-127   2 (非ライド   0 ライド   2) ==
128-132   8 (非ライド   0 ライド   8) ========
```

**三峰の判定**（自己検査と回帰スイートで同じ規則を使う）: 階級（中心 35,40,…,130、±2）の度数を c(中心) として、
(i) c(50) = max{c(35..65)}、c(75) = max{c(70..90)}、c(100) = max{c(95..130)}、
(ii) 50 と 75 の間（中心 55〜70）に c ≤ min(c(50), c(75))/2 の階級があり、75 と 100 の間（中心 80〜95）にも同様の階級がある。
期待値: c(50)=23, c(75)=62, c(100)=68、谷は c(65)=c(70)=0、c(90)=4。

#### S1-c. 役割（攻撃区分 × 耐久区分）

1. **攻撃区分** = 改名後の `offense_of(trait, index)`（S1-f）。値は `Physical` / `Special` / `Mixed`。決め方は現行 `profile_of` と同一
   （`TRAIT_PHYSICAL` → Physical、`TRAIT_SPECIAL` → Special、他は species_id 昇順の序数で 物4:特3:両3 の周期）。
   **learnset は既にこの区分で生成済み**（物理攻撃は物理技中心、特殊攻撃は特殊技中心、両刀攻撃は両方）なので、learnset との整合は自動的に保たれる。内訳 物理124・特殊84・両刀79（現行と同じ）。
2. **耐久区分** = 次の順で決める（`Tools/learnset_rules.py` に `defense_of(trait_id, species_index)` を新設）:
   - 特性 `hard_armor`（物理技を受けるとき防御2倍）→ `Physical`。特性 `hard_block`（特殊技を受けるとき精神2倍）→ `Special`（各6種）。
   - それ以外は `DEFENSE_CYCLE = ['Physical', 'Special', 'Both']` を `species_index % 3` で引く（index は攻撃区分と同じ species_id 昇順の序数）。
     周期3と攻撃区分の周期10は互いに素なので、9通りが偏りなく出る。
3. **役割** = `f"{offense}-{defense}"`。表示は `攻撃区分/耐久区分`（例「物理攻撃/特殊耐久」）。

内訳（期待値。略号は付録A と同じ: P=物理攻撃 S=特殊攻撃 M=両刀攻撃、B=物理耐久 D=特殊耐久 E=両面耐久）:

| | 物理耐久 B | 特殊耐久 D | 両面耐久 E | 計 |
|---|---|---|---|---|
| 物理攻撃 P | 46 | **41**（物理攻撃/特殊耐久） | 37 | 124 |
| 特殊攻撃 S | **27**（特殊攻撃/物理耐久） | 30 | 27 | 84 |
| 両刀攻撃 M | 25 | 28 | 26 | 79 |
| 計 | 98 | 99 | 90 | 287 |

#### S1-d. 配分（`Tools/build_habcds.py` `compute` を置き換え）

1種ごとに、すべて `Fraction` で計算する。

```
入力: 旧HAD h, a, d（legacy_had）、BST3 = h+a+d、offense、defense、learnset
1. BST6 = S1-a の式
2. S    = S1-b の式（speed_of）
3. T    = BST6 − S                                   # H・A・B・C・D に配る予算
4. 耐久の取り分 w:
     W(S) = 76/100 − (30/100) × (S − 35) / 95         # S=35 → 0.76、S=130 → 0.46（S は常に 35〜130）
     φ    = (h + d) / BST3                            # 旧HADの耐久傾向（平均 ≒ 2/3）
     w    = clamp(W(S) + (φ − 2/3), 45/100, 80/100)
5. Bulk = round(T × w)、Off = T − Bulk                # Bulk = H+B+D、Off = A+C
6. H = round(Bulk × h / (h + 2d))、G = Bulk − H        # G = B+D。H:(B+D) は旧 h:2d の比
7. B = round(G × β)、D = G − B
     β = 13/20（物理耐久）, 7/20（特殊耐久）, 1/2（両面耐久）    # B:D = 65:35 / 35:65 / 50:50
8. A = round(Off × α)、C = Off − A
     α = 7/10（物理攻撃）, 3/10（特殊攻撃）, r（両刀攻撃）       # A:C = 70:30 / 30:70 / r:1−r
     r = clamp(learnset の Physical 技数 / (Physical + Special 技数), 2/5, 3/5)
         （Status は数えない。id が rs_ で始まる技は数えない。分母0なら 1/2。現行 physical_ratio と同一）
     両刀攻撃だけ A を [ceil(2/5 × Off), floor(3/5 × Off)] にクランプしてから C = Off − A
9. 出力 H, A, B, C, D, S（合計は BST6 に厳密に一致する）
```

- **「S が低いほど耐久指数が高い」の組み込み方**: S を先に決めて残り T を配る現行方式はそのまま。そのうえで耐久の取り分 w を S の減少関数にした。
  S が低い個体は (a) T が大きく、かつ (b) T のうち耐久に回る割合も大きい、の二重で耐久が厚くなる。
  S が高い個体は w が 0.46 前後まで下がり、**耐久がペラペラ**になり得る（例: 172 ダイナモフ H46/B50/D49・S101、096B チルバード H53/B44/D43・S123）。
- 旧HADの h:d の比は H:(B+D) に、旧HADの耐久傾向 φ は w の補正に残る（元の Palworld の個性を消さないため）。旧草案の「主側の比 h:a:d を保つ一様縮尺」は使わない。
- 現行の最大剰余法（`largest_remainder`・`TIE_ORDER`）と `PROTO`・`role_of`・`mixed_alloc` は削除する（2段の四捨五入で合計が厳密に合うので不要）。
- **両刀攻撃の「H×0.8 = B+D」規則は廃止**（§8 Q2 推奨。維持する場合は手順6の h/(h+2d) を両刀攻撃だけ 5/9 にする）。
  A:C ∈ [40:60, 60:40] は維持（手順8のクランプ）。

**結果の要約**（全287種）: H 41〜104（平均68.1）、A 18〜148（68.3）、B 29〜116（66.5）、C 21〜150（63.6）、D 31〜134（66.8）、S 35〜130（82.0）。
全能力の最小は 18（040 ミルカルビの A）。w が下限/上限に張り付くのは 6 種。
B/(B+D): 物理耐久 0.646〜0.655、特殊耐久 0.346〜0.354、両面耐久 0.500〜0.506。A/(A+C): 物理攻撃 0.696〜0.706、特殊攻撃 0.295〜0.307、両刀攻撃 0.400〜0.600。

役割別の平均 H/A/B/C/D/S:

| 役割 | 種 | H | A | B | C | D | S |
|---|---|---|---|---|---|---|---|
| 物理攻撃/物理耐久 | 46 | 67.6 | 91.8 | 85.1 | 39.3 | 45.8 | 82.0 |
| 物理攻撃/特殊耐久 | 41 | 67.6 | 96.1 | 46.3 | 41.2 | 86.1 | 83.8 |
| 物理攻撃/両面耐久 | 37 | 68.1 | 89.1 | 66.2 | 38.1 | 65.7 | 79.6 |
| 特殊攻撃/物理耐久 | 27 | 66.6 | 38.9 | 86.2 | 90.6 | 46.4 | 78.9 |
| 特殊攻撃/特殊耐久 | 30 | 74.2 | 38.6 | 49.6 | 89.9 | 92.2 | 81.5 |
| 特殊攻撃/両面耐久 | 27 | 64.8 | 41.7 | 65.7 | 97.5 | 65.2 | 90.5 |
| 両刀攻撃/物理耐久 | 25 | 71.3 | 63.1 | 86.7 | 75.7 | 46.6 | 84.2 |
| 両刀攻撃/特殊耐久 | 28 | 64.5 | 60.2 | 46.2 | 70.9 | 85.8 | 81.6 |
| 両刀攻撃/両面耐久 | 26 | 68.8 | 59.1 | 68.0 | 65.1 | 67.5 | 76.0 |

**俊敏と総合耐久指数**（期待値）: r(S, H(B+D)) = **−0.475**、BST6 統制の偏相関 = **−0.892**、r(S, H(B+D)/BST6²) = −0.893、
r(S, H·B) = −0.387、r(S, H·D) = −0.341。総合耐久指数 最小 4462・最大 19208・平均 9227。

| S の帯 | 種 | 総合耐久指数 平均 | 中央値 |
|---|---|---|---|
| 35〜62 | 68 | 11903 | 10792 |
| 63〜87 | 102 | 8732 | 8256 |
| 88〜130 | 117 | 8103 | 8107 |

（生の相関が偏相関より弱いのは、ライドの個体ほど BST3 が大きい＝速い個体ほど予算そのものが大きいため。）

**計算例**（H/A/B/C/D/S。全287種は付録A）:

| id | 名前 | BST3 | BST6 | 役割 | HABCDS | 総合耐久指数 |
|---|---|---|---|---|---|---|
| 001 | モコロン | 210 | 347 | 物理攻撃/物理耐久 | 58/69/75/30/41/74 | 6728 |
| 003 | タマコッコ | 180 | 320 | 物理攻撃/両面耐久 | 64/55/64/23/64/50 | 8192 |
| 005 | カモノスケ | 200 | 338 | 特殊攻撃/特殊耐久 | 51/34/35/79/66/73 | 5151 |
| 008 | ミルフィー | 215 | 352 | 両刀攻撃/物理耐久 | 71/33/99/49/53/47 | 10792 |
| 009 | ブシガエル | 265 | 397 | 物理攻撃/特殊耐久 | 62/92/46/39/85/73 | 8122 |
| 029B | ユキツネ | 215 | 352 | 両刀攻撃/両面耐久 | 64/59/69/40/69/51 | 8832 |
| 034 | ベリゴート | 260 | 393 | 特殊攻撃/物理耐久（hard_armor） | 95/23/111/54/59/51 | 16150 |
| 105 | マグマンダー | 350 | 474 | 特殊攻撃/両面耐久 | 66/52/70/121/69/96 | 9174 |
| 130B | ライトロット | 315 | 442 | 特殊攻撃/両面耐久 | 51/50/47/118/46/130 | 4743 |
| 172 | ダイナモフ | 240 | 374 | 物理攻撃/両面耐久 | 46/90/50/38/49/101 | 4554 |
| 186 | リリクイン | 350 | 474 | 特殊攻撃/特殊耐久 | 88/40/70/94/130/52 | 17600 |
| 196 | ゼノドラン | 370 | 492 | 両刀攻撃/両面耐久 | 60/115/55/77/55/130 | 6600 |
| 199 | ベイントール | 395 | 515 | 物理攻撃/両面耐久 | 73/141/68/61/68/104 | 9928 |
| 201 | ネプティオス | 375 | 497 | 特殊攻撃/両面耐久 | 49/61/58/141/58/130 | 5684 |
| 203 | グランジーラ | 500 | 610 | 特殊攻撃/特殊耐久 | 95/64/60/150/111/130 | 16245 |

ユーザー例との対応: 「耐久ペラペラ・高速」= 130B ライトロット、172 ダイナモフ、096 ライバード（55/68/29/100/54/123）など。
「足は速くないが物理受け気質で特殊技を返す」= 034 ベリゴート（S51、B111、C54）、125 フワフラワ（86/31/99/73/53/51）、176 ラピエール（72/43/94/100/51/78）など。

#### S1-e. 俊敏 95 以上は優先度 +1 以上の攻撃技を覚えない

- **規則（硬い規則として実装）**: 種族値 `base_speed ≥ 95` の個体の learnset に、`priority ≥ 1` かつ `power > 0` の技を置かない。
  変化技（power 0）は対象外。突破値・気質で実数値が 95 を超えても learnset は変わらない（learnset は種族のもの）。
- `Tools/learnset_rules.py` に定数と判定を置く: `FAST_SPEED_MIN = 95`、`def fast_blocks(species, move): return species['base_speed'] >= FAST_SPEED_MIN and move.get('priority', 0) >= 1 and move['power'] > 0`。
- 現行データで優先度 +1 以上の攻撃技は **MV_140 ヘッドバット（無・物理・威力40・命中100・単体・接触・優先度1）の1つだけ**。習得者21種のうち、新しい S で 95 以上になるのは次の6種（全てライド。S は現行と同じ値）:

| id | 名前 | S | 置き換え先（同じ level のまま） | 理由 |
|---|---|---|---|---|
| 080B | ライガルダ | 104 | MV_139 テイルスイープ（無・物理・35・単体・接触） | MV_141 は既習 |
| 110 | トコナッツ | 98 | MV_139 テイルスイープ | 同上 |
| 127 | ヤクモマル | 99 | **MV_141 スピンタックル**（無・物理・40・単体・接触） | 威力差0で最も近い |
| 184 | シェルガドラ | 99 | MV_139 テイルスイープ | MV_141 は既習 |
| 200 | グレイシャル | 103 | MV_139 テイルスイープ | 同上 |
| 200B | グレイシャドウ | 103 | MV_139 テイルスイープ | 同上 |

- 置き換え規則（新規 `Tools/apply_fast_priority_rule.py`。何度実行しても同じ結果）:
  対象 learnset 行の技 X に対し、候補 = 「X と同じ type・category・range・is_contact、`priority ≤ 0`、`power > 0`、id が `rs_` で始まらず、
  `SIGNATURE` に無く、その個体の learnset に未登録」の技。`(|power − X.power|, power, id)` 昇順の先頭へ、同じ level のまま置き換える。
  候補が無ければ置き換えずに非0終了して報告。
- category が変わらないので、両刀攻撃の r（S1-d 手順8）は置き換え前後で同じ。**`build_habcds.py` → `apply_fast_priority_rule.py` の順に実行**し、
  その後 `build_habcds.py` をもう一度実行しても値が変わらないこと（冪等）を確認する。
- `Tools/generate_learnset.py`（再実行しない）にも同じ規則を入れる: 攻撃技の候補プールを作る所で `fast_blocks(s, m)` の技を除く（将来の再生成で規則が崩れないように）。
- `Tools/verify_learnset.py` に検査を1つ追加: 「S≥95 の個体は優先度+1以上の攻撃技を持たない」（`fast_blocks` を import して判定、違反0件で PASS）。

#### S1-f. 表記の改名（コードとツール）

- `Tools/learnset_rules.py`:
  - `profile_of` → **`offense_of(trait_id, species_index)`**。戻り値 `('Physical'|'Special'|'Mixed', 'trait'|'cycle')`（`'Versatile'` → `'Mixed'`、`'profile'` → `'cycle'`）。
  - `PROFILE_CYCLE` → `OFFENSE_CYCLE`（中身は `'Versatile'` を `'Mixed'` にするだけ。並び・周期は同じ）。
  - `CEIL` のキーの `'Versatile'` → `'Mixed'`（値は変えない）。
  - 新設: `DEFENSE_CYCLE`、`defense_of`、`FAST_SPEED_MIN`、`fast_blocks`、ラベル表 `OFFENSE_JA = {'Physical':'物理攻撃','Special':'特殊攻撃','Mixed':'両刀攻撃'}`、
    `DEFENSE_JA = {'Physical':'物理耐久','Special':'特殊耐久','Both':'両面耐久'}`。
  - コメントの「物理型/特殊型/打ち分け型」を「攻撃区分（物理攻撃/特殊攻撃/両刀攻撃）」に。
- `Tools/generate_learnset.py`・`Tools/verify_learnset.py`・`Tools/build_ls_payload.py`・`Tools/build_habcds.py`・`Tools/add_void_type.py`（コメントのみ）:
  import と比較文字列を `offense_of` / `'Mixed'` に。コメントの「打ち分け型」「器用型」「物理型」「特殊型」を「両刀攻撃」「物理攻撃」「特殊攻撃」に。
  **ロジックは変えない**（`generate_learnset.py` は実行しないので `python3 -m py_compile` で構文だけ確かめる）。
- `Scripts/UI/Battle/SpeciesPickScreen.cs` 15行のコメント「炎の物理型」→「炎の物理攻撃」。
- `Scripts/Battle/BattleTestScene.cs` のコメント（828行付近「物理/特殊型208種」）→ 新しい検証に置き換わる（S1-h）。
- `docs/design/pvp_spec.md` 96行「特殊型の主力」→「特殊攻撃の個体の主力」。他に `grep -rn "物理型\|特殊型\|打ち分け型\|器用型" Scripts Tools docs/design/pvp_spec.md`
  で出る箇所を全て直す（`Tools/artifacts/*.html` は S4 で直す。`docs/design/drafts/` は対象外）。

#### S1-g. `species.json` のキー（`Tools/build_habcds.py` `main`）

`base_def` の直後の並びを次にする（既存キーの順は保つ）:
`base_spatk, base_spdef, base_speed, offense, defense, role, legacy_had, speed_source`

| キー | 値 |
|---|---|
| `offense` | `"Physical"` / `"Special"` / `"Mixed"` |
| `defense` | `"Physical"` / `"Special"` / `"Both"` |
| `role` | `"<offense>-<defense>"`（例 `"Physical-Special"`）。**旧値（`PhysicalWall` 等）は残さない** |
| `legacy_had`, `speed_source` | 現行どおり（`legacy_had` を入力に使う＝冪等） |

`build_habcds.py` の自己検査（違反があれば非0終了）を次に置き換える:
6能力の合計 = BST6 式 / 全能力 ≥ 10 / S ∈ [35,130] で最小35・最大130 が実在 / 基準点 199=104・130B=130・196=130・201=130 /
BST6 の基準 003=320・186=474・203=610 / 9役割すべて実在（各20種以上）/ `role == offense + '-' + defense` /
B = round(G×β)・A = round(Off×α)（両刀はクランプ後）を再計算して一致 / 両刀攻撃の A/(A+C) ∈ [0.4, 0.6] /
三峰の判定（S1-b）/ r(S, H(B+D)) ≤ −0.40 かつ偏相関 ≤ −0.80 / S ≥ 95 の個体に優先度+1以上の攻撃技が無い
（**この項目だけは `apply_fast_priority_rule.py` 実行前の初回は違反するので、警告として表示し終了コードには含めない**。
`apply_fast_priority_rule.py` の側で同じ検査を最後に行い、違反があれば非0終了する）。
`report` は S1-b のヒストグラム・S1-c の内訳表・S1-d の役割別平均と相関・帯別平均を出す。

#### S1-h. その他のデータ・C#（Phase S1 の範囲）

- `Scripts/Species/SpeciesDatabase.cs` `SpeciesJson`: `offense`・`defense` を追加（string、既定 ""）。`SpeciesData` に `Offense`・`Defense`（string）と
  `public string RoleLabel`（`OFFENSE_JA/DEFENSE_JA` と同じ和名で「物理攻撃/特殊耐久」を返す。未設定なら ""）。`Role` は残す。
- `Scripts/Battle/NpcOpponent.cs` 24–25行: `Bst6Weight = 32f / 29f`、コメントの式を新しい式に。
- `Tools/generate_npc_teams.py`: `TARGET_BST_MIN = 365`・`TARGET_BST_MAX = 501`（旧BST3 230/380 を新式で写した値）とコメントの式。
  **編成は再生成しない**。`--refresh-bst` オプションを足し、`Data/npc_teams.json` の各チームの `total_bst` だけを新しい BST6 の合計で書き換える（編成・並び・他のキーは変えない）。
  `Tools/verify_npc_teams.py` が ALL PASS であること。
- `Scripts/Battle/BattleTestScene.cs`（項目の書き換えと追加。S1 完了で **147**）:
  - `VerifyHabcdsData` #1: 「全て10以上・合計 **320〜610**（003=320/186=474/203=610）」。
  - `VerifyHabcdsData` #2（打ち分け79種の検査を置き換え）: 「配分比率が区分どおり」— 全287種で |B/(B+D) − β| ≤ 0.5/(B+D)、
    物理攻撃・特殊攻撃は |A/(A+C) − α| ≤ 0.5/(A+C)、両刀攻撃は A/(A+C) ∈ [0.40, 0.60]（β・α は S1-d）。
  - `VerifyHabcdsData` #3（5役割の検査を置き換え）: 「9役割が全て実在し内訳が表どおり」— S1-c の9つの数、かつ全種で `Role == Offense + "-" + Defense`。
  - `VerifyHabcdsData` #4: 「俊敏35〜130・基準点・三峰」— 最小35・最大130、基準点4種、S1-b の三峰の判定（c(50)=23, c(75)=62, c(100)=68）。
  - **追加** `[検証] 俊敏95以上は優先度+1以上の攻撃技を習得しない`: S≥95 の111種で該当技0件（件数と 127 ヤクモマルが MV_141 を持つことを括弧内に）。
  - **追加** `[検証] 俊敏が低いほど総合耐久指数が高い`: r(S, H(B+D)) ≤ −0.40 かつ帯別平均（≤62 / 63–87 / ≥88）が単調減少（実測値を括弧内に）。
  - `VerifyTurnOrder`: `lowBst == 320 && highBst == 397`。コメント「S47・BST6 360」→「S50・BST6 320」、「S74・BST6 432」→「S73・BST6 397」。
  - `VerifyEntryWeather`: コメント「BST6 503 / 390」→「474 / 352」（期待値は変わらない: マグマンダー側の BST6 合計が大きいので、ゆきが残る）。
  - `VerifyHabcdsWiring` #2: モコロン(58/69/75/30/41/74)同士で **物理12・特殊5**（攻74 防80 → 12、魔35 精46 → 5）。直接ベンチマーク 13 は不変。
    #3 のコメント（S73 と S50）、#4 の期待値 **320 / 397**。
  - 他の項目（持ち物倍率等）は能力値が変わるので括弧内の数値は変わってよい。**OK/NG が変わったら報告**。

### Phase S2 — 突破値と気質（計算・構築データ・検証・通信・行動順）

#### S2-a. 実数値の式（`Scripts/Entities/EntityStats.cs`）

```
素の値(X)   = BaseX * 2 * Level / 100 + 5        （整数の除算。非負なので floor と同じ）
気質後(X)   = 上昇なら 素 * 11 / 10、下降なら 素 * 9 / 10、どちらでもなければ 素   （整数の除算 = 切り捨て）
実数値(X)   = 気質後(X) + BreakthroughX            （X = Atk, Def, SpAtk, SpDef, Speed）
MaxHp       = BaseMaxHp * 2 * Level / 100 + Level + 10 + BreakthroughHP     （HP に気質は掛からない）
```

- **浮動小数を使わない**（`0.9f` は 0.89999998 なので `Mathf.FloorToInt(50 * 0.9f)` が 44 になる）。必ず整数の `* 11 / 10`・`* 9 / 10`。
- Lv50 では 素 = 種族値 + 5、HP = 種族値 + 60。
- 突破値・気質が全て 0・無補正のとき、現行の式と**全レベルで同じ値**になる（現行 `floor((Base*2+0)*Lv/100)+5`）。迷宮側は常に 0・無補正なので迷宮の挙動は変わらない。
- 追加するもの:
  - `public enum NatureStat { None, Atk, Def, SpAtk, SpDef, Speed }`（HP は含めない。新規 `Scripts/Entities/StatSpread.cs` に置く）。
  - `EntityStats.NatureUp` / `NatureDown`（`NatureStat`、既定 None）。`NatureUp == NatureDown` は無補正として扱う（検証で弾くので通常は起きない）。
  - `public void ApplySpread(StatSpread s)`: 6つの突破値と気質を写す。**`BreakthroughHP` はプロパティ経由で代入**（既存の `ApplyMaxHpAffectingChange` が CurrentHp を同じ差分だけ動かすので、満タンのまま）。
- 既存の `BreakthroughXxx` は**名前を変えず意味だけ変える**（§4-7）。クラス冒頭と各プロパティのコメントを「構築で振る実数値への加算（気質の後）。各0〜25・合計55以下は構築側で検証」に書き換える。
- `SpeciesData.cs` 24行付近・9行付近のコメント（「IV-like Breakthrough」）も同じ意味に直す。
- 種族値%の平坦加算（`AttackAction` のきずな・まもり・リーダー系）は**種族値（`BaseAtk` 等）のまま**。突破値・気質は見ない（§4-8）。
- `BaseTotal`（BST6）・`BattleScheduler.Bst` は**種族値の合計のまま**（個体差を含めない）。

#### S2-b. `StatSpread`（新規 `Scripts/Entities/StatSpread.cs`）

```csharp
public sealed class StatSpread
{
    public const int PerStatMax = 25;
    public const int TotalMax = 55;
    public int Hp, Atk, Def, SpAtk, SpDef, Speed;           // 突破値
    public NatureStat NatureUp = NatureStat.None, NatureDown = NatureStat.None;
    public int Total => Hp + Atk + Def + SpAtk + SpDef + Speed;
    public StatSpread Clone();
    public static StatSpread Zero() => new();
    public List<string> Validate();     // 下記
    public static StatSpread Auto(SpeciesData sp);   // S2-d
    public string NatureLabel { get; }  // "攻撃↑ 魔力↓" / "無補正"
}
```

`Validate()` の規則（違反ごとに1行の日本語メッセージ）:
- 各突破値が 0 以上 25 以下。
- 合計が 55 以下（**55 未満も可**。余りを残してよい。§4-9）。
- 気質: (None, None) か、(X, Y) で X ≠ Y かつ両方 None でない。片方だけ None、または X == Y は不可。

#### S2-c. 構築データ（`Scripts/Battle/BattleTeam.cs` ほか）

- `BattleEntry` に `public StatSpread Spread { get; set; } = StatSpread.Zero();`。
- `BattleTeam.Validate()`: 各個体について `e.Spread?.Validate()` の各行を `"{who}: "` 付きで errors に追加（`Spread == null` は「突破値・気質が未設定」エラー）。
- `BattleTeam.SetSpecies(slot, id)`: 種族を差し替えたら `Spread = StatSpread.Auto(新種族)`（§8 Q4 推奨。Q4 が (a) なら `Zero()`）。
- 新規 `public bool SetSpread(int slot, StatSpread s)`: 範囲外の slot は false。値の検証は `Validate()` に任せ、ここでは代入だけ（画面が途中の状態を持てるように）。
- `BattlePal.ApplyLoadout(entry)`: 技・持ち物の後に `Stats.ApplySpread(entry.Spread ?? StatSpread.Zero())`。`_Ready` で `Stats.Level = 50` の後に呼ばれる順は変えない。
- **既定値（§8 Q4 推奨 = (b) 自動配分）を入れる箇所**（`new BattleEntry` している全箇所。`grep -rn "new BattleEntry" Scripts` で漏れを確認）:
  `BattleMatchScene.RandomTeam`（1085行）・`ForceCarrier`（373行）・`TeamOf`（1121行）・`MetaScenario.StrongestTeam`（91行）・`TacticalBuilder`（278行）・
  `BattleFlowScene.BuildTeam`（110行）・`NpcTeamDatabase.Load`（JSON に指定が無いとき）は `Spread = StatSpread.Auto(species)`。
  `BattleMatchScene.StripItems`（1131行）は**元の `Spread` を `Clone()` して引き継ぐ**。
  `BattleNetwork.DecodeFull` は受信値（無ければ Zero）。
  **`BattleTestScene` の構築は全て `Zero()` のまま**（ダメージを測る既存の検証の期待値を、種族値以外の理由で動かさないため）。
- **`StatSpread.Auto` は乱数を使わない**ので、`RandomTeam`・`MetaScenario` の乱数列の消費は変わらない（§4-11）。

#### S2-d. 自動配分 `StatSpread.Auto(SpeciesData sp)`（§8 Q4 推奨案）

決定的（同じ種族なら常に同じ）。合計は常に 55。

1. 主攻撃 P: 攻撃区分 Physical → Atk、Special → SpAtk、Mixed → `BaseAtk ≥ BaseSpAtk` なら Atk、でなければ SpAtk。
2. 気質: Physical → ↑Atk ↓SpAtk、Special → ↑SpAtk ↓Atk、**Mixed → 無補正**（両方の攻撃を使うので削る能力が無い）。
3. 突破値: P に 25。種族値 S ≥ 88（100 の峰の側）なら Speed に 25、そうでなければ Hp に 25。
   残り 5 を耐久区分の能力へ: Physical → Def、Special → SpDef、Both → `BaseDef ≤ BaseSpDef` なら Def、でなければ SpDef。
4. 例:
   - 009 ブシガエル（物理攻撃/特殊耐久、S73）: Hp25・Atk25・SpDef5、↑攻撃↓魔力 → 実数値 HP147・攻131・防51・魔39・精95・俊78。
   - 130B ライトロット（特殊攻撃/両面耐久、S130、B47 D46）: SpAtk25・Speed25・SpDef5、↑魔力↓攻撃 → 俊160。
   - 186 リリクイン（特殊攻撃/特殊耐久、S52）: Hp25・SpAtk25・SpDef5、↑魔力↓攻撃。
   - 007 ルミカイト（両刀攻撃/特殊耐久、C58>A40）: SpAtk25・Hp25・SpDef5、無補正。

#### S2-e. 行動順（`Scripts/Battle/BattleScheduler.cs` `Order`。§8 Q3 推奨）

- 比較を **優先度 → `Stats.Speed`（実数値: 気質・突破値込み）の高い順 → `Bst()`（BST6）の低い順 → 乱数** に変える
  （`BaseSpeed` の比較1行を `Speed` に置き換え、コメントも直す）。
- 俊敏にランク変化は無く、持ち物も俊敏を変えないので「ランク変化・持ち物では動かない」はそのまま成り立つ（コメントに残す）。
- 突破値・気質は相手に開示されない（`PublicEntryView` は種族のみ）。したがって**相手の実際の行動順は種族値 S からの目安でしか読めない**（ポケモンと同じ）。
- `Scripts/UI/Battle/BattleHud.cs` `RefreshRail`: 並びを `OrderByDescending(e => e.Stats.BaseSpeed).ThenBy(BattleScheduler.Bst)`（**公開情報の種族値 S**）にし、
  表示を `BST {bst}` から `S {BaseSpeed}` に変える（自陣の個体だけ実数値を `S {BaseSpeed}→{Speed}` と出してよい）。
  現行は BST 昇順で、Phase 4（S 優先）以降の行動順とずれているのを直す。`RefreshRail` 上のコメント「種族値も…公開情報」は維持。
- `BattleNetProtocol.cs` `TurnResult` のコメント「行動順は優先度→種族値→ランダム」を「優先度→俊敏→種族値→ランダム」に。

#### S2-f. 通信（`Scripts/Battle/BattleNetwork.cs` `EncodeFull/DecodeFull`）

- `EncodeFull`: 各個体の辞書に `["b"] = [Hp, Atk, Def, SpAtk, SpDef, Speed]`（int の配列）、`["nu"]`・`["nd"]` = `NatureStat` の名前文字列を追加。
- `DecodeFull`: キーが無ければ `Zero()`（古い相手との互換）。
- `EncodePublic` は変えない（種族だけ。突破値・気質は開示しない）。

#### S2-g. NPC 構築データ（`Scripts/Battle/NpcTeamDatabase.cs`）

- `NpcEntryJson` に省略可能な `breakthrough`（`{"hp","atk","def","spatk","spdef","speed"}`）と `nature`（`{"up":"Atk","down":"SpAtk"}`）。
- 省略時は `StatSpread.Auto(species)`。`Data/npc_teams.json` には**追加しない**（全チームが自動配分になる）。
- 読み込み後の `team.Validate()` は現行どおり（突破値・気質の違反も弾かれる）。

#### S2-h. 回帰スイートの変更（S2 完了で **152**）

既存の書き換え:
- `VerifyHabcdsWiring` #5（種族値%加算）: 実数値を合わせる操作を `BreakthroughSpAtk = addSp * 2` → **`addSp`**、`BreakthroughAtk = addPh * 2` → **`addPh`** に
  （1ポイント = 実数値+1 になったため）。コメント「突破値 +2 で実数値 +1」も直す。

追加（+5）:
1. `[検証] 気質の後に突破値を足す`: 盤上のブシガエル(009, A92)に、無補正・突破0 → 攻撃 **97**、↑攻撃 → **106**、↑攻撃＋攻撃25 → **131**
   （先に足すと 134 になるので区別できる）、↓攻撃 → **87**。HP25 → 最大HP **147** かつ現在HP = 最大HP（満タンのまま）。確認後に元へ戻す。
2. `[検証] 突破値と気質の構築検証`: 1能力26 → NG、合計56（25+25+6）→ NG、合計55（25+25+5）→ OK、合計0 → OK、
   気質 ↑攻撃↓攻撃 → NG、↑攻撃のみ（下降None）→ NG、↑攻撃↓魔力 → OK。`BattleTeam.Validate()` を通して見る。
3. `[検証] 行動順は俊敏の実数値を見る`: タマコッコ(003, S50) に ↑俊敏＋俊敏25（実数値 floor(55×1.1)+25 = **85**）、ブシガエル(009, S73) は無補正・突破0（実数値 **78**）、
   優先度0同士でタマコッコが先。突破・気質を戻すとブシガエルが先。（Q3 が (b) なら「突破値・気質を振っても順序が変わらない」に置き換える。数は同じ）
4. `[検証] 自動配分は決定的で規則どおり`: 全287種で `Auto` の合計が55・各≤25・`Validate()` が空、2回呼んで同じ。ブシガエルが Hp25/Atk25/SpDef5・↑攻撃↓魔力、
   ライトロット(130B)が SpAtk25/Speed25/SpDef5・↑魔力↓攻撃。（Q4 が (a) なら「既定値は全0・無補正」の確認に置き換える）
5. `[検証] 通信の直列化で突破値と気質が保たれる`: 突破値・気質を設定した6匹を `EncodeFull` → `DecodeFull` して全個体の6値と気質が一致、
   キー `b`/`nu`/`nd` を消した JSON は Zero で読める。

### Phase S3 — 構築画面（UI）

- `Scripts/UI/Battle/TeamBuildScreen.cs`:
  - `BuildDetail` を `SpeciesPanel` → **`SpreadPanel`（新設）** → `MovePanel` → `ItemPanel` の順に。
  - `SpeciesPanel`: 種族名の行に役割のピル（`sp.RoleLabel`、例「物理攻撃/特殊耐久」）を追加。種族値7チップは現行どおり。
  - `SpreadPanel`（新設）: 見出し「突破値・気質」と `残り {55 − Total}` のピル（0 で Brass、負なら赤系）。
    6行（HP/攻撃/防御/魔力/精神/俊敏）: ラベル・種族値・突破値（数値）・ボタン `−5 −1 +1 +5`（0〜25 と残りの範囲で頭打ち。押せない方向は Disabled）・実数値（Lv50。気質の↑↓を色で示す）。
    気質: 2つの `OptionButton`「上昇」「下降」（なし/攻撃/防御/魔力/精神/俊敏）。片方を選んだらもう片方の同じ能力は選べない。両方なし＝無補正。
    ボタン「自動配分」（`StatSpread.Auto`）と「すべて0」。
    変更は `_team.SetSpread(_slot, s)` → 既存の検証・再描画経路（`_ready.Disabled = errors.Count > 0` の所）に乗せる。実数値は `EntityStats` と同じ式の静的関数（`StatSpread` に `RealStat(...)` を置き、`EntityStats` からも使う）で計算して表示する。
  - `SlotCard`: 持ち物の行の右に気質のラベル（`NatureLabel`）を小さく出す（任意。並べ方は実装者判断）。
- `Scripts/UI/Battle/SpeciesPickScreen.cs`: 種族カードに役割ラベル（`RoleLabel`）を1つ出す。属性フィルタの並びは変えない。
- 回帰スイートの項目追加なし（**152**）。`godot --headless` で構築画面を開いて例外が出ないことは、既存の UI 起動経路があればそれで確かめる（無ければ確認できなかった旨を報告）。

### Phase S4 — Artifact・設計記録

- `Tools/build_ls_payload.py`: `pf`・`pb` を削除し、`of`（offense）・`df`（defense）・`ob`（`'特性'` / `'周期'`）を出力。`role` は新しい値。`bst` は新 BST6。
  総合耐久指数 `ti = H*(B+D)` を追加。
- `Tools/artifacts/ls_tail.html`: `PFJA` を削除し、`OFJA = {Physical:'物理攻撃', Special:'特殊攻撃', Mixed:'両刀攻撃'}`、`DFJA = {Physical:'物理耐久', Special:'特殊耐久', Both:'両面耐久'}`。
  絞り込みは「攻撃区分」「耐久区分」の2つのチップ列（`pfs` → `ofs`、`roles` → `dfs`）。両方選べば 9 役割のどれかに絞れる。
  表示の `${PFJA[s.pf]}・${ROLEJA[s.role]}` → `${OFJA[s.of]}/${DFJA[s.df]}`、詳細の「役割」も同じ表記。詳細に「総合耐久指数」を1行足す。並べ替えに「総合耐久指数」「俊敏」を追加。
- `Tools/artifacts/ls_head.html`: チップ列の id とラベル（「型」→「攻撃区分」、「役割」→「耐久区分」）。
- `python3 Tools/build_ls_payload.py` と `python3 Tools/refresh_move_editor.py`（learnset が6種変わり MV_139/140/141 の習得者数が変わるため）を実行し、
  `Tools/artifacts/learnset_viewer.html` を **url `188eb9b1-d49d-4c39-bf77-895a30185e99`**、`Tools/artifacts/move_editor.html` を **url `975b54fe-5c46-4597-ae30-26d694ac3eb2`** へ、同じ file_path で再公開。
  出来上がった `learnset_viewer.html` に「物理型」「特殊型」「打ち分け型」が含まれないことを `grep` で確かめる。
- `docs/design/pvp_spec.md` に新しい節（Phase 6 が使った次の番号。§28 の次が空いていれば §29）を追加: BST6 の新式、俊敏三峰の写像とヒストグラム、
  役割の直積と配分式、S≥95 の優先技規則と6種の置換、突破値・気質の式と検証規則・自動配分・行動順の変更、S5 の計測結果。
  §1「行動順の決定」を新規則（実数値の俊敏）に書き換える。
- **`CLAUDE.md` は編集しない**。ただし「行動順と天候の向きは逆」の節（行動順の記述）と「種族値を変えると固定シードの再現性が崩れる」の節が古くなるので、S4 完了報告で更新が必要と指摘する。

### Phase S5 — 計測（10,000 試合）

- `Tools/analyze_random_by_element.py` を一般化し、`--by element|offense|defense|role|speed` を受ける（既定 element。集計の数え方・BST 補正は現行と同じ）。
  `speed` は種族値 S の帯 `≤62 / 63–87 / 88–99 / ≥100`。species.json の `offense`/`defense`/`role`/`base_speed` を読む。
- `godot --headless --path . Scenes/BattleMatchScene.tscn -- --random --matches 2500 --out /tmp/s5_<n>.csv` を4プロセス（`--random` は状態を持たないので分割してよい）。
- 結果を S4 で足した節に表で記録（属性10・攻撃区分3・耐久区分3・役割9・俊敏帯4）。§5-4 の基準で評価して報告。
- **CLAUDE.md の注意**: 種族値と行動順が変わったので、`MetaScenario`（シード `20260822UL`）の構築番号（#66/#53/#30 等）は、species の並び・技・持ち物が同じでも別の強さになる。過去節と構築番号で比較しない。

---

## 4. 判断が必要だった点（採った解釈と理由）

### 4-1. BST6 の「320〜610」
- 候補: (a) 現行と同じ線形写像の端点だけ変える（BST3 180→320、500→610、傾き 290/320）。(b) 現行 BST6 から一律 −40 / 比例縮小（630×k=610 なら最小は 349）。
- **採用: (a)**。「最大値を320〜610に」は範囲の両端の指定と読める。(b) は下端が 320 にならない。前回の確認時の追加指示（180→360、500→630 の線形）と同じ形式を保つ。
  BST の並び順はどこでも変わらない（S1-a）。

### 4-2. 「表記をなくす」の範囲
- 型という概念自体は learnset の生成規則（多数側の分類・威力天井）を支えているので消せない。消すのは**名前**と、役割から独立した軸としての存在。
  「型」を「攻撃区分」と呼び替え、役割（攻撃区分/耐久区分）の前半に吸収した。値の `Versatile` → `Mixed`、和名「打ち分け型」→「両刀攻撃」。
- 「打ち分け」という語も残さない（「打ち分け型」を連想させるため）。代わりに「両刀」（物理・特殊の両方で攻める、の慣用語）を使った。命名は §8 に入れていない（後から1行で変えられ、結果が変わらないため）。
- 過去の草案（`docs/design/drafts/2026-10-25-*.md`）は確定時点の記録なので書き換えない。`pvp_spec.md` は現行仕様の記述なので直す。

### 4-3. 攻撃と耐久の直積（§8 Q1）
- 現行の5役割は「攻撃型/耐久型」が**攻撃と耐久の取り合い**（副側を削るか残すか）を表しており、B と D の偏りを表していない。
  ユーザー例「物理攻撃/特殊耐久」「特殊攻撃/物理耐久」は**攻撃の分類と、耐久の向き（B か D か）が逆**の組み合わせなので、耐久区分を B:D の偏りとして新設し直積にした。
- 攻撃と耐久の**量**の取り合いは、新しい要求「俊敏が低いほど耐久が厚い」で S が決める（S1-d の w）。旧「攻撃型/耐久型」の区別はこれに置き換わる。
- 耐久区分の決め方はユーザー指定が無い。ハードアーマー/ハードブロックは被弾分類ごとの耐久特性なので、その向きを強める側（相乗）に置いた。
  残りは攻撃区分と同じく species_id 序数の周期で散らした（再現可能・9通りが偏らない）。各役割 25〜46 種。

### 4-4. B:D と A:C の比
- B:D = 65:35 / 35:65 / 50:50 は、前回ユーザーが打ち分け型に与えた許容幅「35:65〜65:35」の両端と中央。
- A:C = 70:30 / 30:70 は旧物理耐久型・物理攻撃型の副側の残し方（旧 C=0.4a〜0.6a ⇔ A 比 0.63〜0.71）の中間。両刀攻撃は前回の「40:60〜60:40」と learnset の物理比 r をそのまま使う。

### 4-5. 「俊敏性が低いほど…耐久指数」（文が途中で切れている）
- 原文は「また、俊敏性が低いほど物理/特殊耐久指数(…)」で述語が無い。前後（ペラペラだが速い／遅いが物理受け）から**「俊敏が低いほど耐久指数が高い」**と読んだ。
- 組み込み方: 現行の「S を先に決め、残り T を配る」を保ち、耐久の取り分 w を S の一次減少関数（0.76→0.46）にした。T 自体も S が低いほど大きいので二重に効く。
- 検証指標: 生の相関は BST の交絡（ライドの個体ほど BST3 が大きい）で弱く出るので、**生の相関 ≤ −0.40 と BST6 統制の偏相関 ≤ −0.80 の両方**、加えて S 帯別平均の単調減少を基準にした。
  傾き 0.30 は、生の相関が −0.4 を明確に超え、かつ最小能力が 10 を割らない範囲で選んだ（傾き 0.22 では生の相関 −0.28）。
- 「物理耐久指数」「特殊耐久指数」は耐久区分で B/D が偏るので、S との相関は総合より弱い（−0.39 / −0.34）。基準は総合耐久指数に置いた。

### 4-6. 俊敏の三峰
- 50 の峰は、現行で 35〜73 に散っていた運搬0の非ライド61種を 47〜60 に集めて作った。75 の峰は運搬1〜2（88種）、100 の峰はライド（現行のまま）。
- 最小35（035 のみ）・最大130・基準点4種は前回の確定事項なので維持した。
- 山の位置の判定は「中心±2」の階級で行う（前回と同じ階級）。50 の峰は 43〜62 に裾が広い（運搬0の個体の走行速度の差）が、最大階級は 48–52。

### 4-7. 既存の突破値との関係
- 既存の `Breakthrough*` は努力値型（種族値の2倍に加算、Lv50 で +2 → +1）で、0 以外に設定する経路が無い（§0-4）。
- 候補: (a) **同じ名前のまま「気質の後に実数値へ直接加算」に置き換える**。(b) 別名で新設し、既存は残す。
- **採用: (a)**。ユーザーは「突破値」という名前で新仕様を定義しており、既存が未使用なので互換の問題が無い。名前が2つあると誤用の元になる。
  検証の1箇所（`BattleTestScene` 702–706行）だけ `×2` を外す。

### 4-8. 個体差（突破値・気質）を見る箇所と見ない箇所
| 箇所 | 見るもの | 理由 |
|---|---|---|
| ダメージ計算（A/B/C/D・HP） | **実数値**（個体差込み） | 突破値・気質の目的そのもの |
| 行動順の俊敏 | **実数値**（§8 Q3 推奨） | 俊敏に振る意味が行動順にしか無い。種族値のままだと S の突破値・気質が完全に無効になる |
| 行動順のタイブレーク BST6 | 種族値 | 「合計種族値」は種族の定数。個体差を混ぜると 55 振った個体が常に後手になる |
| 天候の発動順 | 種族値（BST6） | 同上。公開情報だけで決まる現行の性質を保つ |
| NPC 選出採点 | 種族値（BST6） | 同上 |
| 種族値%の平坦加算（きずな・まもり・リーダー系） | 種族値 | 効果文が「種族値の○%」 |
| S≥95 の優先技規則 | 種族値 | learnset は種族のもの |

### 4-9. 突破値の合計「55 ちょうど」か「55 以下」か
- 「全体で55ポイントを振り分け可能」は上限の表現と読み、**55 以下**にした（ポケモンの努力値と同じ慣例。構築画面の初期状態0も合法にできる）。
  自動配分は常に 55 を使い切る。結果への影響は「余らせる理由が無い」ので小さく、確認事項にはしなかった。

### 4-10. 気質の表現
- ポケモンの25性格（無補正5種を含む）と同等の自由度を、`NatureUp`/`NatureDown` の2値で表す。無補正は (None, None) の1通りに正規化する
  （(攻撃↑攻撃↓) のような「同じ能力の上下」は不可にして、無補正の表し方を1つにする）。

### 4-11. 既定の突破値・気質と再現性（§8 Q4）
- ランダム構築・MetaScenario・TacticalBuilder・NPC は構築を自動で作るので既定値が要る。全0だと対戦シミュレーションで突破値・気質が一度も使われず、
  バランス計測が実際の対戦を反映しない。乱数で振ると Fisher-Yates の乱数列の消費が増え、同じシードの構築（species・技・持ち物）が変わる。
- **推奨: 役割に応じた決定的な自動配分**（S2-d）。乱数を消費しないので、同じシードから出る species・技・持ち物の列は現行と同じ
  （ただし S1-e で learnset が変わる6種は、`RandomTeam` の技の抽選と `DefaultLoadout` の選択が変わり得る）。
- 回帰スイートの構築は全0のまま（既存のダメージ検証を個体差で動かさないため）。

### 4-12. 優先技規則の「傾向として」
- 「傾向として」は規則の動機（速い個体に先制技は要らない）を述べたものと読み、**例外の無い規則**として実装・検証する（検証方法 (c) が「持たないこと」を求めているため）。
  対象は技の優先度だけで、特性による優先度（水のちから＝あめで+1）は対象外。制限技（`rs_`）に優先度+1の技は無い。
- 置き換えは「同じ属性・分類・射程・接触で優先度0の最も近い威力」。習得者が持つ攻撃技の数・分類比は変わらない。

### 4-13. 種族値の変更による影響（BST6 に依存する全箇所）
| 箇所 | 影響 |
|---|---|
| `BattleScheduler.Order` のタイブレーク・`BattleArena.ApplyEntryWeather`・`MetaScenario.StrongPool`・`TacticalBuilder`（BST 昇順の並び）・`SpeciesPickScreen` の種族値順 | BST6 は BST3 の狭義単調増加関数なので**並びは変わらない**（陣営合計の比較も、BST3 合計が異なれば向きは保たれる: 5点差で 4.53 > 丸め誤差の最大4） |
| `NpcOpponent.SelectionScore` | `Bst6Weight` を 32/29 に（1点の重みを旧 BST3 並みに保つ） |
| `generate_npc_teams.py` の TARGET_BST・`npc_teams.json` の `total_bst` | 新式の値に（編成は変えない） |
| UI の「BST」表示（Deploy・TeamBuild・SpeciesPick・Selection・OpponentSelect・BattleHud） | 値が新 BST6 になるだけ（文言は変えない。`BattleHud.RefreshRail` は S2-e で S 表示に変える） |
| `--random --out` の CSV `home_bst/away_bst` と集計の BST 補正 | 値が新 BST6。ロジスティックの k は集計時に当て直すので式の変更は不要 |
| `Tools/build_ls_payload.py` の `bst` | 新 BST6 |
| learnset 系ツールの閾値（BST180〜500 前提の上限表・`<310`） | `legacy_had` の BST3 を読み続けるので**影響なし** |
| 回帰スイートの期待値 | S1-h に列挙 |

---

## 5. 検証方法

各フェーズで次を必ず実行する（CLAUDE.md）。

```sh
dotnet build MysteryDungeonRoguelike.csproj                              # 0 エラー（godot より先に）
godot --headless --path . Scenes/BattleTestScene.tscn | grep '\[検証\]'   # 項目数と NG 0（スプライトの import エラーは無視してよい）
git diff                                                                 # この仕様書と突き合わせる
python3 Tools/verify_learnset.py && python3 Tools/verify_attack_moves.py && python3 Tools/verify_npc_teams.py   # ALL PASS
```

### 5-1. データ（S1）— ユーザー指定の (a)〜(e)

| # | 確かめること | 方法 | 期待値 |
|---|---|---|---|
| (a) | 全287種の BST6 が 320〜610 | `build_habcds.py` 自己検査、スイート `VerifyHabcdsData` #1 | 最小320・最大610、003=320・186=474・203=610 |
| (b) | S が 50/75/100 の三峰 | `build_habcds.py` の `report`、スイート #4 | S1-b のヒストグラムと全階級で一致、c(50)=23・c(75)=62・c(100)=68 |
| (c) | S≥95 は優先度+1以上の攻撃技を持たない | `apply_fast_priority_rule.py` の最終検査、`verify_learnset.py` の新検査、スイート追加項目 | 111種で0件。置換6件が S1-e の表どおり |
| (d) | S と総合耐久指数の相関 | 自己検査、スイート追加項目 | r = −0.475（≤ −0.40）、偏相関 −0.892（≤ −0.80）、帯別平均 11903 > 8732 > 8103 |
| (e) | 役割の直積が全て実在 | 自己検査、スイート #3 | S1-c の表の9つの数（P:46/41/37、S:27/30/27、M:25/28/26） |
| — | 付録A と一致 | species.json の全287種の HABCDS・offense・defense を付録A と突き合わせるスクリプトを一時的に書いて実行（コミットしない） | 287/287 一致 |
| — | 冪等 | `build_habcds.py` → `apply_fast_priority_rule.py` → `build_habcds.py` → `apply_fast_priority_rule.py` の後に `git diff --stat Data/species.json` が1回目の後と同じ | 2回目は差分なし |
| — | 表記 | `grep -rn "物理型\|特殊型\|打ち分け型\|器用型" Scripts Tools docs/design/pvp_spec.md` | S1 完了時は `Tools/artifacts/` 以外 0 件、S4 完了時は 0 件 |

### 5-2. 突破値・気質（S2）— ユーザー指定の (f)

- スイート追加1（S2-h）: ブシガエル A92 → 97 / 106 / **131**（≠134）/ 87、HP 147 で満タン。
- 手計算の確認（スイートと別に、仕様の読み違い防止）: 素=種族値+5、↑は `*11/10`、↓は `*9/10`（いずれも切り捨て）、突破値は最後に足す。
  例: S50 → 素55 → ↑60 → +25 で 85。S130 → 素135 → +25 で 160。A=40 → 素45 → ↓40（45×9/10=40.5→40）。
- 迷宮の回帰: 突破値0・無補正で実数値が変わらないこと（スイートの既存項目が全て OK のままであることで確認。既存のダメージ期待値が S1 の値から動かない）。

### 5-3. 回帰スイートの項目数

| 完了時点 | 項目数 | 内訳 |
|---|---|---|
| 基準（Phase 6 完了） | 145 | — |
| S1 | **147** | 書き換え: HABCDS データ4項目・BST/ベンチマーク関連3項目（数は不変）。追加: 優先技規則・俊敏と耐久の相関 |
| S2 | **152** | 書き換え: 種族値%加算（×2を外す）。追加: 適用順・構築検証・実数値の行動順・自動配分・通信 |
| S3〜S5 | 152 | 追加なし |

Python: `verify_learnset.py` は N_L+1 PASS（S≥95 の検査）、`verify_attack_moves.py` は N_A（技データは変えない）、`verify_npc_teams.py` は N_N。

### 5-4. 10,000 試合（S5）— ユーザー指定の (g)

- `analyze_random_by_element.py --by element|offense|defense|role|speed` で、出場数・勝率・BST 補正後差(pt) を出す。
- **基準（良好）**: 属性10・攻撃区分3・耐久区分3・役割9 の BST 補正後差がすべて **±5pt 以内**。前回（炎 +2.9pt / 無 −3.5pt）から属性の最大偏差が悪化していないこと。
  未決着率 2% 以下。俊敏帯は判断材料として報告（S が行動順を決めるので速い帯が強く出るのは想定内。帯の差が ±8pt を超えたら報告）。
- ±5pt を超える区分があっても**合否ではなく調整の判断材料**として報告する（実装者は数値を変えない）。
- 自動配分（Q4）の影響を見るため、可能なら同じ 10,000 試合を突破値0・無補正（`--spread zero` のような一時オプション。コミットしない）でも回し、差を報告する（任意）。

---

## 6. やらないこと

- learnset の再生成（`generate_learnset.py` の再実行）。learnset の変更は S1-e の6行の置換だけ。
- `Data/source/paldb_speed.json` の再取得。ライドの写像 `P_R`・S の範囲 35〜130・基準点4種の変更。
- Phase 6 の内容（`rs_` 技、その習得者、`RESTRICTION_LEARNERS`）の変更。
- 攻撃区分の決め方（`TRAIT_PHYSICAL`/`TRAIT_SPECIAL`/周期）の変更。
- 技データ（`Data/moves.json`）の変更。ヘッドバットの優先度は変えない（S が 95 未満の15種は引き続き覚える）。
- 俊敏のランク変化・俊敏を変える持ち物の新設。俊敏の回避・命中・迷宮のターン速度への利用。
- 迷宮側（Player/AllyEntity 等）への突破値・気質の導入と、迷宮のステータス画面。
- NPC の行動判断に能力値・気質・突破値を足すこと（`NpcOpponent` の変更は `Bst6Weight` のみ）。NPC の構築を突破値・気質を見て最適化すること。
- `Data/npc_teams.json` の編成の変更（`total_bst` の値の更新のみ）。
- 対戦 HUD に相手の突破値・気質・実数値を出すこと（非公開情報）。
- `docs/design/drafts/` の過去の草案の書き換え、`CLAUDE.md` の編集（S4 の報告で指摘だけする）。
- 既存の天候・持ち物・特性・ダメージ式の数値調整（S5 で偏りが出ても、実装者は調整しない）。

---

## 7. フェーズと完了条件

| Phase | 内容 | 完了条件 |
|---|---|---|
| S1 | BST6 新式・俊敏三峰・役割直積・配分・S≥95 の優先技置換・表記改名（Python/C#コメント）・`offense`/`defense` の DTO・`Bst6Weight`・npc_teams の `total_bst` | build 0エラー / スイート **147**・NG0 / Python 3本 ALL PASS（learnset は N_L+1）/ `build_habcds.py` 自己検査 OK / 付録A と 287 種一致 / 冪等 |
| S2 | 突破値・気質（`EntityStats` の式・`StatSpread`・`BattleEntry`・`Validate`・自動配分・全生成箇所・通信・NPC JSON・行動順の実数値化・HUD レール） | build 0エラー / スイート **152**・NG0 / Python 3本 ALL PASS |
| S3 | 構築画面（`SpreadPanel`・役割ラベル） | build 0エラー / スイート **152**・NG0 / 構築画面で突破値・気質を設定→検証→対戦開始まで通ることを確認（確認できない環境なら、その旨と、代わりに確かめた範囲を報告） |
| S4 | Artifact 2つの再生成・同 URL 再公開、`pvp_spec.md` の新節と §1 の書き換え | 2つの URL が更新 / `learnset_viewer.html` に旧表記0件 / スイート 152 |
| S5 | 10,000 試合と区分別集計 | 新節に表 / §5-4 の基準で評価した報告 |

各フェーズは前のフェーズが完了条件を満たしてから着手し、フェーズごとに別コミットにする（コミットの指示があれば）。
項目数がこの表とずれたら、追加・変更した `[検証]` の一覧を添えて報告する。

---

## 8. ユーザーに確認すべき点

解釈が分かれて**結果が実質的に変わる**ものだけ。本文は推奨案で書いてある。

1. **役割の構成（攻撃区分 × 耐久区分）**
   - (a) **攻撃3（物理攻撃/特殊攻撃/両刀攻撃）× 耐久3（物理耐久/特殊耐久/両面耐久）= 9役割**【推奨】。learnset はそのまま使える（攻撃区分＝現行の型の改名）。各25〜46種。
   - (b) 攻撃2 × 耐久3 = 6役割。両刀攻撃の79種を learnset の多数側（物理54・特殊25）へ寄せ、A:C を 70:30/30:70 にする。learnset は打ち分けのまま残るので、技と能力がずれる。
   - (c) 攻撃2 × 耐久2 = 4役割（両面耐久も無し）。最も単純だが、B=D の個体が居なくなる。
   - 差し替え箇所: S1-c・S1-d 手順7〜8・付録A・スイート #2/#3。
2. **両刀攻撃の「H×0.8 = B+D」（前回確定した打ち分け型の耐久配分）を残すか**
   - (a) **廃止し、全役割共通の H:(B+D) = h:2d にする**【推奨】。両刀攻撃の H 平均 68（他と同水準）、B:D は耐久区分が決める。
   - (b) 両刀攻撃だけ維持（H = Bulk×5/9）。両刀攻撃の H 平均 112・B+D 平均 90 で、「HP が高く防御が薄い」個性が残る。
   - 差し替え箇所: S1-d 手順6・付録A の両刀攻撃79種。
3. **行動順が見る俊敏**
   - (a) **実数値（気質・突破値込み）**【推奨】。俊敏への突破値・気質が意味を持つ。相手の行動順は種族値 S からの目安でしか読めなくなる（突破値・気質は非公開）。
   - (b) 種族値のまま（現行）。行動順は公開情報だけで決まり続けるが、俊敏に振る突破値・気質は完全に無意味になる（俊敏を使う箇所が行動順しか無いため）。
   - 差し替え箇所: S2-e・スイート追加3・§4-8。
4. **NPC・ランダム構築・MetaScenario・TacticalBuilder・構築画面の既定の突破値・気質**
   - (a) 全0・無補正。シミュレーションでは突破値・気質が一度も使われない。
   - (b) **役割に応じた決定的な自動配分**（主攻撃25＋俊敏または HP 25＋耐久区分の能力5、気質は主攻撃↑・使わない攻撃↓、両刀は無補正）【推奨】。乱数を使わないので同じシードの構築列は保たれる。
   - (c) 乱数で配分・気質を抽選。多様だが、Fisher-Yates の乱数消費が増え、同じシードでも構築（species・技・持ち物）が変わる。
   - 差し替え箇所: S2-c・S2-d・S2-g・スイート追加4。

（決めて本文に書いたもの: BST6 の写像（§4-1）、表記の範囲と「両刀」の名前（§4-2）、耐久区分の決め方（§4-3）、B:D・A:C の比（§4-4）、
俊敏と耐久の結び付け方と傾き（§4-5）、三峰の写像（§4-6）、既存突破値の置き換え（§4-7）、個体差を見る箇所（§4-8、行動順を除く）、
合計55以下（§4-9）、気質の表し方（§4-10）、優先技規則を例外なしにすること（§4-12）。）

---

## 実装時に判断してよい範囲

次は結果（付録A・§5 の期待値）を変えないので、実装者が決めてよい（報告不要）。これ以外で迷ったら決めずに報告する。

1. `build_habcds.py` の関数分割・`Fraction` か整数演算か（結果が付録A と一致すること）。
2. `StatSpread` の置き場所（`Scripts/Entities/` か `Scripts/Battle/`）と、`NatureStat` の enum 名（JSON・通信の文字列は `Atk/Def/SpAtk/SpDef/Speed` で固定）。
3. `SpreadPanel` の並べ方・色・ボタンの刻み（−5/−1/+1/+5 が押せること、残りポイントと実数値が見えること）。
4. `[検証]` 行の括弧内の補足文言（OK/NG の判定内容は §3 どおり）。
5. `analyze_random_by_element.py` の出力の体裁（区分ごとに 出場数・勝率・期待勝率・補正後差 が出ること）。

逆に、次は**決めずに報告**: BST6 の式、`P_N` の点、w(S) の係数（0.76・0.30・2/3・0.45・0.80）、β・α、耐久区分の決め方、
優先技の置換規則、実数値の式と丸め、検証規則（25・55・気質）、自動配分の規則、行動順の比較順。

---

## 付録A. HABCDS の期待値（species_id:攻撃区分耐久区分:H/A/B/C/D/S）

略号: 攻撃区分 P=物理攻撃 S=特殊攻撃 M=両刀攻撃、耐久区分 B=物理耐久 D=特殊耐久 E=両面耐久（JSON の `Both`）。
6値の合計は §S1-a の BST6。§S1-b〜S1-d の式で、`Data/source/paldb_speed.json` と Phase 6 作業ツリーの learnset（`rs_` は r に数えない）から計算したもの。
S1-e の learnset 置換は category を変えないので、この表は置換の前後で同じ。

```
001:PB:58/69/75/30/41/74 002:PD:58/69/41/30/75/74 003:PE:64/55/64/23/64/50 004:PB:75/60/92/25/49/51 005:SD:51/34/35/79/66/73 005B:PE:50/83/51/36/50/73
006:PB:71/60/92/25/50/49 007:MD:64/40/44/58/83/76 007B:ME:63/62/64/42/63/76 008:MB:71/33/99/49/53/47 009:PD:62/92/46/39/85/73 009B:PE:64/99/69/42/68/73
010:PB:65/73/79/31/43/74 011:PD:73/50/51/21/96/47 012:PE:72/58/73/25/72/47 013:SB:57/33/74/78/40/74 014:SD:83/24/47/55/87/51 015:ME:70/37/70/55/69/51
015B:SB:67/27/99/63/54/51 016:MD:58/40/41/59/76/73 017:PE:57/74/57/32/57/75 017B:PB:57/78/73/33/40/75 018:PD:68/90/48/39/89/81 018B:PE:71/94/72/40/71/81
019:SB:68/25/101/58/54/50 020:SD:59/35/41/80/76/97 021:SE:79/38/60/90/59/98 021B:MB:79/52/77/76/42/98 022:MD:60/63/36/43/68/73 023:ME:67/40/59/58/59/73
023B:SB:67/31/76/73/41/73 024:PD:76/70/50/30/92/47 025:SE:53/28/71/64/71/60 026:SB:74/25/96/59/51/51 027:SD:70/23/49/52/91/53 027B:SE:70/24/70/57/69/53
028:SB:56/32/63/76/34/95 029:MD:64/38/49/55/90/51 029B:ME:64/59/69/40/69/51 030:MB:49/36/74/53/40/77 030B:PD:57/62/40/27/75/77 031:PE:58/80/45/34/44/73
032:PB:63/85/70/36/37/97 032B:PD:63/85/37/36/70/97 033:SE:50/40/47/94/47/101 034:SB:95/23/111/54/59/51 034B:SD:94/25/59/58/110/51 035:ME:72/28/73/40/72/35
036:MB:64/80/75/54/40/98 037:MD:55/40/59/60/110/73 037B:PE:61/77/90/33/90/73 038:PB:65/70/74/30/40/77 038B:PD:62/80/38/34/70/81 039:PB:67/55/112/24/60/47
039B:SB:66/23/116/55/63/47 040:SD:86/18/54/42/100/56 041:SE:67/44/62/103/61/101 041B:MB:70/62/83/91/44/101 042:MD:49/63/40/43/74/74 043:ME:65/37/65/55/65/60
043B:PB:65/64/85/28/45/60 044:PD:70/60/46/25/86/60 044B:PE:76/59/66/25/66/60 045:PB:68/80/83/34/45/60 046:SD:74/31/43/72/80/74 047:SE:54/30/54/71/54/75
048:SB:56/35/72/83/39/76 048B:MD:60/47/39/70/73/76 049:ME:74/64/59/44/59/56 050:MB:69/51/73/76/40/74 051:PD:66/97/37/41/68/97 052:PE:74/89/62/38/62/77
053:PB:61/64/93/28/50/47 054:PD:47/106/43/45/80/99 054B:SE:46/45/64/106/64/99 055:PB:73/84/79/36/42/74 055B:PD:73/84/42/36/79/74 056:PE:72/85/72/36/72/56
056B:PB:72/88/93/38/50/56 057:MD:65/48/49/70/91/60 058:PE:51/98/57/42/57/101 059:PB:52/80/62/34/33/73 060:PD:64/76/44/33/83/74 061:PE:65/69/74/29/73/55
062:SB:69/31/84/72/45/60 063:SD:72/37/42/85/77/98 063B:SE:75/37/62/86/62/98 064:PB:54/113/71/49/38/99 064B:MD:53/100/40/68/73/99 065:ME:84/47/93/70/93/55
066:PB:71/91/110/39/59/77 067:PD:60/85/47/37/87/77 068:PE:79/83/88/35/87/52 069:PB:98/70/75/30/40/80 070:SD:74/37/38/85/70/98 071:SE:67/44/61/104/60/102
072:SB:61/44/88/102/48/90 072B:MD:61/59/49/87/92/90 073:ME:70/67/44/46/44/76 073B:MB:72/45/58/67/31/74 074:PD:65/91/45/39/84/82 074B:PE:64/95/65/41/64/82
075:PB:56/95/65/40/35/102 075B:PD:56/93/44/40/81/97 076:SE:67/42/60/98/60/79 076B:SB:67/42/78/98/42/79 077:SD:63/28/41/65/76/74 078:ME:68/58/61/86/61/77
078B:MB:67/86/83/58/44/77 079:MD:67/56/47/82/86/77 079B:PE:71/97/67/41/67/77 080:PB:57/123/75/53/40/103 080B:PD:60/124/40/53/75/104 081:PE:77/99/73/42/73/83
081B:SB:85/41/96/94/52/83 082:SD:67/40/47/93/86/78 083:SE:68/47/56/111/55/101 083B:MB:67/66/72/98/38/101 084:MD:72/76/41/52/76/98 084B:ME:71/54/59/80/58/98
085:PB:58/114/75/49/40/79 085B:PD:58/118/40/50/75/79 086:PE:59/76/67/32/67/73 087:PB:101/91/79/39/43/98 087B:SD:101/39/43/91/79/98 088:SE:66/32/71/75/70/60
088B:SB:66/34/91/79/49/60 089:MD:73/51/51/76/94/79 089B:ME:68/82/72/56/72/79 090:MB:72/60/89/90/48/79 091:PD:71/94/44/40/82/80 091B:PE:68/99/61/43/61/83
092:PB:56/78/72/34/39/77 092B:PD:55/78/41/34/76/77 093:SE:62/42/56/99/55/101 093B:SB:62/42/72/99/39/101 094:SD:72/50/46/115/86/101 094B:MB:71/69/85/102/46/101
095:MB:77/65/116/44/62/56 096:MD:55/68/29/100/54/123 096B:PE:53/119/44/51/43/123 097:PB:50/124/75/53/41/104 097B:PD:50/127/40/55/75/104 098:PE:53/104/48/44/47/106
098B:SB:55/46/63/106/34/102 099:SD:78/34/71/80/133/60 099B:SE:78/36/101/84/101/60 100:MB:60/47/73/70/40/75 100B:MD:60/73/39/50/73/75 101:ME:60/52/67/78/66/101
102:PB:62/95/72/41/39/102 102B:PB:60/104/77/44/42/102 103:PE:58/85/52/37/52/99 103B:PB:58/90/67/38/36/99 104:SD:52/47/46/110/86/106 105:SE:66/52/70/121/69/96
105B:SB:66/52/94/121/50/96 106:MD:63/63/48/94/90/98 107:ME:69/60/104/41/103/47 108:MB:77/54/83/81/45/98 108B:PD:77/98/44/42/83/98 109:PE:78/67/74/29/74/75
109B:PB:81/71/100/30/54/75 110:PD:81/99/52/43/97/98 111:SE:59/49/59/115/59/101 111B:PB:59/115/80/49/43/101 112:SD:66/47/35/110/66/100 112B:ME:66/66/50/97/50/100
113:MB:80/82/104/56/56/87 113B:MD:80/58/56/86/103/87 114:PE:97/78/88/33/88/54 115:PB:82/104/87/44/47/78 116:PD:100/68/63/29/118/51 116B:PE:104/67/91/29/91/51
117:SB:46/45/63/106/34/99 118:SD:72/37/41/86/76/99 118B:SE:71/39/58/90/58/99 119:MB:68/60/97/88/52/77 120:PD:75/107/53/46/98/77 121:ME:78/69/61/103/60/103
121B:PB:80/127/79/54/43/100 122:PD:71/109/44/47/81/104 122B:PE:75/109/63/47/62/104 123:PB:61/94/66/40/36/100 124:SD:60/45/40/106/75/107 124B:SE:61/47/58/109/57/106
125:SB:86/31/99/73/53/51 126:MD:65/36/55/52/102/60 127:ME:54/76/54/52/53/99 128:MB:63/49/94/72/51/54 128B:PD:63/88/51/38/94/54 129:PE:66/106/72/46/72/98
129B:PB:65/107/100/46/54/98 130:PD:67/103/43/44/80/101 130B:SE:51/50/47/118/46/130 131:SD:86/33/72/77/134/49 131B:SD:86/35/72/81/133/49 132:ME:67/55/74/82/74/86
132B:MB:67/85/95/58/51/86 133:MD:62/42/46/62/85/77 134:PE:88/90/63/39/62/105 134B:PB:86/91/88/39/47/105 135:PD:89/102/53/44/99/60 136:PE:64/86/64/37/63/79
137:SB:57/53/90/122/48/100 137B:SD:60/54/50/127/92/100 138:SD:64/46/51/108/95/101 138B:MD:64/64/51/96/94/101 139:MD:82/102/48/68/88/86 140:ME:79/60/72/89/72/79
141:PB:76/92/99/39/54/60 141B:PD:76/96/53/41/98/60 142:PD:58/99/57/43/107/92 142B:PB:57/99/110/43/59/92 143:SD:79/40/50/94/93/82 144:SE:41/26/88/61/87/80
145:SB:65/49/80/113/43/79 146:MD:63/69/52/102/97/100 147:ME:78/75/75/50/75/76 148:MB:77/50/100/75/54/77 148B:PD:81/88/54/37/101/77 149:PE:77/75/77/32/77/55
149B:PB:76/74/104/32/56/55 150:PB:84/81/116/35/62/51 151:SE:62/49/65/115/65/100 151B:SB:62/51/84/118/45/100 152:SD:63/46/44/106/83/82 153:ME:67/60/74/89/73/79
154:MB:72/96/86/64/46/101 155:MD:64/54/50/80/92/80 156:PE:72/99/63/42/63/99 157:PB:73/112/79/48/42/102 157B:PD:74/115/43/49/79/100 158:PE:57/123/72/52/71/99
159:SB:64/46/106/108/57/93 159B:SD:63/48/57/112/106/93 160:SE:73/48/70/111/69/99 161:MB:78/69/86/102/47/101 162:MD:69/61/45/42/84/60 163:ME:64/34/75/51/74/49
163B:PB:63/59/101/25/55/49 164:PD:59/75/35/32/66/76 165:PE:55/74/63/31/63/75 166:PB:55/81/81/35/44/74 167:SD:83/26/50/61/94/47 168:SE:54/35/61/82/61/77
169:SB:55/41/67/95/36/103 169B:MD:54/57/36/85/67/103 170:ME:91/67/87/46/86/56 171:MB:52/78/70/116/37/130 171B:PD:52/138/38/59/71/130 172:PE:46/90/50/38/49/101
173:PB:76/71/104/30/56/56 174:PD:78/111/53/48/97/78 175:SE:77/50/71/117/71/102 176:SB:72/43/94/100/51/78 177:SD:63/50/38/118/71/107 178:ME:60/47/68/69/67/77
179:MB:100/61/111/41/60/47 180:MD:83/44/62/65/114/47 181:PE:67/86/50/37/50/75 182:PB:78/102/107/44/57/77 183:PD:70/112/47/48/87/87 184:PD:59/123/55/53/103/99
185:SB:72/47/82/108/44/103 186:SD:88/40/70/94/130/52 186B:SE:98/38/98/90/98/52 187:MB:80/68/99/102/53/86 188:MD:59/97/46/65/84/105 188B:ME:61/66/65/97/64/107
189:PB:68/121/103/52/55/102 190:PD:76/118/45/51/84/105 191:PE:84/127/73/55/72/99 192:PB:64/148/72/63/38/130 193:SD:102/46/56/106/104/78 194:SE:86/52/75/121/75/83
195:SB:81/57/88/134/47/85 195B:MD:81/77/47/114/88/85 196:ME:60/115/55/77/55/130 197:MB:80/74/85/110/45/103 198:PD:73/123/57/53/105/104 199:PE:73/141/68/61/68/104
200:PB:81/137/90/59/49/103 200B:PD:80/138/54/59/99/103 201:SE:49/61/58/141/58/130 202:PB:53/141/73/61/39/130 203:SD:95/64/60/150/111/130
```

## 付録B. 試算の参照実装（`build_habcds.py` の `compute` に相当。この出力が付録A）

```python
from fractions import Fraction as F
from math import floor, ceil
def rnd(x): return floor(F(x) + F(1, 2))
def bst6_of(b3): return 320 + floor((F(b3 - 180) * 58 + 32) / 64)
BETA  = {'Physical': F(13, 20), 'Special': F(7, 20), 'Both': F(1, 2)}
ALPHA = {'Physical': F(7, 10), 'Special': F(3, 10)}
def compute(h, a, d, offense, defense, S, r):        # r は両刀攻撃の物理比（physical_ratio と同じ）
    b3 = h + a + d; b6 = bst6_of(b3); T = b6 - S
    W = F(76, 100) - F(30, 100) * (F(S) - 35) / 95
    w = min(F(80, 100), max(F(45, 100), W + (F(h + d, b3) - F(2, 3))))
    Bulk = rnd(T * w); Off = T - Bulk
    H = rnd(Bulk * F(h, h + 2 * d)); G = Bulk - H
    B = rnd(G * BETA[defense]); D = G - B
    A = rnd(Off * (r if offense == 'Mixed' else ALPHA[offense]))
    if offense == 'Mixed':
        A = max(ceil(F(2, 5) * Off), min(floor(F(3, 5) * Off), A))
    C = Off - A
    return H, A, B, C, D, S
```
