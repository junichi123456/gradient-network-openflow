#!/bin/sh
# 表示へ送るべき配置を書き出す。実機の /raid dump と突き合わせる（§12.6）。
#   ./dump-rig.sh            第一形態・待機・tick 0
#   ./dump-rig.sh 1 20 3段突き
set -e
cd "$(dirname "$0")"
[ -d out ] || javac -encoding UTF-8 -d out $(find src -name '*.java')
exec java -Dstdout.encoding=UTF-8 -cp out jp.mcserver.core.raid.RigDump "$@"
