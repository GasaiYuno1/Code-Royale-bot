#!/usr/bin/env python3
"""Ввод игрока из лога сверки (match.sh -log) для подачи боту: стартовые строки + ходы 0..N.

python3 tools/extract_turns.py <лог> <игрок 0|1> <до хода N> > build/turns.txt
dotnet src/RoyaleBot/bin/Release/net8.0/RoyaleBot.dll league=4 debug=1 < build/turns.txt 2>&1 | grep -A30 "root: .* turn N "
"""
import re
import sys

path, player, upto = sys.argv[1], int(sys.argv[2]), int(sys.argv[3])
init, frames, cur, mode = [], {}, None, None
for line in open(path, encoding="utf-8"):
    line = line.rstrip("\n")
    if line.startswith("# init player "):
        mode = "init" if int(line[14:]) == player else None
        continue
    m = re.match(r"# frame (\d+) player (\d+)", line)
    if m:
        cur = (int(m.group(1)) // 2, int(m.group(2)))
        mode = "frame" if cur[1] == player else None
        frames.setdefault(cur, [])
        continue
    if line.startswith("#") or line.startswith("> ") or not line:
        continue
    if mode == "init":
        init.append(line)
    elif mode == "frame":
        frames[cur].append(line)
out = init[:]
for t in range(upto + 1):
    out += frames.get((t, player), [])
print("\n".join(out))
