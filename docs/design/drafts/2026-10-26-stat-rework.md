# 能力配分の再設計（BST6 320〜610・攻撃×耐久の直積・俊敏三峰・突破値と気質）— 仕様書

- **状態: 確定（2026-10-26、ユーザー確認済み）**。確認の回答と、それを受けて決めた事項は §8「確定事項」にまとめてある。
- 作成: 2026-10-26
- 対象ブランチ: `claude/mystery-dungeon-roguelike-egnzjt`
- **前提コミット: `980b590`**（Phase 6「制限技12種」完了・コミット済み。回帰スイート 145 項目）。
  本書の試算（付録A・付録C・§3〜§5 の期待値）は、このコミットの `Data/species.json`・`Data/moves.json`・`Data/source/paldb_speed.json` で行った。
  **Phase 6 の内容（`rs_` 技12種、その習得者、`Tools/learnset_rules.py` の `RESTRICTION_LEARNERS`）には触れない。**
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

確認への回答（原文。番号は §8 の項目）:

```
1. 1.aで確定、ただし攻撃側の両刀はどちらか一方の種族値に対して10%以上の開きがあることを必須としたい
2. 2.aで確定 耐久区分のBD振り分けは適当でよい、ただし攻撃両刀種族値のパルは同列付近のBSTと比較して俊敏性が高い傾向にある
   パルに絞る。最低でもBST±4%ゾーンで中央値より足が早い部類のパルに限定する。
3. 3.aで確定
4. 4.bで確定
```

### 0-2. 用語

| 用語 | 意味 |
|---|---|
| H/A/B/C/D/S | HP / 攻撃 / 防御 / 魔力 / 精神 / 俊敏（HABCDS）。物理技は A 対 B、特殊技は C 対 D（現行どおり） |
| 旧HAD (h, a, d) | `species.json` の `legacy_had`（HABCDS 導入前の HP/攻撃/防御）。**配分の入力** |
| BST3 | h + a + d（180〜500、全287種が5の倍数） |
| BST6 | 6能力の合計 = 「合計種族値」。本書で式を変える（§3 S1-a） |
| 序数 (index) | species_id 昇順に並べたときの 0 始まりの番号（`sorted(species, key=species_id)` の位置）。現行の周期規則と同じもの |
| 攻撃区分の候補 | 現行の「型(profile)」を改名したもの。`TRAIT_PHYSICAL` → 物理攻撃、`TRAIT_SPECIAL` → 特殊攻撃、他は序数の 4:3:3 周期で 物理攻撃/特殊攻撃/両刀攻撃。**learnset はこの候補で生成済み**（物理124・特殊84・両刀79） |
| 両刀候補 | 攻撃区分の候補が両刀攻撃の79種 |
| 俊敏ゾーン | ある種族と BST6 が近い他種族の集まり（§3 S1-c 手順2。BST6 ±4%） |
| 攻撃区分 (offense) | 確定した攻撃区分。`Physical` 物理攻撃 / `Special` 特殊攻撃 / `Mixed` 両刀攻撃。両刀候補のうち**俊敏ゾーンの中央値より S が大きい38種だけ**が両刀攻撃、残る41種は物理攻撃か特殊攻撃へ再分類（§3 S1-c） |
| 主側 | 両刀攻撃の、A と C のうち大きい側（物理なら A、特殊なら C）。従側はもう一方 |
| 耐久区分 (defense) | `Physical` 物理耐久（B寄り） / `Special` 特殊耐久（D寄り） / `Both` 両面耐久（B=D） |
| 役割 (role) | 攻撃区分 × 耐久区分 の 9 通り。表記は「物理攻撃/特殊耐久」のように `攻撃区分/耐久区分`。JSON の `role` は `"Physical-Special"` のように `<offense>-<defense>` |
| 物理耐久指数 | H × B |
| 特殊耐久指数 | H × D |
| 総合耐久指数 | H × (B + D)。**役割の「両面耐久」と混同しないこと**（両面耐久は B:D の配分の名前、総合耐久指数は指標の名前） |
| 突破値 (breakthrough, BT) | 構築時に振る値。各能力 0〜25、6能力合計 0〜55。**1ポイント = 実数値 +1**（§3 S2） |
| 気質 (nature) | HP 以外の5能力（A・B・C・D・S）のうち1つを ×1.1、別の1つを ×0.9。無補正もある |
| 実数値 | 対戦で使う値（Lv50 固定）。§3 S2-a の式 |
| round(x) | 四捨五入（x.5 は切り上げ）= floor(x + 1/2)。比率計算は Python `fractions.Fraction` で厳密に行ってから丸める（現行 `build_habcds.py` と同じ） |
| 中央値 | Python `statistics.median` と同じ（個数が偶数なら中央2値の平均。x.5 になり得る） |

### 0-3. 現行の数値（前提コミット `980b590`）

- 種族 287 / 技 651（`rs_` 12種込み）/ 属性10（虚を含む）。
- BST6 = round(360 + (BST3 − 180) × 270/320)、360〜630（タマコッコ(003) 360・リリクイン(186) 503・グランジーラ(203) 630）。
- S は 35〜130、75 付近と 100 付近の二峰（現 `Tools/build_habcds.py` の `P_R`・`P_N`）。
- 型: 物理型124・特殊型84・打ち分け型79（`Tools/learnset_rules.py` `profile_of`）。`species.json` の `role` は5種（`PhysicalWall` 76・`PhysicalAttacker` 48・`SpecialWall` 53・`SpecialAttacker` 31・`Mixed` 79）。
- 優先度 +1 以上の攻撃技は MV_140 ヘッドバット（無・物理・威力40・優先度1）の1つだけ。
- 行動順: 優先度 → **S 種族値**の高い順 → BST6 の低い順 → 乱数（`BattleScheduler.Order`）。
- 回帰スイート `[検証]`: **145**（本書の基準値）。
- Python 検証（`[PASS]` 行の数。すべて ALL PASS）: `Tools/verify_learnset.py` **16**・`Tools/verify_attack_moves.py` **23**・`Tools/verify_npc_teams.py` **9**。
- 直近の 10,000 試合（`docs/design/pvp_spec.md` §28「Phase 5 の計測」）: BST 補正後で 炎 +2.9pt・無 −3.5pt、全属性 ±5pt 以内。
- `pvp_spec.md` の最後の節は §29（制限技12種）。

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

→ 本書では**同じプロパティ名のまま意味を「実数値への直接加算（気質の後）」に置き換える**（§4-11）。全員 0 の現状では、置き換え前後で全実数値が一致する。

---

## 1. 目的（達成条件）

1. **表記**: 「物理型」「特殊型」「打ち分け型」（および「器用型」）という語が、ゲーム UI・Artifact（learnset インスペクタ）・
   `Scripts/`・`Tools/` のコードとコメント・`docs/design/pvp_spec.md` から無くなっている（過去の草案ファイル `docs/design/drafts/*` は記録なので触らない）。
   代わりに「攻撃区分（物理攻撃/特殊攻撃/両刀攻撃）」「耐久区分（物理耐久/特殊耐久/両面耐久）」「役割（攻撃区分/耐久区分）」を使う。
2. **BST6**: `BST6 = round(320 + (BST3 − 180) × 290/320)`。全287種が 320〜610 に収まり、タマコッコ(003) 320・リリクイン(186) 474・グランジーラ(203) 610。
3. **役割の直積**: 攻撃区分3 × 耐久区分3 = **9役割すべてに個体が実在**する（各11種以上。§3 S1-c の内訳表）。
   「物理攻撃/特殊耐久」43種、「特殊攻撃/物理耐久」34種を含む。
4. **両刀攻撃の条件**: 両刀攻撃の38種すべてが (i) 俊敏ゾーンの S の中央値より S が大きく、(ii) max(A, C) ≥ 1.10 × min(A, C) かつ 主側比率 ≤ 3/5。
   両刀候補のうち (i) を満たさない41種は、learnset の多数側の攻撃区分（物理攻撃13・特殊攻撃28）になっている。
5. **learnset と攻撃区分の整合**: 再分類した41種の learnset が、物理攻撃/特殊攻撃の learnset 規則（`verify_learnset.py` の型ごとの検査）を満たす。
   変更は付録C の 114 行の削除だけ（learnset の入れ替えは、これとは別に S1-f の優先技規則による6行の置換のみ。技の追加は無い）。
6. **俊敏の三峰**: S は 35〜130、ヒストグラム（中心±2 の5刻み）が **48–52・73–77・98–102 の三峰**になる（§3 S1-b の表）。
7. **俊敏と耐久**: S が低いほど総合耐久指数が高い。全287種で Pearson 相関 r(S, H(B+D)) ≤ −0.40、BST6 を統制した偏相関 ≤ −0.80、
   S の帯（≤62 / 63–87 / ≥88）ごとの平均総合耐久指数が単調減少（期待値: −0.475 / −0.892 / 11903 > 8732 > 8103）。
8. **俊敏と優先技**: 種族値 S ≥ 95 の個体（111種）の learnset に、優先度 +1 以上の攻撃技（威力>0）が1つも無い。
9. **突破値・気質**: すべての構築個体が 突破値（各0〜25・合計≤55）と気質を持ち、
   **実数値 = floor(素の実数値 × 気質) + 突破値**（HP は気質なし）で対戦に反映される。構築検証・通信・NPC 構築・構築画面に組み込まれている。
   自動で作る構築は役割に応じた決定的な自動配分（§3 S2-d）を持つ。
10. **行動順**: 優先度 → **俊敏の実数値（気質・突破値込み）**の高い順 → BST6 の低い順 → 乱数。
11. 回帰スイートが全フェーズで NG 0、項目数が §7 の値。Python 検証3本が ALL PASS（`[PASS]` 行の数が §5-3 の値）。Artifact 2つが同じ URL で更新。
12. 10,000 試合のランダム構築で、属性別・役割別の BST 補正後勝率差が全て ±5pt 以内（判断材料。§5-4）。

---

## 2. 現状（関係するコード）

### 2-1. データ生成（Python）

| 場所 | 内容 |
|---|---|
| `Tools/build_habcds.py` `bst6_of(bst3)` | `360 + floor(((bst3−180)×54 + 32)/64)` |
| 同 `P_R`, `P_N`, `speed_of(rec)` | ライド: `S = min(130, round(P_R(v_eff) + 運搬Lv))`、非ライド: `q = 運搬Lv + clamp((RunSpeed−250)/400, 0, 1)`, `S = round(P_N(q))` |
| 同 `role_of(profile,h,a,d)`, `PROTO`, `largest_remainder`, `mixed_alloc`, `physical_ratio`, `compute`, `self_check`, `report`, `main` | 現行5役割の配分と自己検査。`main` は species.json のキー順を保って `base_spatk/base_spdef/base_speed/role/legacy_had/speed_source` を `base_def` の後に差し込む |
| `Tools/learnset_rules.py` `profile_of(trait_id, species_index)` | `TRAIT_PHYSICAL`→Physical、`TRAIT_SPECIAL`→Special、他は `PROFILE_CYCLE[index % 10]`（物4:特3:打3）。戻り値 `(profile, 'trait'|'profile')` |
| 同 `CEIL` | キー `('single'|'dual', 'Physical'|'Special'|'Versatile')` の上限表（自属性数・攻撃技合計・他属性の威力上限・他属性1属性あたりの数・変化技の加算） |
| `Tools/generate_learnset.py` 223–249行 | `profile_of` で profile を決め、`major`（多数側の分類。Versatile は**序数の偶奇**: 偶数 Physical・奇数 Special）、`CEIL`、威力天井（Versatile は −20）、`maj_share`（Versatile 0.67／他 0.8）、少数側の威力上限（Versatile は多数側最高の 90%／他 60%）を決める。**本書でも再実行しない** |
| `Tools/verify_learnset.py` | `profile_of` で `prof` を引き、#5（少数側の最高威力 ≤ 多数側の 60%、Versatile は 90%）・#9（自属性数）・#10（攻撃技合計）・#12（他属性1属性あたり）・#13（他属性の威力上限）を型ごとの `CEIL` で検査。コメントに「打ち分け型」「器用型」 |
| `Tools/build_ls_payload.py` | payload に `pf`（型）・`pb`（'特性'/'型'）・`role` を出力 |
| `Tools/artifacts/ls_tail.html` 7–8行 | `PFJA = {Physical:'物理型', Special:'特殊型', Versatile:'打ち分け型'}`、`ROLEJA`（5役割）。`pfs` と `roles` の2つの絞り込みチップ |
| `Tools/artifacts/ls_head.html` 161–162行 | `<div class="chipset" id="pfs">` と `id="roles"` |
| `Tools/generate_npc_teams.py` 37–38行 | `TARGET_BST_MIN = 402`, `TARGET_BST_MAX = 529`（旧BST3 230/380 を旧BST6式で写した値） |
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
| `Scripts/Battle/BattleMatchScene.cs` `RandomTeam`（1085行）, `ForceCarrier`（373行）, `TeamOf`（1121行）, `StripItems`（1131行） | ランダム構築など。`StripItems` は BattleEntry を詰め直す |
| `Scripts/Battle/BattleFlowScene.cs` `BuildTeam`（110行） | フロー確認用の既定編成 |
| `Scripts/Battle/NetTestScene.cs` 59行 | 通信確認用の編成（`new BattleEntry`） |
| `Scripts/Battle/DefaultLoadout.cs` `PickMoves` | 構築の技4つの既定選択（learnset から決定的に選ぶ） |
| `Scripts/UI/Battle/TeamBuildScreen.cs` `SpeciesPanel`（種族値7チップ）, `MovePanel`, `ItemPanel`, `BuildDetail` | 構築画面 |
| `Scripts/UI/Battle/SpeciesPickScreen.cs` 15行コメント「炎の物理型で…」 | 表記の残り |
| `Scripts/UI/Battle/BattleHud.cs` `RefreshRail`（261行〜） | 行動順の目安レール。**現状 `OrderBy(BattleScheduler.Bst)`（BST 昇順）で並べており、Phase 4 以降の行動順（S 優先）とずれている** |
| `Scripts/Turn/AttackAction.cs` | ダメージは `Stats.Attack/Defense/SpAttack/SpDefense`（実数値）を使う。種族値%の平坦加算（きずな・まもり・リーダー系）は `Stats.BaseAtk/BaseSpAtk/BaseDef/BaseSpDef`（種族値）を使う |

### 2-3. 回帰スイート（`Scripts/Battle/BattleTestScene.cs`）で本書が触る項目

- `VerifyEntryWeather`（452行〜）: マグマンダー(105)・ユキツネ(029B) の BST6 コメント。
- `VerifyTurnOrder`（602行〜）: タマコッコ(003)/ブシガエル(009) で `lowBst == 360 && highBst == 432` を見ている。
- `VerifyHabcdsWiring`（632行〜）: #2 ベンチマーク（モコロン同士 物理13・特殊3）、#3 S 順（コメント S74/S47）、#4 BST6（360/432）、
  #5 種族値%加算（`Breakthrough*2` で実数値を合わせる。702–706行）。
