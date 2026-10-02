package jp.mcserver.plugin;

import java.util.List;
import java.util.UUID;
import org.bukkit.Location;
import org.bukkit.Material;
import org.bukkit.entity.Player;

/**
 * レイド個体が {@link RaidPlugin} に対して持つ共通の窓口。
 *
 * <p>{@code RaidPlugin} は種ごとの実装（{@link KnightBoss}、{@link HollowGuardBoss}）を
 * 区別せずに扱う。被弾の分配・討伐時のドロップ配布・状態表示はどの個体でも同じ形をしており、
 * 個体ごとの挙動（モーションの選び方、移動の仕方）だけがそれぞれの実装に閉じている。
 */
interface RaidBoss {

    /** 死んだか。 */
    boolean isDead();

    /** 表示エンティティと当たり判定を片付ける。 */
    void despawn();

    /** 討伐の演出。 */
    void playDefeat();

    /** 状態の一行要約（{@code /raid info}）。 */
    String status();

    /** 表示へ送っている変換と当たり判定の位置（{@code /raid dump}）。 */
    List<String> describe();

    /**
     * 部位への攻撃を受け取る。
     *
     * @return この個体が処理した攻撃か。false なら他の個体へ回す
     */
    boolean handleHit(UUID hitEntity, Player attacker, Location origin, boolean ranged,
                      Material weapon);

    /** ドロップを受け取る資格のある者（§12.4）。生死は問わない。 */
    List<UUID> rewarded();

    /** その者が個体へ与えたダメージ。案内に使う。 */
    double dealtBy(UUID player);

    /** ドロップの配布に必要なダメージ量。 */
    double rewardThreshold();
}
