#!/usr/bin/env python3
"""SPSA-тюнинг весов оценки по винрейту в self-play (арена в процессе: tests -- arena, физика арены CodinGame).

Каждая итерация: случайное направление Δ∈{±1}^d, матч θ+cΔ против θ−cΔ (стороны чередуются, партии параллельно
в потоках арены), шаг по направлению Δ пропорционально (2·winrate − 1). Раз в --eval-every итераций текущий θ
играет против замороженного эталона (умолчания сборки); лучший по этой проверке θ сохраняется в build/spsa/best.json,
полный журнал — build/spsa/log.txt, состояние для --resume — build/spsa/state.json.

  python3 tools/arena/spsa.py --iters 80 --games 300 --ms 6 --threads 4        # в фоне, на часы (чистый self-play)
  python3 tools/arena/spsa.py --iters 40 --games 320 --panel "wood=1 barlate=1 income=4 towers=3|" --panel-weight 0.5 --start build/spsa/best.json
      # с панелью-регуляризатором: половина партий и градиента — θ+ и θ− против имитации босса и против умолчаний ('' = умолчания)
  python3 tools/arena/spsa.py --resume                                         # продолжить
  python3 tools/arena/spsa.py --check build/spsa/best.json --games 2000        # θ против эталона
  python3 tools/arena/spsa.py --vs "eknight=15" "" --games 400                 # быстрый матч A против B
Сборка: dotnet build tests/RoyaleBot.Tests -c Release (арена берётся из bin тестов; во время прогона не пересобирать).
"""
import argparse
import json
import random
import re
import subprocess
import sys
import time
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent.parent
ARENA = ROOT / "tests" / "RoyaleBot.Tests" / "bin" / "Release" / "net8.0" / "RoyaleBot.Tests.dll"
OUT = ROOT / "build" / "spsa"
TOTAL_RE = re.compile(r"total a=(\d+) b=(\d+) d=(\d+)")

# ключ Tuning: (дефолт, масштаб возмущения c, минимум, максимум, целое?)
PARAMS = {
    "hp": (130, 15, 40, 250, False),
    "ehp": (70, 10, 10, 150, False),
    "tower": (0.36, 0.08, 0.0, 1.0, False),
    "towerbase": (130, 80, 0, 1200, False),
    "towerneed": (500, 150, 100, 2000, False),
    "towercalm": (750, 100, 0, 1500, False),
    "exposure": (5, 1, 0, 12, False),
    "safe": (250, 40, 100, 500, True),
    "minefar": (0.85, 0.1, 0.0, 1.0, False),
    "mine": (3.8, 0.8, 0.5, 10, False),
    "emine": (1.9, 0.5, 0, 6, False),
    "gold": (4.6, 1, 0, 10, False),
    "knight": (6.25, 1.2, 0, 15, False),
    "eknight": (10, 3, 2, 40, False),
    "nobar": (6100, 1000, 500, 15000, False),
    "econshort": (140, 100, 0, 1500, False),
    "readiness": (1.5, 0.7, 0, 8, False),
    "defneed": (500, 120, 100, 1500, True),
    "giantbar": (1350, 300, 0, 5000, False),
    "cover": (10, 80, 0, 1000, False),
    "lead": (6500, 1000, 0, 15000, False),
    "lowhpw": (3.2, 1.5, 0, 15, False),
    "erange": (320, 80, 0, 1000, False),
    "expeta": (0.9, 0.15, 0.0, 1.0, False),
    "persist": (240, 40, 0, 500, False),
}


def fmt(theta):
    return " ".join(f"{k}={theta[k]:.4g}" for k in PARAMS)


def args_of(theta):
    if theta is None:
        return ""
    parts = []
    for k, (_, _, _, _, integer) in PARAMS.items():
        v = theta[k]
        parts.append(f"{k}={int(round(v))}" if integer else f"{k}={v:.5g}")
    return " ".join(parts)


def match(games, seed, args_a, args_b, ms, threads):
    """Возвращает (очки A с ничьими за 0.5, всего партий)."""
    c = ["dotnet", str(ARENA), "arena", f"games={games}", f"seed={seed}", f"ms={ms}", f"threads={threads}",
         f"a={args_a}", f"b={args_b}", "quiet=1"]
    out = subprocess.run(c, cwd=ROOT, capture_output=True, text=True).stdout
    m = TOTAL_RE.search(out)
    if not m:
        print(out[-800:], file=sys.stderr)
        raise RuntimeError("arena failed")
    a, b, d = int(m.group(1)), int(m.group(2)), int(m.group(3))
    return a + 0.5 * d, a + b + d


def clip(theta):
    for k, (_, _, lo, hi, _) in PARAMS.items():
        theta[k] = min(hi, max(lo, theta[k]))
    return theta


