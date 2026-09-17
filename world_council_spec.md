# 世界協議 要件定義書

`minecraft_server_spec.md` §23「未着手の領域」から参照される、新設のシステムの要件定義書。
`rail_infra_spec.md` と同じ位置づけ（マスター仕様書から参照される個別ドキュメント）。
統合レイヤー（本書の対象）は `core`・`plugin` とも実装済み（§7）。BLOCK CONQUEST 本体は
仕様（アーティファクト）のみで実装は未着手（§2）。

---

## 1. システム概要

「世界協議」は、**BLOCK CONQUEST**（別途アーティファクトとして仕様確定済みのボードゲーム。
4チーム×2名・8×8盤面・カード制）を用いた国家対抗イベントである。世界政府が徴収した
キャッシュを、成績上位の国家へ「還付」するという名目で、上位国家の国庫（非課税）に
資金を加える。共和制における国家間競争がやや弱い、という課題認識から発案された
（現実にどのような利益をもたらすかは構想段階であり、還付額を含め運用しながら調整する）。

本書が扱うのは、この対抗戦を**既存の国家システムへ接続する統合レイヤー**である。

- 国家の参加資格判定（属国は宗主国に統合する）
- 専用ワールドへの移送とインベントリの退避・復元
- 専用ワールド内の安全策（ブロック・エンティティ・アイテム操作の禁止、体力・空腹の無効化）
- 日照サイクル制御（ラウンド間の演出）
- 上位国家への還付（固定額表・非課税）

BLOCK CONQUEST 自体のゲームロジック（盤面生成・21種カード・進行・UI、`data/bg/` 以下の
mcfunction データパック）は対象外——**ユーザーへ確認して、今回は着手しないことに決定した**
（§2）。

## 2. 実装範囲の決定（ユーザーへ確認して決定）

BLOCK CONQUEST は既にアーティファクトとして詳細な仕様（盤面・カード21種・UI・進行・
データ構造・実装チェックリストまで）が確定している。これをこのセッションでも実装するか、
統合レイヤーのみにとどめるかをユーザーへ確認し、**統合レイヤーのみ**に決定した。

理由: BLOCK CONQUEST は `core`/`plugin`（Java、Bukkit/Paper）とは技術的に独立した
バニラデータパック（`mcfunction`・`loot_table`・`item_modifier`）であり、非常に大規模
（実装チェックリストだけで7フェーズ）。今回の依頼の主眼は「国家システムとの接続」で
あったため、切り分けて後続作業とした。

この決定の帰結として、本書のいくつかの機能は「効果の実体はあるが、呼び出し元
（BLOCK CONQUEST側のゲーム進行）が無い」状態になる。該当箇所は各節で明記する。

## 3. 参加資格・参加登録

### 3.1 資格

参加できる国家はランク7以上に限る（属国を保有できる資格＝`Vassalage.limit` のリーダー枠
開放基準と同じ。ユーザーへ確認して決定）。開催は管理者トリガー（ユーザーへ確認して決定）。

### 3.2 属国の扱い

**1か国の扱いは宗主国であり、属国も同一の国家に換算する。** 属国単独では参加登録できず、
属国のプレイヤーが代表者になる場合は宗主国の枠に含めて扱う
（`WorldCouncilEligibility.effectiveNation`）。

### 3.3 参加国家数・参加人数

| 項目 | 値 |
|---|---|
| 最大参加国家数 | 4か国 |
| 1か国あたりの代表者数 | 2名（固定） |
| 最大参加人数 | 8名 |

代表者数を2名固定にしたのは、BLOCK CONQUEST が「4チーム×2名固定」で盤面・カード枚数・
制限時間まで組まれているためである（アーティファクト仕様 §1・§7.2。ユーザーへ確認せず、
ゲーム側の確定仕様に合わせた既定値として採用）。

### 3.4 登録の流れ（現状は管理者コマンドによる手動登録）

ライブな国家ランク管理システムがまだ無いため（`rail_infra_spec.md` §2 と同じ制約——
`core` にはランクの計算式があるが、Bukkit 側でランクを実際に保持するプラグインがまだ無い）、
`/worldcouncil register <国家> <rank>` でランクを管理者が申告する運用とする
（`rail_infra_spec.md` の `/rail admin setnation` と同じ考え方）。本物の国家プラグインが
できたら、ここは自動判定に置き換わる想定。

## 4. 専用ワールドへの移送・復帰

### 4.1 専用ワールド

BLOCK CONQUEST の会場は専用ワールド（フォルダ名 `worldcouncil`）に置く。**想定される方法
（BLOCK CONQUEST のカード・進行コマンドを通した操作。今回は未実装）以外でのブロック・
エンティティ・アイテムの操作はできない**（ユーザーの決定。§5）。

盤面の実体（BLOCK CONQUEST データパック）が無いため、現状の入場地点はワールドスポーン
地点を使う（`WorldCouncilArena.entryPoint`）。盤面が用意され次第、draft（アーティファクト
仕様 §7.4）で決まる開始マスへの降下に置き換わる。

