package jp.mcserver.core;

import java.util.HashSet;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.Set;

/**
 * コア層の検証。外部依存を持たないため javac だけで実行できる。
 *
 * <p>実行: java -cp out jp.mcserver.core.CoreTests
 */
public final class CoreTests {

    private static int passed = 0;
    private static int failed = 0;

    public static void main(String[] args) {
        formulas();
        activityWindow();
        contribution();
        newPlayerCoefficient();
        coordinateAnnouncement();
        territory();
        chunkRelease();
        claimTool();
        boundary();
        accounts();
        ranking();
        alliance();
        vassalage();
        sanction();
        war();
        unification();

        System.out.println();
        System.out.println("合計 " + (passed + failed) + " 件: 成功 " + passed + " / 失敗 " + failed);
        if (failed > 0) {
            System.exit(1);
        }
    }

    // ---------------------------------------------------------------- §4.1

    private static void formulas() {
        section("§4.1 数式");

        check("B(0)=10", Formulas.promotionCost(0) == 10);
        check("B(1)=18", Formulas.promotionCost(1) == 18);
        check("B(5)=70", Formulas.promotionCost(5) == 70);
        check("B(10)=180", Formulas.promotionCost(10) == 180);
        check("B(15)=340", Formulas.promotionCost(15) == 340);
        check("B(20)=550", Formulas.promotionCost(20) == 550);

        check("C(1)=10", Formulas.cumulativeCost(1) == 10);
        check("C(5)=150", Formulas.cumulativeCost(5) == 150);
        check("C(10)=700", Formulas.cumulativeCost(10) == 700);
        check("C(15)=1900", Formulas.cumulativeCost(15) == 1900);
        check("C(20)=4000", Formulas.cumulativeCost(20) == 4000);
        check("C(25)=7250", Formulas.cumulativeCost(25) == 7250);

        boolean sumMatches = true;
        for (int n = 1; n <= 25; n++) {
            long sum = 0;
            for (int a = 0; a < n; a++) {
                sum += Formulas.promotionCost(a);
            }
            if (sum != Formulas.cumulativeCost(n)) {
                sumMatches = false;
            }
        }
        check("C(n) = ΣB(a) が n=1..25 で成立", sumMatches);

        check("定員 rank1=3", Formulas.capacity(1) == 3);
        check("定員 rank25=27", Formulas.capacity(25) == 27);
        check("M(1)=3", Formulas.maintenanceCapacity(1) == 3);
        check("M(10)=9", Formulas.maintenanceCapacity(10) == 9);
        check("M(15)=13", Formulas.maintenanceCapacity(15) == 13);
        check("M(20)=17", Formulas.maintenanceCapacity(20) == 17);
        check("M(25)=21", Formulas.maintenanceCapacity(25) == 21);

        check("許容欠員 rank10=3", Formulas.capacity(10) - Formulas.maintenanceCapacity(10) == 3);
        check("許容欠員 rank25=6", Formulas.capacity(25) - Formulas.maintenanceCapacity(25) == 6);

        check("チャンク rank0=3", Formulas.chunks(0) == 3);
        check("チャンク rank1=16", Formulas.chunks(1) == 16);
        check("チャンク rank25=400", Formulas.chunks(25) == 400);

        boolean inverse = true;
        for (int n = 3; n <= 27; n++) {
            int expected = -1;
            for (int a = 0; a <= 25; a++) {
                if (Formulas.maintenanceCapacity(a) <= n) {
                    expected = a;
                }
            }
            if (Formulas.rankSupportedBy(n) != expected) {
                inverse = false;
            }
        }
        check("a' = min(25, ⌊4N/3⌋−2) が M(a) の逆関数として一致（N=3..27）", inverse);

        boolean sustained = true;
        for (int a = 0; a <= 25; a++) {
            double threshold = Formulas.maintenanceActivityHours(a);
            if (Formulas.rankSustainedBy(threshold) != a) {
                sustained = false;
            }
            if (a > 0 && Formulas.rankSustainedBy(threshold - 0.01) >= a) {
                sustained = false;
            }
        }
        check("rankSustainedBy が B(a)×0.1 の境界で正しく切り替わる", sustained);

        check("シュルカー累計 6個=210,000", Formulas.shulkerCumulativeCost(6) == 210_000);
        check("シュルカー累計 24個=1,272,000", Formulas.shulkerCumulativeCost(24) == 1_272_000);
        check("シュルカー累計 54個=4,482,000", Formulas.shulkerCumulativeCost(54) == 4_482_000);
        check("シュルカー上限 rank0=6", Formulas.shulkerLimit(0) == 6);
        check("シュルカー上限 rank25=54", Formulas.shulkerLimit(25) == 54);
        check("exp換算 3,750/h", Formulas.EXP_PER_HOUR == 3750);
    }

    // ---------------------------------------------------------------- §2.1

    private static void activityWindow() {
        section("§2.1 有効活動時間");

        ActivityWindow w = new ActivityWindow();
        w.onLogin();
        int credited = w.advance(12 * 60, false);
        check("12分間 pitch を動かさなければ計上されない", credited == 0 && w.countedTodayMinutes() == 0);

        w = new ActivityWindow();
        w.onLogin();
        credited = w.advance(12 * 60, true);
        check("12分間に pitch が動けば12分計上される", credited == 12 && w.countedTodayMinutes() == 12);

        w = new ActivityWindow();
        w.onLogin();
        w.advance(7 * 60, true);
        w.onLogout();
        w.onLogin();
        credited = w.advance(5 * 60, true);
        check("端数の経過時間はセッションを跨いで繰り越される", credited == 12);

        w = new ActivityWindow();
        w.onLogin();
        w.advance(11 * 60, true);   // ログイン直後に視線を動かす
        w.onLogout();
        w.onLogin();
        credited = w.advance(60, false); // 残り1分を放置
        check("pitch のフラグはセッションごとにリセットされる（抜け道の封鎖）", credited == 0);

        w = new ActivityWindow();
        w.onLogin();
        int total = 0;
        for (int i = 0; i < 40; i++) {
            total += w.advance(12 * 60, true);
        }
        check("40窓で日次上限480分に到達する", total == 480 && w.dailyCapReached());
        check("上限到達後は計上されない", w.advance(12 * 60, true) == 0);

        w.onDayRollover();
        check("日付が変われば計上枠が回復する", w.countedTodayMinutes() == 0 && !w.dailyCapReached());

        w = new ActivityWindow();
        w.onLogin();
        credited = w.advance(36 * 60, true);
        check("窓を跨ぐ一括加算では、変化の証拠がある最初の窓しか計上されない", credited == 12);

        w = new ActivityWindow();
        w.onLogin();
        total = 0;
        for (int i = 0; i < 36; i++) {
            total += w.advance(60, true); // 1分ごとに操作あり
        }
        check("1分刻みで進めれば3窓ぶん36分が計上される", total == 36);
    }

