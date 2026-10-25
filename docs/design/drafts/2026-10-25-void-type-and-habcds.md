# 虚(うろ)属性・虚脱状態・HABCDS・制限技 — 仕様書草案

- 作成: 2026-10-25（草案。末尾「ユーザーに確認すべき点」が決まるまで実装しない）
- 対象ブランチ: `claude/mystery-dungeon-roguelike-egnzjt`
- 読み手: 会話履歴を持たない実装者。**この文書だけで実装できるように書いてある。**
  書かれていないことは勝手に決めず、報告すること。

---

## 0. 前提（この文書を読むのに必要な事実）

### 0-1. ユーザー要求（原文）

```
* 炎属性に対し相性不利を追加
* 無属性に対し相性有利を追加
* この中間に「虚(うろ)」属性を追加
   * 虚属性は現在の闇属性の約40%、次に個体数が多い属性から2体を割り当て
   * 炎属性の攻撃技は虚属性が耐性を持つ。
   * 闇属性の攻撃技は虚属性が耐性を持つ。
   * 虚属性の攻撃技は無属性が耐性を持つ。
   * 虚属性は氷属性が弱点
   * 虚属性は無属性が弱点
* 虚脱状態:自身の一致属性の補正倍率がかからない。2回目の行動終了時に解除
* パルのステータスをHP-攻撃-防御-魔力-精神-俊敏に割り当て、今後このステータスの略称をHABCDSと呼称する。
  https://palworld-lab.com/pals/ 魔力、精神、俊敏は割り当てるべきステータスが存在していないため、
  新規に参考となるステータスを考える。そこで、作業適正をひとつの傾向ととらえた。
   * 「運搬」が高いほど俊敏も高くなる
   * パートナースキルが「ライド」の個体も俊敏が高くなる
   * ペア強化パルなし状態でランク4のベイントールの走行を参照して俊敏を8/10としたとき、
     ペア強化条件を最大に満たしたランク4のライトロットが10/10で最速となる。
     すなわちペア強化パルなし飛行ライドのゼノドランも同様に10/10評価である。
     なお水上ライドではネプティオスが最も速い。
* 現在、物理型/特殊型 としているものについては、物理耐久型/物理攻撃型/特殊耐久型/特殊攻撃型に分類する。
  打ち分け型に関しては耐久配分をH*0.8=B+Dとなるバランスとし、BおよびDのバランスは偏っても
  35:65~65:35までとする。なおACの配分は40:60～60:40に収めるものとする
* 相手に対する制限を行う技のバリエーションをもっと増やすがアイデアがまだないため考えたい。
  少なくとも10種類を考え、それぞれの技を1~2体だけが習得できる技としたい。
```

### 0-2. 用語

| 用語 | 意味 |
|---|---|
| H/A/B/C/D/S | HP / 攻撃 / 防御 / 魔力 / 精神 / 俊敏。**HABCDS** と総称する |
| 魔力(C) | 特殊技の攻撃側に使う能力（本草案の解釈。§4-5） |
| 精神(D) | 特殊技の防御側に使う能力（同上） |
| 俊敏(S) | 行動順に使う能力（§4-6、確認事項 Q5） |
| 旧HAD | 現行の `base_hp` / `base_atk` / `base_def` の3能力。本草案では小文字 h/a/d と書く |
| BST3 | 旧HADの合計 h+a+d（現行の「合計種族値」） |
| BST6 | HABCDS の合計 H+A+B+C+D+S（新しい「合計種族値」） |
| 型(profile) | `Tools/learnset_rules.py` の `profile_of()` が返す Physical / Special / Versatile（物理型/特殊型/打ち分け型） |
| 役割(role) | 本草案で新設。物理耐久/物理攻撃/特殊耐久/特殊攻撃/打ち分け の5種 |
| 行動 | 対戦では、パルが自分のターンに実際に行動した1回（攻撃・変化技・移動）。迷宮では、その個体の1回の action-cycle |
| 一致補正(STAB) | 技の属性が使用者の属性と一致するときの ×1.2（〇〇派特性なら ×1.5） |

### 0-3. 現行の数値（2026-10-25 時点、実測）

- 種族 287 / 技 582（Physical 310・Special 214・Status 58）/ 特性 121
- 属性 9種（`Neutral, Fire, Water, Grass, Electric, Ground, Ice, Dragon, Dark`）。
  個体数（二重属性は両方に数える）: 闇74, 水47, 草46, 地44, 炎43, 氷38, 無36, 電25, 竜21。
  闇の内訳は単属性38・二重属性36。
- 型の内訳: 物理型124（周期105＋特性19）、特殊型84（周期79＋特性5）、打ち分け型79。
- 旧HAD: h 60〜200（平均95.3）、a 50〜150（96.7）、d 50〜180（93.1）、BST3 180〜500（285.2）。
- 回帰スイート `godot --headless --path . Scenes/BattleTestScene.tscn` は
  `[検証]` 行が **112** 行（`OK` を含む 109 行＋期待値併記の情報行 3 行。NG 0）。
- Python 検証: `Tools/verify_learnset.py` 16 PASS、`Tools/verify_attack_moves.py` 23 PASS、
  `Tools/verify_npc_teams.py` 10 PASS（いずれも ALL PASS）。
- 直近の 10,000 試合ランダム構築（呼び出し元の集計）: **炎が勝率55.7%・BST補正後+5.1pt で最強、
  無が41.6%・補正後−6.6pt で最弱**。※この集計を再現するスクリプトはリポジトリに無い（Phase 0 で作る）。

### 0-4. palworld-lab.com は取得できない → paldb.cc を代替参照元にする

- `https://palworld-lab.com/pals/` は Cloudflare のチャレンジで **HTTP 403**（curl / WebFetch とも）。
- 代替として **`https://paldb.cc/ja/Pals`（一覧）と `https://paldb.cc/ja/<英名>`（個体ページ）** が
  取得できる（HTTP 200、プロキシ経由で可）。
  - 一覧の各カードの `data-filters` 属性に作業適性とLv（例 `"手作業1 運搬1 牧場1 無属性"`）、
    `class="itemname"` のリンクテキストに和名、`href` に英名がある。**287種の `display_name` は全て一覧に存在する。**
  - 個体ページに パートナースキル文・`RunSpeed`・`RideSprintSpeed`・`SwimDashSpeed`・
    パートナースキルのLv別効果表（`MoveSpeed_up_PartnerSkill_Ride_4` 等）がある。
  - **同一データ源である確認**: 個体ページの HP/攻撃/防御 は species.json の旧HADと
    **285/287 種で完全一致**（不一致はこのリポジトリで調整済みのリリクインと、HP/防御が入れ替わっている
    グランジーラのみ）。
- 実装時は取得結果を **`Data/source/paldb_speed.json` に保存してコミット**する（以後ネットワーク不要に）。

---

## 1. 目的（達成条件）

1. **属性相性**: 10番目の属性「虚(Void)」が追加され、相性表が 10×10 になっている。
   ユーザー指定の5マス（炎→虚0.5、闇→虚0.5、虚→無0.5、無→虚2.0、氷→虚2.0）が入り、
   既存 81 マスは変わらない（確認事項 Q1/Q2 で追加マスが決まればそれも）。
   その結果として **炎の攻撃面・防御面が下がり、無の攻撃面が上がる**（§5-1 の静的指標で期待値を示す）。
2. **虚の個体**: 30 種が虚を持つ（闇から28種・水から2種）。虚の個体は虚の攻撃技を覚え、
   闇（水）の攻撃技は覚えていない。
3. **虚脱**: 新しい状態異常「虚脱(Hollow)」が実装され、虚脱中は一致補正が 1.0 になり、
   **自分の2回目の行動終了時に解除**される。主な付与手段は虚技の蓄積。
4. **HABCDS**: 287 種すべてが H/A/B/C/D/S の6能力を持ち、物理技は A 対 B、特殊技は C 対 D で
   ダメージ計算される。S は行動順に効く（Q5）。合計種族値は6能力合計(BST6)。
5. **役割**: 型（物理/特殊/打ち分け）が役割5種に再分類され、打ち分け型は
   B+D = 0.8H・B:D ∈ [35:65, 65:35]・A:C ∈ [40:60, 60:40] を満たす。
6. **制限技**: 相手を制限する技が **12種**（10種以上）追加され、各技を習得できるのは 1〜2 種だけ。
7. 回帰スイートが全フェーズで NG 0、項目数が §7 の各フェーズの値になる。
   Python 検証3本が ALL PASS。Artifact 2つが同じ URL で更新されている。

---

## 2. 現状（関係するコード）

### 2-1. 属性

| 場所 | 内容 |
|---|---|
| `Data/type_chart.json` | `types`（9要素）と `matrix`（行=攻撃側、列=防御側）。値は {0.5, 1.0, 2.0} のみ |
| `Scripts/Combat/Element.cs` `enum Element` | 9要素。**名前と順序が type_chart.json の `types` と完全一致**していることが前提 |
| `Scripts/Combat/TypeChartManager.cs` `GetMultiplier(attackType, params defenderTypes)` | 文字列で引く。防御側の2属性は積 |
| `Scripts/Combat/ElementAffinity.cs` `Weaknesses/Complements` | 相性表から弱点・補完属性を導く（C# からの呼び出しは現状なし。learnset 生成の概念） |
| `Scripts/Species/SpeciesDatabase.cs` `Validate()` | Element の全値が type_chart にあるかを検査 |
| `Scripts/Entities/StatusEffectManager.cs` `ElementalAilment(moveType)` | 属性→蓄積状態異常（炎=Burn, 水=Soaked, 雷=Paralyze, 地=MudCaked, 草=VineBound, 氷=Freeze, 闇=Darkness, 無・竜=なし） |
| `Scripts/UI/Battle/BattleTheme.cs` `Element(string)` / `ElementLabel(string)` | 属性色・和名1字 |
| `Scripts/UI/Battle/SpeciesPickScreen.cs` 25行 | 属性フィルタの並び（9要素の配列） |
| `Scripts/Battle/TacticalBuilder.cs` 61–63行 `Elements` / `_mult` | 9×9 の相性キャッシュ |
| `Scripts/Entities/EntityStats.cs` `GetMovementProfile()` | 炎=溶岩、水・氷=水地形。虚は該当なし |
| `Tools/build_ls_payload.py` `EL` / `Tools/artifacts/*.html` | 和名マップ（`'Dark': '闇'` 等） |
| `Tools/verify_learnset.py` #4 | 補完属性のサンプル手計算（9属性ぶんの期待値をべた書き） |
| `Tools/verify_attack_moves.py` | 属性ごとの技数規則（スラスト「無2・他1」など） |
| `Tools/assign_weather_traits.py` | 属性→天候特性（闇=haze/purple_haze） |

