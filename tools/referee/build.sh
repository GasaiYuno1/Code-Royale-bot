#!/usr/bin/env bash
# Сборка официального рефери Code Royale (csj/code-royale, Kotlin) и раннера матчей.
# Нужны JDK 17+, Maven и сеть до github.com и Maven Central (первый запуск). Результат — build/referee/.
# Правки рефери: Kotlin 1.2 -> 1.9 (компилятор 1.2 не работает на новых JDK), minBy -> minByOrNull
# (в Kotlin 1.7+ minBy бросает исключение на пустом списке, старая семантика — null). Логика игры не меняется.
set -euo pipefail
ROOT=$(cd "$(dirname "$0")/../.." && pwd)
BUILD=$ROOT/build/referee
REF=$BUILD/code-royale
mkdir -p "$BUILD"

if [ ! -d "$REF" ]; then
  git clone -q --depth 1 https://github.com/csj/code-royale "$REF"
  sed -i 's|<kotlin.version>1.2.10</kotlin.version>|<kotlin.version>1.9.24</kotlin.version>|; s|kotlin-stdlib-jre8|kotlin-stdlib-jdk8|; /<experimentalCoroutines>/d' "$REF/pom.xml"
  grep -rl 'minBy\|maxBy' "$REF/src" | xargs sed -i 's/\.minBy {/.minByOrNull {/g; s/\.maxBy {/.maxByOrNull {/g'
  # лимит хода из -Dturn.max.time (локальные матчи; по умолчанию 50 мс, как на CodinGame)
  sed -i 's|    theGameManager.maxTurns = 200|    theGameManager.maxTurns = 200\n    System.getProperty("turn.max.time")?.let { theGameManager.turnMaxTime = it.toInt() }|' "$REF/src/main/kotlin/com/codingame/game/Referee.kt"
fi

(cd "$REF" && mvn -q -B -DskipTests package && mvn -q -B dependency:build-classpath -Dmdep.outputFile="$BUILD/cp.txt")

for l in 1 2 3 4; do
  mkdir -p "$BUILD/boss/level$l"
  javac -nowarn -d "$BUILD/boss/level$l" "$REF/config/level$l/Boss.java" 2>&1 | grep -v "Picked up" || true
done

CP="$REF/target/code-royale-1.0-SNAPSHOT.jar:$(cat "$BUILD/cp.txt")"
mkdir -p "$BUILD/runner"
javac -nowarn -cp "$CP" -d "$BUILD/runner" "$ROOT/tools/referee/Match.java" 2>&1 | grep -v "Picked up" || true
echo "$CP:$BUILD/runner" > "$BUILD/classpath.txt"
echo "ok: $BUILD (jar, cp.txt, boss/level1..4, runner)"
