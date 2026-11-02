using System.Collections.Generic;
using System.Linq;
using MysteryDungeon.Combat;
using MysteryDungeon.Species;

namespace MysteryDungeon.Battle;

// 構築画面がまだ技の編集に対応していないので、手持ちの技4つは自動で決める。
//
// 以前は learnset の先頭4件を採っていたが、learnset はレベル順なので
// **先頭はレベル1の技**——威力15の技2つと変化技2つ、という組み合わせに
// なっていた。NPC側は威力90〜135を持つので、実際に回すと自陣は1試合も
// 取れない（20試合0勝）。編成の問題ではなく、この選び方の問題だった。
//
// 規則は Tools/generate_npc_teams.py の pick_moves と同じにしてある。
// 自分と相手で技の選び方が違うと、対戦の結果が「どちらの選び方が良いか」に
// なってしまい、編成やプレイの差が見えなくなる。
public static class DefaultLoadout
{
    // 自属性の最大威力 → 打点の重ならない他属性 → 残りの威力順 → 変化技。
    public static List<string> PickMoves(SpeciesData species, int count)
    {
        var known = new List<MoveData>();
        var seen = new HashSet<string>();
        foreach (var row in species?.Learnset ?? new List<LearnsetEntry>())
        {
            if (!seen.Add(row.MoveId)) continue;
            var m = MoveDatabase.Get(row.MoveId);
            if (m != null) known.Add(m);
        }

        var attacks = known.Where(m => m.Power > 0).OrderByDescending(m => m.Power).ToList();
        var own = new HashSet<string>((species?.Types ?? new List<Element>()).Select(t => t.ToString()));

        var chosen = new List<MoveData>();
        var covered = new HashSet<string>();

        // ① 自属性の最大威力。タイプ一致で伸びるので、これが主力になる。
        var stab = attacks.FirstOrDefault(m => own.Contains(m.Type));
        if (stab != null) { chosen.Add(stab); covered.Add(stab.Type); }

        // ② 打点の重ならない他属性を威力順に。技だけ強くても通らない相手が
        //    出るので、種類を散らすほうを優先する。
        foreach (var m in attacks)
        {
            if (chosen.Count >= count) break;
            if (chosen.Contains(m) || covered.Contains(m.Type)) continue;
            chosen.Add(m);
            covered.Add(m.Type);
        }

        // ③ 埋まらなければ威力順、最後に変化技で埋める。
        foreach (var pool in new[] { attacks, known })
            foreach (var m in pool)
            {
                if (chosen.Count >= count) break;
                if (!chosen.Contains(m)) chosen.Add(m);
            }

        return chosen.Take(count).Select(m => m.Id).ToList();
    }
}
