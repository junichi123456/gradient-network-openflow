# 両刀攻撃の一部の個体に「2つめの特性」を与える調査（3種のランダム特性・3種の地形特性）— 仕様書

- **状態: 確定（2026-10-28、ユーザー確認済み。ただしレッツハギングの節（§9）はユーザーの確認待ち）**。
  §1〜§7 と付録A はそのまま実装してよい。§9 は提案であり、ユーザーが案を選ぶまで実装しない（フェーズ P6。§4-3）。
- 作成: 2026-10-28（草案）→ 同日 確定版に改稿（ユーザーの確認結果 §0-2 を反映）
- 対象ブランチ: `claude/mystery-dungeon-roguelike-egnzjt`
- **前提コミット: `964763b`**（範囲技の整理 Phase C4 まで。`Data/moves.json` 627 技、`Data/species.json` 287 種、`Data/traits.json` 121 件、回帰スイート 153 項目、Python 検証の `[PASS]` 数 learnset 19・attack 25・npc 9）。
- 読み手: 会話履歴を持たない実装者（別プロセス）。**この文書だけで実装できるように書いてある。**
  書かれていないことは勝手に決めずに報告すること（裁量の範囲は §6-3）。
- **commit・push はしない**（検証者が行う）。

---

## 0. ユーザーの指示と位置づけ

### 0-1. 指示（原文）

```
2、特性の面から調査を行います。両刀種族値のパルは2つめの特性を与え、『部屋対象の範囲技を使用する際の技の威力+10』
『威力50以下の特殊技の威力+20』『技によって与える蓄積値2倍』をランダムに与えます。また、そのパルの属性に応じて、
『このパルが場に出たとき、自身がいるマスとその隣接するマスをみずたまり(水)/はなばたけ(草)/うすらひ(氷)にする』を各1体ずつ与える。
```

- 「2」は、直前に示した選択肢「2. 範囲技以外の原因を調べる」のこと。
- **これは調査のための試験的な変更である。** 特性を与えたあとの勝率の変化を測り、両刀攻撃の不利の原因（特性の弱さか、個体差か）を調べる。
  計測結果が基準（攻撃区分 ±3pt・役割 ±5pt）を超えていても、**調整はせずにそのまま報告する**。試合数は **3,000 試合を上限**とする（ユーザー指示: テスト量を増やさない）。

### 0-2. ユーザーの確認結果（原文と、本書での読み方）

| # | 論点 | ユーザーの回答（原文） | 本書での読み方 |
|---|---|---|---|
| 1 | 地形特性の3体とランダム3種の関係 | 「置き換え、ランダム3種は残り35体に4・5・4で配る。2種類持たない種族も存在する。○○のリーダーを持つパルは追加しません。きずな、ちから、おしえ、まもり、式を持つパルは優先的に追加候補とします。」 | 2つめの特性は1枠（`trait2`）。地形特性の3体はランダム3種を持たない。ランダム3種は 4＋5＋4＝**13体**にだけ配り、`trait2` を持つのは**計16体**、**残り22体は `trait2` なし**（「35体に」は、地形の3体を除いた35体が配る対象の母集団、の意）。リーダー持ちは除外、きずな／ちから／おしえ／まもり／式 持ちを優先（§3-4） |
| 2 | 地形特性を与える個体の選び方 | 「前述を参照」 | 確認1 の優先候補の規則に従う（各属性の優先候補から固定シードで無作為） |
| 3 | しみこみ（蓄積2倍）に特性由来の固定加算を含めるか | 含めない | 技そのものが与える蓄積だけ2倍。レッツハギング・ひょうてんま・どくせんボディの固定加算は据え置き（§3-2 ③） |
| 4 | ワイドバーストを部屋技を覚えない個体にも与えるか | 「前述の候補に対し優先的に部屋技を覚える個体を無作為で選択」 | ワイドバーストは、優先候補のうち部屋の攻撃技を覚える個体から無作為。足りなければ優先候補外の部屋技持ちから補う（§3-4） |

加えて、ユーザーから「レッツハギングの要件を曖昧なままにしているので確定させたい。提案してください」との依頼があった。これは本書 §9 に**提案**として書き、§8 でユーザーに確認する（本書の計測とは独立）。

---

## 1. 目的（達成条件）

次の全てを満たせば完了（§9 レッツハギングは含まない。P6 として別に扱う）。

1. `Data/traits.json` に新しい特性 6 種（§3-1）があり、`Data/species.json` の両刀攻撃 38 種（`offense == "Mixed"`）のうち**付録A の 16 体だけ**が新しい項目 `trait2` を持ち、
   その値が付録A の割り当て表どおりである（ワイドバースト 4・こわざじょうず 5・しみこみ 4・地形特性 3 種 各1）。残りの両刀 22 体と、両刀以外の 249 種は `trait2` を持たない（キー自体を書かない）。
   割り当ては `Tools/assign_mixed_traits.py`（新規・固定シード・冪等）で生成されており、再実行しても `Data/` が変わらない。
2. `trait2` を持つ個体は `trait`（従来の特性）と `trait2` を**同時に**持ち、両方の効果が同時に効く。`trait2` を持たない個体の挙動は変わらない。
3. 3 種の新特性（ワイドバースト／こわざじょうず／しみこみ）が §3-2 の定義どおりに効き、効かない条件では効かない。
4. 3 種の地形特性が、対戦開始時（両陣営が立ち終わったあと）に、保持者のマスとその周囲8マス（盤面内のみ）を対応する地形にする。地形の効果は既存のもの（§2-5）がそのまま効く。
5. `dotnet build MysteryDungeonRoguelike.csproj` 0 エラー。回帰スイートの `[検証` 行に NG を含む行が 0 を **6回連続**（153 項目＋本書で足す 12 項目＝**165 項目**）。
6. Python 検証3本が ALL PASS（`[PASS]` 数: learnset **20**（#20 を新設）・attack **25**・npc **9**）。
7. learnset インスペクタのペイロード（`Tools/artifacts/learnset_viewer.html`）が2つめの特性を表示し（持たない個体は表示しない）、特性の絞り込みで2つめの特性でも絞れる。技データベース エディタも再生成済み（変化なしのはず）。**再公開は担当外**（検証者が行う）。
8. 3,000 試合を1回計測し、§5 の手順で前後比較・特性別の集計を行い、`docs/design/pvp_spec.md` に **§32** を追記している。

---

## 2. 現状（実装者向けの要約）

### 2-1. 経緯（なぜ調べるか）

完全ランダム構築の対戦（`--random`）で、攻撃区分別の「BST補正後の勝率差」（出場1回ごとの勝率 − 両陣営の BST6 差から求めた期待勝率、単位 pt）に偏りがある。
直近（`964763b` の状態、3,000 試合、`docs/design/pvp_spec.md` §31-8-6）の結果:

| 区分 | 出場数 | 補正後pt (SE) |
|---|---|---|
| 物理攻撃 | 11610 | -1.2 (0.30) |
| 特殊攻撃 | 9044 | +3.8 (0.40) |
| **両刀攻撃** | 2970 | **-6.6 (0.91)** |
| 両刀攻撃/物理耐久 | 1092 | -5.5 (1.59) |
| 両刀攻撃/特殊耐久 | 1067 | -7.7 (1.56) |
| 両刀攻撃/両面耐久 | 811 | -6.6 (1.59) |

特殊の範囲技の削除と威力 -20（＋命中 -5）で特殊攻撃は +5.1 から下がったが、両刀攻撃は調整前の -2.8 から -6.6 に悪化した。
両刀攻撃は全 38 種（攻撃 A と魔力 C に 10% 以上の開きがある個体のうち、BST6±4% ゾーンで俊敏が中央値より高い個体。`species.json` の `offense == "Mixed"`。
回帰スイートにも「両刀攻撃 38 種」の検査が既にある）。役割（`role`）は `Mixed-Physical` 13・`Mixed-Special` 14・`Mixed-Both` 11。

### 2-2. 特性の仕組み（1個体1特性）

- 定義: `Data/traits.json`（121 件。`category` は `"unique"` 59・`"template"` 62）。項目: `id`・`name`・`description`・`category`・`element`・`template_kind`
  （`bond`＝〇のきずな／`power`＝〇のちから／`guard`＝〇のまもり／`resist`＝〇流／`stab`＝〇派／`weakness`＝〇式／`oshie`＝〇のおしえ、unique は null）・任意の `weather_on_entry`（11 件だけ）。
- 名前に「リーダー」を含む特性は unique の 4 件（冷却のリーダー `reikyaku_leader`・力持ちのリーダー `chikaramochi_leader`・もふもふのリーダー `mofumofu_leader`・とうそつのリーダー `tousotsu_leader`）。
- 読み込み: `Scripts/Combat/TraitDatabase.cs`（`TraitJson` → `TraitData`。`TraitData.cs` に `Id/Name/Description/Category/Element/TemplateKind/WeatherOnEntry`）。
- 種族: `Data/species.json` の各個体は `"trait"` を**1つだけ**持つ。`Scripts/Species/SpeciesDatabase.cs` の `SpeciesJson.Trait`（`[JsonPropertyName("trait")]`）→ `SpeciesData.Trait`。
  `SpeciesDatabase.Validate()` は「空でない `Trait` が traits.json にあるか」だけを見る。
- 個体: `Scripts/Entities/EntityStats.cs` の `public string Trait`（`FillFromSpecies` で `Trait = species.Trait;`、272 行付近）。
- **複数特性を同時に持たせる仕組みは無い。** 特性の判定はすべて `Stats.Trait` の文字列比較で、次の箇所に散らばっている（`Stats.Trait` を直接見る箇所の洗い出し、検証用シーンを除く）:

| ファイル | 箇所 | 見ている特性 |
|---|---|---|
| `Scripts/Turn/AttackAction.cs` | `HasTrait(Entity, string)`（1112 行付近。`entity.Stats.Trait == traitId`）経由で約 50 箇所 | unique 特性の大半（こうふん・フリーフォール・パープルヘイズ・レッツハギング・どくせんボディ 等） |
| 同 | `bondTrait`（627 行付近）・`EffectiveMoveType`（1099 行付近、おしえ）・`HasMatchingTemplateTrait`（1121 行付近、派・流） | template 特性 |
| `Scripts/Combat/PartyElementCensus.cs` | 56・98・111 行付近 | ちから・まもり・式（template）、リーダー系・がんばりサポート 等（unique） |
| `Scripts/Combat/PowerTraitWeather.cs` | 6 関数 | 〇〇のちから の天候効果 |
| `Scripts/Entities/Entity.cs` | 248〜254（battery/blitz_beat/over_heal）・643〜644（fuwafuwa/yukisuberi）・796（konton_no_shukufuku） | unique |
| `Scripts/Dungeon/FloorController.cs` | `ApplyWeatherOnEntry`（802 行付近） | `weather_on_entry` を持つ特性 |
| `Scripts/Turn/BuildUpRelay.cs` 49・`TurnScheduler.cs` 98・`TurnManager.cs` 68 | `build_up` | unique |
| `Scripts/Battle/TacticalBuilder.cs` 102〜114 | 天候特性・fuwafuwa/yukisuberi（構築AI） | unique |
| `Scripts/UI/Battle/TeamBuildScreen.cs` 236・`SpeciesPickScreen.cs` 168 | 表示 | 全特性 |

