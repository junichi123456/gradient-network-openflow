package jp.mcserver.plugin.rail;

import java.util.HashMap;
import java.util.Map;
import java.util.Optional;
import java.util.UUID;
import jp.mcserver.core.rail.DiplomacyQuota;
import jp.mcserver.core.rail.MonthlyBilling;
import jp.mcserver.core.rail.StationCertification;
import org.bukkit.Bukkit;
import org.bukkit.Location;
import org.bukkit.OfflinePlayer;
import org.bukkit.command.Command;
import org.bukkit.command.CommandExecutor;
import org.bukkit.command.CommandSender;
import org.bukkit.entity.Player;

/**
 * {@code /rail} コマンド一式（`rail_infra_spec.md` §5）。
 *
 * <p><b>駅舎の範囲指定は要件定義書の {@code /rail station create} 1個だけの想定より
 * 手順を増やしている</b>——「プレイヤーが立っている空間」を自動で検出する（壁をたどって
 * 部屋の形を見つける）処理は本書にアルゴリズムが無く、この回では見送った。代わりに
 * {@code /rail station pos1}/{@code pos2} で対角の2点を指定してから {@code create} を
 * 打つ、`WorldEdit` に近い手順にしてある。自動検出は今後の課題として残る。
 */
public final class RailCommand implements CommandExecutor {

    private final RailModule module;
    private final Map<UUID, Location> pos1 = new HashMap<>();
    private final Map<UUID, Location> pos2 = new HashMap<>();

    public RailCommand(RailModule module) {
        this.module = module;
    }

    @Override
    public boolean onCommand(CommandSender sender, Command command, String label, String[] args) {
        String sub = args.length > 0 ? args[0] : "check";
        switch (sub) {
            case "check" -> check(sender);
            case "station" -> station(sender, args);
            case "admin" -> admin(sender, args);
            default -> sender.sendMessage("§7/rail check | /rail station pos1|pos2|create"
                    + " | /rail admin ...");
        }
        return true;
    }

    // ------------------------------------------------------------ /rail check

    private void check(CommandSender sender) {
        Player player = requirePlayer(sender);
        if (player == null) {
            return;
        }
        Optional<String> nationOpt = module.database().nationOfPlayer(player.getUniqueId());
        if (nationOpt.isEmpty()) {
            sender.sendMessage("§c国家に所属していません（/rail admin setnation で登録できます）");
            return;
        }
        String nationId = nationOpt.get();
        int placed = module.database().placedThisMonth(nationId);
        int limit = (int) DiplomacyQuota.effectiveLimit(module.database().allianceCount(nationId),
                module.database().suzerainOfVassalCount(nationId));
        long maintenance = module.database().railsByNation(nationId).stream()
                .mapToLong(r -> MonthlyBilling.maintenanceCost(r.type(), r.outsideTerritory()))
                .sum();
        var balances = module.database().balances(nationId);
        sender.sendMessage("§7国家: §f" + nationId);
        sender.sendMessage("§7当月の設置数: §f" + placed + " / " + limit);
        sender.sendMessage("§7月額推定維持費: §f" + maintenance);
        sender.sendMessage("§7国庫: §f" + balances.treasury() + " §7外交準備高: §f"
                + balances.reserve());
    }

    // ------------------------------------------------------------ /rail station

    private void station(CommandSender sender, String[] args) {
        Player player = requirePlayer(sender);
        if (player == null) {
            return;
        }
        String action = args.length > 1 ? args[1] : "";
        switch (action) {
            case "pos1" -> {
                pos1.put(player.getUniqueId(), player.getLocation());
                sender.sendMessage("§71点目を記録しました");
            }
            case "pos2" -> {
                pos2.put(player.getUniqueId(), player.getLocation());
                sender.sendMessage("§72点目を記録しました");
            }
            case "create" -> createStation(sender, player);
            default -> sender.sendMessage("§7/rail station pos1 | pos2 | create");
        }
    }

