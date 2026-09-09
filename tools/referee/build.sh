#!/usr/bin/env bash
# Сборка официального рефери Code Royale (csj/code-royale, Kotlin) и раннера матчей.
# Нужны JDK 17+, Maven и сеть до github.com и Maven Central (первый запуск). Результат — build/referee/.
# Правки рефери: Kotlin 1.2 -> 1.9 (компилятор 1.2 не работает на новых JDK), minBy -> minByOrNull
# (в Kotlin 1.7+ minBy бросает исключение на пустом списке, старая семантика — null), 250 ходов и физика арены (см. ниже).
set -euo pipefail
ROOT=$(cd "$(dirname "$0")/../.." && pwd)
BUILD=$ROOT/build/referee
REF=$BUILD/code-royale
mkdir -p "$BUILD"

if [ ! -d "$REF" ]; then
  git clone -q --depth 1 https://github.com/csj/code-royale "$REF"
  sed -i 's|<kotlin.version>1.2.10</kotlin.version>|<kotlin.version>1.9.24</kotlin.version>|; s|kotlin-stdlib-jre8|kotlin-stdlib-jdk8|; /<experimentalCoroutines>/d' "$REF/pom.xml"
  grep -rl 'minBy\|maxBy' "$REF/src" | xargs sed -i 's/\.minBy {/.minByOrNull {/g; s/\.maxBy {/.maxByOrNull {/g'
  # лимит хода из -Dturn.max.time (локальные матчи; по умолчанию 50 мс, как на CodinGame);
  # число ходов из -Dturn.max (по умолчанию 250 — столько играет арена, в исходниках на GitHub 200)
  sed -i 's|    theGameManager.maxTurns = 200|    theGameManager.maxTurns = (System.getProperty("turn.max") ?: "250").toInt()\n    System.getProperty("turn.max.time")?.let { theGameManager.turnMaxTime = it.toInt() }|' "$REF/src/main/kotlin/com/codingame/game/Referee.kt"
  # физика арены CodinGame (восстановлена по реплеям, см. CLAUDE.md): рыцарь 25 HP, порядок тел в коллизиях как до PR #3,
  # спавн = towards(чужая королева, 30) от центра сайта + углы единичного квадрата, урон башни без вычитания радиуса сайта
  sed -i 's|KNIGHT(   4, 80,  100, 0,   20, 400,  30,  5, "Unite_Fantassin"),|KNIGHT(   4, 80,  100, 0,   20, 400,  25,  5, "Unite_Fantassin"), // arena: 25 hp|' "$REF/src/main/kotlin/com/codingame/game/Constants.kt"
  sed -i 's|  private fun allEntities(): List<FieldObject> = gameManager.players.flatMap { it.activeCreeps } + gameManager.players.map { it.queenUnit } + obstacles|  private fun allEntities(): List<FieldObject> = gameManager.players.flatMap { it.allUnits() } + obstacles // arena: pre-PR#3 order|' "$REF/src/main/kotlin/com/codingame/game/Referee.kt"
  python3 - "$REF/src/main/kotlin/com/codingame/game/Referee.kt" <<'PY'
import sys
p = sys.argv[1]; s = open(p).read()
old = """                    val c = if (barracks.owner.isSecondPlayer) -1 else 1
                    it.location = barracks.obstacle.location + Vector2(c * iter, c * iter)
                    //it.location = barracks.obstacle.location + Vector2(iter, iter) // Fix units start point outside barracks
                    it.finalizeFrame()
                    it.location = it.location.towards(barracks.owner.enemyPlayer.queenUnit.location, 30.0)
                    it.finalizeFrame()"""
new = """                    // arena: towards(enemy queen, 30) from the site centre, then unit-square corners (+1,-1), (-1,+1), (+1,+1), (-1,-1)
                    val dxs = intArrayOf(1, -1, 1, -1); val dys = intArrayOf(-1, 1, 1, -1)
                    it.location = barracks.obstacle.location
                    it.finalizeFrame()
                    it.location = it.location.towards(barracks.owner.enemyPlayer.queenUnit.location, 30.0) + Vector2(dxs[iter % 4], dys[iter % 4])
                    it.finalizeFrame()"""
assert old in s, "spawn block"
open(p, "w").write(s.replace(old, new))
PY
  sed -i 's|val shotDistance = target.location.distanceTo(obstacle.location).toDouble - obstacle.radius|val shotDistance = target.location.distanceTo(obstacle.location).toDouble // arena: no obstacle.radius subtraction|' "$REF/src/main/kotlin/com/codingame/game/Structures.kt"
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
