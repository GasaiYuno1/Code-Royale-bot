#!/usr/bin/env python3
"""Подготовка партии арены к сверке симулятора (tests -- arenasim): <id>.actions.txt (ход, игрок, действие королевы | TRAIN)
и <id>.hud.txt (ход, золото0, HP0, золото1, HP1 из HUD просмотрщика). Печатает seed рефери для replaybot/match.sh.

python3 tools/arena/prep.py <game.json> <outdir>
Затем: TURN_MS=200 tools/referee/match.sh 4 1 <seed> "python3 tools/arena/replaybot.py <game.json> 0" "python3 tools/arena/replaybot.py <game.json> 1" -noswap -log <dir>
"""
import json
import re
import sys
from pathlib import Path

game = json.load(open(sys.argv[1], encoding="utf-8"))
out = Path(sys.argv[2]); out.mkdir(parents=True, exist_ok=True)
gid = game["gameId"]
frames = game["frames"]
seed = re.search(r"seed=(-?\d+)", str(game.get("refereeInput")))
print("seed", seed.group(1) if seed else game.get("refereeInput"))

with open(out / f"{gid}.actions.txt", "w", encoding="utf-8") as f:
    for i, fr in enumerate(frames):
        a = fr.get("agentId")
        if a is None or a < 0:
            continue
        t = (i - 1) // 2
        lines = (fr.get("stdout") or "WAIT\nTRAIN\n").strip().split("\n")
        q = lines[0].strip() if lines else "WAIT"
        tr = lines[1].strip() if len(lines) > 1 else "TRAIN"
        f.write(f"{t} {a} {q} | {tr}\n")

pos, text, hud = {}, {}, {}
for i, fr in enumerate(frames):
    v = fr.get("view") or ""
    if "{" in v:
        d = json.loads(v[v.index("{"):])
        ents = d["frame"]["entitymodule"] if "frame" in d else d["entitymodule"]
        for e in ents:
            m = re.match(r"U (\d+) [\d.]+ (.*)", e)
            if not m:
                continue
            eid = int(m.group(1)); props = m.group(2)
            px = re.search(r"\bx (-?\d+)", props)
            if px: pos[eid] = int(px.group(1))
            mt = re.search(r"\bT (\S+)", props)
            if mt: text[eid] = mt.group(1)
    if i > 0 and i % 2 == 0:
        vals = {pos[eid]: tx for eid, tx in text.items() if eid in pos}
        hud[(i - 1) // 2] = vals
with open(out / f"{gid}.hud.txt", "w", encoding="utf-8") as f:
    for t in sorted(hud):
        v = hud[t]
        if all(k in v for k in (700, 553, 1020, 1771)):
            f.write(f"{t} {v[700]} {v[553]} {v[1020]} {v[1771]}\n")
print("ok ->", out / f"{gid}.actions.txt", out / f"{gid}.hud.txt")
