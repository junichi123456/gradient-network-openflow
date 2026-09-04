#!/bin/sh
# コア層の検証。Bukkit API に依存しないため、サーバーを起動せずに実行できる。
set -e
cd "$(dirname "$0")"
rm -rf out
javac -encoding UTF-8 -d out $(find src -name '*.java')
exec java -Dstdout.encoding=UTF-8 -cp out jp.mcserver.core.CoreTests
