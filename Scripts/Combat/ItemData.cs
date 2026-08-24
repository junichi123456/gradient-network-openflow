namespace MysteryDungeon.Combat;

public enum ItemType
{
    Consumable,
    Throwable,

    // 対戦で1匹に1つだけ持ち込む装備。迷宮では拾えず、使うこともない
    // （UseItemAction は BattleHeld を対象外として扱う）。
    BattleHeld,

    // Hub-upgrade currency (Phase 11). Never usable in-dungeon
    // (UseItemAction's default branch just logs "no effect" and
    // doesn't consume it) - only picked up into MaterialInventory
    // (never InventoryManager) and spent via HubUpgradeManager.
    Material,
}

public enum ItemEffectTarget
{
    Hp,
    Belly,
    Damage,

    // Material items carry no direct-use effect.
    None,
}

// 対戦用の持ち物が持つ効果。1アイテム1効果で、data 側の battle_effect と
// 1対1に対応する。使い切り(発動で消費)か所有中持続かは
// ItemData.ConsumedOnTrigger が持つ。
public enum BattleItemEffect
{
    None,

    // --- 使い切り ---
    HealAt50,          // HPが50%未満になると最大HPの25%回復
    HealAt33,          // HPが33%以下になると最大HPの50%回復
    SurviveFromFull,   // HP満タンから即死する攻撃を受けてもHP1で耐える
    CritCut90,         // 急所を受けた時、そのダメージを90%減
    WeaknessCut75,     // 弱点属性を受けた時、そのダメージを75%減
    CounterRoom,       // Room射程を受けた時、受けたダメージを使用者へ反射
    CoverAllies,       // 範囲射程を受けた時、自分以外の味方はダメージ0
    RestoreRank,       // 自分のランクが下がったら元に戻す
    CureAilment,       // 自分が状態異常になったら回復する

    // --- 所有中持続 ---
    RegenOnTurnEnd,    // 自分のターン終了時にHP10%回復
    PurgeOnTurnEnd,    // 自分のターン終了時に全状態異常蓄積値を100減少
    PhysAtkUp25,       // 物理技の使用時、攻撃力(実数値)+25%
    SpecAtkUp25,       // 特殊技の使用時、攻撃力(実数値)+25%
    PhysDefUp30,       // 物理技の被弾時、防御力(実数値)+30%
    SpecDefUp40,       // 特殊技の被弾時、防御力(実数値)+40%
    ImmuneWideRange,   // Room/Area の攻撃を受けない
}

// Immutable item definition, loaded once by ItemDatabase from
// Data/items.json. ElementType is only meaningful for Throwable items -
// it feeds TypeChartManager the same way a move's Type does.
public class ItemData
{
    public string Id { get; set; }

    // 対戦持ち物のみ意味を持つ。効果種別と、発動で消費されるか否か。
    public BattleItemEffect BattleEffect { get; set; } = BattleItemEffect.None;
    public bool ConsumedOnTrigger { get; set; }
    public string Description { get; set; } = "";
    public string Name { get; set; }
    public ItemType Type { get; set; }
    public ItemEffectTarget EffectTarget { get; set; }
    public int EffectValue { get; set; }
    public string ElementType { get; set; }
}
