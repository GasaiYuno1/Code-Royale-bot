#!/usr/bin/env bash
# Снимок текущей сборки бота как эталон для матчей: build/bot-ref[-имя]/ (match.sh: ref, ref:key=value, ref-имя).
set -euo pipefail
ROOT=$(cd "$(dirname "$0")/.." && pwd)
NAME=${1:-}
DST=$ROOT/build/bot-ref${NAME:+-$NAME}
rm -rf "$DST" && mkdir -p "$DST"
cp -r "$ROOT/src/RoyaleBot/bin/Release/net8.0/." "$DST/"
echo "$(git -C "$ROOT" rev-parse --short HEAD) $(date -Is)" > "$DST/VERSION.txt"
echo "ok: $DST ($(cat "$DST/VERSION.txt"))"