現行の相性表（行=攻撃、列=防御。空欄=1.0）:

| 攻\防 | 無 | 炎 | 水 | 草 | 電 | 地 | 氷 | 竜 | 闇 |
|---|---|---|---|---|---|---|---|---|---|
| 無 | | | | | | | | | 0.5 |
| 炎 | | | 0.5 | 2 | | | 2 | | |
| 水 | | 2 | | | 0.5 | | | | |
| 草 | | 0.5 | | | | 2 | | | |
| 電 | | | 2 | | | 0.5 | | | |
| 地 | | | | 0.5 | 2 | | | | |
| 氷 | | 0.5 | | | | | | 2 | |
| 竜 | | | | | | | 0.5 | | 2 |
| 闇 | 2 | | | | | | | 0.5 | |

**構造上の偏り**: 抜群を2つ持つのは炎（草・氷）だけ、抜群を1つも持たないのは無だけ。
炎は被弾面でも水にしか弱くなく、草・氷を半減する。これが「炎最強・無最弱」の静的な原因。

### 2-2. ダメージ計算と一致補正

- `Scripts/Combat/DamageCalculator.cs` `Calculate(DamageContext)`:
  `effAtk²/(effAtk+effDef) × effPower/100 × 型相性 × STAB × 急所 × 竜倍率 × …`、最後に1回だけ切り捨て、最低1。
- `Scripts/Turn/AttackAction.cs` `StrikeTarget` 相当の処理（約 374–830 行）:
  - 448–454行: `stabApplies` と `stabMultiplier`（1.2、〇〇派なら1.5）。**一致補正の唯一の算出箇所。**
  - 798–799行: `BaseAtk = _attacker.Stats.Attack`, `BaseDef = defenderStats.Defense`。
    **物理/特殊の区別なく同じ攻撃・防御を読んでいる**（`MoveCategory` は持ち物・特性の条件にしか使われない）。
  - 597行 `Stats.BaseAtk*0.08`（〇〇のきずな）、615行 `Stats.BaseDef*0.10`（〇〇のまもり）、
    734/736/750行 `SpeciesPercent(Stats.BaseAtk/BaseDef, …)`（リーダー系）: 種族値%の平坦加算。
  - 630–631行: ハードブロック（特殊被弾時に防御2倍）/ハードアーマー（物理被弾時）。
  - 786–792行: パワーレンズ等 4 種（分類別の威力補正）。
- `Scripts/Entities/EntityStats.cs`: `BaseMaxHp/BaseAtk/BaseDef`（種族値）と
  `MaxHp/Attack/Defense`（実数値。`Attack = floor((Base*2+Breakthrough)*Lv/100)+5`、
  `MaxHp = floor((Base*2+BT)*Lv/100)+Lv+10`）。`SpAttack/SpDefense` は **int の未使用フィールド**
  （Player/AllyEntity/BossEntity/DummyNPC/FastNPC が 4〜15 の値を代入しているが、どこからも読まれない）。
  `FillFromSpecies(SpeciesData)` が種族値を写す。
- `Scripts/Species/SpeciesData.cs` / `SpeciesDatabase.cs` `SpeciesJson`: `base_hp/base_atk/base_def`。
- `Scripts/Entities/StatusEffectManager.cs`: 攻撃ランク・防御ランクは1本ずつ（`GetAtkMultiplier/GetDefMultiplier`）。

### 2-3. 状態異常

- `Scripts/Combat/MoveData.cs` `enum AilmentType { None, Poison, Toxic, Burn, Paralyze, Freeze, Stun, Soaked, MudCaked, VineBound, Darkness }`。
  Stun 以外は**1スロット排他**（`StatusEffectManager.Ailment`）。
- 蓄積: `StatusEffectManager.AccumulateOnHit(moveType, declaredAilment, chance)`。属性の技が当たるたび
  その属性の蓄積に +25、技が同じ状態異常を宣言していれば +chance×10。1000 で発症し全蓄積をリセット。
- 持続の刻み: `StatusEffectManager.AdvanceTurn(maxHp)`（`Entity.ResolveStatusTick()` から）。
  **対戦ではサイクル終了時（`BattleScheduler.EndCycle`）に全員一斉**、迷宮では各個体の action-cycle ごと
  （`Scripts/Turn/TurnScheduler.cs` 100行付近）。例: MudCaked は「その下で2回目のサイクル終了時に解除」。
- 「自分のターン終了時」の処理は `BattleScheduler.ResolveTurnEndItems(Entity)`
  （`ResolveTurn` の中で、行動が実行された場合だけ呼ばれる——凍結/気絶で行動できなかった手番では呼ばれない）。
- 対戦 HUD（`Scripts/UI/Battle/BattleHud.cs`）は**状態異常を一切表示していない**。表示はログ（`MessageLogger.Log`）のみ。

### 2-4. 行動順と合計種族値

- `Scripts/Battle/BattleScheduler.cs` `Order(a, b)`: 優先度（`EffectivePriority`）高い順 →
  `Bst()` 低い順 → 乱数。`Bst(Entity) = BaseMaxHp + BaseAtk + BaseDef`。
- BST3 を直接計算している箇所（**全て BST6 に置き換える対象**）:
  `BattleScheduler.Bst`、`BattlePal.Bst`、`BattleArena.ApplyEntryWeather`（合計種族値の高い陣営から天候発動）、
  `MetaScenario` 55行（`StrongPool` の並び）、`NpcOpponent` 84行（選出の採点）、
  `TacticalBuilder` 105–106行（`Profile.Hp/Atk/Def/Bst`）、`BattleMatchScene` 907–908行、
  UI: `DeployScreen` 247、`TeamBuildScreen` 161・214–217、`SpeciesPickScreen` 98・157・162–164、
  `SelectionScreen` 152、`BattleHud` 282・409。
  Python: `Tools/generate_npc_teams.py` `bst()`、`Data/npc_teams.json` の `total_bst`、
  `Tools/verify_learnset.py` `tot()`、`Tools/generate_learnset.py` `total_of()`、`Tools/build_ls_payload.py`。
- `Scripts/Battle/NpcOpponent.cs` の行動採点は `Power × 相性 × 命中` だけを見ており、能力値もSTABも見ない。
  **変化技（Power 0）は評価されず、NPC は一切使わない**（223行 `if (data.Power <= 0) continue;`）。

### 2-5. 型（profile）

- `Tools/learnset_rules.py` `profile_of(trait_id, species_index)`: 特性指定（`TRAIT_PHYSICAL`/`TRAIT_SPECIAL`）が
  優先、無ければ `species_id` 昇順の序数で `PROFILE_CYCLE`（物4:特3:打3）。**C# 側には型の概念が無い。**

---

## 3. 変更内容

フェーズ順に書く（§7 に完了条件）。ファイル名は repo 相対。

### Phase 0 — 計測の土台（コード変更は計測のみ）

**目的**: 「炎55.7% / 無41.6%」を再現でき、変更前後を同じ物差しで比べられるようにする。

1. `Scripts/Battle/BattleMatchScene.cs` `RunRandomBatch()`:
   `--out <path>` が与えられたら、1試合1行の CSV を追記する。列（ヘッダ行なし）:
   `match,outcome,home_sel,away_sel,home_bst,away_bst,cycles`
   - `outcome`: `H`（自陣勝ち）/`A`/`D`（引き分け）/`U`（未決着）
   - `home_sel` / `away_sel`: **盤面に立った選出4匹**の species_id を `|` 区切り
   - `home_bst` / `away_bst`: 選出4匹の合計種族値（そのフェーズで有効な定義。Phase 4 以降は BST6）
   - 既存の標準出力は変えない。`--shard` は不要（別プロセスで別 `--out` に出して後で連結する）。
2. 新規 `Tools/analyze_random_by_element.py <csv>...`:
   - 属性 X について「選出に X を持つ個体（二重属性は両方に計上）を含む側」の勝率を、
     **個体の出場1回ごとに**数える（同じ側に X が2体いれば2回）。U・D は分母から除く。
   - BST補正: 全試合で `P(自陣勝ち) = 1/(1+exp(-k·(home_bst−away_bst)))` を最尤で当て、
     属性ごとに「実勝率 − 期待勝率（その出場の試合の期待値の平均）」をポイント差で出す。
   - 出力: 属性ごとに 出場数・勝率・期待勝率・補正後差(pt)。
3. 変更前のベースライン: `godot --headless --path . Scenes/BattleMatchScene.tscn -- --random --matches 10000 --out /tmp/base.csv`
   （複数プロセスに分けてよい）。結果を `docs/design/pvp_spec.md` §28 の表に書く。
   **炎が最高・無が最低であること**を確認（呼び出し元の 55.7% / 41.6% と数字が厳密に一致する必要はない。
   集計方法が違う可能性があるため。差が ±3pt を超えたら報告）。

### Phase 1 — 虚属性（Void）

#### 1-a. `Data/type_chart.json`

`types` の**末尾**に `"Void"` を追加（既存の添字を動かさない）。`matrix` を 10×10 にする:

| 攻\防 | 無 | 炎 | 水 | 草 | 電 | 地 | 氷 | 竜 | 闇 | **虚** |
|---|---|---|---|---|---|---|---|---|---|---|
| 無 | | | | | | | | | 0.5 | **2** |
| 炎 | | | 0.5 | 2 | | | 2 | | | **0.5** |
| 水 | | 2 | | | 0.5 | | | | | |
| 草 | | 0.5 | | | | 2 | | | | |
| 電 | | | 2 | | | 0.5 | | | | |
| 地 | | | | 0.5 | 2 | | | | | |
| 氷 | | 0.5 | | | | | | 2 | | **2** |
| 竜 | | | | | | | 0.5 | | 2 | |
| 闇 | 2 | | | | | | | 0.5 | | **0.5** |
| **虚** | **0.5** | **2** ※Q1 | | | | | | | | |

