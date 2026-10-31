using System.Collections.Generic;
using System.Linq;
using Godot;
using MysteryDungeon.Battle;
using MysteryDungeon.Combat;
using MysteryDungeon.Species;
using MysteryDungeon.Entities;
using static MysteryDungeon.UI.Battle.BattleUiKit;

namespace MysteryDungeon.UI.Battle;

// 構築画面。対戦中に変更できない項目（6匹・技4つ・持ち物1つ）をすべて決める。
//
// 形はパーティ編成画面の定石にならう。**左に6枠の並び、右に選んだ1匹の中身。**
// 枠を選ぶと右が丸ごと入れ替わるので、6匹を見比べながら1匹を作り込める。
// 別画面へ飛ばさないのは、技を差し替えた結果が他の5匹とどう噛み合うかを
// 見ながら決めるものだから——飛ばすと毎回戻って確認することになる。
//
// 右側（1匹の中身）は4段。
//   種族  … 名前・属性・役割・種族値・特性。押すと種族選択へ
//   突破値・気質 … 6能力に合計55まで振る（各25まで）。気質は上昇・下降の2つ。
//           実数値（Lv50）を見ながら決める。種族を変えると自動配分に戻る
//   技    … **learnset 全部**から4つ。習得レベルは表示するだけで、選べる
//           かどうかには関係しない（対人戦はレベルキャップを無視する）
//   持ち物… 16種から1つ。他の枠が持っていれば持ち替えになる
//
// 構築の禁止事項は下部に常時表示し、違反した瞬間に赤へ転じる
// （BattleTeam.Validate をそのまま出す）。
public partial class TeamBuildScreen : Control
{
    // 構築を確定して選出へ進む。規則を満たしていないと押せない。
    [Signal] public delegate void BuildConfirmedEventHandler();
    // 種族を選び直したい。枠の番号を渡す。
    [Signal] public delegate void SpeciesPickRequestedEventHandler(int slot);

    private BattleTeam _team;
    private int _slot;                     // いま編集している枠

    private VBoxContainer _roster, _detail;
    private HBoxContainer _rules;
    private Label _count;
    private Button _ready;

    public int Slot => _slot;

    public void Initialize(BattleTeam team)
    {
        _team = team;
        BuildLayout();
        Refresh();
    }

    private void BuildLayout()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var bg = new ColorRect { Color = BattleTheme.Ground };
        bg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(bg);

        var root = Col(8);
        root.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        root.OffsetLeft = 10; root.OffsetTop = 6;
        root.OffsetRight = -10; root.OffsetBottom = -6;
        AddChild(root);

        root.AddChild(TopBar("パーティ構築", out var bar));
        _count = Text("0 / 6 匹", BattleTheme.Muted, BattleTheme.FontLabel);
        bar.AddChild(_count);
        bar.AddChild(Spacer());
        _ready = new Button { Text = "対戦を受け付ける" };
        _ready.Pressed += () => EmitSignal(SignalName.BuildConfirmed);
        bar.AddChild(_ready);

        var split = Row(10);
        split.SizeFlagsVertical = SizeFlags.ExpandFill;
        root.AddChild(split);

        // 左: 6枠。幅を固定して、右の中身が伸び縮みしても位置が動かないように。
        var left = Col(6);
        left.CustomMinimumSize = new Vector2(250, 0);
        left.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
        left.AddChild(Text("登録6匹", BattleTheme.Muted, BattleTheme.FontLabel));
        _roster = Col(5);
        left.AddChild(_roster);
        split.AddChild(left);

        // 右: 選んだ1匹の中身。技の一覧が長いので全体をスクロールさせる。
        var scroll = new ScrollContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        _detail = Col(8);
        _detail.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.AddChild(_detail);
        split.AddChild(scroll);

