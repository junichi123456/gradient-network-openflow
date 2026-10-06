package jp.mcserver.core.racing;

/**
 * 特性の階層（`minecraft_server_spec.md` §27.7.2）。
 *
 * <p>今回実装するのは通常特性・上位特性の2階層のみ。固有特性・レジェンド特性・
 * 絆特性は、固定の実写馬ロスターや騎手システムを前提とするため今回は未実装
 * とする（§23）。
 */
public enum TraitTier {
    NORMAL,
    UPPER
}
