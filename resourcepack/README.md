# レイド個体のリソースパック

`raid_model_spec.md` に沿ってモデルを置く場所である。**いまは較正用の立方体だけが入っている。**

## 入れ方

```powershell
# フォルダのまま置ける（zip でなくてよい）
Copy-Item -Recurse -Force E:\raid-dev\resourcepack E:\raid-test-pack
```

> **置き場所はランチャーによって違う。** 公式ランチャーの既定は
> `%APPDATA%\.minecraft\resourcepacks` だが、MultiMC / Prism などは
> **インスタンスごと**にゲームディレクトリを持つ。
>
> | ランチャー | resourcepacks の場所 |
> |---|---|
> | 公式（既定） | `%APPDATA%\.minecraft\resourcepacks` |
> | MultiMC / Prism | `<インスタンス>\.minecraft\resourcepacks` |
>
> このプロジェクトの検証環境は MultiMC であり、実際の場所は
> `C:\Users\junem\AppData\Roaming\.minecraft\mods\MultiMC\instances\1.21.4\.minecraft\resourcepacks` である。

> **Minecraft はシンボリックリンクのパックを既定で拒否する。** 1.20 以降のセキュリティ機能で、
> リンク先が許可リストに無いパックは**一覧に出ない**（エラーも出ない）。
> リンクを使いたい場合は、インスタンスの `.minecraft` 直下の `allowed_symlinks.txt` に
> `[glob]E:/raid-dev/**` を足して**ゲームを再起動**する。区切りは `/` である。

### 方法A: 同期スクリプト（推奨）

`git pull` のあとに実行すると、実体をコピーして最新にする。

```powershell
powershell -ExecutionPolicy Bypass -File E:\raid-dev\resourcepack\sync-pack.ps1
```

置き場所が違う環境では、スクリプト冒頭の `$packs` を書き換える。

### 方法B: 手で置く

シンボリックリンクを張ると `git pull` がそのまま反映されるが、上記の
`allowed_symlinks.txt` の設定が必要である。

```powershell
# 管理者権限の PowerShell
$packs = "C:\Users\junem\AppData\Roaming\.minecraft\mods\MultiMC\instances\1.21.4\.minecraft\resourcepacks"
New-Item -ItemType SymbolicLink -Path "$packs\raid-dev" -Target E:\raid-dev\resourcepack
Test-Path "$packs\raid-dev\pack.mcmeta"   # True になれば置けている
```

リンクが作れない場合は実体をコピーする（`git pull` のたびにコピーし直す）。

```powershell
Copy-Item -Recurse -Force E:\raid-dev\resourcepack "$packs\raid-dev"
```

置いたあと、ゲーム内の**リソースパック画面で「使用可能」から「選択済み」へ移す**。
リンクを張った場合は `F3 + T` で再読み込みできる（ゲームの再起動は不要）。

## 効いていないときの切り分け

```
/give @s minecraft:paper[minecraft:custom_model_data={floats:[9000]}] 1
```

手に持った紙が色付きの立方体になるかで、どちら側の問題か分かる。

| 結果 | 意味 |
|---|---|
| 立方体になる | パックは効いている。プラグインの `custom_model_data` の書き込み側の問題 |
| 紙のまま | パックが読み込まれていない（置き場所・選択済みか・バージョン警告を確認） |
| 紫と黒の欠損モデル | パックは効いているが**モデルの JSON が壊れている** |

## 中身

| ファイル | 役割 |
|---|---|
| `pack.mcmeta` | パックの宣言。`pack_format 46` は 1.21.4 |
| `assets/minecraft/items/paper.json` | `custom_model_data` からモデルへの振り分け（**1.21.4 の書き方**） |
| `assets/minecraft/models/knight/calibration.json` | 較正用の 16 単位の立方体 |

## 較正用の立方体

`custom_model_data 9000`。面ごとに色が違うので、向きと反転が読める。

| 面 | 色 |
|---|---|
| 上 | 黄緑 |
| 下 | 赤 |
| 北（−Z） | 青 |
| 南（+Z） | 黄 |
| 西（−X） | 白 |
| 東（+X） | 黒 |

さらに**モデル座標 (0,0,0) の角に、3単位のマゼンタの小さな立方体**を付けてある。
これが立方体のどの角に付いているかで、モデルの原点がどこかが分かる。

使い方は `raid_model_spec.md` §7、または `local_test_setup.md` の較正の節を見ること。

## 本番のモデルを描くとき

1. `assets/minecraft/models/knight/` に部位ごとの JSON を置く
2. `assets/minecraft/items/paper.json` の `entries` に `threshold`（= 部位のID）を**昇順で**足す
3. 部位のIDと寸法は `raid_model_spec.md` §4 の表にある
