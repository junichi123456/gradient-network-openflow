# 両刀攻撃の BST6≤470 の個体に氷の変化技「プロテクション」を追加する — 仕様書

- **状態: 確定（2026-10-29、ユーザー確認済み）**。ユーザーの確認結果は §0-3。本書のとおりに実装してよい。
- 作成: 2026-10-26（草案）→ 2026-10-29 確定版に改稿（確認結果を反映）
- 対象ブランチ: `claude/mystery-dungeon-roguelike-egnzjt`
- **前提コミット: `472c3b4`**（`Data/moves.json` 627 技・うち変化技 58、`Data/species.json` 287 種、回帰スイート **170 項目**（NG 0 を確認済み）、
  Python 検証の `[PASS]` 数 learnset **20**・attack **25**・npc **9**）。
- 読み手: 会話履歴を持たない実装者（別プロセス）。**この文書だけで実装できるように書いてある。** 書かれていないことは勝手に決めずに報告すること（裁量の範囲は §6-3）。
- **commit・push はしない**（検証者が行う）。Artifact の再公開もしない（検証者が行う）。

---

## 0. ユーザーの指示と位置づけ

### 0-1. 指示（原文）

```
両刀でBST470以下のパルに対し、氷属性の変化技『プロテクション』優先度3 正面3方向にいる味方(北を向いているとき、前、右前、左前)に
プロテクション状態を付与。を習得させます。プロテクション状態は1発だけ、どのような技も無効にします。追加で習得させてください。
```

### 0-2. 背景

- 直前の計測（`docs/design/pvp_spec.md` §32、3,000 試合）で、攻撃区分ごとの BST補正後勝率差は 物理 -1.3 / 特殊 +3.2 / **両刀 -4.9pt**。
  両刀攻撃は不利で、2つめの特性（`trait2`）を与えても効果は検出できなかった。今回は両刀に「守りの手段」を足す試みである。
- **これは試験的な変更である。** 計測結果が基準（攻撃区分 ±3pt）を超えても超えなくても、**調整はせずにそのまま報告する**。
- 試合数の上限は **3,000 試合**（ユーザー指示: テスト量を増やさない）。

### 0-3. ユーザーの確認結果（本書はこれに従う）

| # | 論点 | 決定 |
|---|---|---|
| 1 | NPC に使わせるか | **使わせる。この技だけ特例でスコア評価する**（§3-4 の 11）。相手の NPC も、プロテクション中の個体への攻撃を**得点0として避ける**。他の 58 の変化技は今までどおり使わない |
| 2 | 状態の持続 | **そのサイクルの終わりまで**（使われなければサイクル終了で消える） |
| 3 | 向きの決め方 | **技を出すとき隣接8マスのどれかを指し、その方向を向いて張る**（直線・2マスの技と同じ操作） |
| 4 | 使用者自身 | **守られない**（原文どおり、正面3方向の味方だけ）。正面に味方がいなければ不発 |
| 5 | 計測 | **従来の `--random` 前後比較**（`--random --matches 750 --out` ×4 ＝ 3,000 試合を1回）。前は §32 の「後」の 3,000 試合。同じチームの ON/OFF 対照実験は作らない |

---

## 1. 目的（達成条件）

次の全てを満たせば完了。

1. `Data/moves.json` の**末尾**に技 `mvs_ice_protection`「プロテクション」が1つ追加されている（定義は §3-1。既存 627 技は1バイトも変わらない）。
2. `Data/species.json` で、**両刀攻撃（`offense == "Mixed"`）かつ BST6 ≤ 470 の 31 種（§2-1 の表）だけ**が learnset にこの技を **level 10 で1行**持つ。他の 256 種は持たない。
   追加は `Tools/add_protection_move.py`（新規・冪等）で行われ、再実行しても `git diff Data/` が変わらない。
3. 技を使うと、使用者が指した方向を向き、その正面3マス（§3-2）にいる**味方**（使用者自身は含めない）にプロテクション状態が付く。正面に味方がいなければ不発。
4. プロテクション状態の個体は、他者が使った技が自分に及ぶとき、**その技の1回の使用ぶん**（多段技なら全段、追加効果も含めて）を完全に無効にし、状態が消える（§3-3）。
   使われなければ**そのサイクルの終わり**（`BattleScheduler.EndCycle`）で消える。
5. 優先度 3 で、行動順は既存の全ての手より先か同順になる（§2-4）。
6. NPC（`NpcOpponent`）が、まだ動いていない敵の攻撃が届く味方が正面3マスにいる場面でこの技を選び得る。プロテクション中の個体への攻撃は得点0として避ける。他の 58 の変化技は従来どおり使わない。
7. `dotnet build MysteryDungeonRoguelike.csproj` 0 エラー。回帰スイートで NG を含む `[検証` 行 0 を **6 回連続**（170 項目＋本書で足す 16 項目＝**186 項目**。§4-2）。
8. Python 検証 3 本が ALL PASS（`[PASS]` 数: learnset **21**（#21 新設）・attack **26**（1件更新＋1件新設）・npc **9**）。
9. learnset インスペクタ・技データベース エディタのファイルを再生成済み（§3-5。再公開は担当外）。
10. §5 の手順で **3,000 試合ちょうど**を1回計測し、`docs/design/pvp_spec.md` に **§33** を追記している。

---

## 2. 現状（実装者向けの要約）

### 2-1. 対象個体（実データ。`472c3b4` の `Data/species.json` から計算）

- 両刀攻撃 = `offense == "Mixed"` の **38 種**。BST6 = `base_hp + base_atk + base_def + base_spatk + base_spdef + base_speed`（HABCDS の6能力合計）。
- BST6 ≤ 470（470 を含む）の **31 種**が対象。除外される両刀 7 種（本書の「非対象の両刀」）: 094B パリピドン(474)・121 レヴィドラ(474)・146 ゼノルーダ(483)・161 オーマサンダ(483)・171 レイバーン(483)・196 ゼノドラン(492)・197 レジェンディア(497)。
- 「4技に入る確率」は `--random` の技選び（learnset から重複なしで無作為に4つ。§2-7）で、追加後にこの技が4技に入る確率 = 4 ÷（追加後の技数）。平均 **13.9%**。

