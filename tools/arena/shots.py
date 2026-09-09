#!/usr/bin/env python3
"""Выстрелы башен в реплее арены: кого башня ударила (HP из подсказок упал больше, чем на 1 старения за ход) и геометрия,
чтобы проверить правило выбора цели. Сайты — из локального лога воспроизведения (та же карта).

python3 tools/arena/shots.py <game.json> <лог> [--all]
Печатает по каждому однобашенному ходу: кандидаты в радиусе с дистанциями до башни и до королев, HP, порядок; кто ударен.
В конце — счёт совпадений для правил: ближайший к башне / к своей королеве / к чужой королеве, мин. HP, макс. HP, первый, последний.
"""
import json
import math
import re
import sys

game = json.load(open(sys.argv[1], encoding="utf-8"))
log = sys.argv[2]
show_all = "--all" in sys.argv
use_prev = "--prevpos" in sys.argv
frames = game["frames"]

sites = {}
mode = None
for line in open(log, encoding="utf-8"):
    line = line.rstrip("\n")
    if line.startswith("# init player 0"): mode = "init"; continue
    if line.startswith("#"): mode = None if mode == "init" else mode; 
    if mode == "init" and line and not line.startswith("#"):
        t = line.split()
        if len(t) == 4: sites[int(t[0])] = (int(t[1]), int(t[2]), int(t[3]))
    if mode is None and sites: break

img, pos = {}, {}
turns = {}   # t -> dict(creeps: {key: (owner, hp, x, y, order)}, towers: {site: (owner, hp, r)}, queens: {p: (hp, x, y)})
order_seen = []
for i, f in enumerate(frames):
    v = f.get("view") or ""
    if "{" not in v:
        continue
    d = json.loads(v[v.index("{"):]); frame = d["frame"] if "frame" in d else d
    for e in frame.get("entitymodule", []):
        m = re.match(r"U (\d+) [\d.]+ (.*)", e)
        if not m: continue
        eid = int(m.group(1)); props = m.group(2)
        mi = re.search(r"\bi (\S+)", props)
        if mi: img[eid] = mi.group(1)
        px = re.search(r"\bx (-?\d+)", props); py = re.search(r"\by (-?\d+)", props)
        if px or py:
            x, y = pos.get(eid, (None, None))
            if px: x = int(px.group(1))
            if py: y = int(py.group(1))
            pos[eid] = (x, y)
    if i % 2 != 0:
        continue
    t = (i - 1) // 2
    tips = frame.get("tooltips") or []
    merged = {}
    for tp in tips:
        if isinstance(tp, dict):
            for k, val in tp.items():
                if isinstance(val, list): merged[int(k)] = [str(x) for x in val]
    creeps, towers, queens = {}, {}, {}
    for k, lines in merged.items():
        text = " | ".join(lines)
        if "TOWER" in text:
            hp = int(re.search(r"Health: (\d+)", text).group(1)); rg = int(re.search(r"Range: (\d+)", text).group(1))
            sid = k - 3
            owner = None
            towers[sid] = [hp, rg]
        elif lines and lines[0].startswith("Health:"):
            hp = int(lines[0].split()[1])
            base = img.get(k, "")
            kind = img.get(k + 1, "")
            if base.startswith("Unite_Base") or kind == "Unite_Reine" or "Reine" in base:
                owner = 1 if base.endswith("Bleu") else 0
                grp = k + 2
                if "Reine" in kind or "Reine" in base:
                    queens[owner] = (hp, pos.get(grp, (None, None)))
                else:
                    if k not in order_seen: order_seen.append(k)
                    creeps[k] = (owner, hp, pos.get(grp, (None, None)), kind)
    turns[t] = (creeps, towers, queens)

# владелец башни: сайт принадлежит игроку, чья королева… неизвестно из подсказок; определим по тому, чьих крипов она бьёт (или по действиям)
# проще: башня стреляет по крипам противника; владельца выведем из actions (BUILD <site> TOWER игроком p)
owner_of = {}
for i, f in enumerate(frames):
    a = f.get("agentId")
    if a is None or a < 0: continue
    m = re.match(r"BUILD (\d+) TOWER", (f.get("stdout") or ""))
    if m: owner_of[int(m.group(1))] = a

rules = {"closest_tower": lambda c: c["dt"], "closest_own_queen": lambda c: c["dq"], "closest_enemy_queen": lambda c: c["deq"],
         "min_hp": lambda c: c["hp"], "max_hp": lambda c: -c["hp"], "first": lambda c: c["order"], "last": lambda c: -c["order"]}