- 通信（`Scripts/Battle/BattleNetProtocol.cs`・`BattleNetwork.cs`）は特性を送らない（種族 id から各端末が引く）。**プロトコルの変更は不要。**

### 2-3. 威力の計算（`AttackAction.StrikeTarget` 内、620〜860 行付近）

- `powerFlatBuff`（威力への定数加算）に各特性の加算を足す（こうふん +15・フリーフォール +25・パープルヘイズ +15・`PowerTraitWeather.FlatPowerBonus`（炎のちから +10）・発煙器官 +10 等）。
  **加算どうしは足し算**。そのあと `powerMul`（ちから ×1.1・おこりんぼ ×1.1・みずたまりの炎 ×0.75・レンズ等）が掛かる。
- `DamageCalculator.Calculate`: `effPower = max(0, (BasePower + PowerFlatBuff) * PowerMultiplier)`。`BasePower = move.Power`。
- 急所時は `powerFlatBuff = max(0, powerFlatBuff)`（攻撃側の加算は急所でも残る）。
- 前例: パープルヘイズの「威力55以下」は **`move.Power`（技データの威力。実行時の加算後ではない）** で判定している。
- `move.Power` は `Data/moves.json` の値で、範囲技の -20 調整は**データに書き込み済み**（調整後の値が `move.Power`）。
- 射程 `MoveRange`（`Scripts/Combat/MoveData.cs`）: `Adjacent`/`Line`/`TwoTile`/`Area`/`Room`/`FullFloor`/`Surrounding`。対戦盤は 7×8 の1部屋で、`Room` は相手全員に当たる。
  `Room` の攻撃技は 19 件（特殊 18・物理 1＝ディスチャージパンチ `MV_170`）。`FullFloor` の技は無い。
  両刀 38 種のうち、`Room` の攻撃技を learnset に1つ以上持つのは **18 種**（付録A の「部屋」列が 1 以上）。
- 威力 50 以下（威力>0）の特殊技は 81 件（Adjacent 38・Line 34・Room 6・Area 2・TwoTile 1）。うち多段技は 0 件。

### 2-4. 状態異常の蓄積（`Scripts/Entities/StatusEffectManager.cs` 205〜345 行付近）

- 個体ごと・状態異常ごとに 0〜1000 の蓄積値（`_accumulation`）。1000 に達するとその状態異常が発症し、**全ての蓄積値が 0 に戻る**。何かの状態異常が発症中は蓄積しない。
- `AccumulateOnHit(moveType, declaredAilment, declaredChancePercent)`（254 行）: 技の命中ごとに呼ぶ。
  - 技の（実効）属性に対応する状態異常（炎→やけど・水→ずぶ濡れ・電→まひ・地→泥まみれ・草→ツタまみれ・氷→こおり・闇→暗闇・虚→虚脱。無と竜は無し）に **基礎 +25**。
    実効属性は `AttackAction.EffectiveMoveType`（〇のおしえ持ちは無属性技がその属性になる）。
  - 技が宣言する状態異常がその属性の状態異常と一致すれば、さらに **宣言確率×10**（10% なら +100）。
  - 宣言が毒/猛毒なら、属性と無関係に毒/猛毒の蓄積に **宣言確率×10**。
- `AccumulateFlat(type, amount)`（332 行）: 特性による固定加算（レッツハギング: 物理技命中で相手のツタまみれ +500、ひょうてんま: こおり +500、どくせんボディ: 物理技命中で相手の毒 +75／被弾時に相手へ毒 +250）。
- 呼び出し元（`AttackAction.cs`）: `ApplyAilmentEffectIfAny`（単体、1767 行付近）・`ApplyAoeAilment`（範囲、1791 行付近）・`StrikeMultiHit`（多段の2発目以降に基礎分、1200 行付近）・
  `ApplyTraitDrivenAccumulation`（1800〜1807 行）・どくせんボディ（1052・1055 行）。
- スタン（`AilmentType.Stun`）は蓄積ではなく確率判定（`GD.Randf() * 100 < AilmentChance`）。瞬間冷凍（ずぶ濡れの相手を即こおり）も蓄積ではない。
- 技の状態異常の対象が自分（`AilmentTarget == Self`）の技は現在 0 件。
- 回帰検査で蓄積値を読むには `StatusEffectManager.GetAccumulation(AilmentType)`（300 行）を使う。

### 2-5. 地形（床の重ね置き）は既にある

**「みずたまり」「はなばたけ」「うすらひ」は既存の地形として実装済みで、対戦盤でも効く。**

- 種別: `Scripts/Dungeon/FieldType.cs` の `enum FieldType { None, Puddle, FlowerBed, ToxicMist, ThinIce, Crevasse, Fissure }`。
- 保持: `Scripts/Dungeon/FieldManager.cs`（位置 → 種別の辞書。`Set`（21 行）は**上書き**、重ならない。`Count`（53 行）。時間では消えない。げきりゅうで消える）。`FloorController.Fields` で参照。
  対戦盤 `Scripts/Battle/BattleArena.cs` も `FloorController` を持ち（`InitializeArena`）、**試合ごとに新しい BattleArena を作る**ので試合をまたいで残らない。
- 効果（既存）:

| 地形 | 効果 | 実装箇所 |
|---|---|---|
| みずたまり `Puddle` | その上にいる個体が受ける電気技のダメージ ×1.25（相性倍率に乗算）。その上にいる個体が使う**炎技の威力 ×0.75** | `AttackAction.cs` 720〜735 行付近 |
| はなばたけ `FlowerBed` | その上にいる個体は行動サイクルの終わりに最大HPの 9% 回復（生態「飛行」`flight` を除く） | `Entity.ResolveStatusTick`（750〜765 行付近）。対戦では `BattleScheduler.EndCycle` から呼ばれる |
| うすらひ `ThinIce` | その上に**移動して入った**個体は進んだ向きに滑り続ける（壁・他個体・盤端で止まる）。炎・氷属性と生態「飛行」は滑らない | `Entity.ResolveTileEntry` / `SlideOnThinIce`（500〜560 行付近）。`MoveAction` から呼ばれる |

- 置く手段（既存）: 技 パドル `mizutamari`・ブルームガーデン `bloom_garden`・シンアイス `usurahi_no_ue`（`field_placement: "HalfArea"`）。
  `AttackAction.ApplyFieldEffect` → `FloorController.PlaceField(centre, type, count)`。**`PlaceField` は個体のいるマスを候補から外す**（`FieldCandidates`）ので、本書の「自分のマスと隣接マス」には使えない。
- 表示: 対戦画面（`Scripts/UI/Battle/*`）は地形を**描画していない**（技で置いた地形も同じ）。ログには出る。

### 2-6. 「場に出たとき」のフック

- 対戦にはパルの入れ替えが無い。選出 4 体が対戦開始時に一斉に盤面に立つ。
- `BattleArena.ApplyEntryWeather(BattleScheduler)`（81 行）が、両陣営が立ち終わったあとに `weather_on_entry` を発動させる。
  順序は **選出4体の BST6 合計が高い陣営から**（同値なら陣営の列挙値順）、陣営内は `Roster` の順。最後に発動したものが残るので、**低い側が上書きして勝つ**（CLAUDE.md「入場時の天候」）。
  呼び出し元: `Scripts/UI/Battle/BattleFlow.cs` `SpawnOrApply`（235 行付近。`--random` の試合も `BattleMatchScene.RunMatch` → `BattleFlow` を通る）と `BattleTestScene.VerifyEntryWeather`（453 行付近）。
- 迷宮側の「フロアに出たとき」は `FloorController.ApplyTraitWeather`（生成時・途中出現のたびに全員ぶん呼ぶ）。

### 2-7. 構築・計測の道具

- `--random`（`Scripts/Battle/BattleMatchScene.cs` `RunRandomBatch`/`RandomTeam`）: 毎試合、種族 6・技 4（learnset から無作為。変化技も含む）・持ち物を無作為に組む。乱数は `rng.Randomize()`（シード固定ではない）。
  CSV 列（ヘッダなし）: `match,outcome,home_sel,away_sel,home_bst,away_bst,cycles`（`home_sel` は選出 4 体の species_id を `|` 区切り）。
- `Tools/analyze_random_by_element.py <csv>... [--species path] [--by element|offense|defense|role|speed] [--bootstrap B]`。`--by` が element 以外なら `s[by]` の値で群分けし、既定の順にない値は後ろに並べる。
- **前の計測（§31-8）の CSV が `/tmp/aoe_runs/final24_1.csv`〜`final24_4.csv`（各 750 行）に残っている**（本書の改稿時点で存在を確認済み。コンテナが作り直されると消える。§5-1）。
- 構築の規則（CLAUDE.md）: 1 構築内で重複禁止は種族と持ち物だけ。特性は構築の検証（`BattleTeam.Validate`）に関係しない。
- learnset の規則（`Tools/learnset_rules.py`・`Tools/verify_learnset.py`）の「特性適応技」（`adapted()`）は `trait` だけを見る。
- データ生成スクリプトの前例: `Tools/assign_weather_traits.py`（species_id 順・機械的・再実行可。`json.dump(..., ensure_ascii=False, indent=2)` ＋末尾改行で書き出す）。

---

## 3. 変更内容

### 3-1. 新しい特性 6 種（`Data/traits.json` に追記）

全て `category: "unique"`・`element: null`・`template_kind: null`。新しい項目 **`"secondary": true`**（2つめの特性の枠にだけ入れてよい特性の印）を付ける。
地形特性には新しい項目 **`"field_on_entry"`**（`FieldType` の列挙名）を付ける。既存 121 件には `secondary` も `field_on_entry` も付けない（無い＝false/None）。

| id | name | description | 追加項目 |
|---|---|---|---|
| `wide_burst` | ワイドバースト | 部屋全体が対象の攻撃技を使うとき、技の威力+10。 | `secondary: true` |
| `kowaza_jouzu` | こわざじょうず | 威力50以下の特殊技の威力+20。 | `secondary: true` |
| `shimikomi` | しみこみ | 自分の技で相手に与える状態異常の蓄積値が2倍になる（特性による蓄積は対象外）。 | `secondary: true` |
| `entry_puddle` | みずたまりのぬし | 場に出たとき、自分のいるマスと隣接するマスをみずたまりにする。 | `secondary: true`, `field_on_entry: "Puddle"` |
| `entry_flowerbed` | はなばたけのぬし | 場に出たとき、自分のいるマスと隣接するマスをはなばたけにする。 | `secondary: true`, `field_on_entry: "FlowerBed"` |
| `entry_thin_ice` | うすらひのぬし | 場に出たとき、自分のいるマスと隣接するマスをうすらひにする。 | `secondary: true`, `field_on_entry: "ThinIce"` |

名前・説明文は仮。ユーザーが変えても実装には影響しない（id で判定する）。traits.json は 121 → **127** 件。追記位置は末尾。

### 3-2. 3 種の特性の効果の定義