| # | 個体 | 属性 | BST6 | S | 耐久区分 | 特性 / 特性2 | learnset 技数（攻撃/変化） | うち氷の変化技 | 追加後の技数 | 4技に入る確率 |
|---|---|---|---|---|---|---|---|---|---|---|
| 1 | 030 ヤミイカ | 虚/水 | 329 | 77 | 物理 | どくせんボディ / — | 32（27/5） | 0 | 33 | 12.1% |
| 2 | 022 ネムラム | 虚 | 343 | 73 | 特殊 | チェイサー / — | 29（24/5） | 0 | 30 | 13.3% |
| 3 | 042 ボルトラ | 電 | 343 | 74 | 特殊 | けいかいかん / — | 27（22/5） | 0 | 28 | 14.3% |
| 4 | 016 ンダコアラ | 虚 | 347 | 73 | 特殊 | どくせんボディ / — | 28（23/5） | 0 | 29 | 13.8% |
| 5 | 073 ダリザード | 虚 | 347 | 76 | 両面 | こうふん / — | 30（25/5） | 0 | 31 | 12.9% |
| 6 | 073B チリザード | 炎 | 347 | 74 | 物理 | こうふん / — | 27（22/5） | 0 | 28 | 14.3% |
| 7 | 023 エテッパ | 草 | 356 | 73 | 両面 | ふわふわ / — | 27（22/5） | 0 | 28 | 14.3% |
| 8 | 007 ルミカイト | 水 | 365 | 76 | 特殊 | ディープダイブ / — | 28（23/5） | 0 | 29 | 13.8% |
| 9 | 048B ユメンダコ | 水/無 | 365 | 76 | 特殊 | ディープダイブ / — | 30（25/5） | 0 | 31 | 12.9% |
| 10 | 100 ハグミー | 草 | 365 | 75 | 物理 | レッツハギング / しみこみ | 29（24/5） | 0 | 30 | 13.3% |
| 11 | 007B エレカイト | 水/電 | 370 | 76 | 両面 | ぜつえんたい / — | 32（27/5） | 0 | 33 | 12.1% |
| 12 | 100B ハグユー | 闇/草 | 370 | 75 | 特殊 | レッツハギング / — | 31（26/5） | 0 | 32 | 12.5% |
| 13 | 133 ユキボウ | 氷 | 374 | 77 | 特殊 | 氷のちから / ワイドバースト | 27（22/5） | 4 | 28 | 14.3% |
| 14 | 127 ヤクモマル | 無 | 388 | 99 | 両面 | 無のちから / ワイドバースト | 29（24/5） | 0 | 30 | 13.3% |
| 15 | 178 メドゥーナ | 闇 | 388 | 77 | 両面 | 闇のちから / こわざじょうず | 28（23/5） | 0 | 29 | 13.8% |
| 16 | 169B トノサマンボ | 水/電 | 402 | 103 | 特殊 | 水のおしえ / みずたまりのぬし | 30（25/5） | 0 | 31 | 12.9% |
| 17 | 036 ドンモップ | 氷 | 411 | 98 | 物理 | もふもふのリーダー / — | 27（22/5） | 4 | 28 | 14.3% |
| 18 | 084 アロアリュー | 草/虚 | 415 | 98 | 特殊 | 草のちから / はなばたけのぬし | 30（25/5） | 0 | 31 | 12.9% |
| 19 | 084B ビリビリュー | 電/虚 | 420 | 98 | 両面 | 雷のちから / ワイドバースト | 30（25/5） | 1 | 31 | 12.9% |
| 20 | 021B アオパオー | 氷 | 424 | 98 | 物理 | 冷却のリーダー / — | 27（22/5） | 4 | 28 | 14.3% |
| 21 | 101 ツララジカ | 氷 | 424 | 101 | 両面 | 氷のまもり / うすらひのぬし | 29（21/8） | 5 | 30 | 13.3% |
| 22 | 096 ライバード | 電 | 429 | 123 | 特殊 | 雷のおしえ / こわざじょうず | 26（21/5） | 0 | 27 | 14.8% |
| 23 | 112B シンエンオ | 炎/闇 | 429 | 100 | 両面 | フリーフォール / — | 25（20/5） | 0 | 26 | 15.4% |
| 24 | 064B シロカバネ | 氷/闇 | 433 | 99 | 特殊 | フリーフォール / — | 30（25/5） | 3 | 31 | 12.9% |
| 25 | 108 ラブラドン | 草 | 438 | 98 | 物理 | 草のちから / ワイドバースト | 26（21/5） | 0 | 27 | 14.8% |
| 26 | 083B ムラサメ | 電 | 442 | 101 | 物理 | 雷のきずな / こわざじょうず | 24（19/5） | 0 | 25 | 16.0% |
| 27 | 041B アズルーナ | 氷/虚 | 451 | 101 | 物理 | たまゆき / — | 27（22/5） | 1 | 28 | 14.3% |
| 28 | 188 ホルス | 炎 | 456 | 105 | 特殊 | 炎のちから / しみこみ | 25（20/5） | 0 | 26 | 15.4% |
| 29 | 188B イシス | 水 | 460 | 107 | 両面 | 水のちから / しみこみ | 24（19/5） | 1 | 25 | 16.0% |
| 30 | 154 キンガゼル | 地 | 465 | 101 | 物理 | 地のおしえ / こわざじょうず | 25（20/5） | 0 | 26 | 15.4% |
| 31 | 138B ノロウゾー | 地/虚 | 470 | 101 | 特殊 | ハードブロック / — | 29（24/5） | 0 | 30 | 13.3% |

- 属性の内訳（重複計上）: 虚 8・水 6・電 6・氷 6・草 5・闇 4・炎 3・無 2・地 2。S≥95 は 17 種。`trait2` 持ちは 14 種、無しは 17 種。
- 対象の species_id（昇順。スクリプトの定数・回帰の期待値に使う）:
  `007 007B 016 021B 022 023 030 036 041B 042 048B 064B 073 073B 083B 084 084B 096 100 100B 101 108 112B 127 133 138B 154 169B 178 188 188B`
- NPC 編成（`Data/npc_teams.json`）に入っている対象個体: npc_fire 073B・npc_water 007B・npc_grass 084・npc_electric 096/083B・npc_ice 064B/041B・npc_ground 154・npc_dark 138B。
  NPC 編成の技4つ（`move_ids`）は固定値なので、**learnset に足しても NPC 編成は変わらない**（再生成もしない。§7）。

### 2-2. 技データの形（`Data/moves.json`、`Scripts/Combat/MoveData.cs`・`MoveDatabase.cs`）

- 1技 = JSON の1オブジェクト。必須キー `id, name, type, category, power, accuracy, max_pp, range`、任意キーは `MoveDatabase.cs` の `MoveJson`（未指定は C# の既定値）。
- 変化技は 58 種（全て `power: 0, accuracy: 100`、PP は 10/15/16、射程は Adjacent 57・Area 1）。id の接頭辞は `mvs_`（変化技の拡張。`Tools/add_status_moves.py`）・`mvw_`（天候）・`MV_` など。
  習得レベルは変化技の強さの段で 1 / 4 / 7 / 10（10 が最上位: `mvs_ep_*` の属性威力+2、`poison_fog`）。
- **優先度（`priority`）を持つ技は現在 1 つだけ**: `MV_140` ヘッドバット（+1）。他は全て 0。
- `MoveRange`（`MoveData.cs`）: Adjacent / Line / TwoTile / Area / Room / FullFloor / Surrounding。**JSON の未知の射程名は黙って Adjacent に落ちる**（`MoveDatabase` の `Enum.TryParse`）ので、新しい射程は enum に足さないと動かない。
- 向き（facing）を使う既存の射程は Line・TwoTile（`TargetResolver.ResolveTiles` が `user.FacingDirection` 方向へ `CastRay`）。

### 2-3. 向き（facing）の実装

- `Entity.FacingDirection`（`Vector2I`、**8方向の単位ベクトル**。初期値 (0,1)）。変わるのは `Entity.FaceDirection(dir)`（符号を取って単位化）と、移動・攻撃時の `UpdateFacingDirection`。
- 対戦盤（`Scripts/Battle/BattleBoard.cs`）: 7×8。論理座標で Player が下側（Y 大）で**正面は (0,-1) = 北**、Enemy は上側で正面は (0,1)。配置時に `BattleBoard.Facing(faction)` を向く。
  自陣は縦2×横3（Player: X=2..4, Y=4..5 / Enemy: X=2..4, Y=2..3）で、上列（相手に近い列）どうしが接している。
- **対戦で技を出すとき、向きは狙い先で決まる**: `Scripts/UI/Battle/BattleFlow.cs` の `BuildCommitment`（535 行付近）が `StepToward(actor.GridPosition, input.Target)` を求め、0 でなければ `actor.FaceDirection(dir)` する。
  NPC も人も同じ経路（headless の `BattleMatchScene` もここを通る）。
- 8方向の並び（時計回り）: `BattleTargeting.Directions` = N(0,-1), NE(1,-1), E(1,0), SE(1,1), S(0,1), SW(-1,1), W(-1,0), NW(-1,-1)。

### 2-4. 行動順と優先度（`Scripts/Battle/BattleScheduler.cs`）

- 1ターン = 両陣営が1匹ずつ1行動（計2行動）。1サイクル = 全員が1回ずつ（4v4 なら 4 ターン）。
- `Order(a, b)`: **優先度の高い順 → 俊敏の実数値が高い順 → BST6 が低い順 → 乱数**。同じターンの2行動の順番だけを決める。
- 優先度は `EffectivePriority(actor, move)` = 技の `Priority` ＋ 水のちから（天候あめで +1）− フロストラグ（`PriorityPenalty` 1）。`BattleFlow.BuildCommitment` が移動（move=null）・技の両方で呼ぶ。
- 既存の最大は ヘッドバット 1 ＋ あめ 1 ＝ **2**。プロテクションの 3 は（フロストラグで -1 されても 2 で）**常に同じターンの相手の手より先か同順**。同優先度（両者がプロテクション）なら俊敏 → BST6 → 乱数（既存の規則のまま）。
- サイクル終了: `BattleScheduler.EndCycle()` が全生存者の `Entity.ResolveStatusTick()` を呼ぶ（状態異常のターン経過・継続ダメージ）。

### 2-5. 攻撃の処理（`Scripts/Turn/AttackAction.cs`）

`Execute`（81 行〜）の順序:
1. 制限技による使用不可（`RestrictionBlocks`。挑発中は変化技不可、射程制限中は Adjacent 以外不可）→ PP 切れ → ざんきょうのしゅごしゃ（攻撃技のみ禁止）→ **PP -1**・`LastUsedMoveSlot`。
2. 使用時効果: 自身の気絶（大技の隙）、きぬぬい（**氷技を使うと** 被ダメ軽減を1回ぶん装填）。
3. `ApplyFieldEffect`（設置技）→ `ApplyWeatherEffect`（天候技）。どちらも**使用時に解決して `return`**（対象を取らない）。
4. `PerformedAttack` → 射程が Adjacent なら `ExecuteSingle`、それ以外は `ExecuteAoe`。
5. `TriggerChasers`（チェイサー）→ `ApplySelfDestruct`。