JSON そのもの（Q1 で「虚→炎 2.0」を採った場合。採らない場合は最終行の 2 番目を 1.0）:

```json
{
  "types": ["Neutral", "Fire", "Water", "Grass", "Electric", "Ground", "Ice", "Dragon", "Dark", "Void"],
  "matrix": [
    [1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 0.5, 2.0],
    [1.0, 1.0, 0.5, 2.0, 1.0, 1.0, 2.0, 1.0, 1.0, 0.5],
    [1.0, 2.0, 1.0, 1.0, 0.5, 1.0, 1.0, 1.0, 1.0, 1.0],
    [1.0, 0.5, 1.0, 1.0, 1.0, 2.0, 1.0, 1.0, 1.0, 1.0],
    [1.0, 1.0, 2.0, 1.0, 1.0, 0.5, 1.0, 1.0, 1.0, 1.0],
    [1.0, 1.0, 1.0, 0.5, 2.0, 1.0, 1.0, 1.0, 1.0, 1.0],
    [1.0, 0.5, 1.0, 1.0, 1.0, 1.0, 1.0, 2.0, 1.0, 2.0],
    [1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 0.5, 1.0, 2.0, 1.0],
    [2.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 0.5, 1.0, 0.5],
    [0.5, 2.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 1.0]
  ]
}
```

#### 1-b. C#

- `Scripts/Combat/Element.cs`: `Dark,` の後に `Void,` を追加（**末尾**。コメントの「9-element」を「10-element」に）。
- `Scripts/Entities/StatusEffectManager.cs` `ElementalAilment`: Phase 2 で `"Void" => AilmentType.Hollow` を足す（Phase 1 では足さない）。
- `Scripts/UI/Battle/BattleTheme.cs`: `"Void" => new Color("6b7a8f")`、`ElementLabel` に `"Void" => "虚"`。
- `Scripts/UI/Battle/SpeciesPickScreen.cs` 25行の配列: `"Dark"` の後に `"Void"`。
- `Scripts/Battle/TacticalBuilder.cs` 61–63行: `Elements` に `Element.Void` を追加し、`_mult` のコメント「9x9」を「10x10」に
  （配列サイズを `Elements.Length` から作っているならそれで済む。べた書きなら直す）。
- その他 `"Dark"` を列挙している箇所は `grep -rn '"Dark"\|Element.Dark' Scripts` で洗い、
  **属性の全列挙**であれば Void を足す。特定の闇の効果（闇のちから等）は触らない。

#### 1-c. 虚の個体（`Data/species.json` の `types`）

**30種**を虚にする。選定規則（再現可能にするため規則として書く。下表はこの規則の出力）:

1. 候補は **闇を持ち、伝説でなく、闇に紐づく特性を持たない**個体。
   「闇に紐づく特性」= `traits.json` で `element == "Dark"` のテンプレート特性（dark_bond/power/guard/resist/stab/weakness/oshie）、
   および `akui`（闇技被弾で回復）、`purple_haze`・`haze`（きりを張る。`assign_weather_traits.py` で闇に割当）、
   `jiyuu_no_tsubasa`（竜・闇の味方を数える）、`akumu_no_hitomi`（闇技ナイトメアボール/パルスを名指し）。
2. 回帰スイートの試合で使う8種（モコロン, クルリス, タマモ, ブシガエル, カモノスケ, ミルフィー, ルミカイト, タマコッコ）は除く。
3. **単属性の闇 → 単属性の虚**を15種: 名前に「ゼノ」を含む候補を必ず入れ、残りは候補を (BST3, species_id) 昇順に並べて
   等間隔抽出（i 番目 = 並びの `int((i+0.5)*n/k)` 番目）。
4. **二重属性の闇 → 闇の側を虚に**: 相方の属性ごとに、その相方を持つ闇複属性**全体**の個体数×0.4 を四捨五入した数だけ
   （候補数が上限）。「ゼノ」は必ず入れ、残りは同じ等間隔抽出。合計13種（炎/闇を虚に寄せすぎないための層化）。
5. **単属性の水 → 単属性の虚**を2種: 水単属性で、水のテンプレート特性を持たず、伝説でなく、2. の8種でない候補から等間隔抽出。

| species_id | 名前 | 旧 | 新 | BST3 | 特性 |
|---|---|---|---|---|---|
| 016 | ンダコアラ | 闇 | 虚 | 210 | dokusen_body |
| 022 | ネムラム | 闇 | 虚 | 205 | chaser |
| 027 | ニャオテト | 闇 | 虚 | 200 | keikaikan |
| 034B | ベノゴート | 闇 | 虚 | 265 | dokusen_body |
| 056 | ツキカゲ | 闇 | 虚 | 260 | eisou |
| 057 | マスクロウ | 闇 | 虚 | 250 | dokusen_body |
| 073 | ダリザード | 闇 | 虚 | 210 | koufun |
| 091B | アビスゴート | 闇 | 虚 | 285 | tsume_no_kariudo |
| 111B | カゲヌイ | 闇 | 虚 | 320 | body_press |
| 130 | ナイトロット | 闇 | 虚 | 310 | body_press |
| 145 | ゼノベーダ | 闇 | 虚 | 300 | free_fall |
| 150 | オーメン | 闇 | 虚 | 300 | hard_armor |
| 157B | コスモディア | 闇 | 虚 | 335 | konton_no_shukufuku |
| 166 | フードール | 闇 | 虚 | 235 | kyoujin_na_karada |
| 189 | ゼノグリフ | 闇 | 虚 | 380 | takane_no_hana |
| 030 | ヤミイカ | 闇/水 | 虚/水 | 190 | dokusen_body |
| 041 | アズレーン | 水/闇 | 水/虚 | 310 | amagumo |
| 041B | アズルーナ | 氷/闇 | 氷/虚 | 325 | tamayuki |
| 064 | カバネドリ | 炎/闇 | 炎/虚 | 295 | moeru_kobushi |
| 078B | フォレーオ | 炎/闇 | 炎/虚 | 285 | foehn |
| 079B | クレメーナ | 闇/炎 | 虚/炎 | 290 | kagerou_body |
| 084 | アロアリュー | 草/闇 | 草/虚 | 285 | grass_power |
| 084B | ビリビリュー | 電/闇 | 電/虚 | 290 | electric_power |
| 099 | デスティング | 闇/地 | 虚/地 | 330 | kyoujin_na_karada |
| 115 | マジョルナ | 闇/炎 | 虚/炎 | 315 | bakusen |
| 138B | ノロウゾー | 地/闇 | 地/虚 | 345 | hard_block |
| 147 | プラムルリム | 草/闇 | 草/虚 | 300 | free_fall |
| 196 | ゼノドラン | 闇/竜 | 虚/竜 | 370 | takane_no_hana |
| 047 | リオリネ | 水 | 虚 | 200 | keikaikan |
| 055 | シャーキッド | 水 | 虚 | 255 | okorinbo |

変更後の個体数: 草46, **闇46**, **水45**, 地44, 炎43, 氷38, 無36, **虚30**, 電25, 竜21。
二重属性の Types の**並び順は保つ**（`["Dark","Water"]` → `["Void","Water"]`）。
**特性は変えない**（上の規則で虚と矛盾する特性を候補から外してある）。

#### 1-d. 虚の技（`Data/moves.json`）

- **闇の攻撃技（power > 0）57種を1対1で複製**して虚の技を作る。
  - `id`: `void_` + 元の id（例 `MV_123` → `void_MV_123`）。
  - `type`: `"Void"`。`category/power/accuracy/max_pp/range/is_contact/weapon_tag/multi_hit*/rank_effect*/
    crit_rank_bonus/recoil/drain/guaranteed_*/priority` は元と同一。
  - `ailment_effect`: 元が `"Darkness"` なら **`"Hollow"`**（Phase 2 で enum が増えるまでは Phase 1 では `Darkness` のまま複製し、
    Phase 2 で `Hollow` に書き換える）。`Poison`/`Toxic` はそのまま。
  - `name`: 元の名前の先頭の語を置換表で置き換える。置換表（先に長いものから当てる）:
    `ダークネス→ホロウネス, ダーク→ホロウ, シャドウ→ヴォイド, ブラック→ブランク, カース→エンプティ,
    アビス→ウロ, ナイトメア→ネヴァー, ハデス→ヌル, ダスク→ヌル`。
    置換後に既存技名と重複したら末尾に「・ウロ」を付ける。`Tools/verify_attack_moves.py` の「技名が一意」で確認。
- 闇の変化技8種（ポイズンフォグ等）は**複製しない**（虚の個体は闇の変化技をそのまま持ち続ける）。
- 生成は新規スクリプト `Tools/add_void_type.py` で行い（1-c の species 変更・1-e の learnset 差し替えも同じスクリプト）、
  **何度実行しても同じ結果になる**こと（既に void_ 技があれば作り直す）。

#### 1-e. 虚の個体の learnset 差し替え（`Data/species.json`）

- 闇→虚の28種: learnset 中の**闇の攻撃技**を、同じ level のまま対応する `void_` 技へ置き換える。
- 水→虚の2種: learnset 中の**水の攻撃技**を、虚の技のうち「同じ category・同じ range」で威力が最も近いもの
  （同差なら低い方、さらに同じなら id 昇順）へ置き換える。同じ個体の中で同じ虚技が2回選ばれたら、次に近いものを使う。
  水の変化技はそのまま残す。
- **他の個体の learnset は変えない**（learnset 生成 `Tools/generate_learnset.py` は再実行しない）。

#### 1-f. Python ツール

- `Tools/build_ls_payload.py` `EL`、`Tools/refresh_move_editor.py`（と生成する HTML 内の和名マップ）、
  `Tools/export_learnsets_csv.py`、`Tools/add_status_moves.py`、`Tools/assign_weather_traits.py` に `'Void': '虚'` を追加
  （属性の全列挙だけ。天候割当には虚を足さない）。