    // ---------------------------------------------------------------- §6.3

    private static void contribution() {
        section("§6.3 貢献度");

        check("納入ゼロなら活動時間そのもの", Contribution.score(10, 0) == 10.0);
        check("活動時間と同額の納入で2倍になる", Contribution.score(10, 37_500) == 20.0);
        check("納入が過大でも活動時間分で頭打ちになる", Contribution.score(10, 3_000_000) == 20.0);
        check("上限に達したことを検出できる", Contribution.donationCapped(10, 3_000_000));
        check("上限未満では検出しない", !Contribution.donationCapped(10, 1_000));
        check("活動時間ゼロなら納入は貢献度にならない", Contribution.score(0, 1_000_000) == 0.0);
    }

    // ---------------------------------------------------------------- §5

    private static void newPlayerCoefficient() {
        section("§5 新規プレイヤー係数");

        check("開始30日間は停止する", !NewPlayerCoefficient.applies(30, 1));
        check("1日目参加は31日目のみ適用", NewPlayerCoefficient.applies(31, 1) && !NewPlayerCoefficient.applies(32, 1));
        check("15日目参加は31〜45日", NewPlayerCoefficient.applies(31, 15)
                && NewPlayerCoefficient.applies(45, 15) && !NewPlayerCoefficient.applies(46, 15));
        check("25日目参加は31〜55日", NewPlayerCoefficient.applies(55, 25) && !NewPlayerCoefficient.applies(56, 25));
        check("31日目参加は31〜61日", NewPlayerCoefficient.applies(61, 31) && !NewPlayerCoefficient.applies(62, 31));
        check("適用時は1.5倍", NewPlayerCoefficient.toCumulative(10, 31, 15) == 15.0);
        check("非適用時は等倍", NewPlayerCoefficient.toCumulative(10, 20, 15) == 10.0);
    }

    // ---------------------------------------------------------------- §4.8

    private static void coordinateAnnouncement() {
        section("§4.8 座標告知");

        boolean inRange = true;
        boolean deterministic = true;
        Set<Integer> distinct = new HashSet<>();
        for (int x = -50; x <= 50; x++) {
            for (int z = -50; z <= 50; z++) {
                int ox = CoordinateAnnouncement.offsetX(x, z);
                int oz = CoordinateAnnouncement.offsetZ(x, z);
                if (Math.abs(ox) > 64 || Math.abs(oz) > 64) {
                    inRange = false;
                }
                if (ox != CoordinateAnnouncement.offsetX(x, z)) {
                    deterministic = false;
                }
                distinct.add(ox);
            }
        }
        check("オフセットが ±64 の範囲に収まる", inRange);
        check("同じチャンクは常に同じオフセットを返す", deterministic);
        check("オフセットが偏らず分散している", distinct.size() > 100);

        check("100単位に丸められる", CoordinateAnnouncement.round(1234) == 1200
                && CoordinateAnnouncement.round(1250) == 1300
                && CoordinateAnnouncement.round(-1234) == -1200);

        int a = CoordinateAnnouncement.announcedX(1000, 62, 62);
        long sum = 0;
        for (int i = 0; i < 100; i++) {
            sum += CoordinateAnnouncement.announcedX(1000, 62, 62);
        }
        check("複数回の告知を平均しても真の座標に近づかない", sum / 100 == a);
    }

    // ---------------------------------------------------------------- §4.4 / §4.6

    private static void territory() {
        section("§4.4 昇格 / §4.6 降格");

        var s = new Territory.NationState(0, 10, 5, 3);
        check("rank0→1 の条件を満たせば昇格できる", Territory.canPromote(s, false).allowed());
        check("同日に2回目の昇格はできない", !Territory.canPromote(s, true).allowed());

        check("累計不足では昇格できない",
                !Territory.canPromote(new Territory.NationState(0, 9, 5, 3), false).allowed());
        check("実効国民不足では昇格できない",
                !Territory.canPromote(new Territory.NationState(1, 100, 20, 3), false).allowed());
        check("維持要件を満たさなければ昇格できない",
                !Territory.canPromote(new Territory.NationState(10, 5000, 1, 12), false).allowed());

        // rank10: 維持閾値 B(10)×0.1 = 18h、M(10)=9
        var violating = new Territory.NationState(10, 700, 10, 12);
        var first = Territory.evaluate(violating, 1);
        check("活動要件違反の1回目は −1", first.newRank() == 9 && first.cause() == Territory.DemotionCause.ACTIVITY);

        var second = Territory.evaluate(violating, 2);
        int floor = Formulas.rankSustainedBy(10);
        check("2回連続以降は −8、ただし維持可能ランクを下回らない",
                second.newRank() == Math.max(floor, 2) && second.newRank() >= floor);

        var shortStaffed = new Territory.NationState(10, 700, 100, 6);
        var byCap = Territory.evaluate(shortStaffed, 0);
        check("定員起因は N が支える最大ランクへ即時調整",
                byCap.newRank() == Formulas.rankSupportedBy(6) && byCap.cause() == Territory.DemotionCause.CAPACITY);

        var both = Territory.evaluate(new Territory.NationState(10, 700, 10, 6), 1);
        check("両要件に抵触したらより低い方を採る",
                both.newRank() == Math.min(9, Formulas.rankSupportedBy(6))
                        && both.cause() == Territory.DemotionCause.BOTH);

        check("要件を満たしていれば降格しない",
                !Territory.evaluate(new Territory.NationState(10, 700, 100, 12), 0).demoted());

        check("閾値の70%割れで警告する",
                Territory.shouldWarn(new Territory.NationState(10, 700, 12, 12)));
        check("余裕があれば警告しない",
                !Territory.shouldWarn(new Territory.NationState(10, 700, 100, 12)));
    }

    // ---------------------------------------------------------------- §4.7