- `ExecuteSingle`（175 行〜）: 対象なし → 自分だけに効く変化技（`IsSelfContained` かつランク変化あり）は自分に適用、他は空振り。対象あり → 変化技は `ApplyRankEffectIfAny` / `ApplyAilmentEffectIfAny`、攻撃技は `Strike` →（ダメージ>0 なら）追加効果・ノックバック・制限、→ `ApplyRendClear / ApplyDrain / ApplyRecoil / ApplyFundoRecoil`。
- `ExecuteAoe`（226 行〜）: `TargetResolver.Resolve` の対象リスト → 自己ランク1回 → エリアイージス（範囲技で、所持者以外の味方を対象外にする）→ 各対象ごとに 変化技なら状態異常・ランク、攻撃技なら `Strike` と追加効果。
- `Strike` = 多段技なら `StrikeMultiHit`（`StrikeTarget` を複数回）、それ以外は `StrikeTarget`。
- **既存の「完全無効」**（ダメージも追加効果も 0）は全て `StrikeTarget` の**命中判定の後**・ダメージ計算の前で `return 0` する形: ワイドウォード（持ち物。Room/Area を受けない）、ぜつえんたい（電気無効）、ディープダイブ（水無効）。
  **ただし `StrikeTarget` は攻撃技しか通らない**（変化技は `ExecuteSingle`/`ExecuteAoe` で直接適用される）うえ、多段技は `StrikeTarget` を段数ぶん呼ぶので、ここで消費すると1段目だけ防いで2段目が通ってしまう。
  そのため本書の判定は `StrikeTarget` ではなく、対象単位の入口（`ExecuteSingle`・`ExecuteAoe`）に置く（§3-4 の 9）。
- その他の守り系の既存実装（いずれも今回の仕組みとは別物で、流用しない）: エンデュアチャーム、ガード/ラストトニック、クリット/ウィークシェル、ルームミラー、エリアイージス、ランクアンカー、キュアベル（`Data/items.json`）、
  きょうじんなからだ（`DefenderBlocksSecondaryEffects`）、ビルドアップ（`HasBuildUpShield`）、きぬぬい（`ArmDamageReduction` / `ConsumeDamageReductionIfArmed`）、回避ランク。
  **「1回だけ全部を無効にする」状態は既存に無い。** 形はきぬぬい・ディープダイブの「1つのフラグを立てて、次の1回で消す」に揃える。

### 2-6. 状態の保持（`Scripts/Entities/StatusEffectManager.cs`）

- 状態異常スロット（`Ailment`、9種排他）・気絶（`IsStunned`）・制限（`_restrictions`、複数同時）・各種1回きりのフラグ（`_damageReductionArmed`・`_deepDiveCharged` 等）を持つ。
- `Reset()`（667 行〜）が全部を消す（迷宮の階移動、回帰の `Fresh()`）。新しい状態もここで消すこと。

### 2-7. NPC の技選択（`Scripts/Battle/NpcOpponent.cs`）と構築

- `Decide()`（160 行〜）の枠組み:
  1. 冒頭で `threats`（まだこのサイクルで動いていない生存中の敵）と、その攻撃技（威力>0・PP>0）ごとの「届くマスの集合」`threatMoves` を1回だけ求める（`Reach == null` は盤面全体に届く）。
  2. 出せる各個体 `actor`（`mine` の並び順）について、その場に留まる被弾見積もり `stayPenalty = SurvivalPenalty(actor, EstimateThreat(...), CycleNumber)` を求め、
     技枠 slot 0→3 × `SelectableTiles` の並び順の各 aim で `score = ScoreAttack(...) - stayPenalty` を、次に移動先の各マスで `ApproachBonus − SurvivalPenalty` を計算する。
  3. `if (score <= bestScore) continue;` の**厳密な大小比較**で最大の1手を残す。同点なら**先に評価した手が残る**（列挙順のタイブレーク）。
  4. 候補が1つも無ければ最初の個体がその場に留まる。
  - **`Decide` は乱数を一切使わない**（乱数は選出の `SelectionScore` の `GD.Randi()` だけ）。
- **`if (data.Power <= 0) continue;  // 変化技は評価しない`（230 行付近）** で変化技は一切選ばれない。
- 攻撃の得点 `ScoreAttack` = Σ（威力 × 相性 × 命中/100）＋ 倒しきれる相手に +120、味方を巻き込むと −1.5 倍。`SurvivalPenalty` は最大約 102。
- `--random`（`Scripts/Battle/BattleMatchScene.cs` `RandomTeam`）: 種族6・**技は learnset（重複を畳んだもの）から無作為に4つ**・持ち物16種から重複なく全員。乱数は `rng.Randomize()`（固定シードではない）。判断は両陣営とも `NpcOpponent`。
  → 対象個体が出場すると、約 13.9% の確率でこの技を4技に持つ。
- `DefaultLoadout.PickMoves`（`--strongest`・メタ系・手持ち既定）は 自属性最大威力 → 他属性 → 威力順 → 最後に変化技 なので、対象個体（攻撃技 19 以上）では**この技は選ばれない**。
  `TacticalBuilder`（`--tactics`）の技選びも、搦め手（ランク変化・状態異常・設置を持つ変化技）と攻撃技を優先し、残りを変化技で埋めるだけなので、この技は選ばれない（攻撃技が4未満の個体は対象にいない）。

### 2-8. 検証規則（Python）への影響（`Tools/` と `Data/` を一時ディレクトリに複製し、§3-1 の技と 31 行を足して実際に回した結果）

- `Tools/verify_learnset.py`: **ALL PASS のまま**（#1〜#20）。変化技に関わる規則は #11「変化技 2 つ以上」（下限のみ）だけで、上限・属性ごとの変化技数の規則は無い。
  #17「S≥95 は優先度+1以上の**攻撃技**（威力>0）を持たない」は `learnset_rules.fast_blocks` が `power > 0` を条件にしているので、優先度3の変化技は対象外（対象 31 種中 17 種が S≥95 だが違反 0）。
  #5・#9・#10・#12〜#15 は威力>0 の技だけを数える。**規則を緩める必要は無い。**
- `Tools/verify_attack_moves.py`: **1 件 FAIL** —「変化技58種が手つかず」（55 行。変化技の総数が 58 であることを直接数えている）。§3-5 の 4 で書き換える（緩めるのではなく、対象を正確にする）。
- `Tools/verify_npc_teams.py`: ALL PASS のまま。
- `Tools/generate_learnset.py` を再実行すると learnset が作り直され、この技（と rs_ 制限技）が消える。**生成のあとに `add_restriction_moves.py` → `add_protection_move.py` の順で足す**手順にする（docstring に書く）。

### 2-9. 回帰スイート（`Scripts/Battle/BattleTestScene.cs`）への既存の影響

- 既存の項目で、この追加により結果が変わるものは無い見込み: 手持ち/敵の既定 12 種（001〜012）のうち対象は 007 だけで、`BuildTeam` は learnset 先頭4件（level 1〜4）を使うので level 10 の行は入らない。
  `.First(m => m.Category == Status ...)`（1098・2225 行付近）は moves.json の先頭から探すので、**末尾に足せば**選ばれる技は変わらない。
  ヘッドバット以外の優先度を見る項目は `MV_141` 1技だけ（449 行）。S≥95 の項目（1000 行付近）は威力>0 だけを数える。

---

## 3. 変更内容

### 3-1. 技の定義（`Data/moves.json` の**末尾**に1件）

```json
{
  "id": "mvs_ice_protection",
  "name": "プロテクション",
  "type": "Ice",
  "category": "Status",
  "power": 0,
  "accuracy": 100,
  "max_pp": 10,
  "range": "FrontArc",
  "priority": 3,
  "grants_protection": true
}
```

| 項目 | 値 | 理由 |
|---|---|---|
| id | `mvs_ice_protection` | 変化技の拡張の接頭辞 `mvs_` ＋ 属性 ＋ 効果（`mvs_ice_atk_crit` 等と同じ並び）。既存と衝突しない（確認済み） |
| name | プロテクション | 原文どおり。既存の技名と衝突しない（確認済み） |
| type / category / power | Ice / Status / 0 | 原文どおり（氷属性の変化技） |
| accuracy | 100 | 変化技は全て 100。命中判定は行わない（外れない） |
| max_pp | 10 | 変化技の最上位段（`mvs_ep_*`・`mvs_big_*`・`mvs_toxic_dark`）と同じ |
| range | `FrontArc`（新設） | 正面3マス（§3-2） |
| priority | 3 | 原文どおり |
| grants_protection | true（新設キー） | 効果の印。射程と効果を分けて持つ（`FrontArc` は射程の形だけを表す） |

