# チャート1のダンジョンの敵レベルを 1→3 にエスカレート — 仕様書（確定）

- ユーザー指示（2026-11-02）: 「ダメージが全く通らないので、ダンジョンの敵のレベルを1からエスカレーションする形で最後のフロアのレベルでレベル3となるように調整してください」。確認の回答: **適用はチャート1のダンジョン（ビーチのどうくつ、`d01_c1`、5階）だけ**、**最終フロアのボスも Lv3**。
- ブランチ `claude/mystery-dungeon-roguelike-egnzjt`。実装者は git を使わない（検証者がコミット）。ビルドは `dotnet build MysteryDungeonRoguelike.csproj` を先に。回帰は NG 行数（現在 258 項目、6回連続）。
- 関係箇所: `Scripts/Hub/DungeonDestination.cs`（`ConfigFor`: `WildLevel = Math.Max(2, bossLv + db.LevelCurve.WildOffset)`、`Spec`: ボスLv＝データ値優先、0なら曲線）、`Scripts/Dungeon/DungeonConfig.cs`、`Scripts/Dungeon/FloorController.cs`（`SpawnEnemyAt`: 野生Lv＝`max(2, WildLevel − (MaxFloors−1−階))`）、`Scripts/Story/StoryDatabase.cs`（`BossLevelFor`）、`Data/story/dungeons.json`（`d01_c1`）、`Data/story/level_curve.json`、既存のストーリー系テスト（`Scripts/Battle/StoryDungeonTests.cs` など。`d01_c1` のボスLv・野生Lvの期待値）、`Tools/verify_story.py`。

## 1. 変更内容
1. **`d01_c1` の野生の敵**: 階 f（1〜最終階 F=5）で `Lv = 1 + floor((f−1) × 2 / (F−1))`＝ 1階=1、2階=1、3階=2、4階=2、5階（最終階）=3。他のダンジョンの計算（`max(2, …)` と `WildOffset`）は変えない。
2. **`d01_c1` のボス（ヒグルミ）**: Lv3（`dungeons.json` のボスの `level` を 3 に。曲線の `first_level=5` は変えない＝他の使い道に影響させない）。
3. 実装は、ダンジョンのデータに任意項目 `wild_level_range: [最初の階のLv, 最終階のLv]`（階ごとに線形・端数切り捨て）を足し、`DungeonDefs`→`DungeonConfig`→`FloorController.SpawnEnemyAt` で、指定があればそれを優先する形にする（今後ほかのダンジョンにも同じ指定ができる）。`d01_c1` にだけ `[1, 3]` を書く。指定がなければ現行どおり。
4. 既存の期待値（`d01_c1` のボスLv5・野生Lv2、`verify_story.py` の「ボスLv＝曲線」の検査）を、**この1件だけ例外**として直す。他のダンジョン・曲線の検査は変えない。

## 2. 調査（「ダメージが全く通らない」の原因確認。修正は §3 の範囲だけ）
- ヘッドレスで、主人公（プレイアブル16種それぞれ）と相棒を Lv5・初期の技で、Lv1・Lv2・Lv3 の野生（`d01_c1` の出現プールの種族）とボス（ヒグルミ Lv3）に対して攻撃させ、**通常攻撃（体当たり）と各技のダメージ値**、敵を倒すまでの回数を測る（`DamageCalculator`・`AttackAction` の実経路を使う。結果は `docs/design/pvp_spec.md` に節を足さず、報告に表で載せる）。
- **ダメージ0（または極端に少ない）の組合せ**が出たら、原因（技の威力0/状態技のみ、命中、種族相性、能力値の式、ボスの `hp_mult`/`atk_mult`、`StoryBoss` の無敵条件など）を特定して報告する。原因が「敵のレベル設定」のデータ・式なら §1 の範囲で直す。**戦闘計算式そのものは変えない**。それ以外が原因なら直さずに報告（修正方針の提案を添える）。

## 3. 検証
- ビルド 0 エラー、回帰 NG 0 を6回連続、既存 Python 検証と `Tools/verify_story.py` が ALL PASS。
- 追加チェック: `d01_c1` の野生Lvが階ごとに 1,1,2,2,3、ボス Lv3、他のダンジョン（例 `d02_c2`）の野生Lv・ボスLvが変わらない、`wild_level_range` なしの挙動が不変。
- 報告に、§2 の測定表と結論を載せる。

## 4. やらないこと
チャート2以降の敵レベル・曲線の変更、戦闘計算式の変更、主人公・相棒の初期レベル（5）の変更。
