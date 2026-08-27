package jp.mcserver.core.raid;

import java.util.ArrayList;
import java.util.List;
import java.util.Random;
import jp.mcserver.core.Raid;

/**
 * 騎士型の戦闘をオフラインで再現する（§12.7）。
 *
 * <p>Minecraft を起動せず、定義どおりに tick を進めて挙動を確認する。
 * 実行: {@code java -cp out jp.mcserver.core.raid.KnightSimulation [参加人数] [1人あたりDPS] [被ダメ軽減%]}
 */
public final class KnightSimulation {

    /** プレイヤーの体力（バニラ）。 */
    private static final double PLAYER_HEALTH = 20;

    /** 制限時間（§12.1）。 */
    private static final int TIME_LIMIT_TICKS = Raid.TIME_LIMIT_MINUTES * 60 * 20;

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

    public static void main(String[] args) {
        int participants = args.length > 0 ? Integer.parseInt(args[0]) : 5;
        double dpsPerPlayer = args.length > 1 ? Double.parseDouble(args[1]) : 8.0;
        double reductionPercent = args.length > 2 ? Double.parseDouble(args[2]) : 80.0;

        RaidSpecies boss = KnightDefinition.boss();
        long maxHealth = boss.healthFor(participants);
        double health = maxHealth;
        Random random = new Random(20260827L);

        List<Player> players = new ArrayList<>();
        for (int i = 1; i <= participants; i++) {
            players.add(new Player(i, 12 + i % 5));
        }

        System.out.println("=== 騎士型 オフライン検証 ===");
        System.out.printf("参加人数 %d / 体力 %,d（基準600 × %.1f倍）%n",
                participants, maxHealth,
                Raid.difficulty(participants).healthMultiplier());
        System.out.printf("1人あたりDPS %.1f / 被ダメ軽減 %.0f%% / 制限時間 %d分%n%n",
                dpsPerPlayer, reductionPercent, Raid.TIME_LIMIT_MINUTES);

        int tick = 0;
        int cycle = 0;
        String phaseName = boss.phaseAt(100).name();
        int motionIndex = 0;
        int totalHits = 0;
        int deaths = 0;

        while (health > 0 && tick < TIME_LIMIT_TICKS && aliveCount(players) > 0) {
            int percent = (int) Math.ceil(health * 100 / maxHealth);
            RaidSpecies.Phase phase = boss.phaseAt(Math.min(100, Math.max(0, percent)));
            if (!phase.name().equals(phaseName)) {
                phaseName = phase.name();
                System.out.printf("[%s] %s へ移行（体力 %,.0f / %d%%）%n",
                        time(tick), phaseName, health, percent);
            }

            List<String> names = phase.motionNames();
            MotionSpec motion = phase.motion(names.get(motionIndex++ % names.size()));
            cycle++;

            // 待機 → 移動 → 攻撃モーション
            int idle = phase.behavior().idleTicks();
            int approach = phase.behavior().approachTicks();
            health -= dpsPerPlayer * aliveCount(players) * (idle + approach) / 20.0;
            tick += idle;
            double closed = phase.behavior().approachDistance();
            for (Player player : players) {
                player.distance = Math.max(1.5, player.distance - closed);
            }
            tick += approach;

            int hitsThisMotion = 0;
            for (MotionSpec.DamageWindow window : motion.damageWindows()) {
                Player target = nearest(players);
                if (target == null) {
                    break;
                }
                double amount = roll(window.damage(), random) * (1 - reductionPercent / 100.0);
                target.health -= amount;
                hitsThisMotion++;
                totalHits++;
                if (!target.alive()) {
                    deaths++;
                    System.out.printf("[%s] プレイヤー%d が %s で戦闘不能（%s %.1f 被弾）%n",
                            time(tick + window.fromTick()), target.id, motion.name(),
                            window.part(), amount);
                }
            }
            if (motion.area().isPresent()) {
                MotionSpec.AreaEffect area = motion.area().get();
                for (Player player : players) {
                    if (player.alive() && player.distance <= area.radiusBlocks()) {
                        player.health -= area.damage().average() * (1 - reductionPercent / 100.0);
                        totalHits++;
                        if (!player.alive()) {
                            deaths++;
                            System.out.printf("[%s] プレイヤー%d が 衝撃波で戦闘不能%n",
                                    time(tick), player.id);
                        }
                    }
                }
            }

            health -= dpsPerPlayer * aliveCount(players) * motion.animation().durationTicks() / 20.0;
            tick += motion.animation().durationTicks();

            if (cycle <= 6 || cycle % 10 == 0) {
                System.out.printf("[%s] 周期%2d %-8s（%3dtick, 判定%d回, 最大%.0f） 体力 %,.0f 残 生存%d/%d%n",
                        time(tick), cycle, motion.name(), motion.animation().durationTicks(),
                        hitsThisMotion, motion.maxDamage(), Math.max(0, health),
                        aliveCount(players), participants);
            }
        }

        System.out.println();
        if (health <= 0) {
            System.out.printf("討伐成功: %s（%d周期）%n", time(tick), cycle);
        } else if (aliveCount(players) == 0) {
            System.out.printf("全滅: %s（%d周期）%n", time(tick), cycle);
        } else {
            System.out.printf("時間切れ: 体力 %,.0f 残（%d%%）%n", health, (int) (health * 100 / maxHealth));
        }
        System.out.printf("被弾 %d 回 / 戦闘不能 %d 名 / 生存 %d 名%n",
                totalHits, deaths, aliveCount(players));

        System.out.println();
        System.out.println("--- モーションのサンプリング（なぎ払い・右腕） ---");
        Animation sweep = boss.phaseAt(100).motion("なぎ払い").animation();
        for (int t = 0; t <= sweep.durationTicks(); t += 2) {
            Transform sample = sweep.sample("右腕", t);
            System.out.printf("tick %2d  回転Y %6.1f度%n", t, sample.rotationDeg().y());
        }

        System.out.println();
        System.out.println("--- 通信量 ---");
        System.out.printf("必要な更新間隔: %dtick（観戦20名）%n", boss.requiredUpdateInterval(20));
        System.out.printf("動く部位2・更新間隔2tick・20名 → 毎秒 %d 件（上限 %d）%n",
                MotionBudget.updatesPerSecond(2, 2, 20), MotionBudget.MAX_UPDATES_PER_SECOND);
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

    private static int aliveCount(List<Player> players) {
        return (int) players.stream().filter(Player::alive).count();
    }

    private static String time(int tick) {
        int seconds = tick / 20;
        return String.format("%d:%02d", seconds / 60, seconds % 60);
    }
}