- 習得: 対象 31 種（§2-1）の learnset に `{"level": 10, "move_id": "mvs_ice_protection"}` を1行。**挿入位置は「level が 10 以下の最後の行の直後」**
  （learnset は level 昇順で並んでいるが、同 level 内の並びが move_id 順でない個体が 29 種ある（対象では 016・022・030・041B・073・084・084B・138B）ので、**全体を並べ替えない**）。
  level 10 は既存の変化技の最上位段。対人戦は習得レベルを見ない（`BattleEntry.Learnable`）ので、level が効くのは迷宮だけ。

### 3-2. 射程「正面3マス」（`MoveRange.FrontArc`）

- **形**: 向き `f`（8方向の単位ベクトル）について、`f` と、`BattleTargeting.Directions` の並びで `f` の**両隣**（時計回り ±45°）の3方向へ1マスずつ。盤外・壁のマスは除く。`f` が (0,0) なら空。
  - 北 (0,-1) を向いているとき: 前 (0,-1)・右前 (1,-1)・左前 (-1,-1)（原文どおり）。
  - 北東 (1,-1) を向いているとき: (1,-1)・(1,0)・(0,-1)。斜めを向いたときも「向きと、その両隣の45°」で一般化する。
- **向きの決め方**: 技を出すときに**隣接8マスのどれかを指し**、その方向を向いてから正面3マスに効く（Line・TwoTile と同じ操作）。
  `BattleFlow.BuildCommitment` が既に「狙い先の方向を向く」ので、`SelectableTiles` が隣接8マスを返せばよい。
- **対象**: そのマスに立っている**生存している味方（使用者と同じ `Faction`）だけ**。敵が立っていても何も起きない。**使用者自身は含めない**。
- 正面3マスに味方が1体もいなければ**不発**（PP は減り、手番は消費する。ログを出す）。
- **`BattleTargeting.cs` と、UI 側の写し `BattleFlow.cs`（`SelectableTiles`・`Preview`）の両方を直す**（§3-4 の 4・5）。片方だけ直すと、UI と NPC で選べるマス・予告が食い違う。

盤面で機能する場面の見積もり（参考）: 開始時の NPC 配置（`NpcOpponent.ChooseDeployment`）は、自陣6マスのうち相手に近い列3マスを先に埋め、残り1体を後列の端（Player なら (2,5)）に置く。
後列の1体は北を向けば前列の2体（(2,4)・(3,4)）が正面3マスに入る。前列中央の個体も横（西/東）を向けば前列の隣と後列の1体の計2体を覆える。
つまり**序盤は「正面に味方が2体」の配置が普通に起きる**。移動や撃破で陣形が崩れると減る。

### 3-3. プロテクション状態

| 項目 | 定義 |
|---|---|
| 付与 | プロテクションの効果で、正面3マスの味方に付く。既に付いている個体には**重ねない**（1回ぶんのまま、持続も延ばさない）。 |
| 持続 | **そのサイクルの終わりまで**（`BattleScheduler.EndCycle` で消える）。使われずに消えた数を数える（§3-4 の 8）。迷宮（サイクルが無い）では消費されるか `StatusEffectManager.Reset()`（階移動）まで残る。 |
| 何が「1発」か | **他者が使った1つの技の、1回の使用のうち、この個体に及ぶぶん全部**。多段技は全段、範囲技はこの個体のぶんだけ（同じ範囲技の他の対象には通常どおり当たる）。 |
| 無効にする範囲 | その技がこの個体に与えるはずだったもの全部: ダメージ、状態異常（蓄積・確定付与・気絶）、ランク変化、制限技の制限、引き寄せ・ノックバック・PP 削り、特性由来の蓄積（レッツハギング等）、急所で発動する被弾側の特性（きょうしんぞう）、被弾で発動する持ち物（トニック・ルームミラー等。ダメージ 0 なので発動しない）。 |
| 判定の時点 | その技がこの個体に及ぶことが決まった時点で、**命中判定より前**。外れるはずだった技でも、無効化して状態を消費する。 |
| 消費 | 無効化した時点で状態が消える。 |

「どのような技も」の範囲:

| 種類 | 防ぐ | 消費する | 理由・備考 |
|---|---|---|---|
| 敵の攻撃技（単体・直線・2マス・範囲・部屋・周囲・全体・多段） | ○ | ○ | 原文どおり |
| 敵の変化技でこの個体に効くもの（ランク下げ・状態異常。単体・範囲とも） | ○ | ○ | 「どのような技も」 |
| **味方の技**（範囲技の巻き込み、単体で味方を殴る） | ○ | ○ | 「どのような技も」。味方の範囲技に巻き込まれると状態が消える |
| 自分の技（自分は対象にならない: `TargetResolver` は使用者を除く） | — | — | 反動・自身の気絶・メガデストラクトの自滅は使用者の代償なので通る |
| プロテクション自身（付与の対象になる） | — | ✕ | 付与は「及ぶ」とみなさない（重ねがけは何も起きない） |
| 自分だけに効く変化技（`IsSelfContained`: 自己ランク上昇のみ）を、この個体のいるマスへ向けて使われた | — | ✕ | 対象に何も及ばない |
| 設置技（シンアイス・クレバス等）・天候技 | ✕ | ✕ | 使用時に盤面を変えるだけで、個体を対象に取らない |
| 地形・設置物のダメージや効果、天候ダメージ、状態異常のサイクル終了時ダメージ（毒・猛毒等） | ✕ | ✕ | 技ではない |
| ノックバックで**ぶつけられた側**（`ApplyKnockback` の occupant）のダメージ | ✕ | ✕ | 技の対象ではなく衝突の副作用。殴られた本人が守られていればノックバック自体が起きない |
| ざんきょうのしゅごしゃを守護者が受けるぶん | ✕ | ✕ | 守護者は対象ではない。守られた個体のダメージが 0 なら肩代わりも起きない |
| ルームミラーの反射ダメージ（使用者が受ける） | ✕ | ✕ | 持ち物の効果で、技ではない |
| 投げる道具（`ThrowItemAction`） | ✕ | ✕ | 技ではない |
| チェイサー（味方の攻撃に反応してワープ） | — | — | 守られた個体に何も及ばない。従来どおり発動する |

使用者側への波及:
- 守られた相手へのダメージは 0 として扱う → 反動・吸収（ダメージ比例）は、その相手ぶんが 0 になる（他の対象に当たったぶんは通常どおり）。
- 使用時効果（自身の気絶・きぬぬいの装填・メガデストラクトの自滅・レンドの足下消去・チェイサー）は、防がれても従来どおり起きる（既存の「外れた」ときと同じ）。
- 範囲技の自己ランク変化（`ApplySelfRankOnce`）は従来どおり1回（対象リストが空でなければ）。
- エリアイージスで対象外にされた個体はプロテクションを消費しない（イージスの判定が先）。

### 3-4. C# の変更（ファイルごと）

1. **`Scripts/Combat/MoveData.cs`**
   - `MoveRange` に `FrontArc`（コメント: 正面3マス。向きとその両隣45°の3方向へ1マス）を**末尾に**足す。
   - `MoveData` に `public bool GrantsProtection { get; set; } = false;`（コメント: プロテクション。正面3マスの味方にプロテクション状態を付ける）。
2. **`Scripts/Combat/MoveDatabase.cs`**: `MoveJson` に `[JsonPropertyName("grants_protection")] public bool GrantsProtection { get; set; }`、`MoveData` への写し `GrantsProtection = entry.GrantsProtection`。
3. **`Scripts/Turn/TargetResolver.cs`** `ResolveTiles`: `case MoveRange.FrontArc:` を足す。`facing` が 0 なら空。`facing` の8方向の並び（§2-3 と同じ順）上の位置 i に対し i, i+1, i−1（mod 8）の3方向へ `userPos + d`、`grid.InBounds` かつ壁でないマスを追加。
   （`BattleTargeting.Directions` は `MysteryDungeon.Battle` にあるので、同じ並びの配列を `TargetResolver` 内に private で持ってよい。）
   `Resolve` は既存の「タイル集合に立っている候補を返す」分岐（Area 等と同じ）でよい（使用者は候補から除かれている。敵味方の選別は効果側で行う）。