    private static void chunkRelease() {
        section("§4.7 チャンク解放");

        ChunkPos capital = new ChunkPos(0, 0);

        Set<ChunkPos> line = new LinkedHashSet<>(List.of(
                new ChunkPos(0, 0), new ChunkPos(1, 0), new ChunkPos(2, 0), new ChunkPos(3, 0)));
        List<ChunkPos> released = ChunkRelease.select(line, capital, 3);
        check("一直線の領土は先端から解放される",
                released.equals(List.of(new ChunkPos(3, 0), new ChunkPos(2, 0), new ChunkPos(1, 0))));

        Set<ChunkPos> branch = new LinkedHashSet<>(List.of(
                new ChunkPos(0, 0), new ChunkPos(1, 0), new ChunkPos(2, 0), new ChunkPos(2, 1)));
        List<ChunkPos> first = ChunkRelease.select(branch, capital, 1);
        check("距離が同じなら境界接触面が多い方から解放される",
                first.equals(List.of(new ChunkPos(2, 1))));

        // 連結性: どの段階でも残存領土は首都と連結していなければならない
        Set<ChunkPos> shape = new LinkedHashSet<>();
        for (int i = 0; i <= 3; i++) {
            shape.add(new ChunkPos(i, 0));
        }
        shape.add(new ChunkPos(3, 1));
        shape.add(new ChunkPos(3, 2));
        shape.add(new ChunkPos(1, 1));
        shape.add(new ChunkPos(1, 2));

        boolean alwaysConnected = true;
        for (int count = 1; count < shape.size(); count++) {
            List<ChunkPos> out = ChunkRelease.select(shape, capital, count);
            Set<ChunkPos> remain = new HashSet<>(shape);
            out.forEach(remain::remove);
            if (!ChunkRelease.isConnected(remain, capital)) {
                alwaysConnected = false;
            }
            if (out.contains(capital)) {
                alwaysConnected = false;
            }
        }
        check("どの段階でも残存領土は首都と連結し、首都は解放されない", alwaysConnected);

        check("連結判定が分断を検出する",
                !ChunkRelease.isConnected(new HashSet<>(List.of(
                        new ChunkPos(0, 0), new ChunkPos(2, 0))), capital));

        Set<ChunkPos> rank1 = new LinkedHashSet<>(List.of(
                new ChunkPos(0, 0), new ChunkPos(1, 0), new ChunkPos(0, 1), new ChunkPos(0, 2),
                new ChunkPos(2, 0), new ChunkPos(3, 0)));
        List<ChunkPos> toCity = ChunkRelease.selectDownToCityState(rank1, capital);
        Set<ChunkPos> keptCity = new HashSet<>(rank1);
        toCity.forEach(keptCity::remove);
        check("rank1→都市国家では首都＋隣接2チャンクが残る",
                keptCity.size() == 3 && keptCity.contains(capital)
                        && keptCity.contains(new ChunkPos(0, 1)) && keptCity.contains(new ChunkPos(1, 0)));
        check("都市国家に残る3チャンクは連結している", ChunkRelease.isConnected(keptCity, capital));

        List<ChunkPos> toCamp = ChunkRelease.selectDownToCamp(keptCity, capital);
        check("都市国家→野営地では首都のみが残る",
                toCamp.size() == 2 && !toCamp.contains(capital));

        Set<ChunkPos> wide = new LinkedHashSet<>();
        for (int x = 0; x < 8; x++) {
            for (int z = 0; z < 4; z++) {
                wide.add(new ChunkPos(x, z));
            }
        }
        List<ChunkPos> demoted = ChunkRelease.selectForDemotion(wide, capital, 2, 1);
        check("rank2→rank1 は 32→16 チャンクへ縮小する", demoted.size() == 16);

    }

    // ---------------------------------------------------------------- §4.11 棒

