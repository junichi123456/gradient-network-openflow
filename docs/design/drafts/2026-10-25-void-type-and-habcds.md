# 虚(うろ)属性・虚脱状態・HABCDS・制限技 — 仕様書

- **状態: 確定（2026-10-25、ユーザー確認済み）**。確認で決まった内容は §8「確定事項」にまとめ、本文はすべてそれに合わせてある。
- 作成: 2026-10-25（草案 → ユーザー確認 → 本確定版）
- 対象ブランチ: `claude/mystery-dungeon-roguelike-egnzjt`
- 読み手: 会話履歴を持たない実装者。**この文書だけで実装できるように書いてある。**
  書かれていないことは勝手に決めず、報告すること（裁量を認める範囲は末尾「実装時に判断してよい範囲」に限る）。

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

確認時の追加指示（原文）:

```
ステータスの合計値は https://palworld-lab.com/pals/ のデータベースを参照とし、タマコッコなどが属する
BST3=180 のとき BST6=360 となるように再構築する。しかしながらグランジーラのような BST3=500 の場合は
BST6=630 となるよう、合計種族値に線形の補正を行うものとする
```

### 0-2. 用語

| 用語 | 意味 |
|---|---|
| H/A/B/C/D/S | HP / 攻撃 / 防御 / 魔力 / 精神 / 俊敏。**HABCDS** と総称する |
| 魔力(C) | 特殊技の攻撃側に使う能力（§4-5） |
| 精神(D) | 特殊技の防御側に使う能力（同上） |
| 俊敏(S) | 行動順に使う能力（§4-6）。「x/10 評価」は **S = 13 × x**（10/10 = 130、8/10 = 104）と読む |
| 旧HAD | 現行の `base_hp` / `base_atk` / `base_def` の3能力。本書では小文字 h/a/d と書く |
| BST3 | 旧HADの合計 h+a+d（現行の「合計種族値」） |
| BST6 | HABCDS の合計 H+A+B+C+D+S（新しい「合計種族値」）。**§3-c の式で BST3 から決まる固定値** |
| 型(profile) | `Tools/learnset_rules.py` の `profile_of()` が返す Physical / Special / Versatile（物理型/特殊型/打ち分け型）。**習得する技の傾向**を表す |
| 役割(role) | 本書で新設。`PhysicalWall` 物理耐久 / `PhysicalAttacker` 物理攻撃 / `SpecialWall` 特殊耐久 / `SpecialAttacker` 特殊攻撃 / `Mixed` 打ち分け の5種 |
| 主側 / 副側 | 物理型の主側 = 攻撃 A・防御 B、副側 = C・D。特殊型の主側 = C・D、副側 = A・B。H はどちらにも属さない共通の耐久 |
| 行動 | 対戦では、パルが自分のターンに実際に行動した1回（攻撃・変化技・移動）。迷宮では、その個体の1回の action-cycle |
| 一致補正(STAB) | 技の属性が使用者の属性と一致するときの ×1.2（〇〇派特性なら ×1.5） |
| round(x) | **四捨五入（x.5 は切り上げ）= floor(x + 1/2)**。本書の数式はすべて有理数で厳密に計算してから丸める（浮動小数の誤差で .5 の扱いが揺れないように、Python は `fractions.Fraction` を使う） |

### 0-3. 現行の数値（2026-10-25 時点、実測）

- 種族 287 / 技 582（Physical 310・Special 214・Status 58）/ 特性 121
- 属性 9種（`Neutral, Fire, Water, Grass, Electric, Ground, Ice, Dragon, Dark`）。
  個体数（二重属性は両方に数える）: 闇74, 水47, 草46, 地44, 炎43, 氷38, 無36, 電25, 竜21。
  闇の内訳は単属性38・二重属性36。
- 型の内訳: 物理型124（周期105＋特性19）、特殊型84（周期79＋特性5）、打ち分け型79。
- 旧HAD: h 60〜200（平均95.3）、a 50〜150（96.7）、d 50〜180（93.1）。
  **BST3 は最小 180（003 タマコッコ・035）〜最大 500（203 グランジーラ）、平均 285.2、全287種が5の倍数**。
- 回帰スイート `godot --headless --path . Scenes/BattleTestScene.tscn` は
  `[検証]` 行が **112** 行（`OK` を含む 109 行＋期待値併記の情報行 3 行。NG 0）。
- Python 検証: `Tools/verify_learnset.py` 16 PASS、`Tools/verify_attack_moves.py` 23 PASS、
  `Tools/verify_npc_teams.py` 10 PASS（いずれも ALL PASS）。
- 直近の 10,000 試合ランダム構築（呼び出し元の集計）: **炎が勝率55.7%・BST補正後+5.1pt で最強、
  無が41.6%・補正後−6.6pt で最弱**。※この集計を再現するスクリプトはリポジトリに無い（Phase 0 で作る）。
- 回帰スイートの盤上8種: モコロン(001), クルリス, タマモ, ブシガエル(009), カモノスケ(005), ミルフィー(008),
  ルミカイト(007), タマコッコ(003)。`VerifyTurnOrder` は BST 最小のタマコッコと最大のブシガエルを使う。

### 0-4. 参照元: palworld-lab.com は取得できない → paldb.cc を代替にする

- `https://palworld-lab.com/pals/` は Cloudflare のチャレンジで **HTTP 403**（curl / WebFetch とも。
  2026-10-25 に再試行しても 403、ブラウザ相当の User-Agent でも 403）。
- 代替として **`https://paldb.cc/ja/Pals`（一覧）と `https://paldb.cc/ja/<英名>`（個体ページ）** を使う
  （HTTP 200、プロキシ経由で可）。
  - 一覧の各カードの `data-filters` 属性に作業適性とLv（例 `"手作業1 運搬1 牧場1 無属性"`）、
    `class="itemname"` のリンクテキストに和名、`href` に英名がある。**287種の `display_name` は全て一覧に存在する。**
  - 個体ページに パートナースキル文・`RunSpeed`・`RideSprintSpeed`・`SwimDashSpeed`・
    パートナースキルのLv別効果表（`MoveSpeed_up_PartnerSkill_Ride_4` 等）がある。
- **ユーザーの意図（Palworld の実データに基づく配分）との整合**:
  palworld-lab.com も paldb.cc も Palworld のゲームデータ（HP/攻撃/防御・作業適性・移動速度）を載せた
  データベースで、同じ値を参照している。実測で確認した:
  - 個体ページの HP/攻撃/防御 は species.json の旧HADと **285/287 種で完全一致**。
  - 不一致2種も **合計は一致**: リリクイン（186）は paldb 120/120/110、本リポジトリ 105/125/120（調整済み）で
    どちらも合計350。グランジーラ（203）は paldb 180/120/200、本リポジトリ 200/120/180（HP と防御が入れ替わっている）で
    どちらも合計500。
  - したがって **BST3（＝BST6 の入力）は 287/287 種で参照元と一致**し、どちらのサイトを使っても BST6 は同じになる。
    能力の内訳（H と B の比など）は本リポジトリの旧HAD（調整済みの値）を正とする。
- 実装時は取得結果を **`Data/source/paldb_speed.json` に保存してコミット**する（以後ネットワーク不要に）。

---

## 1. 目的（達成条件）

1. **属性相性**: 10番目の属性「虚(Void)」が追加され、相性表が 10×10 になっている。
   ユーザー指定の5マス（炎→虚0.5、闇→虚0.5、虚→無0.5、無→虚2.0、氷→虚2.0）と、確定事項 Q1 の
   **虚→炎2.0**（相性鎖 無 > 虚 > 炎）の計6マスが入り、**既存 81 マスは変わらない**（Q2）。
   その結果として **炎の攻撃面・防御面が下がり、無の攻撃面が上がる**（§5-1 の静的指標）。
2. **虚の個体**: 30 種が虚を持つ（闇から28種・水から2種）。虚の個体は虚の攻撃技を覚え、
   闇（水）の攻撃技は覚えていない。
3. **虚脱**: 新しい状態異常「虚脱(Hollow)」が実装され、虚脱中は一致補正が 1.0 になり、
   **自分の2回目の行動終了時に解除**される。付与手段は虚技の蓄積＋確定付与の専用技1種（Q9）。
4. **HABCDS**: 287 種すべてが H/A/B/C/D/S の6能力を持ち、**6能力の合計が
   `BST6 = round(360 + (BST3 − 180) × 270/320)` に一致する**（タマコッコ 360、グランジーラ 630、リリクイン 503）。
   物理技は A 対 B、特殊技は C 対 D でダメージ計算される。S は行動順に効く（優先度 → S 高い順 → BST6 低い順 → 乱数）。
   合計種族値はゲーム内のすべての箇所で BST6。
5. **役割**: 物理型/特殊型は「主側の H・攻撃・防御のうち最高の能力」で耐久型/攻撃型に分かれ（Q4）、
   打ち分け型は B+D = 0.8H・B:D ∈ [35:65, 65:35]・A:C ∈ [40:60, 60:40] を満たす（Q3）。
6. **俊敏**: 全287種の S が **35〜130** に収まり（最小35・最大130 を実際に取る個体がある）、
   ヒストグラムが **75 付近と 100 付近の二峰**になる（Q8。§3-d の表）。ベイントール S=104（8/10）、
   ライトロット・ゼノドラン・ネプティオス S=130（10/10）。
