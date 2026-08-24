using System.Collections.Generic;
using System.Linq;
using Godot;
using MysteryDungeon.Combat;
using MysteryDungeon.Dungeon;
using MysteryDungeon.Entities;
using MysteryDungeon.Species;

namespace MysteryDungeon.Battle;

// 戦術（構築の型）。1チームは2つを兼ねる——「基本的に2つの戦い方を取れる
// 6匹を採用する」という前提を、そのまま構築の生成条件にしている。
public enum Tactic
{
    Guardian,   // 仁王立ち型: 耐久で前を止め、後ろから遠距離で削る
    Burst,      // ワンサイクルショット型: 最初のサイクルに最大火力をぶつける
    Control,    // コントロール型: 耐久＋搦め手でジリ貧に持ち込む
    HitAway,    // ヒットアンドアウェイ型: クラッシュのノックバックと攻撃後移動で距離を取る
    Weather,    // 天候型: 特性で天候を取り、天候補正で押す
}

// 戦術にもとづく構築の組み立て。
//
// §20〜§23までの構築生成（MetaScenario）は「種族値の高い順に無作為」
// だったので、耐久・搦め手・ノックバックといった**戦い方の違い**は
// 構築に現れなかった。ここでは5つの戦術それぞれに「どの種族・技・持ち物が
// 向くか」の物差しを与え、2戦術を兼ねる6匹を組む。
//
// 287種ぶんの集計（最大威力・射程の内訳・状態異常技の数・特性の天候など）は
// 起動時に1度だけ作って使い回す。1試合ごとに全チームが組み直すため、
// ここを毎回引き直すと1万試合規模では効かなくなる。
public static class TacticalBuilder
{
    // ---- 種族ごとの前計算 ----

    public sealed class Profile
    {
        public string Id;
        public SpeciesData Sp;
        public int Bst, Hp, Atk, Def;
        public List<Element> Types;
        public bool Legendary;

        public WeatherType TraitWeather;   // 特性が連れてくる天候（None=無し）
        public bool AttackThenMove;        // ふわふわ／ゆきすべり（攻撃後に1マス移動）
        public WeatherType MoveWeather;    // 攻撃後移動が効く天候（Gale/Snow）

        public List<MoveData> Attacks;     // 威力>0
        public List<MoveData> Statuses;    // 威力0（搦め手）
        public float[] BestPowerByType;    // 属性ごとの最大威力（相性計算用）
        public int MaxPower;
        public int CrushCount, RangedCount, AoeCount, AilmentCount, SlipCount, TrickCount;

        public float[] Fit = new float[5]; // 戦術ごとの適性 0..1
    }

    private static Dictionary<string, Profile> _profiles;
    private static float[,] _mult;              // 属性相性 9x9（毎回引くと重い）
    private static List<string> _heldItems;

    private static readonly Element[] Elements =
        { Element.Neutral, Element.Fire, Element.Water, Element.Grass,
          Element.Electric, Element.Ground, Element.Ice, Element.Dragon, Element.Dark };

    public static IReadOnlyDictionary<string, Profile> Profiles
    {
        get { EnsureBuilt(); return _profiles; }
    }

    private static void EnsureBuilt()
    {
        if (_profiles != null) return;

        _mult = new float[9, 9];
        for (int a = 0; a < 9; a++)
            for (int d = 0; d < 9; d++)
                _mult[a, d] = TypeChartManager.GetMultiplier(
                    Elements[a].ToString(), Elements[d].ToString());

        _heldItems = ItemDatabase.AllIds()
            .Where(id => ItemDatabase.Get(id)?.Type == ItemType.BattleHeld).ToList();

        _profiles = new Dictionary<string, Profile>();
        foreach (var sp in SpeciesDatabase.Instance?.All.Values ?? Enumerable.Empty<SpeciesData>())
            _profiles[sp.SpeciesId] = BuildProfile(sp);

        Normalize();
    }