- `VerifyHabcdsData`（803行〜）: #1 BST6 範囲 360〜630、#2 打ち分け79種の配分、#3 5役割の整合と内訳、#4 S 35〜130 と基準点。
- 構築の技は `BuildTeam`（learnset の先頭4件。技5つの検証は先頭5件）。スイートが構築に使う種族（`PlayerRoster` 001/004/006/009/002/010、`EnemyRoster` 005/008/007/003/011/012、
  029B・105・198・199）では、本書で learnset から削る技（付録C）は先頭5件に入っていない（確認済み）ので、スイートの技構成は変わらない。
  NPC 8チームを読む検証は、S1-e の技2件の置き換え後の `npc_teams.json` を読む。

---

## 3. 変更内容

フェーズ S1〜S5 の順に実装する（§7 に完了条件）。ファイル名は repo 相対。

### Phase S1 — データ（BST6・俊敏・役割・配分・learnset の整合・優先技の制約・表記の改名）

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
- **S は役割（攻撃区分・耐久区分）に依存しない**。S1-c の俊敏条件は S を入力に使うだけで、S を変えない。

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

攻撃区分は次の3段で決める。**learnset には依存しない**（BST6・S・特性・序数だけで決まる）。

1. **攻撃区分の候補** = `base_offense_of(trait_id, index)`（現行 `profile_of` の改名。S1-f）。
   `TRAIT_PHYSICAL` → Physical、`TRAIT_SPECIAL` → Special、他は `OFFENSE_CYCLE[index % 10]`（物4:特3:両3）。内訳 物理124・特殊84・**両刀候補79**。
2. **俊敏条件**（両刀候補だけに当てる）:
   - 俊敏ゾーン Z(i) = 自分以外の全種族 j のうち **|BST6_j − BST6_i| ≤ 0.04 × BST6_i**（両端を含む。整数で書くと `25 × |BST6_j − BST6_i| ≤ BST6_i`）を満たすもの。
     BST6 は S1-a の新しい値。母集団は全287種（攻撃区分を問わない）。
   - Z(i) が **10種未満**のときは、自分以外の全種族を |BST6_j − BST6_i| の小さい順に並べ、10番目の距離以下の種族すべてを Z(i) とする（10番目と同じ距離の種族は全て含む）。
   - **S_i > median{S_j : j ∈ Z(i)}**（中央値は `statistics.median`。**中央値と等しい場合は満たさない**）なら俊敏条件を満たす。
   - ゾーンの大きさ（全287種）: 拡張後の最小 10（203 グランジーラ）。±4% で10種未満になるのは 203, 003, 035, 200B, 200, 030, 192, 198, 199 の9種で、
     そのうち両刀候補は 030（S77、中央値73 → 満たす）と 035（S35 → 満たさない）。拡張しなくてもこの2種の判定は同じ。
3. **確定**:
   - 候補が Physical/Special → そのまま。`offense_source` = `"trait"`（特性で決まった）または `"cycle"`（周期で決まった）。
   - 両刀候補で俊敏条件を満たす → **Mixed（38種）**。`offense_source = "cycle"`。
   - 両刀候補で俊敏条件を満たさない → **`mixed_side_of(index)`**（序数が偶数なら Physical、奇数なら Special）へ再分類（**41種: 物理攻撃13・特殊攻撃28**）。`offense_source = "speed"`。
     `mixed_side_of` は `generate_learnset.py` が両刀候補の learnset を作ったときの多数側（`major`）と同じ規則で、
     **両刀候補79種すべてで learnset の攻撃技の多数側（物理/特殊の数。`rs_` と `SIGNATURE` を除く）と一致し、同数の種は無い**。旧 HABCDS の A と C の大小とも41種すべてで一致する（確認済み）。
4. **耐久区分** = 次の順で決める（`Tools/learnset_rules.py` に `defense_of(trait_id, index)` を新設）:
   - 特性 `hard_armor`（物理技を受けるとき防御2倍）→ `Physical`。特性 `hard_block`（特殊技を受けるとき精神2倍）→ `Special`（各6種）。
   - それ以外は `DEFENSE_CYCLE = ['Physical', 'Special', 'Both']` を `index % 3` で引く。
5. **役割** = `f"{offense}-{defense}"`。表示は `攻撃区分/耐久区分`（例「物理攻撃/特殊耐久」）。

両刀攻撃の38種（id(BST6, S > ゾーン中央値)）:

```
007(365,S76>73) 007B(370,S76>74) 016(347,S73>60) 021B(424,S98>82) 022(343,S73>60) 023(356,S73>60) 030(329,S77>73)
036(411,S98>81) 041B(451,S101>98) 042(343,S74>60) 048B(365,S76>73) 064B(433,S99>86) 073(347,S76>60) 073B(347,S74>60)
083B(442,S101>98) 084(415,S98>81.5) 084B(420,S98>81) 094B(474,S101>99) 096(429,S123>82) 100(365,S75>73) 100B(370,S75>74)
101(424,S101>82) 108(438,S98>90) 112B(429,S100>82) 121(474,S103>99) 127(388,S99>75) 133(374,S77>74) 138B(470,S101>100)
146(483,S100>99) 154(465,S101>99) 161(483,S101>99) 169B(402,S103>80) 171(483,S130>99) 178(388,S77>75) 188(456,S105>98)
188B(460,S107>98) 196(492,S130>99) 197(497,S103>100.5)
```

- 両刀攻撃の S: 73〜77 が14種（BST6 329〜388 の低い帯。周りが 50 の峰の個体なので 75 の峰でも中央値を超える）、98〜130 が24種。88〜97 は0種。
- 主側: 物理（A > C）12種、特殊（C > A）26種。
- 再分類された41種の一覧（BST6・S・ゾーン中央値・再分類先・learnset から削る技）は付録C。

内訳（期待値。略号は付録A と同じ: P=物理攻撃 S=特殊攻撃 M=両刀攻撃、B=物理耐久 D=特殊耐久 E=両面耐久）:

| | 物理耐久 B | 特殊耐久 D | 両面耐久 E | 計 |
|---|---|---|---|---|
| 物理攻撃 P | 51 | **43**（物理攻撃/特殊耐久） | 43 | 137 |
| 特殊攻撃 S | **34**（特殊攻撃/物理耐久） | 42 | 36 | 112 |
| 両刀攻撃 M | 13 | 14 | 11 | 38 |
| 計 | 98 | 99 | 90 | 287 |

（耐久区分の列の計は再分類の前後で同じ。再分類は攻撃区分だけを動かす。）

#### S1-d. 配分（`Tools/build_habcds.py` `compute` を置き換え）

1種ごとに、すべて `Fraction` で計算する。

```
入力: 旧HAD h, a, d（legacy_had）、BST3 = h+a+d、offense、defense、index、learnset
1. BST6 = S1-a の式
2. S    = S1-b の式（speed_of）
3. T    = BST6 − S                                   # H・A・B・C・D に配る予算
4. 耐久の取り分 w:
     W(S) = 76/100 − (30/100) × (S − 35) / 95         # S=35 → 0.76、S=130 → 0.46（S は常に 35〜130）
     φ    = (h + d) / BST3                            # 旧HADの耐久傾向（平均 ≒ 2/3）
     w    = clamp(W(S) + (φ − 2/3), 45/100, 80/100)
5. Bulk = round(T × w)、Off = T − Bulk                # Bulk = H+B+D、Off = A+C
6. H = round(Bulk × h / (h + 2d))、G = Bulk − H        # G = B+D。H:(B+D) は旧 h:2d の比（全役割共通）
7. B = round(G × β)、D = G − B
     β = 13/20（物理耐久）, 7/20（特殊耐久）, 1/2（両面耐久）    # B:D = 65:35 / 35:65 / 50:50
8. 物理攻撃・特殊攻撃:
     A = round(Off × α)、C = Off − A
     α = 7/10（物理攻撃）, 3/10（特殊攻撃）                     # A:C = 70:30 / 30:70
   両刀攻撃:
     主側 = mixed_side_of(index)（Physical なら A、Special なら C が主側の値 X）
     p, q = learnset の攻撃技（power > 0）の Physical / Special の数（id が rs_ で始まる技と SIGNATURE の技は数えない）
     share = （主側の分類の数）/ (p + q)          （p + q = 0 なら 1/2）
     X = round(Off × clamp(share, 11/21, 3/5))
     X = clamp(X, ceil(11/21 × Off), floor(3/5 × Off))     # 整数でも 開き10%以上・主側60%以下 を保証
     主側 = X、従側 = Off − X
9. 出力 H, A, B, C, D, S（合計は BST6 に厳密に一致する）
```

- **両刀攻撃の「10%以上の開き」**: X ≥ 11/21 × Off ⇔ 10X ≥ 11(Off − X) ⇔ **max(A,C) ≥ 1.10 × min(A,C)**。上限 X ≤ 3/5 × Off（= 開き 50%、A:C = 60:40）は現行の「40:60〜60:40」を引き継ぐ。
  したがって両刀攻撃の A:C は **主側比率 ∈ [11/21, 3/5] ≒ [0.524, 0.600]**、A/(A+C) ∈ [0.400, 0.476] ∪ [0.524, 0.600]。50:50 付近（0.476〜0.524）は出ない。
  実データでは38種すべてで learnset の share が 0.65〜0.75 なので上限 3/5 に張り付き、主側比率 0.592〜0.600、max/min = 1.45〜1.50（**下限 11/21 は現データでは効かない**が、規則として入れる）。
- **「S が低いほど耐久指数が高い」の組み込み方**: S を先に決めて残り T を配る現行方式はそのまま。そのうえで耐久の取り分 w を S の減少関数にした。
  S が低い個体は (a) T が大きく、かつ (b) T のうち耐久に回る割合も大きい、の二重で耐久が厚くなる。
  S が高い個体は w が 0.46 前後まで下がり、**耐久がペラペラ**になり得る（例: 172 ダイナモフ H46/B50/D49・S101、096B チルバード H53/B44/D43・S123）。
- H・B・D は攻撃区分に依存しない（手順4〜7に offense が出てこない）。**再分類は A と C だけを動かす**ので、§1-7 の相関・帯別平均は再分類の有無で変わらない。
- 旧HADの h:d の比は H:(B+D) に、旧HADの耐久傾向 φ は w の補正に残る（元の Palworld の個性を消さないため）。
- 現行の最大剰余法（`largest_remainder`・`TIE_ORDER`）と `PROTO`・`role_of`・`mixed_alloc`・`physical_ratio` は削除する（2段の四捨五入で合計が厳密に合うので不要）。
- 現行の両刀（打ち分け型）の「H×0.8 = B+D」規則は**廃止**（§8 確定事項2）。手順6は全役割共通。

**結果の要約**（全287種）: H 41〜104（平均68.1）、A 18〜148（67.6）、B 29〜116（66.5）、C 21〜150（64.3）、D 31〜134（66.8）、S 35〜130（82.0）。
全能力の最小は 18（040 ミルカルビの A）。w が下限/上限に張り付くのは 6 種。
B/(B+D): 物理耐久 0.646〜0.655、特殊耐久 0.346〜0.354、両面耐久 0.500〜0.506。
A/(A+C): 物理攻撃 0.696〜0.706、特殊攻撃 0.294〜0.307、両刀攻撃 主側物理 0.593〜0.600（12種）・主側特殊 0.400〜0.408（26種）。

役割別の平均 H/A/B/C/D/S:

| 役割 | 種 | H | A | B | C | D | S |
|---|---|---|---|---|---|---|---|
| 物理攻撃/物理耐久 | 51 | 68.7 | 91.6 | 86.7 | 39.2 | 46.7 | 80.9 |
| 物理攻撃/特殊耐久 | 43 | 68.0 | 96.1 | 46.3 | 41.2 | 86.1 | 83.3 |
| 物理攻撃/両面耐久 | 43 | 68.9 | 87.8 | 67.8 | 37.5 | 67.3 | 77.0 |
| 特殊攻撃/物理耐久 | 34 | 67.6 | 39.0 | 87.6 | 91.0 | 47.1 | 77.2 |
| 特殊攻撃/特殊耐久 | 42 | 72.5 | 38.6 | 50.2 | 90.0 | 93.3 | 79.3 |
| 特殊攻撃/両面耐久 | 36 | 66.3 | 39.9 | 67.5 | 93.2 | 67.1 | 83.7 |
| 両刀攻撃/物理耐久 | 13 | 68.5 | 63.7 | 77.5 | 83.4 | 41.6 | 96.8 |
| 両刀攻撃/特殊耐久 | 14 | 59.5 | 64.2 | 41.4 | 68.1 | 76.9 | 89.5 |
| 両刀攻撃/両面耐久 | 11 | 64.5 | 64.9 | 58.7 | 72.6 | 58.1 | 94.5 |

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
| 007 | ルミカイト | 230 | 365 | 両刀攻撃/特殊耐久（主側 特殊） | 64/40/44/58/83/76 | 8128 |
| 008 | ミルフィー | 215 | 352 | 特殊攻撃/物理耐久（両刀候補から再分類） | 71/25/99/57/53/47 | 10792 |
| 009 | ブシガエル | 265 | 397 | 物理攻撃/特殊耐久 | 62/92/46/39/85/73 | 8122 |
| 029B | ユキツネ | 215 | 352 | 物理攻撃/両面耐久（両刀候補から再分類） | 64/69/69/30/69/51 | 8832 |
| 034 | ベリゴート | 260 | 393 | 特殊攻撃/物理耐久（hard_armor） | 95/23/111/54/59/51 | 16150 |
| 096 | ライバード | 300 | 429 | 両刀攻撃/特殊耐久（主側 特殊） | 55/68/29/100/54/123 | 4565 |
| 105 | マグマンダー | 350 | 474 | 特殊攻撃/両面耐久 | 66/52/70/121/69/96 | 9174 |
| 130B | ライトロット | 315 | 442 | 特殊攻撃/両面耐久 | 51/50/47/118/46/130 | 4743 |
| 172 | ダイナモフ | 240 | 374 | 物理攻撃/両面耐久 | 46/90/50/38/49/101 | 4554 |
| 186 | リリクイン | 350 | 474 | 特殊攻撃/特殊耐久 | 88/40/70/94/130/52 | 17600 |
| 196 | ゼノドラン | 370 | 492 | 両刀攻撃/両面耐久（主側 物理） | 60/115/55/77/55/130 | 6600 |
| 199 | ベイントール | 395 | 515 | 物理攻撃/両面耐久 | 73/141/68/61/68/104 | 9928 |
| 201 | ネプティオス | 375 | 497 | 特殊攻撃/両面耐久 | 49/61/58/141/58/130 | 5684 |
| 203 | グランジーラ | 500 | 610 | 特殊攻撃/特殊耐久 | 95/64/60/150/111/130 | 16245 |

