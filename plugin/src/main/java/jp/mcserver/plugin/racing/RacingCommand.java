package jp.mcserver.plugin.racing;

import java.time.LocalDateTime;
import java.util.List;
import java.util.Map;
import java.util.Optional;
import java.util.Random;
import jp.mcserver.core.racing.AbilityStat;
import jp.mcserver.core.racing.AbilityValue;
import jp.mcserver.core.racing.TrainingMenu;
import jp.mcserver.core.racing.TrainingOutcome;
import org.bukkit.command.Command;
import org.bukkit.command.CommandExecutor;
import org.bukkit.command.CommandSender;
import org.bukkit.entity.Player;

/**
 * {@code /horse} コマンド一式（`minecraft_server_spec.md` §27.3）。
 *
 * <p><b>実機で最初に触れる部分として、能力値の確認と1日1回の調教だけを対象にする。</b>
 * 血統に基づく出生（§27.2）はまだ無いため、{@code /horse create}は出生時上限
 * （{@link AbilityValue#BIRTH_CAP}）以内の一様乱数で仮の能力値を割り当てる暫定実装。
 * 調教の成否確率も、コンディション連動の式（§23で決定）の代わりに固定の暫定確率
 * （失敗20%・成功55%・大成功25%）を使う。実測後にどちらも差し替える。
 */
public final class RacingCommand implements CommandExecutor {

    private static final int FAILURE_PERCENT = 20;
    private static final int SUCCESS_PERCENT = 55;
    // 残り25%が大成功

    private final RacingModule module;
    private final Random random = new Random();

    public RacingCommand(RacingModule module) {
        this.module = module;
    }

    @Override
    public boolean onCommand(CommandSender sender, Command command, String label, String[] args) {
        Player player = requirePlayer(sender);
        if (player == null) {
            return true;
        }
        String sub = args.length > 0 ? args[0] : "list";
        switch (sub) {
            case "create" -> create(player, args);
            case "train" -> train(player, args);
            case "status" -> status(player, args);
            case "list" -> list(player);
            case "admin" -> admin(player, args);
            default -> sender.sendMessage("§7/horse create <名前> | /horse train <id> "
                    + "slope|woodchip|pool|paired|rest | /horse status <id> | /horse list"
                    + " | /horse admin resetday <id>");
        }
        return true;
    }

    // ------------------------------------------------------------ /horse create

    private void create(Player player, String[] args) {
        if (args.length < 2) {
            player.sendMessage("§c使い方: /horse create <名前>");
            return;
        }
        Optional<String> nation = module.database().nationOfPlayer(player.getUniqueId());
        if (nation.isEmpty()) {
            player.sendMessage("§c国家に所属していません");
            return;
        }
        String name = args[1];
        Map<AbilityStat, Integer> birth = new java.util.EnumMap<>(AbilityStat.class);
        for (AbilityStat stat : AbilityStat.values()) {
            birth.put(stat, random.nextInt(AbilityValue.BIRTH_CAP + 1));
        }
        int horseId = module.database().createHorse(player.getUniqueId(), nation.get(), name, birth);
        player.sendMessage("§a競走馬「" + name + "」を登録しました（ID " + horseId + "）");
        player.sendMessage(statusLines(module.database().horse(horseId).orElseThrow()));
        player.sendMessage("§7出生時の能力値は暫定の一様乱数です（血統システムは未実装、§27.2）");
    }

    // ------------------------------------------------------------ /horse train

    private void train(Player player, String[] args) {
        if (args.length < 3) {
            player.sendMessage("§c使い方: /horse train <id> slope|woodchip|pool|paired|rest");
            return;
        }
        Optional<RacingDatabase.HorseRecord> horseOpt = parseOwnedHorse(player, args[1]);
        if (horseOpt.isEmpty()) {
            return;
        }
        RacingDatabase.HorseRecord horse = horseOpt.get();

        TrainingMenu menu = parseMenu(args[2]);
        if (menu == null) {
            player.sendMessage("§c調教メニューが不正です: slope|woodchip|pool|paired|rest");
            return;
        }

        int today = trainingDay();
        if (horse.trainedOn(today)) {
            player.sendMessage("§c本日はすでに調教済みです（毎日0:30更新）");
            return;
        }

        if (menu == TrainingMenu.REST) {
            module.database().rest(horse.horseId(), today);
            player.sendMessage("§a「" + horse.name() + "」を休養させました（疲労0まで回復）");
            return;
        }

        if (menu.targetStats().stream().allMatch(stat ->
                horse.currentValues().get(stat) - horse.birthValues().get(stat) >= AbilityValue.MAX_TRAINING_GROWTH)) {
            player.sendMessage("§c対象の能力値がすべて調教の生涯成長上限（+20）に達しているため、"
                    + "このメニューでは調教できません（expは徴収されません）");
            return;
        }

        long cost = menu.costExp();
        long treasury = module.database().treasuryOf(horse.nationId());
        if (treasury < cost) {
            player.sendMessage("§c国庫の残高不足のため調教できません（必要 " + cost
                    + " exp、残高 " + treasury + " exp、§27.11）");
            return;
        }
        module.ledger().payDomestic(horse.nationId(), cost);

        TrainingOutcome outcome = rollOutcome();
        Map<AbilityStat, Integer> applied = module.database().train(horse.horseId(), menu, outcome, today);

        player.sendMessage("§6調教結果: " + outcomeLabel(outcome)
                + "（国庫から " + cost + " exp徴収、§27.11）");
        applied.forEach((stat, gain) -> player.sendMessage("§7  " + statLabel(stat) + " +" + gain
                + (gain < outcome.statGain() ? "（生涯成長上限+20に到達したため頭打ち）" : "")));
        player.sendMessage(statusLines(module.database().horse(horse.horseId()).orElseThrow()));
    }

