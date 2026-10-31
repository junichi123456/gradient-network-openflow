using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MysteryDungeon.Dungeon;

namespace MysteryDungeon.Story;

// ダンジョンから戻った結果（FloorController → ハコニワ）。Defeated = 負けても進行するボス戦で敗北した。
public class DungeonResult
{
    public string DungeonId = "";
    public bool Defeated;
}

// チャートの進行（開始・完了・補完の挿入・ダンジョン解放）を司る純 C#（Godot 非依存）。
// イベント本文の実行は EventInterpreter に任せ、ここは「いつ何を発火するか」だけを決める。
public class StoryFlow
{
    private readonly StoryDatabase _db;
    private string _pendingChart;

    public StoryState State { get; }
    public EventInterpreter It { get; }
    // start_scenario_fight が要求したダンジョンID（シーン側が読み取って入る）。
    public string PendingFight { get; private set; }

    public StoryFlow(StoryDatabase db, StoryState state, EventInterpreter it)
    {
        _db = db; State = state; It = it;
        It.AfterEvent = OnAfterEvent;
        It.Callbacks.GotoChart = id => _pendingChart = id;
        It.Callbacks.StartScenarioFight = id => PendingFight = id;
    }

    public string TakePendingFight() { var f = PendingFight; PendingFight = null; return f; }

    // ---- 解放 ----

    public bool UnlockSatisfied(ChartDef c)
        => c.Unlock.Charts.All(State.CompletedCharts.Contains) && c.Unlock.Flags.All(State.HasFlag)
           && (!c.Unlock.PostClear || State.PostClear);

    public bool IsStarted(string chartId) => State.HasFlag("started_" + chartId);

    // ダンジョン選択に出せるか: 隠しでなく、解放済み（チャート開始で解放）で、追加枠は PostClear 後。
    public bool IsDungeonAvailable(DungeonDef d)
        => !d.Hidden && State.UnlockedDungeons.Contains(d.Id) && (!d.PostClear || State.PostClear);

    public List<DungeonDef> AvailableDungeons() => _db.Dungeons.Where(IsDungeonAvailable).ToList();

    // 完了条件: 隠しでない自チャートのダンジョンをすべてクリア。
    public bool ChartComplete(ChartDef c)
    {
        var req = c.Dungeons.Select(_db.Dungeon).Where(d => d != null && !d.Hidden).ToList();
        return req.Count > 0 && req.All(d => State.ClearedDungeons.Contains(d.Id));
    }

    // ---- 進行 ----

    // ハコニワに入るたびに呼ぶ。新規なら c01 を始め、進行中のチャートが無ければ次の本編を始める。
    public async Task EnterHub()
    {
        if (State.CurrentChart == "" && !IsStarted("c01")) await StartChart("c01");
        await AdvanceCharts();
        await It.Fire("hub_enter");
        await Drain();
    }

    public async Task StartChart(string id)
    {
        var c = _db.Chart(id) ?? throw new StoryException($"チャート '{id}' が無い");
        if (IsStarted(id)) return;
        State.SetFlag("started_" + id);
        if (c.Kind == "main") State.CurrentChart = id;
        foreach (var d in c.Dungeons) State.UnlockedDungeons.Add(d);
        await It.Fire("chart_start", new EventInterpreter.TriggerArgs(Chart: id));
        await Drain();
    }

    // 進行中の本編が無ければ、解放条件を満たす最初の本編チャートを始める。
    public async Task AdvanceCharts()
    {
        while (true)
        {
            var cur = _db.Chart(State.CurrentChart);
            if (cur != null && cur.Kind == "main" && !State.CompletedCharts.Contains(cur.Id)) return;
            var next = _db.Charts.FirstOrDefault(c => c.Kind == "main" && !IsStarted(c.Id) && UnlockSatisfied(c));
            if (next == null) return;
            await StartChart(next.Id);
        }
    }

    public async Task OnDungeonResult(DungeonResult r)
    {
        var d = _db.Dungeon(r.DungeonId) ?? throw new StoryException($"ダンジョン '{r.DungeonId}' が無い");
        State.ClearedDungeons.Add(d.Id);
        if (r.Defeated) State.SetFlag("lost_" + d.Id);   // 負けても進行するボス戦: イベント側で分岐できる
        var a = new EventInterpreter.TriggerArgs(Dungeon: d.Id);
        await It.Fire("boss_defeated", a);
        await It.Fire("dungeon_clear", a);
        var c = _db.Chart(d.Chart);
        if (c != null && !State.CompletedCharts.Contains(c.Id) && ChartComplete(c)) await CompleteChart(c);
        await Drain();
    }

    public async Task CompleteChart(ChartDef c)
    {
        if (!State.CompletedCharts.Add(c.Id)) return;
        await It.Fire("chart_end", new EventInterpreter.TriggerArgs(Chart: c.Id));
        foreach (var h in c.HubChanges) State.SetFlag("hub_" + h);
        if (c.EndsGame)
        {
            State.PostClear = true;
            foreach (var d in _db.Dungeons.Where(d => d.PostClear)) State.UnlockedDungeons.Add(d.Id);
        }
        foreach (var s in SupplementsAfter(c.Id, "chart_end")) await OfferSupplement(s);
        await AdvanceCharts();
        await Drain();
    }

    // ---- 補完チャートの挿入点 ----

    private IEnumerable<ChartDef> SupplementsAfter(string hostChart, string evId)
        => _db.Charts.Where(s => s.Kind == "supplement" && s.InsertAfter != null && s.InsertAfter.Chart == hostChart
            && (evId == "chart_end" ? s.InsertAfter.Event == "chart_end"
                : s.InsertAfter.Event != "chart_end" && (evId == s.InsertAfter.Event || evId.StartsWith(s.InsertAfter.Event + "_"))))
            .ToList();

    private async Task OnAfterEvent(string evId)
    {
        // どの本編チャートのイベントか（cNN_eMM...）を ID から引く。
        int us = evId.IndexOf('_');
        if (us <= 0) return;
        foreach (var s in SupplementsAfter(evId[..us], evId)) await OfferSupplement(s);
    }

    private async Task OfferSupplement(ChartDef s)
    {
        if (State.HasFlag("offered_" + s.Id)) return;
        State.SetFlag("offered_" + s.Id);
        // 後から回収できるのは SkippedSupplements のみ。受けたものは通常のチャートとして始める。
        int pick = 0;
        if (It.Host != null)
        {
            if (s.Offer != "") await It.Host.Narration(s.Offer);
            pick = await It.Host.Choice(new[] { "寄り道する", "あとにする" });
        }
        if (pick == 0) await StartChart(s.Id);
        else State.SkippedSupplements.Add(s.Id);
    }

    private async Task Drain()
    {
        while (_pendingChart != null)
        {
            string id = _pendingChart; _pendingChart = null;
            await StartChart(id);
        }
    }
}