ユーザー例との対応: 「耐久ペラペラ・高速」= 130B ライトロット、172 ダイナモフ、096 ライバード（両刀攻撃。55/68/29/100/54/123）など。
「足は速くないが物理受け気質で特殊技を返す」= 034 ベリゴート（S51、B111、C54）、008 ミルフィー（S47、B99、C57）、125 フワフラワ（86/31/99/73/53/51）、176 ラピエール（72/43/94/100/51/78）など。

#### S1-e. 再分類した41種の learnset を攻撃区分に合わせる（新規 `Tools/conform_offense_learnsets.py`）

再分類した41種の learnset は両刀候補の規則（`maj_share` 0.67、少数側の最高威力は多数側の 90% まで、攻撃技の数の上限が大きい）で生成されている。
これを**物理攻撃/特殊攻撃の learnset 規則**（`verify_learnset.py` が型ごとに見ている #5・#9・#10・#12・#13）に合わせる。**技は削るだけで、足さない・入れ替えない。**

現データで新しい区分の規則を当てると、41種すべてが #5（少数側の最高威力 ≤ 多数側最高の 60%）、38種が #10（攻撃技合計）、3種が #12（他属性1属性あたり2種まで）、1種が #9（自属性数）に違反する。

対象: `offense_source == "speed"` の種族（41種）。`new` = その種の `offense`（Physical/Special）、`C = CEIL[('dual' if 複合属性 else 'single', new)]`。
上限 `ot = round_half_up(C.own[0] + (C.own[1] − C.own[0]) × span)`、`ac = round_half_up(C.atk[0] + (C.atk[1] − C.atk[0]) × span)`、`span = (BST3 − 180)/320`（BST3 は `legacy_had`。`verify_learnset.py` と同じ式）、`per = C.off_per`。

「攻撃技」= `power > 0` の learnset 行。「適応技」= `verify_learnset.py` の `adapted(s)` と同じ集合。
**削ってはいけない行**（その時点の learnset で判定）:
- id が `rs_` で始まる技、`SIGNATURE` の技、その種の特性の `TRAIT_NAMED` の技。
- 種族が無属性でなく、その技が無属性の攻撃技で、無属性の攻撃技（`rs_` を除く）が残り2つ以下のとき。
- 適応技の候補が6種以上の特性で、その技が適応技で、learnset の適応技が残り6つ以下のとき。
- 変化技（power 0）はそもそも対象外。

並べ方 `key(行) = (分類 == new, power, move_id)`（少数側が先、威力の低い順、id 順）。

手順（各段の「自属性」「他属性」は `verify_learnset.py` と同じく、適応技・`SIGNATURE`・`rs_` を除いて数える）:
1. **少数側の威力**: 多数側（分類 == new）の攻撃技（`rs_`・`SIGNATURE` を除く）の最高威力を mj とする。
   分類 ≠ new の攻撃技（`rs_`・`SIGNATURE` を除く）で power > 0.6 × mj のものを、learnset の並び順に、削ってよければ削る。
2. **自属性の数**: 自属性の攻撃技が `ot` を超える間、自属性の攻撃技のうち削ってよいものを key 順で先頭から1つずつ削る。
3. **他属性1属性あたり**: 他属性（無属性を含む）の攻撃技を属性ごとに数え、`per` を超える属性がある間、
   超えている属性のうち learnset で最初に現れる属性について、その属性の削ってよい行を key 順で1つ削る。
4. **攻撃技合計**: `rs_` を除く攻撃技が `ac` を超える間、他属性の攻撃技のうち削ってよいものを key 順で1つ削る（無ければ全攻撃技から）。
5. 最後に `verify_learnset.py` と同じ規則（S1-g の新しい `prof`）で41種を検査し、違反があれば非0終了して報告する。

- 期待結果: **41種で計114行を削る**（手順1で106行、手順3で2行、手順4で6行、手順2で0行）。1種あたり2〜5行。削る行の全一覧は付録C（実装後の差分と行単位で一致すること）。
  削った後の物理:特殊の数は付録C の右端の列。削った結果、習得者が0種になる技は無い（81種の技の習得者数が減る）。
- 削る技に MV_140（ヘッドバット）は含まれない。41種のうち S ≥ 95 は 106 ココヤンバ（S98）だけで、MV_140 を持たない。S1-f の置換と重ならない。
- **NPC 構築の追従**: 同じスクリプトの最後で `Data/npc_teams.json` を見て、learnset から消えた技を持つ個体があれば、
  「同じ属性・分類 == その種の offense の攻撃技のうち、learnset にあり同じ個体の技に無いもので、威力最大（同威力は id 順）」に置き換える（無ければ分類 == offense の攻撃技で威力最大）。
  編成・並び・持ち物・他の技・`total_bst` は変えない。期待値は2件:
  - `npc_ground` の 113 グラクレス: `gap_ground_s_105` → **`gap_ground_p_100`**（地・物理・100）
  - `npc_dragon` の 187 ボルゼクス: `gap_dragon_p_105` → **`mvn_086`**（竜・特殊・55）
- 冪等: 2回目の実行では何も削らない・置き換えない。
- `BattleTestScene` が構築に使う種族（§2-3）の learnset の先頭5件に削る技は入っていないので、スイートの技構成は変わらない
  （先頭5件に削る技が入るのは 029 キツネビ・057 マスクロウの `gap_electric_p_15` と 089 フラリーナの `gap_ground_p_15` だけで、いずれもスイートの構築には使われない）。

#### S1-f. 俊敏 95 以上は優先度 +1 以上の攻撃技を覚えない

- **規則（硬い規則として実装）**: 種族値 `base_speed ≥ 95` の個体の learnset に、`priority ≥ 1` かつ `power > 0` の技を置かない。
  変化技（power 0）は対象外。突破値・気質で実数値が 95 を超えても learnset は変わらない（learnset は種族のもの）。
- `Tools/learnset_rules.py` に定数と判定を置く: `FAST_SPEED_MIN = 95`、`def fast_blocks(species, move): return species['base_speed'] >= FAST_SPEED_MIN and move.get('priority', 0) >= 1 and move['power'] > 0`。
- 優先度 +1 以上の攻撃技は MV_140 ヘッドバットだけ。習得者21種のうち、新しい S で 95 以上になるのは次の6種（全てライド。S は現行と同じ値。攻撃区分は 127 が両刀攻撃、他は物理攻撃で、いずれも再分類の対象外）:

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
  候補が無ければ置き換えずに非0終了して報告。最後に「S≥95 の個体に優先度+1以上の攻撃技が無い」を検査し、違反があれば非0終了。
- category が変わらないので、両刀攻撃の share（S1-d 手順8）は置き換え前後で同じ（127 は両刀攻撃だが、物理→物理の置換）。
- `Tools/generate_learnset.py`（再実行しない）にも同じ規則を入れる: 攻撃技の候補プールを作る所で `fast_blocks(s, m)` の技を除く（将来の再生成で規則が崩れないように）。
- `Tools/verify_learnset.py` に検査 #17 を追加: 「S≥95 の個体は優先度+1以上の攻撃技を持たない」（`fast_blocks` を import して判定、違反0件で PASS）。

**実行順**: `build_habcds.py` → `conform_offense_learnsets.py` → `apply_fast_priority_rule.py` → `build_habcds.py`（2回目。値が変わらないこと）→ `generate_npc_teams.py --refresh-bst`（S1-i）。
`build_habcds.py` の値（攻撃区分・HABCDS）は再分類種の learnset に依存せず（攻撃区分は learnset を見ない。再分類種の A:C は α）、
両刀攻撃の share は S1-e（両刀攻撃には触れない）でも S1-f（分類を変えない）でも変わらないので、2回目は差分なしになる。

#### S1-g. 表記の改名と、learnset 系ツールが見る攻撃区分

- `Tools/learnset_rules.py`:
  - `profile_of` → **`base_offense_of(trait_id, species_index)`**。戻り値 `('Physical'|'Special'|'Mixed', 'trait'|'cycle')`（`'Versatile'` → `'Mixed'`、`'profile'` → `'cycle'`）。
  - `PROFILE_CYCLE` → `OFFENSE_CYCLE`（中身は `'Versatile'` を `'Mixed'` にするだけ。並び・周期は同じ）。
  - `CEIL` のキーの `'Versatile'` → `'Mixed'`（値は変えない）。
  - 新設: `mixed_side_of(species_index)`（偶数 → `'Physical'`、奇数 → `'Special'`）、
    `MIXED_ZONE = Fraction(4, 100)`、`MIXED_ZONE_MIN = 10`、`MIXED_MAIN_MIN = Fraction(11, 21)`、`MIXED_MAIN_MAX = Fraction(3, 5)`、
    `speed_zone(sid, table)`・`offense_of(sid, trait_id, species_index, table)`（`table` は `{species_id: (BST6, S)}`。付録B の `zone`・`offense_final` と同じ処理。戻り値 `(offense, offense_source)`）、
    `offense_from_species(s, species_index)`（`s.get('offense')` があればそれ、無ければ `base_offense_of(...)[0]`。learnset 系ツールが使う）、
    `DEFENSE_CYCLE`、`defense_of`、`FAST_SPEED_MIN`、`fast_blocks`、ラベル表 `OFFENSE_JA = {'Physical':'物理攻撃','Special':'特殊攻撃','Mixed':'両刀攻撃'}`、
    `DEFENSE_JA = {'Physical':'物理耐久','Special':'特殊耐久','Both':'両面耐久'}`。
  - コメントの「物理型/特殊型/打ち分け型」を「攻撃区分（物理攻撃/特殊攻撃/両刀攻撃）」に。
- **`Tools/verify_learnset.py`**: `prof[...]` を `profile_of(...)` から **`offense_from_species(x, i)`**（= S1 後は species.json の `offense`）に変える。`r = 0.9 if prof == 'Mixed' else 0.6`。
  比較文字列 `'Versatile'` → `'Mixed'`、コメントの「打ち分け型」「器用型」を「両刀攻撃」に。検査を2つ追加:
  #17（S1-f）と **#18「物理攻撃/特殊攻撃の個体は、learnset の攻撃技（`rs_`・`SIGNATURE` を除く）の多数側の分類が攻撃区分と一致する（攻撃技0の個体は除く）」**。
  - この変更は**緩和ではない**。再分類した41種に、他の物理攻撃/特殊攻撃の249種と同じ規則を当てるだけで、他の246種（候補と確定が同じ）の検査内容は変わらない。
    41種にとっては #5（90% → 60%）・#9/#10（上限が小さい表）・#12（単属性 3 → 2）が厳しくなり、#13（他属性の威力上限 90/80 → 物理 110/100・特殊 100/90）だけが広がるが、
    これはその区分の全種族に共通の値で、41種の learnset は削るだけなので他属性の最高威力は現行のまま（90/80 以内）。
- **`Tools/generate_learnset.py`**（再実行しない）: profile を `offense_from_species(s, species_index)` で決める（S1 後は再分類種も物理攻撃/特殊攻撃の規則で生成される）。
  両刀攻撃の `major` は `mixed_side_of(species_index)`（現行の偶奇規則と同じ）。`'Versatile'` → `'Mixed'`。S1-f の `fast_blocks` 除外を入れる。`python3 -m py_compile` で構文だけ確かめる。
- `Tools/build_ls_payload.py`・`Tools/build_habcds.py`・`Tools/add_void_type.py`（コメントのみ）: import と比較文字列を新しい名前・`'Mixed'` に。コメントの「打ち分け型」「器用型」「物理型」「特殊型」を「両刀攻撃」「物理攻撃」「特殊攻撃」に。
- `Scripts/UI/Battle/SpeciesPickScreen.cs` 15行のコメント「炎の物理型」→「炎の物理攻撃」。
- `Scripts/Battle/BattleTestScene.cs` のコメント（828行付近「物理/特殊型208種」）→ 新しい検証に置き換わる（S1-i）。
- `docs/design/pvp_spec.md` 96行「特殊型の主力」→「特殊攻撃の個体の主力」。他に `grep -rn "物理型\|特殊型\|打ち分け型\|器用型" Scripts Tools docs/design/pvp_spec.md`
  で出る箇所を全て直す（`Tools/artifacts/*.html` は S4 で直す。`docs/design/drafts/` は対象外）。

#### S1-h. `species.json` のキー（`Tools/build_habcds.py` `main`）

`base_def` の直後の並びを次にする（既存キーの順は保つ）:
`base_spatk, base_spdef, base_speed, offense, offense_source, defense, role, legacy_had, speed_source`

| キー | 値 |
|---|---|
| `offense` | `"Physical"` / `"Special"` / `"Mixed"` |
| `offense_source` | `"trait"`（特性で決まった）/ `"cycle"`（周期で決まり、そのまま確定）/ `"speed"`（両刀候補から俊敏条件で再分類） |
| `defense` | `"Physical"` / `"Special"` / `"Both"` |
| `role` | `"<offense>-<defense>"`（例 `"Physical-Special"`）。**旧値（`PhysicalWall` 等）は残さない** |
| `legacy_had`, `speed_source` | 現行どおり（`legacy_had` を入力に使う＝冪等） |

`build_habcds.py` の処理順: (1) 全287種の BST6 と S を先に求めて `table` を作る → (2) `offense_of`・`defense_of` → (3) S1-d の配分。

`build_habcds.py` の自己検査（違反があれば非0終了）を次に置き換える:
6能力の合計 = BST6 式 / 全能力 ≥ 10 / S ∈ [35,130] で最小35・最大130 が実在 / 基準点 199=104・130B=130・196=130・201=130 /
BST6 の基準 003=320・186=474・203=610 / 9役割すべて実在（各10種以上）/ `role == offense + '-' + defense` /
`offense_source` の内訳 trait・cycle・speed の合計 287、speed は41種（物理13・特殊28）、両刀攻撃は38種 /
両刀攻撃は全種で俊敏条件（S1-c 手順2）を満たし、`offense_source == "speed"` の種は全て満たさない /
B = round(G×β)・A = round(Off×α)・両刀攻撃の X（S1-d 手順8）を再計算して一致 / 両刀攻撃は 10·max(A,C) ≥ 11·min(A,C) かつ 5·max(A,C) ≤ 3·(A+C) /
三峰の判定（S1-b）/ r(S, H(B+D)) ≤ −0.40 かつ偏相関 ≤ −0.80 / S ≥ 95 の個体に優先度+1以上の攻撃技が無い
（**この項目だけは `apply_fast_priority_rule.py` 実行前の初回は違反するので、警告として表示し終了コードには含めない**。
`apply_fast_priority_rule.py` の側で同じ検査を最後に行い、違反があれば非0終了する）。
`report` は S1-b のヒストグラム・S1-c の内訳表と両刀攻撃38種・再分類41種の一覧・S1-d の役割別平均と相関・帯別平均を出す。

#### S1-i. その他のデータ・C#（Phase S1 の範囲）

- `Scripts/Species/SpeciesDatabase.cs` `SpeciesJson`: `offense`・`offense_source`・`defense` を追加（string、既定 ""）。`SpeciesData` に `Offense`・`OffenseSource`・`Defense`（string）と
  `public string RoleLabel`（`OFFENSE_JA/DEFENSE_JA` と同じ和名で「物理攻撃/特殊耐久」を返す。未設定なら ""）。`Role` は残す。
