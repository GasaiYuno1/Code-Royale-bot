#!/usr/bin/env python3
"""Покоординатный подбор весов поиска на арене self-play (tests -- arena).

python3 tools/arena/tune.py [--games 100] [--ms 6] [--threads 4] [--base "key=value ..."] [--rounds 1]
                            [--params "mine=3,5,6 tower=0.3,1 ..."] [--log build/tune/log.txt]

Кандидат = базовые веса + одно изменение. Оценка = средний винрейт против панели соперников (--panel, по умолчанию имитация босса Bronze,
«черепаха» и рашер на WoodStrategy) и против поиска с текущими принятыми весами (self).
Изменение принимается, если суммарный винрейт выше базового на --margin (по умолчанию 0.03).
"""
import argparse
import re
import subprocess
import sys
import time
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent.parent
BOSSLIKE = "wood=1 barlate=1 income=4 towers=3"
TURTLE = "wood=1 income=0 towers=8 upgrade=800"     # только башни (как «черепаха» из арены)
RUSHER = "wood=1 income=4 towers=0"                 # казарма, шахты до дохода 4, рыцари, без башен
ECON = "wood=1 income=8 towers=4 upgrade=600"      # экономический бот: 75% соперников Bronze по реплеям (шахты, башни, рыцари)
DEFAULT_PANEL = "|".join([BOSSLIKE, ECON, TURTLE, RUSHER])


def arena(a, b, games, seed, ms, threads):
    cmd = ["dotnet", "run", "-c", "Release", "--no-build", "--project", str(ROOT / "tests/RoyaleBot.Tests"), "--",
           "arena", f"games={games}", f"seed={seed}", f"ms={ms}", f"threads={threads}", f"a={a}", f"b={b}", "quiet=1"]
    out = subprocess.run(cmd, capture_output=True, text=True, cwd=ROOT).stdout
    m = re.search(r"total a=(\d+) b=(\d+) d=(\d+)", out)
    if not m:
        raise RuntimeError("arena failed: " + out[-500:])
    w, l, d = map(int, m.groups())
    return (w + 0.5 * d) / max(1, w + l + d)


def score(cand, base, games, seed, ms, threads, panel):
    """Средний винрейт против панели соперников и текущей базы (self); возвращает (список по панели, self, среднее)."""
    parts = [arena(cand, opp, games, seed + 1000 * i, ms, threads) for i, opp in enumerate(panel)]
    s_self = arena(cand, base, games, seed + 10000, ms, threads)
    allv = parts + [s_self]
    return parts, s_self, sum(allv) / len(allv)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--games", type=int, default=100)
    ap.add_argument("--ms", type=int, default=6)
    ap.add_argument("--threads", type=int, default=4)
    ap.add_argument("--base", default="")
    ap.add_argument("--rounds", type=int, default=1)
    ap.add_argument("--margin", type=float, default=0.03)
    ap.add_argument("--seed", type=int, default=1000)
    ap.add_argument("--params", default="mine=3,6 tower=0.25,1 towerneed=500,1200 towercalm=300,800 exposure=1,4 eknight=10,40 knight=2,10 ehp=30,80 gold=2,8 cover=100,600")
    ap.add_argument("--log", default="build/tune/log.txt")
    ap.add_argument("--panel", default=DEFAULT_PANEL, help="соперники через |; self (текущая база) добавляется всегда")
    args = ap.parse_args()
    panel = [p.strip() for p in args.panel.split("|") if p.strip()]

    log = ROOT / args.log
    log.parent.mkdir(parents=True, exist_ok=True)

    def note(msg):
        print(msg, flush=True)
        with log.open("a", encoding="utf-8") as f:
            f.write(msg + "\n")

    base = args.base.strip()
    params = []
    for tok in args.params.split():
        k, vs = tok.split("=")
        params.append((k, vs.split(",")))

    t0 = time.time()
    parts, b2, best = score(base, base if base else "", args.games, args.seed, args.ms, args.threads, panel)
    fmt = lambda ps: " ".join(f"{v:.2f}" for v in ps)
    note(f"panel: {panel}")
    note(f"base [{base}]: vs panel {fmt(parts)}, vs self {b2:.3f}, score {best:.3f}  ({time.time() - t0:.0f}s)")
    for rnd in range(args.rounds):
        for key, values in params:
            for v in values:
                cand = re.sub(rf"\b{key}=\S+", "", base).strip() + f" {key}={v}"
                cand = cand.strip()
                t1 = time.time()
                parts, s2, sc = score(cand, base, args.games, args.seed, args.ms, args.threads, panel)
                verdict = "ACCEPT" if sc > best + args.margin else "keep"
                note(f"round {rnd} [{cand}]: vs panel {fmt(parts)}, vs base {s2:.3f}, score {sc:.3f} -> {verdict}  ({time.time() - t1:.0f}s)")
                if verdict == "ACCEPT":
                    base, best = cand, sc
    note(f"result [{base}] score {best:.3f}, total {time.time() - t0:.0f}s")


if __name__ == "__main__":
    main()
