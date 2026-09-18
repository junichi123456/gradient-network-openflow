package jp.mcserver.plugin.worldcouncil;

import java.util.List;
import jp.mcserver.core.worldcouncil.WorldCouncilPayout;
import jp.mcserver.core.worldcouncil.WorldCouncilRoster;
import org.bukkit.Bukkit;
import org.bukkit.command.Command;
import org.bukkit.command.CommandExecutor;
import org.bukkit.command.CommandSender;
import org.bukkit.entity.Player;

/**
 * {@code /worldcouncil} コマンド一式（`world_council_spec.md`）。
 *
 * <p>開催は管理者トリガー（ユーザーへ確認して決定）。BLOCK CONQUEST 本体（盤面・カード・
 * 進行）は今回対象外のため、<b>順位の決定は管理者が {@code finish} で直接入力する</b>
 * ——本来ならゲーム側の最終得点から {@code WorldCouncilRanking} で算出するが、
 * その入力元（盤面の得点データ）が今回は存在しない。
 *
 * <p>国家ランク（rank7以上という参加資格の判定に使う）も、ライブな国家ランク管理が
 * まだ無いため（`rail_infra_spec.md` §6 と同じ制約）、{@code register} で管理者が
 * 申告する値を使う。
 */
public final class WorldCouncilCommand implements CommandExecutor {

    private final WorldCouncilModule module;

    public WorldCouncilCommand(WorldCouncilModule module) {
        this.module = module;
    }

    @Override
    public boolean onCommand(CommandSender sender, Command command, String label, String[] args) {
        String sub = args.length > 0 ? args[0] : "status";
        switch (sub) {
            case "register" -> register(sender, args);
            case "addrep" -> addRep(sender, args);
            case "status" -> status(sender);
            case "begin" -> begin(sender);
            case "advanceday" -> advanceDay(sender);
            case "finish" -> finish(sender, args);
            default -> sender.sendMessage("§7/worldcouncil register <国家> <rank> "
                    + "| /worldcouncil addrep <国家> <プレイヤー> | /worldcouncil status "
                    + "| /worldcouncil begin | /worldcouncil advanceday "
                    + "| /worldcouncil finish <1位> <2位> <3位> <4位>");
        }
        return true;
    }

    private void register(CommandSender sender, String[] args) {
        if (args.length < 3) {
            sender.sendMessage("§c使い方: /worldcouncil register <国家> <rank>");
            return;
        }
        String nation = args[1];
        int rank;
        try {
            rank = Integer.parseInt(args[2]);
        } catch (NumberFormatException e) {
            sender.sendMessage("§crank は数値で指定してください");
            return;
        }
        var check = WorldCouncilRoster.canRegisterNation(nation, rank, module.rosterEntries());
        if (!check.allowed()) {
            sender.sendMessage("§c" + check.message());
            return;
        }
        module.roster().put(nation, new java.util.ArrayList<>());
        sender.sendMessage("§a" + nation + " を登録しました（" + module.roster().size()
                + "/" + WorldCouncilRoster.MAX_NATIONS + "か国）");
    }

    private void addRep(CommandSender sender, String[] args) {
        if (args.length < 3) {
            sender.sendMessage("§c使い方: /worldcouncil addrep <国家> <プレイヤー>");
            return;
        }
        String nation = args[1];
        String playerName = args[2];
        List<String> reps = module.roster().get(nation);
        if (reps == null) {
            sender.sendMessage("§c" + nation + " は登録されていません（先に register）");
            return;
        }
        var entry = new WorldCouncilRoster.Entry(nation, reps);
        var check = WorldCouncilRoster.canAddRepresentative(playerName, entry, module.rosterEntries());
        if (!check.allowed()) {
            sender.sendMessage("§c" + check.message());
            return;
        }
        reps.add(playerName);
        sender.sendMessage("§a" + playerName + " を " + nation + " の代表者に追加しました（"
                + reps.size() + "/" + WorldCouncilRoster.REPRESENTATIVES_PER_NATION + "名）");
    }

    private void status(CommandSender sender) {
        sender.sendMessage("§7=== 世界協議 参加登録 ===");
        if (module.roster().isEmpty()) {
            sender.sendMessage("§7（未登録）");
        }
        module.roster().forEach((nation, reps) ->
                sender.sendMessage("§f" + nation + " §7: " + String.join(", ", reps)));
        sender.sendMessage(WorldCouncilRoster.ready(module.rosterEntries())
                ? "§a開催可能です（/worldcouncil begin）"
                : "§7" + WorldCouncilRoster.MAX_NATIONS + "か国×"
                        + WorldCouncilRoster.REPRESENTATIVES_PER_NATION + "名が揃うまで開催できません");
    }

    private void begin(CommandSender sender) {
        if (!WorldCouncilRoster.ready(module.rosterEntries())) {
            sender.sendMessage("§c参加登録が揃っていません（/worldcouncil status で確認）");
            return;
        }
        int moved = 0;
        for (List<String> reps : module.roster().values()) {
            for (String name : reps) {
                Player player = Bukkit.getPlayerExact(name);
                if (player == null) {
                    sender.sendMessage("§c" + name + " がオンラインではありません。移送を中止しました");
                    return;
                }
                module.arena().enter(module.plugin(), player);
                moved++;
            }
        }
        sender.sendMessage("§a" + moved + "名を専用ワールドへ移送しました");
    }

    private void advanceDay(CommandSender sender) {
        var world = WorldCouncilArena.world(module.plugin());
        WorldCouncilDayCycle.advanceToNoon(module.plugin(), world);
        sender.sendMessage("§7日照サイクルを進めています（2秒後に正午で停止）");
    }

    private void finish(CommandSender sender, String[] args) {
        if (args.length < 5) {
            sender.sendMessage("§c使い方: /worldcouncil finish <1位> <2位> <3位> <4位>");
            return;
        }
        for (int rank = 1; rank <= 4; rank++) {
            String nation = args[rank];
            if (!module.roster().containsKey(nation)) {
                sender.sendMessage("§c" + nation + " は登録されていません");
                return;
            }
            long amount = WorldCouncilPayout.amountFor(rank);
            module.ledger().deposit(nation, amount);
            sender.sendMessage("§a" + rank + "位 " + nation + " に " + amount + " を非課税で還付しました");
        }

        int moved = 0;
        for (List<String> reps : module.roster().values()) {
            for (String name : reps) {
                Player player = Bukkit.getPlayerExact(name);
                if (player != null) {
                    module.arena().exit(player);
                    moved++;
                }
            }
        }
        sender.sendMessage("§a" + moved + "名を国家ワールドへ復帰させました");
        module.clearRoster();
    }
}