- `Scripts/Battle/NpcOpponent.cs` 24–25行: `Bst6Weight = 32f / 29f`、コメントの式を新しい式に。
- `Tools/generate_npc_teams.py`: `TARGET_BST_MIN = 365`・`TARGET_BST_MAX = 501`（旧BST3 230/380 を新式で写した値）とコメントの式。
  **編成は再生成しない**。`--refresh-bst` オプションを足し、`Data/npc_teams.json` の各チームの `total_bst` だけを新しい BST6 の合計で書き換える（編成・並び・他のキーは変えない）。
  技の2件の置き換えは S1-e のスクリプトが行う。`Tools/verify_npc_teams.py` が ALL PASS であること。
- `Scripts/Battle/BattleTestScene.cs`（項目の書き換えと追加。S1 完了で **148**）:
  - `VerifyHabcdsData` #1: 「全て10以上・合計 **320〜610**（003=320/186=474/203=610）」。
  - `VerifyHabcdsData` #2（打ち分け79種の検査を置き換え）: 「配分比率が区分どおり」— 全287種で |B/(B+D) − β| ≤ 0.5/(B+D)、
    物理攻撃・特殊攻撃は |A/(A+C) − α| ≤ 0.5/(A+C)、両刀攻撃は 10·max(A,C) ≥ 11·min(A,C) かつ 5·max(A,C) ≤ 3·(A+C)（β・α は S1-d）。
  - `VerifyHabcdsData` #3（5役割の検査を置き換え）: 「9役割が全て実在し内訳が表どおり」— S1-c の9つの数、かつ全種で `Role == Offense + "-" + Defense`。
  - `VerifyHabcdsData` #4: 「俊敏35〜130・基準点・三峰」— 最小35・最大130、基準点4種、S1-b の三峰の判定（c(50)=23, c(75)=62, c(100)=68）。
  - **追加** `[検証] 俊敏95以上は優先度+1以上の攻撃技を習得しない`: S≥95 の111種で該当技0件（件数と 127 ヤクモマルが MV_141 を持つことを括弧内に）。
  - **追加** `[検証] 俊敏が低いほど総合耐久指数が高い`: r(S, H(B+D)) ≤ −0.40 かつ帯別平均（≤62 / 63–87 / ≥88）が単調減少（実測値を括弧内に）。
  - **追加** `[検証] 両刀攻撃は同じ帯のBST6より速い`: `Offense == "Mixed"` が38種で、全種が S > 俊敏ゾーンの中央値（S1-c 手順2 を C# で同じく計算。
    整数で `25 × |ΔBST6| ≤ BST6_i`、10種未満は拡張、中央値は偶数個なら `2·S > s[k−1] + s[k]` で比べる）、
    かつ `OffenseSource == "speed"` が41種で全種 S ≤ 中央値・`Offense` が Physical/Special（括弧内に 38/41 と最小の差 S − 中央値）。
  - `VerifyTurnOrder`: `lowBst == 320 && highBst == 397`。コメント「S47・BST6 360」→「S50・BST6 320」、「S74・BST6 432」→「S73・BST6 397」。
  - `VerifyEntryWeather`: コメント「BST6 503 / 390」→「474 / 352」（期待値は変わらない: マグマンダー側の BST6 合計が大きいので、ゆきが残る）。
  - `VerifyHabcdsWiring` #2: モコロン(58/69/75/30/41/74)同士で **物理12・特殊5**（攻74 防80 → 12、魔35 精46 → 5）。直接ベンチマーク 13 は不変。
    #3 のコメント（S73 と S50）、#4 の期待値 **320 / 397**。
  - 他の項目（持ち物倍率等）は能力値が変わるので括弧内の数値は変わってよい（敵側の 008 ミルフィーは特殊攻撃/物理耐久 71/25/99/57/53/47 になる）。**OK/NG が変わったら報告**。

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
- 既存の `BreakthroughXxx` は**名前を変えず意味だけ変える**（§4-11）。クラス冒頭と各プロパティのコメントを「構築で振る実数値への加算（気質の後）。各0〜25・合計55以下は構築側で検証」に書き換える。
- `SpeciesData.cs` 24行付近・9行付近のコメント（「IV-like Breakthrough」）も同じ意味に直す。
- 種族値%の平坦加算（`AttackAction` のきずな・まもり・リーダー系）は**種族値（`BaseAtk` 等）のまま**。突破値・気質は見ない（§4-12）。
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
    public static int RealStat(int baseStat, int level, int bt, int natureMul10);   // S2-a の式（HP 以外）。EntityStats からも使う
}
```

`Validate()` の規則（違反ごとに1行の日本語メッセージ）:
- 各突破値が 0 以上 25 以下。
- 合計が 55 以下（**55 未満も可**。余りを残してよい。§4-13）。
- 気質: (None, None) か、(X, Y) で X ≠ Y かつ両方 None でない。片方だけ None、または X == Y は不可。

#### S2-c. 構築データ（`Scripts/Battle/BattleTeam.cs` ほか）

- `BattleEntry` に `public StatSpread Spread { get; set; } = StatSpread.Zero();`。
- `BattleTeam.Validate()`: 各個体について `e.Spread?.Validate()` の各行を `"{who}: "` 付きで errors に追加（`Spread == null` は「突破値・気質が未設定」エラー）。
- `BattleTeam.SetSpecies(slot, id)`: 種族を差し替えたら `Spread = StatSpread.Auto(新種族)`。
- 新規 `public bool SetSpread(int slot, StatSpread s)`: 範囲外の slot は false。値の検証は `Validate()` に任せ、ここでは代入だけ（画面が途中の状態を持てるように）。
- `BattlePal.ApplyLoadout(entry)`: 技・持ち物の後に `Stats.ApplySpread(entry.Spread ?? StatSpread.Zero())`。`_Ready` で `Stats.Level = 50` の後に呼ばれる順は変えない。
- **既定値（自動配分）を入れる箇所**（`new BattleEntry` している全箇所。`grep -rn "new BattleEntry" Scripts` で漏れを確認）:
  `BattleMatchScene.RandomTeam`（1085行）・`ForceCarrier`（373行）・`TeamOf`（1121行）・`MetaScenario.StrongestTeam`（91行）・`TacticalBuilder`（278行）・
  `BattleFlowScene.BuildTeam`（110行）・`NetTestScene`（59行）・`NpcTeamDatabase.Load`（76行。JSON に指定が無いとき）は `Spread = StatSpread.Auto(species)`。
  `BattleMatchScene.StripItems`（1131行）は**元の `Spread` を `Clone()` して引き継ぐ**。
  `BattleNetwork.DecodeFull`（223行）は受信値（無ければ Zero）。
  **`BattleTestScene` の構築は全て `Zero()` のまま**（ダメージを測る既存の検証の期待値を、種族値以外の理由で動かさないため）。
- **`StatSpread.Auto` は乱数を使わない**ので、`RandomTeam`・`MetaScenario` の乱数列の消費は変わらない（§4-15）。

#### S2-d. 自動配分 `StatSpread.Auto(SpeciesData sp)`

決定的（同じ種族なら常に同じ）。合計は常に 55。`sp.Offense`（S1-h の確定値）を使う。

1. 主攻撃 P: 攻撃区分 Physical → Atk、Special → SpAtk、Mixed → `BaseAtk ≥ BaseSpAtk` なら Atk、でなければ SpAtk
   （両刀攻撃は A と C に10%以上の開きがあるので同値は起きない。主側と一致する）。
2. 気質: Physical → ↑Atk ↓SpAtk、Special → ↑SpAtk ↓Atk、**Mixed → 無補正**（§4-15。両方の攻撃技を使うので、どちらの攻撃を削っても使う技が弱くなる）。
3. 突破値: P に 25。種族値 S ≥ 88（100 の峰の側）なら Speed に 25、そうでなければ Hp に 25。
   残り 5 を耐久区分の能力へ: Physical → Def、Special → SpDef、Both → `BaseDef ≤ BaseSpDef` なら Def、でなければ SpDef。
   （両刀攻撃38種では S ≥ 88 の24種が Speed、S 73〜77 の14種が Hp に 25。）
4. 例:
   - 009 ブシガエル（物理攻撃/特殊耐久、S73）: Hp25・Atk25・SpDef5、↑攻撃↓魔力 → 実数値 HP147・攻131・防51・魔39・精95・俊78。
   - 130B ライトロット（特殊攻撃/両面耐久、S130、B47 D46）: SpAtk25・Speed25・SpDef5、↑魔力↓攻撃 → 俊160。
   - 186 リリクイン（特殊攻撃/特殊耐久、S52）: Hp25・SpAtk25・SpDef5、↑魔力↓攻撃。
   - 007 ルミカイト（両刀攻撃/特殊耐久、C58>A40、S76）: SpAtk25・Hp25・SpDef5、無補正。
   - 196 ゼノドラン（両刀攻撃/両面耐久、A115>C77、S130、B55 D55）: Atk25・Speed25・Def5、無補正。
   - 008 ミルフィー（両刀候補から特殊攻撃/物理耐久へ再分類、S47）: SpAtk25・Hp25・Def5、↑魔力↓攻撃。

#### S2-e. 行動順（`Scripts/Battle/BattleScheduler.cs` `Order`）

- 比較を **優先度 → `Stats.Speed`（実数値: 気質・突破値込み）の高い順 → `Bst()`（BST6）の低い順 → 乱数** に変える
  （`BaseSpeed` の比較1行を `Speed` に置き換え、コメントも直す）。
- 俊敏にランク変化は無く、持ち物も俊敏を変えないので「ランク変化・持ち物では動かない」はそのまま成り立つ（コメントに残す）。
- 突破値・気質は相手に開示されない（`PublicEntryView` は種族のみ）。したがって**相手の実際の行動順は種族値 S からの目安でしか読めない**（ポケモンと同じ）。
- `Scripts/UI/Battle/BattleHud.cs` `RefreshRail`: 並びを `OrderByDescending(e => e.Stats.BaseSpeed).ThenBy(BattleScheduler.Bst)`（**公開情報の種族値 S**）にし、
  表示を `BST {bst}` から `S {BaseSpeed}` に変える（自陣の個体だけ実数値を `S {BaseSpeed}→{Speed}` と出してよい）。
  現行は BST 昇順で、Phase 4（S 優先）以降の行動順とずれているのを直す。レールは**目安**であり、突破値・気質で実際の順と食い違い得る（相手の個体差は見えないため）。`RefreshRail` 上のコメント「種族値も…公開情報」は維持し、「目安」である旨を足す。
- `BattleNetProtocol.cs` `TurnResult` のコメント「行動順は優先度→種族値→ランダム」を「優先度→俊敏の実数値→種族値→ランダム」に。

#### S2-f. 通信（`Scripts/Battle/BattleNetwork.cs` `EncodeFull/DecodeFull`）

- `EncodeFull`: 各個体の辞書に `["b"] = [Hp, Atk, Def, SpAtk, SpDef, Speed]`（int の配列）、`["nu"]`・`["nd"]` = `NatureStat` の名前文字列を追加。
- `DecodeFull`: キーが無ければ `Zero()`（古い相手との互換）。
- `EncodePublic` は変えない（種族だけ。突破値・気質は開示しない）。

#### S2-g. NPC 構築データ（`Scripts/Battle/NpcTeamDatabase.cs`）

- `NpcEntryJson` に省略可能な `breakthrough`（`{"hp","atk","def","spatk","spdef","speed"}`）と `nature`（`{"up":"Atk","down":"SpAtk"}`）。
- 省略時は `StatSpread.Auto(species)`。`Data/npc_teams.json` には**追加しない**（全チームが自動配分になる）。
- 読み込み後の `team.Validate()` は現行どおり（突破値・気質の違反も弾かれる）。

#### S2-h. 回帰スイートの変更（S2 完了で **153**）

既存の書き換え:
- `VerifyHabcdsWiring` #5（種族値%加算）: 実数値を合わせる操作を `BreakthroughSpAtk = addSp * 2` → **`addSp`**、`BreakthroughAtk = addPh * 2` → **`addPh`** に
  （1ポイント = 実数値+1 になったため）。コメント「突破値 +2 で実数値 +1」も直す。

追加（+5）:
1. `[検証] 気質の後に突破値を足す`: 盤上のブシガエル(009, A92)に、無補正・突破0 → 攻撃 **97**、↑攻撃 → **106**、↑攻撃＋攻撃25 → **131**
   （先に足すと 134 になるので区別できる）、↓攻撃 → **87**。HP25 → 最大HP **147** かつ現在HP = 最大HP（満タンのまま）。確認後に元へ戻す。
2. `[検証] 突破値と気質の構築検証`: 1能力26 → NG、合計56（25+25+6）→ NG、合計55（25+25+5）→ OK、合計0 → OK、
   気質 ↑攻撃↓攻撃 → NG、↑攻撃のみ（下降None）→ NG、↑攻撃↓魔力 → OK。`BattleTeam.Validate()` を通して見る。
3. `[検証] 行動順は俊敏の実数値を見る`: タマコッコ(003, S50) に ↑俊敏＋俊敏25（実数値 floor(55×1.1)+25 = **85**）、ブシガエル(009, S73) は無補正・突破0（実数値 **78**）、
   優先度0同士でタマコッコが先。突破・気質を戻すとブシガエルが先。
4. `[検証] 自動配分は決定的で規則どおり`: 全287種で `Auto` の合計が55・各≤25・`Validate()` が空、2回呼んで同じ。ブシガエルが Hp25/Atk25/SpDef5・↑攻撃↓魔力、
   ライトロット(130B)が SpAtk25/Speed25/SpDef5・↑魔力↓攻撃、ゼノドラン(196)が Atk25/Speed25/Def5・無補正。
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
    変更は `_team.SetSpread(_slot, s)` → 既存の検証・再描画経路（`_ready.Disabled = errors.Count > 0` の所）に乗せる。実数値は `StatSpread.RealStat`（S2-b）で計算して表示する。
  - `SlotCard`: 持ち物の行の右に気質のラベル（`NatureLabel`）を小さく出す（任意。並べ方は実装者判断）。
- `Scripts/UI/Battle/SpeciesPickScreen.cs`: 種族カードに役割ラベル（`RoleLabel`）を1つ出す。属性フィルタの並びは変えない。
- 回帰スイートの項目追加なし（**153**）。`godot --headless` で構築画面を開いて例外が出ないことは、既存の UI 起動経路があればそれで確かめる（無ければ確認できなかった旨を報告）。

### Phase S4 — Artifact・設計記録

- `Tools/build_ls_payload.py`: `pf`・`pb` を削除し、`of`（offense）・`df`（defense）・`ob`（`offense_source` を和名にしたもの: `'特性'` / `'周期'` / `'俊敏'`）を出力。`role` は新しい値。`bst` は新 BST6。
  総合耐久指数 `ti = H*(B+D)` を追加。
- `Tools/artifacts/ls_tail.html`: `PFJA` を削除し、`OFJA = {Physical:'物理攻撃', Special:'特殊攻撃', Mixed:'両刀攻撃'}`、`DFJA = {Physical:'物理耐久', Special:'特殊耐久', Both:'両面耐久'}`。
  絞り込みは「攻撃区分」「耐久区分」の2つのチップ列（`pfs` → `ofs`、`roles` → `dfs`）。両方選べば 9 役割のどれかに絞れる。
  表示の `${PFJA[s.pf]}・${ROLEJA[s.role]}` → `${OFJA[s.of]}/${DFJA[s.df]}`、詳細の「役割」も同じ表記。詳細に「総合耐久指数」と「攻撃区分の根拠」（`ob`）を1行ずつ足す。並べ替えに「総合耐久指数」「俊敏」を追加。
- `Tools/artifacts/ls_head.html`: チップ列の id とラベル（「型」→「攻撃区分」、「役割」→「耐久区分」）。
- `python3 Tools/build_ls_payload.py` と `python3 Tools/refresh_move_editor.py`（learnset が47種変わり〔S1-e の41種・S1-f の6種〕、技84種の習得者数が変わるため）を実行し、
  `Tools/artifacts/learnset_viewer.html` を **url `188eb9b1-d49d-4c39-bf77-895a30185e99`**、`Tools/artifacts/move_editor.html` を **url `975b54fe-5c46-4597-ae30-26d694ac3eb2`** へ、同じ file_path で再公開。
  出来上がった `learnset_viewer.html` に「物理型」「特殊型」「打ち分け型」が含まれないことを `grep` で確かめる。
- `docs/design/pvp_spec.md` に **§30** を追加: BST6 の新式、俊敏三峰の写像とヒストグラム、役割の直積と配分式、両刀攻撃の条件（開き10%・俊敏ゾーン）と再分類41種、
  learnset の整合（114行の削除と NPC の2件）、S≥95 の優先技規則と6種の置換、突破値・気質の式と検証規則・自動配分・行動順の変更、S5 の計測結果。
  §1「行動順の決定」を新規則（実数値の俊敏）に書き換える。
- **`CLAUDE.md` は編集しない**。ただし「行動順と天候の向きは逆」の節（行動順の記述）・「種族値を変えると固定シードの再現性が崩れる」の節・回帰スイートの項目数（145 → 153）が古くなるので、S4 完了報告で更新が必要と指摘する。

### Phase S5 — 計測（10,000 試合）

- `Tools/analyze_random_by_element.py` を一般化し、`--by element|offense|defense|role|speed` を受ける（既定 element。集計の数え方・BST 補正は現行と同じ）。
  `speed` は種族値 S の帯 `≤62 / 63–87 / 88–99 / ≥100`。species.json の `offense`/`defense`/`role`/`base_speed` を読む。
- `godot --headless --path . Scenes/BattleMatchScene.tscn -- --random --matches 2500 --out /tmp/s5_<n>.csv` を4プロセス（`--random` は状態を持たないので分割してよい）。
- 結果を S4 で足した §30 に表で記録（属性10・攻撃区分3・耐久区分3・役割9・俊敏帯4）。§5-4 の基準で評価して報告。
- **CLAUDE.md の注意**: 種族値・learnset・行動順が変わったので、`MetaScenario`（シード `20260822UL`）の構築番号（#66/#53/#30 等）は過去と別の構築・強さになる。過去節と構築番号で比較しない。

---

## 4. 判断が必要だった点（採った解釈と理由）

### 4-1. BST6 の「320〜610」
- 候補: (a) 現行と同じ線形写像の端点だけ変える（BST3 180→320、500→610、傾き 290/320）。(b) 現行 BST6 から一律 −40 / 比例縮小（630×k=610 なら最小は 349）。
- **採用: (a)**。「最大値を320〜610に」は範囲の両端の指定と読める。(b) は下端が 320 にならない。前回の確認時の追加指示（180→360、500→630 の線形）と同じ形式を保つ。
  BST の並び順はどこでも変わらない（S1-a）。

### 4-2. 「表記をなくす」の範囲
- 型という概念自体は learnset の生成規則（多数側の分類・威力天井）を支えているので消せない。消すのは**名前**と、役割から独立した軸としての存在。
  「型」を「攻撃区分の候補」と呼び替え、確定した攻撃区分を役割（攻撃区分/耐久区分）の前半に吸収した。値の `Versatile` → `Mixed`、和名「打ち分け型」→「両刀攻撃」。
- 「打ち分け」という語も残さない（「打ち分け型」を連想させるため）。代わりに「両刀」（物理・特殊の両方で攻める、の慣用語。ユーザーも確認の回答で「両刀」と書いている）を使った。
- 過去の草案（`docs/design/drafts/2026-10-25-*.md`）は確定時点の記録なので書き換えない。`pvp_spec.md` は現行仕様の記述なので直す。

### 4-3. 攻撃と耐久の直積（確定事項1）と耐久区分の決め方
- 現行の5役割は「攻撃型/耐久型」が**攻撃と耐久の取り合い**（副側を削るか残すか）を表しており、B と D の偏りを表していない。
  ユーザー例「物理攻撃/特殊耐久」「特殊攻撃/物理耐久」は**攻撃の分類と、耐久の向き（B か D か）が逆**の組み合わせなので、耐久区分を B:D の偏りとして新設し直積にした（ユーザー確認 1.a）。
- 攻撃と耐久の**量**の取り合いは、新しい要求「俊敏が低いほど耐久が厚い」で S が決める（S1-d の w）。旧「攻撃型/耐久型」の区別はこれに置き換わる。
- 耐久区分の B/D 振り分けはユーザーが「適当でよい」と回答したので、ハードアーマー/ハードブロック（被弾分類ごとの耐久特性）はその向きを強める側に置き、残りは序数の周期で散らした（再現可能・偏らない）。

### 4-4. 両刀攻撃の「10%以上の開き」の定式化（確定事項1の条件）
- 「どちらか一方の種族値に対して10%以上の開き」を **max(A, C) ≥ 1.10 × min(A, C)**（小さい側を基準に10%以上大きい）と読んだ。
  A:C = 50:50（開き0%）や 51:49（開き4%）は不可、40:60（開き50%）は可。主側比率で書くと X/(A+C) ≥ 11/21 ≒ 0.524。
- 別の読み「大きい側を基準に10%以上小さい（min ≤ 0.9 × max）」は主側比率 ≥ 10/19 ≒ 0.526 で、閾値の差は 0.002。
  **現データの両刀攻撃38種は全種が主側比率 0.592〜0.600（開き 45〜50%）なので、どちらの読みでも結果は同じ**。ユーザーの文言に素直な前者を採った。
- 上限は前回の確定事項「A:C = 40:60〜60:40」を維持（主側比率 ≤ 3/5）。結果として主側比率 ∈ [11/21, 3/5]。
- 整数への丸め: 主側の値 X を `round` の後に [ceil(11/21 × Off), floor(3/5 × Off)] へクランプする。下限は 10X ≥ 11(Off − X) と同値なので、丸めた後の整数でも開き10%以上が保証される。
- 主側の決め方: learnset の多数側（= `mixed_side_of`、§4-6）。主側比率の大きさは learnset の share をクランプして使う（現行の「learnset の物理比 r を [0.4, 0.6] にクランプ」と同じ考え方を、主側から見て書き直したもの）。

### 4-5. 両刀攻撃の俊敏条件（確定事項2の条件）
- 「最低でも BST ±4% ゾーンで中央値より足が早い部類に限定」を、**最低条件そのもの**として実装した（それより厳しい追加条件は付けない。38種）。
- **±4% の基準**: 自分の BST6 に対する比率（|ΔBST6| ≤ 0.04 × 自分の BST6、両端含む）。BST は本書で定義し直した BST6（S1-a）。BST3 で測っても同じ並びだが、±4% の幅が変わるので BST6 に固定した。
- **自分を含めない**: 「同列付近のパルと比較して」は他の個体との比較と読んだ。含めた場合も判定が変わる種は0（確認済み）。
- **母集団は全287種**: 「同列付近の BST」のパル全体と比べる（両刀候補どうしだけで比べると、候補が少ない帯で中央値が不安定になる）。
- **中央値と等しい場合は不可**: ユーザーの「中央値より足が早い」（中央値以上ではない）に従う。中央値以上にすると S が中央値ちょうどの 050 ニャンシー・072B チョコザラシ・106 ココヤンバの3種が両刀攻撃に残り、両刀攻撃41種・再分類38種になる。
- **端の個体・ゾーンが小さい場合**: ±4% で10種未満なら近い順に10種まで広げる（10番目と同距離は全部含む）。BST6 の両端（320 付近・500 以上）でゾーンが片側だけ・数種だけになり、中央値が1〜2種で決まるのを防ぐため。
  該当は9種で、両刀候補は 030・035 の2種。広げない場合も2種の判定は同じ（030 は 9種のゾーンでも中央値 73 < 77、035 は S35 で全287種の最小）。
- **§4（S の三峰・S と耐久の相関・俊敏の写像）との整合**: 俊敏条件は S を読むだけで S を変えない。H・B・D は攻撃区分に依存しない（S1-d）。
  したがって S のヒストグラム・三峰の判定・相関・帯別平均は条件の有無で一切変わらない（§1-6・§1-7 の期待値は再分類を入れる前の計算と同じ値）。
- **優先技規則との整合**: 両刀攻撃38種のうち S ≥ 95 は24種で、MV_140 を持つのは 127 ヤクモマルだけ（S1-f で MV_141 に置換）。
  「速い両刀攻撃は先制技を持たない」ことになり、規則の動機（速い個体に先制技は要らない）とも矛盾しない。再分類41種のうち S ≥ 95 は 106 ココヤンバ（S98、中央値98と同値で不可）だけで、MV_140 を持たない。
- 両刀攻撃の S は 73〜77（14種）と 98〜130（24種）に分かれる。73〜77 の14種は BST6 329〜388 の低い帯で、周りの多くが 50 の峰にいるため 75 の峰でも「速い部類」になる。
  ユーザーの基準は「同列付近の BST と比較して」なので、これは意図どおりと判断した。

### 4-6. 再分類先（物理攻撃か特殊攻撃か）
- 候補: (a) learnset の多数側（物理/特殊の攻撃技の数）、(b) 旧 HABCDS の A と C の大小、(c) 生成時の多数側（序数の偶奇）。
- **41種すべてで (a)(b)(c) は一致し、learnset の物理と特殊が同数の種も無い**（確認済み）。規則としては **(c) `mixed_side_of(index)`** を採った。
  理由: 攻撃区分が learnset に依存しなくなり、`build_habcds.py` と S1-e（learnset を削る）の間に循環が生じない。`generate_learnset.py` が両刀候補の learnset を作った規則そのものなので、learnset の偏りと必ず一致する。
- 結果: 物理攻撃へ13種、特殊攻撃へ28種（俊敏条件を満たさなかった41種のうち序数が奇数の種が28種あった）。攻撃区分の内訳は 物理124+13=137・特殊84+28=112・両刀38。

### 4-7. 再分類した41種の learnset（S1-e）
- 再分類した41種の learnset は両刀候補の規則で作られており、そのまま物理攻撃/特殊攻撃と呼ぶと「技と能力がずれる」（A:C = 70:30 なのに少数側に多数側の 90% の威力の技がある等）。
  攻撃区分と learnset の偏りが食い違う状態が41種で起きる。
- 候補: (a) 41種の learnset を新しい区分の規則に合うまで**削る**（追加・入れ替えはしない）。(b) learnset はそのまま、検証は生成時の区分（候補）で続ける（攻撃区分と learnset の区分が2本立てになる）。
  (c) 41種の learnset を新しい区分で再生成する。
- **採用: (a)**。理由:
  - 物理攻撃/特殊攻撃の249種と同じ learnset 規則（`verify_learnset.py`）が41種にも成り立ち、「攻撃区分 = learnset の偏り」がどの種でも言える。(b) は「型」という独立した軸を実質的に残すことになり、表記をなくす要求（§4-2）にも反する。
  - (c) は `generate_learnset.py` の共有率カウンタ（種族を序数順に回しながら配る）を通すため、41種だけ作り直すことができず、全287種の learnset と後段の追加スクリプト（虚属性・変化技・制限技など）に波及する。
  - (a) は削る行が決定的に決まり、41種で114行（1種2〜5行）に収まる。違反は「少数側の強すぎる技」（106行）が大半で、削るのはまさに区分と食い違う技。
- 削った後も、各種は変化技2つ以上・無属性攻撃技2つ以上・適応技6つ以上（該当特性）・制限技を保つ（削らない行の規則で保証）。
- 再分類種の威力天井は両刀候補の値（−20）のまま（技を足さないため）。物理攻撃/特殊攻撃としてはやや控えめな learnset になるが、規則上の上限ではなく下限側なので検査には触れない。
- NPC 構築の2件は、消えた技を「同じ属性・新しい区分の分類で最強の技」に置き換える最小の修正にした（編成の再生成はしない）。

### 4-8. B:D と A:C の比
- B:D = 65:35 / 35:65 / 50:50 は、前回ユーザーが打ち分け型に与えた許容幅「35:65〜65:35」の両端と中央。
- A:C = 70:30 / 30:70 は旧物理耐久型・物理攻撃型の副側の残し方（旧 C=0.4a〜0.6a ⇔ A 比 0.63〜0.71）の中間。両刀攻撃は §4-4。

### 4-9. 「俊敏性が低いほど…耐久指数」（文が途中で切れている）
- 原文は「また、俊敏性が低いほど物理/特殊耐久指数(…)」で述語が無い。前後（ペラペラだが速い／遅いが物理受け）から**「俊敏が低いほど耐久指数が高い」**と読んだ。
- 組み込み方: 現行の「S を先に決め、残り T を配る」を保ち、耐久の取り分 w を S の一次減少関数（0.76→0.46）にした。T 自体も S が低いほど大きいので二重に効く。
- 検証指標: 生の相関は BST の交絡（ライドの個体ほど BST3 が大きい）で弱く出るので、**生の相関 ≤ −0.40 と BST6 統制の偏相関 ≤ −0.80 の両方**、加えて S 帯別平均の単調減少を基準にした。
  傾き 0.30 は、生の相関が −0.4 を明確に超え、かつ最小能力が 10 を割らない範囲で選んだ（傾き 0.22 では生の相関 −0.28）。
- 「物理耐久指数」「特殊耐久指数」は耐久区分で B/D が偏るので、S との相関は総合より弱い（−0.39 / −0.34）。基準は総合耐久指数に置いた。

### 4-10. 俊敏の三峰
- 50 の峰は、現行で 35〜73 に散っていた運搬0の非ライド61種を 47〜60 に集めて作った。75 の峰は運搬1〜2（88種）、100 の峰はライド（現行のまま）。
- 最小35（035 のみ）・最大130・基準点4種は前回の確定事項なので維持した。
- 山の位置の判定は「中心±2」の階級で行う（前回と同じ階級）。50 の峰は 43〜62 に裾が広い（運搬0の個体の走行速度の差）が、最大階級は 48–52。

### 4-11. 既存の突破値との関係
- 既存の `Breakthrough*` は努力値型（種族値の2倍に加算、Lv50 で +2 → +1）で、0 以外に設定する経路が無い（§0-4）。
- 候補: (a) **同じ名前のまま「気質の後に実数値へ直接加算」に置き換える**。(b) 別名で新設し、既存は残す。
- **採用: (a)**。ユーザーは「突破値」という名前で新仕様を定義しており、既存が未使用なので互換の問題が無い。名前が2つあると誤用の元になる。
  検証の1箇所（`BattleTestScene` 702–706行）だけ `×2` を外す。

### 4-12. 個体差（突破値・気質）を見る箇所と見ない箇所
| 箇所 | 見るもの | 理由 |
|---|---|---|
| ダメージ計算（A/B/C/D・HP） | **実数値**（個体差込み） | 突破値・気質の目的そのもの |
| 行動順の俊敏 | **実数値**（確定事項3） | 俊敏に振る意味が行動順にしか無い。種族値のままだと S の突破値・気質が完全に無効になる |
| 行動順の目安（HUD レール） | 種族値 S（自陣だけ実数値を併記してよい） | 相手の突破値・気質は非公開。目安であることを明示する |
| 行動順のタイブレーク BST6 | 種族値 | 「合計種族値」は種族の定数。個体差を混ぜると 55 振った個体が常に後手になる |
| 天候の発動順 | 種族値（BST6） | 同上。公開情報だけで決まる現行の性質を保つ |
| NPC 選出採点 | 種族値（BST6） | 同上 |
| 種族値%の平坦加算（きずな・まもり・リーダー系） | 種族値 | 効果文が「種族値の○%」 |
| S≥95 の優先技規則・両刀攻撃の俊敏条件 | 種族値 | learnset と攻撃区分は種族のもの |

### 4-13. 突破値の合計「55 ちょうど」か「55 以下」か
- 「全体で55ポイントを振り分け可能」は上限の表現と読み、**55 以下**にした（ポケモンの努力値と同じ慣例。構築画面の初期状態0も合法にできる）。
  自動配分は常に 55 を使い切る。

### 4-14. 気質の表現
- ポケモンの25性格（無補正5種を含む）と同等の自由度を、`NatureUp`/`NatureDown` の2値で表す。無補正は (None, None) の1通りに正規化する
  （(攻撃↑攻撃↓) のような「同じ能力の上下」は不可にして、無補正の表し方を1つにする）。

### 4-15. 既定の突破値・気質と再現性（確定事項4）、両刀攻撃の気質
- ランダム構築・MetaScenario・TacticalBuilder・NPC は構築を自動で作るので既定値が要る。ユーザー確認 4.b により**役割に応じた決定的な自動配分**（S2-d）。
  乱数を消費しないので、同じシードから出る species・持ち物の列は乱数の面では現行と同じ。
  ただし learnset が変わる47種（S1-e の41種・S1-f の6種）は `RandomTeam` の技の抽選と `DefaultLoadout` の選択が変わり得る。
- 回帰スイートの構築は全0のまま（既存のダメージ検証を個体差で動かさないため）。
- **両刀攻撃の気質は無補正のまま**とした。両刀攻撃になる条件（開き10%以上・俊敏ゾーンで速い）が加わっても、両刀攻撃は learnset に物理・特殊の両方の攻撃技を持ち（多数側 65〜75%）、
  従側の攻撃技も多数側の最高威力の 90% まで持てる（両刀攻撃の learnset 規則）ので、従側を ×0.9 にすると実際に使う技が弱くなる。耐久・俊敏を下げる案は、俊敏条件で選ばれた個体の持ち味（速さ）か、
  自動配分で耐久区分に5振った能力を削ることになり、「主攻撃↑・使わない攻撃↓」という 4.b の規則の考え方（使わない能力を削る）に当てはまる能力が無い。
  ユーザーが確定した 4.b の文言「両刀は無補正」とも一致する。
- 両刀攻撃の主攻撃 P は主側（`BaseAtk ≥ BaseSpAtk`）。開き10%以上なので同値は起きない。

### 4-16. 優先技規則の「傾向として」
- 「傾向として」は規則の動機（速い個体に先制技は要らない）を述べたものと読み、**例外の無い規則**として実装・検証する。
  対象は技の優先度だけで、特性による優先度（水のちから＝あめで+1）は対象外。制限技（`rs_`）に優先度+1の技は無い。
- 置き換えは「同じ属性・分類・射程・接触で優先度0の最も近い威力」。習得者が持つ攻撃技の数・分類比は変わらない。

### 4-17. 種族値の変更による影響（BST6 に依存する全箇所）
| 箇所 | 影響 |
|---|---|
| `BattleScheduler.Order` のタイブレーク・`BattleArena.ApplyEntryWeather`・`MetaScenario.StrongPool`・`TacticalBuilder`（BST 昇順の並び）・`SpeciesPickScreen` の種族値順 | BST6 は BST3 の狭義単調増加関数なので**並びは変わらない**（陣営合計の比較も、BST3 合計が異なれば向きは保たれる: 5点差で 4.53 > 丸め誤差の最大4） |
| `NpcOpponent.SelectionScore` | `Bst6Weight` を 32/29 に（1点の重みを旧 BST3 並みに保つ） |
| `generate_npc_teams.py` の TARGET_BST・`npc_teams.json` の `total_bst` | 新式の値に（編成は変えない） |
| UI の「BST」表示（Deploy・TeamBuild・SpeciesPick・Selection・OpponentSelect・BattleHud） | 値が新 BST6 になるだけ（文言は変えない。`BattleHud.RefreshRail` は S2-e で S 表示に変える） |
| `--random --out` の CSV `home_bst/away_bst` と集計の BST 補正 | 値が新 BST6。ロジスティックの k は集計時に当て直すので式の変更は不要 |
| `Tools/build_ls_payload.py` の `bst` | 新 BST6 |
| learnset 系ツールの閾値（BST180〜500 前提の上限表・`<310`） | `legacy_had` の BST3 を読み続けるので**影響なし** |
| 両刀攻撃の俊敏ゾーン | 新 BST6 で測る（S1-c） |
| 回帰スイートの期待値 | S1-i に列挙 |

### 4-18. 両刀攻撃の「H×0.8 = B+D」（確定事項2）
- 前回確定した打ち分け型の耐久配分「H×0.8 = B+D」は**廃止**し、全役割共通の H:(B+D) = h:2d にした（ユーザー確認 2.a）。両刀攻撃の H・B・D は耐久区分と S が決める。

---

## 5. 検証方法

各フェーズで次を必ず実行する（CLAUDE.md）。

```sh
dotnet build MysteryDungeonRoguelike.csproj                              # 0 エラー（godot より先に）
godot --headless --path . Scenes/BattleTestScene.tscn | grep '\[検証\]'   # 項目数と NG 0（スプライトの import エラーは無視してよい）
git diff                                                                 # この仕様書と突き合わせる
python3 Tools/verify_learnset.py && python3 Tools/verify_attack_moves.py && python3 Tools/verify_npc_teams.py   # ALL PASS
```

### 5-1. データ（S1）

| # | 確かめること | 方法 | 期待値 |
|---|---|---|---|
| (a) | 全287種の BST6 が 320〜610 | `build_habcds.py` 自己検査、スイート `VerifyHabcdsData` #1 | 最小320・最大610、003=320・186=474・203=610 |
| (b) | S が 50/75/100 の三峰 | `build_habcds.py` の `report`、スイート #4 | S1-b のヒストグラムと全階級で一致、c(50)=23・c(75)=62・c(100)=68 |
| (c) | S≥95 は優先度+1以上の攻撃技を持たない | `apply_fast_priority_rule.py` の最終検査、`verify_learnset.py` #17、スイート追加項目 | 111種で0件。置換6件が S1-f の表どおり |
| (d) | S と総合耐久指数の相関 | 自己検査、スイート追加項目 | r = −0.475（≤ −0.40）、偏相関 −0.892（≤ −0.80）、帯別平均 11903 > 8732 > 8103 |
| (e) | 役割の直積が全て実在 | 自己検査、スイート #3 | S1-c の表の9つの数（P:51/43/43、S:34/42/36、M:13/14/11） |
| (f) | 両刀攻撃の条件 | 自己検査、スイート #2 と追加項目 | 両刀攻撃38種が S1-c の一覧と一致し全種が俊敏条件を満たす、再分類41種（物理13・特殊28）が付録C と一致、両刀攻撃の max/min が 1.45〜1.50（≥1.10）・主側比率 ≤ 0.600 |
| (g) | 再分類41種の learnset | `conform_offense_learnsets.py` の最終検査、`verify_learnset.py`（新 `prof`）、`git diff Data/species.json` | 削除114行が付録C と行単位で一致、追加行0（S1-f の6行の置換を除く）、`verify_learnset.py` #5・#9・#10・#12・#13・#18 PASS |
| (h) | NPC 構築の追従 | `git diff Data/npc_teams.json`、`verify_npc_teams.py` | 技の変更は S1-e の2件だけ、他は `total_bst` の値のみ |
| — | 付録A と一致 | species.json の全287種の HABCDS・offense・defense を付録A と突き合わせるスクリプトを一時的に書いて実行（コミットしない） | 287/287 一致 |
| — | 冪等 | S1-f 末尾の実行順を2周した後の `git diff --stat Data/` が1周目の後と同じ | 2周目は差分なし |
| — | 表記 | `grep -rn "物理型\|特殊型\|打ち分け型\|器用型" Scripts Tools docs/design/pvp_spec.md` | S1 完了時は `Tools/artifacts/` 以外 0 件、S4 完了時は 0 件 |

### 5-2. 突破値・気質（S2）

- スイート追加1（S2-h）: ブシガエル A92 → 97 / 106 / **131**（≠134）/ 87、HP 147 で満タン。
- 手計算の確認（スイートと別に、仕様の読み違い防止）: 素=種族値+5、↑は `*11/10`、↓は `*9/10`（いずれも切り捨て）、突破値は最後に足す。
  例: S50 → 素55 → ↑60 → +25 で 85。S130 → 素135 → +25 で 160。A=40 → 素45 → ↓40（45×9/10=40.5→40）。
- 迷宮の回帰: 突破値0・無補正で実数値が変わらないこと（スイートの既存項目が全て OK のままであることで確認。既存のダメージ期待値が S1 の値から動かない）。

### 5-3. 回帰スイートと Python 検証の項目数

| 完了時点 | スイート | 内訳 |
|---|---|---|
| 基準（`980b590`） | 145 | — |
| S1 | **148** | 書き換え: HABCDS データ4項目・BST/ベンチマーク関連3項目（数は不変）。追加: 優先技規則・俊敏と耐久の相関・両刀攻撃の俊敏条件 |
| S2 | **153** | 書き換え: 種族値%加算（×2を外す）。追加: 適用順・構築検証・実数値の行動順・自動配分・通信 |
| S3〜S5 | 153 | 追加なし |

Python（`[PASS]` 行の数。S1 以降）: `verify_learnset.py` **18**（基準16 + #17 S≥95 の優先技 + #18 多数側 = 攻撃区分）、`verify_attack_moves.py` **23**（技データは変えない）、`verify_npc_teams.py` **9**。

### 5-4. 10,000 試合（S5）

- `analyze_random_by_element.py --by element|offense|defense|role|speed` で、出場数・勝率・BST 補正後差(pt) を出す。
- **基準（良好）**: 属性10・攻撃区分3・耐久区分3・役割9 の BST 補正後差がすべて **±5pt 以内**。前回（炎 +2.9pt / 無 −3.5pt）から属性の最大偏差が悪化していないこと。
  未決着率 2% 以下。俊敏帯は判断材料として報告（S が行動順を決めるので速い帯が強く出るのは想定内。帯の差が ±8pt を超えたら報告）。
  両刀攻撃は38種と少なく、全種が速い帯にいるので、役割別の両刀攻撃3区分は出場数も併記して評価する。
- ±5pt を超える区分があっても**合否ではなく調整の判断材料**として報告する（実装者は数値を変えない）。
- 自動配分の影響を見るため、可能なら同じ 10,000 試合を突破値0・無補正（`--spread zero` のような一時オプション。コミットしない）でも回し、差を報告する（任意）。

---

## 6. やらないこと

- learnset の再生成（`generate_learnset.py` の再実行）。learnset の変更は S1-e の114行の削除と S1-f の6行の置換だけ。技を足す・入れ替える変更（再分類種の威力天井を上げる等）はしない。
- `Data/source/paldb_speed.json` の再取得。ライドの写像 `P_R`・S の範囲 35〜130・基準点4種の変更。
- Phase 6 の内容（`rs_` 技、その習得者、`RESTRICTION_LEARNERS`）の変更。
- 攻撃区分の候補の決め方（`TRAIT_PHYSICAL`/`TRAIT_SPECIAL`/周期）の変更。物理攻撃・特殊攻撃の候補を両刀攻撃へ昇格させること（俊敏条件は両刀候補を絞るだけ）。
- 俊敏条件を最低条件より厳しくすること（§4-5）。
- 技データ（`Data/moves.json`）の変更。ヘッドバットの優先度は変えない（S が 95 未満の15種は引き続き覚える）。
- 俊敏のランク変化・俊敏を変える持ち物の新設。俊敏の回避・命中・迷宮のターン速度への利用。
- 迷宮側（Player/AllyEntity 等）への突破値・気質の導入と、迷宮のステータス画面。
- NPC の行動判断に能力値・気質・突破値を足すこと（`NpcOpponent` の変更は `Bst6Weight` のみ）。NPC の構築を突破値・気質を見て最適化すること。
- `Data/npc_teams.json` の編成の変更（`total_bst` の値の更新と、S1-e の技2件の置き換えのみ）。
- 対戦 HUD に相手の突破値・気質・実数値を出すこと（非公開情報）。
- `docs/design/drafts/` の過去の草案の書き換え、`CLAUDE.md` の編集（S4 の報告で指摘だけする）。
- 既存の天候・持ち物・特性・ダメージ式の数値調整（S5 で偏りが出ても、実装者は調整しない）。

---

## 7. フェーズと完了条件

| Phase | 内容 | 完了条件 |
|---|---|---|
| S1 | BST6 新式・俊敏三峰・攻撃区分（両刀の俊敏条件と再分類）・耐久区分・配分（両刀の開き10%）・再分類41種の learnset 整合と NPC 2件・S≥95 の優先技置換・表記改名（Python/C#コメント）・`offense`/`offense_source`/`defense` の DTO・`Bst6Weight`・npc_teams の `total_bst` | build 0エラー / スイート **148**・NG0 / Python ALL PASS（`[PASS]` 行 learnset **18**・attack **23**・npc **9**）/ `build_habcds.py`・`conform_offense_learnsets.py`・`apply_fast_priority_rule.py` の自己検査 OK / 付録A と 287 種一致 / 付録C の114行と一致 / 冪等 |
| S2 | 突破値・気質（`EntityStats` の式・`StatSpread`・`BattleEntry`・`Validate`・自動配分・全生成箇所・通信・NPC JSON・行動順の実数値化・HUD レール） | build 0エラー / スイート **153**・NG0 / Python ALL PASS（18/23/9） |
| S3 | 構築画面（`SpreadPanel`・役割ラベル） | build 0エラー / スイート **153**・NG0 / 構築画面で突破値・気質を設定→検証→対戦開始まで通ることを確認（確認できない環境なら、その旨と、代わりに確かめた範囲を報告） |
| S4 | Artifact 2つの再生成・同 URL 再公開、`pvp_spec.md` の §30 と §1 の書き換え | 2つの URL が更新 / `learnset_viewer.html` に旧表記0件 / スイート 153 |
| S5 | 10,000 試合と区分別集計 | §30 に表 / §5-4 の基準で評価した報告 |

各フェーズは前のフェーズが完了条件を満たしてから着手し、フェーズごとに別コミットにする（コミットの指示があれば）。
項目数がこの表とずれたら、追加・変更した `[検証]` の一覧を添えて報告する。

---

## 8. 確定事項

ユーザー確認（2026-10-26）の回答と、それを受けて本書で決めたこと。

1. **役割の構成 = 攻撃3 × 耐久3 の9役割**（回答 1.a）。
   追加条件「両刀はどちらか一方の種族値に対して10%以上の開き」→ max(A, C) ≥ 1.10 × min(A, C)、主側比率 ∈ [11/21, 3/5]（S1-d 手順8、§4-4）。
2. **両刀攻撃の「H×0.8 = B+D」は廃止、全役割共通の H:(B+D) = h:2d**（回答 2.a）。耐久区分の B/D 振り分けは §4-3 の決め方（回答「適当でよい」）。
   追加条件「両刀は同列付近の BST と比べて俊敏が高い傾向のものに絞る。最低でも BST ±4% ゾーンで中央値より速い」→
   BST6 ±4%（自分を除く全種、10種未満は近い順に10種）の S の中央値より S が大きい両刀候補だけを両刀攻撃にし（38種）、残り41種を learnset の多数側へ再分類（S1-c、§4-5・§4-6）。
   再分類41種の learnset は物理攻撃/特殊攻撃の規則に合うまで削る（114行。S1-e、§4-7）。
3. **行動順は俊敏の実数値（気質・突破値込み）**（回答 3.a）。HUD の行動順レールは公開情報の種族値 S で並べる目安（S2-e）。
4. **既定の突破値・気質は役割に応じた決定的な自動配分**（回答 4.b）。両刀攻撃の気質は無補正のまま（S2-d、§4-15）。

本書で決めて確認事項にしなかったもの: BST6 の写像（§4-1）、表記の範囲と「両刀」の名前（§4-2）、10%の開きの読み方（§4-4。現データでは読み方で結果が変わらない）、
俊敏ゾーンの細部（自分を含めない・端の拡張・中央値と同値は不可。§4-5）、再分類先の規則（§4-6。3つの候補が全種で一致）、learnset を削って合わせる方式（§4-7）、
B:D・A:C の比（§4-8）、俊敏と耐久の結び付け方と傾き（§4-9）、三峰の写像（§4-10）、既存突破値の置き換え（§4-11）、個体差を見る箇所（§4-12）、
合計55以下（§4-13）、気質の表し方（§4-14）、優先技規則を例外なしにすること（§4-16）。

---

## 実装時に判断してよい範囲

次は結果（付録A・付録C・§5 の期待値）を変えないので、実装者が決めてよい（報告不要）。これ以外で迷ったら決めずに報告する。

1. `build_habcds.py`・`conform_offense_learnsets.py` の関数分割・`Fraction` か整数演算か（結果が付録A・付録C と一致すること）。
2. `StatSpread` の置き場所（`Scripts/Entities/` か `Scripts/Battle/`）と、`NatureStat` の enum 名（JSON・通信の文字列は `Atk/Def/SpAtk/SpDef/Speed` で固定）。
3. `SpreadPanel` の並べ方・色・ボタンの刻み（−5/−1/+1/+5 が押せること、残りポイントと実数値が見えること）。
4. `[検証]` 行の括弧内の補足文言（OK/NG の判定内容は §3 どおり）。
5. `analyze_random_by_element.py` の出力の体裁（区分ごとに 出場数・勝率・期待勝率・補正後差 が出ること）。
6. 俊敏ゾーンの C# 実装の書き方（判定結果が S1-c・付録C と一致すること）。

逆に、次は**決めずに報告**: BST6 の式、`P_N` の点、w(S) の係数（0.76・0.30・2/3・0.45・0.80）、β・α、両刀攻撃の主側比率の範囲（11/21・3/5）、
俊敏ゾーンの定義（±4%・自分を除く・10種への拡張・中央値と同値は不可）、再分類先の規則、耐久区分の決め方、learnset の削除規則（S1-e）と NPC の置き換え規則、
優先技の置換規則、実数値の式と丸め、検証規則（25・55・気質）、自動配分の規則、行動順の比較順。

---

## 付録A. HABCDS の期待値（species_id:攻撃区分耐久区分:H/A/B/C/D/S）

略号: 攻撃区分 P=物理攻撃 S=特殊攻撃 M=両刀攻撃、耐久区分 B=物理耐久 D=特殊耐久 E=両面耐久（JSON の `Both`）。
6値の合計は §S1-a の BST6。§S1-b〜S1-d の式（付録B）で、前提コミット `980b590` の `Data/species.json`（`legacy_had`・learnset）と `Data/source/paldb_speed.json` から計算したもの。
S1-e（再分類種の learnset を削る）・S1-f（分類を変えない置換）はこの表の値を変えない（再分類種の A:C は learnset を見ず、両刀攻撃の learnset はどちらも触れないため）。

```
001:PB:58/69/75/30/41/74 002:PD:58/69/41/30/75/74 003:PE:64/55/64/23/64/50 004:PB:75/60/92/25/49/51 005:SD:51/34/35/79/66/73 005B:PE:50/83/51/36/50/73
006:PB:71/60/92/25/50/49 007:MD:64/40/44/58/83/76 007B:ME:63/62/64/42/63/76 008:SB:71/25/99/57/53/47 009:PD:62/92/46/39/85/73 009B:PE:64/99/69/42/68/73
010:PB:65/73/79/31/43/74 011:PD:73/50/51/21/96/47 012:PE:72/58/73/25/72/47 013:SB:57/33/74/78/40/74 014:SD:83/24/47/55/87/51 015:SE:70/28/70/64/69/51
015B:SB:67/27/99/63/54/51 016:MD:58/40/41/59/76/73 017:PE:57/74/57/32/57/75 017B:PB:57/78/73/33/40/75 018:PD:68/90/48/39/89/81 018B:PE:71/94/72/40/71/81
019:SB:68/25/101/58/54/50 020:SD:59/35/41/80/76/97 021:SE:79/38/60/90/59/98 021B:MB:79/52/77/76/42/98 022:MD:60/63/36/43/68/73 023:ME:67/40/59/58/59/73
023B:SB:67/31/76/73/41/73 024:PD:76/70/50/30/92/47 025:SE:53/28/71/64/71/60 026:SB:74/25/96/59/51/51 027:SD:70/23/49/52/91/53 027B:SE:70/24/70/57/69/53
028:SB:56/32/63/76/34/95 029:SD:64/28/49/65/90/51 029B:PE:64/69/69/30/69/51 030:MB:49/36/74/53/40/77 030B:PD:57/62/40/27/75/77 031:PE:58/80/45/34/44/73
032:PB:63/85/70/36/37/97 032B:PD:63/85/37/36/70/97 033:SE:50/40/47/94/47/101 034:SB:95/23/111/54/59/51 034B:SD:94/25/59/58/110/51 035:SE:72/20/73/48/72/35
036:MB:64/80/75/54/40/98 037:SD:55/30/59/70/110/73 037B:PE:61/77/90/33/90/73 038:PB:65/70/74/30/40/77 038B:PD:62/80/38/34/70/81 039:PB:67/55/112/24/60/47
039B:SB:66/23/116/55/63/47 040:SD:86/18/54/42/100/56 041:SE:67/44/62/103/61/101 041B:MB:70/62/83/91/44/101 042:MD:49/63/40/43/74/74 043:SE:65/28/65/64/65/60
043B:PB:65/64/85/28/45/60 044:PD:70/60/46/25/86/60 044B:PE:76/59/66/25/66/60 045:PB:68/80/83/34/45/60 046:SD:74/31/43/72/80/74 047:SE:54/30/54/71/54/75
048:SB:56/35/72/83/39/76 048B:MD:60/47/39/70/73/76 049:PE:74/76/59/32/59/56 050:SB:69/38/73/89/40/74 051:PD:66/97/37/41/68/97 052:PE:74/89/62/38/62/77
053:PB:61/64/93/28/50/47 054:PD:47/106/43/45/80/99 054B:SE:46/45/64/106/64/99 055:PB:73/84/79/36/42/74 055B:PD:73/84/42/36/79/74 056:PE:72/85/72/36/72/56
056B:PB:72/88/93/38/50/56 057:SD:65/35/49/83/91/60 058:PE:51/98/57/42/57/101 059:PB:52/80/62/34/33/73 060:PD:64/76/44/33/83/74 061:PE:65/69/74/29/73/55
062:SB:69/31/84/72/45/60 063:SD:72/37/42/85/77/98 063B:SE:75/37/62/86/62/98 064:PB:54/113/71/49/38/99 064B:MD:53/100/40/68/73/99 065:SE:84/35/93/82/93/55
066:PB:71/91/110/39/59/77 067:PD:60/85/47/37/87/77 068:PE:79/83/88/35/87/52 069:PB:98/70/75/30/40/80 070:SD:74/37/38/85/70/98 071:SE:67/44/61/104/60/102
072:SB:61/44/88/102/48/90 072B:SD:61/44/49/102/92/90 073:ME:70/67/44/46/44/76 073B:MB:72/45/58/67/31/74 074:PD:65/91/45/39/84/82 074B:PE:64/95/65/41/64/82
075:PB:56/95/65/40/35/102 075B:PD:56/93/44/40/81/97 076:SE:67/42/60/98/60/79 076B:SB:67/42/78/98/42/79 077:SD:63/28/41/65/76/74 078:SE:68/43/61/101/61/77
078B:PB:67/101/83/43/44/77 079:SD:67/41/47/97/86/77 079B:PE:71/97/67/41/67/77 080:PB:57/123/75/53/40/103 080B:PD:60/124/40/53/75/104 081:PE:77/99/73/42/73/83
081B:SB:85/41/96/94/52/83 082:SD:67/40/47/93/86/78 083:SE:68/47/56/111/55/101 083B:MB:67/66/72/98/38/101 084:MD:72/76/41/52/76/98 084B:ME:71/54/59/80/58/98
085:PB:58/114/75/49/40/79 085B:PD:58/118/40/50/75/79 086:PE:59/76/67/32/67/73 087:PB:101/91/79/39/43/98 087B:SD:101/39/43/91/79/98 088:SE:66/32/71/75/70/60
088B:SB:66/34/91/79/49/60 089:SD:73/38/51/89/94/79 089B:PE:68/97/72/41/72/79 090:SB:72/45/89/105/48/79 091:PD:71/94/44/40/82/80 091B:PE:68/99/61/43/61/83
092:PB:56/78/72/34/39/77 092B:PD:55/78/41/34/76/77 093:SE:62/42/56/99/55/101 093B:SB:62/42/72/99/39/101 094:SD:72/50/46/115/86/101 094B:MB:71/69/85/102/46/101
095:PB:77/76/116/33/62/56 096:MD:55/68/29/100/54/123 096B:PE:53/119/44/51/43/123 097:PB:50/124/75/53/41/104 097B:PD:50/127/40/55/75/104 098:PE:53/104/48/44/47/106
098B:SB:55/46/63/106/34/102 099:SD:78/34/71/80/133/60 099B:SE:78/36/101/84/101/60 100:MB:60/47/73/70/40/75 100B:MD:60/73/39/50/73/75 101:ME:60/52/67/78/66/101
102:PB:62/95/72/41/39/102 102B:PB:60/104/77/44/42/102 103:PE:58/85/52/37/52/99 103B:PB:58/90/67/38/36/99 104:SD:52/47/46/110/86/106 105:SE:66/52/70/121/69/96
105B:SB:66/52/94/121/50/96 106:SD:63/47/48/110/90/98 107:PE:69/71/104/30/103/47 108:MB:77/54/83/81/45/98 108B:PD:77/98/44/42/83/98 109:PE:78/67/74/29/74/75
109B:PB:81/71/100/30/54/75 110:PD:81/99/52/43/97/98 111:SE:59/49/59/115/59/101 111B:PB:59/115/80/49/43/101 112:SD:66/47/35/110/66/100 112B:ME:66/66/50/97/50/100
113:PB:80/97/104/41/56/87 113B:SD:80/43/56/101/103/87 114:PE:97/78/88/33/88/54 115:PB:82/104/87/44/47/78 116:PD:100/68/63/29/118/51 116B:PE:104/67/91/29/91/51
117:SB:46/45/63/106/34/99 118:SD:72/37/41/86/76/99 118B:SE:71/39/58/90/58/99 119:SB:68/44/97/104/52/77 120:PD:75/107/53/46/98/77 121:ME:78/69/61/103/60/103
121B:PB:80/127/79/54/43/100 122:PD:71/109/44/47/81/104 122B:PE:75/109/63/47/62/104 123:PB:61/94/66/40/36/100 124:SD:60/45/40/106/75/107 124B:SE:61/47/58/109/57/106
125:SB:86/31/99/73/53/51 126:SD:65/26/55/62/102/60 127:ME:54/76/54/52/53/99 128:SB:63/36/94/85/51/54 128B:PD:63/88/51/38/94/54 129:PE:66/106/72/46/72/98
129B:PB:65/107/100/46/54/98 130:PD:67/103/43/44/80/101 130B:SE:51/50/47/118/46/130 131:SD:86/33/72/77/134/49 131B:SD:86/35/72/81/133/49 132:SE:67/41/74/96/74/86
132B:PB:67/100/95/43/51/86 133:MD:62/42/46/62/85/77 134:PE:88/90/63/39/62/105 134B:PB:86/91/88/39/47/105 135:PD:89/102/53/44/99/60 136:PE:64/86/64/37/63/79
137:SB:57/53/90/122/48/100 137B:SD:60/54/50/127/92/100 138:SD:64/46/51/108/95/101 138B:MD:64/64/51/96/94/101 139:PD:82/119/48/51/88/86 140:SE:79/45/72/104/72/79
141:PB:76/92/99/39/54/60 141B:PD:76/96/53/41/98/60 142:PD:58/99/57/43/107/92 142B:PB:57/99/110/43/59/92 143:SD:79/40/50/94/93/82 144:SE:41/26/88/61/87/80
145:SB:65/49/80/113/43/79 146:MD:63/69/52/102/97/100 147:PE:78/88/75/37/75/76 148:SB:77/38/100/87/54/77 148B:PD:81/88/54/37/101/77 149:PE:77/75/77/32/77/55
149B:PB:76/74/104/32/56/55 150:PB:84/81/116/35/62/51 151:SE:62/49/65/115/65/100 151B:SB:62/51/84/118/45/100 152:SD:63/46/44/106/83/82 153:SE:67/45/74/104/73/79
154:MB:72/96/86/64/46/101 155:SD:64/40/50/94/92/80 156:PE:72/99/63/42/63/99 157:PB:73/112/79/48/42/102 157B:PD:74/115/43/49/79/100 158:PE:57/123/72/52/71/99
159:SB:64/46/106/108/57/93 159B:SD:63/48/57/112/106/93 160:SE:73/48/70/111/69/99 161:MB:78/69/86/102/47/101 162:PD:69/72/45/31/84/60 163:SE:64/26/75/59/74/49
163B:PB:63/59/101/25/55/49 164:PD:59/75/35/32/66/76 165:PE:55/74/63/31/63/75 166:PB:55/81/81/35/44/74 167:SD:83/26/50/61/94/47 168:SE:54/35/61/82/61/77
169:SB:55/41/67/95/36/103 169B:MD:54/57/36/85/67/103 170:PE:91/79/87/34/86/56 171:MB:52/78/70/116/37/130 171B:PD:52/138/38/59/71/130 172:PE:46/90/50/38/49/101
173:PB:76/71/104/30/56/56 174:PD:78/111/53/48/97/78 175:SE:77/50/71/117/71/102 176:SB:72/43/94/100/51/78 177:SD:63/50/38/118/71/107 178:ME:60/47/68/69/67/77
179:PB:100/71/111/31/60/47 180:SD:83/33/62/76/114/47 181:PE:67/86/50/37/50/75 182:PB:78/102/107/44/57/77 183:PD:70/112/47/48/87/87 184:PD:59/123/55/53/103/99
185:SB:72/47/82/108/44/103 186:SD:88/40/70/94/130/52 186B:SE:98/38/98/90/98/52 187:SB:80/51/99/119/53/86 188:MD:59/97/46/65/84/105 188B:ME:61/66/65/97/64/107
189:PB:68/121/103/52/55/102 190:PD:76/118/45/51/84/105 191:PE:84/127/73/55/72/99 192:PB:64/148/72/63/38/130 193:SD:102/46/56/106/104/78 194:SE:86/52/75/121/75/83
195:SB:81/57/88/134/47/85 195B:SD:81/57/47/134/88/85 196:ME:60/115/55/77/55/130 197:MB:80/74/85/110/45/103 198:PD:73/123/57/53/105/104 199:PE:73/141/68/61/68/104
200:PB:81/137/90/59/49/103 200B:PD:80/138/54/59/99/103 201:SE:49/61/58/141/58/130 202:PB:53/141/73/61/39/130 203:SD:95/64/60/150/111/130
```

## 付録B. 試算の参照実装（`build_habcds.py` の攻撃区分と `compute` に相当。この出力が付録A と 287/287 一致することを確認済み）

```python
from fractions import Fraction as F
from math import floor, ceil
from statistics import median
def rnd(x): return floor(F(x) + F(1, 2))
def bst6_of(b3): return 320 + floor((F(b3 - 180) * 58 + 32) / 64)
BETA  = {'Physical': F(13, 20), 'Special': F(7, 20), 'Both': F(1, 2)}
ALPHA = {'Physical': F(7, 10), 'Special': F(3, 10)}
ZONE, ZONE_MIN = F(4, 100), 10            # 俊敏ゾーン: BST6 ±4%、最低10種
MAIN_LO, MAIN_HI = F(11, 21), F(3, 5)     # 両刀攻撃の主側比率: 開き10%以上・40:60まで

