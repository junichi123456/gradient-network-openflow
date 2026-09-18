package jp.mcserver.plugin.rail;

import java.time.LocalDate;
import java.time.YearMonth;
import java.util.logging.Logger;
import jp.mcserver.core.rail.MonthlyBilling;
import org.bukkit.plugin.Plugin;
import org.bukkit.scheduler.BukkitRunnable;

/**
 * F-03 月末維持費請求タスク（`rail_infra_spec.md`）。
 *
 * <p>「毎月最終日の指定日時に」という規定を、<b>Bukkit のスケジューラーで毎日チェックし、
 * その日が月末ならその日のうちに1回だけ実行する</b>形で満たす——ユーザーへ確認して決定
 * （外部cronのような、サーバーの外側の仕組みは使わない）。時刻そのものの厳密さは
 * 要件定義書に無いため、サーバーが動いている限り月末の1日のどこかで必ず1回走る、
 * という緩さで足りるとした。
 */
public final class MonthlyBillingTask extends BukkitRunnable {

    private final RailDatabase db;
    private final Logger logger;

    /** 二重請求を防ぐ、直近に請求した年月（"yyyy-M"）。 */
    private String lastBilledMonth;

    public MonthlyBillingTask(RailDatabase db, Logger logger) {
        this.db = db;
        this.logger = logger;
    }

    /** 起動20秒後から、以降24時間ごとにチェックする。 */
    public static MonthlyBillingTask scheduleDaily(Plugin plugin, RailDatabase db, Logger logger) {
        MonthlyBillingTask task = new MonthlyBillingTask(db, logger);
        task.runTaskTimer(plugin, 20L * 20, 20L * 60 * 60 * 24);
        return task;
    }

    @Override
    public void run() {
        LocalDate today = LocalDate.now();
        if (!today.equals(YearMonth.from(today).atEndOfMonth())) {
            return;
        }
        String thisMonth = today.getYear() + "-" + today.getMonthValue();
        if (thisMonth.equals(lastBilledMonth)) {
            return;
        }
        lastBilledMonth = thisMonth;
        billAll();
    }

    /** {@code /rail admin bill-now} からも呼べるよう、手動実行の入口を分けてある。 */
    public void billAll() {
        int nations = 0;
        for (String nationId : db.nationsWithRails()) {
            long total = 0;
            for (var rail : db.railsByNation(nationId)) {
                total += MonthlyBilling.maintenanceCost(rail.type(), rail.outsideTerritory());
            }
            if (total == 0) {
                continue;
            }
            nations++;
            var suzerain = db.suzerainOf(nationId);
            if (suzerain.isPresent()) {
                var billing = MonthlyBilling.billVassal(db.balances(suzerain.get()),
                        db.balances(nationId), total);
                db.saveBalances(suzerain.get(), billing.suzerainPayment().after());
                db.saveBalances(nationId, billing.vassalPayment().after());
                logger.info("[rail] 属国 " + nationId + " の維持費 " + total + "（宗主国 "
                        + suzerain.get() + " 負担 " + billing.suzerainPayment().fromTreasury()
                        + "・自国負担 " + billing.vassalPayment().fromTreasury() + "）");
            } else {
                var payment = MonthlyBilling.billIndependent(db.balances(nationId), total);
                db.saveBalances(nationId, payment.after());
                logger.info("[rail] " + nationId + " の維持費 " + total + "（引き落とし "
                        + payment.fromTreasury()
                        + (payment.fulfilled() ? "" : "、不足 " + payment.unpaid()) + "）");
            }
        }
        db.resetAllMonthlyCounts(LocalDate.now().toString());
        logger.info("[rail] 月末維持費請求が完了（" + nations + " 国）。"
                + "全国家の当月設置カウントをリセットした");
    }
}
