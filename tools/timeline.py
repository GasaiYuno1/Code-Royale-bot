#!/usr/bin/env python3
"""Таймлайн партии по логу сверки (match.sh -log): экономика, башни, HP, действия каждого игрока раз в N ходов.

python3 tools/timeline.py build/logs/diag/game-6.log [шаг=10] [игрок=0|1|both]
"""
import re
import sys

path = sys.argv[1]
step = int(sys.argv[2]) if len(sys.argv) > 2 else 10
who = sys.argv[3] if len(sys.argv) > 3 else "both"

frames = {}
cur = None
header = ""
for line in open(path, encoding="utf-8"):
    line = line.rstrip("\n")
    if line.startswith("# game "):
        header = line
        continue
    m = re.match(r"# frame (\d+) player (\d+)", line)
    if m:
        f = int(m.group(1)); p = int(m.group(2))
        cur = {"t": f // 2, "p": p, "lines": [], "out": []}
        frames[(f // 2, p)] = cur
        continue
    if line.startswith("#"):
        continue
    if line.startswith("> "):
        if cur: cur["out"].append(line[2:])
        continue
    if cur: cur["lines"].append(line)

num_sites = None
for (t, p), fr in sorted(frames.items()):
    if num_sites is None:
        # число сайтов = число строк с 7 числами подряд после первой
        n = 0
        for l in fr["lines"][1:]:
            if len(l.split()) == 7: n += 1
            else: break
        num_sites = n
    break

def parse(fr):
    L = fr["lines"]
    gold, touched = map(int, L[0].split())
    sites = [list(map(int, l.split())) for l in L[1:1 + num_sites]]
    m = int(L[1 + num_sites])
    units = [list(map(int, l.split())) for l in L[2 + num_sites:2 + num_sites + m]]
    return gold, sites, units

print(header)
for p in (0, 1):
    if who != "both" and int(who) != p: continue
    print(f"=== player {p}")
    t = 0
    while (t, p) in frames:
        fr = frames[(t, p)]
        gold, sites, units = parse(fr)
        own = [s for s in sites if s[4] == 0]
        mines = [s for s in own if s[3] == 0]; towers = [s for s in own if s[3] == 1]; bar = [s for s in own if s[3] == 2]
        etowers = [s for s in sites if s[4] == 1 and s[3] == 1]
        income = sum(s[5] for s in mines if s[5] > 0)
        q = [u for u in units if u[3] == -1 and u[2] == 0][0]
        eq = [u for u in units if u[3] == -1 and u[2] == 1][0]
        my = [u for u in units if u[2] == 0 and u[3] >= 0]; en = [u for u in units if u[2] == 1 and u[3] >= 0]
        near = len([u for u in en if u[3] == 0 and ((u[0] - q[0]) ** 2 + (u[1] - q[1]) ** 2) ** 0.5 < 400])
        print(f"t{t:3d} gold {gold:4d} inc {income:2d} mines {len(mines)} towers {len(towers)}/{sum(s[5] for s in towers):4d} etowers {len(etowers)} bar {len(bar)} | q ({q[0]:4d},{q[1]:4d}) hp {q[4]:3d} ehp {eq[4]:3d} creeps {len(my)}/{len(en)} knights@q {near} | {' ; '.join(fr['out'])}")
        t += step