- `Tools/verify_learnset.py` #4 の期待値を新しい表から手計算した値に更新し、表示を「10属性」に:
  `Neutral→[Dragon], Fire→[Neutral, Electric, Ice], Water→[Ground], Grass→[Water, Void], Electric→[Grass],
  Ground→[Fire], Ice→[Water, Void], Dragon→[Fire], Dark→[Ice], Void→[Fire, Dark]`（Q1 採用時。
  不採用なら Fire→[Electric], Grass→[Water], Ice→[Water], Void→[Fire, Dark]）。比較はリスト全体で行う。
- `Tools/verify_attack_moves.py`: スラストの規則「10件(無2・他1)」を「11件(無2・他1、虚を含む)」に。
  「変化技58種が手つかず」はそのまま（Phase 1 は変化技を足さない）。その他の属性別規則は虚にもそのまま適用される
  （闇の複製なので闇と同じ分布で通るはず。通らなければ報告）。

### Phase 2 — 虚脱（Hollow）

#### 2-a. 定義

| 項目 | 決定 |
|---|---|
| 名前 | 虚脱 / `AilmentType.Hollow`（JSON の文字列も `"Hollow"`）。enum の**末尾**に追加 |
| 効果 | 虚脱中の個体が使う技は**一致補正 1.0**（通常の 1.2 も、〇〇派の 1.5 もかからない）。それ以外は何も変えない |
| スロット | **主状態異常スロット（排他）**に入る。毒・やけど等と同時にはかからない。気絶(Stun)とは共存する |
| 主な付与 | **虚属性の技の蓄積**（`ElementalAilment("Void") => AilmentType.Hollow`）。虚技が当たるたび +25、虚脱を宣言する虚技（1-d で `Darkness` から書き換えた13種）は +chance×10。1000で発症。既存の蓄積の仕組みそのまま |
| 確定付与 | 制限技 R12「ホロウタッチ」（Phase 6）。`ailment_effect: Hollow, ailment_chance: 100` |
| 解除 | **その個体自身の2回目の行動が終わった時点**。数え方は下記 |
| 迷宮 | 同じ定義で動く（一致補正の算出は共通コード） |

**「2回目の行動終了時」の数え方**:

- 虚脱になった時点で `_hollowActionsElapsed = 0`。
- その個体の行動が**実行されて終わる**たびに +1（攻撃・変化技・移動・待機。対戦の `BattleScheduler.ResolveTurn` で
  `action?.Execute(...)` の直後＝`ResolveTurnEndItems` と同じ位置）。2 に達したら解除。
- **虚脱にかかったのがその個体自身の行動中**（反動・接触反撃などで自分の手番中に付いた場合）は、その行動の終了を数えない
  （行動開始時点で虚脱でなかった行動は数えない）。
- **気絶でその手番を飛ばされた場合は数えない**（行動していないため。`ResolveTurnEndItems` が呼ばれない分岐と同じ）。
- 例（1サイクルに各個体1回行動）:
  - サイクル N で自分が**まだ動いていない**ときに虚脱 → N の自分の行動後に1、N+1 の行動後に2で解除。
  - サイクル N で自分が**もう動いた後**に虚脱 → N+1 の行動後に1、N+2 の行動後に2で解除。
- 迷宮: `Scripts/Turn/TurnScheduler.cs` の各 actor の行動実行後（`ResolveStatusTick` を呼ぶ直前、行動ロックで飛ばした場合は除く）、
  および `Scripts/Turn/TurnManager.cs` のプレイヤー行動実行後に同じ通知を呼ぶ。
- `StatusEffectManager.AdvanceTurn` の switch に Hollow の分岐は**作らない**（サイクル終了では刻まない）。

#### 2-b. 実装

- `Scripts/Combat/MoveData.cs`: `AilmentType` に `Hollow` を末尾追加。`MoveDatabase` が文字列→enum を `Enum.TryParse` で
  読んでいるなら追加作業なし（読んでいなければ対応して報告）。
- `Scripts/Entities/StatusEffectManager.cs`:
  - `public bool IsHollow => Ailment == AilmentType.Hollow;`
  - `private int _hollowActionsElapsed; private bool _hollowAtActionStart;`
  - `public void NotifyOwnActionStarting() => _hollowAtActionStart = IsHollow;`
  - `public bool NotifyOwnActionEnded()`: `_hollowAtActionStart && IsHollow` のときだけ +1、2 で `ClearAilment()` して true を返す。
  - `TryApplyAilment` で Hollow を付けたとき `_hollowActionsElapsed = 0`。`Reset()` でも 0。
  - `ElementalAilment` に `"Void" => AilmentType.Hollow`。
- `Scripts/Entities/Entity.cs`: `NotifyOwnActionStarting()` / `NotifyOwnActionEnded()` を公開し、
  解除時に `MessageLogger.Log($"{ActorName}の虚脱が解けた", MessageLogger.ProgressionColor)`。
- `Scripts/Battle/BattleScheduler.cs` `ResolveTurn`: 実行分岐で `Execute` の前に Starting、後（`ResolveTurnEndItems` の直前）に Ended。
- `Scripts/Turn/AttackAction.cs` 448–454行: `stabApplies` に `&& !_attacker.StatusEffects.IsHollow` を加える。
- ログ: 発症時 `"{名前}は虚脱した!(タイプ一致の補正が消える)"`。発症ログを出している既存の共通箇所があればそこに乗せる。
  無ければ `Add()` の発症分岐と `TryApplyAilment` 経由の確定付与の呼び出し側で出す。
- UI: 対戦 HUD には状態異常の表示が現状まったく無いので、**虚脱だけのために HUD 表示は足さない**（やらないこと参照）。
  `GD.Print("[Battle] ...")` の行動ログには発症・解除を出す。

### Phase 3 — HABCDS のデータ

#### 3-a. 新しい species.json のキー

| キー | 意味 | 既存との関係 |
|---|---|---|
| `base_hp` | H | 値は役割の式で決まる（打ち分け型は変わり得る。Q3） |
| `base_atk` | A（物理攻撃） | **意味が「攻撃(共通)」→「物理攻撃」に変わる** |
| `base_def` | B（物理防御） | 同上 |
| `base_spatk` | C（魔力） | 新規 |
| `base_spdef` | D（精神） | 新規 |
| `base_speed` | S（俊敏） | 新規 |
| `role` | `PhysicalWall` / `PhysicalAttacker` / `SpecialWall` / `SpecialAttacker` / `Mixed` | 新規 |
| `legacy_had` | `{"hp":h,"atk":a,"def":d}` 変換前の旧HAD | 新規。**learnset 系ツールの BST 閾値はこれを使う**（§4-7） |
| `speed_source` | `{"kind":"ground|fly|water|none","v_eff":数値,"transport":Lv,"run":数値,"s10":数値}` | 新規。S の根拠を残す |

#### 3-b. 役割の決め方

1. 型 = `Tools/learnset_rules.py` `profile_of(trait, index)`（index は species_id 昇順の序数。現行と同じ）。
2. 物理型 → `d >= a` なら **物理耐久型**、`d < a` なら **物理攻撃型**。特殊型も同様に **特殊耐久型 / 特殊攻撃型**。
   打ち分け型 → **打ち分け型**（Mixed）。
   - 結果の内訳: 物理耐久60・物理攻撃64・特殊耐久42・特殊攻撃42・打ち分け79。

#### 3-c. 変換式（`round` = 四捨五入、x.5 は切り上げ）

**物理型/特殊型**（H = h。主系統は旧値そのまま、副系統を削る）:

| 役割 | A | B | C | D | A:C |
|---|---|---|---|---|---|
| 物理攻撃型 | a | d | round(0.6a) | round(0.75d) | 62.5:37.5 |
| 物理耐久型 | a | d | round(0.4a) | round(0.95d) | 71:29 |
| 特殊攻撃型 | round(0.6a) | round(0.75d) | a | d | 37.5:62.5 |
| 特殊耐久型 | round(0.4a) | round(0.95d) | a | d | 29:71 |

- 主系統（物理なら A と B、特殊なら C と D）が旧 a, d と一致するので、**主系統どうしの殴り合いのダメージは現行と同じ**
  （DamageCalculator のベンチマーク「70/70/70 同士・威力35 で 13」が保たれる）。
- 攻撃型は副系統の攻撃が強く副系統の防御が薄い、耐久型はその逆（副系統の攻撃を捨てて両面の防御を持つ）。

**打ち分け型**（Q3 の推奨案 (II)「耐久予算の再配分」で書く。案 (I) は Q3 参照）:

- 物理寄り度 r = clamp(learnset の Physical 技数 / (Physical + Special 技数), 0.40, 0.60)。Status は数えない。分母0なら 0.5。
- 耐久予算 E = h + 1.85d（物理/特殊型の B+D は 1.75d〜1.95d、その中央 1.85d に揃える）。
- H = round(E / 1.8)、B+D の総量 G = 0.8E / 1.8（⇔ **B+D = 0.8H**）。
- B = round(G·r)、D = round(G) − B（**B:D = r : 1−r ∈ [40:60, 60:40] ⊂ [35:65, 65:35]**）。
- 攻撃予算 = 1.7a（物理/特殊型の A+C 1.4a〜1.6a よりやや多い。両刀の代わりに主力側は最大 1.02a に留まる）。
  A = round(1.7a·r)、C = round(1.7a) − A（**A:C = r : 1−r ∈ [40:60, 60:40]**）。
- 丸めで B+D と round(0.8H) が ±1 ずれることは許す（検証も ±1 で見る）。

**全役割共通**: 各値の下限は 10。S は 3-d。

計算例（案 (II)。H/A/B/C/D/S）:

| id | 名前 | 旧 h/a/d | 役割 | HABCDS | BST6 |
|---|---|---|---|---|---|
| 001 | モコロン | 70/70/70 | 物理耐久 | 70/70/70/28/67/63 | 368 |
| 003 | タマコッコ | 60/60/60 | 物理耐久 | 60/60/60/24/57/57 | 318 |
| 005 | カモノスケ | 60/80/60 | 特殊攻撃 | 60/48/45/80/60/59 | 352 |
| 007 | ルミカイト | 80/70/80 | 打ち分け | 127/48/41/71/60/69 | 416 |
| 008 | ミルフィー | 70/70/75 | 打ち分け | 116/48/37/71/56/54 | 382 |
| 009 | ブシガエル | 80/100/85 | 物理攻撃 | 80/100/85/60/64/59 | 448 |
| 130B | ライトロット | 110/105/100 | 特殊攻撃 | 110/63/75/105/100/140 | 593 |
| 196 | ゼノドラン | 130/120/120 | 打ち分け | 196/122/94/82/62/140 | 696 |
| 199 | ベイントール | 130/145/120 | 物理攻撃 | 130/145/120/87/90/120 | 692 |
| 201 | ネプティオス | 105/145/125 | 特殊攻撃 | 105/87/94/145/125/140 | 696 |
| 203 | グランジーラ | 200/120/180 | 特殊耐久 | 200/48/171/120/180/140 | 859 |

BST6 は 318〜859（平均 500.3）。役割別の平均: 物理耐久 483 / 物理攻撃 500 / 特殊耐久 499 / 特殊攻撃 510 / 打ち分け 510。

#### 3-d. 俊敏 S の導出

paldb.cc から取る値（個体ページ。正規化: タグを ` | ` に置換し空白・区切りを畳んでから正規表現で拾う）:

| 値 | 取り方 |
|---|---|
| ライド種別 | パートナースキル文（「パートナースキル」〜「作業適性」の間）に「乗って」があればライド。うち「空を飛」があれば `fly`、「水上」があれば `water`、他は `ground`。無ければ `none` |
| `RideSprintSpeed` / `SwimDashSpeed` / `RunSpeed` | `<名前> \| <数値>` |
| ライド速度ボーナス(ランク4) | Lv別効果表の `MoveSpeed_up_PartnerSkill_Ride_*` の**最後の値**（Lv5=ランク4。例: ベイントール20、ゼノドラン20） |
| 水上速度ボーナス(ランク4) | 同 `SwimSpeed_up_PartnerSkill_*` の最後の値（ネプティオス25） |
| ペア強化(ランク4) | 同 `MoveSpeed_PartnerSkill_SpecificElement` の最後の値（ライトロット25 = 手持ちの他の無属性1体につき+25%） |
| 運搬Lv | 一覧の `data-filters` の `運搬N`（無ければ0） |

実効速度 v_eff:
- ground / fly: `RideSprintSpeed × (1 + ライド速度ボーナス/100) × (1 + 4 × ペア強化/100)`
  （「ペア強化条件を最大に満たす」= 手持ち5体のうち自分以外の4体が条件を満たす）
- water: `SwimDashSpeed × (1 + 水上速度ボーナス/100)`

基準点の実測: ベイントール 1900×1.20 = **2280**、ライトロット 1250×(1+4×0.25) = **2500**、
ゼノドラン 2700×1.20 = **3240**、ネプティオス 2000×1.25 = **2500**（水上ライドの最速。ライトロットと同値）。

10段階評価 S10（小数第1位で四捨五入）:
- **ライド個体**（115種）: 区分線形 `(800→5.0), (2280→8.0), (2500→10.0)`、800未満は5.0、2500以上は10.0。
  これに `+0.1×運搬Lv` を足して上限10.0。→ ベイントール 8.0、ライトロット/ゼノドラン/ネプティオス 10.0。
- **非ライド個体**（172種）: `1 + 0.5×運搬Lv + 2×clamp((RunSpeed − 200)/500, 0, 1)`、上限 6.0。

S = round(40 + 10×S10)（50〜140）。分布: S10 の整数部 1:50種, 2:62, 3:41, 4:13, 5:76, 6:26, 7:7, 8:2, 9:2, 10:8。
**期待値の全287種一覧は付録B**（実装結果をこれと突き合わせる）。

#### 3-e. 実装

- 新規 `Tools/fetch_paldb_speed.py`: 一覧と287ページを取得（1リクエストごとに0.3秒待つ、取得済みは再取得しない）、
  3-d の値を抽出して `Data/source/paldb_speed.json`（species_id をキー）に保存。生HTMLはコミットしない。
- 新規 `Tools/build_habcds.py`: species.json と paldb_speed.json から 3-b〜3-d を計算して species.json に書き戻す。
  **`legacy_had` があればそれを入力に使う**（2回実行しても結果が変わらないように）。
- `Scripts/Species/SpeciesDatabase.cs` `SpeciesJson`: `base_spatk`/`base_spdef`/`base_speed`/`role` を追加
  （`legacy_had`/`speed_source` は C# では読まない）。`Validate()` に「6能力が全て1以上」を追加。
- `Scripts/Species/SpeciesData.cs`: `BaseSpAtk/BaseSpDef/BaseSpeed`（int）、`Role`（string）、
  `public int Bst => BaseHP + BaseAtk + BaseDef + BaseSpAtk + BaseSpDef + BaseSpeed;`。

### Phase 4 — HABCDS の配線

- `Scripts/Entities/EntityStats.cs`:
  - 未使用の `[Export] int SpAttack/SpDefense` を削除し、`BaseSpAtk/BaseSpDef/BaseSpeed`（既定 70）、
    `BreakthroughSpAtk/BreakthroughSpDef/BreakthroughSpeed`（既定 0）を追加。
  - 実数値 `SpAttack`/`SpDefense`/`Speed` を `Attack` と同じ式で計算するプロパティにする。
  - `FillFromSpecies` で3つを写す。
  - `Player/AllyEntity/BossEntity/DummyNPC/FastNPC` の `Stats.SpAttack = …; Stats.SpDefense = …;` は削除
    （どこからも読まれていなかった値。種族から埋まらない仮個体は既定 70 になる）。
  - `public int BaseTotal => BaseMaxHp + BaseAtk + BaseDef + BaseSpAtk + BaseSpDef + BaseSpeed;`
- `Scripts/Turn/AttackAction.cs`:
  - 798–799行: `bool special = move.Category == MoveCategory.Special;`
    `BaseAtk = special ? _attacker.Stats.SpAttack : _attacker.Stats.Attack`、
    `BaseDef = special ? defenderStats.SpDefense : defenderStats.Defense`。
  - 種族値%の平坦加算（597・615・734・736・750行）: `Stats.BaseAtk` → `special ? Stats.BaseSpAtk : Stats.BaseAtk`、
    `Stats.BaseDef` → `special ? Stats.BaseSpDef : Stats.BaseDef`。
  - 攻撃ランク・防御ランクは**分けない**（攻撃ランクは A と C の両方、防御ランクは B と D の両方に掛かる）。
  - ハードブロック/ハードアーマーは現状のまま（防御側の倍率 defMul が、その技の分類に対応する B/D に掛かるので
    自然に「特殊被弾時は D を2倍」になる）。説明文 `traits.json` の「防御力」を「精神」/「防御」に直す。
- `Scripts/Battle/BattleScheduler.cs`:
  - `Bst(Entity)` を `e.Stats.BaseTotal`（BST6）に。
  - `Order(a, b)`（Q5 推奨案）: 優先度高い順 → **`Stats.BaseSpeed`（種族値）高い順** → BST6 低い順 → 乱数。
    ランク変化・持ち物で S は動かない（現行の「種族値を見る」方針を S にも適用）。
- BST3 を計算している §2-4 の全箇所を `SpeciesData.Bst` / `BattleScheduler.Bst` に置き換える。
  - `NpcOpponent` 84行の選出採点は、BST6 の規模が BST3 の約1.75倍なので `bst * 0.57f` として重みを現行並みに保つ（定数に名前を付ける）。
  - `TacticalBuilder` の「耐久(HP+防御×1.4)」は `H + (B+D)/2 × 1.4`、「攻撃」は `max(A, C)` に。
  - `Data/npc_teams.json` の `total_bst` を BST6 で**値だけ**更新（編成は変えない）。`Tools/generate_npc_teams.py` の `bst()` も BST6 に。
- UI:
  - `TeamBuildScreen` 214–217行: StatChip を `HP / 攻撃 / 防御 / 魔力 / 精神 / 俊敏 / 合計` の7つに。
  - `SpeciesPickScreen` 162–164行: `H 70 A 70 B 70 C 28 D 67 S 63` の1行に（略称表記）。
  - 「BST」表記の箇所は値が BST6 になるだけで文言は変えない。
- `docs/design/pvp_spec.md` §1「行動順の決定」を新規則に書き換え（§28 で経緯を説明）。

### Phase 5 — Artifact・設計記録・通し計測

- `Tools/build_ls_payload.py`: payload に `b`(=B) `c` `dd`(=D) `sp`(=S) `role` を追加、`bst` を BST6 に、`EL` に虚。
  `Tools/artifacts/ls_head.html`/`ls_tail.html` の表示を 6 能力に（列見出し H A B C D S 合計、役割の絞り込み）。
- `python3 Tools/build_ls_payload.py` と `python3 Tools/refresh_move_editor.py` を実行し、
  `Tools/artifacts/learnset_viewer.html` を **url `188eb9b1-d49d-4c39-bf77-895a30185e99`**、
  `Tools/artifacts/move_editor.html` を **url `975b54fe-5c46-4597-ae30-26d694ac3eb2`** へ、同じ file_path で再公開。
- `docs/design/pvp_spec.md` に **§28** を追加（相性表・虚の割当規則・虚脱・HABCDS・行動順の変更と、Phase 0/5 の計測結果）。
- `--random --matches 10000 --out` を実行し、Phase 0 のベースラインと `Tools/analyze_random_by_element.py` で比較。
  **注意（CLAUDE.md）**: 種族値を変えたので `MetaScenario`（シード `20260822UL`）の構築番号は以前と別物になる。
  §20〜§26 の結果と構築番号で比較しないこと。

### Phase 6 — 制限技 12 種（Q10 で採否が決まってから）

#### 6-a. 共通の仕組み

- **全て攻撃技（威力あり）**にする。理由: NPC は変化技を評価しない（§2-4）ので、変化技にすると
  シミュレーションで一度も使われず効果を測れない。制限効果は「命中してダメージが入ったときの追加効果」。
- 追加効果を通さない条件は既存と同じ: 防御側の `kyoujin_na_karada`、攻撃側の MudCaked
  （`AttackAction.BlocksSecondaryEffectsFor`）、無効化（ワイドウォード・ぜつえんたい等でダメージ0）。