def zone(sid, table):                      # table: {species_id: (BST6, S)} 全287種
    b = table[sid][0]
    z = [t for t in table if t != sid and abs(table[t][0] - b) <= ZONE * b]
    if len(z) >= ZONE_MIN: return z
    near = sorted((abs(table[t][0] - b), t) for t in table if t != sid)
    return [t for dist, t in near if dist <= near[ZONE_MIN - 1][0]]

def offense_final(sid, base_offense, index, table):
    """base_offense: 特性/周期で決まる候補（Physical/Special/Mixed）。index: species_id 昇順の序数。
    戻り値 (offense, offense_source)。候補が Physical/Special なら source は 'trait' か 'cycle'（呼び出し側が base_offense_of の戻り値で埋める）。"""
    if base_offense != 'Mixed': return base_offense, None
    if F(table[sid][1]) > F(median(table[t][1] for t in zone(sid, table))):
        return 'Mixed', 'cycle'
    return ('Physical' if index % 2 == 0 else 'Special'), 'speed'

def compute(h, a, d, offense, defense, S, index, p, q):
    """p, q: learnset の物理/特殊の攻撃技数（rs_ と SIGNATURE を除く）。両刀攻撃でだけ使う。"""
    b3 = h + a + d; b6 = bst6_of(b3); T = b6 - S
    W = F(76, 100) - F(30, 100) * (F(S) - 35) / 95
    w = min(F(80, 100), max(F(45, 100), W + (F(h + d, b3) - F(2, 3))))
    Bulk = rnd(T * w); Off = T - Bulk
    H = rnd(Bulk * F(h, h + 2 * d)); G = Bulk - H
    B = rnd(G * BETA[defense]); D = G - B
    if offense == 'Mixed':
        main = 'Physical' if index % 2 == 0 else 'Special'          # 主側 = learnset の多数側
        share = F(1, 2) if p + q == 0 else F(p if main == 'Physical' else q, p + q)
        X = rnd(Off * min(MAIN_HI, max(MAIN_LO, share)))
        X = max(ceil(MAIN_LO * Off), min(floor(MAIN_HI * Off), X))   # 主側の値
        A, C = (X, Off - X) if main == 'Physical' else (Off - X, X)
    else:
        A = rnd(Off * ALPHA[offense]); C = Off - A
    return H, A, B, C, D, S