4. **`Scripts/Battle/BattleTargeting.cs`**
   - `SelectableTiles`: `case MoveRange.FrontArc:` で隣接8マス（`Adjacent` と同じく `Directions` の順で、`grid.IsWalkable` のもの）を返す。
   - `ImpactTiles`: `FrontArc` は Line/TwoTile と同じく `StepToward(actor.GridPosition, aim)` を向きとして `ResolveTiles`（aim が自分のマスなら `actor.FacingDirection`）。
   - `Victims`: 変更不要（Line/TwoTile 以外は一時的に向きを狙い方向へ合わせて `TargetResolver.Resolve` を呼び、元へ戻す既存の経路に乗る）。
5. **`Scripts/UI/Battle/BattleFlow.cs`**: `SelectableTiles`（395 行付近、BattleTargeting と同じ規則の UI 側の写し）に 4. と同じ `FrontArc` の case（隣接8マス）を、`Preview`（428 行付近）に 4. の `ImpactTiles` と同じ扱いを足す。
6. **`Scripts/UI/Battle/TeamBuildScreen.cs` / `DeployScreen.cs`** の `RangeLabel`: `MoveRange.FrontArc => "正面3"`。
7. **`Scripts/Entities/StatusEffectManager.cs`**
   ```csharp
   // プロテクション: 次に自分に及ぶ他者の技1回ぶんを完全に無効にする（1回きり、重ねない）。
   // 対戦ではサイクル終了で消える（BattleScheduler.EndCycle）。
   public bool IsProtected { get; private set; }
   public bool GrantProtection() { if (IsProtected) return false; IsProtected = true; return true; }   // 新しく付いたら true
   public bool ConsumeProtection() { if (!IsProtected) return false; IsProtected = false; return true; }
   ```
   `Reset()` で `IsProtected = false`。
8. **`Scripts/Combat/ProtectionStats.cs`**（新規。`RestrictionStats` と同じ形の static カウンタ）: `long Used`（技が実行された回数。不発を含む）、`Granted`（新しく付いた体数）、`Blocked`（無効化した回数）、`Expired`（使われずにサイクル終了で消えた体数）、`Reset()`。
9. **`Scripts/Turn/AttackAction.cs`**
   - `Execute`: `if (ApplyWeatherEffect(move)) return;` の直後に `if (ApplyProtectionEffect(move)) return;`。
   - `ApplyProtectionEffect(MoveData move)`: `!move.GrantsProtection` なら false。
     向き: `_aimTile` があり使用者のマスと違えば `StepToward` の方向を `_attacker.FaceDirection` で向く（`BuildCommitment` が既に向けているが、`AttackAction` を直接作る回帰・迷宮の経路のため）。
     タイル = `TargetResolver.ResolveTiles(MoveRange.FrontArc, 位置, _attacker.FacingDirection, …)`。そのタイルに立つ生存している同陣営の個体（使用者を除く。`_floorController.AllActors()`）へ `GrantProtection()`。
     ログ（日本語。`MessageLogger.Log` と `GD.Print("[Battle] …")`）: 付いた個体ごとに「{名前}はプロテクションで守られた!」、既に付いていた個体は「{名前}は既に守られている」、誰もいなければ「{使用者}のプロテクション! しかし正面に味方がいない」。
     `ProtectionStats.Used++`、新しく付いた数を `Granted` に足す。`true` を返す（手番はここで終わる。`PerformedAttack` は false のまま、チェイサーも起きない。天候技と同じ）。
     泥まみれ・きょうじんなからだでは止めない（天候技・設置技と同じく、技の主効果であって追加効果ではない）。
   - `private bool BlockedByProtection(MoveData move, Entity target)`: `target == null || target == _attacker || move.GrantsProtection` なら false。`target.StatusEffects.ConsumeProtection()` が true なら
     ログ「{target}のプロテクションが{技名}を防いだ!」、`ProtectionStats.Blocked++`、true。
   - `ExecuteSingle`: 対象がいる分岐（`PlayBumpAttack` の後）で、
     - 変化技: `move.IsSelfContained` なら従来どおり（消費しない）。そうでなく `BlockedByProtection(move, _defender)` なら、その技の効果を何も適用せずに return（現行データに「自分と相手の両方に効く変化技」は無い。§6-2）。
     - 攻撃技: `int damage = BlockedByProtection(move, _defender) ? 0 : Strike(move, _defender);`（以降は既存のまま。damage 0 なので追加効果・撃破処理は飛び、反動・吸収は 0）。
   - `ExecuteAoe`: 対象ループで、エリアイージスの「庇われた側は対象外」の `continue` の**直後**に `if (BlockedByProtection(move, target)) continue;`（変化技・攻撃技の両方の前）。
   - `StrikeTarget` には手を入れない（多段の段ごとに消費しないため）。
10. **`Scripts/Battle/BattleScheduler.cs`** `EndCycle()`: 既存の `ResolveStatusTick` のループ（生存者）で、各個体について `if (e.StatusEffects.ConsumeProtection()) ProtectionStats.Expired++;` を足す。
    順序は `ResolveStatusTick()` の**後**（継続ダメージは防がないので、順序で結果は変わらないが、回帰 P10 の書き方に合わせて固定する）。
11. **`Scripts/Battle/NpcOpponent.cs`**（プロテクションだけの特例。他の 58 変化技の扱いは変えない）
    - **入る位置**: `Decide` の技ループ（§2-7 の 2）の `if (data.Power <= 0) continue;` を次に置き換える。PP 切れ・`RestrictionBlocks` の除外は攻撃技と同じに先に行う（既存の2行の順序を、PP 判定 → 威力0の分岐の中で `RestrictionBlocks` → … とする）。
      ```csharp
      if (data.Power <= 0)
      {
          if (!data.GrantsProtection) continue;              // 他の変化技は従来どおり評価しない
          if (actor.StatusEffects.RestrictionBlocks(actor, slot, data) != null) continue;
          foreach (var aim in BattleTargeting.SelectableTiles(actor, slot, grid, floor, sched.Roster))
          {
              var allies = BattleTargeting.Victims(actor, slot, aim, grid, floor, sched.Roster)
                  .Where(v => v.Faction == actor.Faction && v.IsAlive && !v.StatusEffects.IsProtected).ToList();
              float s = ScoreProtection(allies, threatMoves);
              if (s <= 0f) continue;                         // 守る相手・届く脅威が無ければ候補にしない
              float score = s - stayPenalty;                 // 攻撃と同じく、その場に留まる被弾見積もりを引く
              if (score <= bestScore) continue;
              bestScore = score; best = new TurnInput(actorIndex, slot, aim);
          }
          continue;
      }
      ```
    - **得点**（公開・純関数。回帰から直接叩く）:
      `public static float ScoreProtection(IReadOnlyList<Entity> allies, IReadOnlyList<(Entity Foe, MoveData Data, HashSet<Vector2I> Reach)> threatMoves)`
      各味方について、`threatMoves` のうちその味方のマスに届く（`Reach == null` または `Reach.Contains(味方の位置)`）技の**1発の最大値** `t = max(威力 × Effectiveness(技, 味方) × 命中/100)`（無ければ 0）を求め、
      `value = t + (t >= 味方の現在HP ? 120 : 0)`。**得点 = 0.5 × Σ value**（定数 `ProtectionWeight = 0.5f`）。
      意味: 防げるのは1発だけなので各味方の最大の1発、倒されかけている味方には撃破ボーナスと同じ 120 を上乗せ、敵が実際にその味方を狙うとは限らないので 0.5 倍。
      `threatMoves` は「まだこのサイクルで動いていない敵」だけから作られるので、敵が全員動き終えていれば 0 点 → 選ばない（サイクル終わりに消える状態を無駄打ちしない）。
    - **保護中の個体を避ける**: `ScoreAttack` の対象ループの先頭で `if (v.StatusEffects.IsProtected) continue;`（得点に何も足さない・引かない。撃破ボーナスも無し。敵味方とも）。
      保護中の個体しか当たらない手は 0 点になり、既存の `if (offense <= 0f) continue;` で候補から外れる。`EstimateThreat`・`SurvivalPenalty` は変えない。
    - **タイブレークと乱数**: 比較は既存と同じ `score <= bestScore` の厳密比較で、同点なら列挙順で先の手（個体の並び → 技枠 0→3 → `SelectableTiles` の順＝N, NE, E, SE, S, SW, W, NW → 最後に移動先）が残る。
      `ScoreProtection`・`ScoreAttack` の変更は**乱数を使わない**（`GD.Randf/Randi`・`RandomNumberGenerator` を呼ばない）。`Decide` は元々乱数を使わないので、同じ盤面からは同じ手が返る。
      構築を作る乱数列（`--strongest`・`--meta-*` の固定シード `MetaScenario.Seed`）にも触れない。固定シードの構築は `DefaultLoadout`・`TacticalBuilder` の技選び（§2-7。この技は選ばれない）と種族値（変えない）で決まるので、生成される構築は変わらない。
      （`--random` は元々固定シードではない。対象 31 種の learnset が1技増えるので、`RandomTeam` のシャッフルの結果は変わる。）
    - 選出（`SelectionScore`）・配置（`MeleeBias`）は変えない（威力0の技は数えないまま）。