**① ワイドバースト `wide_burst`**: 保持者が使う技が `move.Range == MoveRange.Room` かつ `move.Power > 0` のとき、`powerFlatBuff += 10`。
- 物理・特殊を問わない（`MV_170` ディスチャージパンチも対象）。`Area`・`Surrounding`・`FullFloor` は対象外（「部屋対象」の字義どおり。`FullFloor` の技は現存しない）。
- 通路から撃って単体に落ちた `Room` 技（迷宮のみ）も、技の射程が `Room` なので対象（判定は技データで行う）。

**② こわざじょうず `kowaza_jouzu`**: 保持者が使う技が `move.Category == MoveCategory.Special` かつ `0 < move.Power <= 50` のとき、`powerFlatBuff += 20`。
- 判定は **`move.Power`（データの威力。範囲技の -20 調整後の値）**。パープルヘイズの前例（`move.Power <= 55`）と同じく、他の加算を足す前の値で見る（加算の順番で条件が変わらないように）。
- 変化技（`Power == 0`）は対象外。物理技は対象外。
- 多段技は1発ごとに +20（1発ごとに `StrikeTarget` で威力を計算するため）。該当する多段技は現在 0 件。

**①②の加算順**: 既存の加算（こうふん等）と**足し算**、そのあと `powerMul`（ちから ×1.1 等）が掛かる。例: 威力 45 の特殊技に こわざじょうず と 同属性のちから（味方）→ (45+20)×1.1。
急所でも残る（既存の `powerFlatBuff = max(0, …)` のまま）。

**③ しみこみ `shimikomi`**: 保持者の技の命中によって**保持者以外の個体**に入る `AccumulateOnHit` の加算量を **2 倍**にする（ユーザー確認3: 技そのものが与える蓄積だけ）。
- 2 倍になるもの: 属性の基礎 +25、宣言一致の 宣言確率×10、毒/猛毒の 宣言確率×10、多段技の2発目以降の基礎 +25。
- 2 倍にならないもの: スタン（確率判定で蓄積ではない）・瞬間冷凍・**特性による固定加算（`AccumulateFlat`。レッツハギング +500・ひょうてんま +500・どくせんボディ +75/+250）**・保持者自身に入る蓄積・発症のしきい値（1000 のまま）。
- 加算は「量を2倍にしてから足す」。1000 に達したら発症して全蓄積が 0 に戻る規則は変えない。
- 注: 付録A で しみこみ を持つ 100 ハグミーは、1つめの特性がレッツハギング（`AccumulateFlat(VineBound, 500)`）。上の規則により +500 は 2 倍にしない。草技の基礎 +25（＋宣言分）だけが 2 倍になる。

### 3-3. 地形特性の効果の定義

保持者が「場に出たとき」＝**対戦開始時、両陣営が立ち終わったあと**（天候特性と同じ時点）に、保持者のマスを中心とする 3×3（保持者のマス＋周囲8マス）のうち**盤面内で壁でないマス全て**を対応する地形にする。

- **個体がいるマスも含める**（保持者自身のマス・味方や相手がいるマスも地形になる）。`PlaceField` は個体のいるマスを外すので使わず、新しい関数で置く（§3-5-6）。
- 既にある地形は上書き（`FieldManager.Set` の既存規則）。
- 複数の地形特性の保持者がいるときは、天候と同じ順（**選出4体の BST6 合計が高い陣営から、陣営内は登録順**）に置き、重なるマスは**後に置いたもの（低い陣営）が残る**。
- 地形の効果は §2-5 の既存の効果そのまま。新しい効果は足さない。持続も既存どおり（試合終了まで。げきりゅうで消える）。
  注意（実装は変えない）: みずたまりは保持者自身にも効く（保持者が受ける電気技 ×1.25・保持者の炎技 ×0.75）。付録A のみずたまりのぬし 169B トノサマンボ（水/電）も自分のマスがみずたまりになる。
  はなばたけは周囲にいる**相手**も回復させる。うすらひは保持者が氷属性（付録A の 101 ツララジカは氷）なので保持者自身は滑らない。
- 迷宮（`FloorController.ApplyTraitWeather`）では置かない（§7）。
- 戦闘画面での描画は足さない（§7）。発動時にログを1行出す（例: `"{名前}のみずたまりのぬしで足元がみずたまりになった!"`。日本語で、既存の天候ログ程度）。

### 3-4. 割り当て（`Tools/assign_mixed_traits.py` を新設）

`Tools/assign_weather_traits.py` を前例にした、**固定シードで再現できるデータ生成スクリプト**。実行時の乱数は使わない。
割り当て関数 `assign(species, traits_by_id, moves_by_id) -> dict`（species_id → trait2）は**付録A-2 の参照実装をそのまま使う**（定数・関数の中身・乱数を引く順番を変えない。変えると付録A と一致しなくなる）。

#### 規則（付録A-2 の手順番号と対応）

用語:
- **両刀**: `offense == "Mixed"` の 38 種。species_id の文字列昇順に並べたものを基準の順とする。
- **除外**: 1つめの特性（`trait`）の名前（`traits.json` の `name`）が「リーダー」を含む個体。`trait2` を与えない。現行は 021B アオパオー（冷却のリーダー）・036 ドンモップ（もふもふのリーダー）の 2 体。
- **優先候補**: 除外でなく、1つめの特性の名前が「きずな」「ちから」「おしえ」「まもり」「式」のいずれかを含む個体（template の bond・power・oshie・guard・weakness に当たる。〇流・〇派 は含まない）。現行 **15 体**: 083B, 084, 084B, 096, 101, 108, 127, 133, 146, 154, 161, 169B, 178, 188, 188B。
- **一般**: 除外でも優先候補でもない両刀（現行 21 体）。優先候補で足りないときの補充元。
- **部屋技持ち**: learnset（重複除く）に `range == "Room"` かつ `power > 0` の技を1つ以上持つ個体。

手順（`rng = random.Random(20261028)` を1つだけ作り、以下の順で引く）:

1. （書き出し側）`species.json` を読み、**全種の `trait2` を一旦消す**（冪等にするため）。
2. 両刀を基準の順で並べる。`len == 38` でなければ停止（両刀の定義が変わったら割り当ては作り直しになるため）。各個体を 除外／優先候補／一般 に分ける。
3. 乱数を作る。
4. **地形特性（先に決める）**: 水→草→氷 の順に、「その属性を `types` に含む、まだ選ばれていない優先候補」（基準の順）から `rng.choice` で1体。いなければ同じ条件の一般から。それもいなければ停止。
   二重属性の個体も候補に含める（地形の属性が2属性のどちらかに一致すればよい）。
5. **5 体になる特性**: `rng.choice(['wide_burst','kowaza_jouzu','shimikomi'])` の 1 つを 5 体、他の 2 つを 4 体にする。
6. **ワイドバースト**: まだ選ばれていない「優先候補かつ部屋技持ち」（基準の順）を `rng.shuffle`、続けて「一般かつ部屋技持ち」（基準の順）を `rng.shuffle`（補充が要らなくてもこの shuffle は実行する）。前者→後者の順に連結した先頭から必要数を取る。足りなければ停止。
7. **こわざじょうず・しみこみ**: まだ選ばれていない優先候補（基準の順）を集める。2 特性の合計数に足りなければ、まだ選ばれていない一般（基準の順）を `rng.shuffle` した先頭から不足数を補う。
   集めた個体を基準の順に並べ直してから `rng.shuffle` し、先頭から こわざじょうず の数、続けて しみこみ の数を配る（優先候補が余る場合は先頭から合計数だけ使う）。
8. （書き出し側）書き出しは `json.dump(species, f, ensure_ascii=False, indent=2)` ＋末尾改行（既存スクリプトと同じ形式）。`trait2` のキーは `trait` の直後に置く（dict の挿入順を組み直す）。`trait2` を持たない個体にはキーを書かない。
9. （書き出し側）標準出力に、特性ごとの個体一覧と件数、`trait2` を持たない両刀の一覧（22 体）を出す。

結果（付録A-1）: 地形 3 体はいずれも優先候補、ワイドバースト 4 体は優先候補の部屋技持ち、こわざじょうず・しみこみ の 9 体は優先候補 8 体＋一般からの補充 1 体（100 ハグミー）。**優先候補 15 体は全員 `trait2` を持ち、一般からは 1 体だけ、除外の 2 体は持たない。**

性質:
- **冪等**: 何度実行しても同じ `species.json` になる。
- 再現性の前提は「両刀 38 種の集合と species_id、各個体の `trait` とその名前、learnset の `Room` 攻撃技の有無」。種族値は読まない。これらが変わると割り当て全体が変わり得る（CLAUDE.md の固定シードの注意と同じ種類の順序依存）。
- `trait2` は種族値合計に影響しないので、`MetaScenario` の固定シード `20260822UL` の生成列（構築の並び）は変わらない。ただし対戦結果は特性で変わる。
- 結果は付録A。**実行結果が付録A と一致しなければ、直さずに報告する。**

### 3-5. C# の変更（ファイルごと）

1. `Scripts/Combat/TraitData.cs`: `public bool Secondary { get; set; }`、`public Dungeon.FieldType FieldOnEntry { get; set; } = Dungeon.FieldType.None;` を追加。
2. `Scripts/Combat/TraitDatabase.cs`: `TraitJson` に `[JsonPropertyName("secondary")] public bool? Secondary`、`[JsonPropertyName("field_on_entry")] public string FieldOnEntry` を追加し、`TraitData` へ写す（`Secondary = j.Secondary ?? false`。`FieldOnEntry` は `Enum.TryParse<Dungeon.FieldType>(…, true, …)`、空・失敗時 None）。
3. `Scripts/Species/SpeciesDatabase.cs`: `SpeciesJson` に `[JsonPropertyName("trait2")] public string Trait2 { get; set; } = "";`、`SpeciesData` への写し（`Trait2 = j.Trait2 ?? ""`）。
   `Validate()` に追加: (a) 空でない `Trait2` が traits.json にあること、(b) その特性の `Secondary` が true であること、(c) `Trait`（1つめ）の特性の `Secondary` が false であること、(d) `Trait2 != Trait`、
   (e) 空でない `Trait2` を持つ個体の `Offense` が両刀（`Mixed`）であること、(f) 1つめの特性の名前に「リーダー」を含む個体の `Trait2` が空であること。違反はエラー文字列を足す（既存の書式に合わせる）。
   **`Trait2` が空なのは正常**（両刀でも 22 体は空）。空をエラーにしない。