```

入力: `h, a, d` = `legacy_had`、`base_offense` = `base_offense_of(trait, index)`（`'Versatile'` は `'Mixed'`）、`defense` = `defense_of(trait, index)`、
`S` = S1-b の `speed_of`、`table` = 全287種の `(bst6_of(BST3), S)`、`p, q` = 前提コミットの learnset から数えた値。

## 付録C. 両刀候補から再分類する41種と、learnset から削る114行（S1-e）

列: BST6・S・俊敏ゾーンの S の中央値（S ≤ 中央値なので再分類）・再分類先・削る行数・削る技（`move_id(分類 威力)`。印の無いものは手順1〔少数側の威力〕、`[o]` 手順2、`[p]` 手順3、`[t]` 手順4）・削った後の 物理:特殊 の攻撃技数（`rs_`・`SIGNATURE` を除く）。
同じ技が learnset に2行ある種は無い（確認済み）ので、削る行は技で一意に決まる。

| id | 名前 | BST6 | S | 中央値 | 再分類先 | 行 | 削る技 | 後 P:S |
|---|---|---|---|---|---|---|---|---|
| 008 | ミルフィー | 352 | 47 | 73 | 特殊攻撃 | 3 | MVP_009(P75), MVP_015(P90), MVP_022(P40)[t] | 6:16 |
| 015 | パチグリ | 352 | 51 | 73 | 特殊攻撃 | 3 | MVP_071(P80), MVP_074(P90), MV_250(P50)[t] | 6:16 |
| 029 | キツネビ | 347 | 51 | 66.5 | 特殊攻撃 | 4 | MVP_025(P60), MVP_026(P70), MVP_031(P80), gap_electric_p_15(P15)[p] | 4:17 |
| 029B | ユキツネ | 352 | 51 | 73 | 物理攻撃 | 2 | MV_193(S70), MV_196(S85) | 17:6 |
| 035 | コモップ | 320 | 35 | 73 | 特殊攻撃 | 3 | MV_200(P65), MVP_131(P80), MVP_134(P90) | 5:18 |
| 037 | ヘルメッコ | 397 | 73 | 80 | 特殊攻撃 | 3 | MV_221(P55), MVP_045(P65), gap_fire_p_30(P30)[t] | 4:17 |
| 043 | ケルピス | 347 | 60 | 66.5 | 特殊攻撃 | 3 | MV_230(P65), MVP_051(P80), MVP_054(P90) | 5:17 |
| 049 | エアムルグ | 356 | 56 | 73 | 物理攻撃 | 5 | gap_fire_s_60(S60), MV_248(S65), MV_276(S70), gap_neutral_s_70(S70), gap_neutral_s_85(S85) | 16:4 |
| 050 | ニャンシー | 383 | 74 | 74 | 特殊攻撃 | 4 | MVP_165(P65), MV_230(P65), MV_238(P65), MVP_171(P80) | 3:17 |
| 057 | マスクロウ | 383 | 60 | 74 | 特殊攻撃 | 4 | void_MVP_165(P65), MVP_111(P80), void_MVP_171(P80), gap_electric_p_15(P15)[p] | 4:16 |
| 065 | ヤミトバリ | 442 | 55 | 98 | 特殊攻撃 | 2 | MVP_170(P80), gap_dark_p_105(P105) | 5:15 |
| 072B | チョコザラシ | 438 | 90 | 90 | 特殊攻撃 | 3 | MVP_096(P95), MVP_140(P120), MV_288(P55)[t] | 5:17 |
| 078 | フォレーナ | 411 | 77 | 82 | 特殊攻撃 | 5 | MV_245(P55), MV_288(P55), MVP_086(P65), MV_238(P65), MVP_030(P80) | 2:16 |
| 078B | フォレーオ | 415 | 77 | 82 | 物理攻撃 | 3 | void_gap_dark_s_65(S65), void_gap_dark_s_80(S80), volcanic_rain(S90) | 18:5 |
| 079 | クレメーオ | 415 | 77 | 82 | 特殊攻撃 | 4 | MV_288(P55), MVP_165(P65), MV_263(P65), MVP_171(P80) | 3:16 |
| 089 | フラリーナ | 424 | 79 | 82 | 特殊攻撃 | 3 | MVP_109(P75), MVP_115(P90), gap_ground_p_15(P15)[t] | 4:16 |
| 089B | チェリーナ | 429 | 79 | 82.5 | 物理攻撃 | 3 | gap_grass_s_65(S65), gap_fire_s_80(S80), volcanic_rain(S90) | 17:5 |
| 090 | リーファン | 438 | 79 | 90 | 特殊攻撃 | 2 | MVP_112(P85), MVP_118(P110) | 4:16 |
| 095 | フブキツネ | 420 | 56 | 82 | 物理攻撃 | 2 | gap_ice_s_65(S65), MV_196(S85) | 17:4 |
| 106 | ココヤンバ | 456 | 98 | 98 | 特殊攻撃 | 2 | MVP_112(P85), MVP_118(P110) | 4:16 |
| 107 | ドリタス | 424 | 47 | 82 | 物理攻撃 | 3 | mvn_055(S55), gap_grass_s_60(S60), gap_water_s_65(S65) | 16:4 |
| 113 | グラクレス | 465 | 87 | 100 | 物理攻撃 | 2 | gap_ground_s_85(S85), gap_ground_s_105(S105) | 17:5 |
| 113B | ゴルクレス | 470 | 87 | 100 | 特殊攻撃 | 2 | MVP_091(P80), gap_ground_p_105(P105) | 4:15 |
| 119 | チルウィッチ | 442 | 77 | 98 | 特殊攻撃 | 2 | MVP_133(P85), MVP_140(P120) | 5:15 |
| 126 | コロマジロ | 370 | 60 | 74 | 特殊攻撃 | 3 | MVP_082(P50), MV_258(P55), MVP_086(P65) | 4:17 |
| 128 | オオタチウオ | 383 | 54 | 74 | 特殊攻撃 | 3 | MVP_048(P75), MVP_055(P90), MV_282(P40)[t] | 4:17 |
| 132 | ツンドラー | 438 | 86 | 90 | 特殊攻撃 | 2 | MVP_132(P85), MVP_140(P120) | 5:15 |
| 132B | グランドラー | 442 | 86 | 98 | 物理攻撃 | 2 | gap_ground_s_80(S80), sand_twister(S100) | 16:4 |
| 139 | アヌビス | 474 | 86 | 99 | 物理攻撃 | 2 | gap_ground_s_80(S80), sand_twister(S100) | 15:4 |
| 140 | セクメト | 451 | 79 | 98 | 特殊攻撃 | 2 | MVP_090(P80), gap_ground_p_105(P105) | 4:16 |
| 147 | プラムルリム | 429 | 76 | 82.5 | 物理攻撃 | 3 | gap_grass_s_65(S65), void_gap_dark_s_75(S75), gap_grass_s_90(S90) | 18:4 |
| 148 | ナイトメアリ | 433 | 77 | 86 | 特殊攻撃 | 2 | MVP_165(P65), MVP_171(P80) | 4:16 |
| 153 | ヤンデリナ | 442 | 79 | 98 | 特殊攻撃 | 2 | MVP_170(P80), gap_dark_p_105(P105) | 5:15 |
| 155 | ワンフー | 420 | 80 | 82 | 特殊攻撃 | 2 | MVP_007(P70), MVP_015(P90) | 5:16 |
| 162 | ニャルル | 361 | 60 | 73 | 物理攻撃 | 3 | gap_neutral_s_70(S70), gap_electric_s_75(S75), gap_neutral_s_85(S85) | 16:6 |
| 163 | デンツム | 347 | 49 | 66.5 | 特殊攻撃 | 3 | MVP_166(P70), MVP_071(P80), MVP_074(P90) | 6:16 |
| 170 | ネモフィ | 433 | 56 | 86 | 物理攻撃 | 3 | MV_151(S65), gap_ground_s_75(S75), gap_neutral_s_85(S85) | 15:4 |
| 179 | マシュリー | 420 | 47 | 82 | 物理攻撃 | 2 | gap_grass_s_65(S65), gap_grass_s_85(S85) | 17:4 |
| 180 | ヴィランタン | 415 | 47 | 82 | 特殊攻撃 | 3 | MVP_025(P60), MVP_167(P70), MVP_171(P80) | 5:18 |
| 187 | ボルゼクス | 488 | 86 | 100 | 特殊攻撃 | 3 | MV_109(P75), MVP_155(P90), gap_dragon_p_105(P105) | 4:16 |
| 195B | ベラルージュ | 492 | 85 | 100 | 特殊攻撃 | 2 | MVP_171(P80), gap_dark_p_105(P105) | 3:16 |

合計: 41種・114行（手順1: 106、手順2: 0、手順3: 2、手順4: 6）。物理攻撃へ13種（削るのは特殊技）、特殊攻撃へ28種（削るのは物理技）。