7. **制限技**: 相手を制限する技が **12種** 追加され、各技を習得できるのは 1〜2 種だけ（Q10）。
8. 回帰スイートが全フェーズで NG 0、項目数が §7 の各フェーズの値になる。
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
| `Scripts/Battle/TacticalBuilder.cs` 61–63行 `Elements` / `_mult`、115行付近 `BestPowerByType = new float[9]` | 9×9 の相性キャッシュと属性別の最大威力配列 |
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
  ファイル冒頭コメントのベンチマーク: 種族値 70/70/70・Lv50・突破0 → 実数値 HP130/攻75/防75、威力35で **13**。
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
  `MaxHp = floor((Base*2+BT)*Lv/100)+Lv+10`。Lv50・突破0 なら 攻撃=Base+5、HP=Base+60）。
  `SpAttack/SpDefense` は **int の未使用フィールド**
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
  `BattleScheduler.Bst`、`BattlePal.Bst`、`BattleArena.ApplyEntryWeather`（合計種族値の高い陣営から天候発動＝低い側が最後に上書きして勝つ）、
  `MetaScenario` 55行（`StrongPool` の並び）、`NpcOpponent` 84行（選出の採点 `bst + power*2 + jitter(0〜60)`）、
  `TacticalBuilder` 105–106行（`Profile.Hp/Atk/Def/Bst`）と 142–144行（耐久 `Hp + Def*1.4`・攻撃 `Atk`）、`BattleMatchScene` 907–908行、
  UI: `DeployScreen` 247、`TeamBuildScreen` 161・214–217、`SpeciesPickScreen` 98・157・162–164、
  `SelectionScreen` 152、`BattleHud` 282・409。
  Python: `Tools/generate_npc_teams.py` `bst()`、`Data/npc_teams.json` の `total_bst`、
  `Tools/verify_learnset.py` `tot()`、`Tools/generate_learnset.py` `total_of()`、`Tools/build_ls_payload.py`。
- `Scripts/Battle/NpcOpponent.cs` の行動採点は `Power × 相性 × 命中` だけを見ており、能力値もSTABも見ない。
  **変化技（Power 0）は評価されず、NPC は一切使わない**（223行 `if (data.Power <= 0) continue;`）。
- 回帰スイート `Scripts/Battle/BattleTestScene.cs`:
  `VerifyTurnOrder`（590行付近）が「同優先度なら種族値が低いほうが先」「優先度が種族値を上書きする」を、
  `VerifyEntryWeather`（440行付近）がマグマンダー(105, BST3 350)とユキツネ(029B, BST3 215)で「低種族値側の天候が勝つ」を見ている。

### 2-5. 型（profile）

- `Tools/learnset_rules.py` `profile_of(trait_id, species_index)`: 特性指定（`TRAIT_PHYSICAL`/`TRAIT_SPECIAL`）が
  優先、無ければ `species_id` 昇順の序数で `PROFILE_CYCLE`（物4:特3:打3）。**C# 側には型の概念が無い。**
  learnset は既にこの型に沿って生成されている（物理型は物理技中心、など）。

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

`types` の**末尾**に `"Void"` を追加（既存の添字を動かさない）。`matrix` を 10×10 にする。
**既存 81 マスは1つも変えない**（確定事項 Q2）。追加は太字の6マスのみ（虚→炎 2.0 は確定事項 Q1）:

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
| **虚** | **0.5** | **2** | | | | | | | | |

JSON そのもの:

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
  115行付近の `BestPowerByType = new float[9]` も 10 にする（`Elements.Length` を使う形が望ましい）。
- その他 `"Dark"` を列挙している箇所は `grep -rn '"Dark"\|Element.Dark\|\[9\]\|, 9\b' Scripts` で洗い、
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

（BST3 は旧HADの合計。BST6 は BST3 の単調増加関数なので、BST6 で並べても同じ順になる。）

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
- learnset の Physical/Special 技数は差し替え前後で変わらない（同じ category へ置き換えるため）。§3-c の打ち分け型の r はこれを使う。

#### 1-f. Python ツール

- `Tools/build_ls_payload.py` `EL`、`Tools/refresh_move_editor.py`（と生成する HTML 内の和名マップ）、
  `Tools/export_learnsets_csv.py`、`Tools/add_status_moves.py`、`Tools/assign_weather_traits.py` に `'Void': '虚'` を追加
  （属性の全列挙だけ。天候割当には虚を足さない）。
- `Tools/verify_learnset.py` #4 の期待値を新しい表から手計算した値に更新し、表示を「10属性」に:
  `Neutral→[Dragon], Fire→[Neutral, Electric, Ice], Water→[Ground], Grass→[Water, Void], Electric→[Grass],
  Ground→[Fire], Ice→[Water, Void], Dragon→[Fire], Dark→[Ice], Void→[Fire, Dark]`。比較はリスト全体で行う。
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
| 付与①（蓄積） | **虚属性の技の蓄積**（`ElementalAilment("Void") => AilmentType.Hollow`）。虚技が当たるたび +25、虚脱を宣言する虚技（1-d で `Darkness` から書き換えたもの）は +chance×10。1000で発症。既存の蓄積の仕組みそのまま |
| 付与②（確定） | 専用技1種: 制限技 R12「ホロウタッチ」（Phase 6）。`ailment_effect: Hollow, ailment_chance: 100` |
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
| `base_hp` | H | 値は 3-c の式で決まる（**全種で値が変わり得る**） |
| `base_atk` | A（物理攻撃） | **意味が「攻撃(共通)」→「物理攻撃」に変わる**。値も 3-c で決まる |
| `base_def` | B（物理防御） | 同上 |
| `base_spatk` | C（魔力） | 新規 |
| `base_spdef` | D（精神） | 新規 |
| `base_speed` | S（俊敏） | 新規 |
| `role` | `PhysicalWall` / `PhysicalAttacker` / `SpecialWall` / `SpecialAttacker` / `Mixed` | 新規 |
| `legacy_had` | `{"hp":h,"atk":a,"def":d}` 変換前の旧HAD | 新規。**3-c の入力と、learnset 系ツールの BST 閾値に使う**（§4-7） |
| `speed_source` | `{"kind":"ground|fly|water|none","v_eff":数値|null,"transport":Lv,"run":数値}` | 新規。S の根拠を残す |

#### 3-b. 役割の決め方（確定事項 Q4）

**技の傾向（型）は特性基準、細分は主側の最高能力で決める。**

1. 型 = `Tools/learnset_rules.py` `profile_of(trait, index)`（index は species_id 昇順の序数。現行と同じ）。
   **型は変えない**（learnset は既にこの型で生成済みで、再生成もしない）。
2. 打ち分け型（Versatile）→ **`Mixed`**（細分しない）。
3. 物理型・特殊型は、**旧HAD の3値 h・a・d** を比べる。物理型にとっては H=h・A=a・B=d、
   特殊型にとっては H=h・C=a・D=d が主側の3能力に当たる（特殊型は旧攻撃を魔力、旧防御を精神として扱う）。
   - **a > max(h, d)**（攻撃系の能力が単独で最高）→ 攻撃型（`PhysicalAttacker` / `SpecialAttacker`）
   - **それ以外**（h か d が最高。**a と最高値が同値の場合も含む**）→ 耐久型（`PhysicalWall` / `SpecialWall`）
4. 同値の扱いの理由: 「最も高い能力で分類」の最高値が攻撃と耐久で並ぶときは、攻撃が単独で抜けていないので耐久型に入れる
   （旧草案の `d >= a → 耐久` と同じく、同値は耐久側）。
5. 3-c の配分は主側3値の比をそのまま保つ一様な縮尺なので、**変換後の値で比べても同じ分類になる**
   （物理: A > max(H, B) ⇔ 攻撃型、特殊: C > max(H, D) ⇔ 攻撃型。287/287 種で一致を確認済み。
   同値の端数配分は H・B・D を先にするので、同値だった耐久型で A(C) が H や B(D) を上回ることはない）。

数値例:

| id | 名前 | 型 | h/a/d | 判定 | 役割 |
|---|---|---|---|---|---|
| 001 | モコロン | 物理 | 70/70/70 | a=70 = max(h,d)=70（同値） | 物理耐久 |
| 009 | ブシガエル | 物理 | 80/100/85 | a=100 > 85 | 物理攻撃 |
| 199 | ベイントール | 物理 | 130/145/120 | a=145 > 130 | 物理攻撃 |
| 005 | カモノスケ | 特殊 | 60/80/60 | a(→C)=80 > 60 | 特殊攻撃 |
| 130B | ライトロット | 特殊 | 110/105/100 | h=110 > a=105（HP が最高） | 特殊耐久（旧規則 d<a では攻撃型だった） |
| 186 | リリクイン | 特殊 | 105/125/120 | a=125 > 120 | 特殊攻撃 |
| 203 | グランジーラ | 特殊 | 200/120/180 | h=200 が最高 | 特殊耐久 |

内訳: **物理耐久 76・物理攻撃 48・特殊耐久 53・特殊攻撃 31・打ち分け 79**（物理/特殊 208 種のうち
a が単独最高 79、a が最高と同値 30、a が最高未満 99）。旧草案の規則（d ≥ a）との差: 攻撃型→耐久型に移るのが27種（HP が最高の個体）、逆向きは0。

#### 3-c. 合計種族値 BST6 と HABCDS の配分（BST6 固定）

**① BST6（確定事項・追加指示）**

```
BST6 = round(360 + (BST3 − 180) × 270/320)        # 傾き 27/32 = 0.84375、round は x.5 切り上げ
     = 360 + floor(((BST3 − 180) × 54 + 32) / 64) # 整数だけで書いた同値式（Python の // はこの floor）
```

- BST3 は `legacy_had` の合計（無ければ現行の base_hp+base_atk+base_def）。
- 本リポジトリの BST3 は **180〜500 で範囲外は無い**ので、BST6 は 360〜630。
  範囲外の値が将来現れても**クランプせず同じ式を延長する**（例: BST3=520 → 647、BST3=170 → 352）。
- BST3 は全種5の倍数で、5違うと BST6 は 4.22 違うので、**BST3 が異なる2種の BST6 が同値になることは無い**。
  BST6 は BST3 の狭義単調増加関数なので、**BST で並べた順序はすべての箇所で変わらない**（§4-13）。
- 主な値: 180→360, 200→377, 210→385, 215→390, 230→402, 265→432, 300→461, 310→470, 315→474,
  **350→503（リリクイン）**, 370→520, 375→525, 395→541, 400→546, 450→588, **500→630（グランジーラ）**。
- 287種の BST6: 最小360・最大630・**平均 448.7**（BST3 平均 285.2 の写像）。役割別平均:
  物理耐久 449.8 / 物理攻撃 442.4 / 特殊耐久 450.9 / 特殊攻撃 456.5 / 打ち分け 447.1。

**② 配分の手順**（1種ごと）