### 4.2 インベントリの退避・復元

- 専用ワールドへ移送する際、国家ワールド（オーバーワールド）のインベントリ・立ち位置・
  ゲームモードを退避してから、インベントリを空にして入場する
  （BLOCK CONQUEST はホットバーを手札等に使う設計のため。アーティファクト仕様 §15.3）
- 専用ワールドから国家ワールドへ戻る際、退避しておいたインベントリ・立ち位置・
  ゲームモードを復元する

（`WorldCouncilArena.enter` / `exit`）。**位置とゲームモードの退避・復元は、ユーザーの
要求文にはインベントリしか明記されていないが、「行き来する」以上は自然な付随動作として
実装した（ユーザーへ確認せず、慣例的な既定値として採用）。**

### 4.3 体力・空腹度

専用ワールド滞在中はプレイヤーは体力・空腹度を消費しない。**完全無効化**（ユーザーへ確認して
決定。「消費のみ止める」案は採らなかった）:

- 入場時に体力を全回復・空腹度を満腹（20）に固定し、`Entity#setInvulnerable(true)` を
  かける（一次防御）
- `EntityDamageEvent` そのものをキャンセルする二重の保険を重ねる
  （`WorldCouncilGuard.onDamage`）
- `FoodLevelChangeEvent` をキャンセルし、空腹度の減少を防ぐ（`WorldCouncilGuard.onFoodChange`）

### 4.4 ブロック・エンティティ・アイテム操作の禁止

`WorldCouncilGuard` が専用ワールド内の `BlockBreakEvent` / `BlockPlaceEvent` /
`PlayerDropItemEvent` を無条件キャンセルする（`RaidArena` の設置・破壊禁止と同じ考え方）。

**未対応として残る範囲**: コンテナ経由の操作（`InventoryClickEvent` 等）は対象にしていない
——盤面の実体が無く、現状の専用ワールドにはコンテナが存在しないため、優先度を下げた
（BLOCK CONQUEST 側の実装時に、必要なら追加する）。

## 5. 日照サイクル制御

「すべてのプレイヤーの行動が終わると、2秒のあいだに1日サイクルが進み、ゲーム内時間で
12:00（正午。Minecraft の `time` 換算で 6000）で停止する」という要求を、効果本体として
`WorldCouncilDayCycle.advanceToNoon` に実装した。

**「行動が終わった」の判定はデータパック側（BLOCK CONQUEST の8人の手番が一巡したという
状態）が必要であり、今回の統合レイヤーの対象外。** 効果（2秒かけて次の正午まで滑らかに
時刻を進め、到達したら `doDaylightCycle` を止めて固定する）だけを用意し、
`/worldcouncil advanceday` から手動で呼べるようにした。BLOCK CONQUEST 側の進行が実装され
たら、そちらの「1巡終了」処理からこの関数を呼ぶ想定。

## 6. 還付金

### 6.1 算出方法（固定額表・ユーザーへ確認して決定）

実際の世界政府の徴収総額（属国上納の世界政府取り分・援助金の償却・外交準備高の月次減価
など）とは連動させず、**固定額表**で運用する（調整のしやすさを優先）。

| 順位 | 還付額（exp） |
|---|---|
| 1位 | 40,000 |
| 2位 | 24,000 |
| 3位 | 12,000 |
| 4位 | 4,000 |

**金額は暫定値。** 現実にどのような利益をもたらすかは構想段階であり、運用しながら調整する
ことを前提とする（`WorldCouncilPayout.AMOUNTS`）。同順位（共同優勝など）は、その順位の額を
それぞれが満額受け取る。

### 6.2 非課税の意味

「非課税」とは、外交準備高を経由せず、援助金（`NationalAccounts` §7.3）のような3%の償却も
発生させずに、**全額を国庫へ計上する**ことを指す。`NationalAccounts.donate`（国庫にのみ入る
経路）をそのまま使う（`WorldCouncilPayout.credit`）。

### 6.3 順位の決定（現状は管理者コマンドによる手動入力）

BLOCK CONQUEST の最終得点データが無いため、`core` には勝敗判定ロジック
（`WorldCouncilRanking`。BLOCK CONQUEST §7.5 の「合計得点→得点差→共同優勝」をそのまま
移植）はあるが、現状の `/worldcouncil finish` は順位を管理者が直接入力する形になっている。
BLOCK CONQUEST 側の得点集計が実装されたら、`WorldCouncilRanking.rank` へ置き換わる想定。

## 7. 実装の状態

### 7.1 国家データの置き場所（ユーザーへ確認して決定）

「世界協議」の還付金を入れる国庫データをどこに持たせるかが論点になった
（`rail_infra_spec.md` で作った「鉄道専用の最小限の代用品」しか、Bukkit 側に国家の国庫を
持つ実装が無かったため）。**`rail_nations` テーブルを rail 専用から昇格させ、両モジュールが
共有する台帳に格上げする**ことに決定した。

