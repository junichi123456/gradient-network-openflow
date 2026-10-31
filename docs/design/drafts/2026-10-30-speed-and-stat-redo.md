# 俊敏の確定と能力配分の作り直し（両刀 12種・両受の式・最低値・特性の選択制）— 仕様書

- 状態: **確定**（2026-10-30。ユーザーが俊敏表を了承し、論点3つに回答済み。§4）
- ブランチ: `claude/mystery-dungeon-roguelike-egnzjt`。前提: 直前コミットの `Data/`（species 287・moves 628・traits 127）、回帰 186 項目、Python 検証 21/26/9 PASS
- 読み手: 会話履歴のない実装者。**書かれていないことは決めずに報告する。commit/push は検証者（Opus）が行う**（git を使わない）。
- 環境: `/usr/bin/python3`（numpy/PIL 入り）。dotnet/godot が無ければ CLAUDE.md の手順で入れる。ビルドは必ず `dotnet build MysteryDungeonRoguelike.csproj` を先に。

## 1. 目的（達成条件）
1. 全287種の俊敏 S を、ユーザー承認済みの表（`docs/design/drafts/2026-10-30-speed-table.md`。行ごとの値。最小20・最大130・山45/75/105）に固定する。
2. 他の能力（H,A,B,C,D）を、**各種の BST6 の合計は変えずに**、新しい規則で配り直す。
3. 両刀攻撃（offense==Mixed）を**12種**にし、全員が条件（§3-2）を満たし、全員が追加特性 `trait2` を持つ。
4. 両面耐久（defense==Both）の全員が H=0.8(B+D) かつ B:D が 41:59〜59:41（§3-3）。両刀と両面耐久は重ならない。
5. 全員の全能力が20以上、最小の2能力の和が49以上（S を含む6能力で判定）。
6. 追加特性は「元の特性と追加特性のどちらか一方だけが有効」。構築で選ぶ。NPC・自動構築は追加特性を使う（§3-5）。
7. build 0 エラー、回帰スイートの NG 行 0 を6回連続、Python 検証 ALL PASS、2つの Artifact 更新、3,000試合の計測を `docs/design/pvp_spec.md` §35 に記録。

## 2. 現状（関係箇所）
- `Tools/build_habcds.py`: `P_N`/`P_R`/`speed_of` が俊敏を作る（35〜130・三峰50/75/100）。`compute()` が配分（`W(S)` で耐久の取り分、`ALPHA`（A:C=70:30/30:70）、`BETA`（B/(B+D)=13/20,7/20,1/2）、両刀は `mixed_side_of`＋10%開き）。`ROLE_COUNT`、`self_check()`（Mixed==38 等）。出力は `Data/species.json`（base_hp/atk/def/spatk/spdef/speed, offense, offense_source, defense, role, speed_source…）。
- `Tools/assign_mixed_traits.py`: 現行の両刀38種のうち16種に `trait2`（wide_burst 4・kowaza_jouzu 5・shimikomi 4・entry_puddle/entry_flowerbed/entry_thin_ice 各1）を固定シードで付与。`Data/traits.json` の二次特性 6 種。
- `Tools/conform_offense_learnsets.py`（攻撃区分に learnset を合わせる）、`Tools/apply_fast_priority_rule.py`（S≥95 は優先度+1以上の攻撃技を覚えない）、`Tools/add_protection_move.py`（両刀かつ BST6≤470 に `mvs_ice_protection` を Lv10 で）、`Tools/learnset_rules.py`、`Tools/verify_learnset.py`/`verify_attack_moves.py`/`verify_npc_teams.py`、`Tools/build_ls_payload.py`、`Tools/refresh_move_editor.py`。
- C#: `Scripts/Species/SpeciesDatabase.cs`（`Trait`/`Trait2` と検証：trait2 は Mixed のみ等）、`Scripts/Combat/SecondaryTraits.cs`・`PowerTraitWeather.cs`・`BattleArena.cs`/`FloorController.cs`（`ApplyFieldOnEntry`）などが trait/trait2 を**同時に**参照、`Scripts/UI/Battle/TeamBuildScreen.cs`、`Scripts/Battle/BattleTeam.cs`・`BattleNetwork.cs`（`EncodeFull/DecodeFull`）・`NpcTeamDatabase.cs`・`NpcOpponent.cs`、`Scripts/Battle/BattleTestScene.cs`（回帰 186 項目）。
- 俊敏の表の作り方: `Tools/plan_speed.py`（入力 `Tools/speed_list.txt`）。`docs/design/drafts/2026-10-30-speed-table.md` は、その出力で、ユーザーが承認済み。