1. S を 3-d で決める。
2. **残り予算 T = BST6 − S** を H・A・B・C・D の5能力に配る（6能力の合計が BST6 と厳密に一致する）。
3. 役割ごとの「原型」を旧HADから作り、T に縮尺する（下記）。原型の比は旧草案の設計（主側は旧値の比、副側は削る）を保つ。

**③ 物理型・特殊型の原型と整数化**

| 役割 | H | A | B | C | D |
|---|---|---|---|---|---|
| 物理攻撃 | h | a | d | 0.6a | 0.75d |
| 物理耐久 | h | a | d | 0.4a | 0.95d |
| 特殊攻撃 | h | 0.6a | 0.75d | a | d |
| 特殊耐久 | h | 0.4a | 0.95d | a | d |

- 縮尺率 k = T / (原型5値の合計)。実数値 x_i = 原型_i × k。
- 整数化（最大剰余法）: 各 x_i を切り捨て、合計の不足分 T − Σfloor(x_i) を、小数部の大きい順に1ずつ足す。
  小数部が同じなら **H, B, D, A, C の順**に優先する（耐久系を先に。3-b の「変換後で比べても同じ分類」を保つため）。
- k の範囲は 0.695（グランジーラ）〜1.199（タマコッコ）、平均 0.889。BST3 が大きい個体ほど、また S が大きい個体ほど k が小さい。
- **旧草案の「主側は旧値そのまま」は BST6 固定と両立しない**ため捨てた（§4-13）。保つのは**主側の比 h:a:d**。

**④ 打ち分け型（確定事項 Q3「耐久の予算配分」）**

- 物理寄り度 r = clamp(learnset の Physical 技数 / (Physical + Special 技数), 0.40, 0.60)。Status は数えない。分母0なら 0.5。
  （1-e の差し替え後の learnset で数える。差し替えで category は変わらないので前後同じ。
  **Phase 6 で足す制限技（id が `rs_` で始まる技）は数えない**——Phase 6 の後に `build_habcds.py` を再実行しても値が変わらないように。）
- 耐久原型 E_def = h + 1.85d、攻撃原型 E_atk = 1.7a（旧草案と同じ係数）。
- 耐久予算 Dt = round(T × E_def / (E_def + E_atk))、攻撃予算 At = T − Dt。
- **H = round(Dt / 1.8)**、G = Dt − H（⇔ **B+D = G ≒ 0.8H**。|G − 0.8H| ≤ 0.9 が常に成り立つ）。
- B = round(G × r)、D = G − B（**B:D ≒ r : 1−r ∈ [40:60, 60:40] ⊂ [35:65, 65:35]**）。
- A = round(At × r) を [ceil(0.4·At), floor(0.6·At)] にクランプ、C = At − A（**A:C ∈ [40:60, 60:40] を厳密に満たす**）。
- 実データでは r は 0.40（54種）か 0.60（25種）のどちらかに張り付く（learnset が物理/特殊どちらかに大きく偏っているため）。

**⑤ 共通**

- 6能力すべて 10 以上であること（実データの最小は A=20。10 を下回る個体が出たら丸めで補正せず報告する）。
- 計算は有理数で厳密に行う（Python `fractions.Fraction`）。

**計算例**（H/A/B/C/D/S。付録D に全287種）:

| id | 名前 | 旧 h/a/d | BST3 | BST6 | 役割 | HABCDS |
|---|---|---|---|---|---|---|
| 001 | モコロン | 70/70/70 | 210 | 385 | 物理耐久 | 71/71/71/29/68/75 |
| 003 | タマコッコ | 60/60/60 | 180 | 360 | 物理耐久 | 72/72/72/29/68/47 |
| 005 | カモノスケ | 60/80/60 | 200 | 377 | 特殊攻撃 | 62/50/46/83/62/74 |
| 007 | ルミカイト | 80/70/80 | 230 | 402 | 打ち分け(r=0.40) | 119/45/38/67/57/76 |
| 008 | ミルフィー | 70/70/75 | 215 | 390 | 打ち分け(r=0.40) | 124/51/40/76/59/40 |
| 009 | ブシガエル | 80/100/85 | 265 | 432 | 物理攻撃 | 74/92/78/55/59/74 |
| 029B | ユキツネ | 65/80/70 | 215 | 390 | 打ち分け(r=0.60) | 112/84/53/56/36/49 |
| 105 | マグマンダー | 110/125/115 | 350 | 503 | 特殊攻撃 | 88/60/69/99/91/96 |
| 130B | ライトロット | 110/105/100 | 315 | 474 | 特殊耐久 | 84/32/72/80/76/130 |
| 186 | リリクイン | 105/125/120 | 350 | 503 | 特殊攻撃 | 92/65/78/109/105/54 |
| 196 | ゼノドラン | 130/120/120 | 370 | 520 | 打ち分け(r=0.60) | 137/85/66/58/44/130 |
| 199 | ベイントール | 130/145/120 | 395 | 541 | 物理攻撃 | 99/111/92/66/69/104 |
| 201 | ネプティオス | 105/145/125 | 375 | 525 | 特殊攻撃 | 74/62/67/103/89/130 |
| 203 | グランジーラ | 200/120/180 | 500 | 630 | 特殊耐久 | 139/33/119/84/125/130 |

能力ごとの範囲（287種）: H 51〜151（平均95.6）、A 20〜113（66.8）、B 31〜119（69.5）、C 25〜121（63.3）、
D 30〜125（70.2）、S 35〜130（83.4）。役割別の平均 H/A/B/C/D/S:
物理耐久 87.6/81.0/84.4/32.5/80.1/84.2、物理攻撃 77.8/92.9/76.5/55.8/57.3/82.0、
特殊耐久 88.7/31.6/81.9/79.1/86.3/83.3、特殊攻撃 79.9/56.3/59.9/93.5/79.8/87.1、
打ち分け 125.0/64.9/46.3/75.0/53.7/82.1。

**ダメージ感の変化**（Lv50・突破0、DamageCalculator に実数値だけを渡し、STAB・相性・補正なし、威力35）:

| 攻撃側 → 防御側 | 分類 | ダメージ | 防御側HP | 確定数 |
|---|---|---|---|---|
| モコロン → モコロン | 物理（A71→76 対 B71→76） | **13** | 131 | 11 |
| モコロン → モコロン | 特殊（C29→34 対 D68→73） | **3** | 131 | 44 |
| タマコッコ → タマコッコ | 物理（A72 対 B72） | **13** | 132 | 11 |
| ブシガエル → ブシガエル | 物理（A92 対 B78） | **18** | 134 | 8 |
| ブシガエル → モコロン | 物理（A92 対 B71） | **19** | 131 | 7 |
| カモノスケ → カモノスケ | 特殊（C83 対 D62） | **17** | 122 | 8 |

- 旧仕様の「70/70/70 同士・威力35 で 13」は、DamageCalculator に 70/70/70 を直接与えるベンチマークとしては
  **そのまま 13**（式は変えないため）。ただし種族 001 モコロンはもう 70/70/70 ではない（71/71/71/29/68/75）。
- 287種それぞれの同族対戦（主側の高い方の技、威力35）の確定数は 6〜22、中央値 10。
  「同族対戦がおよそ10発で決着」という旧設計の性質は中央値では保たれる。

#### 3-d. 俊敏 S の導出（確定事項 Q7・Q8）

paldb.cc から取る値（個体ページ。正規化: タグを ` | ` に置換し空白・区切りを畳んでから正規表現で拾う）:

| 値 | 取り方 |
|---|---|
| ライド種別 | パートナースキル文（「パートナースキル」〜「作業適性」の間）に「乗って」があればライド。うち「空を飛」があれば `fly`、「水上」があれば `water`、他は `ground`。無ければ `none` |
| `RideSprintSpeed` / `SwimDashSpeed` / `RunSpeed` | `<名前> \| <数値>` |
| ライド速度ボーナス(ランク4) | Lv別効果表の `MoveSpeed_up_PartnerSkill_Ride_*` の**最後の値**（Lv5=ランク4。例: ベイントール20、ゼノドラン20） |
| 水上速度ボーナス(ランク4) | 同 `SwimSpeed_up_PartnerSkill_*` の最後の値（ネプティオス25） |
| ペア強化(ランク4) | 同 `MoveSpeed_PartnerSkill_SpecificElement` の最後の値（ライトロット25 = 手持ちの他の無属性1体につき+25%） |
| 運搬Lv T | 一覧の `data-filters` の `運搬N`（無ければ0） |

ライド 115種（地上75・飛行29・水上11）、非ライド 172種。

**実効速度 v_eff（ライド個体のみ）**:
- ground / fly: `RideSprintSpeed × (1 + ライド速度ボーナス/100) × (1 + 4 × ペア強化/100)`
  （「ペア強化条件を最大に満たす」= 手持ち5体のうち自分以外の4体が条件を満たす）
- water: `SwimDashSpeed × (1 + 水上速度ボーナス/100)`（地上・飛行と同じ尺度で評価。Q7）

基準点の実測: ベイントール 1900×1.20 = **2280**、ライトロット 1250×(1+4×0.25) = **2500**、
ゼノドラン 2700×1.20 = **3240**、ネプティオス 2000×1.25 = **2500**。

**S の式**（P(x) は下表の点を結ぶ区分線形関数。端点の外は端点の値で一定）:

- **ライド個体**: `S = min(130, round(P_R(v_eff) + T))`

  | v_eff | 375 | 1000 | 1400 | 2280 | 2500 |
  |---|---|---|---|---|---|
  | P_R | 90 | 98 | 102 | **104**（=8/10） | **130**（=10/10） |

  → ベイントール 104+運搬0 = **104**、ライトロット・ネプティオス 2500 → **130**、ゼノドラン 3240 → **130**。
  ライドの最小は 90。2280〜2500 の傾きが急なのは、基準点（8/10 と 10/10）がこの区間に指定されているため。

