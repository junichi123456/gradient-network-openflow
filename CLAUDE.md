# このリポジトリでの作業の進め方

## 作業フロー（ユーザー指示 2026-10-25 により恒久化）

すべての指示は **草案 → 確認 → 実装 → 検証** の順で処理する。

### 1. 仕様書の草案（Opus が書く）

指示を受けたら、まず `docs/design/drafts/<YYYY-MM-DD>-<slug>.md` に仕様書の
草案を書く。実装には入らない。草案に必ず含めるもの:

- **目的** — 何を達成したいのか（指示の言い換えではなく、達成条件）
- **現状** — 既存コードのどこが関係するか。ファイル名と関数名まで書く
- **変更内容** — ファイルごとに、何をどう変えるか
- **判断が必要だった点** — 複数の解釈が成り立つところと、採った解釈
- **検証方法** — 何をどう確かめれば「できた」と言えるか。期待値まで書く
- **やらないこと** — 指示の範囲外だと判断したもの

草案は実装者（別プロセスの Sonnet）が会話履歴なしで読む。**この会話で前提に
なっている事柄を省略すると実装できない。** 仕様書は単独で完結させる。

### 2. 確認（ユーザーに聞く）

草案のうち、解釈が分かれて**結果が実質的に変わる**ところだけを
`AskUserQuestion` でユーザーに確認する。慣例的な既定値がある選択や、
コードを読めば決まることは自分で決めて、草案にそう書く。

確認で決まった内容を草案に反映し、そこで仕様書は確定とする。

### 3. 実装（Sonnet に委譲する）

確定した仕様書をもとに、`Agent` ツールで `model: "sonnet"` を指定して
実装させる。プロンプトには仕様書のパスを渡し、仕様書に書いていないことは
勝手に決めずに報告させる。

### 4. 検証（Opus が自分でやる）

Sonnet の報告を信用せず、必ず自分で確かめる。

```sh
dotnet build MysteryDungeonRoguelike.csproj      # コンパイルエラーを先に出す
godot --headless --path . Scenes/BattleTestScene.tscn  # 回帰スイート（現在237項目）
git diff                                          # 仕様書と差分を突き合わせる
```

**回帰スイートの判定は NG の行数で行う。** このスイートは NG があっても終了コード 0
で終わるので、終了コードでは失敗を拾えない。`[検証` 行のうち `NG` を含む行を数え、
乱数で時々落ちる項目を見逃さないよう**6回連続**で実行する。

通らなければ自分で直すか、仕様書を直して再委譲する。

### 適用範囲

コード・データ・Artifact を変える作業はすべてこのフローを通す。
純粋な質問や既存結果の分析には仕様書を作らず直接答える——ただし、その答えを
受けて変更に入るなら、そこから改めてこのフローに乗せる。

## このリポジトリ固有の注意

### ツールチェーンのセットアップ（コンテナは使い捨て）

クラウドセッションのコンテナは再作成されると `godot` も `dotnet` も消える。
`dotnet: command not found` が出たら、まずこれを実行する。

```sh
S=/tmp   # 作業用ディレクトリならどこでもよい
curl -sSL https://dot.net/v1/dotnet-install.sh -o $S/dotnet-install.sh
bash $S/dotnet-install.sh --channel 8.0 --install-dir /opt/dotnet
ln -sf /opt/dotnet/dotnet /usr/local/bin/dotnet

curl -sSL -o $S/godot.zip \
  https://github.com/godotengine/godot/releases/download/4.3-stable/Godot_v4.3-stable_mono_linux_x86_64.zip
unzip -q -o $S/godot.zip -d /opt/godot4
ln -sf /opt/godot4/Godot_v4.3-stable_mono_linux_x86_64/Godot_v4.3-stable_mono_linux.x86_64 \
  /usr/local/bin/godot
```

`godot --version` が `4.3.stable.mono` を返せば完了。

headless 実行ではスプライトの import エラーが大量に出る（`.godot/imported/`
が無いため）。**戦闘ロジックの検証には影響しない**ので無視してよい。

### ビルド

`godot --headless` は古いアセンブリを黙って使うことがある。**必ず
`dotnet build MysteryDungeonRoguelike.csproj` を先に実行する。** これを
飛ばすと、新しく足したコマンドライン引数が無視されて既定の経路に落ちる。

### 種族値を変えると固定シードの再現性が崩れる

`MetaScenario` の生成は Fisher-Yates シャッフルを使っており、**順序に
依存する。** どれか1種の種族値合計を変えると、ソート済みリストの並びが
変わり、生成列全体に伝播する。シード `20260822UL` で作った「同じ」構築
番号（#66 / #53 / #30 など）が、変更後は別の構築になる。過去の実験結果と
比較するときは必ずこれを疑う。

### 行動順と天候の向きは逆