score = {r: [0, 0] for r in rules}
dmgstat = {}
for t in sorted(turns):
    if t - 1 not in turns: continue
    prev_c, prev_t, prev_q = turns[t - 1]
    cur_c, cur_t, cur_q = turns[t]
    # кого ударили в ход t: HP упал больше чем на 1, или крип исчез при HP_prev > 1
    shot = {}
    for k, (o, hp, xy, kind) in prev_c.items():
        if k in cur_c:
            dmg = hp - cur_c[k][1] - 1
            if dmg > 0: shot[k] = dmg
        else:
            if hp - 1 > 0: shot[k] = hp - 1
    # башни владельца p, живые в конце t-1
    for sid, (hp, rg) in prev_t.items():
        p = owner_of.get(sid)
        if p is None or sid not in sites: continue
        sx, sy, sr = sites[sid]
        enemy = 1 - p
        # кандидаты: чужие крипы, живые в конце t-1, с позицией в конце t (после движения); если исчезли — позиция t-1
        cands = []
        for k, (o, hp0, xy0, kind) in prev_c.items():
            if o != enemy or hp0 <= 0: continue
            xy = xy0 if use_prev else (cur_c[k][2] if k in cur_c else xy0)
            if xy[0] is None: continue
            dt = math.hypot(xy[0] - sx, xy[1] - sy)
            if dt >= rg: continue
            q = cur_q.get(p, prev_q.get(p)); eq = cur_q.get(enemy, prev_q.get(enemy))
            dq = math.hypot(xy[0] - q[1][0], xy[1] - q[1][1]) if q and q[1][0] is not None else 9e9
            deq = math.hypot(xy[0] - eq[1][0], xy[1] - eq[1][1]) if eq and eq[1][0] is not None else 9e9
            cands.append({"k": k, "hp": hp0, "dt": dt, "dq": dq, "deq": deq, "order": order_seen.index(k), "shot": shot.get(k, 0), "kind": kind})
        if not cands: continue
        enemy_towers = [s for s, _ in prev_t.items() if owner_of.get(s) == p]
        single = len(enemy_towers) == 1
        hit = [c for c in cands if c["shot"] > 0]
        if single or show_all:
            print(f"t{t:3d} tower {sid} (p{p}) r{rg} cands:", " ".join(f"[{c['k']} hp{c['hp']} dt{c['dt']:.0f} dq{c['dq']:.0f} deq{c['deq']:.0f} o{c['order']}{' HIT-'+str(c['shot']) if c['shot'] else ''}]" for c in cands))
        if single and len(hit) == 1:
            h = hit[0]
            da = 3 + int((rg - (h["dt"] - sr)) / 200)   # GitHub: с вычитанием радиуса сайта
            db = 3 + int((rg - h["dt"]) / 200)          # без вычитания
            dmgstat.setdefault("a", [0, 0]); dmgstat.setdefault("b", [0, 0])
            dmgstat["a"][1] += 1; dmgstat["b"][1] += 1
            if da == h["shot"]: dmgstat["a"][0] += 1
            if db == h["shot"]: dmgstat["b"][0] += 1
            if da != h["shot"] or db != h["shot"]:
                print(f"   DMG t{t} tower {sid} r{rg} sr{sr}: hit {h['k']} dt{h['dt']:.1f} observed {h['shot']} formula_a {da} formula_b {db}")
            for r, key in rules.items():
                pred = min(cands, key=key)
                score[r][1] += 1
                if pred["k"] == hit[0]["k"]: score[r][0] += 1
            pred = min(cands, key=rules["closest_tower"])
            if pred["k"] != hit[0]["k"]:
                print(f"   MISMATCH t{t} tower {sid}: closest {pred['k']} dt{pred['dt']:.1f} hp{pred['hp']} vs hit {hit[0]['k']} dt{hit[0]['dt']:.1f} hp{hit[0]['hp']} (n cands {len(cands)}, r{rg})")
        # ударенные чужие крипы вне моего радиуса (радиус конца прошлого хода)
        for k, dmg in shot.items():
            o = prev_c[k][0]
            if o != enemy: continue
            if any(c["k"] == k for c in cands): continue
            xy = cur_c[k][2] if k in cur_c else prev_c[k][2]
            if xy[0] is None: continue
            dt = math.hypot(xy[0] - sx, xy[1] - sy)
            if single: print(f"   OUT-OF-RANGE hit t{t} tower {sid}: creep {k} dt{dt:.1f} > r{rg} (tower hp {hp}, cur r {cur_t.get(sid, ['?','?'])[1]})")
print("rule scores (single-tower turns):", {r: f"{a}/{b}" for r, (a, b) in score.items()})
print("damage formula matches: with site radius (GitHub) %s, without %s" % (dmgstat.get("a"), dmgstat.get("b")))