4. `Scripts/Species/SpeciesData.cs`: `public string Trait2 { get; set; } = "";`（コメント: 両刀攻撃の調査用の2つめの特性枠。secondary の特性だけが入る。空＝持たない）。
5. `Scripts/Entities/EntityStats.cs`: `public string Trait2 { get; set; } = "";` と `public bool HasTrait(string id) => !string.IsNullOrEmpty(id) && (Trait == id || Trait2 == id);`。`FillFromSpecies` で `Trait2 = species.Trait2 ?? "";`。
6. `Scripts/Dungeon/FloorController.cs`: `public int ApplyFieldOnEntry(Entity actor)` を追加。`actor.Stats.Trait` と `actor.Stats.Trait2` の各 `TraitData.FieldOnEntry` が None でなければ、`actor.GridPosition` を中心とする 3×3 のうち `_grid.InBounds(pos)` かつ `Terrain != Wall` のマスに `_fields.Set(pos, type)`。置いた数を返す（地形特性を持たなければ 0）。置いたときだけログ1行。
7. `Scripts/Battle/BattleArena.cs` `ApplyEntryWeather`: 既存の二重ループ内で `Floor.ApplyWeatherOnEntry(actor);` の直後に `Floor.ApplyFieldOnEntry(actor);` を呼ぶ（同じ順序＝低 BST 陣営が後）。メソッド冒頭のコメントに「地形特性も同じ順で置く」と追記。メソッド名は変えない（呼び出し元 2 箇所を触らないため）。
8. `Scripts/Combat/SecondaryTraits.cs`（新規。`PowerTraitWeather.cs` と同じ形の static クラス）:
   - 定数 `WideBurst = "wide_burst"`・`KowazaJouzu = "kowaza_jouzu"`・`Shimikomi = "shimikomi"`・`WideBurstBonus = 10f`・`KowazaBonus = 20f`・`KowazaPowerCap = 50`。
   - `public static float FlatPowerBonus(Entity attacker, MoveData move)`: §3-2 ①② の合計を返す（`attacker.Stats.HasTrait` で判定）。
   - `public static int AccumulationMultiplier(Entity attacker, Entity target)`: `attacker` が `shimikomi` を持ち `target != attacker` なら 2、それ以外 1。
   - 乱数を使わない純粋な判定にする（回帰スイートで戻り値を直接確かめるため。§4-2）。
9. `Scripts/Turn/AttackAction.cs`:
   - `powerFlatBuff += PowerTraitWeather.FlatPowerBonus(...)`（700 行付近）の直後に `powerFlatBuff += SecondaryTraits.FlatPowerBonus(_attacker, move);`。
   - `AccumulateOnHit` の 3 箇所（1200・1767・1791 行付近）に `SecondaryTraits.AccumulationMultiplier(_attacker, target)` を渡す（多段の箇所は `target`、単体の箇所は実際に蓄積を受ける `target` 変数）。
   - `HasTrait(Entity, string)`（1112 行付近）の中身を `entity != null && entity.Stats.HasTrait(traitId)` に変える（2つめの枠に将来 unique 特性を入れても AttackAction 内の判定が効くように。現状 `trait2` には secondary 特性しか入らないので挙動は変わらない）。
   - `ApplyTraitDrivenAccumulation`・どくせんボディの `AccumulateFlat` には倍率を**掛けない**（確認3）。
   - template 特性の判定（`bondTrait`・`EffectiveMoveType`・`HasMatchingTemplateTrait`）と `PartyElementCensus`・`PowerTraitWeather`・`Entity.cs`・`TurnScheduler` 等の `Stats.Trait ==` は**変えない**（`trait2` に template 特性や既存 unique 特性は入らないことを `Validate` と verify_learnset #20 で保証するため）。
10. `Scripts/Entities/StatusEffectManager.cs` `AccumulateOnHit`: 末尾に引数 `int multiplier = 1` を足し、`Add` に渡す量（基礎＋宣言分、毒/猛毒の量）に掛ける。`AccumulateFlat` は変えない。
11. 表示（最小）:
    - `Scripts/UI/Battle/TeamBuildScreen.cs`（236 行付近）: `sp.Trait2` が空でなければ、1つめの特性の行と同じ形でもう1行（ピル「特性2」・名前・説明）を足す。空なら何も足さない。
    - `Scripts/UI/Battle/SpeciesPickScreen.cs`（168 行付近）: `Trait2` が空でなければ名前を `"{1つめ} / {2つめ}"` で表示。空なら従来どおり。
12. `Scripts/Battle/BattleTestScene.cs`: §4-2 の検査を足す。既存の検査で `Stats.Trait` を一時的に書き換えて戻している箇所は、`Trait2` を持つ個体を使うと結果が変わり得る。**検査用ロスターの個体に `trait2` 持ち（付録A の 16 体）が含まれていないか確認し、含まれていれば既存の検査の冒頭で `Trait2` を退避して空にし、終わりで戻す。**

### 3-6. Python・Artifact の変更

1. `Tools/assign_mixed_traits.py`（新規、§3-4）。付録A-2 の定数と `assign` をそのまま持ち、`if __name__ == '__main__':` で読み込み・書き出し・一覧表示を行う（import しただけでは書き込まない）。
2. `Tools/verify_learnset.py`: **#20「2つめの特性」**を新設。条件:
   - (a) `trait2` を持つのは `offense == "Mixed"` の個体だけで、ちょうど **16 体**。両刀のうち 22 体は持たない（持たないことはエラーではない）。
   - (b) `trait2` は `secondary: true` の特性、`trait` は `secondary` でない。`trait2 != trait`。
   - (c) 件数: wide_burst 4・kowaza_jouzu 5・shimikomi 4・entry_puddle 1・entry_flowerbed 1・entry_thin_ice 1。
   - (d) 地形特性の保持者の `types` がその地形の属性（Puddle＝Water・FlowerBed＝Grass・ThinIce＝Ice）を含む。
   - (e) 1つめの特性の名前に「リーダー」を含む個体は `trait2` を持たない。
   - (f) wide_burst の保持者は全員、部屋技持ち（§3-4 の定義）。
   - (g) 優先候補（§3-4 の定義）が全員 `trait2` を持つか、または `trait2` を持つ一般の個体が 0 体（＝優先候補が余っていないのに一般へ配っていない）。現行データでは前者（優先候補 15 体全員＋一般 1 体）。
   - (h) `assign_mixed_traits.py` の `assign` を import して再計算した結果と `species.json` の `trait2` が一致。
   `[PASS]` は 19 → **20**。`learnset_rules.py` は変えない（特性適応技は `trait` だけで数える。§7）。
3. `Tools/build_ls_payload.py`: 各種族に `'tr2'`（2つめの特性の名前、無ければ `''`）と `'td2'`（説明、無ければ `''`）を足す。
4. `Tools/artifacts/ls_tail.html`（`build_ls_payload.py` が head＋ペイロード＋tail を連結して `learnset_viewer.html` を作る）:
   - 特性の絞り込みドロップダウン（53〜59 行付近の `traitCount`）: `tr` に加えて、**空でない** `tr2` も数えて選択肢に入れる（空文字の選択肢を作らない）。絞り込み（88 行付近）は `s.tr === fTrait || s.tr2 === fTrait`。文字列検索（89 行付近）も `tr2` を含める。
   - カード（164 行付近）: `tr2` が空でなければ `<p class="trait"><b>特性2: …</b> — …</p>` を1行足す。空なら足さない。
5. `python3 Tools/build_ls_payload.py` と `python3 Tools/refresh_move_editor.py` を実行（後者は技データ不変なので `move_editor.html` に差分が出ないはず。出たら報告）。
6. `Tools/analyze_random_by_element.py`: `--by` に **`trait2`**（区分＝`s.get('trait2') or 'なし:' + s['offense']`。両刀で `trait2` を持たない 22 体は `なし:Mixed` になる）と **`species`**（`species_id + ' ' + display_name`。`--only-mixed` 指定時は両刀 38 種の行だけ表示）を足す。
   `trait2` の表示順は `wide_burst, kowaza_jouzu, shimikomi, entry_puddle, entry_flowerbed, entry_thin_ice, なし:Mixed, なし:Physical, なし:Special`。docstring も更新。
   `trait2` 区分の集計を前の CSV に当てるときは、新しい `species.json` で読めば割り当てが過去の試合に当てはまる（species_id が同じなので「もし前の状態で同じ群分けをしていたら」の値が出る）。

---

## 4. 検証方法

### 4-1. 共通（CLAUDE.md）

```sh
dotnet build MysteryDungeonRoguelike.csproj                       # 0 エラー（先に必ず実行。古いアセンブリを使わないため）
godot --headless --path . Scenes/BattleTestScene.tscn > run.log    # 回帰スイート
grep '\[検証' run.log | grep -c NG                                 # 0 を 6 回連続
python3 Tools/verify_learnset.py; python3 Tools/verify_attack_moves.py; python3 Tools/verify_npc_teams.py
git diff                                                           # 本書と突き合わせる
```

回帰スイートは NG があっても終了コード 0 で終わる。**判定は NG を含む `[検証` 行の数**。乱数で時々落ちる項目を見逃さないよう 6 回連続で実行する。
headless 実行のスプライト import エラーは無視してよい。`dotnet`/`godot` が無ければ CLAUDE.md の手順で入れる。

### 4-2. 回帰スイートに足す検査（「効く条件で効く／効かない条件で効かない」を必ず対で）

`BattleTestScene.VerifyArena` から呼ぶ新メソッド `VerifySecondaryTraits()` にまとめる。既存の `VerifyPowerTraitWeather` と同じく、特性を一時的に設定して戻す。

| # | 検査名（`[検証] …:` の文言は実装者が決めてよい） | 期待値 |
|---|---|---|
| 1 | ワイドバースト: 部屋技で+10・単体技で0 | `FlatPowerBonus(Trait2=wide_burst, Room 特殊技)` = 10、同・`Adjacent` 技 = 0、特性なし・Room 技 = 0 |
| 2 | ワイドバースト: 物理の部屋技（`MV_170`）にも+10 | 10 |
| 3 | こわざじょうず: 特殊・威力50以下で+20、それ以外は0 | 特殊・威力 50 の技 = 20、特殊・威力 55 以上の技 = 0、物理・威力 50 以下の技 = 0、変化技 = 0 |
| 4 | しみこみ: 倍率は相手に2・自分に1・非保持者は1 | 2 / 1 / 1 |
| 5 | しみこみ: 宣言なしの属性技1発で蓄積+50（非保持者は+25） | 既存 `VerifyHollow` の 1. と同じ形（16 回まで撃って最初の命中で止める）で、保持者 50・非保持者 25（`GetAccumulation` で読む） |
| 6 | しみこみ: 特性の固定加算は2倍にしない（確認3） | Trait=`let_us_hug`・Trait2=`shimikomi` の攻撃側が、**無属性・状態異常の宣言なし・単体の物理技**を状態異常なしの相手に1回命中させる → 相手の `GetAccumulation(VineBound)` = 500（1000 にならず発症もしない）。Trait2 を空にしても 500 |
| 7 | 2つの特性が同時に効く（Trait=こうふん＋Trait2=こわざじょうず） | 隣接の相手に威力 50 以下の特殊技（命中 100・単体・追加効果なし）を `MeasureHit` で測り、両方 > こうふんのみ > 特性なし、かつ両方 > こわざじょうずのみ |
| 8 | `Stats.HasTrait` が両方の枠を見る | Trait=A, Trait2=B で `HasTrait(A)`・`HasTrait(B)` が true、`HasTrait(C)` が false。Trait2 が空のとき `HasTrait("")` は false |
| 9 | 地形特性: 保持者の3×3が全てその地形・外側は無し | 1 対 1 の盤（`VerifyEntryWeather` の `Probe` と同じ組み方）で保持者を立て `ApplyEntryWeather`。保持者のマスと周囲8マス（盤面内）が Puddle、5×5 の外周 16 マスは None。保持者のマスも含むこと |
| 10 | 地形特性: 非保持者は地形を置かない | 同じ盤で特性なしの個体だけ → `Fields.Count == 0` |
| 11 | 地形特性: 重なるマスは低種族値側が残る | 高 BST 陣営に みずたまりのぬし、低 BST 陣営に はなばたけのぬし を隣接させて立てる（立てる順を入れ替えた2回）。重なるマスが2回とも FlowerBed |
| 12 | 種族データ: 付録A の 16 体だけが2つめの特性を持つ | `SpeciesDatabase` の全種で `Trait2` 非空の個体が 16 体・全員 `Offense == Mixed`、特性ごとの件数 4/5/4/1/1/1、021B と 036 の `Trait2` が空、`Validate()` のエラー 0 |

