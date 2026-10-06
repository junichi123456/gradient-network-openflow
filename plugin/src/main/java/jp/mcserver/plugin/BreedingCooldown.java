package jp.mcserver.plugin;

import org.bukkit.entity.Breedable;
import org.bukkit.entity.LivingEntity;
import org.bukkit.event.EventHandler;
import org.bukkit.event.EventPriority;
import org.bukkit.event.Listener;
import org.bukkit.event.entity.EntityBreedEvent;
import org.bukkit.plugin.Plugin;
import org.bukkit.plugin.java.JavaPlugin;

/**
 * モブが交配（繁殖）を行う間隔のクールダウンを、全体で20%軽減する
 * （`minecraft_server_spec.md` §1.4）。
 *
 * <p>Bukkit API に「交配クールダウン」専用の口は無い。バニラでは、成体の
 * {@link org.bukkit.entity.Ageable#getAge() 年齢}が正の値であることがそのまま
 * 「次に交配できるまでの残りtick」を表し、毎tick 1ずつ0へ向かって減る。交配が成立すると
 * 双方の親の年齢が6000（5分）にセットされる。
 *
 * <p><b>その6000は{@link EntityBreedEvent}の発火後に、同じtickの中でセットされる</b>
 * （Paper の {@code Animal} / {@code VillagerMakeLove} のパッチで確認。イベントが
 * キャンセルされなかった場合にだけ年齢を設定する順序になっている）。そのためイベントの
 * 中で年齢を読み書きしても、直後にバニラの6000で上書きされる。ここでは次tickに回して、
 * セット済みの残りtickを{@link #REMAINING_RATIO}（0.8）倍に縮める——「交配してから
 * 次に交配できるようになるまでの間隔」を対象にした（要件の「交配を行う間隔の
 * クールダウン」という文言をそう読んだ）。
 */
final class BreedingCooldown implements Listener {

    /** 軽減後に残る割合。0.8 = 20%軽減。 */
    static final double REMAINING_RATIO = 0.8;

    @EventHandler(priority = EventPriority.MONITOR, ignoreCancelled = true)
    public void onBreed(EntityBreedEvent event) {
        LivingEntity mother = event.getMother();
        LivingEntity father = event.getFather();
        Plugin plugin = JavaPlugin.getProvidingPlugin(BreedingCooldown.class);
        plugin.getServer().getScheduler().runTask(plugin, () -> {
            reduce(mother);
            reduce(father);
        });
    }

    private static void reduce(LivingEntity parent) {
        if (parent instanceof Breedable breedable && breedable.isValid()) {
            int remaining = breedable.getAge();
            if (remaining > 0) {
                breedable.setAge((int) Math.round(remaining * REMAINING_RATIO));
            }
        }
    }
}
