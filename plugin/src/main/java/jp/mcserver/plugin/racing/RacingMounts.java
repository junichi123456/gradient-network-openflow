package jp.mcserver.plugin.racing;

import java.util.Optional;
import java.util.UUID;
import jp.mcserver.core.racing.AbilityStat;
import jp.mcserver.core.racing.MountSpeed;
import net.kyori.adventure.text.Component;
import org.bukkit.Bukkit;
import org.bukkit.Location;
import org.bukkit.Material;
import org.bukkit.NamespacedKey;
import org.bukkit.World;
import org.bukkit.attribute.Attribute;
import org.bukkit.attribute.AttributeInstance;
import org.bukkit.entity.Entity;
import org.bukkit.entity.Horse;
import org.bukkit.entity.Player;
import org.bukkit.event.EventHandler;
import org.bukkit.event.Listener;
import org.bukkit.event.world.EntitiesLoadEvent;
import org.bukkit.inventory.ItemStack;
import org.bukkit.persistence.PersistentDataType;
import org.bukkit.plugin.java.JavaPlugin;

/**
 * 記録した競走馬（{@link RacingDatabase}）を、ゲーム内の馬として出す（{@code /horse spawn}）。
 *
 * <p><b>1つの記録につき、ゲーム内の馬は1頭だけ。</b>出し直すと古い馬を消す。古い馬が
 * 読み込まれていないチャンクにいて消せなかった場合は、そのチャンクが読み込まれた時点で
 * 消す（{@link #onEntitiesLoad}）——どの馬が「いまの1頭」かは{@code entity_uuid}列で持つ。
 *
 * <p>速さは出す場所で変える（{@link MountSpeed}）。競馬専用次元
 * （ワールド名 {@value #RACING_WORLD}。まだ作っていない）では仕様書どおりの10〜25m/s、
 * それ以外の国家ワールドではバニラの馬の範囲に縮めて当てはめる。馬が後から別の
 * ワールドへ移った場合の掛け直しは、競馬専用次元を作るときに入れる。
 */
final class RacingMounts implements Listener {

    /** 競馬専用次元のワールド名（§27.1）。この名前のワールドでだけ仕様書どおりの速さにする。 */
    static final String RACING_WORLD = "racing";

    private static final String TAG = "racing_horse_id";

    private final RacingDatabase database;
    private final NamespacedKey key;

    RacingMounts(JavaPlugin plugin, RacingDatabase database) {
        this.database = database;
        this.key = new NamespacedKey(plugin, TAG);
    }

    /** プレイヤーの足元に、記録と紐づいた馬を出す。すでに出していれば古い方を消す。 */
    Horse spawn(Player owner, RacingDatabase.HorseRecord record) {
        record.entityUuid().map(Bukkit::getEntity).ifPresent(Entity::remove);
        Location at = owner.getLocation();
        World world = at.getWorld();
        Horse horse = world.spawn(at, Horse.class, h -> {
            h.setAdult();
            h.setTamed(true);
            h.setOwner(owner);
            h.getInventory().setSaddle(new ItemStack(Material.SADDLE));
            h.customName(Component.text(record.name()));
            h.setCustomNameVisible(true);
            h.setRemoveWhenFarAway(false);
            h.getPersistentDataContainer().set(key, PersistentDataType.INTEGER, record.horseId());
            applySpeed(h, world, record.currentValues().get(AbilityStat.SPEED));
        });
        database.setEntityUuid(record.horseId(), horse.getUniqueId());
        return horse;
    }

    /** 調教などで能力値が変わったあと、出している馬の速さを合わせ直す。出していなければ何もしない。 */
    void refreshSpeed(RacingDatabase.HorseRecord record) {
        record.entityUuid().map(Bukkit::getEntity)
                .filter(entity -> entity instanceof Horse)
                .ifPresent(entity -> applySpeed((Horse) entity, entity.getWorld(),
                        record.currentValues().get(AbilityStat.SPEED)));
    }

    /** ゲーム内での最高速度（m/s）の目安。表示用。 */
    static double metersPerSecond(Horse horse) {
        AttributeInstance speed = horse.getAttribute(Attribute.MOVEMENT_SPEED);
        return speed == null ? 0 : speed.getBaseValue() * MountSpeed.METERS_PER_SECOND_PER_ATTRIBUTE;
    }

    static boolean isRacingWorld(World world) {
        return world.getName().equals(RACING_WORLD);
    }

    private static void applySpeed(Horse horse, World world, int speed) {
        AttributeInstance attribute = horse.getAttribute(Attribute.MOVEMENT_SPEED);
        if (attribute == null) {
            return;
        }
        attribute.setBaseValue(isRacingWorld(world)
                ? MountSpeed.specAttribute(speed)
                : MountSpeed.vanillaScaledAttribute(speed));
    }

    /** 出し直しで置き去りになった古い馬（読み込まれていなかったもの）を、読み込まれた時点で消す。 */
    @EventHandler
    public void onEntitiesLoad(EntitiesLoadEvent event) {
        for (Entity entity : event.getEntities()) {
            Integer horseId = entity.getPersistentDataContainer().get(key, PersistentDataType.INTEGER);
            if (horseId == null) {
                continue;
            }
            Optional<UUID> current = database.horse(horseId).flatMap(RacingDatabase.HorseRecord::entityUuid);
            if (current.isEmpty() || !current.get().equals(entity.getUniqueId())) {
                entity.remove();
            }
        }
    }
}