153 → **165 項目**（目安。実際の数を報告する）。

### 4-3. フェーズと完了条件

| フェーズ | 内容 | 完了条件 |
|---|---|---|
| P0 準備 | ツールチェーン確認、前提の確認 | `dotnet build` 0 エラー、回帰 NG 0（153 項目）を1回、Python 3 本が **19/25/9** PASS、`/tmp/aoe_runs/final24_{1..4}.csv` の存在確認と退避（§5-1） |
| P1 データ | traits.json に 6 件、`assign_mixed_traits.py`、`species.json` に `trait2`（16 体）、verify_learnset #20 | 付録A と一致（16 体・4/5/4/1/1/1・22 体は無し）。スクリプトの再実行で `git diff Data/` が変わらない。Python 3 本 **20/25/9** PASS |
| P2 C# | §3-5 の 1〜11 | `dotnet build` 0 エラー。回帰 NG 0（既存 153 項目のまま）を1回。`Validate()` のエラー 0 |
| P3 回帰 | §4-2 の 12 項目 | **165 項目**で NG を含む `[検証` 行 0 を **6 回連続** |
| P4 Artifact・集計道具 | §3-6 の 3〜6 | `learnset_viewer.html` に `tr2` が入り、特性2で絞り込める（ブラウザ不要。生成 HTML 内のペイロードで空でない `tr2` が 16 件（`"tr2":"ワイドバースト"` 4・`"こわざじょうず"` 5・`"しみこみ"` 4・地形 3 種 各1）、`traitCount` が空でない tr2 だけを数えることをコードで確認）。`move_editor.html` に差分なし。`analyze_random_by_element.py --by trait2` が前の CSV で §5-1 の表と同じ値を出す |
| P5 計測・記録 | §5 | **3,000 試合ちょうど**（上限。追加しない）、未決着率 2% 以下、`pvp_spec.md` §32 を追記。**結果が基準を超えても調整しない** |
| P6（任意）レッツハギング | §9 | **ユーザーが §9 の案を選んでから**着手する。P5 の計測が終わってから行い、P5 の計測には含めない。完了条件は §9-5 |

---

## 5. 調査としての計測

### 5-1. 前の値（変更前）

- 前の計測は §31-8（`964763b`、3,000 試合）。CSV が `/tmp/aoe_runs/final24_1.csv`〜`final24_4.csv` に残っている。**P0 で存在を確認し、作業用ディレクトリ（例 `/tmp/mixed_traits/before/`）へコピーしておく。** 無ければ報告し、下の表（本書の改稿時に計算済み）を前の値として使う。前の計測をやり直してはいけない（試合数の上限）。
- 付録A の割り当てで前の CSV を群分けした値（`--bootstrap 200`。改稿時に、付録A-2 の割り当てを `role` 列に入れた一時的な species.json と既存の `analyze_random_by_element.py --by role` で計算）:

| 区分（前） | 体数 | 出場数 | 勝率% | 期待% | 補正後pt (SE) |
|---|---|---|---|---|---|
| 両刀/ワイドバースト | 4 | 284 | 39.1 | 49.1 | -10.0 (2.85) |
| 両刀/こわざじょうず | 5 | 395 | 42.3 | 51.8 | -9.5 (2.44) |
| 両刀/しみこみ | 4 | 364 | 47.0 | 52.0 | -5.0 (2.54) |
| 両刀/地形3体 | 3 | 175 | 38.9 | 50.1 | -11.2 (3.49) |
| 　みずたまりのぬし（169B） | 1 | 61 | 44.3 | 48.2 | -3.9 (6.34) |
| 　はなばたけのぬし（084） | 1 | 69 | 33.3 | 50.9 | -17.6 (5.19) |
| 　うすらひのぬし（101） | 1 | 45 | 40.0 | 51.3 | -11.3 (6.44) |
| **両刀/trait2なし（なし:Mixed）** | 22 | 1752 | 44.1 | 49.3 | -5.2 (1.18) |
| なし:Physical | — | 11610 | 48.6 | 49.9 | -1.2 (0.30) |
| なし:Special | — | 9044 | 53.9 | 50.2 | +3.8 (0.40) |
| 両刀 全体 | 38 | 2970 | 43.4 | 50.0 | -6.6 (0.91) |

- 読み方の注意: `trait2` を持つ 16 体は 1つめの特性が template（きずな・ちから 等）の個体に偏っており（優先候補の規則による）、**変更前から** なし:Mixed（-5.2）より低い群がある。
  したがって群どうしを比べず、**各群の「後 − 前」**と、なし:Mixed（`trait2` を持たない両刀 22 体。特性は変わらない対照群）の「後 − 前」との差で読む。

### 5-2. 計測（変更後）

```sh
dotnet build MysteryDungeonRoguelike.csproj
mkdir -p /tmp/mixed_traits
for k in 1 2 3 4; do
  godot --headless --path . Scenes/BattleMatchScene.tscn -- --random --matches 750 --out /tmp/mixed_traits/after_$k.csv &
done; wait
```

（`--random` は状態を持たないので 4 プロセス並列でよい。§31-4 と同じ「`--random --matches 750 --out` を4プロセス並列」。シーンは `Scenes/BattleMatchScene.tscn`。）合計 **3,000 試合ちょうど**。追加の試合はしない。

### 5-3. 集計

```sh
A=/tmp/mixed_traits/after_*.csv; B=/tmp/mixed_traits/before/final24_*.csv
python3 Tools/analyze_random_by_element.py $A --by offense --bootstrap 200   # 攻撃区分（前: §31-8 の表）
python3 Tools/analyze_random_by_element.py $A --by role    --bootstrap 200   # 役割
python3 Tools/analyze_random_by_element.py $A --by element --bootstrap 200   # 属性（参考）
python3 Tools/analyze_random_by_element.py $A --by trait2  --bootstrap 200   # 特性別（後）
python3 Tools/analyze_random_by_element.py $B --by trait2  --bootstrap 200   # 特性別（前。新しい species.json で群分け。§5-1 の表と一致するはず）
python3 Tools/analyze_random_by_element.py $A --by species --only-mixed      # 個体別（後）
python3 Tools/analyze_random_by_element.py $B --by species --only-mixed      # 個体別（前）
```

記録（`docs/design/pvp_spec.md` に **§32「両刀攻撃の2つめの特性（調査）」** を追加。§31 の書式に合わせる）:

1. 決定と経緯（ユーザー原文、§0-2 の確認結果）。
2. 新特性 6 種の定義と、割り当て表（付録A-1 をそのまま。割り当ての規則 §3-4 の要約も）。
3. 計測の概要: 試合数・決着・引分・未決着率・k・BST6 が高い側の勝率。
4. **前後比較表**: 攻撃区分 3 行・役割 9 行・属性 10 行（前は §31-8 の値）、判定列（○/×/境界。§31-8 と同じ定義: 基準は攻撃区分 ±3pt・役割と属性 ±5pt、境界＝|補正後pt − 基準| < SE）。
5. **特性別の前後比較表**: `wide_burst`・`kowaza_jouzu`・`shimikomi`・地形3体まとめ（と個別3行）・`なし:Mixed`・`なし:Physical`・`なし:Special`。
   列: 前の補正後pt(SE)・後の補正後pt(SE)・差（後−前）・差の SE（√(前SE²+後SE²)）・**対照差**（その群の差 − なし:Mixed の差）。
6. 個体別の前後表（38 行。`trait2` の有無の列を付ける。出場数が少ないので参考扱いと明記。1 個体の出場は 3,000 試合で 45〜120 回程度、SE はおよそ 5〜7pt）。
7. 読み方の注記: 特性 1 群（4〜5 体）の出場は 3,000 試合で 280〜400 回、補正後pt の SE は約 2.4〜2.9pt なので、**差の SE は約 3.5〜4pt**（地形 3 体まとめは約 5pt、地形 1 体は約 8〜9pt）。
   **差が 2SE（1 群で約 7〜8pt、地形まとめで約 10pt）を超えない群は「効果は検出できない」と書く**（効果が無いとは書かない）。
   ワイドバーストは部屋技を持つ個体でも 4 技のうちに部屋技が入る確率が低く（learnset 約 20〜30 技中 1〜2 技）、効果は小さいと見込まれる。
   なし:Mixed の差が大きく動いた場合は、特性以外の要因（乱数のゆらぎ・相手側の変化）が混ざっていると書く。
8. 基準を超えた行があっても調整しない。結果をそのまま報告し、以後の判断はユーザーに委ねる。

---

## 6. 判断が必要だった点

### 6-1. ユーザーの確認で決まったこと（§0-2）

1. **置き換え（確認1）**: 2つめの特性は1枠（`trait2`）。地形特性の3体はランダム3種を持たない。ランダム3種は 4・5・4 の計 13 体だけで、`trait2` は計 16 体。両刀 22 体は `trait2` を持たない。
2. **除外と優先（確認1）**: 1つめの特性の名前が「リーダー」を含む個体には与えない。「きずな・ちから・おしえ・まもり・式」を含む個体を優先候補にする。
   判定は特性の**名前の部分一致**で行う（ユーザーの言い方が名前ベースのため）。現行データでは template_kind の bond/power/oshie/guard/weakness と完全に一致する。
3. **地形の個体の選び方（確認2）**: 各属性の優先候補から固定シードで無作為。
4. **しみこみの範囲（確認3）**: 技そのものが与える蓄積だけ2倍。特性の固定加算は含めない。
5. **ワイドバーストの個体（確認4）**: 優先候補のうち部屋技持ちから無作為。

### 6-2. 本書で決めたこと（ユーザー確認不要と判断）