- `Data/moves.json` に新しいキー `restriction`（文字列）と `restriction_turns`（int）。
  `MoveData` に `Restriction`（新 enum `RestrictionKind`）と `RestrictionTurns`。
- `StatusEffectManager` に**状態異常スロットとは独立の**制限状態を持たせる（複数同時にかかる。同じ種類を重ねると残り回数を
  上書き＝延長しない）。各制限は「対象自身の行動回数」で減る（2-a の行動終了通知で −1）。
  ただし R4・R8 は「サイクル」で減る（`BattleScheduler.EndCycle` で −1。迷宮では action-cycle ごと）。
- 「最後に使った技」: `Entity.LastUsedMoveSlot`（int、未使用は −1）。PP を消費して技を出したときに記録。対戦開始時 −1。
- 使えない技の扱い:
  - エンジン: `AttackAction.Execute` の先頭（PP判定より前）で制限に該当したら、PP を減らさず、
    `"{名前}は{理由}で{技名}を出せない!"` をログに出して行動を終える（手番は消費）。
  - NPC: `NpcOpponent` の技ループで、制限に該当するスロットを候補から外す。
  - 対戦 UI（`BattleHud` の技ボタン）: 該当スロットを押せない状態にする。
  - 判定は `StatusEffectManager.RestrictionBlocks(Entity self, int slot, MoveData move) → string reason or null` の1箇所に集める。

#### 6-b. 技の一覧

| # | id / 名前 | 属 | 分類 | 威力/命中/PP | 射程・接触 | 効果（`restriction`, 回数） | 習得 |
|---|---|---|---|---|---|---|---|
| R1 | `rs_seal_snap` シールスナップ | 虚 | 特殊 | 40/100/8 | 単体・非接触 | `SealLastMove`, 2行動: 対象が最後に使った技を使えない（未使用なら効果なし） | 189 ゼノグリフ, 145 ゼノベーダ |
| R2 | `rs_provoke` プロヴォーク | 闇 | 物理 | 45/100/8 | 単体・接触 | `NoStatusMoves`, 2行動: 変化技を使えない | 080 ヘルガルダ, 168 ウラミィ |
| R3 | `rs_repeat_chant` リピートチャント | 無 | 特殊 | 40/100/8 | 直線・非接触 | `Encore`, 1行動: 次の行動は最後に使った技に強制差し替え（提出された狙いのまま。狙えなければ不発。移動を出しても差し替え）。未使用なら効果なし | 082 ミステリア, 170 ネモフィ |
| R4 | `rs_item_jam` グリッチボルト | 電 | 特殊 | 45/100/8 | 2マス・非接触 | `ItemJam`, 3サイクル: 持ち物の効果なし（`Entity.Holds()` が false を返す。使い切り型は発動も消費もしない） | 185 エレパンダ, 161 オーマサンダ |
| R5 | `rs_tide_hook` タイドフック | 水 | 物理 | 50/100/8 | 2マス・非接触 | `Pull`, 即時: 対象を使用者へ向けて1マス引き寄せる（引き寄せ先が空き＆通行可のときだけ。クラッシュのノックバックの逆向き） | 075 シーペント, 128 オオタチウオ |
| R6 | `rs_frost_lag` フロストラグ | 氷 | 特殊 | 45/100/8 | 直線・非接触 | `PriorityDown`, 1行動: 次の行動の実効優先度 −1（`BattleScheduler.EffectivePriority` に加算） | 119 チルウィッチ, 095 フブキツネ |
| R7 | `rs_sand_eater` サンドイーター | 地 | 物理 | 50/100/8 | 単体・接触 | `PpDrain`, 即時: 対象が最後に使った技の PP −2（0 未満にしない） | 139 アヌビス, 140 セクメト |
| R8 | `rs_scorch_smog` スコーチスモッグ | 炎 | 特殊 | 40/100/6 | 範囲・非接触 | `HealBlock`, 3サイクル: HP回復しない（`EntityStats.Heal` に `HealBlocked` 判定を1箇所。ドレイン・リジェネ・天候・はなばたけ・ラストトニック・闇のちから全て止まる。`HealToFull` は対象外） | 104 イグニクス, 176 ラピエール |
| R9 | `rs_dragon_anchor` ドラゴンアンカー | 竜 | 特殊 | 55/100/8 | 単体・非接触 | `RankUpBlock`, 3行動: ランクが上がらない（`ApplyRankDelta` の delta>0 と `ArmTemporaryRankBuff` を無視。下降は通る） | 160 アイギルガ, 124 フェスキー |
| R10 | `rs_short_leash` ショートリーシュ | 草 | 物理 | 45/100/8 | 単体・接触 | `AdjacentOnly`, 2行動: 射程が Adjacent 以外の技を使えない | 066 ヴィオレッタ, 152 ラベロット |
| R11 | `rs_nag_jab` ナグジャブ | 無 | 物理 | 40/100/8 | 単体・接触 | `NoRepeat`, 3行動: 直前の行動と同じ技を続けて使えない | 074 ゴリレイジ, 033 ガウルフ |
| R12 | `rs_hollow_touch` ホロウタッチ | 虚 | 特殊 | 50/100/6 | 単体・非接触 | 虚脱を確定付与（`ailment_effect: Hollow`, `ailment_chance: 100`。`restriction` なし） | 157B コスモディア, 150 オーメン |

- 既存との重複回避: 行動不能（凍結・気絶）、移動封じ（帯電）、命中低下（暗闇）、追加効果封じ（泥まみれ）、
  道具封じ（ツタまみれ）、ランク低下、ノックバック（クラッシュ）とは**効果が重ならない**ものだけを選んだ。
  R4 はツタまみれ（道具を**使う**行動の封印＝迷宮向け）と違い、**持っている持ち物の効果**を止める（対戦向け）。
- 習得: 各技を上表の1〜2種の learnset に level 30 で追加（対戦では level を見ない）。1種が持つ制限技は1つだけ。
  `Tools/learnset_rules.py` に `RESTRICTION_LEARNERS = {move_id: [species_id, ...]}` を置き、
  `verify_learnset.py` は SIGNATURE と同様に上限検査から除外、かつ「各制限技の習得者が1〜2種で表と一致」を検査。
- 威力値が `verify_attack_moves.py` の「同属性・同分類で同威力は2種まで」に触れたら ±5 して報告。
- バランス上の注意:
  - R3（アンコール）と R1（封印）は「最後に使った技」が前提なので、1サイクル目の開幕には効かない。
  - R4 は持ち物が勝敗を大きく動かす（§17）ので最も強い可能性がある。3サイクルで重すぎれば2へ。
  - R6 は優先度+1を持つヘッドバット・水のちから(あめ)の打ち消しにもなる。
  - R8 の範囲技は味方も巻き込む（範囲の既存仕様どおり）。
  - NPC は制限効果そのものを評価しない（威力でだけ選ぶ）ので、シミュレーションでの使用率は過小になる。

---

## 4. 判断が必要だった点（採った解釈と理由）

### 4-1. 「炎に相性不利を追加」「無に相性有利を追加」「この中間に虚」
- 候補: (a) この2つは**虚の追加によって実現する**（炎→虚 0.5 が「炎の不利」、無→虚 2.0 が「無の有利」、虚がその中間）。
  (b) 既存9属性のマスも別途変える（例: 炎→炎 0.5、無→地 2.0）。
- 採用: **(a)**。箇条書きの構造（親項目2つの直後に「この中間に虚」、子項目に具体マス）がそう読める。
  §5-1 の静的指標で (a) だけでも炎は攻撃平均 1.199→1.146、被弾平均 1.021→1.119（最良→平均並み）、
  無は攻撃平均 0.871→1.024 と、狙いの方向に十分動く。(b) は Q2 で確認。

### 4-2. 虚の攻撃面（虚→何が抜群か）
- 指定は「虚→無が半減」のみで、虚が抜群を取る相手が無い。全属性が抜群を1つ以上持つ現行の作り（炎だけ2つ）に揃えると、
  虚にも1つ要る。「無 > 虚 > 炎」の並びを完成させる **虚→炎 2.0** を推奨（Q1）。これが無いと虚の攻撃平均は 0.937 で最下位になる。

### 4-3. 「闇の約40%、次に個体数が多い属性から2体」
- 候補: (a) 闇の40%≒30体＋水2体＝32体。(b) **虚の総数を闇の40%≒30体とし、うち2体を水から**（闇28＋水2）。
- 採用: **(b)**。0.4×74=29.6≒30 を「虚属性の規模」と読んだ。差は2体で結果への影響は小さいので確認事項にはしない。
- 闇から抜くのは単属性15・二重属性13（闇の単/複の比 38:36 にほぼ比例）。二重属性は相方属性ごとに層化して、
  炎/闇が虚へ寄りすぎない（炎/闇10種のうち4種）ようにした。炎/虚は炎技を半減するため、寄せすぎると炎の弱体化が薄まる。

### 4-4. 虚の技
- 闇の攻撃技を1対1で複製（`void_` 技 57種）。新しく威力表を設計すると verify_attack_moves の属性別規則
  （5刻みの網羅・射程上限・系統定員）を満たすよう作り直す必要があるが、闇の写しなら闇と同じ分布で規則を満たす。
- 闇の暗闇宣言は虚では虚脱宣言に変える（属性→蓄積状態異常の対応を他属性と揃える）。

### 4-5. 魔力=特殊攻撃、精神=特殊防御
- 技データは既に Physical/Special を持っているので、C/D をそれぞれ特殊の攻防に当てるのが最も自然で、技データの変更が要らない。
  攻撃ランク・防御ランクは分けない（分けると 70 以上の変化技・特性の対象を全部決め直すことになる。やらないこと参照）。

### 4-6. 俊敏の使い道
- 名前と、ユーザーが速度の尺度を細かく指定していることから、**行動順に効かせる**のが自然（Q5）。
  ただし現行は「低BST先手」で非力な側を補償する設計（pvp_spec §1・§25）なので、S を最優先にすると補償の向きが変わる。
- 「天候は合計種族値の高い側から発動（低い側が取る）」は S と無関係なので BST6 のまま残す。

