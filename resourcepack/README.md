# レイド個体のリソースパック

`raid_model_spec.md` に沿ってモデルを置く場所である。

**見た目を作る作業は `PAINTING.md` を見ること。**箱の形・向き・UV は骨格データから生成してあるので、
Windows のペイントで PNG を塗り替えれば見た目が変わる。

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

> **`.ps1` は BOM 付き UTF-8 で保存する。** BOM が無いと Windows PowerShell 5.1 が
> ANSI（CP932）として読み、日本語のコメントが化けて**改行ごと飲み込まれ**、
> 構文が壊れる。`.gitattributes` で `*.ps1` を変換対象外にしてある。

> **リンクを消すときは `Remove-Item -Recurse` を使わない。** ディレクトリの
> シンボリックリンクに対して**リンク先の中身まで消すことがある**。
> スクリプトは `[System.IO.Directory]::Delete($path, $false)` でリンクだけを消している。

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

| ファイル | 役割 | 生成 |
|---|---|---|
| `pack.mcmeta` | パックの宣言。`pack_format 46` は 1.21.4 | 手書き |
| `assets/minecraft/items/paper.json` | `custom_model_data` からモデルへの振り分け | **自動**（上書き） |
| `assets/minecraft/models/knight/p1/*.json` | 第一形態の部位（13件） | **自動**（上書き） |
| `assets/minecraft/models/knight/p2/*.json` | 第二形態の部位（16件） | **自動**（上書き） |
| `assets/minecraft/textures/knight/*.png` | 塗り絵（11枚）。**ここを塗る** | 自動（無いときだけ置く） |
| `templates/*.png` | 塗り絵の原本（11枚）。戻すとき写す | **自動**（上書き） |
| `assets/minecraft/models/knight/calibration.json` | 較正用の 16 単位の立方体 | 手書き |

**自動**は骨格データから生成している。

```sh
./core/generate-pack.sh
```

寸法表を手で写さないため、**当たり判定と見た目の寸法が必ず一致する**。骨格の寸法を変えたら
生成し直す。ただし**生成し直すと手で描いた形は失われる**ので、差分を見て必要な部位だけ描き直すこと。

## 描き始める

**塗る手順は `PAINTING.md` にある。**要点だけ:

- 塗るのは `assets/minecraft/textures/knight/*.png`（11枚・各 128×128）
- 1枚に6つの枠があり、左上の文字が面を示す（`F` 前 / `B` 後 / `R` 右 / `L` 左 / `U` 上 / `D` 下）
- 濃い灰色の余白はどの面にも貼られない
- 画像の大きさとファイル名は変えない。透明は使わない

生成された JSON は **Blockbench でそのまま開ける**。形も UV もそこで直せるが、
**生成し直すと手で直した形は失われる**（塗った PNG は消えない）。

描いたモデルを実際に使うには、ゲーム内で切り替える。**ビルドし直さなくてよい。**

```
F3 + T                  # リソースパックを読み直す
/raid model authored    # 描いたモデルで出し直す
/raid model vanilla     # バニラの素材と見比べる
```

サーバーを起動し直すとバニラの素材に戻る（既定値）。切り分けのため、
コマンド1つで比較できる状態を残してある。

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

