package jp.mcserver.plugin;

/**
 * 特殊系統（浮遊する剣）の1本。{@link SpecialTrack} が生成・保持・破棄する。
 *
 * <p>移動の法則ごとに3クラスある。降り注ぐ刃は {@code RainBlade}、串刺しは {@code SpikeBlade}、
 * 空間斬撃と全域大旋回はどちらも追尾直進なので {@code HomingBlade} を共有する。
 * 駆動する側からは同じ形で扱えればよい。
 */
interface FloatingBlade {

    /** 1tick進める。戻り値が true なら、この呼び出しの後で破棄してよい（役目を終えた）。 */
    boolean tick();

    /** 表示実体を除去する。討伐・停止・寿命切れのいずれでも呼ぶ。 */
    void despawn();
}
