#!/bin/sh
# 骨格を正射投影した略図を SVG で出力する。Minecraft は不要。
# 使い方: ./sketch-knight.sh > knight.svg
set -e
cd "$(dirname "$0")"
[ -d out ] || javac -encoding UTF-8 -d out $(find src -name '*.java')
exec java -Dstdout.encoding=UTF-8 -cp out jp.mcserver.core.raid.RigSketch
