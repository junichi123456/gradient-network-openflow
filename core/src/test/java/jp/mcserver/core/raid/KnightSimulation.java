package jp.mcserver.core.raid;

import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.Random;
import jp.mcserver.core.Raid;

/**
 * 騎士型の戦闘をオフラインで再現する（§12.7）。
 *
 * <p>Minecraft を起動せず、定義どおりに tick を進めて挙動を確認する。
 * モーションの選択・弱点の露出・激昂まで、実機と同じ機構を通す。
 *
 * <p>実行:
 * {@code java -cp out jp.mcserver.core.raid.KnightSimulation [参加人数] [1人あたりDPS] [被ダメ軽減%] [パリイ成功率%]}
 */
public final class KnightSimulation {

    /** プレイヤーの体力（バニラ）。 */
    private static final double PLAYER_HEALTH = 20;

    /** 制限時間（§12.1）。 */
    private static final int TIME_LIMIT_TICKS = Raid.TIME_LIMIT_MINUTES * 60 * 20;

    /** 槍の判定が届く距離（ブロック）。 */
    private static final double REACH = 5.0;

    private static final class Player {
        final int id;
        double health = PLAYER_HEALTH;
        double distance;

        Player(int id, double distance) {
            this.id = id;
            this.distance = distance;
        }

        boolean alive() {
            return health > 0;
        }
    }

    /** 個体の状態をまとめて進めるための入れ物。 */
    private static final class Fight {
        RaidSpecies boss;
        RaidSpecies.Phase phase;
        PartTracker parts;
        final RageMeter rage = new RageMeter();
        MotionSelector selector = new MotionSelector(20260827L);
        long maxHealth;
        double health;
        int tick;
        int enrageCount;
        double exposedDamage;
        double normalDamage;
        int exposureCount;
        int parryCount;
        final Map<String, Integer> motionCounts = new LinkedHashMap<>();
    }