    private static void claimTool() {
        section("§4.11 棒による拡張・削除");

        ChunkPos capital = new ChunkPos(0, 0);
        Set<ChunkPos> owned = new LinkedHashSet<>(List.of(capital, new ChunkPos(1, 0)));
        var state = new ClaimService.TerritoryState("テスト国", 1, capital, owned, 0);

        check("上限は 16a ＋ ボーナス", state.limit() == 16 && state.remaining() == 14);

        var ok = ClaimService.expand(state, new ChunkPos(2, 0), ClaimService.ChunkCondition.free(), true);
        check("隣接し余りがあれば拡張できる", ok.ok() && ok.message().contains("残り 13"));

        check("首長でなければ拡張できない",
                ClaimService.expand(state, new ChunkPos(2, 0), ClaimService.ChunkCondition.free(), false)
                        .denial() == ClaimService.ExpandDenial.NOT_LEADER);
        check("既に自国領土なら拡張できない",
                ClaimService.expand(state, capital, ClaimService.ChunkCondition.free(), true)
                        .denial() == ClaimService.ExpandDenial.ALREADY_OWNED);
        check("隣接していなければ拡張できない",
                ClaimService.expand(state, new ChunkPos(5, 5), ClaimService.ChunkCondition.free(), true)
                        .denial() == ClaimService.ExpandDenial.NOT_ADJACENT);
        check("保護区は拡張できない",
                ClaimService.expand(state, new ChunkPos(2, 0),
                        new ClaimService.ChunkCondition(true, false, false), true)
                        .denial() == ClaimService.ExpandDenial.PROTECTED_ZONE);
        check("他国領土は拡張できない",
                ClaimService.expand(state, new ChunkPos(2, 0),
                        new ClaimService.ChunkCondition(false, true, false), true)
                        .denial() == ClaimService.ExpandDenial.OWNED_BY_OTHER);
        check("再取得制限圏内は拡張できない",
                ClaimService.expand(state, new ChunkPos(2, 0),
                        new ClaimService.ChunkCondition(false, false, true), true)
                        .denial() == ClaimService.ExpandDenial.REACQUISITION_RESTRICTED);

        Set<ChunkPos> full = new LinkedHashSet<>();
        for (int x = 0; x < 16; x++) {
            full.add(new ChunkPos(x, 0));
        }
        var atLimit = new ClaimService.TerritoryState("満杯国", 1, capital, full, 0);
        var denied = ClaimService.expand(atLimit, new ChunkPos(16, 0), ClaimService.ChunkCondition.free(), true);
        check("上限に達していれば拡張できない旨が示される",
                denied.denial() == ClaimService.ExpandDenial.AT_LIMIT
                        && denied.message().contains("上限 16"));

        var withBonus = new ClaimService.TerritoryState("戦勝国", 1, capital, full, 8);
        check("ボーナスチャンクは上限に加算される",
                withBonus.limit() == 24
                        && ClaimService.expand(withBonus, new ChunkPos(16, 0),
                        ClaimService.ChunkCondition.free(), true).ok());

        var empty = new ClaimService.TerritoryState("新興国", 0, capital, Set.of(), 0);
        check("領土が空なら隣接を要求しない（最初の野営地）",
                ClaimService.expand(empty, new ChunkPos(9, 9), ClaimService.ChunkCondition.free(), true).ok());

        // 削除
        Set<ChunkPos> line = new LinkedHashSet<>(List.of(
                capital, new ChunkPos(1, 0), new ChunkPos(2, 0)));
        var lineState = new ClaimService.TerritoryState("直線国", 1, capital, line, 0);

        var removal = ClaimService.remove(lineState, new ChunkPos(2, 0), true);
        check("削除は確認を要求する",
                removal.ok() && removal.requiresConfirmation()
                        && removal.message().contains("/territory confirm"));
        check("首都は削除できない",
                ClaimService.remove(lineState, capital, true).denial() == ClaimService.RemoveDenial.CAPITAL);
        check("自国領土でなければ削除できない",
                ClaimService.remove(lineState, new ChunkPos(9, 9), true).denial()
                        == ClaimService.RemoveDenial.NOT_OWNED);
        check("首長でなければ削除できない",
                ClaimService.remove(lineState, new ChunkPos(2, 0), false).denial()
                        == ClaimService.RemoveDenial.NOT_LEADER);
        check("分断を生む削除は拒否される",
                ClaimService.remove(lineState, new ChunkPos(1, 0), true).denial()
                        == ClaimService.RemoveDenial.WOULD_DISCONNECT);

        // 囲い込みの防止: 四辺を自国領土に囲まれたチャンクは削除できない
        Set<ChunkPos> plus = new LinkedHashSet<>(List.of(
                capital,
                new ChunkPos(1, 0), new ChunkPos(2, 0), new ChunkPos(3, 0),
                new ChunkPos(2, 1), new ChunkPos(2, -1)));
        var plusState = new ClaimService.TerritoryState("十字国", 1, capital, plus, 0);
        var enclosedResult = ClaimService.remove(plusState, new ChunkPos(2, 0), true);
        check("四辺を囲まれたチャンクは削除できない",
                enclosedResult.denial() == ClaimService.RemoveDenial.ENCLOSED
                        && enclosedResult.message().contains("囲い込む"));
        check("三辺までなら削除できる",
                ClaimService.remove(plusState, new ChunkPos(2, 1), true).ok());
        check("囲まれていても首都の判定が優先される",
                ClaimService.remove(plusState, capital, true).denial()
                        == ClaimService.RemoveDenial.CAPITAL);

        Set<ChunkPos> square = new LinkedHashSet<>();
        for (int x = 0; x <= 2; x++) {
            for (int z = 0; z <= 2; z++) {
                square.add(new ChunkPos(x, z));
            }
        }
        var squareState = new ClaimService.TerritoryState("方形国", 1, capital, square, 0);
        check("3×3の中心は削除できない（穴を開けられない）",
                ClaimService.remove(squareState, new ChunkPos(1, 1), true).denial()
                        == ClaimService.RemoveDenial.ENCLOSED);
        check("3×3の角は削除できる",
                ClaimService.remove(squareState, new ChunkPos(2, 2), true).ok());

        // 囲い込みの自動編入（拡張側）
        // 環の南側 (1,0) が空いており、(1,1) は外へ抜けられる
        Set<ChunkPos> openRing = new LinkedHashSet<>(List.of(
                capital, new ChunkPos(0, 1), new ChunkPos(0, 2),
                new ChunkPos(1, 2), new ChunkPos(2, 1), new ChunkPos(2, 0)));
        check("環が閉じていなければ囲い込みは検出されない",
                Enclosure.findEnclosed(openRing).isEmpty());

        var ringState = new ClaimService.TerritoryState("環状国", 1, capital, openRing, 0);
        var closing = ClaimService.expand(ringState, new ChunkPos(1, 0),
                ClaimService.ChunkCondition.free(), true);
        check("環を閉じる拡張は囲い込んだチャンクを自動編入する",
                closing.ok() && closing.absorbed().equals(List.of(new ChunkPos(1, 1)))
                        && closing.message().contains("編入"));
        check("編入分も上限から差し引かれる", closing.message().contains("残り 8"));

        var noEnclosure = ClaimService.expand(ringState, new ChunkPos(3, 0),
                ClaimService.ChunkCondition.free(), true);
        check("囲い込みを生まない拡張では編入が起きない",
                noEnclosure.ok() && noEnclosure.absorbed().isEmpty());

        check("編入できないチャンク（保護区など）は編入対象から外れる",
                ClaimService.expand(ringState, new ChunkPos(1, 0),
                        ClaimService.ChunkCondition.free(), true,
                        c -> new ClaimService.ChunkCondition(true, false, false))
                        .absorbed().isEmpty());

        // 残り1チャンクでは、編入を伴う拡張（2チャンク分）ができない
        Set<ChunkPos> tight = new LinkedHashSet<>(openRing);
        for (int x = 3; x <= 11; x++) {
            tight.add(new ChunkPos(x, 0));
        }
        var tightState = new ClaimService.TerritoryState("余裕なし国", 1, capital, tight, 0);
        check("この時点で残りは1チャンクである", tightState.remaining() == 1);
        var refused = ClaimService.expand(tightState, new ChunkPos(1, 0),
                ClaimService.ChunkCondition.free(), true);
        check("編入分の余りが足りなければ拡張自体を拒否する",
                !refused.ok() && refused.denial() == ClaimService.ExpandDenial.AT_LIMIT
                        && refused.message().contains("編入を伴う"));

        // 複数チャンクの空洞も検出する
        Set<ChunkPos> bigRing = new LinkedHashSet<>();
        for (int x = 0; x <= 3; x++) {
            for (int z = 0; z <= 3; z++) {
                boolean hole = (x == 1 || x == 2) && (z == 1 || z == 2);
                if (!hole) {
                    bigRing.add(new ChunkPos(x, z));
                }
            }
        }
        check("2×2の空洞も囲い込みとして検出される",
                Enclosure.findEnclosed(bigRing).size() == 4);
        check("空洞が外へ繋がっていれば検出されない",
                Enclosure.findEnclosed(minus(bigRing, new ChunkPos(1, 0))).isEmpty());

        var pending = new ClaimService.PendingRemoval(new ChunkPos(2, 0), 1_000);
        check("期限内の確認は成立する",
                ClaimService.confirmRemoval(pending, new ChunkPos(2, 0), 1_020));
        check("30秒を超えた確認は無効",
                !ClaimService.confirmRemoval(pending, new ChunkPos(2, 0), 1_031));
        check("別チャンクでの確認は無効",
                !ClaimService.confirmRemoval(pending, new ChunkPos(1, 0), 1_010));
        check("確認待ちが無ければ削除は成立しない",
                !ClaimService.confirmRemoval(null, new ChunkPos(2, 0), 1_010));
    }

    // ---------------------------------------------------------------- §4.11 表示