12. **`Scripts/Battle/BattleMatchScene.cs`** `RunRandomBatch`: 開始時に `ProtectionStats.Reset()`、終了時の `PrintRestrictionStats()` の後に1行
    `[結果] プロテクション: 使用{Used}回 / 付与{Granted}体 / 無効化{Blocked}回 / 未使用で消滅{Expired}体`。（計測の CSV は変えない。この行は4プロセスのログを足し合わせて §33 に記録する）

### 3-5. Python の変更

1. **`Tools/learnset_rules.py`**: 表を1か所に置く（生成・検証の食い違いを防ぐ既存の方針）。
   ```python
   # プロテクション（仕様書 docs/design/drafts/2026-10-29-protection.md）: 両刀攻撃で BST6<=470 の個体が level 10 で1つ覚える氷の変化技。
   PROTECTION_ID = 'mvs_ice_protection'
   PROTECTION_LEVEL = 10
   PROTECTION_BST6_MAX = 470
   def bst6(s): return s['base_hp'] + s['base_atk'] + s['base_def'] + s['base_spatk'] + s['base_spdef'] + s['base_speed']
   def protection_learner(s): return s.get('offense') == 'Mixed' and bst6(s) <= PROTECTION_BST6_MAX
   ```
2. **`Tools/add_protection_move.py`**（新規。`add_restriction_moves.py` と同じ形・冪等）:
   - moves.json から `id == PROTECTION_ID` を取り除き、§3-1 の定義を**末尾に**足す（キーの順も §3-1 どおり。書き出しは `json.dumps(..., indent=2, ensure_ascii=False) + '\n'`）。
   - species.json の全種から `move_id == PROTECTION_ID` の行を取り除き、`protection_learner(s)` の種に §3-1 の位置で1行挿入。
   - 対象が **31 種**で、§2-1 の species_id 一覧（定数 `EXPECTED`）と一致することを assert。名前・id の衝突も assert。
   - 出力: `プロテクションを追加 / 習得 31 種`。docstring に「`generate_learnset.py` で learnset を作り直したら `add_restriction_moves.py` の後にこれを実行する」と書く。
3. **`Tools/verify_learnset.py`** に **#21** を新設:「プロテクション: 習得者が両刀攻撃かつ BST6≤470 の 31 種ちょうど（`protection_learner` と一致）・各1行・level 10、それ以外の種は持たない」。`[PASS]` 20 → **21**。既存 #1〜#20 は変えない。
4. **`Tools/verify_attack_moves.py`**:
   - 55 行の「変化技58種が手つかず」を「**既存の変化技58種が手つかず（プロテクションを除く）**」に書き換える: `id != 'mvs_ice_protection'` の変化技が 58 種。
   - 新しい検査「プロテクションの定義が仕様どおり」: §3-1 の10キーの値が完全一致し、余分なキーが無い。`[PASS]` 25 → **26**。
5. **`Tools/build_ls_payload.py`**: `RG` に `'FrontArc': '正面3'`。
6. **`Tools/artifacts/move_editor.html`**（テンプレート部分。`MOVES_SEED` 以外）: `RANGES` 配列の末尾に `"FrontArc"`、`RANGE_JA` に `FrontArc:"正面3マス"`。
   エディタは `grants_protection`・`priority` の入力欄を持たないが、足さない（§7）。保存時に未知キーが落ちるかどうかは確かめて報告する（直さない）。
7. 再生成: `python3 Tools/build_ls_payload.py`・`python3 Tools/refresh_move_editor.py`（`learnset_viewer.html`・`move_editor.html` が更新される。再公開は検証者）。
8. **`Tools/analyze_random_by_element.py`**: `--by` に **`protection`** を1つだけ足す（§5 の群別集計に必要な最小限）。区分:
   - learnset に `mvs_ice_protection` を持つ個体 → `習得:trait2あり` / `習得:trait2なし`（`trait2` の有無）
   - それ以外 → `なし:` + offense（両刀で持たない7種は `なし:Mixed`）
   表示順は `習得:trait2あり, 習得:trait2なし, なし:Mixed, なし:Physical, なし:Special`。そのあとに**合算行** `習得:計`（2群を合わせた 31 種の出場で、勝率・期待・補正後pt を出場単位で計算し直す。`--bootstrap` 時は同じリサンプルで SE も出す）を1行出す。docstring も更新。
   両刀全体・個体別は既存の `--by offense`・`--by species --only-mixed` を使う（変えない）。

---

## 4. 検証方法

### 4-1. 共通（CLAUDE.md）

```sh
dotnet build MysteryDungeonRoguelike.csproj                        # 0 エラー（先に必ず実行。古いアセンブリを使わないため）
godot --headless --path . Scenes/BattleTestScene.tscn > run.log     # 回帰スイート
grep '\[検証' run.log | grep -c NG                                  # 0 を 6 回連続
grep -c '\[検証' run.log                                            # 項目数（186 の見込み）
python3 Tools/verify_learnset.py; python3 Tools/verify_attack_moves.py; python3 Tools/verify_npc_teams.py
python3 Tools/add_protection_move.py && git diff --stat Data/       # 2回目の実行で差分が増えない（冪等）
git diff                                                            # 本書と突き合わせる
```

回帰スイートは NG があっても終了コード 0 で終わる。**判定は NG を含む `[検証` 行の数**。乱数で時々落ちる項目を見逃さないよう 6 回連続で実行する。
headless 実行のスプライト import エラーは無視してよい。`dotnet`/`godot` が無ければ CLAUDE.md の手順で入れる。

### 4-2. 回帰スイートに足す検査（新メソッド `VerifyProtection()`。`_Ready` で `VerifyRestrictions();` の直後・`RunBattle();` の前に呼ぶ）

`VerifyRestrictions` と同じ作法: 生きている個体を選び、位置・向き・特性・持ち物を保存して最後に戻す。`Fresh()`（`StatusEffects.Reset()`・全回復・PP 全回復・持ち物なし）・`MakeSlot`・
`new AttackAction(使用者, 対象, slot, _floor, aim).Execute(0)` で直接撃つ。盤は 7×8 全面床。攻撃技は `VerifyRestrictions` の `phys`（単体・命中100・追加効果なしの物理）と同じ条件で選ぶ。
**どれも「効く条件で効く／効かない条件で効かない」を対にする。** 文言（`[検証] …:`）は実装者が決めてよい。1項目 = 1行の `[検証` 行。