- **非ライド個体**: 運搬を主因子、走行速度を副因子（最大で運搬1段ぶん）とする。

  ```
  f = clamp((RunSpeed − 250) / 400, 0, 1)    # RunSpeed 250 以下で 0、650 以上で 1
  q = T + f
  S = round(P_N(q))
  ```

  | q | 0 | 1 | 2 | 3 | 8 |
  |---|---|---|---|---|---|
  | P_N | **35** | 73 | 77 | 82 | 96 |

  → 運搬0 の個体は走行速度で 35〜73 に散り、運搬1〜2 の大多数（88種）が 73〜81 に集まって**75 付近の峰**を作る。
  運搬3以上は 82〜95。非ライドの最大は 95（運搬7）。

- 全体の最小 **35**（035、運搬0・RunSpeed 250）、最大 **130**（130B ライトロット, 171, 171B, 192, 196 ゼノドラン, 201 ネプティオス, 202, 203 グランジーラ の8種）。
  平均 83.4、中央値 81。ライドの平均 102.8（中央値 101）、非ライドの平均 70.4（中央値 75）。

**S のヒストグラム**（全287種。階級は中心値±2。`#`=非ライド1種、`=`=ライド1種）:

```
 33- 37   1 (非ライド  1 ライド  0) #
 38- 42  11 (非ライド 11 ライド  0) ###########
 43- 47   7 (非ライド  7 ライド  0) #######
 48- 52  13 (非ライド 13 ライド  0) #############
 53- 57   5 (非ライド  5 ライド  0) #####
 58- 62   3 (非ライド  3 ライド  0) ###
 63- 67   5 (非ライド  5 ライド  0) #####
 68- 72   6 (非ライド  6 ライド  0) ######
 73- 77  56 (非ライド 56 ライド  0) ########################################################
 78- 82  43 (非ライド 43 ライド  0) ###########################################
 83- 87  13 (非ライド 13 ライド  0) #############
 88- 92  11 (非ライド  7 ライド  4) #######====
 93- 97  10 (非ライド  2 ライド  8) ##========
 98-102  68 (非ライド  0 ライド 68) ====================================================================
103-107  25 (非ライド  0 ライド 25) =========================
108-112   0 (非ライド  0 ライド  0) 
113-117   0 (非ライド  0 ライド  0) 
118-122   0 (非ライド  0 ライド  0) 
123-127   2 (非ライド  0 ライド  2) ==
128-132   8 (非ライド  0 ライド  8) ========
```

**二峰**: 73〜77（56種、非ライドの峰）と 98〜102（68種、ライドの峰）が最大の2階級で、その間の 88〜92・93〜97 は
11種・10種まで落ちる。75 より下の裾（35〜72、51種）は運搬0の個体が走行速度で散ったもので、どの階級も13種以下。

**期待値の全287種一覧は付録B**（実装結果をこれと突き合わせる）。

#### 3-e. 実装

- 新規 `Tools/fetch_paldb_speed.py`: 一覧と287ページを取得（1リクエストごとに0.3秒待つ、取得済みは再取得しない）、
  3-d の値を抽出して `Data/source/paldb_speed.json`（species_id をキー）に保存。生HTMLはコミットしない。
  保存する値: `kind`, `run`, `ride_sprint`, `swim_dash`, `ride_bonus`, `swim_bonus`, `pair_pct`, `transport`, `v_eff`。
- 新規 `Tools/build_habcds.py`: species.json と paldb_speed.json から 3-b〜3-d を計算して species.json に書き戻す。
  - **`legacy_had` があればそれを入力に使う**（2回実行しても結果が変わらないように）。無ければ現行の base_* を `legacy_had` に退避してから計算する。
  - 実行の最後に自己検査を行い、違反があれば非0で終了する: 6能力の合計 = BST6 式、全能力 ≥ 10、S ∈ [35, 130]、
    3-b の分類と変換後の値の整合、打ち分け型の3条件、基準点4種の S。
  - S のヒストグラム（3-d と同じ階級）と役割別の内訳を標準出力に出す（3-d の表と一致すること）。
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
  - `Order(a, b)`（確定事項 Q5）: 優先度高い順 → **`Stats.BaseSpeed`（種族値）高い順** → BST6 低い順 → 乱数。
    ランク変化・持ち物で S は動かない（現行の「種族値を見る」方針を S にも適用）。
- BST3 を計算している §2-4 の全箇所を `SpeciesData.Bst` / `BattleScheduler.Bst`（BST6）に置き換える。
  - `NpcOpponent` 84行の選出採点: BST6 は BST3 に対して傾き 27/32 なので、`bst6 * Bst6Weight`（`const float Bst6Weight = 32f / 27f;`）として
    BST 1点あたりの重みを現行並みに保つ（定数項 360 の差は全候補に共通なので順位に影響しない）。
  - `TacticalBuilder` の Profile: `Hp = H`、`Bst = BST6`。「耐久(HP+防御×1.4)」は `H + (B+D)/2 × 1.4`、「攻撃」は `max(A, C)` に。
  - `MetaScenario.StrongPool`: 並びを BST6 に。BST6 は BST3 の狭義単調増加関数なので**並びは変わらない**はず
    （変わったら報告）。
  - `Data/npc_teams.json` の `total_bst` を BST6 で**値だけ**更新（編成は変えない）。`Tools/generate_npc_teams.py` の `bst()` も BST6 に。
- UI:
  - `TeamBuildScreen` 214–217行: StatChip を `HP / 攻撃 / 防御 / 魔力 / 精神 / 俊敏 / 合計` の7つに。
  - `SpeciesPickScreen` 162–164行: `H 71 A 71 B 71 C 29 D 68 S 75` の1行に（略称表記）。
  - 「BST」表記の箇所は値が BST6 になるだけで文言は変えない。
- `Scripts/Battle/BattleTestScene.cs` の既存項目の書き換え（§5-2 の Phase 4）。コメントの「BST180」「BST265」「BST350」「BST215」は
  BST6 の値（360 / 432 / 503 / 390）に直す。
- `docs/design/pvp_spec.md` §1「行動順の決定」を新規則に書き換え（§28 で経緯を説明）。

### Phase 5 — Artifact・設計記録・通し計測

- `Tools/build_ls_payload.py`: payload に `b`(=B) `c` `dd`(=D) `sp`(=S) `role` を追加、`bst` を BST6 に、`EL` に虚。
  `Tools/artifacts/ls_head.html`/`ls_tail.html` の表示を 6 能力に（列見出し H A B C D S 合計、役割の絞り込み）。
- `python3 Tools/build_ls_payload.py` と `python3 Tools/refresh_move_editor.py` を実行し、
  `Tools/artifacts/learnset_viewer.html` を **url `188eb9b1-d49d-4c39-bf77-895a30185e99`**、
  `Tools/artifacts/move_editor.html` を **url `975b54fe-5c46-4597-ae30-26d694ac3eb2`** へ、同じ file_path で再公開。
- `docs/design/pvp_spec.md` に **§28** を追加（相性表・虚の割当規則・虚脱・HABCDS（BST6 の式・役割・俊敏の写像とヒストグラム）・
  行動順の変更と、Phase 0/5 の計測結果）。
- `--random --matches 10000 --out` を実行し、Phase 0 のベースラインと `Tools/analyze_random_by_element.py` で比較。
  **注意（CLAUDE.md）**: 種族値を変えたので、`MetaScenario`（シード `20260822UL`）で作る構築は、species の並びが同じでも
  能力が別物になる。§20〜§26 の結果と構築番号で比較しないこと。

### Phase 6 — 制限技 12 種（確定事項 Q10: 12案すべて採用・習得個体は表のまま）

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
| R4 | `rs_item_jam` グリッチボルト | 電 | 特殊 | 45/100/8 | 2マス・非接触 | `ItemJam`, **3サイクル**: 持ち物の効果なし（`Entity.Holds()` が false を返す。使い切り型は発動も消費もしない） | 185 エレパンダ, 161 オーマサンダ |
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
- **R4 の持続（注記）**: 実装は **3サイクル**で行う。R4 は持ち物が勝敗を大きく動かす（pvp_spec §17）ので最も強い可能性があり、
  **2サイクルへの短縮は Phase 6 の計測で強さを見て判断する**（判断するのは検証者。実装者は短縮せず、
  Phase 6 完了報告に「R4 を習得した2種の出場時勝率」「R4 の命中回数」「R4 命中後に相手の持ち物効果が止まった回数」を添える）。
- バランス上の注意:
  - R3（アンコール）と R1（封印）は「最後に使った技」が前提なので、1サイクル目の開幕には効かない。
  - R6 は優先度+1を持つヘッドバット・水のちから(あめ)の打ち消しにもなる。
  - R8 の範囲技は味方も巻き込む（範囲の既存仕様どおり）。
  - NPC は制限効果そのものを評価しない（威力でだけ選ぶ）ので、シミュレーションでの使用率は過小になる。

---

## 4. 判断が必要だった点（採った解釈と理由）

### 4-1. 「炎に相性不利を追加」「無に相性有利を追加」「この中間に虚」（確定: Q2）
- 候補: (a) この2つは**虚の追加によって実現する**（炎→虚 0.5 が「炎の不利」、無→虚 2.0 が「無の有利」、虚がその中間）。
  (b) 既存9属性のマスも別途変える。
- **確定: (a)**。既存81マスは変えない。§5-1 の静的指標で炎は攻撃平均 1.199→1.146、被弾平均 1.021→1.119（最良→平均並み）、
  無は攻撃平均 0.871→1.024 と、狙いの方向に動く。

### 4-2. 虚の攻撃面（確定: Q1）
- 指定は「虚→無が半減」のみで、虚が抜群を取る相手が無かった。**確定: 虚→炎 2.0 を追加**（相性鎖 無 > 虚 > 炎）。
  これで虚の攻撃平均は 1.087（無い場合 0.937 で最下位）、炎の被弾面も最良から平均並みに下がる。

### 4-3. 「闇の約40%、次に個体数が多い属性から2体」
- 候補: (a) 闇の40%≒30体＋水2体＝32体。(b) **虚の総数を闇の40%≒30体とし、うち2体を水から**（闇28＋水2）。
- 採用: **(b)**。0.4×74=29.6≒30 を「虚属性の規模」と読んだ。差は2体で結果への影響は小さいので確認事項にはしなかった。
- 闇から抜くのは単属性15・二重属性13（闇の単/複の比 38:36 にほぼ比例）。二重属性は相方属性ごとに層化して、
  炎/闇が虚へ寄りすぎない（炎/闇10種のうち4種）ようにした。炎/虚は炎技を半減するため、寄せすぎると炎の弱体化が薄まる。

