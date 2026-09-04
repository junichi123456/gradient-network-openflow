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
     * キーフレーム間の速度の付け方（§12.6）。
     *
     * <p>線形のまま繋ぐと、キーフレームの境目で速度が急に変わり、
     * 折れ線を辿るような動きに見える。緩急を付けることで、
     * 同じキーフレームでも「重さのある動き」になる。
     */
    public enum Easing {
        /** 等速。速度を一定に保ちたい区間に使う。 */
        LINEAR {
            @Override
            public double apply(double ratio) {
                return ratio;
            }
        },
        /** 遅く始まり速く終わる。振りかぶりから打撃へ加速する区間に使う。 */
        EASE_IN {
            @Override
            public double apply(double ratio) {
                return ratio * ratio;
            }
        },
        /** 速く始まり遅く終わる。素早く構えて止まる区間に使う。 */
        EASE_OUT {
            @Override
            public double apply(double ratio) {
                return 1 - (1 - ratio) * (1 - ratio);
            }
        },
        /** 両端が緩やか。既定。つなぎ目で速度が飛ばない。 */
        EASE_IN_OUT {
            @Override
            public double apply(double ratio) {
                return ratio * ratio * (3 - 2 * ratio);
            }
        };

        /**
         * 経過の割合を、進み具合の割合へ変換する。
         *
         * @param ratio 0〜1
         */
        public abstract double apply(double ratio);
    }

    /** 指定のないキーフレームに使う緩急。 */
    public static final Easing DEFAULT_EASING = Easing.EASE_IN_OUT;

    /**
     * キーフレーム。
     *
     * @param tick      モーション開始からの経過tick
     * @param transform その時点の変換
     * @param easing    <b>1つ前のキーフレームからこのキーフレームへ</b>向かうときの緩急
     */
    public record Keyframe(int tick, Transform transform, Easing easing) {

        public Keyframe {
            if (tick < 0) {
                throw new IllegalArgumentException("tickが負である: " + tick);
            }
            if (easing == null) {
                throw new IllegalArgumentException("緩急が null である");
            }
        }

        /** 既定の緩急（両端が緩やか）を用いるキーフレーム。 */
        public Keyframe(int tick, Transform transform) {
            this(tick, transform, DEFAULT_EASING);
        }

        /** 緩急を差し替えた同じキーフレーム。 */
        public Keyframe with(Easing value) {
            return new Keyframe(tick, transform, value);
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
                return previous.transform().lerp(key.transform(), key.easing().apply(ratio));
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