| # | 検査 | 期待値 |
|---|---|---|
| P1 | 技データ | `MoveDatabase.Get("mvs_ice_protection")` が Ice / Status / 威力0 / 命中100 / PP10 / Priority 3 / Range FrontArc / GrantsProtection=true。他の 627 技は全て GrantsProtection=false |
| P2 | 習得者 | `SpeciesDatabase` で learnset にこの技を持つ種がちょうど §2-1 の 31 種（集合一致）、各1行・level 10 |
| P3 | 正面3マスの形 | `ResolveTiles(FrontArc, (3,4), (0,-1))` = {(3,3),(4,3),(2,3)}、向き (1,-1) で {(4,3),(4,4),(3,3)}、`(0,0)` で北向きは空（盤外）・東向き (1,0) は {(1,0),(1,1)}（2マス） |
| P4 | 付与（味方だけ・自分は含まない・狙いで向く） | 使用者 U を南向きにしておき、U の北側の前に味方 A、右前に味方 B、左前に**敵** E、真後ろに味方 C を置いて、aim = U の北隣のマスで使う → U が北を向き、A・B だけ IsProtected、C・E・U は false。PP が1減る |
| P5 | 不発 | 正面3マスに味方がいない → PP は1減り、誰も IsProtected にならない |
| P6 | 単体攻撃を無効化 | 保護中の A に `phys` → HP 変化なし・IsProtected=false（消費）。続けてもう1回 → HP が減る |
| P7 | 変化技も無効化 | 保護中の A に `MV_122`（相手の攻撃ランク-2、単体）→ AtkRank 0 のまま・消費。保護なしなら AtkRank -2。`mvs_toxic_dark`（猛毒、単体）でも 保護中は `Ailment == None`・消費 |
| P8 | 多段技は1回の使用ごと | 保護中の A に多段技（`mvh_water` 等。多段技6種は全て Adjacent・命中 90〜95）→ HP 不変・消費は1回（判定は命中判定より前なので確定）。その後は保護なしで最大16回まで撃ち、最初に当たった使用で HP が減る（1回目で外れても NG にしない） |
| P9 | 範囲技は対象ごと（味方の技でも消費） | 敵 X の Area 技（威力>0・命中100）の 3×3 に 保護中の A・保護中の B・保護なしの C → A・B の HP 不変かつ消費、C は HP 減。**同陣営の味方 D が撃った Area 技**に保護中の A が入った場合も A は HP 不変・消費 |
| P10 | 防がないもの | 保護中で毒の A に `ResolveStatusTick()` → 毒ダメージは入り、IsProtected は true のまま（継続ダメージでは消費されない）。自分だけに効く変化技（`mvs_ice_atk_crit` 等の IsSelfContained）を A のマスへ向けて使われても A の IsProtected は true のまま |
| P11 | 重ねない | 保護中の A に2回目のプロテクション → `ProtectionStats.Granted` は増えず、`phys` 1発で消費されて2発目は当たる |
| P12 | 持続はサイクルの終わりまで | 付与 → `_sched.EndCycle()` で IsProtected=false（`Expired` +1）。付与 → 同じサイクル内で `phys` が来れば防ぐ（P6 と同じ） |
| P13 | 行動順 | `BattleScheduler.EffectivePriority(使用者, プロテクション)` = 3。プロテクション（俊敏の低い個体）と ヘッドバット（俊敏の高い個体・あめ＋水のちからで実効2）の `Order` でプロテクションが先 |
| P14 | 出せない条件 | `NoStatusMoves`（挑発）中・`AdjacentOnly`（射程制限）中は `RestrictionBlocks` が非 null（PP 不変） |
| P15 | NPC の得点 | `NpcOpponent.ScoreProtection`: 届く脅威 (威力100・相性1・命中100) がある味方1体（HP 200）→ 50、HP 80 → 110（=0.5×(100+120)）、脅威が空 → 0、味方リストが空 → 0。同じ入力で2回呼んで同じ値（乱数を使わない） |
| P16 | NPC は保護中の相手を撃たない | 盤上で、隣接する敵が「保護中の個体」1体だけのとき、その個体への攻撃の `ScoreAttack` が 0（＝候補にならない）。保護を外すと正（`ScoreAttack` が private なら `internal static` か検査用の公開ラッパーにしてよい） |

170 → **186 項目**（目安。実際の数を報告する）。

### 4-3. フェーズと完了条件

| フェーズ | 内容 | 完了条件 |
|---|---|---|
| P0 準備 | ツールチェーン確認・前提の確認 | `dotnet build` 0 エラー、回帰 170 項目で NG 0 を1回、Python 3 本 **20/25/9** PASS。`/tmp/mixed_traits/after_{1..4}.csv`（§32 の後の計測。計 3,000 行）の存在を確認して `/tmp/protection/before/` へコピー（無ければ報告し、§5-1 の表を前の値として使う。前の計測をやり直してはいけない） |
| P1 データ・道具 | §3-5 の 1〜4、`add_protection_move.py` の実行 | moves.json 628 技（`git diff Data/moves.json` が末尾の1件の追加だけ）、species.json は対象 31 種に各1行の追加だけ。2回目の実行で差分が変わらない。Python 3 本 **21/26/9** PASS |
| P2 C#（エンジン） | §3-4 の 1〜10・12 | `dotnet build` 0 エラー。回帰 170 項目で NG 0 を1回（既存項目に影響しないこと） |
| P3 C#（NPC） | §3-4 の 11（プロテクションの得点評価、保護中の個体を避ける規則） | `dotnet build` 0 エラー。回帰 170 項目で NG 0 を1回。`--random --matches 20` が完走し、`[結果] プロテクション:` 行の使用回数が 0 でない（20試合で0なら報告。この20試合は計測に含めない） |
| P4 回帰 | §4-2 の 16 項目 | **186 項目**で NG を含む `[検証` 行 0 を **6 回連続** |
| P5 Artifact・集計道具 | §3-5 の 5〜8 | `learnset_viewer.html` のペイロードで対象 31 種の `mv` に `"プロテクション"` 行（射程 `"正面3"`）が1つずつ、他の種に無い。`move_editor.html` の `MOVES_SEED` が 628 件で `RANGES` に FrontArc。`analyze_random_by_element.py /tmp/protection/before/after_*.csv --by protection --bootstrap 200` が §5-1 の表と同じ値を出す |
| P6 計測・記録 | §5 | **3,000 試合ちょうど**（1回。追加しない）、`pvp_spec.md` §33 を追記。**結果が基準を超えても調整しない** |

---

## 5. 計測（`--random` の前後比較）

### 5-1. 前の値（変更前）

- 前の計測は §32 の「後」（3,000 試合。`472c3b4` と同じ挙動。`/tmp/mixed_traits/after_1.csv`〜`after_4.csv`）。P0 で `/tmp/protection/before/` へコピーする。無ければ下の表と §32-4 の値を前の値として使う。
- 本書の群分け（新しい species.json で群分けした値。`--bootstrap 200`。草案作成時に計算済み）:

| 区分 | 体数 | 出場数 | 勝率% | 期待% | 補正後pt | SE |
|---|---|---|---|---|---|---|
| 習得:計（対象31種） | 31 | 2244 | 43.4 | 48.8 | -5.4 | 0.95 |
| 　習得:trait2あり | 14 | 1044 | 43.5 | 50.3 | -6.8 | 1.44 |
| 　習得:trait2なし | 17 | 1200 | 43.3 | 47.4 | -4.1 | 1.35 |
| なし:Mixed（非対象の両刀7種） | 7 | 623 | 51.8 | 55.1 | -3.2 | 1.95 |
| なし:Physical | — | 11686 | 48.5 | 49.8 | -1.3 | 0.34 |
| なし:Special | — | 9103 | 53.4 | 50.3 | +3.2 | 0.43 |
| （参考）攻撃区分: 両刀攻撃 全体（38種） | 38 | 2867 | 45.2 | 50.1 | -4.9 | 0.84 |

（前の CSV の要約: 試合 3,000 / 決着 2957 / 引分 12 / 未決着 31、k = 0.003162、BST6 が高い側の勝ち 58.5%。）

### 5-2. 計測（変更後）

```sh
dotnet build MysteryDungeonRoguelike.csproj
mkdir -p /tmp/protection
for k in 1 2 3 4; do
  godot --headless --path . Scenes/BattleMatchScene.tscn -- --random --matches 750 --out /tmp/protection/after_$k.csv > /tmp/protection/after_$k.log 2>&1 &
done; wait
```

`--random` は状態を持たないので4並列でよい。合計 **3,000 試合ちょうど**を1回。**追加の試合はしない**（P3 の 20 試合の確認は計測に含めない）。
4つのログの `[結果] プロテクション:` 行（使用・付与・無効化・未使用で消滅）を足し合わせて記録する。

### 5-3. 集計

```sh
A=/tmp/protection/after_*.csv; B=/tmp/protection/before/after_*.csv
python3 Tools/analyze_random_by_element.py $A --by offense    --bootstrap 200   # 攻撃区分（両刀全体。前は §32-4）
python3 Tools/analyze_random_by_element.py $A --by protection --bootstrap 200   # 対象31種（trait2 あり/なし）・非対象の両刀7種・物理・特殊（後）
python3 Tools/analyze_random_by_element.py $B --by protection --bootstrap 200   # 同（前。§5-1 の表と一致するはず）
python3 Tools/analyze_random_by_element.py $A --by role       --bootstrap 200   # 役割（参考）
python3 Tools/analyze_random_by_element.py $A --by species --only-mixed         # 個体別（後）
python3 Tools/analyze_random_by_element.py $B --by species --only-mixed         # 個体別（前）
```

### 5-4. 読み方（§33 にそのまま書く）

- **この設計では効果の検出が難しい見込み**: 対象個体が出場しても、この技を4技に持つのは約 14%（§2-1）。持っていたときに勝率が +10pt 上がったとしても、群全体では約 +1.4pt にしかならない。
  対象31種の群の SE は前後とも約 0.95pt なので、差の SE は約 1.35pt、検出の目安（2SE）は約 **2.7pt**。trait2 あり/なしの各群は差の SE 約 2pt（2SE 約 4pt）。