### 4-7. 合計種族値の定義
- **BST6（6能力合計）**にする（Q6）。行動順のタイブレーク・天候・NPC選出・MetaScenario の並びが BST6 を使う。
- ただし **learnset 系ツールの閾値**（BST180〜500 を前提にした上限表、`<310` などの規則）は旧尺度で調整されているので、
  `legacy_had` の合計（BST3）を使い続ける。learnset 自体は再生成しない。

### 4-8. 役割の意味と分け方
- 「現在の物理型を物理耐久/物理攻撃に分ける」と読んだ（物理技を使う型のまま、配分の傾きが違う）。分け方は旧 d と a の大小
  （データだけで決まり再現できる）。ポケモン的に「物理耐久=B が高い型（技の分類と無関係）」と読む案もある（Q4）。
- 物理/特殊型の配分は「主系統は旧値のまま、副系統を削る」。主系統どうしのダメージが変わらないので、
  既存のバランス検証（§19〜§27）との連続性が最も保たれる。

### 4-9. 打ち分け型の「H×0.8 = B+D」
- 文字どおり H を据え置いて B+D=0.8H とすると、打ち分け型だけ防御が約4割に落ちる（70/70/70 で B+D=56、他の型は 120〜135）。
  「耐久配分」という語から、**H・B・D で共有する耐久の予算を H:(B+D)=1:0.8 で配る**と読む案 (II) を推奨（Q3）。
  B:D の傾きは learnset の物理/特殊比 r を使う（物理寄りの打ち分けは物理に硬い）。r は [0.4, 0.6] に収めるので
  B:D の許容幅 35:65〜65:35 にも自動的に収まる。

### 4-10. 俊敏の写像
- 運搬Lvと走行速度だけでは基準点（ライド3種）を表せないので、**ライドの有無で2系統**に分けた。
  ライド系は paldb の実速度を基準点3つ（800→5, 2280→8, 2500→10）で区分線形に写す。
  基準点2つだけの直線だと傾きが急すぎ（110速度で1段）、平均的なライド個体（v≈1200）が非ライドより遅くなり
  「ライド個体は俊敏が高い」に反するため、ライドの下限を5にした。
- 非ライド系は運搬Lvを主、RunSpeed を従にした（運搬0の61種が全て同値になるのを避けるため。上限6でライドの下限5と少し重なる）。
- 水上ライドは同じ尺度（SwimDashSpeed）で評価し、ネプティオス 2500 がライトロットと同値の 10.0 になる（Q7）。
- 「ペア強化」= paldb の `MoveSpeed_PartnerSkill_SpecificElement`（手持ちの特定属性の数で速くなる効果）と解釈した。
  「最大に満たす」は手持ち5体中、自分以外の4体が条件を満たす状態。

### 4-11. 虚脱
- 付与は虚技の蓄積（他属性と同じ仕組み）にした。専用技だけだと虚の個体が「虚らしさ」を持てない（Q9）。
- 主状態異常スロットに入れる（蓄積の仕組みは主スロットに発症させるものなので、独立フラグにすると蓄積処理の例外が要る）。
- 「2回目の行動終了時」は対戦のサイクル終了ではなく**本人の行動の終了**で数える
  （サイクル終了で数える泥まみれ方式だと、自分が動いた後に虚脱になった場合1回の行動で解ける）。

### 4-12. 制限技
- 12種を提案（10種以上の要求に対し、採否で減らせる余地を残した）。全て攻撃技にしたのは NPC が変化技を使わないため。

---

## 5. 検証方法

各フェーズで次の3つを必ず実行する（CLAUDE.md）。

```sh
dotnet build MysteryDungeonRoguelike.csproj              # 0 エラー
godot --headless --path . Scenes/BattleTestScene.tscn | grep '\[検証\]'   # 項目数と NG 0
git diff                                                 # この仕様書と突き合わせる
python3 Tools/verify_learnset.py && python3 Tools/verify_attack_moves.py && python3 Tools/verify_npc_teams.py   # ALL PASS
```

### 5-1. 相性表の静的指標（Phase 1 の期待値。Python で再計算して一致を確認）

「攻撃平均」= その属性の技を287種（変更後の属性）全員に撃ったときの倍率の平均。
「被弾平均」= その属性を持つ個体が10属性の技を等確率で受けたときの倍率の平均。

| | 無 | 炎 | 水 | 草 | 電 | 地 | 氷 | 竜 | 闇 | 虚 |
|---|---|---|---|---|---|---|---|---|---|---|
| 攻撃平均 現行 | 0.871 | 1.199 | 1.106 | 1.075 | 1.082 | 1.007 | 0.991 | 1.186 | 1.089 | — |
| 攻撃平均 Q1採用 | 1.024 | 1.146 | 1.106 | 1.075 | 1.075 | 1.007 | 1.092 | 1.091 | 1.037 | 1.087 |
| 被弾平均 現行 | 1.120 | 1.021 | 1.077 | 1.075 | 1.080 | 1.074 | 1.063 | 1.077 | 1.074 | — |
| 被弾平均 Q1採用 | 1.058 | 1.119 | 1.080 | 1.067 | 1.074 | 1.074 | 1.061 | 1.096 | 1.079 | 1.123 |

（Q1 不採用なら 炎の被弾平均 1.019・虚の攻撃平均 0.937。）

### 5-2. 回帰スイートに足す `[検証]`（各フェーズ。表記は `[検証] <名前>: OK (...)` の既存形式）

**Phase 1（+6 → 118）**
1. 相性表が10属性で Element と一致: `types` が10要素で enum と同順
2. 虚の6マス: 炎→虚0.5 / 闇→虚0.5 / 氷→虚2.0 / 無→虚2.0 / 虚→無0.5 / 虚→炎2.0（Q1不採用なら1.0）
3. 既存81マスが変わっていない（変更前の値をテストに埋め込んで比較）
4. 個体数: 虚30・闇46・水45（二重属性は両方に計上）
5. 虚の30種の learnset に闇（水→虚の2種は水）の攻撃技が0件、虚の攻撃技が1件以上
6. 虚の攻撃技が57件で、元の闇技と威力・分類・射程・命中・PP・接触が一致

**Phase 2（+5 → 123）**
1. 虚技の命中で虚脱の蓄積が+25（宣言なしの虚技1発で `GetAccumulation(Hollow) == 25`）
2. 虚脱中は一致技のダメージが非一致時と同値（同じ個体・同じ技で、虚脱なし＝×1.2、あり＝×1.0。〇〇派持ちでも1.0）
3. 行動前に虚脱 → 1回目の行動後はまだ虚脱、2回目の行動後に解除
4. 行動後に虚脱 → 同サイクル終了時はまだ虚脱、次サイクルの行動後もまだ、その次の行動後に解除
5. 気絶で飛ばした手番は数えない（気絶＋虚脱 → 飛ばした手番の後も虚脱のまま）

**Phase 3（+4 → 127）**
1. 287種全てが H/A/B/C/D/S を持ち全て1以上
2. 打ち分け79種: |B+D − round(0.8H)| ≤ 1、B/(B+D) ∈ [0.35, 0.65]、A/(A+C) ∈ [0.40, 0.60]
3. 物理/特殊型208種の主系統が `legacy_had` と一致（物理: A=a・B=d、特殊: C=a・D=d）
4. 俊敏の基準点: ベイントール S=120、ライトロット・ゼノドラン・ネプティオス S=140

**Phase 4（+5 → 132）**
1. 物理技は A 対 B、特殊技は C 対 D: A≠C・B≠D の2個体で、同威力の物理技と特殊技のダメージがそれぞれの式どおり
2. ベンチマーク: H70/A70/B70 Lv50 同士、威力35の物理技で 13（DamageCalculator のコメントの値が保たれる）
3. 同優先度なら俊敏が高いほうが先（新規）
4. BST が6能力合計（`BattleScheduler.Bst` が H+A+B+C+D+S）
5. 種族値%の平坦加算が分類に応じた能力を見る（〇〇のきずな持ちの特殊技は `BaseSpAtk*0.08*味方数`）

既存の2項目は**文言と期待値を書き換える**（項目数は変えない）:
「同優先度なら種族値が低いほうが先」→「同優先度・同俊敏なら種族値が低いほうが先」、
「優先度が種族値を上書きする」→「優先度が俊敏を上書きする」。
「対戦開始の天候は低種族値側が勝つ」は BST6 で同じ結論になること。

**Phase 5**: スイートの項目追加なし（132）。Artifact 2つの再公開と §28 の記録。
**Phase 6（+13 → 145）**: R1〜R12 の効果1項目ずつ（「効く条件で効く」と「効かない条件で効かない」を1項目内で対にする。
pvp_spec §27 の方針）＋「各制限技の習得者が1〜2種で表どおり」。

### 5-3. データの突き合わせ

- 付録B の S 値と species.json の `base_speed` が287種全て一致（取得元の更新で変わった種があれば、差分の一覧を報告）。
- 3-c の計算例 11 種が表どおり。
- Phase 5 の 10,000 試合: `analyze_random_by_element.py` で、**炎の BST補正後差がベースラインより小さく、
  無の補正後差がベースラインより大きい**こと。全属性の補正後差が ±5pt 以内に収まれば良好、収まらなければ数値を報告
  （ここは調整の判断材料であって、合否の条件ではない）。未決着率が 2% を超えたら報告。

---

## 6. やらないこと

- learnset の再生成（`generate_learnset.py` の再実行）。虚の個体の差し替えと制限技の追加以外、learnset は触らない。
- 虚のテンプレート特性（虚のきずな・ちから・まもり・流・派・式・おしえ）の追加。虚の天候特性・天候効果。
- 虚の地形適性（溶岩・水上移動など）。
- 攻撃/防御ランクの物理・特殊への分割、魔力・精神・俊敏のランク新設。
- 俊敏の回避・命中・迷宮のターン速度への利用。
- 迷宮側の UI（ステータス画面）の6能力表示。対戦の構築画面・種族選択画面だけ直す。
- 対戦 HUD への状態異常表示の新設（虚脱を含め、現状どの状態異常も HUD に出ていない）。
- NPC の判断に能力値・STAB・制限効果の評価を足すこと（選出採点の BST 置き換えのみ）。
- 既存の天候・持ち物・特性の数値調整。
- MetaScenario の構築番号を以前の実験と合わせること（種族値が変わるので不可能。CLAUDE.md 参照）。
- palworld-lab.com からの取得（403 のため。paldb.cc を使う）。

