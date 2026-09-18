#!/bin/sh
# リソースパックの雛形を骨格データから生成する（raid_model_spec.md）。
# 骨格を変えたら実行し直す。手で書いたテクスチャの指定は上書きされるので注意。
set -e
cd "$(dirname "$0")"
rm -rf out
javac -encoding UTF-8 -d out $(find src -name '*.java')
exec java -Dstdout.encoding=UTF-8 -cp out jp.mcserver.core.raid.ModelPack "$@"
