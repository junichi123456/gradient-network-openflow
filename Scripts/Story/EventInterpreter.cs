using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace MysteryDungeon.Story;

// UI 側が実装する窓口。ヘッドレス試験では記録するだけの実装を差す。
public interface IStoryHost
{
    Task Say(SayLine line);
    Task Narration(string text);
    Task<int> Choice(IReadOnlyList<string> options);
    Task<string> NameInput(string defaultName, int maxLength);
    Task Fade(string mode, double seconds);
    Task Wait(double seconds);
}

public record SayLine(string WhoId, string DisplayName, string SpeciesId, string Text, string Face);

public class StoryException : Exception
{
    public StoryException(string msg) : base(msg) { }
}

// 副作用を持つ op のコールバック（P3 以降で配線）。無ければ状態だけ更新する。
public class StoryCallbacks
{
    public Action<string, int> GiveItem;
    public Action<string> StartScenarioFight;
}

public delegate Task OpHandler(EventInterpreter it, JsonObject step, string eventId);

// イベント列を上から順に実行する単純なインタプリタ。op は辞書登録式。
public class EventInterpreter
{
    public const int MaxCallDepth = 8;
    public const string InnerVoice = "protagonist";

    private readonly StoryDatabase _db;
    public StoryState State { get; }
    public IStoryHost Host { get; }
    public StoryCallbacks Callbacks { get; } = new();
    public Func<string, string> SpeciesName { get; set; } = id => id;
    private readonly Dictionary<string, OpHandler> _ops = new();
    private int _depth;

    public IReadOnlyCollection<string> OpNames => _ops.Keys;
    public void Register(string op, OpHandler h) => _ops[op] = h;

    public EventInterpreter(StoryDatabase db, StoryState state, IStoryHost host)
    {
        _db = db; State = state; Host = host;
        RegisterDefaults();
    }

    // ---- トリガー ----

    public readonly record struct TriggerArgs(string Chart = "", string Dungeon = "", int Floor = 0, int Days = 0);

    // 該当するイベントを全部、データ上の順に実行する。実行したイベントIDを返す。
    public async Task<List<string>> Fire(string type, TriggerArgs args = default)
    {
        var fired = new List<string>();
        foreach (var ev in _db.Events.ToList())
        {
            var t = ev.Trigger;
            if (t == null || t.Type != type) continue;
            if (t.Once && State.FiredEvents.Contains(ev.Id)) continue;
            if (!t.Requires.All(State.HasFlag)) continue;
            if (!Matches(t, args)) continue;
            if (t.Once) State.FiredEvents.Add(ev.Id);   // 途中で中断しても二度目は走らせない
            fired.Add(ev.Id);
            await RunSteps(ev.Steps, ev.Id);
        }
        return fired;
    }

    private static bool Matches(TriggerDef t, TriggerArgs a) => t.Type switch
    {
        "dungeon_clear" or "boss_defeated" => t.Dungeon == a.Dungeon,
        "dungeon_floor" => t.Dungeon == a.Dungeon && t.Floor == a.Floor,
        "chart_start" or "chart_end" => t.Chart == a.Chart,
        "time_passed" => a.Days >= t.Days,
        _ => true,
    };

    // ---- 実行 ----

    public async Task RunEvent(string id)
    {
        var ev = _db.Event(id) ?? throw new StoryException($"イベント '{id}' が無い");
        await RunSteps(ev.Steps, id);
    }

    public async Task RunSteps(JsonArray steps, string eventId)
    {
        foreach (var n in steps)
        {
            var step = n as JsonObject ?? throw new StoryException($"{eventId}: step がオブジェクトでない");
            string op = Str(step, "op");
            if (!_ops.TryGetValue(op, out var h)) throw new StoryException($"{eventId}: 未知の op '{op}'");
            await h(this, step, eventId);
        }
    }

    public static string Str(JsonObject o, string key, string def = "") => o[key]?.GetValue<string>() ?? def;

    // ---- 話者と文の展開 ----

    public string SpeakerMbti(string who)
    {
        switch (who)
        {
            case "protagonist":
                if (State.MbtiType != "") return State.MbtiType;
                return _db.CharacterBySpecies(State.Protagonist)?.Mbti ?? throw new StoryException("主人公のタイプが未確定");
            case "partner":
                return _db.CharacterBySpecies(State.Partner)?.Mbti ?? throw new StoryException("相棒のタイプが未確定");
            default:
                return _db.Character(who)?.Mbti ?? throw new StoryException($"話者 '{who}' のタイプが無い");
        }
    }

    public string SpeakerName(string who) => who switch
    {
        "protagonist" => State.PlayerName != "" ? State.PlayerName : SpeciesName(State.Protagonist),
        "partner" => SpeciesName(State.Partner),
        "narrator" => "",
        _ => _db.Character(who)?.DisplayName ?? throw new StoryException($"話者 '{who}' が無い"),
    };

    public string SpeakerSpecies(string who) => who switch
    {
        "protagonist" => State.Protagonist,
        "partner" => State.Partner,
        "narrator" => "",
        _ => _db.Character(who)?.SpeciesId ?? "",
    };

    private static readonly Regex Ph = new(@"\{([A-Za-z_]+)(?::([^}]*))?\}");

    // {I}/{catch} は speakerWho の型、{player_name}/{protagonist}/{partner}/{name:id} は状態から。
    public string Expand(string text, string speakerWho)
    {
        return Ph.Replace(text, m =>
        {
            string arg = m.Groups[2].Value;
            switch (m.Groups[1].Value)
            {
                case "I": return Voice(speakerWho).FirstPerson;
                case "catch": return Voice(speakerWho).Catch;
                case "player_name": return State.PlayerName != "" ? State.PlayerName : SpeciesName(State.Protagonist);
                case "protagonist": return SpeciesName(State.Protagonist);
                case "partner": return SpeciesName(State.Partner);
                case "name": return _db.Character(arg)?.DisplayName ?? throw new StoryException($"{{name:{arg}}} の人物が無い");
                default: throw new StoryException($"未知のプレースホルダ {m.Value}");
            }
        });
    }