    private static Profile BuildProfile(SpeciesData sp)
    {
        var seen = new HashSet<string>();
        var moves = new List<MoveData>();
        foreach (var row in sp.Learnset ?? new List<LearnsetEntry>())
        {
            if (!seen.Add(row.MoveId)) continue;
            var m = MoveDatabase.Get(row.MoveId);
            if (m != null) moves.Add(m);
        }

        var trait = TraitDatabase.Get(sp.Trait);
        var p = new Profile
        {
            Id = sp.SpeciesId, Sp = sp,
            Hp = sp.BaseHP, Atk = sp.BaseAtk, Def = sp.BaseDef,
            Bst = sp.BaseHP + sp.BaseAtk + sp.BaseDef,
            Types = sp.Types ?? new List<Element>(),
            Legendary = sp.IsLegendary,
            TraitWeather = trait?.WeatherOnEntry ?? WeatherType.None,
            AttackThenMove = sp.Trait == "fuwafuwa" || sp.Trait == "yukisuberi",
            MoveWeather = sp.Trait == "fuwafuwa" ? WeatherType.Gale
                        : sp.Trait == "yukisuberi" ? WeatherType.Snow : WeatherType.None,
            Attacks = moves.Where(m => m.Power > 0).ToList(),
            Statuses = moves.Where(m => m.Power <= 0).ToList(),
            BestPowerByType = new float[9],
        };

        foreach (var m in p.Attacks)
        {
            int ti = System.Array.FindIndex(Elements, e => e.ToString() == m.Type);
            if (ti >= 0) p.BestPowerByType[ti] = Mathf.Max(p.BestPowerByType[ti], m.Power);
            if (m.Power > p.MaxPower) p.MaxPower = m.Power;
            if (m.WeaponTag == WeaponTag.Crush) p.CrushCount++;
            if (m.Range != MoveRange.Adjacent) p.RangedCount++;
            if (m.Range is MoveRange.Area or MoveRange.Room or MoveRange.FullFloor) p.AoeCount++;
            if (m.AilmentEffect != AilmentType.None) p.AilmentCount++;
            if (m.AilmentEffect is AilmentType.Poison or AilmentType.Toxic or AilmentType.Burn) p.SlipCount++;
        }
        // 搦め手: ランク変化・状態異常・設置のいずれかを持つ変化技。
        p.TrickCount = p.Statuses.Count(m =>
            m.RankEffectStat != RankStat.None || (m.RankEffects?.Count ?? 0) > 0
            || m.AilmentEffect != AilmentType.None || m.FieldEffect != FieldType.None);

        return p;
    }

    // 適性は「その戦術の中で相対的にどれだけ向くか」なので、素点を出してから
    // 全種族の最大値で割って 0..1 に均す（戦術どうしの重みを揃えるため）。
    private static void Normalize()
    {
        var all = _profiles.Values.ToList();
        float maxBstDur = all.Max(p => p.Hp + p.Def * 1.4f);
        float maxPower = Mathf.Max(1, all.Max(p => p.MaxPower));
        float maxAtk = Mathf.Max(1, all.Max(p => p.Atk));

        foreach (var p in all)
        {
            float atkCount = Mathf.Max(1, p.Attacks.Count);
            float durability = (p.Hp + p.Def * 1.4f) / maxBstDur;
            float rangedShare = p.RangedCount / atkCount;
            float aoeShare = p.AoeCount / atkCount;
            float crushShare = p.CrushCount / atkCount;
            float ailShare = p.AilmentCount / atkCount;

            // 仁王立ち: 耐久で受け止める前列と、後ろから撃つ遠距離。
            p.Fit[(int)Tactic.Guardian] = 0.62f * durability + 0.38f * rangedShare;

            // ワンサイクルショット: 1サイクル目に出せる最大打点。範囲技は
            // 1手で複数を削れるぶん「1サイクルの総火力」に効く。
            p.Fit[(int)Tactic.Burst] =
                0.55f * (p.MaxPower / maxPower) + 0.25f * (p.Atk / maxAtk) + 0.20f * aoeShare;

            // コントロール: 落とされない耐久＋状態異常・搦め手の手数。
            p.Fit[(int)Tactic.Control] =
                0.38f * durability + 0.34f * Mathf.Min(1f, ailShare * 2f)
                + 0.18f * Mathf.Min(1f, p.TrickCount / 5f)
                + 0.10f * Mathf.Min(1f, p.SlipCount / 3f);

            // ヒットアンドアウェイ: 攻撃後移動の特性が本体。クラッシュの
            // ノックバックと遠距離技がそれを補う。
            p.Fit[(int)Tactic.HitAway] =
                0.50f * (p.AttackThenMove ? 1f : 0f)
                + 0.28f * Mathf.Min(1f, crushShare * 2.5f) + 0.22f * rangedShare;

            // 天候: 特性で天候を持ち込めるかがほぼ全て。天候技でも張れるが
            // 1ターン消費するので価値は下げる。
            bool hasWeatherMove = p.Statuses.Any(m => m.WeatherEffect != WeatherType.None);
            p.Fit[(int)Tactic.Weather] =
                0.70f * (p.TraitWeather != WeatherType.None ? 1f : 0f)
                + 0.18f * (hasWeatherMove ? 1f : 0f)
                + 0.12f * (p.MaxPower / maxPower);
        }
    }

