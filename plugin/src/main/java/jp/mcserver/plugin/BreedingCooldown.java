package jp.mcserver.plugin;

import org.bukkit.entity.Breedable;
import org.bukkit.entity.LivingEntity;
import org.bukkit.event.EventHandler;
import org.bukkit.event.EventPriority;
import org.bukkit.event.Listener;
import org.bukkit.event.entity.EntityBreedEvent;

/**
 * モブが交配（繁殖）を行う間隔のクールダウンを、全体で20%軽減する
 * （`minecraft_server_spec.md` §1.4）。
 *
 * <p>{@link EntityBreedEvent} が発火する時点で、バニラの実装はすでに双方の親へ
 * 通常のクールダウン（次に「愛情モード」に入れるまでの待ち時間）を課している。
 * ここではその値を1つずつ {@link #REMAINING_RATIO}（0.8）倍に縮め、次に交配できる
 * ようになるまでの待ち時間を短くする——「愛情モードに入ってから実際に交配するまで」の
 * 待ち時間ではなく、<b>交配してから次に交配できるようになるまでの間隔</b>を対象にした
 * （要件の「交配を行う間隔のクールダウン」という文言をそう読んだ）。
 */
final class BreedingCooldown implements Listener {

    /** 軽減後に残る割合。0.8 = 20%軽減。 */
    static final double REMAINING_RATIO = 0.8;

    @EventHandler(priority = EventPriority.MONITOR, ignoreCancelled = true)
    public void onBreed(EntityBreedEvent event) {
        reduce(event.getMother());
        reduce(event.getFather());
    }

    private void reduce(LivingEntity parent) {
        if (parent instanceof Breedable breedable) {
            int reduced = (int) Math.round(breedable.getBreedCooldown() * REMAINING_RATIO);
            breedable.setBreedCooldown(reduced);
        }
    }
}
