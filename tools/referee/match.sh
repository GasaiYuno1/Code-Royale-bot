#!/usr/bin/env bash
# Матчи настоящим рефери: tools/referee/match.sh <лига 1..4> <игр> <seed> <A> <B> [аргументы Match: -v -noswap -dump dir]
#   A, B: "bot" — собранный RoyaleBot (Release) с league=<лига>; "ref" — эталон build/bot-ref (tools/snapshot_ref.sh),
#         "ref-имя" — build/bot-ref-имя; "boss" — босс этой лиги из config рефери;
#         иначе произвольная командная строка. Лига: 1 = Wood 3, 2 = Wood 2, 3 = Wood 1, 4 = Bronze.
# Сначала: tools/referee/build.sh и dotnet build src/RoyaleBot -c Release.
set -euo pipefail
ROOT=$(cd "$(dirname "$0")/../.." && pwd)
BUILD=$ROOT/build/referee
LEAGUE=$1; GAMES=$2; SEED=$3; A=$4; B=$5; shift 5

resolve() {
  case "$1" in
    bot)  echo "dotnet $ROOT/src/RoyaleBot/bin/Release/net8.0/RoyaleBot.dll league=$LEAGUE" ;;
    bot:*) echo "dotnet $ROOT/src/RoyaleBot/bin/Release/net8.0/RoyaleBot.dll league=$LEAGUE ${1#bot:}" ;;
    ref)  echo "dotnet $ROOT/build/bot-ref/RoyaleBot.dll league=$LEAGUE" ;;
    ref:*) echo "dotnet $ROOT/build/bot-ref/RoyaleBot.dll league=$LEAGUE ${1#ref:}" ;;
    ref-*) echo "dotnet $ROOT/build/bot-${1}/RoyaleBot.dll league=$LEAGUE" ;;
    boss) echo "java -cp $BUILD/boss/level$LEAGUE Player" ;;
    *)    echo "$1" ;;
  esac
}

exec java --add-opens java.base/java.lang=ALL-UNNAMED --add-opens java.base/java.lang.reflect=ALL-UNNAMED --add-opens java.base/java.util=ALL-UNNAMED -Dleague.level="$LEAGUE" -cp "$(cat "$BUILD/classpath.txt")" com.codingame.gameengine.runner.Match \
  -games "$GAMES" -seed "$SEED" -p1 "$(resolve "$A")" -p2 "$(resolve "$B")" "$@" 2> >(grep -v "Picked up JAVA_TOOL_OPTIONS" >&2)
