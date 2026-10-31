"""Wipe Cycle curves from tarkov.dev's flea price history.

For every item: its price through each wipe against its own mid-wipe price (weeks 4 to 16), the median over the wipes
with early data, at the levels the Wipe Cycle uses. Groups (guns, ammo, armour...) are the median of their items.
Writes Prices/wipe-curves.json and Research/wipe-cycle.md.

    python -I Research/tools/wipe_curves.py [path-to-SPT_Data/database] [download-folder]

The download folder is the one fetch_price_history.py fills (it reads <folder>/history/regular/*.json).
"""
import datetime
import glob
import json
import os
import statistics
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.join(HERE, "..", "..")
DB = sys.argv[1] if len(sys.argv) > 1 else "SPT_Runtime/SPT_Data/database/"
CACHE = sys.argv[2] if len(sys.argv) > 2 else os.path.join(ROOT, "tarkovDevData")

# Wipe launches, from the day the whole market jumps in the history (every item's price over its own long median):
# 2.0x or more overnight, then falling for weeks. 2024-12-26 has no data for its first 18 days, so it only counts from
# week 3. The July 2025 and November 2025 (1.0) wipes have no early data at all and aren't used.
WIPES = [("2022-06-25", 0), ("2022-12-26", 0), ("2023-12-26", 0), ("2024-12-26", 3)]
WEEKS = 32

# Average player level by week of a wipe: the game's XP table and quest XP, 25 raids a week (45% survived), up to 12
# quests a week. The same scale the curves have always used.
LEVEL_BY_WEEK = [4, 10, 19, 25, 29, 31, 34, 38, 41, 43, 44, 46, 47, 48, 49, 50, 51, 52, 53, 54, 54, 55, 56, 57, 58, 59,
                 60, 60, 61, 62, 63, 64]
# The level points the Wipe Cycle reads (WipeEconomy.CurveAt), and the week each is.
LEVELS = [4, 10, 19, 25, 29, 33, 41, 46, 51, 55, 60, 64]
POINT_WEEKS = [0, 1, 2, 3, 4, 6, 8, 11, 16, 21, 26, 31]
BASELINE_WEEKS = range(4, 17)

# Groups by base class, first match wins (WipeEconomy.CurveGroups).
GROUPS = [("gun", "5422acb9af1c889c16000029"), ("ammo", "5485a8684bdc2da71d8b4567"), ("armour", "57bef4c42459772e8d35a53b"),
          ("meds", "543be5664bdc2dd4348b4569"), ("keys", "543be5e94bdc2df1348b4568"), ("barter", "5448eb774bdc2d0a728b4567"),
          ("gear", "543be5f84bdc2dd4348b456a")]