    // ---- 相性（相手の6匹に対する通り） ----

    private static float Mult(Element atk, IReadOnlyList<Element> def)
    {
        if (def == null || def.Count == 0) return 1f;
        float m = 1f;
        int ai = (int)atk;
        foreach (var d in def) m *= _mult[ai, (int)d];
        return m;
    }

    // 候補が相手構築にどれだけ噛み合うか（0..1）。攻撃の通りやすさと
    // 被弾のしにくさの両方を見る——§16の選出スコアと同じ考え方を、
    // 1対1ではなく1対6へ広げたもの。
    private static float Counter(Profile me, List<List<Element>> foeTypes)
    {
        if (foeTypes.Count == 0) return 0.5f;

        float offense = 0f, incoming = 0f;
        foreach (var ft in foeTypes)
        {
            float best = 0f;
            for (int t = 0; t < 9; t++)
            {
                if (me.BestPowerByType[t] <= 0f) continue;
                best = Mathf.Max(best, me.BestPowerByType[t] * Mult(Elements[t], ft));
            }
            offense += best;

            float worst = 0f;
            foreach (var t in ft) worst = Mathf.Max(worst, Mult(t, me.Types));
            incoming += worst;
        }
        offense /= foeTypes.Count;
        incoming /= foeTypes.Count;

        float off = Mathf.Min(1f, offense / 200f);              // 威力200相当で頭打ち
        float def = Mathf.Clamp((2f - incoming) / 1.75f, 0f, 1f);
        return 0.60f * off + 0.40f * def;
    }

    // ---- 構築を組む ----

    // 相手の6匹（種族のみ）を見て、2戦術に沿った6匹を組み直す。
    // memory は「この種族を入れた試合で勝てたか」の自己評価（-1..+1）で、
    // チームごとに独立して育つ。
    public static BattleTeam Build(Tactic a, Tactic b, IReadOnlyList<string> foeSpecies,
                                   IReadOnlyDictionary<string, float> memory,
                                   RandomNumberGenerator rng)
    {
        EnsureBuilt();

        var foeTypes = (foeSpecies ?? new List<string>())
            .Select(id => _profiles.TryGetValue(id, out var p) ? p.Types : null)
            .Where(t => t != null && t.Count > 0)
            .ToList();

        var scored = new List<(Profile P, float S)>(_profiles.Count);
        foreach (var p in _profiles.Values)
        {
            float fa = p.Fit[(int)a], fb = p.Fit[(int)b];
            float arche = Mathf.Max(fa, fb) + 0.35f * Mathf.Min(fa, fb);
            float mem = memory != null && memory.TryGetValue(p.Id, out var mv) ? mv : 0f;
            float jitter = rng != null ? rng.Randf() * 0.10f : 0f;
            scored.Add((p, 1.00f * arche + 0.90f * Counter(p, foeTypes) + 0.45f * mem + jitter));
        }
        scored.Sort((x, y) => y.S.CompareTo(x.S));

        var picked = new List<Profile>();
        bool hasLegendary = false;
        foreach (var (p, _) in scored)
        {
            if (picked.Count >= BattleTeam.RosterSize) break;
            if (p.Legendary && hasLegendary) continue;   // 伝説は1構築に1体まで（§6）
            picked.Add(p);
            if (p.Legendary) hasLegendary = true;
        }

        // チームの天候は「weather_on_entry を持つ中で最も種族値が低い1体」が
        // 決める——低種族値側が最後に発動して上書き勝ちする規則
        // （BattleArena.ApplyEntryWeather）を、構築側でもそのまま織り込む。
        var weather = picked.Where(p => p.TraitWeather != WeatherType.None)
                            .OrderBy(p => p.Bst)
                            .Select(p => p.TraitWeather)
                            .FirstOrDefault();

        var used = new HashSet<string>();
        var entries = new List<BattleEntry>();
        foreach (var p in picked)
        {
            var t = p.Fit[(int)a] >= p.Fit[(int)b] ? a : b;   // その個体が担う側の戦術
            entries.Add(new BattleEntry
            {
                SpeciesId = p.Id,
                MoveIds = PickMoves(p, t, foeTypes, weather),
                ItemId = PickItem(p, t, used),
            });
        }
        return new BattleTeam(entries);
    }

