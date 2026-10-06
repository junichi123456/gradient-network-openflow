package jp.mcserver.plugin;

/**
 * 特殊系統（浮遊する剣）の1本。{@link SpecialTrack} が生成・保持・破棄する。
 *
 * <p>4種（{@code RainBlade}・{@code SlashBlade}・{@code SpikeBlade}・{@code WhirlBlade}）は
 * 移動の法則がそれぞれ異なるため別クラスに分けるが、駆動する側からは同じ形で扱えればよい。
 */
interface FloatingBlade {

    /** 1tick進める。戻り値が true なら、この呼び出しの後で破棄してよい（役目を終えた）。 */
    boolean tick();

    /** 表示実体を除去する。討伐・停止・寿命切れのいずれでも呼ぶ。 */
    void despawn();
}
