using System.Collections.Generic;
using System.Linq;
using MysteryDungeon.Combat;
using MysteryDungeon.Story;

namespace MysteryDungeon.Hub;

// 広場の施設ごとの一覧UIの中身（タブと実行処理）。UI から切り離してあるので、ヘッドレスでも動かせる。
public static class HubFacilityMenus
{
    public static string Header(HubUpgradeManager hub) => $"{HubData.Map.Currency}: {hub.Points}";

    public static List<HubListUI.Tab> TabsFor(HubFacilityDef f, HubUpgradeManager hub, ICollection<string> completedCharts)
    {
        var tabs = f.Ui switch
        {
            "storage" => StorageTabs(hub),
            _ => ShopTabs(f.Filter, hub),
        };
        if (f.Ui == "lab") tabs.Add(AnalysisTab(hub, completedCharts));
        return tabs;
    }

    private static string Name(string id) => ItemDatabase.Get(id)?.Name ?? id;

    public static List<HubListUI.Tab> StorageTabs(HubUpgradeManager hub) => new()
    {
        new HubListUI.Tab
        {
            Title = "預ける",
            Rows = () => hub.Carried().Where(kv => ItemDatabase.Get(kv.Key)?.Type != ItemType.Material && kv.Value > 0)
                .Select(kv => new HubListUI.Row(kv.Key, $"{Name(kv.Key)}  持ち物{kv.Value}  倉庫{hub.StoredCount(kv.Key)}", kv.Value)).ToList(),
            Act = (r, n) => hub.Deposit(r.Id, n).Message,
        },
        new HubListUI.Tab
        {
            Title = "引き出す",
            Rows = () => hub.Storage.Where(kv => kv.Value > 0)
                .Select(kv => new HubListUI.Row(kv.Key, $"{Name(kv.Key)}  倉庫{kv.Value}  持ち物{hub.CarriedCount(kv.Key)}", kv.Value)).ToList(),
            Act = (r, n) => hub.Withdraw(r.Id, n).Message,
        },
    };

    public static List<HubListUI.Tab> ShopTabs(string filter, HubUpgradeManager hub)
    {
        string cur = HubData.Map.Currency;
        var rewards = HubData.Rewards;
        return new()
        {
            new HubListUI.Tab
            {
                Title = "買う",
                Rows = () => ItemDatabase.AllIds().Select(ItemDatabase.Get).Where(d => HubUpgradeManager.MatchesFilter(filter, d))
                    .Select(d => new HubListUI.Row(d.Id, $"{d.Name}  {d.Price}{cur}  持ち物{hub.CarriedCount(d.Id)}", System.Math.Max(1, hub.Points / d.Price))).ToList(),
                Act = (r, n) => hub.Buy(filter, r.Id, n).Message,
            },
            new HubListUI.Tab
            {
                Title = "売る",
                Rows = () => hub.Carried().Where(kv => kv.Value > 0 && HubUpgradeManager.MatchesFilter(filter, ItemDatabase.Get(kv.Key)))
                    .Select(kv => new HubListUI.Row(kv.Key, $"{Name(kv.Key)}  売値{rewards.SellPrice(ItemDatabase.Get(kv.Key))}{cur}  持ち物{kv.Value}", kv.Value)).ToList(),
                Act = (r, n) => hub.Sell(filter, r.Id, n, rewards).Message,
            },
        };
    }

    // 研究所の「分析」: 不思議なかけらを1個渡す（消費しない）と、完了したチャートに応じたメモが読める。
    public static HubListUI.Tab AnalysisTab(HubUpgradeManager hub, ICollection<string> completedCharts) => new()
    {
        Title = "分析",
        UseQuantity = false,
        Rows = () => new() { new HubListUI.Row(HubUpgradeManager.ShardId, "不思議なかけらを渡す", 1) },
        Act = (_, _) =>
        {
            if (!hub.HasShard) return "不思議なかけらを持っていない。";
            var notes = HubData.NotesFor(completedCharts);
            if (notes.Count == 0) return "まだ、何も分からない。";
            return "分析メモ(かけらは返してもらった)\n" + string.Join("\n", notes.Select(n => $"・{n.Title}: {string.Join("", n.Lines)}"));
        },
    };
}
