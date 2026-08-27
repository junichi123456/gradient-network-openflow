# ローカル検証の手順（Windows）

騎士型（§12.7）を手元のPCだけで動かすまでの手順。**インターネット公開は不要**で、同じPCの中だけで完結する。

> **シングルプレイでは動かない。** プラグインが動くのは Paper サーバーのみである。手元にサーバーを立て、同じPCのクライアントから `localhost` に接続する。

所要時間はおおよそ30〜60分。**PowerShell** を使う（スタートメニューで「PowerShell」と入力して起動）。

---

## 手順の全体像

| 段 | やること |
|---|---|
| 1 | Java 21 と Gradle を入れる |
| 2 | ソースを取ってきてプラグインをビルドする |
| 3 | Paper サーバーを立てる |
| 4 | プラグインを入れて起動する |
| 5 | クライアントから接続して召喚する |

---

## 1. Java 21 と Gradle を入れる

PowerShell で次を順に実行する。

```powershell
winget install Microsoft.OpenJDK.21
winget install Gradle.Gradle
```

インストール後、**PowerShell を閉じて開き直す**（PATH を反映させるため）。確認する。

```powershell
java -version
gradle -v
```

`java version "21..."` と Gradle のバージョンが出れば成功。`winget` が使えない場合は、Java は [Adoptium](https://adoptium.net/)、Gradle は [公式](https://gradle.org/install/) から入れる。

---

## 2. プラグインをビルドする

### 2.1 ソースを取得する

**方法A（Git を使わない）**

1. ブラウザでリポジトリを開く
2. ブランチを `claude/litematica-rules-usufg6` に切り替える
3. 緑の **Code** ボタン → **Download ZIP**
4. ダウンロードした ZIP を右クリック →「すべて展開」→ `C:\raid-dev` に展開する

**方法B（Git がある場合）**

```powershell
cd C:\
git clone -b claude/litematica-rules-usufg6 <リポジトリのURL> raid-dev
```

### 2.2 対象バージョンを合わせる

`C:\raid-dev\plugin\build.gradle` をメモ帳で開き、次の行のバージョンを**立てるサーバーのバージョン**に合わせる。

```gradle
compileOnly 'io.papermc.paper:paper-api:1.21.4-R0.1-SNAPSHOT'
```

同じく `C:\raid-dev\plugin\src\main\resources\plugin.yml` の `api-version` も合わせる。

### 2.3 ビルドする

```powershell
cd C:\raid-dev
gradle :plugin:jar
```

初回は依存の取得に数分かかる。成功すると次の場所に jar ができる。

```
C:\raid-dev\plugin\build\libs\raid-plugin.jar
```

> **エラーが出たら、そのメッセージをそのまま伝えてほしい。** 特に paper-api のバージョン指定は、対象バージョンによって書き方が変わる。

---

## 3. Paper サーバーを立てる

### 3.1 フォルダと jar

```powershell
mkdir C:\raid-test
cd C:\raid-test
```

ブラウザで [papermc.io](https://papermc.io/downloads/paper) を開き、**対象バージョンの最新ビルド**をダウンロードして、`C:\raid-test\paper.jar` という名前で置く。

### 3.2 起動用のファイルを作る

メモ帳を開き、次を貼り付けて `C:\raid-test\start.bat` として保存する（**ファイルの種類を「すべてのファイル」にする**。`start.bat.txt` にならないよう注意）。

```bat
@echo off
java -Xms2G -Xmx4G -jar paper.jar nogui
pause
```

### 3.3 初回起動と EULA

`start.bat` をダブルクリックする。`eula.txt` が作られて終了する。

`C:\raid-test\eula.txt` をメモ帳で開き、

```
eula=false
```

を

```
eula=true
```

に変えて保存する。もう一度 `start.bat` を実行すると、ワールドが生成されて `Done` と表示される。コンソールに `stop` と入力して停止する。

### 3.4 検証向けの設定

`C:\raid-test\server.properties` をメモ帳で開き、次の行を書き換える。

```
online-mode=false
difficulty=hard
spawn-protection=0
gamemode=creative
max-players=20
```

> `online-mode=false` は**ローカル検証専用**である。公開するサーバーでは必ず `true` に戻す。

---

## 4. プラグインを入れる

```powershell
copy C:\raid-dev\plugin\build\libs\raid-plugin.jar C:\raid-test\plugins\
```

`plugins` フォルダが無ければ作る。

```powershell
mkdir C:\raid-test\plugins
```

`start.bat` を実行し、コンソールに次が出れば読み込み成功。

```
[RaidPlugin] レイド検証プラグインを有効化しました
```

> プラグインを入れ替えるときは、**必ずサーバーを停止**してから差し替える。`/reload` は使わない（表示エンティティが残る）。

---

## 5. 接続して召喚する

1. Minecraft を起動し、**サーバーと同じバージョン**を選ぶ
2. 「マルチプレイ」→「サーバーを追加」→ アドレスに `localhost` と入力
3. 接続する
4. サーバーのコンソールに次を入力して自分に権限を与える

```
op <あなたのMinecraft名>
```

5. ゲーム内でチャットを開き、次を実行する

```
/raid spawn
```

足元に騎士型が現れる。ほかのコマンドは次のとおり。

| コマンド | 内容 |
|---|---|
| `/raid spawn` | 召喚する |
| `/raid info` | 体力・段階・状態を表示する |
| `/raid despawn` | 除去する |

> **リソースパックが無くても検証できる。** 部位は既定の見た目（紙）で出る。モーション・当たり判定・段階移行・ノックバックはこの状態で確認できる。

---

## 6. 確認する項目

上から順に、前が通ってから次へ進む。

### 表示

- [ ] 部位ぶんのエンティティが出る（第一形態7、第二形態10）
- [ ] 親子どおりに追従する（槍が右腕に、四足が馬胴に）
- [ ] 動きが滑らか（カクつくなら更新間隔と補間時間が合っていない）
- [ ] `/raid despawn` とサーバー停止で**残らない**

### モーション

- [ ] 待機 → 20tick 移動 → 攻撃 の周期で動く
- [ ] チャットにモーション名が出る（`[騎士] 3段突き` など）
- [ ] 突進切り上げ、なぎ払い、3段突き、追従4連切りが順に出る

### 戦闘

- [ ] 部位を叩くと体力が減り、残量がチャットに出る
- [ ] **槍を叩いてもダメージが通らない**旨が出る
- [ ] 突進中に槍を叩くと中断する（`中断させた` と出る）
- [ ] 盾を構えて突進を受けると `パリイ成功` になる
- [ ] 被弾でノックバックする

### 進行

- [ ] 体力が半分になると `第二形態 へ移行` が出て、骨格が入れ替わる
- [ ] 第二形態で回旋突進と踏みつけが出る

---

## 7. 詰まったときは

| 症状 | 対処 |
|---|---|
| `gradle` が見つからない | PowerShell を開き直す。それでも駄目なら Gradle を再インストール |
| ビルドで paper-api が見つからない | `build.gradle` のバージョン指定が対象と合っていない。エラーメッセージを共有してほしい |
| 起動直後に閉じる | `eula.txt` が `false` のまま。または Java 21 が入っていない |
| クライアントが接続できない | **サーバーとクライアントのバージョンが違う**。25565 番ポートの競合も確認 |
| プラグインが読み込まれない | 起動ログの警告を読む。`api-version` の不一致が多い |
| 個体が見えない | 表示エンティティに未対応のバージョン。または `/raid despawn` 後に召喚し直す |
| 動きがおかしい | まず `/raid info` で状態と tick を確認する |

---

## 8. 実機を待たずにできること

数値の整合は Minecraft を使わずに確認できる。

```powershell
cd C:\raid-dev\core
.\run-tests.sh          # Git Bash か WSL がある場合
```

Windows で PowerShell だけの場合は次を直接実行する。

```powershell
cd C:\raid-dev\core
javac -encoding UTF-8 -d out (Get-ChildItem -Recurse -Filter *.java src | % FullName)
java -Dstdout.encoding=UTF-8 -cp out jp.mcserver.core.CoreTests
java -Dstdout.encoding=UTF-8 -cp out jp.mcserver.core.raid.KnightSimulation 20 8 80
```

段階移行、被弾、討伐時間、モーションのサンプリング、通信量の見積りがその場で出る。**実機で見るべきは「見た目・当たり判定・体感」であり、数値はここで潰しておく。**

---

## 補足: このプラグインの状態

| 項目 | 状態 |
|---|---|
| 実装済み | 部位の生成、親子の変換合成、待機→移動→攻撃の周期、ダメージ判定、ノックバック、妨害、パリイ、段階移行と骨格の入れ替え、除去 |
| 未実装 | リソースパックのモデル、取り巻き、報酬、レイド次元、参加登録 |
| 未検証 | **作成環境から PaperMC のリポジトリへ到達できないため、コンパイルの確認ができていない。** 最初のビルドでエラーが出る可能性がある |
| 調整が要るとみられる箇所 | 当たり判定の実体（Interaction）への攻撃をどのイベントで拾うか、槍の判定距離（既定5ブロック）、パリイの判定（盾を構えているか） |