1. **データの持ち方**: `trait` はそのまま、別項目 `trait2` を足す（配列化はしない）。既存の約 60 箇所の `Stats.Trait ==` を書き換えずに済み、影響を新しい特性の判定に閉じ込められる。代わりに「`trait2` には secondary 特性しか入れない」を検証で保証する。
2. **地形の効果**: 指示の「みずたまり／はなばたけ／うすらひ」は既存の地形の名前そのものなので、既存の効果（§2-5）をそのまま使う。新しい効果は定義しない。
3. **「場に出たとき」**: 対戦には入れ替えが無いので、対戦開始時（天候特性と同じ時点・同じ順）。
4. **地形を置くマス**: 保持者のマスと周囲8マス（チェビシェフ距離1）。個体がいるマスも含める（「自身がいるマス」を含むと明記されているため）。盤面外・壁は除く。
5. **重なり**: 天候と同じく低 BST 陣営が後で上書き（既存の「非力な側への補償」の向きにそろえる）。
6. **①の範囲**: 射程 `Room` だけ（物理の部屋技も含む）。`Area`・`Surrounding` は含めない。
7. **②の威力の基準**: `move.Power`（-20 調整後のデータ値）。パープルヘイズの前例どおり加算前の値。変化技は対象外。
8. **割り当ての乱数**: 実行時の乱数ではなく、固定シードのデータ生成（付録A-2）。シード値 `20261028`（草案のシード方針どおり。作成日。既存の `20261027`（bootstrap）・`20260822`（MetaScenario）と重ならない）。
9. **「4・5・4」のどれを 5 体にするか**: 呼び出し側の指示に従い固定シードで決める（手順5）。結果は こわざじょうず が 5 体で、ユーザーが特性を挙げた順（ワイドバースト・こわざじょうず・しみこみ）に「4・5・4」を当てはめた読み方とも一致する。どちらの読み方でも結果は同じなので、確認は要らない。
10. **選ぶ順番（優先候補が先に埋まる順序）**: 地形 → ワイドバースト → こわざじょうず・しみこみ。
    地形は属性の条件があり、ワイドバーストは部屋技の条件があるので、**条件の厳しいものから先に**選ぶ。条件の無い こわざじょうず・しみこみ を最後にすると、優先候補の不足分（15 体に対して 16 枠）を条件の無い枠で吸収でき、地形とワイドバーストは優先候補だけで埋まる。
11. **補充の仕方**: 優先候補が足りない分は、除外を除いた一般の個体から固定シードで無作為に補う（ワイドバーストは一般の部屋技持ちから、こわざじょうず・しみこみ は一般全体から）。補充した個体は優先候補と一緒にシャッフルしてから配るので、どちらの特性になるかも無作為。現行データの補充は 1 体（100 ハグミー → しみこみ）。
12. **二重属性の個体を地形の候補に含める**: 指示は「そのパルの属性に応じて」で、二重属性の個体もその属性を持つ。外すと草（084 草/虚・108 草）と水（169B 水/電・188B 水）の候補が1体ずつになり無作為にならない。結果として 169B（水/電）がみずたまり、084（草/虚）がはなばたけ になった。
13. **こわざじょうず・しみこみ の個体の向き不向きは見ない**（ユーザー指示「ランダム」）。付録A の「蓄積」列（属性の蓄積が入る攻撃技の数）を見ると、146 ゼノルーダ（竜）は しみこみ で効く技が 20 中 5 しかない。計測の読み方の注記（§32 の 7.）に書くだけで、割り当ては変えない。
14. **learnset**: 変えない。2つめの特性は特性適応技の数え方（`adapted()`）にも入れない（learnset を変えると特性の効果と learnset の変化が混ざって調査にならないため）。
15. **表示**: 構築画面・種族選択画面に2つめの特性を表示する最小の変更だけ。`trait2` を持たない個体は従来どおり。地形の盤面描画はしない。
16. 記録先は `pvp_spec.md` §32（CLAUDE.md の「現在 §27 まで」は古く、実際は §31 まである）。

### 6-3. 前提の確認で分かったこと（実装者の注意）

- しみこみ を持つ 100 ハグミーは1つめの特性がレッツハギング。確認3 により +500 は2倍にならない（§4-2 #6 で確かめる）。もう1体のレッツハギング持ち 100B ハグユーは `trait2` を持たない。
- どくせんボディ持ち（016・030）、こうふん持ち（073・073B）、フリーフォール持ち（064B・112B）は一般で、`trait2` を持たない。
- みずたまりのぬし 169B トノサマンボ（水/電）は自分のマスがみずたまりになるので、自分が受ける電気技が ×1.25 になる。
- はなばたけのぬし 084 アロアリュー（草/虚）・うすらひのぬし 101 ツララジカ（氷）・みずたまりのぬし 169B は、いずれも部屋技を覚えない（ワイドバーストの候補を減らさない）。
- ツタまみれ（レッツハギングで入る状態異常）は、現状、持ち物の HP 回復と吸収技の回復だけを止め、はなばたけ・天候の回復（`EntityStats.Heal` 経由）は止めない（§9-1）。本書では変えない。

### 6-4. 実装時に判断してよい範囲／決めずに報告する範囲

判断してよい:
- ログ・`[検証]` 行の文言、コメント、変数名、新関数の配置（指定したファイル内での位置）。
- 回帰検査で使う技・個体の選び方（条件を満たす技を `MoveDatabase` から条件で引く形にする。id の直書きは `MV_170` だけ可）。
- `ls_tail.html` の見た目の細部。

決めずに報告する:
- 付録A と割り当て結果が一致しない。
- 既存の回帰検査が `trait2` の追加で NG になり、§3-5-12 の退避で直らない。
- `Validate()` が既存データでエラーを出す。
- `move_editor.html` に差分が出る。
- 前の CSV（`/tmp/aoe_runs/final24_*.csv`）が無い。
- `--by trait2` で前の CSV を集計した値が §5-1 の表と一致しない。
- 計測の未決着率が 2% を超える。
- 本書に書いていない既存の `Stats.Trait` 参照で、`trait2` を見ないと仕様どおりにならない箇所を見つけた。

---

## 7. やらないこと

- 既存の特性・技・種族値・learnset・NPC 構築の変更。2つめの特性に合わせた learnset の調整（部屋技を足す等）。
- `trait2` を持たない両刀 22 体への特性の付与、両刀以外の個体への付与。
- 計測結果を見ての特性の調整（威力 +10/+20・倍率 2 の変更、割り当ての作り直し）。**結果はそのまま報告する。**
- 3,000 試合を超える計測、前の計測のやり直し。
- 迷宮（ダンジョン）での地形特性の発動。①②③は `AttackAction` が共通なので迷宮でも効くが、迷宮での検証はしない。
- 戦闘画面での地形の描画（技で置いた地形も現状描画されていない）。
- 既存の `Stats.Trait ==` 判定（`PartyElementCensus`・`PowerTraitWeather`・`Entity.cs`・`TacticalBuilder` 等）を2枠対応にすること（`AttackAction.HasTrait` だけは §3-5-9 で2枠対応にする）。
- 構築 AI（`TacticalBuilder`）・NPC 構築生成器に新特性を考慮させること。
- **レッツハギング（§9）の変更を P1〜P5 に混ぜること。** §9 はユーザーの確認後に P6 として別に行う。ツタまみれの回復阻害の範囲（§9-1 の (f)）の変更もしない。
- `docs/export/learnsets.csv`（既に古い出力）の再生成。
- Artifact の再公開（検証者が行う）。commit・push。

---

## 8. ユーザーに確認すべき点

確認1〜4 は §0-2 のとおり決着済み。残るのは次の1点（本書の P1〜P5 とは独立。決まるまで P6 に着手しない）。

**確認5. レッツハギングの要件をどう確定するか**（詳細は §9）
- (A) **現行の挙動のまま、説明文だけ確定**: 物理技が当たった相手ごとにツタまみれの蓄積 +500（2回で発症）。ハグミーは攻撃技 24 中 8、ハグユーは 26 中 20 で働く。
- (B) 発動条件を**接触技**に変える（+500 のまま）: 名前（抱きつく）に合う。ハグユーは変わらず 20、ハグミーは 8 → 6 に減る。
- (C) 分類を問わず、**攻撃技の命中ごとに +250**（4回で発症）: 両刀らしく全攻撃技で働く。ハグミー 24、ハグユー 26。1回あたりの量は半分。
- **推奨: (A)**。理由は §9-3。
- 付随: 同じ仮置きの説明文を持つ **ひょうてんま**（191 ヒョウガオー・200 グレイシャル。両刀ではない）も、同じ案で一緒に確定するか（推奨: 一緒に (A) で確定。説明文だけの変更で挙動は変わらない）。

---

## 9. 提案: レッツハギングの要件の確定（ユーザーの確認待ち）

ユーザーの依頼:「ところでレッツハギングの要件を曖昧なままにしているので確定させたいです。提案してください」。
**この節は提案であり、ユーザーが §8 確認5 で案を選ぶまで実装しない。** 実装は P6（P5 の計測のあと、P5 とは別の作業）。

### 9-1. 現行の挙動（コードから読み取ったもの）

- 定義（`Data/traits.json` の `let_us_hug`）の説明文は仮置き: 「特性発の状態異常蓄積付与(技のAilmentChanceとは別枠)。具体的な発動条件・対象状態異常・確率は段階2-b実装時に個別確定する。」
  ひょうてんま `hyouten_ma` も同じ文面。回帰スイートにも設計文書にも、レッツハギングの検査・記述は無い。
- 保持者: **100 ハグミー**（草・両刀/物理耐久 `Mixed-Physical`。A 47・C 70 で魔力寄り）と **100B ハグユー**（闇/草・両刀/特殊耐久 `Mixed-Special`。A 73・C 50 で攻撃寄り）の 2 体だけ（どちらも両刀）。
- 実装: `AttackAction.ApplyTraitDrivenAccumulation(move, target)`（1800〜1807 行）。
  - (a) **発動条件**: `move.Category == Physical` のときだけ。接触かどうかは見ない。変化技は対象外。
  - (b) **量**: 命中した相手に `AccumulateFlat(VineBound, 500)`。確率ではなく**確定の固定加算**で、蓄積は 0〜1000・1000 で発症なので、**物理技を2回当てると必ずツタまみれ**（その時点で相手の全蓄積は 0 に戻る）。
    自分の草技は別に基礎 +25（＋宣言分）を同じツタまみれの蓄積に足すので、草の物理技なら1回 +525。
  - (c) **多段技**: 1回の技につき1回（`ApplyAilmentEffectIfAny`/`ApplyAoeAilment` は技ごと・相手ごとに1回だけ呼ばれる。多段の2発目以降は `StrikeMultiHit` が基礎分だけ足し、レッツハギングは足さない）。両者の learnset に多段技は無い。
  - (d) **範囲技**: 当たった相手ごとに +500（`ApplyAoeAilment`）。両者の物理技は Adjacent と TwoTile だけで、範囲の物理技は無い。
  - (e) **入らない条件**: 外れた・ダメージ 0、その一撃で相手が倒れた、相手が既に何かの状態異常（`Ailment != None`）、攻撃側が泥まみれ、相手が きょうじんなからだ（二次効果ブロック）。
  - (f) **ツタまみれの効果**: 行動サイクル4回で必ず解除。持ち物の HP 回復（`UseItemAction` 44 行）と吸収技の回復（`AttackAction.ApplyDrain` 1608 行）を止め、わたほうし の判定に使われる。
    状態異常の説明は「あらゆる回復を無効化」だが、**はなばたけ・天候による回復（`EntityStats.Heal`。`HealBlockedCheck` は回復封じの制限しか見ない）は止めていない**。本提案の範囲外（変えるなら別の指示として扱う）。
  - (g) **しみこみとの重複**: ユーザー確認3 により、しみこみ は +500 を2倍にしない（付録A で 100 ハグミーが しみこみ を持つ）。
