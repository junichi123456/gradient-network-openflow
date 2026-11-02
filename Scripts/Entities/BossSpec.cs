using System.Collections.Generic;

namespace MysteryDungeon.Entities;

// ボス1体の定義（Data/story/dungeons.json の boss を実行時の形にしたもの）。
// Group は 2体ボス・群れ戦の追加メンバー（主ボスと同時に出る）。
public class BossSpec
{
    public string SpeciesId = "";
    public int Level = 15;
    public double HpMult = 1.0;
    public double AtkMult = 1.0;
    // true のとき、パーティが全滅しても負け扱いにせず物語が進む。
    public bool DefeatOk;
    public List<BossSpec> Group = new();
}