    private static void boundary() {
        section("§4.11 領域の出入り表示");

        ChunkOwner own = ChunkOwner.of(TerritoryRelation.OWN, "自国");
        ChunkOwner ally = ChunkOwner.of(TerritoryRelation.ALLY, "北方連合");
        ChunkOwner vassal = ChunkOwner.of(TerritoryRelation.VASSAL, "南方公国");
        ChunkOwner foreign = ChunkOwner.of(TerritoryRelation.FOREIGN, "東方帝国");
        ChunkOwner wild = ChunkOwner.wilderness();
        ChunkOwner zone = ChunkOwner.protectedZone();

        check("領土外から自国へ入ると進入のみ表示",
                BoundaryMessages.onCross(wild, own).equals(List.of("自国の領土に入りました")));
        check("自国から領土外へ出ると退出のみ表示",
                BoundaryMessages.onCross(own, wild).equals(List.of("自国の領土から出ました")));
        check("領土から領土へ直接移ると退出と進入の両方を表示",
                BoundaryMessages.onCross(own, ally)
                        .equals(List.of("自国の領土から出ました", "同盟国「北方連合」の領土に入りました")));
        check("同一領土内の移動では何も表示しない",
                BoundaryMessages.onCross(own, own).isEmpty());
        check("同じ関係でも国家が異なれば表示する",
                BoundaryMessages.onCross(foreign, ChunkOwner.of(TerritoryRelation.FOREIGN, "西方王国")).size() == 2);
        check("属国の領土を表示できる",
                BoundaryMessages.onCross(wild, vassal).equals(List.of("属国「南方公国」の領土に入りました")));
        check("他国は国家名のみで表示する",
                BoundaryMessages.onCross(wild, foreign).equals(List.of("東方帝国の領土に入りました")));
        check("保護区の出入りも表示する",
                BoundaryMessages.onCross(wild, zone).equals(List.of("保護区に入りました"))
                        && BoundaryMessages.onCross(zone, wild).equals(List.of("保護区から出ました")));
        check("初回ログイン時（直前が不明）は進入のみ表示",
                BoundaryMessages.onCross(null, own).equals(List.of("自国の領土に入りました")));
        check("領土外どうしの移動では何も表示しない",
                BoundaryMessages.onCross(wild, wild).isEmpty());

        check("棒の所持時は保有数と残りを併記する",
                BoundaryMessages.inspect(own, 12, 16).contains("残り 4"));
        check("領土外でも状態を表示する",
                BoundaryMessages.inspect(wild, 12, 16).contains("領土外"));
    }

    // ---------------------------------------------------------------- helpers

    // ---------------------------------------------------------------- §7

    private static void accounts() {
        section("§7 国庫と外交準備高");

        var b = NationalAccounts.Balances.empty();
        check("初期残高は0", b.gdp() == 0);

        check("稼得expの0.5倍が計上される", NationalAccounts.accrual(30_000) == 15_000);
        check("端数は切り捨てる", NationalAccounts.accrual(101) == 50);

        b = NationalAccounts.accrue(b, 30_000);
        check("計上は外交準備高に入り、国庫は増えない",
                b.reserve() == 15_000 && b.treasury() == 0);

        b = NationalAccounts.donate(b, 5_000);
        check("納入は国庫に入る", b.treasury() == 5_000 && b.reserve() == 15_000);
        check("国内総生産は両者の合計", b.gdp() == 20_000);

        var pay = NationalAccounts.payDiplomatic(b, 10_000);
        check("外交コストは外交準備高から支払う",
                pay.fulfilled() && pay.fromReserve() == 10_000 && pay.fromTreasury() == 0
                        && pay.after().treasury() == 5_000);

        pay = NationalAccounts.payDiplomatic(b, 18_000);
        check("外交準備高が不足すれば国庫から補填する",
                pay.fulfilled() && pay.fromReserve() == 15_000 && pay.fromTreasury() == 3_000
                        && pay.after().gdp() == 2_000);

        pay = NationalAccounts.payDiplomatic(b, 25_000);
        check("両方を使っても足りなければ不履行額が残る",
                !pay.fulfilled() && pay.unpaid() == 5_000 && pay.after().gdp() == 0);

        pay = NationalAccounts.payDomestic(b, 10_000);
        check("国内の支払いは外交準備高に手を付けない",
                !pay.fulfilled() && pay.fromReserve() == 0 && pay.fromTreasury() == 5_000
                        && pay.unpaid() == 5_000 && pay.after().reserve() == 15_000);

        var received = NationalAccounts.receiveDiplomatic(b, 1_000);
        check("対外受取は外交準備高に入る",
                received.reserve() == 16_000 && received.treasury() == 5_000);

        // 援助金
        check("償却は3%（切り上げ）",
                NationalAccounts.aidBurn(10_000) == 300 && NationalAccounts.aidBurn(1) == 1
                        && NationalAccounts.aidBurn(101) == 4);

        var donor = new NationalAccounts.Balances(0, 100_000);
        var receiver = NationalAccounts.Balances.empty();
        var aid = NationalAccounts.sendAid(donor, receiver, 10_000, true);
        check("援助金は外交準備高から出て、受領国の国庫に入る",
                aid.fulfilled() && aid.burned() == 300 && aid.delivered() == 9_700
                        && aid.senderAfter().reserve() == 90_000
                        && aid.receiverAfter().treasury() == 9_700);
        check("償却分は国内総生産の合計から失われる",
                aid.senderAfter().gdp() + aid.receiverAfter().gdp() == 100_000 - 300);

        var aidToReserve = NationalAccounts.sendAid(donor, receiver, 10_000, false);
        check("受領国は外交準備高で受け取ることもできる",
                aidToReserve.receiverAfter().reserve() == 9_700
                        && aidToReserve.receiverAfter().treasury() == 0);

        var poor = new NationalAccounts.Balances(0, 4_000);
        var partial = NationalAccounts.sendAid(poor, receiver, 10_000, true);
        check("残高を超える援助は払える分だけ実行され、不履行額が残る",
                !partial.fulfilled() && partial.unpaid() == 6_000
                        && partial.delivered() == 4_000 - NationalAccounts.aidBurn(4_000));

        // 往復による資金洗浄の摩擦
        long start = 1_000_000;
        var a1 = NationalAccounts.sendAid(new NationalAccounts.Balances(0, start),
                NationalAccounts.Balances.empty(), start, true);
        check("外交準備高から国庫への転換は必ず3%を失う",
                a1.receiverAfter().treasury() == start - NationalAccounts.aidBurn(start));
    }

    // ---------------------------------------------------------------- §7.2

