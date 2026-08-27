#!/bin/sh
# 騎士型の戦闘をオフラインで再現する。Minecraft は不要。
# 使い方: ./simulate-knight.sh [参加人数] [1人あたりDPS] [被ダメ軽減%]
set -e
cd "$(dirname "$0")"
[ -d out ] || javac -encoding UTF-8 -d out $(find src -name '*.java')
exec java -Dstdout.encoding=UTF-8 -cp out jp.mcserver.core.raid.KnightSimulation "$@"