- **曖昧なところ**: 説明文が仮のため、(a) 物理か接触か、(c) 多段技を1回と数えるか1発ごとか、(d) 範囲技で全員に入るか、(b) 「確率」と書いてあるが実際は確定の +500 であること、(g) 他の蓄積系特性との重ね方、(e) どの条件で入らないか、がどこにも書かれていない。

### 9-2. 案（データは `964763b` の `Data/species.json`・`Data/moves.json` から計算）

両者の learnset の攻撃技（威力>0、重複除く）: ハグミー 24（物理 8・特殊 16・接触 6。物理のうち非接触 2＝TwoTile）、ハグユー 26（物理 20・特殊 6・接触 20。物理は全て接触）。全技でも特殊の接触技は 0 件なので、「接触技」は「物理技のうち接触のもの」と同じになる。
`--random` は learnset（ハグミー 29 技・ハグユー 31 技。変化技 各5）から無作為に 4 技を選ぶので、「4 技中に働く技が入る数の期待値」も示す。

| 案 | 発動条件と量 | ハグミー: 働く攻撃技 / 4技中の期待数 / 攻撃技1回あたりの期待蓄積 | ハグユー: 同 | 変更 |
|---|---|---|---|---|
| **A 現行維持・文言確定** | 物理技の命中ごと +500（2回で発症） | 8/24 / 1.10 / 167 | 20/26 / 2.58 / 385 | 説明文のみ。挙動は不変 |
| B 接触技に変更 | 接触技の命中ごと +500（2回で発症） | 6/24 / 0.83 / 125 | 20/26 / 2.58 / 385（不変） | `ApplyTraitDrivenAccumulation` の条件を `move.IsContact` に。ハグミーの TwoTile 物理 2 技が外れる |
| C 分類を問わず +250 | 攻撃技（威力>0）の命中ごと +250（4回で発症） | 24/24 / 3.31 / 250 | 26/26 / 3.35 / 250 | 条件を `move.Category != Status` に、量を 250 に。ハグミーは強化、ハグユーは1回あたり弱化 |

（「攻撃技1回あたりの期待蓄積」＝ learnset の攻撃技を等確率で使ったときの、1命中あたりのツタまみれ蓄積の期待値。草技の基礎 +25 は含めない。）

各案の効果:
- **A**: 魔力寄りのハグミーでは働く技が少なく（4 技中 約1.1）、物理寄りのハグユーでは働く技が多い（約2.6）。特性の効き方が2体で大きく違うが、説明どおりの一貫した規則ではある。
- **B**: 名前の「ハギング（抱きつく）」に最も合う。ハグユーには影響なし。ハグミーは非接触の物理 2 技（TwoTile）で働かなくなり、さらに弱くなる。
- **C**: 両刀の個体らしく、特殊技でも働く。2体の効き方がそろう。発症に4回かかるので1試合での発症は遅くなるが、両者とも4技のほぼ全部で働く。ハグミーは実質強化、ハグユーは実質弱化。数値（250）は新しく決める値で、根拠は「2体の期待蓄積を A のハグユー（385）とハグミー（167）の間に置く」程度。

### 9-3. 推奨: A（現行の挙動のまま説明文を確定）

1. ユーザーの依頼は「要件を確定させたい」で、現行の挙動は 9-1 のとおりコード上は一貫している。曖昧なのは説明文だけなので、挙動を変えずに文言で確定するのが最小で確実。
2. いま進めている両刀攻撃の調査（P5）の前後比較に、レッツハギングの変更が混ざらない。B・C は両刀 2 体の強さを変えるので、P5 の「前」の値（§5-1）と比べられなくなる。P5 のあとに入れる場合も、効果を測るには別の計測が要る（試合数の上限に触れる）。
3. ハグミーで働く技が少ない点は、P5 の個体別の表（100・100B の行）で状況を見てから判断できる。そこで問題があれば、改めて B か C を選べばよい。

`Data/traits.json` に入れる説明文の案（A）:

```
物理技が当たるたびに、当たった相手のツタまみれの蓄積値+500（2回当てるとツタまみれになる）。多段技は1回の技につき1回、範囲技は当たった相手ごと。すでに状態異常の相手・倒れた相手には入らない。
```

（B を選んだ場合は先頭を「接触技が当たるたびに」に、C の場合は「攻撃技が当たるたびに、当たった相手のツタまみれの蓄積値+250（4回当てるとツタまみれになる）」にする。）

ひょうてんま も A で一緒に確定する場合の説明文案:

```
物理技が当たるたびに、当たった相手のこおりの蓄積値+500（2回当てるとこおりになる）。多段技は1回の技につき1回、範囲技は当たった相手ごと。すでに状態異常の相手・倒れた相手には入らない。
```

### 9-4. P6 の変更内容（ユーザーが案を選んだあと）

- 全案共通: `Data/traits.json` の `let_us_hug`（と、一緒に確定する場合は `hyouten_ma`）の `description` を 9-3 の文に置き換える。
  `AttackAction.ApplyTraitDrivenAccumulation` の冒頭コメントを確定した規則に書き換える。
- B: `ApplyTraitDrivenAccumulation` の `move.Category != MoveCategory.Physical` を、レッツハギングだけ `!move.IsContact` に分ける（ひょうてんま は物理のまま。一緒に B にする指示があれば両方）。
- C: レッツハギングだけ条件を `move.Category == MoveCategory.Status` で戻る形にし、量を 250 にする（定数として名前を付ける）。
- 回帰スイートに `VerifyLetUsHug()` を足す（全案共通の形。期待値は案ごと）:
  1. 物理・単体・無属性・宣言なしの技を1回命中 → 相手のツタまみれの蓄積 = 500（A・B。B は接触技で。C は 250）。
  2. 同じ技をもう1回命中 → 相手がツタまみれ・蓄積は全て 0（A・B。C は4回目で発症）。
  3. 特殊技を1回命中 → A・B は 0、C は 250。B のみ: 非接触の物理技 → 0。
  4. 相手が既に状態異常 → 増えない。
- `python3 Tools/build_ls_payload.py` で learnset インスペクタを再生成（特性の説明文がペイロードに入っているため）。`refresh_move_editor.py` も実行（差分なしのはず）。

### 9-5. P6 の完了条件

- `dotnet build` 0 エラー。回帰スイート（165 ＋ P6 で足す項目）で NG を含む `[検証` 行 0 を **6 回連続**。Python 3 本 20/25/9 PASS。
- `traits.json` の説明文が選んだ案の文面と一致。`learnset_viewer.html` に新しい説明文が入っている。
- 計測はしない（B・C を選んだ場合に計測が必要かは、ユーザーに改めて確認する。3,000 試合の上限は P5 で使い切っている）。

---

## 付録A. 割り当て表（両刀 38 種。他の 249 種は `trait2` なし）

### A-1. 表（付録A-2 の参照実装をシード 20261028・Python 3.11 で実行した結果）

列: 区分＝優先（優先候補）／除外（リーダー持ち）／一般。部屋＝learnset 中の `Room` の攻撃技の数。特50＝威力50以下の特殊技の数。蓄積＝攻撃技のうち属性の蓄積が入る技の数（実効属性が無・竜以外、または毒/猛毒の宣言あり）/ 攻撃技の数（いずれも重複を除く）。

