#!/usr/bin/env python3
"""Позиции королев и крипов по кадрам из данных просмотрщика реплея арены (SDK 1.x entitymodule).

python3 tools/arena/viewpos.py <game.json> [<лог локального воспроизведения>]
Печатает по ходам позиции королев (и число крипов) из view и, если дан лог, первое расхождение с ним.
Координаты во view усечены (toInt), в логе округлены — расхождение ≤ 1 нормально.
"""
import json
import re
import sys

game = json.load(open(sys.argv[1], encoding="utf-8"))
frames = game["frames"]
kinds = []          # id -> тип создания (S, G, ...)
img = {}            # id -> image
pos = {}            # id -> (x, y)
per_turn = {}       # turn -> snapshot {id: (x,y)}

for i, f in enumerate(frames):
    v = f.get("view") or ""
    if "{" in v:
        d = json.loads(v[v.index("{"):])
        ents = d["frame"]["entitymodule"] if "frame" in d else d["entitymodule"]
        for e in ents:
            if e.startswith("C "):
                kinds.append(e[2:].strip())
                continue
            m = re.match(r"U (\d+) [\d.]+ (.*)", e)
            if not m:
                continue
            eid = int(m.group(1)); props = m.group(2)
            mi = re.search(r"\bi (\S+)", props)
            if mi: img[eid] = mi.group(1)
            px = re.search(r"\bx (-?\d+)", props); py = re.search(r"\by (-?\d+)", props)
            if px or py:
                x, y = pos.get(eid, (None, None))
                if px: x = int(px.group(1))
                if py: y = int(py.group(1))
                pos[eid] = (x, y)
    if i > 0 and i % 2 == 0:
        per_turn[(i - 1) // 2] = dict(pos)

# группы юнитов: спрайт с образом Unite_* -> группа = id + 1 (tokenCircle, characterSprite, group)
units = {}
for eid, im in img.items():
    if im in ("Unite_Reine", "Unite_Fantassin", "Unite_Archer", "Unite_Siege"):
        owner = 1 if img.get(eid - 1, "").endswith("Bleu") else 0
        units[eid + 1] = (im, owner)
queens = sorted([g for g, (im, o) in units.items() if im == "Unite_Reine"], key=lambda g: units[g][1])
print("queen groups:", queens, "units:", len(units))

local = None
if len(sys.argv) > 2:
    local = {}
    cur = None
    for line in open(sys.argv[2], encoding="utf-8"):
        m = re.match(r"# frame (\d+) player (\d+)", line)
        if m: cur = (int(m.group(1)) // 2, int(m.group(2))); local[cur] = []; continue
        if cur and line.strip() and not line.startswith("#") and not line.startswith("> "): local[cur].append(line.strip())

def local_state(t):
    fr = local.get((t, 0))
    if not fr: return None
    n = int(fr[0].split()[1]) if False else None
    # число сайтов: строки из 7 чисел после первой
    k = 1
    while k < len(fr) and len(fr[k].split()) == 7: k += 1
    m = int(fr[k]); us = [list(map(int, l.split())) for l in fr[k + 1:k + 1 + m]]
    q = {u[2]: (u[0], u[1]) for u in us if u[3] == -1}
    creeps = [(u[0], u[1], u[2], u[3]) for u in us if u[3] >= 0]
    return q, creeps

first = None
for t in sorted(per_turn):
    snap = per_turn[t]
    qv = [snap.get(g, (None, None)) for g in queens]
    alive = [(g, units[g]) for g in units if g in snap and units[g][0] != "Unite_Reine"]
    line = f"t{t:3d} view queens {qv} creeps {len(alive)}"
    if local is not None:
        ls = local_state(t + 1)
        if ls:
            lq, lc = ls
            lqv = [lq.get(0), lq.get(1)]
            dq = max(abs(a[0] - b[0]) + abs(a[1] - b[1]) for a, b in zip(qv, lqv) if a[0] is not None and b)
            line += f" | local queens {lqv} creeps {len(lc)} dq {dq}"
            if (dq > 1 or len(lc) != len(alive)) and first is None:
                first = t
                line += "   <== first divergence"
    if t % 5 == 0 or first == t:
        print(line)
    if first is not None and t > first + 3:
        break