    public static void main(String[] args) {
        int participants = args.length > 0 ? Integer.parseInt(args[0]) : 5;
        double dpsPerPlayer = args.length > 1 ? Double.parseDouble(args[1]) : 8.0;
        double reductionPercent = args.length > 2 ? Double.parseDouble(args[2]) : 80.0;
        // パリイは盾ではなく、区間に与えたダメージで判定する（§12.6）。
        // ここでは「パリイの区間に攻撃を通せた割合」として与える
        double parryPercent = args.length > 3 ? Double.parseDouble(args[3]) : 30.0;

        Random random = new Random(20260827L);
        Fight fight = new Fight();
        fight.boss = KnightDefinition.boss();
        fight.phase = fight.boss.phaseAt(100);
        fight.maxHealth = fight.boss.healthFor(participants);
        fight.health = fight.maxHealth;
        fight.parts = new PartTracker(fight.boss.rigFor(fight.phase));

        List<Player> players = new ArrayList<>();
        for (int i = 1; i <= participants; i++) {
            players.add(new Player(i, 12 + i % 5));
        }

        System.out.println("=== 騎士型 オフライン検証 ===");
        System.out.printf("参加人数 %d / 体力 %,d（基準600 × %.1f倍）%n",
                participants, fight.maxHealth,
                Raid.difficulty(participants).healthMultiplier());
        System.out.printf("1人あたりDPS %.1f / 被ダメ軽減 %.0f%% / パリイ区間の攻撃通過率 %.0f%% / 制限時間 %d分%n",
                dpsPerPlayer, reductionPercent, parryPercent, Raid.TIME_LIMIT_MINUTES);
        System.out.printf("パリイに要するダメージ: 1回目 %.0f、成功するたびに +%.0f%n",
                KnightDefinition.PARRY_BASE_DAMAGE, KnightDefinition.PARRY_DAMAGE_INCREASE);
        System.out.printf("弱点 頭 ×%.1f（露出中のみ %d tick / 空振りは %d tick）%n%n",
                KnightDefinition.HEAD_VULNERABILITY, PartTracker.EXPOSURE_TICKS,
                PartTracker.WHIFF_EXPOSURE_TICKS);

        int cycle = 0;
        String phaseName = fight.phase.name();
        int totalHits = 0;
        int deaths = 0;

        while (fight.health > 0 && fight.tick < TIME_LIMIT_TICKS && aliveCount(players) > 0) {
            int percent = (int) Math.ceil(fight.health * 100 / fight.maxHealth);
            RaidSpecies.Phase next = fight.boss.phaseAt(Math.min(100, Math.max(0, percent)));
            if (!next.name().equals(phaseName)) {
                fight.phase = next;
                phaseName = next.name();
                fight.parts = new PartTracker(fight.boss.rigFor(next));
                fight.rage.reset();
                fight.selector.reset();
                System.out.printf("[%s] %s へ移行（体力 %,.0f / %d%%）%n",
                        time(fight.tick), phaseName, fight.health, percent);
            }

            cycle++;
            double distance = nearestDistance(players);
            int surrounding = surrounding(players);
            var situation = new MotionSelector.Situation(distance, surrounding,
                    fight.rage.enraged());
            var choice = fight.selector.select(fight.phase, situation, fight.tick);
            MotionSpec motion = choice.motion();
            fight.motionCounts.merge(motion.name(), 1, Integer::sum);

            // 待機
            int idle = fight.rage.idleTicks(fight.phase.behavior().idleTicks());
            advance(fight, players, dpsPerPlayer, idle);

            // 移動
            int approach = fight.phase.behavior().approachTicks();
            advance(fight, players, dpsPerPlayer, approach);
            // プレイヤーは張り付かず、間合いを取り直す。距離帯ごとの技の出方を見るため
            double closed = fight.phase.behavior().approachDistance();
            for (Player player : players) {
                double reposition = random.nextDouble() * 8.0;
                player.distance = Math.max(1.5, player.distance - closed + reposition);
            }

            // 攻撃モーション
            //
            // パリイは区間に与えたダメージで判定する。区間のあいだに集団が通せる
            // ダメージ量を見積もり、必要量に届くかで成否を決める
            boolean parried = false;
            var parry = motion.parry().orElse(null);
            if (parry != null) {
                int windowTicks = parry.toTick() - parry.fromTick();
                double reachable = dpsPerPlayer * aliveCount(players) * windowTicks / 20.0
                        * (parryPercent / 100.0);
                double required = parry.requiredDamage(fight.parryCount);
                if (reachable >= required) {
                    parried = true;
                    fight.parryCount++;
                    fight.parts.expose(PartTracker.EXPOSURE_TICKS);
                    fight.exposureCount++;
                    System.out.printf(
                            "[%s] パリイ成功（%d回目） — %s を止めた"
                                    + "（%.0f / %.0f ダメージ・弱点が %dtick 露出）次は %.0f 必要%n",
                            time(fight.tick), fight.parryCount, motion.name(), reachable, required,
                            PartTracker.EXPOSURE_TICKS, parry.requiredDamage(fight.parryCount));
                }
            }

            boolean landed = false;
            int hitsThisMotion = 0;
            int cursor = 0;
            for (MotionSpec.DamageWindow window : motion.damageWindows()) {
                if (parried) {
                    break;
                }
                advance(fight, players, dpsPerPlayer, window.fromTick() - cursor);
                cursor = window.fromTick();
                Player target = nearest(players);
                if (target == null) {
                    break;
                }
                if (target.distance > REACH && motion.charge().isEmpty()
                        && motion.area().isEmpty()) {
                    continue;
                }
                double amount = roll(window.damage(), random)
                        * fight.rage.damageMultiplier() * (1 - reductionPercent / 100.0);
                target.health -= amount;
                landed = true;
                fight.rage.landedHit();
                hitsThisMotion++;
                totalHits++;
                if (!target.alive()) {
                    deaths++;
                    System.out.printf("[%s] プレイヤー%d が %s で戦闘不能（%s %.1f 被弾）%n",
                            time(fight.tick), target.id, motion.name(), window.part(), amount);
                }
            }

            if (!parried && motion.area().isPresent()) {
                MotionSpec.AreaEffect area = motion.area().get();
                for (Player player : players) {
                    if (player.alive() && player.distance <= area.radiusBlocks()) {
                        player.health -= area.damage().average() * fight.rage.damageMultiplier()
                                * (1 - reductionPercent / 100.0);
                        landed = true;
                        fight.rage.landedHit();
                        totalHits++;
                        if (!player.alive()) {
                            deaths++;
                            System.out.printf("[%s] プレイヤー%d が 衝撃波で戦闘不能%n",
                                    time(fight.tick), player.id);
                        }
                    }
                }
            }

            advance(fight, players, dpsPerPlayer,
                    motion.animation().durationTicks() - cursor);

            // 空振りは隙になる
            if (!parried && !landed && motion.charge().isPresent()) {
                fight.parts.expose(PartTracker.WHIFF_EXPOSURE_TICKS);
                fight.exposureCount++;
                System.out.printf("[%s] %s が空振り — 弱点が %dtick 露出%n",
                        time(fight.tick), motion.name(), PartTracker.WHIFF_EXPOSURE_TICKS);
            }

            // 中断・パリイの後は長い隙、それ以外は通常の待機
            int after = parried
                    ? motion.interrupt().map(MotionSpec.Interrupt::idleTicks)
                            .orElse(motion.idleAfterTicks())
                    : 0;
            advance(fight, players, dpsPerPlayer, after);

            if (cycle <= 8 || cycle % 10 == 0) {
                System.out.printf(
                        "[%s] 周期%2d %-8s%s（%3dtick, 判定%d回, 最大%.0f） 体力 %,.0f 残 生存%d/%d%s%n",
                        time(fight.tick), cycle, motion.name(), choice.relaxed() ? "*" : " ",
                        motion.animation().durationTicks(), hitsThisMotion, motion.maxDamage(),
                        Math.max(0, fight.health), aliveCount(players), participants,
                        fight.rage.enraged() ? " [激昂]" : "");
            }
        }

        report(fight, players, participants, totalHits, deaths, cycle);
    }

