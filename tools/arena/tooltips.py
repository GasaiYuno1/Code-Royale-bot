#!/usr/bin/env python3
"""HP крипов и башен по ходам из подсказок (tooltips) реплея арены — точная сверка урона с симулятором.

python3 tools/arena/tooltips.py <game.json> [от_хода] [до_хода]
Печатает по ходам: крипы (id сущности: HP) и башни (id сайта: HP, радиус). Id сущности крипа = группа − 2
(группа = спрайт Unite_* + 1); ключ сайта в подсказках — его порядковый id + 1? (см. вывод «site keys»).
"""
import json
import re
import sys

game = json.load(open(sys.argv[1], encoding="utf-8"))
t0 = int(sys.argv[2]) if len(sys.argv) > 2 else 0
t1 = int(sys.argv[3]) if len(sys.argv) > 3 else 40
frames = game["frames"]
img = {}
for i, f in enumerate(frames):
    v = f.get("view") or ""
    if "{" not in v:
        continue
    d = json.loads(v[v.index("{"):])
    frame = d["frame"] if "frame" in d else d
    for e in frame.get("entitymodule", []):
        m = re.match(r"U (\d+) [\d.]+ (.*)", e)
        if m:
            mi = re.search(r"\bi (\S+)", m.group(2))
            if mi: img[int(m.group(1))] = mi.group(1)
    tips = frame.get("tooltips") or []
    t = (i - 1) // 2
    if i % 2 == 0 and t0 <= t <= t1:
        merged = {}
        for tp in tips:
            if isinstance(tp, dict): merged.update(tp)
        creeps, towers, others = [], [], []
        for k, lines in sorted(merged.items(), key=lambda kv: int(kv[0])):
            if isinstance(lines, dict):
                if not all(str(kk).isdigit() for kk in lines):
                    continue
                lines = [lines[kk] for kk in sorted(lines, key=lambda x: int(x))]
            lines = [str(x) for x in lines]
            text = " | ".join(lines)
            if "TOWER" in text:
                hp = re.search(r"Health: (\d+)", text); rg = re.search(r"Range: (\d+)", text)
                towers.append(f"site#{k}:hp{hp.group(1) if hp else '?'} r{rg.group(1) if rg else '?'}")
            elif lines and lines[0].startswith("Health:"):
                creeps.append(f"{k}:{lines[0].split()[1]}")
            elif "MINE" in text or "BARRACKS" in text:
                others.append(f"site#{k}:{text[:40]}")
        print(f"t{t:3d} creeps [{' '.join(creeps)}] towers [{' '.join(towers)}]")