---

## 7. フェーズと完了条件

| Phase | 内容 | 完了条件 |
|---|---|---|
| 0 | 計測の土台（`--random --out`、属性別集計）・ベースライン10,000試合 | build 0エラー / スイート **112**・NG0 / 集計で炎が最高・無が最低 |
| 1 | 虚属性（相性表・enum・30種・虚技57・learnset差し替え・UI和名・ツール） | build 0エラー / スイート **118**・NG0 / Python 3本 ALL PASS / §5-1 の値と一致 |
| 2 | 虚脱 | build 0エラー / スイート **123**・NG0 |
| 3 | HABCDS データ（取得・変換・DTO） | build 0エラー / スイート **127**・NG0 / 付録Bと一致 / Python 3本 ALL PASS |
| 4 | HABCDS 配線（ダメージ・行動順・BST6・UI・NPC・npc_teams） | build 0エラー / スイート **132**・NG0 / Python 3本 ALL PASS |
| 5 | Artifact 再公開・pvp_spec §28・10,000試合の再計測 | Artifact 2つが同じ URL で更新 / §28 に前後比較の表 |
| 6 | 制限技12種（Q10 確定後） | build 0エラー / スイート **145**・NG0 / Python 3本 ALL PASS / Artifact 再公開 |

各フェーズは前のフェーズが完了条件を満たしてから着手し、フェーズごとに別コミットにする（コミットの指示があれば）。
項目数がこの表とずれたら、追加した `[検証]` の一覧を添えて報告する。

---

## 8. ユーザーに確認すべき点

解釈が分かれて**結果が実質的に変わる**ものだけ。各項目の推奨で草案を書いてある。

1. **Q1 虚の攻撃面**: 虚の技が抜群を取る相手を作るか。
   - A: 虚→炎 2.0 を追加（無 > 虚 > 炎 の並び。炎の被弾面が最良→平均並みに下がる。虚の攻撃平均 1.087）**【推奨】**
   - B: 追加しない（指定の5マスのみ。虚の攻撃平均 0.937 で全属性最下位、炎の被弾面は最良のまま）
   - C: 別の属性に抜群（例: 虚→闇 2.0。闇の個体が多いので虚の攻撃面が強くなりすぎる）
2. **Q2 「炎に不利・無に有利」の範囲**: 虚を介した変更だけでよいか。
   - A: 虚を介した変更のみ（既存81マスは不変）**【推奨】**
   - B: 加えて炎→炎 0.5（炎の攻撃平均 1.146→1.075）
   - C: 加えて無→地 2.0 など無に既存属性への抜群を足す（無の攻撃平均 1.024→1.181 で最強側に振れる）
3. **Q3 打ち分け型の「H×0.8 = B+D」**:
   - (I) H は据え置き、B+D を 0.8H にする（文字どおり。打ち分け型の防御が他の型の約4割、BST6平均 414）
   - (II) H・B・D の耐久予算 E = h + 1.85d を H:(B+D) = 1:0.8 で配る（打ち分け型は HP が増え防御が減る。BST6平均 510 で他の型と同水準）**【推奨】**
4. **Q4 物理耐久/物理攻撃…の意味と分け方**:
   - A: 「物理技を使う型」のまま、旧防御≥旧攻撃なら耐久型・それ以外は攻撃型。耐久型は副系統の攻撃を削り副系統の防御を厚く **【推奨】**
   - B: ポケモン的に「物理耐久=B が最も高い型」等、技の分類と独立に能力の山で分類（learnset の物理/特殊の傾きと能力が食い違う個体が出る）
5. **Q5 俊敏の使い道（行動順）**:
   - A: 優先度 → 俊敏の高い順 → BST6 の低い順 → 乱数 **【推奨】**（「低種族値ほど先手」の補償は同速のときだけになる）
   - B: 優先度 → BST6 の低い順 → 俊敏の高い順 → 乱数（現行の補償を維持し、S は同値のときだけ）
   - C: 行動順には使わない（回避など別の用途。用途を指定してほしい）
6. **Q6 合計種族値(BST)の定義**:
   - A: 6能力合計 BST6 に変える（行動順のタイブレーク・開幕天候・NPC選出・画面表示すべて）**【推奨】**
   - B: 旧3能力の合計 BST3 を残す（S や C/D が大きい個体も「非力」扱いのまま先手・天候を取れる）
7. **Q7 水上ライドの評価**:
   - A: 地上・飛行と同じ尺度。ネプティオス（水上 2500）＝ライトロットと同じ 10/10 **【推奨】**
   - B: 対戦盤面に水場が無いので水上は割り引く（×0.8 → ネプティオス 8/10 相当）
8. **Q8 俊敏の非ライド個体に走行速度(RunSpeed)を使うか**:
   - A: 運搬Lvを主、RunSpeed を従に使う（運搬0の61種が全員同値になるのを防ぐ）**【推奨】**
   - B: 運搬Lvだけ（指定に忠実。非ライド172種の S は運搬Lvの6段階に固まる）
9. **Q9 虚脱の付与手段**:
   - A: 虚技の命中で蓄積（他属性の状態異常と同じ仕組み）＋確定付与の専用技1種 **【推奨】**
   - B: 専用技だけ（虚脱は珍しい状態になる。虚の個体の虚技に追加効果が無くなる）
10. **Q10 制限技12案（§3 Phase 6 の表）の採否と習得個体**: 12案すべて採用でよいか、除外・差し替えたい案や、
    習得させたい個体の指定があるか。**【推奨】12案すべて採用・表の習得個体のまま**（R4 グリッチボルトは強すぎる懸念があるので、
    採用時は3サイクル→2サイクルにする選択肢も残す）。

---

## 付録A. 虚の30種
§3 Phase 1 1-c の表のとおり。

## 付録B. 俊敏 S の期待値（species_id:S＋ライド種別。G=地上 F=飛行 W=水上 -=非ライド）

2026-10-25 に paldb.cc から取得した値で §3-d の式を計算したもの。

```
001:63- 002:63- 003:57- 004:58- 005:59- 005B:59- 006:56- 007:69- 007B:69- 008:54- 009:59- 009B:59-
010:61- 011:54- 012:54- 013:63- 014:58- 015:58- 015B:58- 016:59- 017:67- 017B:67- 018:75- 018B:75-
019:57- 020:92G 021:95G 021B:95G 022:59- 023:59- 023B:59- 024:54- 025:57- 026:58- 027:61- 027B:61-
028:90G 029:58- 029B:58- 030:68- 030B:68- 031:59- 032:92G 032B:92G 033:99G 034:58- 034B:58- 035:52-
036:94G 037:60- 037B:60- 038:68- 038B:73- 039:54- 039B:54- 040:65- 041:99W 041B:99W 042:61- 043:70-
043B:70- 044:57- 044B:57- 045:57- 046:63- 047:65- 048:64- 048B:64- 049:66- 050:61- 051:92F 052:68-
053:54- 054:96G 054B:96G 055:63- 055B:63- 056:66- 056B:66- 057:70- 058:99G 059:59- 060:63- 061:64-
062:55- 063:94F 063B:94F 064:94F 064B:94F 065:64- 066:68- 067:70- 068:60- 069:80- 070:94G 071:100G
072:90G 072B:90G 073:71- 073B:63- 074:79- 074B:79- 075:103W 075B:93G 076:76- 076B:76- 077:63- 078:70-
078B:70- 079:70- 079B:70- 080:100F 080B:101F 081:85- 081B:85- 082:72- 083:99G 083B:99G 084:95G 084B:95G
085:76- 085B:76- 086:59- 087:95G 087B:95G 088:57- 088B:57- 089:74- 089B:74- 090:76- 091:80- 091B:85-
092:68- 092B:68- 093:100G 093B:100G 094:97G 094B:97G 095:66- 096:136F 096B:136F 097:103W 097B:103W 098:122G
098B:102G 099:70- 099B:70- 100:68- 100B:68- 101:100G 102:98G 102B:98G 103:95G 103B:95G 104:105F 105:91G
105B:91G 106:94G 107:54- 108:94G 108B:94G 109:65- 109B:65- 110:94G 111:101G 111B:101G 112:98G 112B:98G
113:87- 113B:87- 114:62- 115:72- 116:58- 116B:58- 117:96G 118:95G 118B:95G 119:68- 120:71- 121:110W
121B:98G 122:118F 122B:118F 123:98G 124:107F 124B:106F 125:58- 126:70- 127:96G 128:62- 128B:62- 129:94G
129B:94G 130:99G 130B:140G 131:56- 131B:56- 132:90- 132B:90- 133:68- 134:101G 134B:101G 135:70- 136:76-
137:98G 137B:98G 138:96G 138B:96G 139:90- 140:76- 141:70- 141B:70- 142:90G 142B:90G 143:81- 144:80-
145:74- 146:98G 147:69- 148:67- 148B:67- 149:64- 149B:64- 150:58- 151:98W 151B:98W 152:77- 153:74-
154:99G 155:69- 156:93G 157:104G 157B:98G 158:96F 159:99- 159B:99- 160:95G 161:99G 162:70- 163:56-
163B:56- 164:64- 165:66- 166:63- 167:54- 168:68- 169:107W 169B:107W 170:66- 171:140F 171B:140F 172:97F
173:66- 174:72- 175:104G 176:72- 177:106F 178:70- 179:54- 180:54- 181:65- 182:70- 183:83- 184:96G
185:99G 186:60- 186B:60- 187:90- 188:105F 188B:107F 189:106F 190:109F 191:96G 192:140F 193:72- 194:85-
195:86- 195B:86- 196:140F 197:112G 198:118G 199:120G 200:110F 200B:110F 201:140W 202:140F 203:140F
```

## 付録C. 取得元の確認記録
- `curl https://palworld-lab.com/pals/` → 403（Cloudflare "Just a moment..."）
- `curl https://paldb.cc/ja/Pals` → 200、カード 299 件（亜種・未実装種を含む）、287種の和名すべてを含む
- 個体ページ 287 件 → 全件 200。HP/攻撃/防御が species.json と一致 285/287
