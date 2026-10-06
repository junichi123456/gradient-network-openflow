# Minecraft マルチサーバー（国家・経済・レイド）

国家運営と経済を軸にした Minecraft マルチサーバーの設計仕様と、その実装（Paper プラグイン）を置くリポジトリ。

- 対象: Paper 1.26.3（`paper-api 26.3.build.9-alpha`）、Java 25
- 仕様の本体は `minecraft_server_spec.md`。節番号（§n）は文書どうしで相互参照している

## 構成

| 場所 | 中身 |
|---|---|
| `core/` | Minecraft に依存しない計算の層（外部依存なし）。国家（ランク・領土・同盟・属国・制裁・戦争・統一・継承）、経済（国庫・市場・国債）、レイド（個体の定義・モーション・パリイ・スピアの補正）、競馬、地下鉄インフラ、世界協議 |
| `plugin/` | サーバーへ入れるプラグイン（`raid-plugin.jar`）。レイド、地下鉄インフラ（`/rail`）、世界協議（`/worldcouncil`）、競走馬（`/horse`）、村人の取引制限、『消滅の呪い』の自動付与など |
| `resourcepack/` | レイド個体のモデルと塗り絵。`core` の `ModelPack` が骨格から生成する |
| `docs/raid-boss/` | レイドボスの構想（前提・ダメージの予算・スピアの扱い・決定事項） |

## ビルドと検証

```sh
# core の検証（javac だけで動く。Gradle は不要）
./core/run-tests.sh

# プラグインの jar（plugin/build/libs/raid-plugin.jar）
gradle :plugin:jar
```

実機での立て方と確認項目は `local_test_setup.md` にある。

## 文書

| 文書 | 内容 |
|---|---|
| `minecraft_server_spec.md` | サーバー全体の設計仕様 |
| `raid_species.md` | レイド個体ごとの仕様（騎士型・虚刃の衛士） |
| `raid_model_spec.md` | レイド個体のモデル寸法規定（リソースパック） |
| `docs/raid-boss/CONCEPT.md` | レイドボスの構想と前提 |
| `rail_infra_spec.md` | 地下鉄インフラの要件定義 |
| `world_council_spec.md` | 世界協議（国家対抗イベント）の要件定義 |
| `implementation_feasibility.md` | 既存プラグインで足りる範囲と自作が要る範囲の切り分け |
| `capacity_plan.md` | 60人同時接続の負荷見積り |
| `mod_rulings.md` | クライアント Mod の裁定判例集 |
| `local_test_setup.md` | 実機テストの手順と確認項目 |
| `project_status.md` | 進捗と残っている作業 |
