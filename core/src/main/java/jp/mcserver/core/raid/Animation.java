package jp.mcserver.core.raid;

import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;

/**
 * 攻撃モーション（§12.6）。部位ごとのキーフレーム列を持つ。
 *
 * <p>クライアントは変換の更新のあいだを補間する。したがってサーバーが送るのは
 * <b>更新間隔ごとにサンプリングした変換</b>であり、キーフレームそのものではない。
 */
public final class Animation {

    /**
     * キーフレーム。
     *
     * @param tick      モーション開始からの経過tick
     * @param transform その時点の変換
     */
    public record Keyframe(int tick, Transform transform) {

        public Keyframe {
            if (tick < 0) {
                throw new IllegalArgumentException("tickが負である: " + tick);
            }
        }
    }

    private final String name;
    private final int durationTicks;
    private final boolean loop;
    private final Map<String, List<Keyframe>> tracks = new LinkedHashMap<>();

    public Animation(String name, int durationTicks, boolean loop,
                     Map<String, List<Keyframe>> tracks) {
        if (durationTicks <= 0) {
            throw new IllegalArgumentException("長さが0以下である: " + durationTicks);
        }
        if (tracks.isEmpty()) {
            throw new IllegalArgumentException("動かす部位が1つもない");
        }
        this.name = name;
        this.durationTicks = durationTicks;
        this.loop = loop;
        tracks.forEach((part, keys) -> {
            if (keys.isEmpty()) {
                throw new IllegalArgumentException("キーフレームがない: " + part);
            }
            List<Keyframe> sorted = new ArrayList<>(keys);
            for (int i = 1; i < sorted.size(); i++) {
                if (sorted.get(i).tick() <= sorted.get(i - 1).tick()) {
                    throw new IllegalArgumentException("キーフレームが昇順でない: " + part);
                }
            }
            if (sorted.get(0).tick() != 0) {
                throw new IllegalArgumentException("最初のキーフレームは tick 0 である必要がある: " + part);
            }
            if (sorted.get(sorted.size() - 1).tick() > durationTicks) {
                throw new IllegalArgumentException("キーフレームが長さを超えている: " + part);
            }
            this.tracks.put(part, List.copyOf(sorted));
        });
    }

    public String name() {
        return name;
    }

    public int durationTicks() {
        return durationTicks;
    }

    public boolean loop() {
        return loop;
    }

    public java.util.Set<String> animatedParts() {
        return tracks.keySet();
    }

    /** 骨格と整合するか（存在しない部位を動かしていないか）を検証する。 */
    public void validateAgainst(Rig rig) {
        for (String part : tracks.keySet()) {
            rig.part(part);
        }
    }

    /**
     * 指定tickの変換をサンプリングする。キーフレームの間は線形補間する。
     *
     * @param tick 経過tick。ループするモーションでは長さで折り返す
     */
    public Transform sample(String part, int tick) {
        List<Keyframe> keys = tracks.get(part);
        if (keys == null) {
            throw new IllegalArgumentException("このモーションで動かない部位である: " + part);
        }
        int t = loop ? Math.floorMod(tick, durationTicks) : Math.min(Math.max(tick, 0), durationTicks);

        Keyframe previous = keys.get(0);
        for (Keyframe key : keys) {
            if (key.tick() == t) {
                return key.transform();
            }
            if (key.tick() > t) {
                int span = key.tick() - previous.tick();
                double ratio = span == 0 ? 0 : (double) (t - previous.tick()) / span;
                return previous.transform().lerp(key.transform(), ratio);
            }
            previous = key;
        }
        return previous.transform();
    }

    /** 更新間隔ごとに送るサンプル数。 */
    public int sampleCount(int updateIntervalTicks) {
        if (updateIntervalTicks <= 0) {
            throw new IllegalArgumentException("更新間隔が0以下である");
        }
        return durationTicks / updateIntervalTicks + 1;
    }
}