| species_id | 名前 | 属性 | 役割 | 1つめの特性 | 区分 | 部屋 | 特50 | 蓄積 | trait2 | 選ばれた理由 |
|---|---|---|---|---|---|---|---|---|---|---|
| 007 | ルミカイト | 水 | Mixed-Special | ディープダイブ (deep_dive) | 一般 | 0 | 10 | 20/23 | — | 一般・補充に当たらず |
| 007B | エレカイト | 水/電 | Mixed-Both | ぜつえんたい (zetsuentai) | 一般 | 0 | 5 | 24/27 | — | 一般・補充に当たらず |
| 016 | ンダコアラ | 虚 | Mixed-Special | どくせんボディ (dokusen_body) | 一般 | 3 | 7 | 20/23 | — | 一般・補充に当たらず |
| 021B | アオパオー | 氷 | Mixed-Physical | 冷却のリーダー (reikyaku_leader) | 除外 | 0 | 4 | 19/22 | — | 除外（リーダー） |
| 022 | ネムラム | 虚 | Mixed-Special | チェイサー (chaser) | 一般 | 0 | 3 | 21/24 | — | 一般・補充に当たらず |
| 023 | エテッパ | 草 | Mixed-Both | ふわふわ (fuwafuwa) | 一般 | 0 | 4 | 19/22 | — | 一般・補充に当たらず |
| 030 | ヤミイカ | 虚/水 | Mixed-Physical | どくせんボディ (dokusen_body) | 一般 | 1 | 8 | 24/27 | — | 一般・補充に当たらず |
| 036 | ドンモップ | 氷 | Mixed-Physical | もふもふのリーダー (mofumofu_leader) | 除外 | 0 | 4 | 19/22 | — | 除外（リーダー） |
| 041B | アズルーナ | 氷/虚 | Mixed-Physical | たまゆき (tamayuki) | 一般 | 3 | 6 | 19/22 | — | 一般・補充に当たらず |
| 042 | ボルトラ | 電 | Mixed-Special | けいかいかん (keikaikan) | 一般 | 0 | 4 | 19/22 | — | 一般・補充に当たらず |
| 048B | ユメンダコ | 水/無 | Mixed-Special | ディープダイブ (deep_dive) | 一般 | 0 | 8 | 12/25 | — | 一般・補充に当たらず |
| 064B | シロカバネ | 氷/闇 | Mixed-Special | フリーフォール (free_fall) | 一般 | 0 | 5 | 22/25 | — | 一般・補充に当たらず |
| 073 | ダリザード | 虚 | Mixed-Both | こうふん (koufun) | 一般 | 0 | 5 | 22/25 | — | 一般・補充に当たらず |
| 073B | チリザード | 炎 | Mixed-Physical | こうふん (koufun) | 一般 | 0 | 10 | 20/22 | — | 一般・補充に当たらず |
| 083B | ムラサメ | 電 | Mixed-Physical | 雷のきずな (electric_bond) | 優先 | 1 | 3 | 15/19 | kowaza_jouzu | 手順7 の抽選 1 番目 → こわざ |
| 084 | アロアリュー | 草/虚 | Mixed-Special | 草のちから (grass_power) | 優先 | 0 | 5 | 22/25 | **entry_flowerbed** | 手順4 草の優先候補 {084, 108} から抽選 |
| 084B | ビリビリュー | 電/虚 | Mixed-Both | 雷のちから (electric_power) | 優先 | 2 | 9 | 22/25 | wide_burst | 手順6 優先候補の部屋技持ち 9 体の抽選 1 番目 |
| 094B | パリピドン | 竜/電 | Mixed-Physical | ハードアーマー (hard_armor) | 一般 | 1 | 6 | 16/23 | — | 一般・補充に当たらず |
| 096 | ライバード | 電 | Mixed-Special | 雷のおしえ (electric_oshie) | 優先 | 2 | 6 | 20/21 | kowaza_jouzu | 手順7 の抽選 2 番目 → こわざ |
| 100 | ハグミー | 草 | Mixed-Physical | レッツハギング (let_us_hug) | 一般 | 0 | 9 | 21/24 | shimikomi | 手順7 優先候補が1体不足 → 一般 21 体の抽選 1 番目で補充、抽選 9 番目 → しみこみ |
| 100B | ハグユー | 闇/草 | Mixed-Special | レッツハギング (let_us_hug) | 一般 | 0 | 3 | 24/26 | — | 一般・補充に当たらず |
| 101 | ツララジカ | 氷 | Mixed-Both | 氷のまもり (ice_guard) | 優先 | 0 | 9 | 18/21 | **entry_thin_ice** | 手順4 氷の優先候補 {101, 133} から抽選 |
| 108 | ラブラドン | 草 | Mixed-Physical | 草のちから (grass_power) | 優先 | 2 | 5 | 17/21 | wide_burst | 手順6 抽選 4 番目 |
| 112B | シンエンオ | 炎/闇 | Mixed-Both | フリーフォール (free_fall) | 一般 | 1 | 7 | 18/20 | — | 一般・補充に当たらず |
| 121 | レヴィドラ | 竜/水 | Mixed-Both | グロリアスミスト (glorious_mist) | 一般 | 3 | 7 | 9/23 | — | 一般・補充に当たらず |
| 127 | ヤクモマル | 無 | Mixed-Both | 無のちから (neutral_power) | 優先 | 1 | 5 | 7/24 | wide_burst | 手順6 抽選 3 番目 |
| 133 | ユキボウ | 氷 | Mixed-Special | 氷のちから (ice_power) | 優先 | 2 | 8 | 19/22 | wide_burst | 手順6 抽選 2 番目 |
| 138B | ノロウゾー | 地/虚 | Mixed-Special | ハードブロック (hard_block) | 一般 | 2 | 9 | 21/24 | — | 一般・補充に当たらず |
| 146 | ゼノルーダ | 竜 | Mixed-Special | 竜のちから (dragon_power) | 優先 | 0 | 6 | 5/20 | shimikomi | 手順7 の抽選 8 番目 → しみこみ |
| 154 | キンガゼル | 地 | Mixed-Physical | 地のおしえ (ground_oshie) | 優先 | 0 | 2 | 19/20 | kowaza_jouzu | 手順7 の抽選 3 番目 → こわざ |
| 161 | オーマサンダ | 電 | Mixed-Physical | 雷式 (electric_weakness) | 優先 | 2 | 6 | 17/20 | kowaza_jouzu | 手順7 の抽選 5 番目 → こわざ |
| 169B | トノサマンボ | 水/電 | Mixed-Special | 水のおしえ (water_oshie) | 優先 | 0 | 10 | 24/25 | **entry_puddle** | 手順4 水の優先候補 {169B, 188B} から抽選 |
| 171 | レイバーン | 竜/闇 | Mixed-Physical | じゆうのつばさ (jiyuu_no_tsubasa) | 一般 | 2 | 8 | 14/21 | — | 一般・補充に当たらず |
| 178 | メドゥーナ | 闇 | Mixed-Both | 闇のちから (dark_power) | 優先 | 1 | 7 | 20/23 | kowaza_jouzu | 手順7 の抽選 4 番目 → こわざ |
| 188 | ホルス | 炎 | Mixed-Special | 炎のちから (fire_power) | 優先 | 0 | 3 | 18/20 | shimikomi | 手順7 の抽選 6 番目 → しみこみ |
| 188B | イシス | 水 | Mixed-Both | 水のちから (water_power) | 優先 | 1 | 6 | 16/19 | shimikomi | 手順7 の抽選 7 番目 → しみこみ（手順6 の抽選では 9 番目で、ワイドバーストに入らず） |
| 196 | ゼノドラン | 虚/竜 | Mixed-Both | たかねのはな (takane_no_hana) | 一般 | 0 | 5 | 15/23 | — | 一般・補充に当たらず |
| 197 | レジェンディア | 無 | Mixed-Physical | たかねのはな (takane_no_hana) | 一般 | 1 | 6 | 5/20 | — | 一般・補充に当たらず |

件数: wide_burst 4・kowaza_jouzu 5・shimikomi 4・entry_puddle 1・entry_flowerbed 1・entry_thin_ice 1（計 16）。`trait2` なし 22（除外 2・一般 20）。
区分: 優先 15（全員 `trait2` あり）・除外 2（なし）・一般 21（100 だけ `trait2` あり）。部屋技持ち 18（うち優先 9）。

乱数を引いた順の途中経過（実装の照合用。付録A-2 を実行して同じになること）:

| 手順 | 呼び出し | 対象（基準の順） | 結果 |
|---|---|---|---|
| 4 水 | `choice` | 169B, 188B | 169B |
| 4 草 | `choice` | 084, 108 | 084 |
| 4 氷 | `choice` | 101, 133 | 101 |
| 5 | `choice` | wide_burst, kowaza_jouzu, shimikomi | kowaza_jouzu（5 体） |
| 6 優先の部屋技持ち | `shuffle` | 083B, 084B, 096, 108, 127, 133, 161, 178, 188B | 084B, 133, 127, 108, 083B, 178, 161, 096, 188B（先頭 4 → wide_burst） |
| 6 一般の部屋技持ち | `shuffle` | 016, 030, 041B, 094B, 112B, 121, 138B, 171, 197 | 138B, 171, 112B, 041B, 121, 094B, 030, 197, 016（使わない） |
| 7 一般（補充） | `shuffle` | 一般 21 体 | 100, 023, 094B, 064B, 196, 121, 138B, 073B, 112B, 030, 048B, 073, 007, 042, 022, 171, 007B, 197, 016, 100B, 041B（先頭 1 → 100） |
| 7 配る | `shuffle` | 083B, 096, 100, 146, 154, 161, 178, 188, 188B | 083B, 096, 154, 178, 161, 188, 188B, 146, 100（先頭 5 → kowaza_jouzu、残り 4 → shimikomi） |

役割別の内訳:

| 役割 | 体数 | wide_burst | kowaza_jouzu | shimikomi | 地形 | なし |
|---|---|---|---|---|---|---|
| Mixed-Physical（両刀/物理耐久） | 13 | 1（108） | 3（083B・154・161） | 1（100） | 0 | 8（021B・030・036・041B・073B・094B・171・197） |
| Mixed-Special（両刀/特殊耐久） | 14 | 1（133） | 1（096） | 2（146・188） | 2（084 はなばたけ・169B みずたまり） | 8（007・016・022・042・048B・064B・100B・138B） |
| Mixed-Both（両刀/両面耐久） | 11 | 2（084B・127） | 1（178） | 1（188B） | 1（101 うすらひ） | 6（007B・023・073・112B・121・196） |

役割と特性の組み合わせが偏る（各特性 4〜5 体しかいない）ので、§32 では役割別の結果を特性の効果として読まないこと。

### A-2. 参照実装（`Tools/assign_mixed_traits.py` の `assign` はこれと同一にする）

改稿時にこのコードを `Data/` を読むだけで実行し、出力が A-1 の表（trait2 列）・途中経過と一致することを確認した。

```python
import random

SEED = 20261028
RANDOM3 = ['wide_burst', 'kowaza_jouzu', 'shimikomi']
TERRAIN = [('Water', 'entry_puddle'), ('Grass', 'entry_flowerbed'), ('Ice', 'entry_thin_ice')]
PRIORITY_KEYS = ['きずな', 'ちから', 'おしえ', 'まもり', '式']
EXCLUDE_KEY = 'リーダー'
BASE_COUNT, EXTRA_COUNT = 4, 5


def classify(s, traits_by_id):
    name = traits_by_id[s['trait']]['name']
    if EXCLUDE_KEY in name:
        return 'excluded'
    if any(k in name for k in PRIORITY_KEYS):
        return 'priority'
    return 'other'


def room_attack_count(s, moves_by_id):
    ids = {e['move_id'] for e in s['learnset']}
    return sum(1 for i in ids
               if moves_by_id[i]['range'] == 'Room' and moves_by_id[i]['power'] > 0)


def assign(species, traits_by_id, moves_by_id):
    """species_id -> trait2 の dict を返す（trait2 を持たない個体は含めない）。"""
    mixed = sorted((s for s in species if s.get('offense') == 'Mixed'),
                   key=lambda s: s['species_id'])
    if len(mixed) != 38:
        raise SystemExit(f'両刀攻撃が {len(mixed)} 種（38 種を前提にしている）')
    cls = {s['species_id']: classify(s, traits_by_id) for s in mixed}
    room = {s['species_id']: room_attack_count(s, moves_by_id) for s in mixed}
    rng = random.Random(SEED)
    out = {}

    def free(kind):
        return [s for s in mixed if cls[s['species_id']] == kind and s['species_id'] not in out]

    # 手順4: 地形特性（優先候補から。いなければ優先外から）
    for elem, tid in TERRAIN:
        cands = [s for s in free('priority') if elem in s['types']]
        if not cands:
            cands = [s for s in free('other') if elem in s['types']]
        if not cands:
            raise SystemExit(f'{elem}: 地形特性の候補がいない')
        out[rng.choice(cands)['species_id']] = tid

    # 手順5: 5 体になる特性
    five = rng.choice(RANDOM3)
    counts = {t: (EXTRA_COUNT if t == five else BASE_COUNT) for t in RANDOM3}

    # 手順6: ワイドバースト（部屋技を覚える個体。優先候補 → 優先外）
    p = [s for s in free('priority') if room[s['species_id']] > 0]
    rng.shuffle(p)
    o = [s for s in free('other') if room[s['species_id']] > 0]
    rng.shuffle(o)
    wb = (p + o)[:counts['wide_burst']]
    if len(wb) < counts['wide_burst']:
        raise SystemExit('ワイドバーストの候補が足りない')
    for s in wb:
        out[s['species_id']] = 'wide_burst'

    # 手順7: こわざじょうず・しみこみ（優先候補 → 足りなければ優先外から補う）
    need = counts['kowaza_jouzu'] + counts['shimikomi']
    r = free('priority')
    if len(r) < need:
        o = free('other')
        rng.shuffle(o)
        r = r + o[:need - len(r)]
    if len(r) < need:
        raise SystemExit('こわざじょうず・しみこみの候補が足りない')
    r.sort(key=lambda s: s['species_id'])
    rng.shuffle(r)
    r = r[:need]
    for i, s in enumerate(r):
        out[s['species_id']] = 'kowaza_jouzu' if i < counts['kowaza_jouzu'] else 'shimikomi'
    return out
```

呼び出し側（`__main__`）は `species = json.load(open('Data/species.json', encoding='utf-8'))`、`traits_by_id = {t['id']: t for t in json.load(open('Data/traits.json', encoding='utf-8'))}`、
`moves_by_id = {m['id']: m for m in json.load(open('Data/moves.json', encoding='utf-8'))}` で読み、§3-4 の手順 1・8・9 を行う。
`assign` は `trait2` を見ないので、手順1（既存の `trait2` を消す）の前後どちらで呼んでも結果は同じ。traits.json に新特性 6 件を足したあとでも結果は変わらない（新特性はどの個体の `trait` でもないため）。