def day(text):
    return int(datetime.datetime.strptime(text, "%Y-%m-%d").replace(tzinfo=datetime.timezone.utc).timestamp() // 86400)


ITEMS = json.load(open(os.path.join(DB, "templates/items.json"), encoding="utf-8"))
NAMES = json.load(open(os.path.join(DB, "locales/global/en.json"), encoding="utf-8"))


def group_of(tpl):
    chain, t = set(), tpl
    for _ in range(12):
        node = ITEMS.get(t)
        if not node:
            break
        chain.add(t)
        t = node.get("_parent")
    for name, base in GROUPS:
        if base in chain:
            return name
    return "other"


def item_curve(rows):
    """Week -> ratio to the wipe's mid-wipe median, median over wipes; None where there's no data."""
    by_day = {}
    for r in rows:
        if r.get("price"):
            by_day[r["timestamp"] // 86400000] = r["price"]
    per_week = [[] for _ in range(WEEKS)]
    for start, first_week in WIPES:
        d0 = day(start)
        weeks = []
        for w in range(WEEKS):
            prices = [by_day[d] for d in range(d0 + 7 * w, d0 + 7 * w + 7) if d in by_day]
            weeks.append(statistics.median(prices) if len(prices) >= 3 and w >= first_week else None)
        base = [p for w, p in enumerate(weeks) if w in BASELINE_WEEKS and p]
        if len(base) < 6:
            continue
        mid = statistics.median(base)
        for w, p in enumerate(weeks):
            if p:
                ratio = p / mid
                if 0.2 <= ratio <= 6:  # a single odd week isn't a wipe trend
                    per_week[w].append(ratio)
    # A week counts only where at least two wipes have it: one wipe's odd week (a patch, an event) isn't a trend.
    return [statistics.median(v) if len(v) >= 2 else None for v in per_week]


def points(weekly, need):
    """The curve at the Wipe Cycle's level points; None if too little data."""
    out = []
    for w in POINT_WEEKS:
        near = [weekly[x] for x in range(max(0, w - 1), min(WEEKS, w + 2)) if weekly[x] is not None]
        out.append(statistics.median(near) if near else None)
    if sum(v is not None for v in out) < need:
        return None
    # Fill gaps from the neighbours, flat at the ends.
    known = [i for i, v in enumerate(out) if v is not None]
    for i, v in enumerate(out):
        if v is None:
            lo = max((k for k in known if k < i), default=None)
            hi = min((k for k in known if k > i), default=None)
            out[i] = out[lo] if hi is None else out[hi] if lo is None else out[lo] + (out[hi] - out[lo]) * (i - lo) / (hi - lo)
    # Launch at most 2.5x, and smoothed with its neighbours past launch.
    out[0] = min(out[0], 2.5)
    smooth = [out[0]] + [statistics.median(out[max(1, i - 1):i + 2]) for i in range(1, len(out))]
    return [round(v * 20) / 20 for v in smooth]


def main():
    files = glob.glob(os.path.join(CACHE, "history", "regular", "*.json"))
    weekly_by_item, raw = {}, {}
    for path in files:
        tpl = os.path.basename(path)[:-5]
        if tpl not in ITEMS:
            continue
        weekly = item_curve(json.load(open(path, encoding="utf-8"))["data"])
        weekly_by_item[tpl] = weekly
        curve = points(weekly, need=8)
        if curve:
            raw[tpl] = curve

    groups = {}
    for name in [g for g, _ in GROUPS] + ["other", "all"]:
        members = [w for t, w in weekly_by_item.items() if name == "all" or group_of(t) == name]
        weekly = [statistics.median(v) if v else None
                  for v in ([x[w] for x in members if x[w] is not None] for w in range(WEEKS))]
        groups[name] = points(weekly, need=6) or [1.0] * len(LEVELS)
    # One item's history is thin (a handful of listings a week early in a wipe, when the flea is locked to most
    # players), so its own curve is pulled halfway to its group's and kept within 0.67x to 1.5x of it.
    curves = {}
    for tpl, curve in raw.items():
        group = groups[group_of(tpl)]
        shrunk = [g * min(1.5, max(0.67, (c / g) ** 0.5)) for c, g in zip(curve, group)]
        curves[tpl] = [round(v * 20) / 20 for v in shrunk]

    out = {"levels": LEVELS, "groups": groups, "items": dict(sorted(curves.items()))}
    with open(os.path.join(ROOT, "Prices", "wipe-curves.json"), "w", encoding="utf-8", newline="\n") as f:
        json.dump(out, f, separators=(",", ":"))

    old_path = os.path.join(ROOT, "Prices", "wipe-curves.before.json")
    old = json.load(open(old_path, encoding="utf-8")) if os.path.exists(old_path) else None
    lines = ["# Wipe Cycle curves", "",
             "How flea prices move through a wipe, from tarkov.dev's PvP price history. Written by",
             "`Research/tools/wipe_curves.py`; shipped in the mod as `Prices/wipe-curves.json`.", "",
             f"Wipes used: {', '.join(w for w, _ in WIPES)} ({len(curves)} items with a curve of their own, from {len(files)} histories).",
             "Each item's price each week, against its own median over weeks 4 to 16 of that wipe, then the median over the",
             "wipes. Weeks are turned into levels with the game's XP table: 25 raids a week, 45% survived, up to 12 quests a week.", "",
             "| Group | " + " | ".join(f"lvl {l}" for l in LEVELS) + " |", "|---|" + "---|" * len(LEVELS)]
    for name, curve in groups.items():
        lines.append(f"| {name} | " + " | ".join(f"{v:.2f}" for v in curve) + " |")
        if old and name in old.get("groups", {}):
            lines.append("| (before) | " + " | ".join(f"{v:.2f}" for v in old["groups"][name]) + " |")
    with open(os.path.join(ROOT, "Research", "wipe-cycle.md"), "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(lines) + "\n")
    print(len(curves), "item curves")
    for name, curve in groups.items():
        print(f"{name:7}", curve)


main()