## 3. 変更内容

### Phase D1 — 俊敏の固定と配分（データ）
**3-1. 俊敏。** `Tools/plan_speed.py` に、確定表を `Data/source/speed_fixed.json`（`{species_id: S}`、全287、名前→id は `Data/species.json` の display_name で引く）として出力する機能を足す。出力の値は `docs/design/drafts/2026-10-30-speed-table.md` と**完全一致**させる（不一致は報告）。`build_habcds.py` の `speed_of`/`P_N`/`P_R`/paldb 依存は使わず `speed_fixed.json` を読む（`speed_source` は `{"kind":"table"}` に置換）。`self_check` の三峰判定は新しい山（45/75/105、±4 の頭数が谷より多い）に更新。

**3-2. 両刀（Mixed）の条件。** 次を全て満たす: A+C ≥ 160、(A+C) > 1.3×(B+D)、|A−C| ≥ 10、A/(A+C) と C/(A+C) がともに 0.4 以上（4:6〜6:4）、H ≥ 55、defense ≠ Both。
**3-3. 両面耐久（Both）の条件。** H = round(0.8×(B+D))（±0 を目標、丸め誤差は±1まで許容）、B/(B+D) が 0.41〜0.59。Both は Mixed にならない。
**3-4. 全員の下限。** 6能力すべて ≥ 20。小さい順に並べた下位2つの和 ≥ 49。違反するときは、決定的な手順（最大の非S能力から最小の能力へ1点ずつ移す。S は動かさない）で直し、直した種の一覧を報告する。

**配分 `compute` の改修。** 予算 T = BST6 − S（BST6 は現行値のまま。`legacy_had` から現行式で求まる）。物理/特殊耐久・物理/特殊攻撃は現行の式（`W(S)`, `ALPHA`, `BETA`）を踏襲。Both は Bulk=round(T×w) を `G=B+D=round(Bulk/1.8)`、`H=Bulk−G`、`B=round(G/2)` を目標に、B/(B+D) を 0.41〜0.59 にクランプ。Mixed は Off = T − Bulk を A+C≥160 かつ (A+C)>1.3×G になるよう Bulk から削って満たし（H は 55 未満にしない。足りなければ G を削る）、A:C は 0.4〜0.6 に収め |A−C|≥10 とする（現行の `mixed_side_of`＋learnset 比率は、主側の決め方として流用してよい）。数式の細部は実装者が決めてよいが、**§3-2〜3-4 の不等式は全員で満たすこと**を `self_check` で検査する。

**3-5. 両刀12種の選び方（`Tools/assign_mixed_traits.py` を作り直す）。**
- 候補: 特性が「`*_bond`/`*_power`/`*_guard`/`*_oshie`（属性つきの きずな・ちから・まもり・おしえ）、`keikaikan`、`koufun`」のいずれかで、defense ≠ Both、BST6−S ≥ 255、かつ配分して §3-2 を満たせる種。リーダー系特性の種は含まない。
- 選び方: 現行の両刀（38種）に含まれる候補を**優先**し、足りなければ他の候補から補う。主属性ごとに最大2種、固定シード（`20261030`）で決定的に12種。12種に満たないときは満たせた数で止めて報告（9未満なら実装を止めて報告）。
- 12種に入らなかった旧両刀（約27種）は offense を Physical/Special に直す（現行配分後の A と C の大きい側。同値は Physical）。`offense_source` はその旨の値にする。新たに両刀になる種があれば offense を Mixed にする。`role` を更新し、`ROLE_COUNT`・`self_check` の数（Mixed==38 など）は実数に合わせる。
- 12種すべてに `trait2` を付ける。割り当ては前回と同じ内容: 水・草・氷の主属性の種が選ばれていれば、その種に地形特性（水=entry_puddle・草=entry_flowerbed・氷=entry_thin_ice）を各1体まで。残りには wide_burst・kowaza_jouzu・shimikomi をほぼ均等に（固定シード）。地形特性は主属性が合う種にだけ。

**3-6. learnset 等の追従（既存ツールの再実行と修正）。** `conform_offense_learnsets.py`（offense が変わった種）、`apply_fast_priority_rule.py`（新しい S で）、`add_protection_move.py`（Mixed かつ BST6≤470 だけが `mvs_ice_protection` を Lv10 で持つ。旧両刀から外れた種からは外す。冪等に）を、この順で実行する。`Tools/verify_*.py` が ALL PASS になるまで直す（NPC チームが外れた技を持つ場合は `Data/npc_teams.json` を最小限直す。直した内容を報告）。`Data/` の再生成は冪等で、再実行して差分が出ないこと。