        _rules = Row(14);
        root.AddChild(_rules);
    }

    public void Select(int slot)
    {
        if (slot < 0 || slot >= _team.Entries.Count) return;
        _slot = slot;
        Refresh();
    }

    public void Refresh()
    {
        BattleUiKit.ClearChildren(_roster);
        for (int i = 0; i < _team.Entries.Count; i++) _roster.AddChild(SlotCard(i));

        BattleUiKit.ClearChildren(_detail);
        BuildDetail();

        _count.Text = $"{_team.Entries.Count} / {BattleTeam.RosterSize} 匹";

        // 規則の充足は Validate() の結果をそのまま出す。UI側で判定を
        // 書き直すと、生成器と検証器が食い違ったときと同じ事故になる。
        var errors = _team.Validate();
        BattleUiKit.ClearChildren(_rules);
        _rules.AddChild(RuleChip("同一種族なし", !errors.Any(m => m.Contains("同一種族"))));
        _rules.AddChild(RuleChip("持ち物の重複なし", !errors.Any(m => m.Contains("持ち物の重複"))));
        _rules.AddChild(RuleChip("全員が learnset 内の技4つ",
                                 !errors.Any(m => m.Contains("learnset外") || m.Contains("つまで")
                                                  || m.Contains("技が1つも"))));
        _rules.AddChild(RuleChip($"{BattleTeam.RosterSize}匹の登録",
                                 !errors.Any(m => m.Contains("匹ちょうど"))));
        _rules.AddChild(RuleChip("「伝説」は1体まで", !errors.Any(m => m.Contains("伝説"))));
        _rules.AddChild(RuleChip("突破値・気質が規則内",
                                 !errors.Any(m => m.Contains("突破値") || m.Contains("気質"))));

        _ready.Disabled = errors.Count > 0;
    }

    // ---- 左: 6枠 ----

    // 1枠ぶん。押すと右側がその枠に入れ替わる。技名まで出すのは、
    // 6匹を見比べて穴（属性の偏り・威力帯）を探すのが構築だから。
    private Control SlotCard(int index)
    {
        var entry = _team.Entries[index];
        bool on = index == _slot;
        var card = ClickableCard(on ? BattleTheme.BrassBg : BattleTheme.Surface,
                                 on ? BattleTheme.Brass : BattleTheme.Line);
        card.CustomMinimumSize = new Vector2(0, 84);
        // 技名は長いものがある。切らないと枠の幅を押し広げ、右の詳細の下へ
        // はみ出す（列に幅を与えても中身の最小幅が勝つ）。
        card.ClipContents = true;
        int i = index;
        card.Pressed += () => Select(i);

        var col = Col(2);
        BattleUiKit.AddFilled(card, col);

        var sp = entry.Species;
        var head = Row(6);
        head.AddChild(Text($"{index + 1}", BattleTheme.Muted, BattleTheme.FontLabel));
        head.AddChild(Text(sp?.DisplayName ?? entry.SpeciesId, BattleTheme.Ink, BattleTheme.FontSmall));
        if (sp != null) foreach (var t in sp.Types) head.AddChild(ElementChip(t));
        if (sp != null && sp.IsLegendary) head.AddChild(Pill("伝説", BattleTheme.Brass, BattleTheme.BrassBg));
        head.AddChild(Spacer());
        if (sp != null)
            head.AddChild(Text($"BST {sp.Bst}",
                               BattleTheme.Muted, BattleTheme.FontLabel));
        col.AddChild(head);

        // 技は2行に畳んで並べる。名前だけで属性は色で見せる。
        var names = entry.MoveIds.Select(m => MoveDatabase.Get(m)).Where(m => m != null).ToList();
        for (int r = 0; r < 2; r++)
        {
            var line = Row(5);
            foreach (var m in names.Skip(r * 2).Take(2))
                line.AddChild(Fixed(m.Name, 108, BattleTheme.Element(m.Type)));
            col.AddChild(line);
        }

        var it = string.IsNullOrEmpty(entry.ItemId) ? null : ItemDatabase.Get(entry.ItemId);
        var itemRow = Row(8);
        itemRow.AddChild(Text(it?.Name ?? "持ち物なし",
                              it == null ? BattleTheme.Muted : BattleTheme.Brass, 10));
        itemRow.AddChild(Spacer());
        var nat = (entry.Spread ?? StatSpread.Zero()).NatureLabel;
        itemRow.AddChild(Text(nat, nat == "無補正" ? BattleTheme.Muted : BattleTheme.Ink2, 10));
        col.AddChild(itemRow);
        return card;
    }

    // ---- 右: 1匹の中身 ----

    private void BuildDetail()
    {
        var entry = _team.Entries.ElementAtOrDefault(_slot);
        if (entry == null) return;

        _detail.AddChild(SpeciesPanel(entry));
        _detail.AddChild(SpreadPanel(entry));
        _detail.AddChild(MovePanel(entry));
        _detail.AddChild(ItemPanel(entry));
    }

    // 種族。名前・属性・種族値・特性。押すと種族選択画面へ。
    private Control SpeciesPanel(BattleEntry entry)
    {
        var card = Card(BattleTheme.Surface, BattleTheme.Line);
        var col = Col(4);
        card.AddChild(col);

        var sp = entry.Species;
        var head = Row(8);
        head.AddChild(Text(sp?.DisplayName ?? entry.SpeciesId, BattleTheme.Ink, BattleTheme.FontTitle));
        if (sp != null) foreach (var t in sp.Types) head.AddChild(ElementChip(t));
        if (sp != null && sp.IsLegendary) head.AddChild(Pill("伝説", BattleTheme.Brass, BattleTheme.BrassBg));
        if (sp != null && sp.RoleLabel != "") head.AddChild(Pill(sp.RoleLabel, BattleTheme.Ink2, BattleTheme.Sunk));
        head.AddChild(Spacer());
        var swap = new Button { Text = "種族を変える" };
        swap.Pressed += () => EmitSignal(SignalName.SpeciesPickRequested, _slot);
        head.AddChild(swap);
        col.AddChild(head);

        if (sp != null)
        {
            var stats = Row(12);
            stats.AddChild(StatChip("HP", sp.BaseHP));
            stats.AddChild(StatChip("攻撃", sp.BaseAtk));
            stats.AddChild(StatChip("防御", sp.BaseDef));
            stats.AddChild(StatChip("魔力", sp.BaseSpAtk));
            stats.AddChild(StatChip("精神", sp.BaseSpDef));
            stats.AddChild(StatChip("俊敏", sp.BaseSpeed));
            stats.AddChild(StatChip("合計", sp.Bst));
            stats.AddChild(Spacer());
            stats.AddChild(Text("レベル50固定", BattleTheme.Muted, BattleTheme.FontLabel));
            col.AddChild(stats);

            var trait = TraitDatabase.Get(sp.Trait);
            if (trait != null)
            {
                var tr = Row(6);
                bool hasAlt = !string.IsNullOrEmpty(sp.Trait2);
                tr.AddChild(Pill(hasAlt ? (entry.UseTrait2 ? "特性(無効)" : "特性(有効)") : "特性",
                                 BattleTheme.Muted, BattleTheme.Sunk));
                tr.AddChild(Text(trait.Name, entry.UseTrait2 && hasAlt ? BattleTheme.Muted : BattleTheme.Ink, BattleTheme.FontSmall));
                col.AddChild(tr);
                var desc = Text(trait.Description ?? "", BattleTheme.Muted, BattleTheme.FontLabel);
                desc.AutowrapMode = TextServer.AutowrapMode.WordSmart;
                col.AddChild(desc);
            }

            // 両刀攻撃の調査用: 2つめの特性があるときだけもう1行足す。
            var trait2 = string.IsNullOrEmpty(sp.Trait2) ? null : TraitDatabase.Get(sp.Trait2);
            if (trait2 != null)
            {
                var tr2 = Row(6);
                tr2.AddChild(Pill(entry.UseTrait2 ? "追加特性(有効)" : "追加特性(無効)", BattleTheme.Muted, BattleTheme.Sunk));
                tr2.AddChild(Text(trait2.Name, entry.UseTrait2 ? BattleTheme.Ink : BattleTheme.Muted, BattleTheme.FontSmall));
                tr2.AddChild(Spacer());
                // 元の特性と追加特性は同時には効かない。どちらを使うかをここで選ぶ。
                var toggle = new Button { Text = entry.UseTrait2 ? "元の特性にする" : "追加特性にする" };
                toggle.Pressed += () => { _team.SetUseTrait2(_slot, !entry.UseTrait2); Refresh(); };
                tr2.AddChild(toggle);
                col.AddChild(tr2);
                var desc2 = Text(trait2.Description ?? "", BattleTheme.Muted, BattleTheme.FontLabel);
                desc2.AutowrapMode = TextServer.AutowrapMode.WordSmart;
                col.AddChild(desc2);
            }
        }
        return card;
    }

    // ---- 突破値・気質 ----

    private static readonly string[] StatLabels = { "HP", "攻撃", "防御", "魔力", "精神", "俊敏" };

    // 能力番号 0=HP 1=攻撃 2=防御 3=魔力 4=精神 5=俊敏 で突破値を読み書きする。
    public static int GetBt(StatSpread s, int i) => i switch
    {
        0 => s.Hp, 1 => s.Atk, 2 => s.Def, 3 => s.SpAtk, 4 => s.SpDef, _ => s.Speed,
    };

    public static void SetBt(StatSpread s, int i, int v)
    {
        switch (i)
        {
            case 0: s.Hp = v; break;
            case 1: s.Atk = v; break;
            case 2: s.Def = v; break;
            case 3: s.SpAtk = v; break;
            case 4: s.SpDef = v; break;
            default: s.Speed = v; break;
        }
    }

    // 突破値を delta だけ動かした結果。0〜25 と、合計55に残っているポイントで頭打ち。
    // 例: 残り3で +5 を押すと +3 になる。
    public static int ClampedBt(StatSpread s, int i, int delta)
    {
        int cur = GetBt(s, i);
        int remaining = StatSpread.TotalMax - s.Total;
        int max = System.Math.Min(StatSpread.PerStatMax, cur + System.Math.Max(0, remaining));
        return System.Math.Clamp(cur + delta, 0, System.Math.Max(cur, max));
    }

    // 気質の倍率(×10)。HP(0) は常に10。
    public static int NatureMul10(StatSpread s, int i)
    {
        if (i == 0 || s.NatureUp == s.NatureDown) return 10;
        if ((int)s.NatureUp == i) return 11;
        if ((int)s.NatureDown == i) return 9;
        return 10;
    }

    // 画面に出す実数値（Lv50）。EntityStats と同じ式。
    public static int RealValue(SpeciesData sp, StatSpread s, int i)
    {
        const int level = 50;
        if (i == 0) return sp.BaseHP * 2 * level / 100 + level + 10 + s.Hp;
        int b = i switch { 1 => sp.BaseAtk, 2 => sp.BaseDef, 3 => sp.BaseSpAtk, 4 => sp.BaseSpDef, _ => sp.BaseSpeed };
        return StatSpread.RealStat(b, level, GetBt(s, i), NatureMul10(s, i));
    }

    // 画面の変更は Spread を複製して書き換え、SetSpread で差し込む。
    private void ApplySpread(StatSpread s)
    {
        _team.SetSpread(_slot, s);
        Refresh();
    }

    private Control SpreadPanel(BattleEntry entry)
    {
        var card = Card(BattleTheme.Surface, BattleTheme.Line);
        var col = Col(4);
        card.AddChild(col);

        var sp = entry.Species;
        var spread = entry.Spread ?? StatSpread.Zero();
        int remaining = StatSpread.TotalMax - spread.Total;

        var head = Row(8);
        head.AddChild(Text("突破値・気質", BattleTheme.Ink, BattleTheme.FontBody));
        head.AddChild(Pill($"残り {remaining}",
                           remaining == 0 ? BattleTheme.Brass
                           : remaining < 0 ? BattleTheme.Crit : BattleTheme.Muted,
                           remaining < 0 ? BattleTheme.FoeBg : BattleTheme.Sunk));
        head.AddChild(Spacer());
        head.AddChild(Text("実数値 = 気質(×1.1/×0.9)の後に突破値を加算。各25まで・合計55まで",
                           BattleTheme.Muted, BattleTheme.FontLabel));
        col.AddChild(head);

        // 見出し行
        var legend = Row(6);
        legend.AddChild(Fixed("能力", 40, BattleTheme.Muted));
        legend.AddChild(Fixed("種族値", 46, BattleTheme.Muted));
        legend.AddChild(Fixed("突破値", 46, BattleTheme.Muted));
        legend.AddChild(Fixed("調整", 190, BattleTheme.Muted));
        legend.AddChild(Fixed("実数値", 60, BattleTheme.Muted));
        col.AddChild(legend);

        for (int i = 0; i < 6; i++)
        {
            int stat = i;
            var row = Row(6);
            // 気質の上昇は緑、下降は赤で、ラベルと実数値に色を付ける。
            Color tint = NatureMul10(spread, i) == 11 ? BattleTheme.Ok
                       : NatureMul10(spread, i) == 9 ? BattleTheme.Crit : BattleTheme.Ink;
            string mark = NatureMul10(spread, i) == 11 ? "↑" : NatureMul10(spread, i) == 9 ? "↓" : "";
            row.AddChild(Fixed(StatLabels[i] + mark, 40, tint));
            int baseVal = sp == null ? 0 : i switch
            {
                0 => sp.BaseHP, 1 => sp.BaseAtk, 2 => sp.BaseDef, 3 => sp.BaseSpAtk, 4 => sp.BaseSpDef, _ => sp.BaseSpeed,
            };
            row.AddChild(Fixed(baseVal.ToString(), 46, BattleTheme.Ink2));
            int bt = GetBt(spread, i);
            row.AddChild(Fixed(bt.ToString(), 46, bt > StatSpread.PerStatMax || bt < 0 ? BattleTheme.Crit : BattleTheme.Ink));

            var btns = Row(3);
            btns.CustomMinimumSize = new Vector2(190, 0);
            foreach (int d in new[] { -5, -1, 1, 5 })
            {
                int delta = d;
                var b = new Button { Text = delta > 0 ? $"+{delta}" : $"{delta}" };
                b.CustomMinimumSize = new Vector2(44, 0);
                // 押しても値が変わらない方向は押せなくする。
                b.Disabled = ClampedBt(spread, stat, delta) == bt;
                b.Pressed += () =>
                {
                    var s2 = (entry.Spread ?? StatSpread.Zero()).Clone();
                    SetBt(s2, stat, ClampedBt(s2, stat, delta));
                    ApplySpread(s2);
                };
                btns.AddChild(b);
            }
            row.AddChild(btns);
            row.AddChild(Fixed(sp == null ? "—" : RealValue(sp, spread, i).ToString(), 60, tint));
            col.AddChild(row);
        }

        // 気質
        var nat = Row(8);
        nat.AddChild(Text("気質", BattleTheme.Muted, BattleTheme.FontLabel));
        nat.AddChild(Text("上昇", BattleTheme.Ok, BattleTheme.FontLabel));
        nat.AddChild(NatureOption(entry, spread, true));
        nat.AddChild(Text("下降", BattleTheme.Crit, BattleTheme.FontLabel));
        nat.AddChild(NatureOption(entry, spread, false));
        nat.AddChild(Text(spread.NatureLabel, BattleTheme.Ink2, BattleTheme.FontLabel));
        nat.AddChild(Spacer());
        var auto = new Button { Text = "自動配分" };
        auto.Pressed += () => ApplySpread(StatSpread.Auto(entry.Species));
        nat.AddChild(auto);
        var zero = new Button { Text = "すべて0" };
        zero.Pressed += () => ApplySpread(StatSpread.Zero());
        nat.AddChild(zero);
        col.AddChild(nat);

        // この個体の突破値・気質の違反は、ここにも赤字で出す。
        foreach (var msg in spread.Validate())
            col.AddChild(Text(msg, BattleTheme.Crit, BattleTheme.FontLabel));
        return card;
    }

    // 気質の選択。0=なし 1=攻撃 2=防御 3=魔力 4=精神 5=俊敏（NatureStat の値と同じ並び）。
    // もう片方が選んでいる能力は選べない。両方なし＝無補正。
    private OptionButton NatureOption(BattleEntry entry, StatSpread spread, bool up)
    {
        var ob = new OptionButton();
        var other = up ? spread.NatureDown : spread.NatureUp;
        var mine = up ? spread.NatureUp : spread.NatureDown;
        foreach (NatureStat n in System.Enum.GetValues<NatureStat>())
        {
            ob.AddItem(StatSpread.StatName(n), (int)n);
            if (n != NatureStat.None && n == other) ob.SetItemDisabled((int)n, true);
        }
        ob.Selected = (int)mine;
        ob.ItemSelected += idx =>
        {
            var s2 = (entry.Spread ?? StatSpread.Zero()).Clone();
            if (up) s2.NatureUp = (NatureStat)(int)idx; else s2.NatureDown = (NatureStat)(int)idx;
            ApplySpread(s2);
        };
        return ob;
    }

    private static Control StatChip(string label, int value)
    {
        var r = Row(4);
        r.AddChild(Text(label, BattleTheme.Muted, 10));
        r.AddChild(Text(value.ToString(), BattleTheme.Ink, BattleTheme.FontSmall));
        return r;
    }

    // 技。learnset を全部並べ、そのうち4つを選ぶ。
    private Control MovePanel(BattleEntry entry)
    {
        var card = Card(BattleTheme.Surface, BattleTheme.Line);
        var col = Col(4);
        card.AddChild(col);

        var head = Row(8);
        head.AddChild(Text("技", BattleTheme.Ink, BattleTheme.FontBody));
        head.AddChild(Pill($"{entry.MoveIds.Count} / {MoveManager.MaxMoves}",
                           entry.MoveIds.Count == MoveManager.MaxMoves
                               ? BattleTheme.Brass : BattleTheme.Muted,
                           BattleTheme.Sunk));
        head.AddChild(Spacer());
        // レベルキャップを無視するのは対人戦だけの規則なので、画面に書く。
        head.AddChild(Text("対人戦ではレベルキャップを無視し、覚えられる技すべてから選べます",
                           BattleTheme.Muted, BattleTheme.FontLabel));
        col.AddChild(head);

        // 見出し行。数字が縦に揃うと威力帯の穴が見える。
        var legend = Row(6);
        legend.AddChild(Fixed("Lv", 26, BattleTheme.Muted));
        legend.AddChild(Fixed("", 46, BattleTheme.Muted));
        legend.AddChild(Fixed("技名", 150, BattleTheme.Muted));
        legend.AddChild(Fixed("射程", 44, BattleTheme.Muted));
        legend.AddChild(Fixed("威力", 34, BattleTheme.Muted));
        legend.AddChild(Fixed("命中", 34, BattleTheme.Muted));
        legend.AddChild(Fixed("PP", 26, BattleTheme.Muted));
        col.AddChild(legend);

        // 選んだ技を上に、残りを威力の高い順に。選択中が散らばると
        // 「いま何を持っているか」が一目で分からない。
        var learnable = entry.Learnable();
        var chosen = entry.MoveIds.Select(id => learnable.FirstOrDefault(m => m.Id == id))
                          .Where(m => m != null).ToList();
        var rest = learnable.Where(m => !entry.MoveIds.Contains(m.Id))
                            .OrderByDescending(m => m.Power).ThenBy(m => m.Name).ToList();

        foreach (var m in chosen) col.AddChild(MoveRow(entry, m, true));

        // 覚えられる技は30件近くになる種もある。ここを伸びるに任せると
        // 持ち物の欄が画面のはるか下へ行ってしまうので、この一覧だけ
        // 独立してスクロールさせる。選んだ4つは上に固定で残る。
        if (rest.Count > 0)
        {
            col.AddChild(Text($"覚えられる技 {learnable.Count}件（押すと入れ替え）",
                              BattleTheme.Muted, 10));
            var pool = new ScrollContainer { CustomMinimumSize = new Vector2(0, 264) };
            var inner = Col(2);
            inner.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            foreach (var m in rest) inner.AddChild(MoveRow(entry, m, false));
            pool.AddChild(inner);
            col.AddChild(pool);
        }

        return card;
    }

    private Control MoveRow(BattleEntry entry, MoveData m, bool on)
    {
        bool full = entry.MoveIds.Count >= MoveManager.MaxMoves;
        var row = ClickableCard(on ? BattleTheme.BrassBg : BattleTheme.Sunk,
                                on ? BattleTheme.Brass : BattleTheme.Line, 3);
        row.CustomMinimumSize = new Vector2(0, 26);

        // 4つ埋まっていて、かつ選ばれていない技は押せない。押せない理由が
        // 見た目で分かるよう、無効時は色を落とす。
        row.Disabled = full && !on;
        row.Pressed += () => { if (entry.ToggleMove(m.Id)) Refresh(); };

        var line = Row(6);
        BattleUiKit.AddFilled(row, line, margin: 3);

        int lv = entry.LearnLevel(m.Id);
        line.AddChild(Fixed(lv > 0 ? lv.ToString() : "—", 26,
                            row.Disabled ? BattleTheme.Line2 : BattleTheme.Muted));

        var chips = Row(3);
        chips.CustomMinimumSize = new Vector2(46, 0);
        chips.AddChild(ElementChip(m.Type));
        chips.AddChild(CategoryChip(m.Category));
        line.AddChild(chips);

        line.AddChild(Fixed(m.Name, 150, row.Disabled ? BattleTheme.Line2 : BattleTheme.Ink));
        line.AddChild(Fixed(RangeLabel(m.Range), 44,
                            row.Disabled ? BattleTheme.Line2 : BattleTheme.Ink2));
        line.AddChild(Fixed(m.Power > 0 ? m.Power.ToString() : "—", 34,
                            row.Disabled ? BattleTheme.Line2 : BattleTheme.Ink2));
        line.AddChild(Fixed(m.Accuracy > 0 ? m.Accuracy.ToString() : "—", 34,
                            row.Disabled ? BattleTheme.Line2 : BattleTheme.Ink2));
        line.AddChild(Fixed(m.MaxPp.ToString(), 26,
                            row.Disabled ? BattleTheme.Line2 : BattleTheme.Muted));
        if (m.Priority != 0)
            line.AddChild(Pill($"優先{m.Priority:+#;-#}", BattleTheme.Brass, BattleTheme.BrassBg));
        if (m.WeaponTag != WeaponTag.None)
            line.AddChild(Text(m.WeaponTag.ToString(), BattleTheme.Muted, 10));
        line.AddChild(Spacer());
        return row;
    }

    // 持ち物。16種から1つ。他の枠が持っていれば、そこから移す。
    private Control ItemPanel(BattleEntry entry)
    {
        var card = Card(BattleTheme.Surface, BattleTheme.Line);
        var col = Col(4);
        card.AddChild(col);

        var head = Row(8);
        head.AddChild(Text("持ち物", BattleTheme.Ink, BattleTheme.FontBody));
        head.AddChild(Spacer());
        head.AddChild(Text("チーム内で重複できません（選ぶと持ち替えになります）",
                           BattleTheme.Muted, BattleTheme.FontLabel));
        col.AddChild(head);

        var none = ClickableCard(string.IsNullOrEmpty(entry.ItemId)
                                     ? BattleTheme.BrassBg : BattleTheme.Sunk,
                                 string.IsNullOrEmpty(entry.ItemId)
                                     ? BattleTheme.Brass : BattleTheme.Line, 3);
        none.CustomMinimumSize = new Vector2(0, 24);
        none.Pressed += () => { if (_team.SetItem(_slot, null)) Refresh(); };
        var nrow = Row(6);
        BattleUiKit.AddFilled(none, nrow, margin: 3);
        nrow.AddChild(Text("持たせない", BattleTheme.Ink2, BattleTheme.FontLabel));
        nrow.AddChild(Spacer());
        col.AddChild(none);

        foreach (var id in ItemDatabase.AllIds())
        {
            var item = ItemDatabase.Get(id);
            if (item == null || item.Type != ItemType.BattleHeld) continue;

            bool on = entry.ItemId == id;
            var holder = _team.HolderOf(id);
            bool taken = holder != null && holder != entry;

            var b = ClickableCard(on ? BattleTheme.BrassBg : BattleTheme.Sunk,
                                  on ? BattleTheme.Brass : BattleTheme.Line, 3);
            b.CustomMinimumSize = new Vector2(0, 30);
            string pick = id;
            b.Pressed += () => { if (_team.SetItem(_slot, pick)) Refresh(); };

            var line = Row(6);
            BattleUiKit.AddFilled(b, line, margin: 3);
            line.AddChild(Fixed(item.Name, 130, on ? BattleTheme.Brass : BattleTheme.Ink));
            var desc = Text(item.Description ?? "", BattleTheme.Muted, 10);
            desc.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            desc.ClipText = true;
            desc.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            line.AddChild(desc);
            // 他の枠が持っているものは「誰が持っているか」を出す。押せば移る。
            if (taken)
                line.AddChild(Pill($"{holder.Species?.DisplayName ?? holder.SpeciesId} が所持",
                                   BattleTheme.Warn, BattleTheme.Sunk));
            col.AddChild(b);
        }
        return card;
    }

    private static Label Fixed(string s, int width, Color c)
    {
        var l = Text(s, c, BattleTheme.FontLabel);
        l.CustomMinimumSize = new Vector2(width, 0);
        l.ClipText = true;
        l.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        return l;
    }

    private static string RangeLabel(MoveRange range) => range switch
    {
        MoveRange.Adjacent => "隣接",
        MoveRange.Line => "直線",
        MoveRange.TwoTile => "2マス",
        MoveRange.Area => "範囲",
        MoveRange.Room => "部屋",
        MoveRange.FullFloor => "全体",
        MoveRange.Surrounding => "周囲",
        MoveRange.FrontArc => "正面3",
        _ => "—",
    };
}
