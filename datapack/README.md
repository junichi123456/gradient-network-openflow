# 石の扉・トラップドアのレシピ上書き

バニラに存在しない「石の扉」「石のトラップドア」を、**クリムゾンの扉・トラップドアを
流用して再現する**ためのデータパック。クリムゾン材はこのサーバーの仕様書・別紙の
どこにも使われていないため、見た目の衝突がない。

- `minecraft:crimson_door` のレシピを、クリムゾンの板材6個ではなく**石6個**で
  作れるように上書きする（3個できる。バニラの扉と同じ個数）
- `minecraft:crimson_trapdoor` のレシピを、同様に**石6個**で作れるように上書きする
  （2個できる。バニラのトラップドアと同じ個数）
- 開閉・レッドストーン連動・村人の使用など、**挙動はバニラのクリムゾンの扉/
  トラップドアと完全に同じ**（新しいルールは追加しない）
- 見た目を「石」に変えるのはリソースパック側の役割（`resourcepack/` を参照。
  テクスチャの塗り替えは別途必要——下記「残作業」を参照）

## 入れ方

サーバーの `world/datapacks/`（または各次元のワールドフォルダの `datapacks/`）に、
このフォルダをそのままコピーする。`/reload` または再起動で反映される。

## 残作業（テクスチャ）

このデータパックはレシピ（データ）のみを差し替える。**見た目を石にするには、
`resourcepack/` 側で以下のテクスチャを石materialの見た目に塗り替える必要がある**
（`resourcepack/PAINTING.md` と同じ要領。Windows のペイント等でPNGを塗り替えるだけでよい）。

| ファイル | 内容 |
|---|---|
| `resourcepack/assets/minecraft/textures/block/crimson_door_bottom.png` | 扉の下半分 |
| `resourcepack/assets/minecraft/textures/block/crimson_door_top.png` | 扉の上半分 |
| `resourcepack/assets/minecraft/textures/item/crimson_door.png` | 扉のインベントリ表示 |
| `resourcepack/assets/minecraft/textures/block/crimson_trapdoor.png` | トラップドア |

表示名（「石の扉」「石のトラップドア」）は `resourcepack/assets/minecraft/lang/ja_jp.json`
の言語上書きで対応済み。ブロックの形状・当たり判定はバニラのままのため、モデル
JSON（blockstates）の変更は不要——テクスチャの差し替えだけで見た目が変わる。