- したがって結果は「**効果は検出できない**」で終わる可能性が高い。**それは効果が無いことを意味しない**（この試合数とこの設計では、検出できる大きさに届かないというだけ）。§33 にこの2文を明記する。
- 群どうしを比べず、各群の「後 − 前」と、技が変わらない対照群（なし:Mixed・なし:Physical・なし:Special）の「後 − 前」との差で読む。`--random` は固定シードでないので、対照群の差は乱数のゆらぎの目安になる。
- ログの使用・付与・無効化の回数で、技が実際に使われたか（NPC の特例評価が働いたか）を確かめる。使用回数が 0 に近ければ、効果が出ない理由はそちらだと書く。

### 5-5. 記録（`docs/design/pvp_spec.md` に **§33「プロテクション（両刀 BST6≤470 の変化技）」**。§29・§32 の書式に合わせる）

1. 決定と経緯（ユーザー原文、§0-3 の確認結果）。
2. 技の定義（§3-1 の表）とプロテクション状態の定義（§3-3 の2表の要約）、習得者 31 種の一覧（species_id と名前）。
3. 実装の要点（射程 FrontArc と向きの決め方、判定位置（命中判定より前・対象単位）、サイクル終了で消える、NPC の得点式と保護中の個体を避ける規則）。
4. 計測の概要: 試合数・決着・引分・未決着率（2% 以下か）・k・BST6 が高い側の勝率。プロテクションの使用・付与・無効化・未使用で消滅の合計と1試合あたり。
5. **前後比較表**: 攻撃区分 3 行（前は §32-4）と、`--by protection` の 6 行（習得:計・習得:trait2あり・習得:trait2なし・なし:Mixed・なし:Physical・なし:Special）。
   列: 前の補正後pt(SE)・後の補正後pt(SE)・差（後−前）・差の SE（√(前SE²+後SE²)）・**対照差**（その群の差 − なし:Mixed の差）・差が 2SE を超えるか。攻撃区分の判定列（○/×/境界、§31-8 と同じ定義: 基準 ±3pt、境界＝基準との差が SE 未満）。
6. 個体別の前後表（両刀 38 行。習得の有無と trait2 の有無の列を付ける。出場が少ないので参考扱い）。
7. §5-4 の読み方（検出の目安、「検出できない」は「効果が無い」ではないこと）。
8. 基準を超えた行があっても調整しない。結果をそのまま報告し、以後の判断はユーザーに委ねる。

---

## 6. 判断が必要だった点

### 6-1. ユーザーの確認で決まったこと

§0-3 の5点（NPC に特例で使わせ、相手は保護中の個体を避ける／サイクルの終わりまで／狙いで向きを選ぶ／自分は含めない／`--random` の前後比較）。

### 6-2. 本書で決めたこと（ユーザー確認不要と判断）

| 論点 | 決めたこと | 理由 |
|---|---|---|
| 「BST470以下」 | **BST6**（6能力合計）≤ 470、470 を含む → 31 種 | CLAUDE.md で合計種族値は BST6 と定義。旧 BST3（`legacy_had` の合計）で読むと両刀 38 種が全員 375 以下で「470以下」が全員になり、閾値の意味が無くなる |
| id・PP・命中・習得レベル | `mvs_ice_protection`・PP 10・命中 100・level 10 | 既存の変化技の命名・最上位段の値に揃えた（§3-1） |
| 習得の入れ方 | 既存の learnset に追加（他の技は消さない） | 原文「追加で習得させてください」 |
| 斜め向きのときの3マス | 向きとその両隣 45° | 北向きの例（前・右前・左前）を 8 方向に一般化した唯一の自然な形 |
| 味方だけ | 敵が正面にいても付かない | 原文「味方」 |
| 「1発」の単位 | 1つの技の1回の使用（多段は全段、範囲は対象ごと） | 段ごとにすると多段技の2段目以降が通り「無効」にならない |
| 命中判定との順序 | 命中判定より前に判定して消費 | 「技を無効にする」状態なので当たるかどうかに先立つ。命中後にすると効果が乱数に依存する |
| 味方の技 | 防いで消費する | 原文「どのような技も」 |
| 技でないもの | 防がない（§3-3 の表） | 原文は「技」を無効にする |
| 泥まみれ・きょうじんなからだ | プロテクションの付与を止めない | 付与は技の主効果（天候・設置と同じ扱い）で、追加効果ではない |
| 優先度3と既存規則 | `verify_learnset` #17 は攻撃技（威力>0）だけの規則で、変化技は対象外 | `learnset_rules.fast_blocks` と回帰の該当項目がどちらも威力>0 を条件にしている |
| verify_attack_moves の「変化技58種」 | 「既存の58種が手つかず」に書き換え、追加技の定義を別の検査で固定 | 元の検査の目的（既存の変化技を触らない）を保ったまま、意図した1件の追加を許す |
| moves.json の位置 | 末尾 | 回帰の `.First(...)` で選ばれる技を変えないため（§2-9） |
| 迷宮 | 同じ `AttackAction` なので迷宮でも効く。持続は消費か階移動まで | 迷宮の AI の技選びは変えない（§7） |
| NPC 評価の重み | 0.5、撃破級に +120 | 撃破ボーナスと同じ尺度。敵がその味方を狙う確率は分からないので半分。**計測後に調整しない** |
| 集計 | `--by protection` を1つだけ足す（対象を trait2 の有無で2群＋合算行、非対象は攻撃区分） | 計測に必要な群別集計の最小限。両刀全体・個体別は既存の区分で足りる |
| 自他両方に効く変化技 | 現行データに無いので、守られた相手へ向けた非自己完結の変化技は技全体を不発にする | ランク変化の対象は技ごとに Self のみか Enemy のみ（確認済み） |

### 6-3. 実装時に判断してよい範囲／決めずに報告する範囲

- **判断してよい**: ログの文言、`[検証]` 行の文言、private メソッド名、`TargetResolver` 内の方向配列の持ち方、`ScoreAttack` を検査から呼ぶための可視性（`internal static` 化・ラッパー）、
  回帰で使う具体的な個体・技の選び方（§4-2 の条件を満たす範囲）、`ProtectionStats` の置き場所（`Scripts/Combat/`）、`--by protection` の合算行の実装方法。
- **決めずに報告する**: §3-1 の値の変更、対象 31 種の増減、§3-3 の表と違う挙動が必要になった場合、既存の回帰項目が NG になった場合、Python 検証の既存項目を変える必要が出た場合（§3-5 の 4 以外）、
  NPC の重み・式の変更、計測の試合数の変更、`--random --matches 20` でプロテクションが1回も使われない場合、自他両方に効く変化技が見つかった場合、`move_editor.html` が未知キーを落とす場合、
  前の CSV（`/tmp/mixed_traits/after_*.csv`）が見つからない場合。

### 6-4. 実装者の注意

- `BattleFlow.cs` は `BattleTargeting` と同じ規則の写し（`SelectableTiles`・`Preview`）を持っている。**必ず両方直す**。
- `BattleScheduler.ApplyEncore`（アンコール）でプロテクションを強いられた場合、提出された狙いが隣接8マスでなければ既存の規則どおり不発になる（変えない）。
- きぬぬい（氷技を使うと被ダメ軽減を装填）は、プロテクションも氷技なので装填される（変えない）。対象 31 種にきぬぬい持ちはいない。
- `--random` は乱数シードを固定していない。learnset の技数が変わるので、同じ乱数でも構築は変わる（比較は統計でしか行えない）。

---

## 7. やらないこと

- 他の 58 の変化技を NPC に使わせること。
- 同じチームの ON/OFF 対照実験（`--protect-test` のようなモード）を作ること。3,000 試合を超える追加計測。
- `DefaultLoadout.PickMoves`・`TacticalBuilder`（`--strongest`・メタ系・`--tactics`）や `Data/npc_teams.json` の技選びの変更・再生成。両刀以外・BST6>470 の両刀への習得。
- 既存の技・learnset の行の削除や並べ替え、既存の検証規則（`verify_learnset.py` #1〜#20、`verify_attack_moves.py` の他の検査）を緩めること。
- 対戦 HUD へのプロテクション状態の表示（ログだけ）。技データベース エディタへの `priority`・`grants_protection` 入力欄の追加。
- 迷宮の AI（`HostileEntity`・`AllyEntity`）の技選びの変更。
- 計測結果に応じた調整（重み・優先度・PP・対象の変更）。
- Artifact の再公開、commit、push。

---

## 8. ユーザーに確認すべき点

なし（2026-10-29 に確認済み。§0-3）。