### 4-4. 虚の技
- 闇の攻撃技を1対1で複製（`void_` 技 57種）。新しく威力表を設計すると verify_attack_moves の属性別規則
  （5刻みの網羅・射程上限・系統定員）を満たすよう作り直す必要があるが、闇の写しなら闇と同じ分布で規則を満たす。
- 闇の暗闇宣言は虚では虚脱宣言に変える（属性→蓄積状態異常の対応を他属性と揃える）。

### 4-5. 魔力=特殊攻撃、精神=特殊防御
- 技データは既に Physical/Special を持っているので、C/D をそれぞれ特殊の攻防に当てるのが最も自然で、技データの変更が要らない。
  攻撃ランク・防御ランクは分けない（分けると 70 以上の変化技・特性の対象を全部決め直すことになる。やらないこと参照）。
- **BST6 固定との整合**: C/D の意味づけ自体は BST6 固定と矛盾しない。矛盾するのは「主側は旧値そのまま」の部分だけで、
  それは §4-13 で再設計した。特殊型では旧攻撃 a を C の原型、旧防御 d を D の原型として扱う（3-c の表）。

### 4-6. 俊敏の使い道（確定: Q5）
- **確定: 優先度 → S の高い順 → BST6 の低い順 → 乱数**。現行の「低BST先手」による非力な側への補償は、同速のときだけ効くようになる。
- BST6 固定なので **S を高くもらった個体は他の5能力の予算が減る**（T = BST6 − S）。速さは能力の代償として払われており、
  「速い個体が一方的に得をする」ことにはならない。
- 「天候は合計種族値の高い側から発動（低い側が取る）」は S と無関係なので BST6 のまま残す。

### 4-7. 合計種族値の定義（確定: Q6・追加指示）
- **BST6（6能力合計）**にし、その値は `round(360 + (BST3 − 180) × 270/320)` で個体ごとに固定する。
  行動順のタイブレーク・天候・NPC選出・MetaScenario の並び・画面表示が BST6 を使う。
- 丸めは四捨五入（x.5 切り上げ）。BST3 が全種5の倍数なので .5 ちょうどは (BST3−180)×27/32 の小数部が 0.5 のとき
  （BST3−180 が 80 の奇数倍のとき。例 BST3=260 → 427.5 → 428）に起こる。範囲外はクランプせず同じ式を延長する。
- **learnset 系ツールの閾値**（BST180〜500 を前提にした上限表、`<310` などの規則）は旧尺度で調整されている。
  BST6 は BST3 の狭義単調増加関数なので「BST3 < 310」は「BST6 < 470」と完全に同値であり、どちらで書いても判定は変わらない。
  learnset は再生成しないので、ツールは `legacy_had` の合計（BST3）を読み続け、閾値の書き換えはしない
  （＝ゲームの BST は BST6 に統一、learnset ツールの内部尺度だけ BST3。判定結果は同一）。
- 参照元: palworld-lab.com は取得不可のため paldb.cc を使う。BST3 の合計は 287/287 種で参照元と一致するので、
  BST6 はどちらを参照しても同じ（§0-4）。

### 4-8. 役割の意味と分け方（確定: Q4、ユーザーの自由記述回答）
- ユーザー回答: 「技の習得を特性基準としつつ、ポケモン式に最高ステータスで分類」。
- 解釈: **物理/特殊/打ち分けの別（習得する技の傾向）は従来どおり特性基準の `profile_of` で決め**、
  物理型・特殊型の耐久/攻撃の細分は**主側の3能力（物理なら H・B・A、特殊なら H・D・C）のうち最も高い能力**で決める。
  耐久系（H・B・D）が最高なら耐久型、攻撃系（A・C）が単独で最高なら攻撃型。同値は耐久型（3-b）。
- 比べるのは旧HAD の h・a・d（変換前）。3-c の縮尺は主側の比を保つので、変換後の値で比べても結論は同じ（287/287 確認済み）。
- 打ち分け型は細分しない（ユーザー要求の分類対象が「物理型/特殊型」のため）。
- 耐久型と攻撃型の違いは副側の原型係数だけ（攻撃型 0.6a・0.75d、耐久型 0.4a・0.95d）。つまり
  攻撃型は副側の攻撃が残り副側の防御が薄い、耐久型は副側の攻撃を捨てて両面の防御を持つ。

### 4-9. 打ち分け型の「H×0.8 = B+D」（確定: Q3）
- **確定: 耐久の予算配分**。H・B・D で共有する耐久の予算を H:(B+D) = 1:0.8 で配る。
  文字どおり H を据え置いて B+D=0.8H とすると打ち分け型だけ防御が約4割に落ちるため。
- BST6 固定の下では、まず T（= BST6 − S）を耐久予算 Dt と攻撃予算 At に原型比（h+1.85d : 1.7a）で分け、
  Dt を H:(B+D)=1:0.8、G を B:D=r:1−r、At を A:C=r:1−r で配る（3-c ④）。B:D・A:C の許容幅は r を [0.4, 0.6] に収めることで満たす。
- 結果として打ち分け型は H が高く（平均125）、B・D が低い（平均 46・54）。BST6 平均は 447.1 で他の役割と同水準。

### 4-10. 俊敏の写像（確定: Q7・Q8、Q8 はユーザーの自由記述回答）
- ユーザー回答（Q8）: 非ライド個体は運搬を主因子・走行速度を副因子とし、S の最大130・最小35、分布は 75 と 100 付近の二峰。
- 尺度: 「x/10」は S = 13x（10/10 = 130 が最大と一致）。ベイントール 8/10 = 104、10/10 = 130。
- ライド系は paldb の実効速度を基準点で区分線形に写す（3-d）。2280→104、2500→130 は指定の基準点から決まる。
  2280 未満はライドの峰が 100 付近に来るよう 375→90、1000→98、1400→102 とした（ライドの最小 90 は非ライドの峰 75 より十分高く、
  「ライドの個体は俊敏が高い」を満たす）。ライドにも運搬Lvを +1/Lv で足す（「運搬が高いほど俊敏も高い」）。基準点4種は運搬0なので影響なし。
- 非ライド系は q = 運搬Lv + 走行速度係数 f（0〜1、運搬1段ぶんが上限）を区分線形で写す。0→35 で最小が 35 になり、
  1→73・2→77 で運搬1〜2 の88種が 75 付近に集まる。
- 水上ライドは同じ尺度（SwimDashSpeed）で評価し、ネプティオス 2500 がライトロットと同値の 130 になる（Q7）。
- 「ペア強化」= paldb の `MoveSpeed_PartnerSkill_SpecificElement`（手持ちの特定属性の数で速くなる効果）と解釈した。
  「最大に満たす」は手持ち5体中、自分以外の4体が条件を満たす状態。

### 4-11. 虚脱（確定: Q9）
- **確定: 虚技の命中による蓄積＋確定付与の専用技1種（R12 ホロウタッチ）**。
- 主状態異常スロットに入れる（蓄積の仕組みは主スロットに発症させるものなので、独立フラグにすると蓄積処理の例外が要る）。
- 「2回目の行動終了時」は対戦のサイクル終了ではなく**本人の行動の終了**で数える
  （サイクル終了で数える泥まみれ方式だと、自分が動いた後に虚脱になった場合1回の行動で解ける）。

### 4-12. 制限技（確定: Q10）
- **確定: 12種すべて採用・習得個体は表のまま**。全て攻撃技にしたのは NPC が変化技を使わないため。
- R4 グリッチボルトの 3サイクル→2サイクル短縮は Phase 6 で強さを見て判断する（6-b の注記）。

### 4-13. BST6 固定による再設計（追加指示との整合）
追加指示で BST6 が個体ごとの固定値になったため、草案の前提を次のように検証し、矛盾する点は BST6 固定を優先して作り直した。

| 草案の前提 | BST6 固定との関係 | 確定版での扱い |
|---|---|---|
| 物理型/特殊型は主側を旧 a・d のまま、副側を削る | **矛盾**。BST6 − BST3 = 180 − 0.15625(BST3−180) は 130〜180 しかなく、そこから S（35〜130）を引くと副側 C+D に 0〜145 しか残らない（グランジーラは 630−500−130 = 0）。旧値を保つと合計が BST6 を超える個体が出る | 主側の**比** h:a:d を保ち、5能力を T = BST6 − S に一様縮尺する（3-c ③） |
| 「70/70/70 同士・威力35で13」が種族対戦でも保たれる | 種族の能力が変わるので**保たれない**（モコロンは 71/71/71 になり、たまたま13のまま） | DamageCalculator の直接ベンチマーク（70/70/70→13）は式が同じなので保たれる。種族の期待値は 3-c の表で再計算した |
| 打ち分け型 E = h+1.85d を H:(B+D)=1:0.8 で配る | 予算の総量が BST6 で決まるので、E をそのまま使うと合計が合わない | E_def と E_atk の**比**で T を分けてから同じ比率で配る（3-c ④） |
| S = 40 + 10×S10（50〜140） | Q8 の 35〜130 と矛盾、かつ S が BST6 の外にあった | S を先に決めて BST6 から差し引く（3-d、3-c ②） |
| BST6 平均 500 前後（草案の (II)）/ 414（草案の (I)） | 式で決まる | **平均 448.7**（360〜630）。役割別 442〜457 |
| NPC 選出採点 `bst × 0.57` | BST6/BST3 の比は一定でない（傾き 27/32＋定数） | 傾きだけを戻す `bst6 × 32/27`（定数項は順位に無関係） |
| BST の並びに依存する箇所（MetaScenario・天候・タイブレーク） | BST6 は BST3 の狭義単調増加関数 | **並び順は変わらない**。ただし行動順は S が先に効くので、タイブレークに BST が使われる場面は同速時だけになる |

---

## 5. 検証方法