    // ------------------------------------------------------------ /horse status, list

    private void status(Player player, String[] args) {
        if (args.length < 2) {
            player.sendMessage("§c使い方: /horse status <id>");
            return;
        }
        parseOwnedHorse(player, args[1]).ifPresent(horse -> player.sendMessage(statusLines(horse)));
    }

    private void list(Player player) {
        List<RacingDatabase.HorseRecord> horses = module.database().horsesOwnedBy(player.getUniqueId());
        if (horses.isEmpty()) {
            player.sendMessage("§7所有している競走馬はいません（/horse create <名前>）");
            return;
        }
        player.sendMessage("§6所有する競走馬 " + horses.size() + "頭");
        for (RacingDatabase.HorseRecord horse : horses) {
            player.sendMessage("§7  #" + horse.horseId() + " " + horse.name());
        }
    }

    // ------------------------------------------------------------ /horse admin（検証用）

    /**
     * 検証用の運営コマンド。{@code resetday}は1日1回の制限を外して同じ馬を続けて
     * 調教できるようにする——生涯成長上限+20や疲労の積み上がりを、実時間を待たずに
     * 確かめるため。所有者を問わず、どの馬にも使える。本番前に外すか残すかは未定。
     */
    private void admin(Player player, String[] args) {
        if (!player.hasPermission("horse.admin")) {
            player.sendMessage("§c権限がありません");
            return;
        }
        String action = args.length > 1 ? args[1] : "";
        if (!action.equals("resetday") || args.length < 3) {
            player.sendMessage("§c使い方: /horse admin resetday <id>");
            return;
        }
        int id;
        try {
            id = Integer.parseInt(args[2]);
        } catch (NumberFormatException e) {
            player.sendMessage("§c馬のIDは数値で指定してください");
            return;
        }
        if (module.database().clearLastTrainedDay(id)) {
            player.sendMessage("§a#" + id + " の本日の調教済みを解除しました（検証用）");
        } else {
            player.sendMessage("§c馬が見つかりません（ID " + id + "）");
        }
    }

    // ------------------------------------------------------------ 内部処理

    private String statusLines(RacingDatabase.HorseRecord horse) {
        double baseSpeed = AbilityValue.baseSpeedMetersPerSecond(horse.currentValues().get(AbilityStat.SPEED));
        StringBuilder sb = new StringBuilder();
        sb.append("§6#").append(horse.horseId()).append(" ").append(horse.name())
                .append(" §7(疲労 ").append(horse.fatigue()).append(", 信頼レベル ")
                .append(horse.trustLevel()).append(")\n");
        for (AbilityStat stat : AbilityStat.values()) {
            sb.append("§7  ").append(statLabel(stat)).append(" ")
                    .append(horse.currentValues().get(stat))
                    .append("（出生 ").append(horse.birthValues().get(stat)).append("）");
            if (stat != AbilityStat.WISDOM) {
                sb.append('\n');
            }
        }
        sb.append("\n§7  基礎移動速度 ").append(String.format("%.2f", baseSpeed)).append("m/s");
        return sb.toString();
    }

    private Optional<RacingDatabase.HorseRecord> parseOwnedHorse(Player player, String idArg) {
        int id;
        try {
            id = Integer.parseInt(idArg);
        } catch (NumberFormatException e) {
            player.sendMessage("§c馬のIDは数値で指定してください");
            return Optional.empty();
        }
        Optional<RacingDatabase.HorseRecord> horse = module.database().horse(id);
        if (horse.isEmpty() || !horse.get().owner().equals(player.getUniqueId())) {
            player.sendMessage("§c自分が所有する馬が見つかりません（ID " + id + "）");
            return Optional.empty();
        }
        return horse;
    }

    private static TrainingMenu parseMenu(String arg) {
        return switch (arg) {
            case "slope" -> TrainingMenu.SLOPE;
            case "woodchip" -> TrainingMenu.WOOD_CHIP;
            case "pool" -> TrainingMenu.POOL;
            case "paired" -> TrainingMenu.PAIRED;
            case "rest" -> TrainingMenu.REST;
            default -> null;
        };
    }

    private TrainingOutcome rollOutcome() {
        int roll = random.nextInt(100);
        if (roll < FAILURE_PERCENT) {
            return TrainingOutcome.FAILURE;
        }
        if (roll < FAILURE_PERCENT + SUCCESS_PERCENT) {
            return TrainingOutcome.SUCCESS;
        }
        return TrainingOutcome.GREAT_SUCCESS;
    }

    private static String outcomeLabel(TrainingOutcome outcome) {
        return switch (outcome) {
            case FAILURE -> "失敗";
            case SUCCESS -> "成功";
            case GREAT_SUCCESS -> "大成功";
        };
    }

    private static String statLabel(AbilityStat stat) {
        return switch (stat) {
            case SPEED -> "スピード";
            case STAMINA -> "スタミナ";
            case POWER -> "パワー";
            case GUTS -> "根性";
            case WISDOM -> "賢さ";
        };
    }

    /**
     * 調教日（§27.3、毎日0:30更新）。0:00〜0:29は前日扱いとする——境界をまたいだ直後に
     * 連続で調教できてしまわないようにするため。
     */
    static int trainingDay() {
        LocalDateTime now = LocalDateTime.now();
        LocalDateTime effective = now.getHour() == 0 && now.getMinute() < 30
                ? now.minusDays(1)
                : now;
        return (int) effective.toLocalDate().toEpochDay();
    }

    private static Player requirePlayer(CommandSender sender) {
        if (sender instanceof Player player) {
            return player;
        }
        sender.sendMessage("プレイヤーから実行してください");
        return null;
    }
}