def log(msg):
    line = time.strftime("%H:%M:%S ") + msg
    print(line, flush=True)
    with open(OUT / "log.txt", "a") as f:
        f.write(line + "\n")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--iters", type=int, default=80)
    ap.add_argument("--games", type=int, default=300, help="партий на итерацию (θ+ против θ−)")
    ap.add_argument("--eval-games", type=int, default=600, help="партий на проверку против эталона")
    ap.add_argument("--eval-every", type=int, default=10)
    ap.add_argument("--ms", type=int, default=6, help="бюджет хода в арене (12 = как в бою, 6 ≈ 150 партий/мин)")
    ap.add_argument("--threads", type=int, default=4)
    ap.add_argument("--lr", type=float, default=2.0, help="шаг: Δθ_i = lr·k_decay·(2w−1)·Δ_i·c_i")
    ap.add_argument("--decay", type=float, default=30.0, help="k_decay = decay/(iter+decay)")
    ap.add_argument("--cscale", type=float, default=1.0, help="множитель возмущения c")
    ap.add_argument("--seed", type=int, default=1)
    ap.add_argument("--panel", default="", help="соперники-регуляризаторы через |: key=value каждого (пусто = только self-play); '' в списке = умолчания сборки")
    ap.add_argument("--panel-weight", type=float, default=0.5, help="доля партий и веса градиента на панель (остальное — θ+ против θ−)")
    ap.add_argument("--start", help="json с θ, с которого начать (вместо умолчаний)")
    ap.add_argument("--resume", action="store_true")
    ap.add_argument("--check", help="json с θ: только матч против эталона")
    ap.add_argument("--vs", nargs=2, metavar=("ARGS_A", "ARGS_B"), help="только матч: key=value для A и для B")
    args = ap.parse_args()
    OUT.mkdir(parents=True, exist_ok=True)

    if args.vs:
        t0 = time.time()
        w, n = match(args.games, args.seed + 999, args.vs[0], args.vs[1], args.ms, args.threads)
        print(f"A {w:.1f}/{n} = {100.0 * w / n:.1f}% ({time.time() - t0:.0f} s)\n  A: {args.vs[0] or '(defaults)'}\n  B: {args.vs[1] or '(defaults)'}")
        return

    if args.check:
        theta = json.load(open(args.check))
        w, n = match(args.games, args.seed + 777, args_of(theta), "", args.ms, args.threads)
        print(f"{w:.1f}/{n} = {100.0 * w / n:.1f}% vs reference: {fmt(theta)}")
        return

    rng = random.Random(args.seed)
    state_path = OUT / "state.json"
    if args.resume and state_path.exists():
        st = json.load(open(state_path))
        theta, it, best = st["theta"], st["iter"], st["best"]
        rng.setstate(tuple(tuple(x) if isinstance(x, list) else x for x in st["rng"]))
        log(f"resume at iter {it}: {fmt(theta)}")
    else:
        theta = {k: float(v[0]) for k, v in PARAMS.items()}
        if args.start:
            theta = {k: float(v) for k, v in json.load(open(args.start)).items()}
        it, best = 0, {"score": 0.0, "theta": dict(theta), "iter": 0}
        log(f"start (games {args.games}, ms {args.ms}, lr {args.lr}, decay {args.decay}, panel {args.panel!r} w {args.panel_weight}): {fmt(theta)}")
    panel = [p.strip() for p in args.panel.split("|")] if args.panel != "" else []
    pw = args.panel_weight if panel else 0.0

    while it < args.iters:
        delta = {k: rng.choice((-1.0, 1.0)) for k in PARAMS}
        plus = clip({k: theta[k] + args.cscale * PARAMS[k][1] * delta[k] for k in PARAMS})
        minus = clip({k: theta[k] - args.cscale * PARAMS[k][1] * delta[k] for k in PARAMS})
        t0 = time.time()
        n_self = int(round(args.games * (1 - pw)))
        w, n = match(n_self, args.seed * 100000 + it * 1000, args_of(plus), args_of(minus), args.ms, args.threads)
        wr = w / n
        signal = (1 - pw) * (2 * wr - 1)
        panel_txt = ""
        if panel:
            n_p = max(2, int(round(args.games * pw / len(panel) / 2)))
            diffs = []
            for pi, opp in enumerate(panel):
                wp, np_ = match(n_p, args.seed * 100000 + it * 1000 + 100 + pi * 10, args_of(plus), opp, args.ms, args.threads)
                wm, nm = match(n_p, args.seed * 100000 + it * 1000 + 100 + pi * 10, args_of(minus), opp, args.ms, args.threads)
                diffs.append(wp / np_ - wm / nm)
                panel_txt += f" P{pi} {100 * wp / np_:.0f}/{100 * wm / nm:.0f}"
            signal += pw * sum(diffs) / len(diffs)
        k_decay = args.decay / (it + args.decay)
        for k in PARAMS:
            theta[k] += args.lr * k_decay * signal * delta[k] * PARAMS[k][1]
        clip(theta)
        it += 1
        log(f"iter {it}: self {w:.1f}/{n} = {100 * wr:.1f}%{panel_txt} signal {signal:+.3f} ({time.time() - t0:.0f} s)  -> {fmt(theta)}")
        if it % args.eval_every == 0:
            t0 = time.time()
            w, n = match(args.eval_games, args.seed * 100000 + 50000000 + it * 1000, args_of(theta), "", args.ms, args.threads)
            scores = [w / n]
            txt = f"vs reference {w:.1f}/{n} = {100 * w / n:.1f}%"
            for pi, opp in enumerate(panel):
                if opp == "":
                    continue
                wp, np_ = match(args.eval_games // 2, args.seed * 100000 + 60000000 + it * 1000 + pi, args_of(theta), opp, args.ms, args.threads)
                scores.append(wp / np_)
                txt += f", vs P{pi} {wp:.1f}/{np_} = {100 * wp / np_:.1f}%"
            score = sum(scores) / len(scores)
            log(f"  eval {txt} -> score {100 * score:.1f}% ({time.time() - t0:.0f} s)")
            if score > best["score"]:
                best = {"score": score, "theta": dict(theta), "iter": it}
                json.dump(theta, open(OUT / "best.json", "w"), indent=1)
                log(f"  new best {100 * score:.1f}% saved")
        json.dump({"theta": theta, "iter": it, "best": best,
                   "rng": [list(x) if isinstance(x, tuple) else x for x in rng.getstate()]},
                  open(state_path, "w"))
    log(f"done: best {100 * best['score']:.1f}% at iter {best['iter']}: {fmt(best['theta'])}")


if __name__ == "__main__":
    main()
