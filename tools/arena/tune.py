#!/usr/bin/env python3
"""Покоординатный подбор весов поиска на арене self-play (tests -- arena).

python3 tools/arena/tune.py [--games 100] [--ms 6] [--threads 4] [--base "key=value ..."] [--rounds 1]
                            [--params "mine=3,5,6 tower=0.3,1 ..."] [--log build/tune/log.txt]

Кандидат = базовые веса + одно изменение. Оценка = суммарный винрейт против панели соперников:
имитация босса Bronze (wood=1 barlate=1 income=4 towers=3) и поиск с базовыми весами.
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


def arena(a, b, games, seed, ms, threads):
    cmd = ["dotnet", "run", "-c", "Release", "--no-build", "--project", str(ROOT / "tests/RoyaleBot.Tests"), "--",
           "arena", f"games={games}", f"seed={seed}", f"ms={ms}", f"threads={threads}", f"a={a}", f"b={b}", "quiet=1"]
    out = subprocess.run(cmd, capture_output=True, text=True, cwd=ROOT).stdout
    m = re.search(r"total a=(\d+) b=(\d+) d=(\d+)", out)
    if not m:
        raise RuntimeError("arena failed: " + out[-500:])
    w, l, d = map(int, m.groups())
    return (w + 0.5 * d) / max(1, w + l + d)


def score(cand, base, games, seed, ms, threads):
    s1 = arena(cand, BOSSLIKE, games, seed, ms, threads)
    s2 = arena(cand, base, games, seed + 10000, ms, threads)
    return s1, s2, (s1 + s2) / 2


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
    args = ap.parse_args()

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
    b1, b2, best = score(base, base if base else "", args.games, args.seed, args.ms, args.threads)
    note(f"base [{base}]: vs boss {b1:.3f}, vs self {b2:.3f}, score {best:.3f}  ({time.time() - t0:.0f}s)")
    for rnd in range(args.rounds):
        for key, values in params:
            for v in values:
                cand = re.sub(rf"\b{key}=\S+", "", base).strip() + f" {key}={v}"
                cand = cand.strip()
                t1 = time.time()
                s1, s2, sc = score(cand, base, args.games, args.seed, args.ms, args.threads)
                verdict = "ACCEPT" if sc > best + args.margin else "keep"
                note(f"round {rnd} [{cand}]: vs boss {s1:.3f}, vs base {s2:.3f}, score {sc:.3f} -> {verdict}  ({time.time() - t1:.0f}s)")
                if verdict == "ACCEPT":
                    base, best = cand, sc
    note(f"result [{base}] score {best:.3f}, total {time.time() - t0:.0f}s")


if __name__ == "__main__":
    main()
