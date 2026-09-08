#!/usr/bin/env python3
"""Реплеи арены CodinGame (Code Royale) через внутренний API.

python3 tools/arena/fetch.py --handle <publicHandle из URL профиля> [--out build/arena] [--max-games 50]
python3 tools/arena/fetch.py --league 3 --agents 20 [--max-games 200]      # бои топа лиги (6 Legend ... 3 Bronze)

Сырые JSON партий -> <out>/games/<gameId>.json (повторный запуск качает только новые),
сводка -> <out>/games.tsv: gameId, наш индекс, имена, agentId, лиги, счёт, победитель.
"""
import argparse
import json
import sys
import time
import urllib.request
from pathlib import Path

API = "https://www.codingame.com/services/"
PUZZLE = "code-royale"


def post(path, body, retries=3):
    data = json.dumps(body).encode()
    for attempt in range(retries):
        try:
            req = urllib.request.Request(API + path, data=data, headers={
                "Content-Type": "application/json",
                "User-Agent": "code-royale-bot arena stats (personal research)",
            })
            with urllib.request.urlopen(req, timeout=60) as r:
                return json.loads(r.read().decode())
        except Exception as e:  # noqa: BLE001
            if attempt == retries - 1:
                raise
            time.sleep(2 * (attempt + 1))


def leaderboard():
    return post("Leaderboards/getFilteredPuzzleLeaderboard",
                [PUZZLE, None, "global", {"active": False, "column": "", "filter": ""}])["users"]


def last_battles(agent_id):
    return post("gamesPlayersRanking/findLastBattlesByAgentId", [agent_id, None]) or []


def game_result(game_id):
    return post("gameResult/findByGameId", [game_id, None])


def summarize(game, leagues, my_agent):
    agents = sorted(game["agents"], key=lambda a: a["index"])
    names = [((a.get("codingamer") or {}).get("pseudo") or "Boss").replace(" ", "_") for a in agents]
    ids = [a.get("agentId", -1) for a in agents]
    scores = game.get("scores") or [-2, -2]
    winner = -1 if scores[0] == scores[1] else (0 if scores[0] > scores[1] else 1)
    me = ids.index(my_agent) if my_agent in ids else -1
    return "\t".join(map(str, [game["gameId"], me, names[0], names[1], ids[0], ids[1],
                               leagues.get(ids[0], -1), leagues.get(ids[1], -1), scores[0], scores[1], winner,
                               len(game.get("frames") or [])]))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--handle", default=None)
    ap.add_argument("--agent", type=int, default=None, help="agentId напрямую (если игрока нет в топ-1000 лидерборда)")
    ap.add_argument("--league", type=int, default=3)
    ap.add_argument("--agents", type=int, default=20)
    ap.add_argument("--max-games", type=int, default=50)
    ap.add_argument("--out", default="build/arena")
    ap.add_argument("--delay", type=float, default=0.15)
    args = ap.parse_args()

    out = Path(args.out)
    (out / "games").mkdir(parents=True, exist_ok=True)
    users = leaderboard()
    leagues = {u["agentId"]: u["league"]["divisionIndex"] for u in users}
    my_agent = -1
    if args.agent:
        my_agent = args.agent
        picked = [{"agentId": my_agent}]
        print(f"agent {my_agent}", file=sys.stderr)
    elif args.handle:
        picked = [u for u in users if (u.get("codingamer") or {}).get("publicHandle") == args.handle]
        if not picked:
            print("handle not found in the leaderboard", file=sys.stderr)
            return
        u = picked[0]
        my_agent = u["agentId"]
        print(f"{u['pseudo']}: rank {u['rank']}, league {u['league']['divisionIndex']} #{u['localRank']} of {u['league']['divisionAgentsCount']}, agent {my_agent}, lang {u.get('programmingLanguage')}", file=sys.stderr)
    else:
        picked = [u for u in users if u["league"]["divisionIndex"] == args.league][: args.agents]
        print(f"leaderboard: {len(users)} users, league {args.league}: {sum(1 for u in users if u['league']['divisionIndex'] == args.league)}, using {len(picked)}", file=sys.stderr)

    have = {int(p.stem) for p in (out / "games").glob("*.json")}
    game_ids = []
    for u in picked:
        for b in last_battles(u["agentId"]):
            gid = b.get("gameId")
            if gid and b.get("done") and gid not in have and gid not in game_ids:
                game_ids.append(gid)
        time.sleep(args.delay)
    print(f"new games: {len(game_ids)} (have {len(have)})", file=sys.stderr)

    tsv = out / "games.tsv"
    if not tsv.exists():
        tsv.write_text("gameId\tme\tname0\tname1\tagent0\tagent1\tleague0\tleague1\tscore0\tscore1\twinner\tframes\n", encoding="utf-8")
    with tsv.open("a", encoding="utf-8") as f:
        for i, gid in enumerate(game_ids[: args.max_games]):
            try:
                g = game_result(gid)
            except Exception as e:  # noqa: BLE001
                print(f"game {gid}: error {e}", file=sys.stderr)
                continue
            (out / "games" / f"{gid}.json").write_text(json.dumps(g), encoding="utf-8")
            f.write(summarize(g, leagues, my_agent) + "\n")
            f.flush()
            time.sleep(args.delay)
    print(f"done -> {out}", file=sys.stderr)


if __name__ == "__main__":
    main()
