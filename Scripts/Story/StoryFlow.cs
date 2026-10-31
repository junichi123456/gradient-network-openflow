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
    // 勧誘が成功したとき、ロスターへ加える窓口（HubScene が PartyManager.AddToRoster を差す）。
    public Action<string> OnRecruit { get; set; }
    // 種族ID → 表示名（勧誘成功の文に使う）。
    public Func<string, string> SpeciesName { get; set; } = id => id;

    public const int LegendaryRecruitPct = 30;
    public const int BossPalRecruitPct = 20;

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
        => !d.Hidden && (State.UnlockedDungeons.Contains(d.Id) || IsSkippedSupplementDungeon(d)) && (!d.PostClear || State.PostClear)
           && d.RequiresCleared.All(State.ClearedDungeons.Contains);

    // スキップした補完（まだ始めていない）のダンジョンか。挿入先の本編チャートが完了していれば、いつでも入れる（35 は c14 完了後）。
    public bool IsSkippedSupplementDungeon(DungeonDef d)
    {
        if (!State.SkippedSupplements.Contains(d.Chart) || IsStarted(d.Chart)) return false;
        var c = _db.Chart(d.Chart);
        return c is { Kind: "supplement" } && c.Dungeons.Contains(d.Id)
               && (c.InsertAfter == null || State.CompletedCharts.Contains(c.InsertAfter.Chart));
    }

    // ダンジョン選択で補完ダンジョンに入る直前: 補完チャートを通常どおり始める（本文が流れ、ダンジョンが解放される）。
    public async Task EnterSupplementDungeon(string dungeonId)
    {
        var d = _db.Dungeon(dungeonId);
        if (d == null || !IsSkippedSupplementDungeon(d)) return;
        await StartChart(d.Chart);
    }

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
        await It.Fire("time_passed", new EventInterpreter.TriggerArgs(Days: State.Days));
        await CheckResidentEvents();
        await Drain();
    }

    // ---- 階到達（dungeon_floor） ----

    public async Task OnFloorReached(string dungeonId, int floor)
    {
        await It.Fire("dungeon_floor", new EventInterpreter.TriggerArgs(Dungeon: dungeonId, Floor: floor));
        await Drain();
    }

    // この階に着いたとき実際に流れるイベントがあるか（UI を作る前の判定用）。
    public bool FloorEventsPending(string dungeonId, int floor)
        => _db.Events.Any(e => e.Trigger is { Type: "dungeon_floor" } t && t.Dungeon == dungeonId && t.Floor == floor
                               && (!t.Once || !State.FiredEvents.Contains(e.Id)) && t.Requires.All(State.HasFlag));

    // ---- P9: ハコニワの住民イベント（補完21〜30） ----

    public bool ResidentUnlocked(ChartDef c)
        => c.Kind == "resident" && UnlockSatisfied(c) && State.Days >= c.Unlock.Days && State.Affinity >= c.Unlock.Affinity;

    // 解放条件（先行チャート・日数・親密度）を満たした住民イベントを、1回ずつ流す。
    public async Task CheckResidentEvents()
    {
        foreach (var c in _db.Charts.Where(c => c.Kind == "resident").OrderBy(c => StoryDatabase.ChartNumber(c.Id)).ToList())
        {
            if (State.CompletedCharts.Contains(c.Id) || !ResidentUnlocked(c)) continue;
            await StartChart(c.Id);
            // 台本の「補う内容」の導入: 自チャート宛の call_event 専用イベント（trigger 無し）を、データ上の順に1回ずつ。
            var ids = c.Events.Concat(_db.Events.Where(e => e.Id.StartsWith(c.Id + "_")).Select(e => e.Id)).Distinct();
            foreach (var id in ids)
            {
                var ev = _db.Event(id);
                if (ev == null || ev.Trigger != null || !State.FiredEvents.Add(id)) continue;
                await It.RunEvent(id);
            }
            await CompleteChart(c);
        }
    }

    // 住民と話す: 1人につき1日1回だけ親密度が+1（日が進むとまた+1できる）。
    public bool TalkToResident(string id)
    {
        if (!State.ResidentsArrived.Contains(id)) return false;
        string key = "talk_" + id;
        if (State.GetCounter(key) == State.Days + 1) return false;
        State.Counters[key] = State.Days + 1;
        State.Affinity++;
        return true;
    }

    // ---- P7: 思い出（スキップした補完の回収） ----

    // クリア後、スキップしたまま回収していない補完（ID順）。
    public List<string> MemoryEntries()
        => !State.PostClear ? new() : State.SkippedSupplements.Where(id => !State.MemoryCollected.Contains(id))
            .OrderBy(id => id, StringComparer.Ordinal).ToList();

    // 思い出イベント x_mem_<チャート> を流す。「思い出す」を選ぶとイベントが memory_<チャート> を立て、回収済みになる。
    public async Task<bool> RunMemory(string chartId)
    {
        if (!MemoryEntries().Contains(chartId)) return false;
        await It.RunEvent("x_mem_" + chartId);
        if (!State.HasFlag("memory_" + chartId)) return false;
        State.MemoryCollected.Add(chartId);
        return true;
    }

    // ハコニワのメニュー（思い出／住民と話す）。会話窓の選択肢で出す。
    public async Task RunHubMenu()
    {
        var h = It.Host;
        if (h == null) return;
        while (true)
        {
            var labels = new List<string>(); var kinds = new List<string>();
            if (MemoryEntries().Count > 0) { labels.Add("思い出"); kinds.Add("memory"); }
            if (State.ResidentsArrived.Count > 0) { labels.Add("住民と話す"); kinds.Add("talk"); }
            labels.Add("閉じる");
            int i = await h.Choice(labels);
            if (i < 0 || i >= kinds.Count) return;
            if (kinds[i] == "memory")
            {
                var ids = MemoryEntries();
                int j = await h.Choice(ids.Select(id => $"{id} {_db.Chart(id)?.Title}").Append("戻る").ToList());
                if (j >= 0 && j < ids.Count) await RunMemory(ids[j]);
            }
            else
            {
                var ids = State.ResidentsArrived.OrderBy(x => x, StringComparer.Ordinal).ToList();
                var names = ids.Select(id => _db.Character(id)?.DisplayName ?? id).ToList();
                int j = await h.Choice(names.Append("戻る").ToList());
                if (j >= 0 && j < ids.Count)
                    await h.Narration(TalkToResident(ids[j]) ? $"{names[j]}と話した。(親密度 {State.Affinity})" : $"{names[j]}とは、今日はもう話した。");
            }
        }
    }

    // ---- P8: 勧誘（固定シード + 回数から決める。同じ状態なら必ず同じ結果） ----

    public int RecruitChance(string speciesId)
    {
        var row = _db.PalPlan.FirstOrDefault(r => r.SpeciesId == speciesId);
        return row?.Category switch { "legendary" => LegendaryRecruitPct, "nonwild" => BossPalRecruitPct, _ => 0 };
    }

    // 0〜99 の決定論的な乱数を1つ引く（SplitMix64。引いた回数を状態に残す）。
    public int NextRecruitRoll()
    {
        if (State.RecruitSeed == 0) State.RecruitSeed = (State.SelectionSeed ^ 0x5DEECE66DL) | 1L;
        ulong z = (ulong)State.RecruitSeed + (ulong)(++State.RecruitRolls) * 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        z ^= z >> 31;
        return (int)(z % 100UL);
    }

    private async Task TryRecruit(DungeonDef d)
    {
        var species = new List<string> { d.Boss.SpeciesId };
        species.AddRange(d.Boss.Group.Select(g => g.SpeciesId));
        foreach (var sp in species.Distinct())
        {
            int pct = RecruitChance(sp);
            if (pct == 0 || State.RecruitedPals.Contains(sp)) continue;
            if (NextRecruitRoll() >= pct) continue;
            State.RecruitedPals.Add(sp);
            OnRecruit?.Invoke(sp);
            if (It.Host != null) await It.Host.Narration($"{SpeciesName(sp)}が仲間になった！");
        }
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
        State.Days++;                                                   // ダンジョンから戻るたびにハコニワの日が進む
        if (State.CoopDungeon == d.Id) { State.CoopSpecies = ""; State.CoopDungeon = ""; }   // 共闘は1回の探索だけ
        if (r.Defeated) State.SetFlag("lost_" + d.Id);   // 負けても進行するボス戦: イベント側で分岐できる
        var a = new EventInterpreter.TriggerArgs(Dungeon: d.Id);
        await It.Fire("boss_defeated", a);
        await It.Fire("dungeon_clear", a);
        if (!r.Defeated && d.Boss?.PostBattle is { } pb)
        {
            // 戦闘後共闘: 次のダンジョンで同行する（まだ未クリアのときだけ）。
            var ch = _db.Character(pb.CoopCharacter);
            if (ch != null && !State.ClearedDungeons.Contains(pb.CoopDungeon))
            { State.CoopSpecies = ch.SpeciesId; State.CoopDungeon = pb.CoopDungeon; }
        }
        if (!r.Defeated && d.PostClear && d.Boss != null) await TryRecruit(d);
        var c = _db.Chart(d.Chart);
        if (c != null && !State.CompletedCharts.Contains(c.Id) && ChartComplete(c)) await CompleteChart(c);
        // 後日解放した補完をクリアした: 思い出メニューには残さず、回収済みにする。
        if (c is { Kind: "supplement" } && State.CompletedCharts.Contains(c.Id) && State.SkippedSupplements.Remove(c.Id))
            State.MemoryCollected.Add(c.Id);
        await AdvanceCharts();   // 隠しダンジョンの結果で立つフラグ（c14_done など）が次のチャートの解放条件になる
        await Drain();
    }

    public async Task CompleteChart(ChartDef c)
    {
        if (!State.CompletedCharts.Add(c.Id)) return;
        await It.Fire("chart_end", new EventInterpreter.TriggerArgs(Chart: c.Id));
        // 継ぐ道だけ実装: チャート19の決断フラグ（と16・17の傾き）を ChoicePath に写す。
        if (c.Id == "c19" && (State.HasFlag("c19_path_tsugu") || State.HasFlag("c16_lean_tsugu") || State.HasFlag("c17_lean_tsugu")))
            State.ChoicePath = "tsugu";
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
