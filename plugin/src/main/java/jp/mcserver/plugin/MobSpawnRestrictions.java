package jp.mcserver.plugin;

import java.util.Set;
import org.bukkit.entity.EntityType;
import org.bukkit.event.EventHandler;
import org.bukkit.event.EventPriority;
import org.bukkit.event.Listener;
import org.bukkit.event.entity.CreatureSpawnEvent;
import org.bukkit.event.entity.EntityTransformEvent;

/**
 * 除外要素（`minecraft_server_spec.md` §1.4）のうち、モブそのものを禁止する項目。
 *
 * <p>アレイ・行商人・商人のラマ・ファントム・シュルカー・村人ゾンビは、
 * <b>理由を問わず一切スポーンしない</b>——既存の「不死のトーテム: 入手不可」
 * 「エリトラ: 存在しない」と同じ、例外の無い扱いにした。自然湧き・スポナー・
 * スポーンエッグ・{@code /summon} のいずれも塞ぐため、{@link CreatureSpawnEvent}
 * の {@code SpawnReason} は問わずキャンセルする。
 *
 * <p>村人が村人ゾンビへ変化する経路は、直接のスポーン以外に<b>ゾンビ（またはその亜種）に
 * 襲われて感染する経路</b>もある。こちらは {@link EntityTransformEvent} の
 * {@code TransformReason.INFECTION} で捕まえて、あわせて塞ぐ。
 */
final class MobSpawnRestrictions implements Listener {

    private static final Set<EntityType> BANNED = Set.of(
            EntityType.ALLAY,
            EntityType.WANDERING_TRADER,
            EntityType.TRADER_LLAMA,
            EntityType.PHANTOM,
            EntityType.SHULKER,
            EntityType.ZOMBIE_VILLAGER);

    @EventHandler(priority = EventPriority.HIGHEST)
    public void onSpawn(CreatureSpawnEvent event) {
        if (BANNED.contains(event.getEntityType())) {
            event.setCancelled(true);
        }
    }

    /** 村人がゾンビ（の亜種を含む）に襲われて村人ゾンビへ変化する経路を塞ぐ。 */
    @EventHandler(priority = EventPriority.HIGHEST, ignoreCancelled = true)
    public void onTransform(EntityTransformEvent event) {
        if (event.getTransformReason() == EntityTransformEvent.TransformReason.INFECTION) {
            event.setCancelled(true);
        }
    }
}