各フェーズで次を必ず実行する（CLAUDE.md）。

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
| 攻撃平均 変更後 | 1.024 | 1.146 | 1.106 | 1.075 | 1.075 | 1.007 | 1.092 | 1.091 | 1.037 | 1.087 |
| 被弾平均 現行 | 1.120 | 1.021 | 1.077 | 1.075 | 1.080 | 1.074 | 1.063 | 1.077 | 1.074 | — |
| 被弾平均 変更後 | 1.058 | 1.119 | 1.080 | 1.067 | 1.074 | 1.074 | 1.061 | 1.096 | 1.079 | 1.123 |

### 5-2. 回帰スイートに足す `[検証]`（各フェーズ。表記は `[検証] <名前>: OK (...)` の既存形式）

**Phase 1（+6 → 118）**
1. 相性表が10属性で Element と一致: `types` が10要素で enum と同順
2. 虚の6マス: 炎→虚0.5 / 闇→虚0.5 / 氷→虚2.0 / 無→虚2.0 / 虚→無0.5 / 虚→炎2.0
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
1. 287種全てが H/A/B/C/D/S を持ち全て10以上、かつ6能力の合計が 360〜630。代表3種の合計が
   タマコッコ(003)=360・リリクイン(186)=503・グランジーラ(203)=630
2. 打ち分け79種: |(B+D) − 0.8H| ≤ 1.0、B/(B+D) ∈ [0.35, 0.65]、A/(A+C) ∈ [0.40, 0.60]
3. 物理/特殊型208種の役割と能力の整合: 攻撃型 ⇔ 主側の攻撃 > max(H, 主側の防御)
   （物理: A > max(H, B)、特殊: C > max(H, D)）。内訳 物理耐久76・物理攻撃48・特殊耐久53・特殊攻撃31
4. 俊敏: 全種 35 ≤ S ≤ 130 で最小が35・最大が130、基準点 ベイントール(199) S=104、ライトロット(130B)・ゼノドラン(196)・ネプティオス(201) S=130

**Phase 4（+5 → 132）**
1. 物理技は A 対 B、特殊技は C 対 D: A≠C・B≠D の2個体で、同威力の物理技と特殊技のダメージがそれぞれの式どおり
2. ベンチマーク: (a) DamageCalculator に H70/A70/B70 Lv50 の実数値（攻75/防75）を直接渡し威力35 → **13**（式が変わっていない）。
   (b) 種族 001 モコロン同士 Lv50・突破0、STAB・相性・補正なしで威力35 → 物理 **13**・特殊 **3**（C/D が使われている）
3. 同優先度なら俊敏が高いほうが先（新規）: ブシガエル(S74) とタマコッコ(S47) の優先度0同士で、ブシガエルが先
4. BST が6能力合計: `BattleScheduler.Bst` がタマコッコ 360・ブシガエル 432
5. 種族値%の平坦加算が分類に応じた能力を見る（〇〇のきずな持ちの特殊技は `BaseSpAtk*0.08*味方数`）

既存の項目は**文言と期待値を書き換える**（項目数は変えない）:
- 「同優先度なら種族値が低いほうが先」→「**同優先度・同俊敏なら種族値が低いほうが先**」。
  タマコッコとブシガエルの `Stats.BaseSpeed` を一時的に同じ値（70）にして `Order` を呼び、タマコッコ(BST6 360)が先であることを見る。
  確認後に元の値へ戻す。
- 「優先度が種族値を上書きする」→「**優先度が俊敏を上書きする**」。遅いタマコッコに優先度1、速いブシガエルに優先度0 を与え、
  タマコッコが先であることを見る。
- 「対戦開始の天候は低種族値側が勝つ」は文言・期待値とも変えない（マグマンダー BST6 503 > ユキツネ BST6 390 で、ゆきが残る）。コメントの BST 値だけ直す。
- 盤上の8種の能力が変わるため、持ち物の倍率検証（アイアンプレート等。比率と ±1 で見ている）の括弧内の数値は変わってよい。OK/NG が変わったら報告。

**Phase 5**: スイートの項目追加なし（132）。Artifact 2つの再公開と §28 の記録。
**Phase 6（+13 → 145）**: R1〜R12 の効果1項目ずつ（「効く条件で効く」と「効かない条件で効かない」を1項目内で対にする。
pvp_spec §27 の方針）＋「各制限技の習得者が1〜2種で表どおり」。

### 5-3. データの突き合わせ

- `python3 Tools/build_habcds.py` の自己検査が通り、標準出力のヒストグラムと役割内訳が §3-d・§3-b と一致する。
- 付録B の S と species.json の `base_speed` が287種全て一致し、付録D の HABCDS・役割が287種全て一致する
  （取得元の更新で S が変わった種があれば、差分の一覧と、変更後のヒストグラムを報告。基準点4種の値が変わったら作業を止めて報告）。
- 3-c の計算例 14 種とダメージ表が表どおり。
- Phase 5 の 10,000 試合: `analyze_random_by_element.py` で、**炎の BST補正後差がベースラインより小さく、
  無の補正後差がベースラインより大きい**こと。全属性の補正後差が ±5pt 以内に収まれば良好、収まらなければ数値を報告
  （ここは調整の判断材料であって、合否の条件ではない）。未決着率が 2% を超えたら報告。
  あわせて、S の帯（〜72 / 73〜82 / 83〜99 / 100〜）ごとの出場時勝率を報告する（S を行動順に入れた影響の判断材料。合否条件ではない）。

---

## 6. やらないこと

- learnset の再生成（`generate_learnset.py` の再実行）。虚の個体の差し替えと制限技の追加以外、learnset は触らない。
- 型（`profile_of` の結果）の変更。役割の細分だけを足す。
- 虚のテンプレート特性（虚のきずな・ちから・まもり・流・派・式・おしえ）の追加。虚の天候特性・天候効果。
- 虚の地形適性（溶岩・水上移動など）。
- 攻撃/防御ランクの物理・特殊への分割、魔力・精神・俊敏のランク新設。
- 俊敏の回避・命中・迷宮のターン速度への利用。
- 迷宮側の UI（ステータス画面）の6能力表示。対戦の構築画面・種族選択画面だけ直す。
- 対戦 HUD への状態異常表示の新設（虚脱を含め、現状どの状態異常も HUD に出ていない）。
- NPC の判断に能力値・STAB・制限効果の評価を足すこと（選出採点の BST 置き換えのみ）。
- 既存の天候・持ち物・特性の数値調整。R4 の持続短縮（判断は検証者が Phase 6 の計測で行う）。
- MetaScenario の構築を以前の実験と合わせること（能力が変わるので比較不可。CLAUDE.md 参照）。
- palworld-lab.com からの取得（403 のため。paldb.cc を使う。§0-4）。旧HAD の内訳を paldb の値へ戻すこと
  （リリクイン・グランジーラは本リポジトリの値のまま）。
- `CLAUDE.md` の編集。「行動順と天候の向きは逆」の節は Phase 4 で古くなる（行動順は S が先になる）が、
  実装者は CLAUDE.md を触らず、Phase 4 完了報告で「CLAUDE.md の更新が必要」と指摘する。

---

## 7. フェーズと完了条件

| Phase | 内容 | 完了条件 |
|---|---|---|
| 0 | 計測の土台（`--random --out`、属性別集計）・ベースライン10,000試合 | build 0エラー / スイート **112**・NG0 / 集計で炎が最高・無が最低 |
| 1 | 虚属性（相性表10×10・enum・30種・虚技57・learnset差し替え・UI和名・ツール） | build 0エラー / スイート **118**・NG0 / Python 3本 ALL PASS / §5-1 の値と一致 |
| 2 | 虚脱（蓄積＋2回目の行動終了で解除） | build 0エラー / スイート **123**・NG0 |
| 3 | HABCDS データ（paldb 取得・BST6 式・役割(Q4)・配分・俊敏(Q8)・DTO） | build 0エラー / スイート **127**・NG0 / `build_habcds.py` 自己検査OK / 付録B・付録D と287種一致 / Python 3本 ALL PASS |
| 4 | HABCDS 配線（ダメージ C/D・行動順 S→BST6・BST6 全置換・UI・NPC・npc_teams） | build 0エラー / スイート **132**・NG0 / Python 3本 ALL PASS |
| 5 | Artifact 再公開・pvp_spec §28・10,000試合の再計測 | Artifact 2つが同じ URL で更新 / §28 に前後比較の表 |
| 6 | 制限技12種（R4 は3サイクルで実装し計測を報告） | build 0エラー / スイート **145**・NG0 / Python 3本 ALL PASS / Artifact 再公開 / R4 の計測値を報告 |

各フェーズは前のフェーズが完了条件を満たしてから着手し、フェーズごとに別コミットにする（コミットの指示があれば）。
項目数がこの表とずれたら、追加した `[検証]` の一覧を添えて報告する。

---

## 8. 確定事項（2026-10-25 ユーザー確認済み）

| # | 論点 | 決定 | 反映箇所 |
|---|---|---|---|
| Q1 | 虚の攻撃面 | **虚→炎 2.0 を追加**（相性鎖 無 > 虚 > 炎） | §1-a, §4-2, §5-1, §5-2 P1 |
| Q2 | 「炎に不利・無に有利」の範囲 | **虚を通すだけ。既存81マスは変えない** | §1-a, §4-1 |
| Q3 | 打ち分け型の「H×0.8 = B+D」 | **耐久の予算配分**: H:(B+D)=1:0.8、B:D は 35:65〜65:35、A:C は 40:60〜60:40 | §3-c ④, §4-9 |
| Q4 | 物理耐久/物理攻撃…の分け方 | （ユーザー自由記述）**「技の習得を特性基準としつつ、ポケモン式に最高ステータスで分類」**。型は `profile_of`、細分は主側 H・攻撃・防御の最高能力（攻撃が単独最高なら攻撃型、それ以外は耐久型） | §3-b, §4-8 |
| Q5 | 俊敏の使い道 | **優先度 → S が高い順 → BST6 が低い順 → 乱数** | Phase 4, §4-6, §5-2 P4 |
| Q6 | 合計種族値 | **6能力合計 BST6 に統一** | 全体, §4-7 |
| Q7 | 水上ライド | **同じ尺度。ネプティオス = 10/10（S=130）** | §3-d, §4-10 |
| Q8 | 非ライドの俊敏 | （ユーザー自由記述）**運搬が主因子・走行速度が副因子。S は最大130・最小35、分布は 75 と 100 付近の二峰** | §3-d, §4-10, 付録B |
| Q9 | 虚脱の付与 | **虚技の命中による蓄積＋確定付与の専用技1種** | §2-a, R12 |
| Q10 | 制限技 | **12種すべて採用・習得個体は表のまま**。R4 の2サイクル短縮は Phase 6 で強さを見て判断 | Phase 6 |
| 追加 | 合計種族値の規定 | **BST6 = round(360 + (BST3 − 180) × 270/320)**（BST3=180→360、500→630、リリクイン350→503）。参照は palworld-lab.com（取得不可のため同値の paldb.cc） | §3-c ①, §4-7, §4-13 |