    /**
     * 指定 tick ぶん進める。プレイヤーの攻撃を部位へ通し、露出と激昂を更新する。
     */
    private static void advance(Fight fight, List<Player> players, double dpsPerPlayer,
                                int ticks) {
        for (int i = 0; i < Math.max(0, ticks); i++) {
            fight.tick++;
            fight.parts.tick();
            boolean wasEnraged = fight.rage.enraged();
            fight.rage.tick();
            if (!wasEnraged && fight.rage.enraged()) {
                fight.enrageCount++;
                System.out.printf("[%s] 激昂した — 待機が %dtick に縮み、弱点が閉じる%n",
                        time(fight.tick), RageMeter.ENRAGED_IDLE_TICKS);
            }

            double raw = dpsPerPlayer * aliveCount(players) / 20.0;
            if (raw <= 0) {
                continue;
            }
            String target = chooseTarget(fight);
            PartTracker.Result result = fight.parts.hit(target, raw, fight.rage.enraged());
            fight.health -= result.dealt();
            if (result.critical()) {
                fight.exposedDamage += result.dealt();
            } else {
                fight.normalDamage += result.dealt();
            }
        }
    }

    /**
     * プレイヤー側の狙い方。露出しているなら頭、そうでなければ胴を殴る。
     * 倍率が乗るのは露出を作れた時間だけである、という設計をこの方針で確かめる。
     */
    private static String chooseTarget(Fight fight) {
        Rig rig = fight.boss.rigFor(fight.phase);
        if (fight.parts.exposed() && !fight.rage.enraged()) {
            return "頭";
        }
        return rig.root().name();
    }