    private static void ranking() {
        section("§7.2 国内総生産ランキング");

        List<GdpRanking.Entry> entries = List.of(
                new GdpRanking.Entry("北方連合", 50_000, 9_000),
                new GdpRanking.Entry("東方帝国", 120_000, 1_000),
                new GdpRanking.Entry("南方公国", 50_000, 4_000),
                new GdpRanking.Entry("西方王国", 10_000, 20_000));
        var rows = GdpRanking.rank(entries);

        check("総額の大きい順に並ぶ",
                rows.get(0).nationName().equals("東方帝国") && rows.get(0).rank() == 1);
        check("同額は同順位になる",
                rows.get(1).rank() == 2 && rows.get(2).rank() == 2);
        check("同順位の次は順位が飛ぶ", rows.get(3).rank() == 4);
        check("同額どうしは国家名で安定して並ぶ",
                rows.get(1).nationName().equals("北方連合")
                        && rows.get(2).nationName().equals("南方公国"));
        check("空でも落ちない", GdpRanking.rank(List.of()).isEmpty());

        var byProduction = GdpRanking.rankByProduction(entries);
        check("直近30日の生産額でも並べられる",
                byProduction.get(0).nationName().equals("西方王国")
                        && byProduction.get(0).rank() == 1);
        check("蓄積の大国が生産では下位になりうる",
                byProduction.get(3).nationName().equals("東方帝国"));
        check("行は両方の指標を持つ",
                rows.get(0).production30d() == 1_000 && rows.get(0).gdp() == 120_000);

        section("§7.1 外交準備高の月次減価");

        var rich = new NationalAccounts.Balances(50_000, 1_000_000);
        var decay = NationalAccounts.decayMonthly(rich);
        check("外交準備高の10%が償却される",
                decay.burned() == 100_000 && decay.after().reserve() == 900_000);
        check("国庫は減価しない", decay.after().treasury() == 50_000);
        check("端数は切り上げる", NationalAccounts.decayMonthly(
                new NationalAccounts.Balances(0, 101)).burned() == 11);
        check("残高0なら何も起きない", NationalAccounts.decayMonthly(
                NationalAccounts.Balances.empty()).burned() == 0);

        // 定常状態: 月間計上 P に対し、残高は概ね 10P に収束する
        long monthly = 600_000;
        var acc = NationalAccounts.Balances.empty();
        for (int month = 0; month < 200; month++) {
            acc = acc.withReserve(acc.reserve() + monthly);
            acc = NationalAccounts.decayMonthly(acc).after();
        }
        check("月次10%の減価により残高は月間計上の約9倍で釣り合う",
                acc.reserve() >= monthly * 89 / 10 && acc.reserve() <= monthly * 9);

        section("§7.3 援助金の受領上限");

        var ledger = new AidLedger();
        check("上限は国内総生産の20%",
                AidLedger.cap(1_000_000) == 200_000);
        check("蓄積が乏しくても下限30,000は確保される",
                AidLedger.cap(0) == 30_000 && AidLedger.cap(100_000) == 30_000);

        check("上限内なら受領できる", ledger.check(100, 1_000_000, 200_000).allowed());
        check("上限を超えると拒否される",
                !ledger.check(100, 1_000_000, 200_001).allowed());

        ledger.record(100, 150_000);
        check("受領後は残枠が減る", ledger.remaining(100, 1_000_000) == 50_000);
        check("残枠を超える受領は部分的にも実行しない",
                !ledger.check(100, 1_000_000, 60_000).allowed()
                        && ledger.check(100, 1_000_000, 50_000).allowed());
        check("29日後はまだ枠に含まれる", ledger.receivedInWindow(129) == 150_000);
        check("30日を過ぎた受領は枠から外れる", ledger.receivedInWindow(130) == 0);
        check("枠が空けば再び受領できる", ledger.remaining(130, 1_000_000) == 200_000);
    }

    // ---------------------------------------------------------------- §8.1

    private static void alliance() {
        section("§8.1 同盟");

        check("継続料は 2,000 × 自国ランク", Alliance.upkeep(10) == 20_000);
        check("初期費用は継続料1回分", Alliance.initialCost(10) == Alliance.upkeep(10));
        check("保有数は体制で決まる",
                Government.REPUBLIC.allianceLimit() == 2 && Government.MONARCHY.allianceLimit() == 1
                        && Government.NONE.allianceLimit() == 1);

        check("双方 rank5 以上で締結できる",
                Alliance.canForm(5, 5, Government.NONE, 0, 0, false, false, false).allowed());
        check("自国が rank5 未満では締結できない",
                Alliance.canForm(4, 10, Government.NONE, 0, 0, false, false, false).denial()
                        == Alliance.Denial.RANK_TOO_LOW);
        check("相手が rank5 未満でも締結できない",
                Alliance.canForm(10, 4, Government.NONE, 0, 0, false, false, false).denial()
                        == Alliance.Denial.PARTNER_RANK_TOO_LOW);
        check("君主制は2つ目の同盟を結べない",
                Alliance.canForm(20, 20, Government.MONARCHY, 1, 0, false, false, false).denial()
                        == Alliance.Denial.LIMIT_REACHED);
        check("共和制は2つ目まで結べる",
                Alliance.canForm(20, 20, Government.REPUBLIC, 1, 0, false, false, false).allowed());
        check("再締結クールダウン中は結べない",
                Alliance.canForm(20, 20, Government.REPUBLIC, 0, 5, false, false, false).denial()
                        == Alliance.Denial.COOLDOWN);
        check("属国は同盟を発議できない",
                Alliance.canForm(20, 20, Government.REPUBLIC, 0, 0, false, true, false).denial()
                        == Alliance.Denial.IS_VASSAL);

        var rich = new NationalAccounts.Balances(0, 100_000);
        var billing = Alliance.bill(rich, 10);
        check("継続料は外交準備高から引き落とされる",
                !billing.dissolved() && billing.paid() == 20_000
                        && billing.after().reserve() == 80_000);

        var broke = new NationalAccounts.Balances(1_000, 2_000);
        var failed = Alliance.bill(broke, 10);
        check("払えなければ即解消となる",
                failed.dissolved() && failed.paid() == 3_000 && failed.after().gdp() == 0);
    }

    // ---------------------------------------------------------------- §8.2