    private VoiceDef Voice(string who)
    {
        string mbti = SpeakerMbti(who);
        return _db.Voices.TryGetValue(mbti, out var v) ? v : throw new StoryException($"voice.json に '{mbti}' が無い");
    }

    // by_type があれば話者の型の行、無ければ text。どちらも無ければ起動時エラー相当。
    public string PickText(JsonObject step, string who, string eventId)
    {
        if (step["by_type"] is JsonObject bt && who != "narrator"
            && bt.TryGetPropertyValue(SpeakerMbti(who), out var v) && v != null)
            return v.GetValue<string>();
        return step["text"]?.GetValue<string>() ?? throw new StoryException($"{eventId}: say に該当タイプの行も text も無い");
    }

    // ---- 条件 ----

    public bool Eval(JsonNode cond)
    {
        if (cond is not JsonObject c) throw new StoryException("if.cond がオブジェクトでない");
        if (c["flag"] != null) return State.HasFlag(c["flag"].GetValue<string>());
        if (c["not_flag"] != null) return !State.HasFlag(c["not_flag"].GetValue<string>());
        if (c["counter"] != null)
        {
            int v = State.GetCounter(c["counter"].GetValue<string>());
            if (c["gte"] != null) return v >= c["gte"].GetValue<int>();
            if (c["lte"] != null) return v <= c["lte"].GetValue<int>();
            if (c["eq"] != null) return v == c["eq"].GetValue<int>();
            throw new StoryException("counter 条件に gte/lte/eq が無い");
        }
        if (c["all"] is JsonArray all) return all.All(Eval);
        if (c["any"] is JsonArray any) return any.Any(Eval);
        throw new StoryException("if.cond が解釈できない");
    }

    // ---- 既定の op ----

    private void RegisterDefaults()
    {
        Register("say", async (it, s, id) =>
        {
            string who = Str(s, "who");
            string text = Expand(PickText(s, who, id), who);
            await Host.Say(new SayLine(who, SpeakerName(who), SpeakerSpecies(who), text, Str(s, "face")));
        });
        Register("narration", async (it, s, id) =>
            await Host.Narration(Expand(Str(s, "text"), Str(s, "who", InnerVoice))));
        Register("choice", async (it, s, id) =>
        {
            var opts = s["options"]?.AsArray() ?? throw new StoryException($"{id}: choice に options が無い");
            var labels = opts.Select(o => Expand(Str(o.AsObject(), "text"), InnerVoice)).ToList();
            int i = await Host.Choice(labels);
            if (i < 0 || i >= opts.Count) throw new StoryException($"{id}: choice の選択が範囲外 ({i})");
            var o = opts[i].AsObject();
            if (o["set_flag"] != null) State.SetFlag(o["set_flag"].GetValue<string>());
            if (o["steps"] is JsonArray sub) await RunSteps(sub, id);
        });
        Register("set_flag", (it, s, id) => { State.SetFlag(Str(s, "flag")); return Task.CompletedTask; });
        Register("inc_counter", (it, s, id) =>
        {
            State.IncCounter(Str(s, "counter"), s["n"]?.GetValue<int>() ?? 1);
            return Task.CompletedTask;
        });
        Register("give_item", (it, s, id) =>
        {
            int n = s["n"]?.GetValue<int>() ?? 1;
            Callbacks.GiveItem?.Invoke(Str(s, "id"), n);
            return Task.CompletedTask;
        });
        Register("unlock_dungeon", (it, s, id) => { State.UnlockedDungeons.Add(Str(s, "id")); return Task.CompletedTask; });
        Register("start_scenario_fight", (it, s, id) => { Callbacks.StartScenarioFight?.Invoke(Str(s, "id")); return Task.CompletedTask; });
        Register("join_resident", (it, s, id) => { State.ResidentsArrived.Add(Str(s, "id")); return Task.CompletedTask; });
        Register("fade", async (it, s, id) => await Host.Fade(Str(s, "mode", "out"), s["sec"]?.GetValue<double>() ?? 0.5));
        Register("wait", async (it, s, id) =>
        {
            double sec = s["sec"]?.GetValue<double>() ?? 0.5;
            // 内向度は「間」にだけ効かせる（任意指定。係数は 内向度/50）。
            if (s["introvert"]?.GetValue<bool>() == true) sec *= State.ProtagonistIntrovertion / 50.0;
            await Host.Wait(sec);
        });
        Register("goto_chart", (it, s, id) => { State.CurrentChart = Str(s, "id"); return Task.CompletedTask; });
        Register("if", async (it, s, id) =>
        {
            var branch = Eval(s["cond"]) ? s["then"] : s["else"];
            if (branch is JsonArray arr) await RunSteps(arr, id);
        });
        Register("name_input", async (it, s, id) =>
        {
            string def = SpeciesName(State.Protagonist);
            int max = s["max"]?.GetValue<int>() ?? 8;
            string name = (await Host.NameInput(def, max))?.Trim() ?? "";
            if (name == "") name = def;
            if (name.Length > max) name = name[..max];
            State.PlayerName = name;
        });
        Register("call_event", async (it, s, id) =>
        {
            if (++_depth > MaxCallDepth) { _depth--; throw new StoryException($"{id}: call_event が深すぎる"); }
            try { await RunEvent(Str(s, "id")); } finally { _depth--; }
        });
    }
}
