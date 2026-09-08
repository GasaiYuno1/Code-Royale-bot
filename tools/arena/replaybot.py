#!/usr/bin/env python3
"""Бот-магнитофон: печатает записанные в реплее арены ответы игрока index, синхронизируясь по вводу.
Вместе с настоящим рефери и тем же seed воспроизводит партию арены полностью (для -log и сверки).

tools/referee/match.sh 4 1 <seed> "python3 tools/arena/replaybot.py <game.json> 0" "python3 tools/arena/replaybot.py <game.json> 1" -noswap -log dir
"""
import json
import sys

game = json.load(open(sys.argv[1], encoding="utf-8"))
index = int(sys.argv[2])
outputs = [f.get("stdout") or "WAIT\nTRAIN\n" for f in game["frames"] if f.get("agentId") == index]


def read_line():
    line = sys.stdin.readline()
    if not line:
        sys.exit(0)
    return line


num_sites = int(read_line())
for _ in range(num_sites):
    read_line()
turn = 0
while True:
    read_line()                      # gold touched
    for _ in range(num_sites):
        read_line()
    units = int(read_line())
    for _ in range(units):
        read_line()
    out = outputs[turn] if turn < len(outputs) else "WAIT\nTRAIN\n"
    lines = [l.strip() for l in out.strip().split("\n")]
    if len(lines) < 2:
        lines = (lines + ["WAIT", "TRAIN"])[:2]
    sys.stdout.write(lines[0] + "\n" + lines[1] + "\n")
    sys.stdout.flush()
    turn += 1