    private static void vassalage() {
        section("§8.2 属国");

        check("rank7未満は属国を持てない", Vassalage.limit(6, Government.NONE) == 0);
        check("rank7〜19 は1国", Vassalage.limit(10, Government.NONE) == 1);
        check("rank20以上は2国", Vassalage.limit(20, Government.NONE) == 2);
        check("共和制は +1 で最大3国", Vassalage.limit(20, Government.REPUBLIC) == 3);
        check("君主制は保有不可",
                Vassalage.limit(25, Government.MONARCHY) == 0);

        check("上納は 100 × 属国ランク / 日", Vassalage.tributePerDay(10) == 1_000);
        check("独立時は直前30日分を一括支払い", Vassalage.independenceCost(10) == 30_000);

        var vassal = new NationalAccounts.Balances(0, 10_000);
        var tribute = Vassalage.collect(vassal, 10);
        check("上納は10%が世界政府、90%が宗主国へ",
                tribute.total() == 1_000 && tribute.toWorld() == 100
                        && tribute.toSuzerain() == 900 && tribute.unpaid() == 0);

        var poorVassal = new NationalAccounts.Balances(0, 400);
        var partial = Vassalage.collect(poorVassal, 10);
        check("払えない分は不履行として残る",
                partial.total() == 400 && partial.unpaid() == 600);

        check("成立には宗主国の承認が必要",
                Vassalage.canSubjugate(20, Government.NONE, 0, false, false, false, false, 0, false, false)
                        .denial() == Vassalage.Denial.NOT_APPROVED);
        check("承認があれば成立する",
                Vassalage.canSubjugate(20, Government.NONE, 0, false, false, false, false, 0, true, false)
                        .allowed());
        check("君主制は属国を取れない",
                Vassalage.canSubjugate(25, Government.MONARCHY, 0, false, false, false, false, 0, true, false)
                        .denial() == Vassalage.Denial.MONARCHY);
        check("属国は属国を持てない",
                Vassalage.canSubjugate(20, Government.NONE, 0, false, false, true, false, 0, true, false)
                        .denial() == Vassalage.Denial.SUZERAIN_IS_VASSAL);
        check("属国を持つ国家は従属できない",
                Vassalage.canSubjugate(20, Government.NONE, 0, false, true, false, false, 0, true, false)
                        .denial() == Vassalage.Denial.VASSAL_HAS_VASSALS);
        check("既に他国の属国なら従属できない",
                Vassalage.canSubjugate(20, Government.NONE, 0, true, false, false, false, 0, true, false)
                        .denial() == Vassalage.Denial.ALREADY_VASSAL);
        check("野営地は従属できない",
                Vassalage.canSubjugate(20, Government.NONE, 0, false, false, false, true, 0, true, false)
                        .denial() == Vassalage.Denial.CAMP);
        check("再従属クールダウン中は成立しない",
                Vassalage.canSubjugate(20, Government.NONE, 0, false, false, false, false, 10, true, false)
                        .denial() == Vassalage.Denial.COOLDOWN);

        var joined = List.of("先発国", "中堅国", "後発国");
        check("上限超過は最後に加盟した属国から解消する",
                Vassalage.resolveExcess(joined, 1).equals(List.of("後発国", "中堅国")));
        check("上限内なら解消しない", Vassalage.resolveExcess(joined, 3).isEmpty());
    }

    // ---------------------------------------------------------------- §10

    private static void sanction() {
        section("§10 制裁");

        check("承認は他国の1/3（切り上げ）",
                Sanction.requiredApprovals(3) == 1 && Sanction.requiredApprovals(4) == 2
                        && Sanction.requiredApprovals(6) == 2 && Sanction.requiredApprovals(0) == 0);

        check("1回の制裁額は合計ランク × 1,000", Sanction.installment(50) == 50_000);
        check("7日で合計 350,000（rank25×2）", Sanction.total(50) == 350_000);

        var dist = Sanction.distribute(50_000, 2);
        check("15%を賛成国で等分し、残りは世界政府へ",
                dist.perSupporter() == 3_750 && dist.toWorld() == 50_000 - 7_500);
        var odd = Sanction.distribute(100, 3);
        check("端数は世界政府に寄せる",
                odd.perSupporter() == 5 && odd.toWorld() == 85);

        check("否決時は対象国のランク × 1,000 を各賛成国が支払う",
                Sanction.rejectionPenaltyPerSupporter(20) == 20_000);
        check("不払いは10,000expあたり1hの減算",
                Sanction.unpaidActivityPenaltyHours(35_000) == 3.5);

        check("共和制のみ発起できる",
                Sanction.canInitiate(Government.MONARCHY, 25, 61, false, 0, false, 5, 6, false).denial()
                        == Sanction.Denial.NOT_REPUBLIC);
        check("rank14以下では発起権が停止する",
                Sanction.canInitiate(Government.REPUBLIC, 14, 61, false, 0, false, 5, 6, false).denial()
                        == Sanction.Denial.RANK_TOO_LOW);
        check("制裁中の国家は発起できない",
                Sanction.canInitiate(Government.REPUBLIC, 20, 61, true, 0, false, 5, 6, false).denial()
                        == Sanction.Denial.UNDER_SANCTION);
        check("同一国家への再制裁はクールダウンに従う",
                Sanction.canInitiate(Government.REPUBLIC, 20, 61, false, 30, false, 5, 6, false).denial()
                        == Sanction.Denial.COOLDOWN);
        check("同盟国・属国は対象にできない",
                Sanction.canInitiate(Government.REPUBLIC, 20, 61, false, 0, true, 5, 6, false).denial()
                        == Sanction.Denial.TARGET_IS_ALLY);
        check("承認が足りなければ発起できない",
                Sanction.canInitiate(Government.REPUBLIC, 20, 61, false, 0, false, 1, 6, false).denial()
                        == Sanction.Denial.INSUFFICIENT_APPROVALS);
        check("要件を満たせば発起できる",
                Sanction.canInitiate(Government.REPUBLIC, 20, 61, false, 0, false, 2, 6, false).allowed());
        check("開始60日以内は制裁を発起できない",
                Sanction.canInitiate(Government.REPUBLIC, 20, 60, false, 0, false, 2, 6, false).denial()
                        == Sanction.Denial.EMBARGO);
        check("解禁は61日目から",
                Sanction.canInitiate(Government.REPUBLIC, 20, 61, false, 0, false, 2, 6, false).allowed());

        var target = new NationalAccounts.Balances(10_000, 40_000);
        var col = Sanction.collect(target, 50, 2);
        check("徴収は外交準備高から行い、不足分を国庫で補う",
                col.collected() == 50_000 && col.unpaid() == 0
                        && col.after().reserve() == 0 && col.after().treasury() == 0);

        var drained = Sanction.collect(NationalAccounts.Balances.empty(), 50, 2);
        check("残高がなければ全額が不払いになる",
                drained.collected() == 0 && drained.unpaid() == 50_000);
        check("不払い分はランク減少に換算される",
                Sanction.unpaidActivityPenaltyHours(drained.unpaid()) == 5.0);
    }