- 行動順: 優先度 → **俊敏(S)の高い側が先** → **BST6 の低い側が先** → 乱数。
  S は種族値そのもの（ランク変化・持ち物では動かない）。BST6 は同速のときだけ効く
- 入場時の天候: **BST6 の高い側から順に発動** するので、低い側が
  最後に上書きして勝つ（`BattleArena.ApplyEntryWeather`）

### 能力値は HABCDS の6つ

HP・攻撃(A)・防御(B)・魔力(C)・精神(D)・俊敏(S)。物理技は A 対 B、特殊技は
C 対 D。合計種族値は **BST6**（旧 BST3 から `BST6 = round(360 + (BST3-180)*270/320)`
で一意に決まる固定値）。旧 H/A/B は `species.json` の `legacy_had` に残してあり、
`Tools/learnset_rules.py` の `legacy_had(s)` で読む。配分は `Tools/build_habcds.py`
が生成する（仕様書 `docs/design/drafts/2026-10-25-void-type-and-habcds.md`）。

### 属性は10種（虚を含む）

無/炎/水/草/電/地/氷/竜/闇/虚。相性表は `Data/type_chart.json`（10×10）。
虚脱状態は虚技の命中で蓄積し、一致属性補正が消え、自分の2回目の行動終了で解除。

### Artifact の同期

learnset や個体性能を変えたら、2つの Artifact を必ず更新する。

```sh
python3 Tools/build_ls_payload.py   # learnset インスペクタのペイロードを再生成
python3 Tools/refresh_move_editor.py # 技データベース エディタ
```

生成した `Tools/artifacts/learnset_viewer.html` と
`Tools/artifacts/move_editor.html` を、**同じ `file_path` で再公開**して
URL を保つ。

スプライトは `Assets/Sprites` から自動で取り込まれる。スプライトを再生成したら build_ls_payload.py を再実行。

- learnset インスペクタ: `188eb9b1-d49d-4c39-bf77-895a30185e99`
- 技データベース エディタ: `975b54fe-5c46-4597-ae30-26d694ac3eb2`

俊敏 S は `Data/source/speed_fixed.json`（`Tools/plan_speed.py --fixed`、最小20・最大130・山45/75/105）に固定。両刀（12種）と
両面耐久の配分式・下限（各20以上、下位2つの和49以上）は §35。BST6 は不変。

特性は元（`trait`）と追加（`trait2`、両刀12種のみ）の**どちらか一方だけ**が有効（`ActiveTrait`）。構築で選び、NPC・自動構築は追加特性。

### 突破値と気質

実数値 = floor(種族値から計算した値 × 気質) + 突破値。**気質が先、突破値は後**。
突破値は1能力25まで・合計55以下、気質は HP 以外の1能力を×1.1、別の1能力を×0.9。
気質は整数演算（`*11/10`・`*9/10`）で、`0.9f` は 50×0.9=44 になるので使わない。
行動順の俊敏は、この実数値を見る（`StatSpread` / `EntityStats`）。

### 構築の規則

1構築内で重複が禁止されるのは**種族と持ち物だけ**。技は別個体どうしなら
重複してよい（同じ個体が同じ技を2つ持つことだけが禁止）。

### 適応モードはシャード分割できない

`--strongest` や `--random` は状態を持たないのでシャード分割できるが、
`--meta-core` / `--meta-challengers` / `--tactics` は構築の変更が次の試合に
引き継がれるため、**単一プロセスで逐次実行**するしかない。

### スプライトは自動生成

スプライトはパーツ式モデル（v2）。`Tools/fetch_pal_refs.py`（参照アイコンを `Tools/.refcache/` へ。コミットしない）→
種ごとに参照画像を見て `Data/sprite_models/{id}.json` を作る → `/usr/bin/python3 Tools/gen_pal_sprites.py {id…}`
（`--sheet` で目視用シート。モデルが無い種はエラー）で `Assets/Sprites/{id}/` を再生成。numpy/PIL は `/usr/bin/python3` に入っている。
検証は `Tools/verify_sprites.py`、規格とスキーマは `docs/design/pvp_spec.md` §34（34-5）。

### 設計記録

実験の設計と結果は `docs/design/pvp_spec.md` に節を足して記録する
（現在 §35 まで）。仕様書の草案は `docs/design/drafts/` に置く。

### シナリオ（Data/story/）

仕様書 `docs/design/drafts/2026-10-31-scenario-implementation.md`（一次資料は `docs/design/scenario/`）。進行は `Scripts/Story/`
（`StoryState`/`StoryFlow`/`EventInterpreter`/`SaveService`）、会話は `Data/story/events/*.json`（イベントID `cNN_eMM_slug`）。
`Tools/verify_story.py`・`verify_pal_plan.py` で検証。チャートのイベント一覧は `Tools/sync_chart_events.py` で同期。
