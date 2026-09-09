#!/usr/bin/env python3
"""Крипы по ходам: позиции из просмотрщика реплея арены против дампа симулятора (tests -- arenasim ... dump=file).

python3 tools/arena/poscmp.py <game.json> <dump.txt> [от_хода] [до_хода]
Для каждого хода печатает крипов арены и симулятора (владелец, x, y) и сдвиг каждого крипа арены до ближайшего в симуляторе.
"""
import json
import math
import re
import sys

game = json.load(open(sys.argv[1], encoding="utf-8"))
dump = sys.argv[2]
t0 = int(sys.argv[3]) if len(sys.argv) > 3 else 0
t1 = int(sys.argv[4]) if len(sys.argv) > 4 else 30

frames = game["frames"]
img, pos, per_turn = {}, {}, {}
for i, f in enumerate(frames):
    v = f.get("view") or ""
    if "{" in v:
        d = json.loads(v[v.index("{"):])
        ents = d["frame"]["entitymodule"] if "frame" in d else d["entitymodule"]
        for e in ents:
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
units = {}
queens = {}
for eid, im in img.items():
    if im in ("Unite_Fantassin", "Unite_Archer", "Unite_Siege"):
        owner = 1 if img.get(eid - 1, "").endswith("Bleu") else 0
        units[eid + 1] = (im, owner)
    if im == "Unite_Reine":
        owner = 1 if img.get(eid - 1, "").endswith("Bleu") else 0
        queens[owner] = eid + 1

sim = {}
simq = {}
for line in open(dump, encoding="utf-8"):
    t, p, ty, x, y = line.split()[:5]
    if ty == "T": continue
    if int(ty) >= 0:
        sim.setdefault(int(t), []).append((int(p), int(x), int(y)))
    else:
        simq.setdefault(int(t), {})[int(p)] = (int(x), int(y))

prev = {}
for t in range(t0, t1 + 1):
    snap = per_turn.get(t, {})
    # крип показан в кадре, если его группа обновилась в этом ходу (иначе это иконка казармы или мёртвый)
    arena = []
    for g, (im, o) in units.items():
        if g in snap and snap[g] != prev.get(g) and snap[g][0] is not None:
            arena.append((o, snap[g][0], snap[g][1]))
    prev = snap
    s = sim.get(t, [])
    if not arena and not s:
        continue
    arena.sort(); s.sort()
    diffs = []
    for (o, x, y) in arena:
        best = min((math.hypot(x - sx, y - sy) for (sp, sx, sy) in s if sp == o), default=None)
        diffs.append(None if best is None else round(best))
    qa = [snap.get(queens.get(o), (None, None)) for o in (0, 1)]
    qs = [simq.get(t, {}).get(o) for o in (0, 1)]
    print(f"t{t:3d} arena {arena}  queens {qa}")
    print(f"      sim   {s}  diff {diffs}  queens {qs}")