    // ---------------------------------------------------------------- §11

    private static void war() {
        section("§11 戦争");

        check("開始60日間は実施できない",
                !ServerTimeline.conflictAllowed(1) && !ServerTimeline.conflictAllowed(60)
                        && ServerTimeline.conflictAllowed(61));
        check("解禁までの残日数を数えられる",
                ServerTimeline.daysUntilConflictAllowed(1) == 60
                        && ServerTimeline.daysUntilConflictAllowed(60) == 1
                        && ServerTimeline.daysUntilConflictAllowed(61) == 0);

        check("君主制のみ発起できる",
                War.canInitiate(Government.REPUBLIC, 61, false, 0, 0, false).denial()
                        == War.Denial.NOT_MONARCHY);
        check("開始60日以内は発起できない",
                War.canInitiate(Government.MONARCHY, 60, false, 0, 0, false).denial()
                        == War.Denial.EMBARGO);
        check("関係性がある国には発起できない",
                War.canInitiate(Government.MONARCHY, 61, true, 0, 0, false).denial()
                        == War.Denial.RELATION_EXISTS);
        check("免除期間中の国には発起できない",
                War.canInitiate(Government.MONARCHY, 61, false, 30, 0, false).denial()
                        == War.Denial.TARGET_IMMUNE);
        check("自国の再発起クールダウン中は発起できない",
                War.canInitiate(Government.MONARCHY, 61, false, 0, 100, false).denial()
                        == War.Denial.INITIATOR_COOLDOWN);
        check("要件を満たせば発起できる",
                War.canInitiate(Government.MONARCHY, 61, false, 0, 0, false).allowed());

        check("1〜3勝目は +8",
                War.bonusChunks(1) == 8 && War.bonusChunks(2) == 16 && War.bonusChunks(3) == 24);
        check("4勝目以降は +4",
                War.bonusChunks(4) == 28 && War.bonusChunks(5) == 32);
        check("累積上限は +40",
                War.bonusChunks(7) == 40 && War.bonusChunks(20) == 40);
        check("rank14以下ではボーナスを失う",
                War.effectiveBonus(7, 15) == 40 && War.effectiveBonus(7, 14) == 0);
        check("敗北でランクが1下がる", War.rankAfterDefeat(20) == 19 && War.rankAfterDefeat(0) == 0);
    }

    // ---------------------------------------------------------------- §8.3

    private static void unification() {
        section("§8.3 統一");

        var strong = new Unification.Party("東方帝国", 22, 300, 5_000, false);
        var weak = new Unification.Party("西方王国", 20, 100, 3_000, false);
        var small = new Unification.Party("南方公国", 12, 500, 1_000, false);

        check("rank20以上なら発議できる", Unification.canPropose(strong, small, 0).allowed());
        check("rank20未満は発議できない",
                Unification.canPropose(small, strong, 0).denial()
                        == Unification.Denial.PROPOSER_RANK_TOO_LOW);
        check("属国は発議できない",
                Unification.canPropose(new Unification.Party("属国", 22, 300, 100, true), small, 0)
                        .denial() == Unification.Denial.IS_VASSAL);
        check("再発議クールダウン中は発議できない",
                Unification.canPropose(strong, small, 30).denial() == Unification.Denial.COOLDOWN);

        check("活動が活発な側が存続する",
                Unification.resolveSurvivor(strong, weak).nationName().equals("東方帝国"));
        check("活動が上でも rank20 未満なら存続できない",
                Unification.resolveSurvivor(small, weak).nationName().equals("西方王国"));
        check("両者が rank20 未満なら不成立",
                Unification.resolveSurvivor(small, new Unification.Party("北方連合", 15, 900, 1, false))
                        == null);
        check("累計は単純合算し、C(25) 超も保持する",
                Unification.mergeCumulative(strong, weak) == 8_000);

        check("消滅法人の首長はチーフへ移行する",
                Unification.headTransition(Government.REPUBLIC) == Unification.Role.CHIEF);
        check("君主制では市民になる",
                Unification.headTransition(Government.MONARCHY) == Unification.Role.CITIZEN);

        // 定員超過の除名: 市民 → チーフ → リーダー、首長は除外
        List<Unification.Member> members = List.of(
                new Unification.Member("首長", Unification.Role.HEAD, 1),
                new Unification.Member("リーダーA", Unification.Role.LEADER, 2),
                new Unification.Member("チーフA", Unification.Role.CHIEF, 3),
                new Unification.Member("市民A", Unification.Role.CITIZEN, 50),
                new Unification.Member("市民B", Unification.Role.CITIZEN, 10),
                new Unification.Member("市民C", Unification.Role.CITIZEN, 30));

        var one = Unification.selectExpulsions(members, 5);
        check("活動が最も少ない市民から除名される",
                one.size() == 1 && one.get(0).playerName().equals("市民B"));

        var three = Unification.selectExpulsions(members, 3);
        check("市民から順に除名される",
                three.size() == 3
                        && three.get(0).playerName().equals("市民B")
                        && three.get(1).playerName().equals("市民C")
                        && three.get(2).playerName().equals("市民A"));

        var four = Unification.selectExpulsions(members, 2);
        check("市民が尽きたらチーフを剥奪して対象に加える",
                four.size() == 4 && four.get(3).playerName().equals("チーフA"));

        var five = Unification.selectExpulsions(members, 1);
        check("次にリーダーを剥奪する",
                five.size() == 5 && five.get(4).playerName().equals("リーダーA"));

        var all = Unification.selectExpulsions(members, 0);
        check("首長は除名されない",
                all.size() == 5 && all.stream().noneMatch(m -> m.role() == Unification.Role.HEAD));

        check("定員に収まっていれば除名しない",
                Unification.selectExpulsions(members, 6).isEmpty());

        check("統一ポイントは rank20 以上かつ自国領土内でのみ有効",
                Unification.pointsActive(20, true) && !Unification.pointsActive(19, true)
                        && !Unification.pointsActive(25, false));
    }

    private static Set<ChunkPos> minus(Set<ChunkPos> set, ChunkPos c) {
        Set<ChunkPos> copy = new LinkedHashSet<>(set);
        copy.remove(c);
        return copy;
    }

    private static void section(String name) {
        System.out.println();
        System.out.println("== " + name + " ==");
    }

    private static void check(String label, boolean ok) {
        if (ok) {
            passed++;
            System.out.println("  [OK]   " + label);
        } else {
            failed++;
            System.out.println("  [FAIL] " + label);
        }
    }
}
