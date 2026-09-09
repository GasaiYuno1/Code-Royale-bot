#!/usr/bin/env python3
"""HP/золото обоих игроков по ходам из реплея арены (тексты HUD просмотрщика) + ответы игроков.

python3 tools/arena/hud.py <game.json> [шаг=10]
Колонки: ход, hp0, gold0, hp1, gold1, действия игрока 0 | игрока 1.
"""
import json
import re
import sys

game = json.load(open(sys.argv[1], encoding="utf-8"))
step = int(sys.argv[2]) if len(sys.argv) > 2 else 10
frames = game["frames"]
pos, text = {}, {}
out = {}
hud = {}
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
            px = re.search(r"\bx (-?\d+)", props); py = re.search(r"\by (-?\d+)", props)
            if px or py:
                x, y = pos.get(eid, (None, None))
                if px: x = int(px.group(1))
                if py: y = int(py.group(1))
                pos[eid] = (x, y)
            mt = re.search(r"\bT (\S+)", props)
            if mt: text[eid] = mt.group(1)
    if i > 0:
        t = (i - 1) // 2
        a = f.get("agentId")
        so = (f.get("stdout") or "").strip().replace("\n", " ; ")
        if a is not None and a >= 0:
            out.setdefault(t, {})[a] = so
        vals = {}
        for eid, tx in text.items():
            x = pos.get(eid, (None, None))[0]
            if x is None: continue
            vals[x] = tx
        hud[t] = vals
xs = sorted({x for v in hud.values() for x in v})
print("# hud x columns:", xs, file=sys.stderr)
def col(vals, x):
    return vals.get(x, "?")
for t in sorted(hud):
    if t % step and t != max(hud): continue
    vals = hud[t]
    o = out.get(t, {})
    print(t, col(vals, 553), col(vals, 700), col(vals, 1771), col(vals, 1020), "|", o.get(0, ""), "|", o.get(1, ""))