---

## 実装時に判断してよい範囲

次の点は解釈が割れうるが、**どちらを選んでも付録B・付録D・§5 の期待値が変わらない**ので、実装者が決めてよい（報告不要）。
これ以外で迷ったら決めずに報告すること。

1. **役割判定に使う値**（Q4）: 旧HAD の h・a・d で判定しても、変換後の H・主側攻撃・主側防御で判定してもよい
   （287/287 種で同じ結論になることを確認済み）。ただし同値の扱いは「耐久型」で固定。
2. **役割の C# 上の型**: `SpeciesData.Role` を string のままにするか enum にするか。JSON 上の文字列は §3-a の5値で固定。
3. **`build_habcds.py` の内部構成**: 関数分割、`Fraction` か整数演算か（結果が付録D と一致すること）。
   `legacy_had` 退避のタイミング（計算前に一括か1種ずつか）。
4. **`paldb_speed.json` のキー名・並び**（§3-e の値が全て入っていること）と、取得スクリプトの再試行・待機の細部。
5. **ヒストグラムの表示形式**（`build_habcds.py` の標準出力。階級は §3-d と同じ「中心値±2」で数が一致すること）。
6. **UI の StatChip の並べ方・色**（7つ並ぶこと、略称表記の行が §Phase 4 の形であること）。
7. **[検証] の行の括弧内の補足文言**（OK/NG の判定内容は §5-2 どおり）。

逆に、次は**決めずに報告**する: BST6 の式・丸め、役割の同値の扱い、原型係数（0.6/0.75/0.4/0.95/1.85/1.7）、
整数化の優先順（H,B,D,A,C）、俊敏の区分線形の点、r のクランプ幅、行動順の比較順、制限技の数値。

---

## 付録A. 虚の30種
§3 Phase 1 1-c の表のとおり。

## 付録B. 俊敏 S の期待値（species_id:S＋ライド種別。G=地上 F=飛行 W=水上 -=非ライド）

2026-10-25 に paldb.cc から取得した値で §3-d の式を計算したもの。

```
001:75- 002:75- 003:47- 004:49- 005:74- 005B:74- 006:45- 007:76- 007B:76- 008:40- 009:74- 009B:74-
010:74- 011:40- 012:40- 013:75- 014:49- 015:49- 015B:49- 016:74- 017:76- 017B:76- 018:83- 018B:83-
019:47- 020:97G 021:98G 021B:98G 022:74- 023:74- 023B:74- 024:40- 025:73- 026:49- 027:56- 027B:56-
028:95G 029:49- 029B:49- 030:79- 030B:79- 031:74- 032:97G 032B:97G 033:101G 034:49- 034B:49- 035:35-
036:98G 037:74- 037B:74- 038:79- 038B:83- 039:40- 039B:40- 040:66- 041:101W 041B:101W 042:74- 043:73-
043B:73- 044:73- 044B:73- 045:73- 046:75- 047:75- 048:78- 048B:78- 049:68- 050:74- 051:97F 052:79-
053:40- 054:99G 054B:99G 055:75- 055B:75- 056:68- 056B:68- 057:73- 058:101G 059:74- 060:75- 061:64-
062:73- 063:98F 063B:98F 064:99F 064B:99F 065:64- 066:79- 067:80- 068:54- 069:82- 070:98G 071:102G
072:90G 072B:90G 073:77- 073B:75- 074:84- 074B:84- 075:102W 075B:97G 076:81- 076B:81- 077:75- 078:79-
078B:79- 079:79- 079B:79- 080:103F 080B:104F 081:85- 081B:85- 082:80- 083:101G 083B:101G 084:98G 084B:98G
085:81- 085B:81- 086:74- 087:98G 087B:98G 088:73- 088B:73- 089:81- 089B:81- 090:81- 091:82- 091B:85-
092:79- 092B:79- 093:101G 093B:101G 094:101G 094B:101G 095:68- 096:123F 096B:123F 097:104W 097B:104W 098:106G
098B:102G 099:73- 099B:73- 100:76- 100B:76- 101:101G 102:102G 102B:102G 103:99G 103B:99G 104:106F 105:96G
105B:96G 106:98G 107:40- 108:98G 108B:98G 109:75- 109B:75- 110:98G 111:101G 111B:101G 112:100G 112B:100G
113:89- 113B:89- 114:59- 115:80- 116:49- 116B:49- 117:99G 118:99G 118B:99G 119:79- 120:80- 121:103W
121B:100G 122:104F 122B:104F 123:100G 124:107F 124B:106F 125:49- 126:73- 127:99G 128:59- 128B:59- 129:98G
129B:98G 130:101G 130B:130G 131:45- 131B:45- 132:88- 132B:88- 133:79- 134:105G 134B:105G 135:73- 136:81-
137:100G 137B:100G 138:101G 138B:101G 139:88- 140:81- 141:73- 141B:73- 142:92G 142B:92G 143:84- 144:82-
145:81- 146:100G 147:76- 148:79- 148B:79- 149:64- 149B:64- 150:49- 151:100W 151B:100W 152:84- 153:81-
154:101G 155:82- 156:99G 157:102G 157B:100G 158:99F 159:95- 159B:95- 160:99G 161:101G 162:73- 163:45-
163B:45- 164:78- 165:75- 166:75- 167:40- 168:79- 169:103W 169B:103W 170:68- 171:130F 171B:130F 172:101F
173:68- 174:80- 175:102G 176:80- 177:107F 178:80- 179:40- 180:40- 181:75- 182:80- 183:89- 184:99G
185:103G 186:54- 186B:54- 187:88- 188:105F 188B:107F 189:102F 190:105F 191:99G 192:130F 193:80- 194:85-
195:87- 195B:87- 196:130F 197:103G 198:104G 199:104G 200:103F 200B:103F 201:130W 202:130F 203:130F
```

## 付録C. 取得元の確認記録
- `curl https://palworld-lab.com/pals/` → 403（Cloudflare "Just a moment..."）。2026-10-25 にブラウザ相当の User-Agent で再試行しても 403
- `curl https://paldb.cc/ja/Pals` → 200、カード 299 件（亜種・未実装種を含む）、287種の和名すべてを含む
- 個体ページ 287 件 → 全件 200。HP/攻撃/防御が species.json と一致 285/287。
  不一致の2種: リリクイン(186) paldb 120/120/110・本リポジトリ 105/125/120、グランジーラ(203) paldb 180/120/200・本リポジトリ 200/120/180。
  **合計（BST3）は2種とも一致**（350・500）

## 付録D. HABCDS の期待値（species_id:役割:H/A/B/C/D/S）

役割の略号: PW=物理耐久 PA=物理攻撃 SW=特殊耐久 SA=特殊攻撃 MX=打ち分け。6値の合計は §3-c ① の BST6。
§3-b〜3-d の式で、付録B と同じ取得値・Phase 1 の learnset 差し替え後の learnset から計算したもの
（打ち分け型の r は Phase 1 の差し替えで変わらない）。

