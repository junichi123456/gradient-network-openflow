using Godot;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MysteryDungeon.Species;
using MysteryDungeon.Visuals;

namespace MysteryDungeon.Story;

// 夢の4問 → 主人公決定（本人には非表示）→ 相棒3候補から1体。
// 純ロジックは SelectionLogic。ここは表示と入力だけ。呼び出しは await StartNewGame()。
public partial class SelectionScene : CanvasLayer
{
    public async Task StartNewGame(StoryState state, StoryDatabase db)
    {
        Layer = 40;
        var bg = new ColorRect { Color = new Color(0.03f, 0.03f, 0.08f) };
        bg.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(bg);
        var box = new VBoxContainer { Position = new Vector2(120, 60), CustomMinimumSize = new Vector2(700, 0) };
        bg.AddChild(box);

        // 16の影 → 1問ごとに半分に（シルエットのみ。種族は見せない）。
        var shadows = new Label { Text = "■ ■ ■ ■ ■ ■ ■ ■ ■ ■ ■ ■ ■ ■ ■ ■" };
        var scene = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(700, 0) };
        var options = new VBoxContainer();
        box.AddChild(shadows); box.AddChild(scene); box.AddChild(options);

        var letters = new List<string>();
        int remaining = 16;
        foreach (var q in db.Selection.Questions)
        {
            scene.Text = q.Scene;
            int pick = await Pick(options, q.Options.Select(o => o.Text).ToList());
            letters.Add(q.Options[pick].Letter);
            remaining /= 2;
            shadows.Text = string.Join(" ", Enumerable.Repeat("■", remaining));
        }

        long seed = (long)(GD.Randi() << 16) ^ GD.Randi();
        SelectionLogic.ApplyAnswers(state, db, letters, seed);

        // 相棒候補（主人公は表示しない。候補3体のみスプライトで見せる）。
        scene.Text = "影のひとつが、あなたに寄りそう。だれと歩く？";
        var cands = SelectionLogic.CompanionCandidates(db, state.Protagonist,
            id => SpeciesDatabase.Instance.Require(id).Types[0].ToString(), seed);
        int c = await Pick(options, cands.Select(id => SpeciesDatabase.Instance.Require(id).DisplayName).ToList());
        SelectionLogic.ApplyPartner(state, cands[c]);
        QueueFree();
    }

    private async Task<int> Pick(VBoxContainer holder, List<string> labels)
    {
        var tcs = new TaskCompletionSource<int>();
        foreach (Node n in holder.GetChildren()) n.QueueFree();
        for (int i = 0; i < labels.Count; i++)
        {
            int idx = i;
            var b = new Button { Text = labels[i] };
            b.Pressed += () => tcs.TrySetResult(idx);
            holder.AddChild(b);
        }
        (holder.GetChild(0) as Button)?.GrabFocus();
        return await tcs.Task;
    }
}