    // 技は「相手への通り × 命中」を土台に、担う戦術ぶんの重みを足して4つ選ぶ。
    private static List<string> PickMoves(Profile p, Tactic t, List<List<Element>> foeTypes,
                                          WeatherType weather)
    {
        float Value(MoveData m)
        {
            int ti = System.Array.FindIndex(Elements, e => e.ToString() == m.Type);
            float mult = 1f;
            if (ti >= 0 && foeTypes.Count > 0)
                mult = foeTypes.Average(ft => Mult(Elements[ti], ft));

            float v = m.Power * mult * (m.Accuracy / 100f);

            switch (t)
            {
                case Tactic.Guardian:
                    if (m.Range != MoveRange.Adjacent) v *= 1.30f;   // 後列から撃てる
                    break;
                case Tactic.Burst:
                    if (m.Range is MoveRange.Area or MoveRange.Room) v *= 1.20f;
                    break;
                case Tactic.HitAway:
                    if (m.WeaponTag == WeaponTag.Crush) v *= 1.45f;  // ノックバックで離れる
                    else if (m.Range != MoveRange.Adjacent) v *= 1.20f;
                    break;
                case Tactic.Weather:
                    // 天候補正が乗る属性を優先する（AttackAction の倍率と同じ対応）。
                    if (weather == WeatherType.Sunny && m.Type == "Fire") v *= 1.50f;
                    else if (weather == WeatherType.Rain && m.Type == "Water") v *= 1.50f;
                    else if (weather == WeatherType.Snow && m.Type == "Ice") v *= 1.25f;
                    else if (weather == WeatherType.Gale && m.WeaponTag == WeaponTag.Wind) v *= 1.25f;
                    break;
            }
            return v;
        }

        var chosen = new List<string>();

        // コントロールは搦め手に枠を割く（状態異常・ランク変化・設置）。
        // 攻撃手段が0になると置物になるので、攻撃枠は必ず2つ以上残す。
        if (t == Tactic.Control)
        {
            var tricks = p.Statuses
                .Where(m => m.RankEffectStat != RankStat.None || (m.RankEffects?.Count ?? 0) > 0
                            || m.AilmentEffect != AilmentType.None || m.FieldEffect != FieldType.None)
                .Take(2).Select(m => m.Id);
            chosen.AddRange(tricks);

            // 状態異常を撒ける攻撃技を1つ優先で確保する（スリップの起点）。
            var slip = p.Attacks
                .Where(m => m.AilmentEffect is AilmentType.Poison or AilmentType.Toxic or AilmentType.Burn)
                .OrderByDescending(Value).FirstOrDefault();
            if (slip != null && chosen.Count < MoveManager.MaxMoves) chosen.Add(slip.Id);
        }

        foreach (var m in p.Attacks.OrderByDescending(Value))
        {
            if (chosen.Count >= MoveManager.MaxMoves) break;
            if (!chosen.Contains(m.Id)) chosen.Add(m.Id);
        }
        // それでも埋まらなければ変化技で埋める（攻撃技が少ない種族向けの保険）。
        foreach (var m in p.Statuses)
        {
            if (chosen.Count >= MoveManager.MaxMoves) break;
            if (!chosen.Contains(m.Id)) chosen.Add(m.Id);
        }
        return chosen.Take(MoveManager.MaxMoves).ToList();
    }

    // 持ち物は戦術ごとの優先順で、チーム内の重複を避けて配る。
    private static readonly Dictionary<Tactic, string[]> ItemPriority = new()
    {
        [Tactic.Guardian] = new[] { "iron_plate", "mind_plate", "guard_tonic_50", "area_aegis",
                                    "wide_ward", "guard_tonic_25", "endure_charm" },
        [Tactic.Burst]    = new[] { "power_lens", "focus_lens", "crit_shell", "endure_charm",
                                    "rank_anchor", "guard_tonic_25" },
        [Tactic.Control]  = new[] { "regen_band", "purge_band", "cure_bell", "rank_anchor",
                                    "room_mirror", "guard_tonic_50" },
        [Tactic.HitAway]  = new[] { "focus_lens", "power_lens", "endure_charm", "crit_shell",
                                    "guard_tonic_25", "weakness_shell" },
        [Tactic.Weather]  = new[] { "weakness_shell", "guard_tonic_25", "regen_band",
                                    "mind_plate", "iron_plate", "cure_bell" },
    };

    private static string PickItem(Profile p, Tactic t, HashSet<string> used)
    {
        foreach (var id in ItemPriority[t])
            if (!used.Contains(id)) { used.Add(id); return id; }
        foreach (var id in _heldItems)
            if (!used.Contains(id)) { used.Add(id); return id; }
        return null;
    }
}