- `NationLedger`（`plugin/src/main/java/jp/mcserver/plugin/nation/`、新規）が
  `nation.db` を開き、国庫・外交準備高・プレイヤー所属・宗主国関係・同盟関係を持つ
  （旧 `RailDatabase` の「国家代用」節をそのまま移設。テーブルスキーマは変更していない）
- `RailDatabase` は鉄道固有のデータ（`rail_data` / `nation_monthly_data` /
  `station_data` / `rail_claims`）だけを持ち、国家関連のメソッドは `NationLedger` へ委譲する
  （呼び出し側 `RailCommand` 等の公開APIは変更していない）
- `RailModule` が `NationLedger` を開いて所有し、`WorldCouncilModule` はそれを共有で借りる
  （`RailModule.ledger()`）。close するのは `RailModule` のみ

**この変更により、既存の `rail.db` に入っていた `rail_nations` / `rail_nation_players` /
`rail_alliances` のデータは新しい `nation.db` には引き継がれない**（ファイルが分かれる
ため）。実機でのテストデータがまだ無い前提での判断であり、もし既存データがある場合は
手動移行が必要（ユーザーへ確認せず、この時点でテストデータの引き継ぎ価値は無いと判断した）。

### 7.2 `core`（`core/src/main/java/jp/mcserver/core/worldcouncil/`）

| クラス | 内容 |
|---|---|
| `WorldCouncilEligibility` | 参加資格（rank7以上）・実効国家名（属国→宗主国） |
| `WorldCouncilRoster` | 参加登録（最大4か国×2名、重複拒否） |
| `WorldCouncilRanking` | 最終順位（BLOCK CONQUEST §7.5 の判定を移植） |
| `WorldCouncilPayout` | 還付金の固定額表・非課税の国庫計上 |

`CoreTests.worldCouncil()` で検証済み（22件）。合計 946 件、全件成功。

### 7.3 `plugin`（`plugin/src/main/java/jp/mcserver/plugin/worldcouncil/`）

| クラス | 内容 |
|---|---|
| `WorldCouncilArena` | 専用ワールドの管理、入場・退場（インベントリ退避・復元） |
| `WorldCouncilGuard` | ブロック・エンティティ・アイテム操作の禁止、体力・空腹の無効化 |
| `WorldCouncilDayCycle` | 日照サイクルの2秒アニメーションと正午固定 |
| `WorldCouncilModule` | 配線（`RaidPlugin` からは `enable`/`disable` のみ呼ぶ） |
| `WorldCouncilCommand` | `/worldcouncil` コマンド一式 |

`plugin/build.gradle`・`plugin.yml` の変更は無し（sqlite-jdbc は rail 導入時に追加済み）。
`plugin.yml` に `worldcouncil` コマンドと `worldcouncil.admin` 権限（既定 op）を追加した。

Bukkit スタブに対するコンパイル確認: 新規ファイルに現れた「実」エラーは、いずれも
`CommandExecutor#onCommand` と `BukkitRunnable#run` の `@Override`（スタブに両インター
フェースが無いための既知のノイズ。`RailCommand.java`・`RaidPlugin.java` と同じパターン）
のみ。それ以外は全て `cannot find symbol` 等の既知ノイズで、想定外の実エラーは無かった。

## 8. コマンド仕様

| コマンド | 権限 | 内容 |
|---|---|---|
| `/worldcouncil register <国家> <rank>` | `worldcouncil.admin` | 国家の参加登録 |
| `/worldcouncil addrep <国家> <プレイヤー>` | `worldcouncil.admin` | 代表者の追加（1か国2名まで） |
| `/worldcouncil status` | `worldcouncil.admin` | 登録状況の確認 |
| `/worldcouncil begin` | `worldcouncil.admin` | 4か国×2名が揃った登録者を専用ワールドへ移送 |
| `/worldcouncil advanceday` | `worldcouncil.admin` | 日照サイクルを2秒かけて次の正午まで進める |
| `/worldcouncil finish <1位> <2位> <3位> <4位>` | `worldcouncil.admin` | 還付の実行、全代表者の復帰、登録のクリア |

## 9. 未確認・未実装の領域

- **BLOCK CONQUEST 本体**（盤面生成・21種カードの loot table・UI・進行・データパック一式）
  は仕様（アーティファクト）のみで、実装は未着手（§2）
- 「行動が終わった」の検知（§5）と最終得点の集計（§6.3）は、BLOCK CONQUEST 側の実装が
  無いと自動化できない。現状は管理者コマンドによる手動運用
- ライブな国家ランク管理が無いため、`/worldcouncil register` のランクは管理者の申告に頼る
  （§3.4）
- コンテナ経由のアイテム操作は未対応（§4.4）
- 実機でのビルド・起動・プレイテストはいずれも未確認（今後のマルチ検証まで保留）