    private static void report(Fight fight, List<Player> players, int participants,
                               int totalHits, int deaths, int cycle) {
        System.out.println();
        if (fight.health <= 0) {
            System.out.printf("討伐成功: %s（%d周期）%n", time(fight.tick), cycle);
        } else if (aliveCount(players) == 0) {
            System.out.printf("全滅: %s（%d周期）%n", time(fight.tick), cycle);
        } else {
            System.out.printf("時間切れ: 体力 %,.0f 残（%d%%）%n",
                    fight.health, (int) (fight.health * 100 / fight.maxHealth));
        }
        System.out.printf("被弾 %d 回 / 戦闘不能 %d 名 / 生存 %d 名%n",
                totalHits, deaths, aliveCount(players));

        System.out.println();
        System.out.println("--- 攻略の内訳 ---");
        double total = fight.exposedDamage + fight.normalDamage;
        System.out.printf("倍率が乗ったダメージ %,.0f（%.0f%%）/ 素のダメージ %,.0f%n",
                fight.exposedDamage, total == 0 ? 0 : fight.exposedDamage * 100 / total,
                fight.normalDamage);
        System.out.printf("弱点の露出を作った回数 %d 回（うちパリイ %d 回）%n",
                fight.exposureCount, fight.parryCount);
        System.out.printf("激昂 %d 回%n", fight.enrageCount);

        System.out.println();
        System.out.println("--- モーションの出現回数（* は距離帯の緩和が起きた選択） ---");
        fight.motionCounts.forEach((name, count) ->
                System.out.printf("%-10s %3d 回%n", name, count));

        System.out.println();
        System.out.println("--- モーションのサンプリング（なぎ払い・右腕） ---");
        Animation sweep = fight.boss.phaseAt(100).motion("なぎ払い").animation();
        for (int t = 0; t <= sweep.durationTicks(); t += 2) {
            Transform sample = sweep.sample("右腕", t);
            System.out.printf("tick %2d  回転Y %6.1f度%n", t, sample.rotationDeg().y());
        }

        System.out.println();
        System.out.println("--- 待機モーションのサンプリング（頭・ループ） ---");
        Animation idle = fight.boss.phaseAt(100).behavior().idleAnimation().orElseThrow();
        for (int t = 0; t <= idle.durationTicks(); t += 5) {
            System.out.printf("tick %2d  回転Y %6.1f度%n", t,
                    idle.sample("頭", t).rotationDeg().y());
        }

        System.out.println();
        System.out.println("--- 通信量 ---");
        int viewers = Raid.MAX_PARTICIPANTS;
        Rig rig = fight.boss.rigFor(fight.boss.phases().get(fight.boss.phases().size() - 1));
        int moving = rig.movingDisplays(rig.partNames().stream()
                .filter(name -> rig.part(name).isRoot()).toList());
        int hitboxes = rig.interactiveParts().stream()
                .mapToInt(Rig.Part::hitboxSegments).sum();
        System.out.printf("必要な更新間隔: %dtick（観戦 %d名）%n",
                fight.boss.requiredUpdateInterval(viewers), viewers);
        System.out.printf("姿勢更新: 表示 %d × 10Hz × %d名 → 毎秒 %,d 件（上限 %,d）%n",
                moving, viewers, MotionBudget.updatesPerSecond(moving, 2, viewers),
                MotionBudget.MAX_UPDATES_PER_SECOND);
        System.out.printf("位置の追従: (表示 %d ＋ 判定 %d) × 20Hz × %d名 → 毎秒 %,d 件%n",
                rig.displayCount(), hitboxes, viewers,
                (rig.displayCount() + hitboxes) * 20 * viewers);
        System.out.printf("合計 毎秒 %,d 件。参加人数の上限 %d名 は、この数から引いた線である%n",
                MotionBudget.updatesPerSecond(moving, 2, viewers)
                        + (rig.displayCount() + hitboxes) * 20 * viewers,
                Raid.MAX_PARTICIPANTS);
    }

    private static double roll(MotionSpec.Damage damage, Random random) {
        return damage.random()
                ? damage.min() + random.nextDouble() * (damage.max() - damage.min())
                : damage.min();
    }

    private static Player nearest(List<Player> players) {
        return players.stream().filter(Player::alive)
                .min((a, b) -> Double.compare(a.distance, b.distance)).orElse(null);
    }

    private static double nearestDistance(List<Player> players) {
        Player target = nearest(players);
        return target == null ? 0 : target.distance;
    }

    private static int surrounding(List<Player> players) {
        return (int) players.stream().filter(Player::alive)
                .filter(player -> player.distance <= MotionSpec.Usage.CROWD_RADIUS).count();
    }

    private static int aliveCount(List<Player> players) {
        return (int) players.stream().filter(Player::alive).count();
    }

    private static String time(int tick) {
        int seconds = tick / 20;
        return String.format("%d:%02d", seconds / 60, seconds % 60);
    }
}