```
001:PW:71/71/71/29/68/75 002:PW:71/71/71/29/68/75 003:PW:72/72/72/29/68/47 004:PW:83/77/77/31/73/49 005:SA:62/50/46/83/62/74 005B:PA:61/87/61/52/46/74
006:PW:78/78/78/32/74/45 007:MX:119/45/38/67/57/76 007B:MX:118/70/56/48/38/76 008:MX:124/51/40/76/59/40 009:PA:74/92/78/55/59/74 009B:PA:76/99/81/59/60/74
010:PW:79/74/74/30/71/74 011:PW:81/70/81/28/77/40 012:PW:79/79/79/32/76/40 013:SA:70/48/52/79/70/75 014:SW:90/29/70/73/74/49 015:MX:116/54/37/79/55/49
015B:SW:74/32/80/79/84/49 016:MX:108/47/35/69/52/74 017:PA:70/76/70/45/53/76 017B:PA:70/79/69/48/52/76 018:PW:84/84/84/34/80/83 018B:PW:87/87/87/35/82/83
019:SW:75/30/81/75/86/47 020:SW:79/27/75/66/79/97 021:SW:104/29/74/74/78/98 021B:MX:133/48/42/72/64/98 022:MX:100/76/48/51/32/74 023:MX:113/47/36/69/55/74
023B:SW:81/30/67/76/70/74 024:PA:82/92/76/55/57/40 025:SW:60/28/75/70/79/73 026:SW:81/30/77/76/81/49 027:SW:77/27/74/66/77/56 027B:SW:77/28/73/71/76/56
028:SW:76/27/63/66/67/95 029:MX:113/54/36/79/54/49 029B:MX:112/84/53/56/36/49 030:MX:104/41/34/60/50/79 030B:PW:72/61/72/25/68/79 031:PA:69/85/54/51/40/74
032:PW:85/72/72/29/68/97 032B:PW:85/72/72/29/68/97 033:SA:71/48/50/79/66/101 034:SW:102/28/87/71/91/49 034B:SW:101/30/86/75/91/49 035:MX:113/49/36/72/55/35
036:MX:122/75/59/51/39/98 037:MX:138/44/44/66/66/74 037B:PW:73/73/107/29/101/74 038:PW:80/70/70/28/67/79 038B:PW:78/78/68/31/64/83 039:PW:74/74/95/29/90/40
039B:SW:73/29/93/73/98/40 040:SW:93/21/79/52/83/66 041:SW:91/33/79/83/83/101 041B:MX:134/56/43/83/65/101 042:MX:102/73/49/50/33/74 043:MX:108/47/35/70/52/73
043B:PW:72/72/72/28/68/73 044:PW:77/67/72/27/69/73 044B:PW:82/67/72/27/69/73 045:PA:73/88/68/53/51/73 046:SW:89/29/70/74/74/75 047:SA:67/44/51/73/67/75
048:SA:68/50/51/83/68/78 048B:MX:106/54/34/80/50/78 049:MX:102/85/49/58/32/68 050:MX:109/60/35/88/53/74 051:PW:88/84/71/33/67/97 052:PW:88/88/75/35/71/79
053:PA:68/84/79/51/59/40 054:PW:66/86/86/34/82/99 054B:SW:65/34/85/85/89/99 055:PW:87/86/72/35/68/75 055B:PW:87/86/72/35/68/75 056:PA:76/94/76/57/57/68
056B:PA:75/99/75/59/56/68 057:MX:112/58/36/87/53/73 058:PW:71/80/80/32/76/101 059:PA:63/84/58/51/43/74 060:PW:77/77/77/31/74/75 061:PW:70/80/80/32/76/64
062:SA:75/48/52/80/70/73 063:SW:96/28/74/70/78/98 063B:SW:99/27/78/69/82/98 064:PA:75/95/75/57/56/99 064B:MX:115/95/55/64/37/99 065:MX:143/61/46/91/69/64
066:PW:84/84/101/34/96/79 067:PW:73/82/82/33/78/80 068:PA:84/98/93/58/70/54 069:PW:117/68/68/28/65/82 070:SW:98/29/68/71/72/98 071:SW:91/33/78/83/83/102
072:SW:80/35/84/88/88/90 072B:MX:132/57/42/85/64/90 073:MX:96/81/46/55/30/77 073B:MX:96/55/31/82/46/75 074:PA:80/85/80/51/60/84 074B:PA:80/88/79/53/60/84
075:PW:79/79/70/31/67/102 075B:PW:76/76/85/30/80/97 076:SA:81/57/54/95/72/81 076B:SA:81/57/54/95/72/81 077:SW:77/27/68/66/72/75 078:MX:113/65/36/96/55/79
078B:MX:117/95/56/64/38/79 079:MX:121/61/39/91/58/79 079B:PA:85/93/80/56/60/79 080:PA:80/99/80/60/60/103 080B:PA:84/99/80/60/60/104 081:PW:94/89/89/36/85/85
081B:SW:103/34/85/85/90/85 082:SA:81/54/60/89/80/80 083:SW:92/37/72/92/76/101 083B:MX:122/62/39/92/58/101 084:MX:128/72/62/48/41/98 084B:MX:127/51/41/75/61/98
085:PA:70/109/70/66/53/81 085B:PA:70/113/69/68/52/81 086:PW:72/76/81/31/77/74 087:PW:130/73/78/29/74/98 087B:SW:130/29/74/73/78/98 088:SA:72/49/58/82/77/73
088B:SA:72/51/57/86/76/73 089:MX:133/55/43/81/64/81 089B:MX:129/88/62/60/41/81 090:MX:127/65/40/96/61/81 091:PA:85/90/76/54/57/82 091B:PA:84/93/75/56/56/85
092:PA:69/79/69/47/51/79 092B:PA:68/78/73/46/54/79 093:SW:85/32/73/81/77/101 093B:SW:85/32/73/81/77/101 094:SW:97/37/84/92/88/101 094B:MX:137/62/44/93/66/101
095:MX:137/83/65/56/44/68 096:MX:106/59/34/88/51/123 096B:PA:82/90/66/54/50/123 097:PA:71/99/83/59/62/104 097B:PA:71/102/82/61/62/104 098:PA:76/84/68/51/51/106
098B:SA:76/54/51/89/68/102 099:SW:84/33/104/84/109/73 099B:SW:83/35/103/88/109/73 100:MX:106/54/34/81/51/76 100B:MX:105/84/50/57/34/76 101:MX:134/46/43/69/64/101
102:PW:85/76/77/31/73/102 102B:PW:83/82/83/33/78/102 103:PW:80/71/72/29/68/99 103B:PW:80/75/71/30/68/99 104:SW:74/33/89/82/94/106 105:SA:88/60/69/99/91/96
105B:SA:87/60/71/99/95/96 106:MX:136/58/44/86/65/98 107:MX:151/87/73/58/48/40 108:MX:138/50/44/74/66/98 108B:PW:100/80/84/32/80/98 109:PW:93/65/89/26/84/75
109B:PW:96/68/91/27/87/75 110:PW:105/76/97/31/92/98 111:SA:81/56/61/94/81/101 111B:PA:80/93/85/56/63/101 112:SA:89/56/51/93/68/100 112B:MX:113/63/36/94/55/100
113:MX:151/81/72/54/48/89 113B:MX:149/57/48/84/72/89 114:PW:102/92/92/37/88/59 115:PA:96/100/79/60/59/80 116:PW:105/86/96/34/91/49 116B:PW:110/86/95/34/91/49
117:SA:64/54/52/90/69/99 118:SW:96/28/74/69/78/99 118B:SW:95/29/74/74/78/99 119:MX:131/64/42/95/63/79 120:PA:89/101/89/61/67/80 121:MX:135/63/43/94/65/103
121B:PW:106/106/81/42/77/100 122:PW:98/85/85/34/81/104 122B:PW:102/85/85/34/81/104 123:PW:84/79/70/32/67/100 124:SW:86/33/77/81/81/107 124B:SW:86/34/77/86/81/106
125:SW:92/37/77/92/81/49 126:MX:124/44/40/66/59/73 127:MX:113/72/54/49/36/99 128:MX:112/64/36/94/54/59 128B:PA:67/101/77/61/58/59 129:PW:88/84/96/34/91/98
129B:PW:86/83/102/33/97/98 130:PW:91/83/83/33/79/101 130B:SW:84/32/72/80/76/130 131:SW:91/38/104/95/109/45 131B:SW:90/40/103/100/109/45 132:MX:136/55/44/82/65/88
132B:MX:135/85/65/58/43/88 133:MX:120/47/38/69/58/79 134:PW:117/67/83/27/79/105 134B:PW:115/65/90/26/86/105 135:PA:92/109/79/66/59/73 136:PA:78/83/78/50/58/81
137:SA:78/59/70/98/94/100 137B:SA:81/60/73/101/97/100 138:SW:87/33/93/83/98/101 138B:MX:143/57/46/84/68/101 139:MX:134/104/64/70/43/88 140:MX:134/64/43/96/64/81
141:PA:81/99/81/59/60/73 141B:PA:80/102/80/62/60/73 142:PW:75/79/107/32/102/92 142B:PW:75/79/110/31/104/92 143:SW:95/35/82/87/87/84 144:SW:51/20/105/51/110/82
145:SA:78/65/55/108/74/81 146:MX:144/61/46/91/70/100 147:MX:137/82/66/56/44/76 148:MX:139/54/45/81/67/79 148B:PW:96/83/92/33/87/79 149:PA:82/87/82/52/61/64
149B:PW:81/86/86/34/81/64 150:PA:89/99/94/59/71/49 151:SA:84/56/66/93/88/100 151B:SA:84/57/66/96/88/100 152:SA:78/60/58/99/78/84 153:MX:131/64/42/94/62/81
154:MX:139/86/67/58/44/101 155:MX:127/57/41/85/61/82 156:PW:96/79/84/32/80/99 157:PW:99/90/82/36/78/102 157B:PW:99/94/82/38/78/100 158:PW:78/97/97/39/93/99
159:SW:82/34/100/86/106/95 159B:SW:82/36/100/90/105/95 160:SW:97/35/88/88/92/99 161:MX:142/63/45/93/68/101 162:MX:108/78/52/52/35/73 163:MX:119/50/38/75/58/45
163B:PW:70/76/87/30/82/45 164:PA:72/77/62/46/46/78 165:PW:69/73/78/29/74/75 166:PA:67/81/77/49/57/75 167:SW:89/33/74/84/78/40 168:SA:66/48/57/80/76/79
169:SW:78/31/70/77/73/103 169B:MX:112/53/36/79/53/103 170:MX:139/88/67/59/44/68 171:MX:130/60/42/88/62/130 171B:PA:83/94/87/57/65/130 172:PA:66/75/71/45/53/101
173:PW:81/81/85/32/81/68 174:PA:93/105/88/63/66/80 175:SW:102/36/90/91/95/102 176:SA:87/57/65/95/86/80 177:SA:89/56/57/93/76/107 178:MX:120/51/38/76/58/80
179:MX:144/92/69/62/46/40 180:MX:138/64/44/96/67/40 181:PA:79/89/60/54/45/75 182:PW:92/96/96/39/92/80 183:PA:87/100/83/60/63/89 184:PW:80/95/107/38/101/99
185:SW:98/34/81/85/86/103 186:SA:92/65/78/109/105/54 186B:SA:102/64/76/106/101/54 187:MX:143/69/46/102/68/88 188:MX:133/85/64/57/43/105 188B:MX:135/57/43/84/65/107
189:PW:91/91/107/37/101/102 190:PW:103/92/88/37/83/105 191:PW:110/102/95/41/90/99 192:PW:102/99/88/39/83/130 193:SW:118/40/88/101/93/80 194:SA:102/66/67/110/90/85
195:SA:97/73/61/121/81/87 195B:MX:131/79/42/118/63/87 196:MX:137/85/66/58/44/130 197:MX:142/67/45/100/68/103 198:PW:98/90/109/36/104/104 199:PA:99/111/92/66/69/104
200:PW:109/109/93/43/89/103 200B:PW:106/106/103/43/97/103 201:SA:74/62/67/103/89/130 202:PA:83/101/86/60/65/130 203:SW:139/33/119/84/125/130
```