    private void createStation(CommandSender sender, Player player) {
        Location a = pos1.get(player.getUniqueId());
        Location b = pos2.get(player.getUniqueId());
        if (a == null || b == null) {
            sender.sendMessage("§c先に /rail station pos1 と pos2 で対角の2点を指定してください");
            return;
        }
        if (!a.getWorld().equals(b.getWorld())) {
            sender.sendMessage("§c2点が別のワールドにあります");
            return;
        }
        Optional<String> nationOpt = module.database().nationOfPlayer(player.getUniqueId());
        if (nationOpt.isEmpty()) {
            sender.sendMessage("§c国家に所属していません");
            return;
        }
        String nationId = nationOpt.get();

        StationScanner.ScanResult scan = StationScanner.scan(a, b, module.config());
        double distance = module.stationIndex().nearestDistance(a.getWorld().getName(),
                scan.centerX(), scan.centerY(), scan.centerZ());
        var certification = StationCertification.certifyBox(scan.outerWidth(), scan.outerHeight(),
                scan.outerDepth(), scan.qualifyingCountsByFace(), scan.blockCounts(), distance);

        sender.sendMessage("§7外寸: §f" + scan.outerWidth() + "×" + scan.outerHeight() + "×"
                + scan.outerDepth() + (certification.spaceOk() ? " §a(OK)" : " §c(範囲外)"));
        sender.sendMessage("§7各面の舗装率40%以上: " + (certification.facesOk() ? "§aOK" : "§c不合格"));
        sender.sendMessage("§7対象ブロック合計: §f" + certification.blocks().effectiveTotal()
                + (certification.blocks().totalPassed() ? " §a(1,600以上)" : " §c(不足)")
                + "、主ブロック比率 " + String.format("%.1f%%", certification.blocks().primaryRatio() * 100)
                + (certification.blocks().ratioPassed() ? " §a(70%以上)" : " §c(不足)"));
        sender.sendMessage("§7最寄りの既存駅舎までの距離: §f"
                + (Double.isInfinite(distance) ? "なし" : String.format("%.1f", distance))
                + (certification.distanceOk() ? " §a(OK)" : " §c(150ブロック以内)"));

        if (!certification.certified()) {
            sender.sendMessage("§c駅舎として認定されませんでした");
            return;
        }
        int minX = Math.min(a.getBlockX(), b.getBlockX());
        int minY = Math.min(a.getBlockY(), b.getBlockY());
        int minZ = Math.min(a.getBlockZ(), b.getBlockZ());
        int maxX = Math.max(a.getBlockX(), b.getBlockX());
        int maxY = Math.max(a.getBlockY(), b.getBlockY());
        int maxZ = Math.max(a.getBlockZ(), b.getBlockZ());
        module.database().insertStation(nationId, a.getWorld().getName(), minX, minY, minZ,
                maxX, maxY, maxZ);
        // DBへの登録とあわせて、判定に使うメモリ上の一覧にも足す（§6「負荷対策」）
        module.stationIndex().add(a.getWorld().getName(), minX, minY, minZ, maxX, maxY, maxZ);
        sender.sendMessage("§a駅舎として認定しました。以降この範囲では Mob が湧きません");
    }

    // ------------------------------------------------------------ /rail admin

    private void admin(CommandSender sender, String[] args) {
        if (!sender.hasPermission("rail.admin")) {
            sender.sendMessage("§c権限がありません");
            return;
        }
        String action = args.length > 1 ? args[1] : "";
        switch (action) {
            case "reset" -> {
                if (args.length < 3) {
                    sender.sendMessage("§7/rail admin reset <nation>");
                    return;
                }
                module.database().resetMonthlyCount(args[2]);
                sender.sendMessage("§7" + args[2] + " の当月設置カウントをリセットしました");
            }
            case "reload" -> {
                module.reloadConfig();
                sender.sendMessage("§7config.yml を読み直しました（対象ブロック "
                        + module.config().qualifyingBlocks().size() + " 種を解決）");
            }
            case "bill-now" -> {
                module.billingTask().billAll();
                sender.sendMessage("§7月末維持費請求を手動で実行しました");
            }
            case "claim", "unclaim" -> {
                Player player = requirePlayer(sender);
                if (player == null) {
                    return;
                }
                int chunkX = player.getLocation().getBlockX() >> 4;
                int chunkZ = player.getLocation().getBlockZ() >> 4;
                String world = player.getWorld().getName();
                if (action.equals("claim")) {
                    if (args.length < 3) {
                        sender.sendMessage("§7/rail admin claim <nation>");
                        return;
                    }
                    module.database().claimChunk(world, chunkX, chunkZ, args[2]);
                    sender.sendMessage("§7このチャンクを " + args[2] + " の領土として登録しました");
                } else {
                    module.database().unclaimChunk(world, chunkX, chunkZ);
                    sender.sendMessage("§7このチャンクの登録を外しました");
                }
            }
            case "setnation" -> {
                if (args.length < 4) {
                    sender.sendMessage("§7/rail admin setnation <player> <nation>");
                    return;
                }
                OfflinePlayer target = Bukkit.getOfflinePlayer(args[2]);
                module.database().setNationOfPlayer(target.getUniqueId(), args[3]);
                sender.sendMessage("§7" + args[2] + " を " + args[3] + " に所属させました");
            }
            case "deposit" -> {
                if (args.length < 4) {
                    sender.sendMessage("§7/rail admin deposit <nation> <amount>");
                    return;
                }
                module.database().deposit(args[2], Long.parseLong(args[3]));
                sender.sendMessage("§7" + args[2] + " の国庫へ " + args[3] + " を納入しました");
            }
            case "ally" -> {
                if (args.length < 4) {
                    sender.sendMessage("§7/rail admin ally <nation> <nation>");
                    return;
                }
                module.database().addAlliance(args[2], args[3]);
                sender.sendMessage("§7" + args[2] + " と " + args[3] + " を同盟として登録しました");
            }
            case "vassal" -> {
                if (args.length < 4) {
                    sender.sendMessage("§7/rail admin vassal <suzerain> <vassal>");
                    return;
                }
                module.database().setSuzerain(args[3], args[2]);
                sender.sendMessage("§7" + args[3] + " を " + args[2] + " の属国として登録しました");
            }
            default -> sender.sendMessage("§7/rail admin reset|reload|bill-now|claim|unclaim"
                    + "|setnation|deposit|ally|vassal");
        }
    }

    private Player requirePlayer(CommandSender sender) {
        if (sender instanceof Player player) {
            return player;
        }
        sender.sendMessage("プレイヤーから実行してください");
        return null;
    }
}