### Phase D2 — 特性の選択制（C#）
- `trait` と `trait2` は同時ではなく**どちらか一方だけ**が有効。構築の1個体ごとに「有効な特性」（元 / 追加）を持つ。`trait2` が無い個体は常に元の特性。既定は元の特性。
- 特性を読む全箇所（`SecondaryTraits.cs`, `PowerTraitWeather.cs`, `BattleArena.cs`/`FloorController.cs` の入場時地形、ほか `Trait`/`Trait2` を参照する箇所を `grep` で全て洗う）を「有効な特性」だけを見るように変える。
- 構築データ（`BattleTeam.cs` ほか）に有効特性のフィールドを足し、構築画面（`TeamBuildScreen.cs`）で `trait2` を持つ個体にだけ切替の項目を出す（現在どちらが有効か分かる表示）。通信（`BattleNetwork.cs` `EncodeFull/DecodeFull`）に1バイト足す（版数の扱いは既存の流儀に合わせ、後方互換が要るなら報告）。
- NPC・自動構築（`NpcTeamDatabase.cs`、`--random` 等の生成経路）は、`trait2` を持つ個体では**追加特性を使う**。**乱数は消費しない**（固定シードの再現性を保つ）。
- 回帰スイートに、(a) 元の特性選択では trait2 の効果が出ない、(b) 追加特性選択では元の特性の効果が出ず trait2 の効果が出る、(c) 通信の往復で有効特性が保たれる、(d) 既存の trait2 検証（旧「同時に効く」を前提にした項目）を新仕様に直す、を足す。項目数の増減は報告（現在 186）。

### Phase D3 — Artifact・記録・計測
- `Tools/build_ls_payload.py`・`Tools/refresh_move_editor.py` を再実行（learnset インスペクタは2つめの特性を「選択式の追加特性」として表示。生成物の公開は検証者が行う）。
- `docs/design/pvp_spec.md` に §35 を追記（俊敏表の要約と山の評価、新規則、両刀12種の一覧と選定理由、旧両刀の再分類数、下限規則で直した種の一覧、特性選択制）。CLAUDE.md の「能力値は HABCDS」「突破値と気質」付近に、俊敏は固定表・両刀/両面耐久の式・特性選択制を各1〜2行追記し、回帰項目数と「§35 まで」を更新。
- 計測: `godot --headless --path . Scenes/BattleTestScene.tscn --random --matches 3000 --out <csv>` の通常方式（従来の §28〜§33 と同じ。引数はコードで確認）を **1回**。`Tools/analyze_random_by_element.py --by offense/role/element/speed/trait2 --bootstrap` の結果を §35 に記録。基準を外れても**調整せず**報告。

## 4. 判断が必要だった点（採った解釈）
- 「両刀 9〜14種」は 12種（ユーザー回答）。候補が現行の両刀だけでは11種のため、特性系の他の種から1種以上補う（§3-5）。
- 「追加の特性をおしえ/きずな/ちから/まもり/けいかいかん/こうふん持ちに追加」は、両刀に選ばれる種を**これらの特性を持つ種に限る**と解釈（ユーザー回答の「優先候補」から一段進めて必須とした）。
- 「どちらか一方のみ有効」→ 構築ごとに選択、NPC は追加特性（ユーザー回答）。追加特性の中身は前回の6種を使い回す（ユーザー回答）。
- BST6 は種ごとに現行値を維持（BST の作り直しは別の機会）。
- 160≦(a+c)＞(b+d)×1.3 は「A+C≥160 かつ A+C>1.3(B+D)」（ユーザー確認済み）。最小2つの和は「49以上」（同）。

## 5. 検証方法
- `Data/source/speed_fixed.json` が 287 件、表と一致、最小20・最大130、山の評価（45/75/105 の ±4 頭数 > 谷60/90）。
- 全種: 6能力 ≥20、下位2つの和 ≥49、BST6 が現行値と一致、H+A+B+C+D+S=BST6。
- Mixed 12種: §3-2 全条件、全員 trait2 あり、特性系候補、defense≠Both。Both 全員: §3-3。Mixed 以外は trait2 なし。
- `verify_learnset.py`/`verify_attack_moves.py`/`verify_npc_teams.py` が ALL PASS（件数は増減してよい。報告）。Data 再生成の冪等。
- `dotnet build` 0 エラー、回帰 NG 行 0 を6回連続、計測1回の結果が §35 にある。

## 6. やらないこと
BST6 の再設計、技・威力・命中の変更、勝率の調整、スプライト、UI の見た目の作り込み、Mixed 以外への追加特性。
