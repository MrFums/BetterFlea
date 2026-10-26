"""Writes Research/quest-unlock-difficulty.md: how hard each quest that unlocks a trader item is to finish.

For gun pricing (Prices/Pricing.cs GunPricing): a part a hard quest unlocks keeps more of its flea price, since few
players can buy it from the trader. A quest's difficulty is its own conditions (kills, bosses, maps, found-in-raid
hand-ins, skills, loyalty) plus every quest before it in the chain, and the highest level lock along the way.

Reads the clean install's SPT database (EFT 0.16.9.5 / SPT 4.1.6). Run with:
    python -I Research/tools/quest_difficulty.py [path-to-SPT_Data/database] [output.md] [output.tsv]
"""
import json
import math
import os
import sys
from collections import defaultdict

HERE = os.path.dirname(os.path.abspath(__file__))
DB = sys.argv[1] if len(sys.argv) > 1 else "E:/Games/SPT4.1-CLEAN/SPT_Runtime/SPT_Data/database/"
OUT = sys.argv[2] if len(sys.argv) > 2 else os.path.join(HERE, "..", "quest-unlock-difficulty.md")
TSV = sys.argv[3] if len(sys.argv) > 3 else None


def load(path):
    with open(os.path.join(DB, path), encoding="utf-8") as f:
        return json.load(f)


QUESTS = load("templates/quests.json")
QUEST_CONFIG = load("../configs/quest.json")
# SPT hides non-seasonal event quests unless showNonSeasonalEventQuests is on; seasonal ones only run in their season.
EVENT = {qid: e.get("season", "None") for qid, e in QUEST_CONFIG.get("eventQuests", {}).items()}
SHOW_NON_SEASONAL = QUEST_CONFIG.get("showNonSeasonalEventQuests", False)
ITEMS = load("templates/items.json")
LOCALE = load("locales/global/en.json")
PRICE = {e["Id"]: e["Price"] for e in load("templates/handbook.json")["Items"]}
FENCE = "579dc571d53a0658a154fbec"
LIGHTKEEPER = "638f541a29ffd1183d187f57"
BTR = "656f0f98d80a697f855d34b1"
# Hidden traders no quest reward unlocks: the quest that leads to them. The BTR Driver is met through Skier's
# A Helping Hand (level 20), then in raid.
TRADER_GATE = {BTR: "6752f6d83038f7df520c83e8"}
TRADERS = {}
for trader_id in os.listdir(os.path.join(DB, "traders")):
    base = os.path.join(DB, "traders", trader_id, "base.json")
    if os.path.exists(base):
        TRADERS[trader_id] = load(f"traders/{trader_id}/base.json")

# Quest "location" ids and Location-condition map keys -> (name, points). Labs needs a keycard, Labyrinth a key and
# the Labs route; Lighthouse, Reserve and Streets are the deadly late maps.
MAPS = {
    "laboratory": ("Labs", 3), "5b0fc42d86f7744a585f9105": ("Labs", 3),
    "Labyrinth": ("Labyrinth", 3), "6733700029c367a3d40b02af": ("Labyrinth", 3),
    "Lighthouse": ("Lighthouse", 2), "5704e4dad2720bb55b8b4567": ("Lighthouse", 2),
    "RezervBase": ("Reserve", 2), "5704e5fad2720bc05b8b4567": ("Reserve", 2),
    "TarkovStreets": ("Streets", 2), "5714dc692459777137212e12": ("Streets", 2),
    "Shoreline": ("Shoreline", 1), "5704e554d2720bac5b8b456e": ("Shoreline", 1),
    "Interchange": ("Interchange", 1), "5714dbc024597771384a510d": ("Interchange", 1),
    "factory4_night": ("Night Factory", 1), "59fc81d786f774390775787e": ("Night Factory", 1),
    "bigmap": ("Customs", 0), "56f40101d2720b2a4d8b45d6": ("Customs", 0),
    "Woods": ("Woods", 0), "5704e3c2d2720bac5b8b4567": ("Woods", 0),
    "factory4_day": ("Factory", 0), "55f2d3fd4bdc2d5f408b4567": ("Factory", 0),
    "Sandbox": ("Ground Zero", 0), "Sandbox_high": ("Ground Zero", 0), "653e6760052c01c1c805532f": ("Ground Zero", 0),
}

# Per kill. Goons and Zryachiy roam or sit somewhere awful; bosses spawn by chance; followers come with a boss.
ROLE_WEIGHT = {
    "bossZryachiy": 5, "followerZryachiy": 3,
    "bossKnight": 4, "followerBigPipe": 4, "followerBirdEye": 4,
    "bossKilla": 3, "bossKojaniy": 3, "bossGluhar": 3, "bossSanitar": 3, "bossBully": 3, "bossTagilla": 3,
    "bossBoar": 3, "bossBoarSniper": 2, "bossKolontay": 3, "bossPartisan": 3, "bossKillaAgro": 3, "bossTagillaAgro": 3,
    "sectantPriest": 2, "sectantWarrior": 1.5, "sectantPrizrak": 2, "sectantPredvestnik": 2, "sectantOni": 2,
    "pmcBot": 1, "exUsec": 1, "marksman": 0.3,
}
FOLLOWER = 1.5


def name(key):
    return LOCALE.get(f"{key} name") or LOCALE.get(f"{key} Name") or key


def trader_name(trader_id):
    return LOCALE.get(f"{trader_id} Nickname") or TRADERS.get(trader_id, {}).get("nickname", trader_id)


def kill_points(kill, count, one_raid):
    roles = kill.get("savageRole") or []
    target = kill.get("target")
    notes = []
    if roles:
        weight = max(ROLE_WEIGHT.get(role, FOLLOWER if role.startswith("follower") else 0.5) for role in roles)
        points = weight * count ** 0.6
        notes.append("kill " + "/".join(sorted(set(roles)))[:60] + f" x{count}")
    elif target in ("AnyPmc", "Usec", "Bear"):
        points = 0.25 * count
        notes.append(f"{count} PMC kills")
    elif target == "Any":
        points = 0.12 * count
        notes.append(f"{count} kills")
    else:
        points = 0.04 * count
        notes.append(f"{count} scav kills")

    factor = 1.0
    if kill.get("bodyPart") and set(kill["bodyPart"]) <= {"Head"}:
        factor *= 2
        notes.append("headshots")
    distance = (kill.get("distance") or {}).get("value") or 0
    if distance >= 100:
        factor *= 2
        notes.append(f"{distance}m+")
    elif distance >= 50:
        factor *= 1.5
        notes.append(f"{distance}m+")
    if kill.get("weapon") or kill.get("weaponCaliber") or kill.get("weaponModsInclusive"):
        factor *= 1.3
        notes.append("set weapon")
    if kill.get("enemyEquipmentInclusive") or kill.get("enemyHealthEffects"):
        factor *= 1.3
        notes.append("set target state")
    daytime = kill.get("daytime") or {}
    if daytime.get("from") or daytime.get("to"):
        factor *= 1.2
        notes.append("time of day")
    if one_raid and count > 1:
        factor *= 2
        notes.append("one raid")
    return points * factor, notes


def own_difficulty(quest):
    points = 0.0
    notes = []
    maps = set()
    if quest.get("location") in MAPS:
        maps.add(MAPS[quest["location"]])
    if quest.get("traderId") == LIGHTKEEPER:
        points += 2
        notes.append("Lightkeeper quest")
    if quest.get("traderId") in TRADER_GATE:
        points += 1
        notes.append(f"hidden trader ({trader_name(quest['traderId'])}), met in raid")

    for cond in quest["conditions"].get("AvailableForFinish", []):
        kind = cond["conditionType"]
        if kind == "CounterCreator":
            inner = cond["counter"]["conditions"]
            one_raid = cond.get("oneSessionOnly") or any(c.get("resetOnSessionEnd") for c in inner)
            for c in inner:
                if c["conditionType"] == "Location":
                    maps.update(MAPS[t] for t in c.get("target", []) if t in MAPS)
            for c in inner:
                if c["conditionType"] in ("Kills", "Shots"):
                    p, n = kill_points(c, cond.get("value") or 1, one_raid)
                    if c["conditionType"] == "Shots":
                        p *= 0.5
                    points += p
                    notes += n
                elif c["conditionType"] == "Equipment":
                    points += 0.5
                    notes.append("set gear")
                elif c["conditionType"] in ("HealthEffect", "HealthBuff", "UnderArtilleryFire", "Time"):
                    points += 0.5
                elif c["conditionType"] == "ExitStatus" and not any(x["conditionType"] in ("Kills", "Shots") for x in inner):
                    # Survive-and-extract quests: each needed run is a raid.
                    runs = cond.get("value") or 1
                    points += 0.3 * runs
                    if runs > 1:
                        notes.append(f"survive {runs} raids")
            if one_raid and not any(c["conditionType"] == "Kills" for c in inner):
                points += 1
                notes.append("in one raid")
        elif kind == "HandoverItem" and any(ITEMS.get(t, {}).get("_props", {}).get("QuestItem") for t in cond.get("target", [])):
            # A quest item: a raid to its one spot to fetch it.
            points += 1
            notes.append(f"fetch {name(cond['target'][0])} in raid")
        elif kind == "HandoverItem":
            count = cond.get("value") or 1
            value = sum(PRICE.get(t, 0) for t in cond.get("target", [])) / max(1, len(cond.get("target", []))) * count
            fir = cond.get("onlyFoundInRaid")
            p = min(5, value / (100_000 if fir else 300_000))
            if fir and count >= 5:
                p += 0.5
            points += p
            if p >= 1:
                notes.append(f"hand in {count} {name(cond['target'][0]) if cond.get('target') else '?'}" + (" FIR" if fir else ""))
        elif kind in ("LeaveItemAtLocation", "PlaceBeacon"):
            points += 0.3
        elif kind == "WeaponAssembly":
            points += 0.5
        elif kind == "Skill":
            points += 0.4 * (cond.get("value") or 0)
            notes.append(f"{cond.get('target')} skill {cond.get('value')}")
        elif kind == "TraderLoyalty":
            points += max(0, (cond.get("value") or 1) - 1)
            notes.append(f"{trader_name(cond.get('target'))} LL{cond.get('value')}")

    if quest["conditions"].get("Fail"):
        points += 0.5
    # Taking this quest fails another one: a choice that closes the other route, so fewer players go this way.
    for other in QUESTS.values():
        if other is quest:
            continue
        for cond in other["conditions"].get("Fail", []):
            if cond["conditionType"] == "Quest" and cond.get("target") == quest["_id"]:
                points += 1
                notes.append(f"fails {trader_name(other['traderId'])}'s {name(other['_id'])}")
    map_points = max((p for _, p in maps), default=0)
    points += map_points
    hard_maps = sorted(n for n, p in maps if p >= 2)
    if hard_maps:
        notes.append("/".join(hard_maps))
    return points, notes


def start_requirements(quest):
    level = 0
    prereqs = []
    loyalty = []
    for cond in quest["conditions"].get("AvailableForStart", []):
        kind = cond["conditionType"]
        if kind == "Level":
            level = max(level, cond.get("value") or 0)
        elif kind == "Quest" and cond.get("target") in QUESTS and set(cond.get("status") or []) & {2, 4}:
            prereqs.append(cond["target"])
        elif kind == "TraderLoyalty":
            loyalty.append((cond.get("target"), cond.get("value")))
    return level, prereqs, loyalty


OWN = {qid: own_difficulty(q) for qid, q in QUESTS.items()}
START = {qid: start_requirements(q) for qid, q in QUESTS.items()}


def chain(qid, memo={}):
    """(ancestors set, longest depth, highest level, highest start-loyalty gate as (trader, level))."""
    if qid in memo:
        return memo[qid]
    memo[qid] = (set(), 0, 0, (None, 0))  # breaks cycles
    level, prereqs, loyalty = START[qid]
    gate_quest = TRADER_GATE.get(QUESTS[qid]["traderId"])
    if gate_quest and gate_quest != qid and gate_quest not in prereqs:
        prereqs = prereqs + [gate_quest]
    ancestors = set()
    depth = 0
    gate = max(loyalty, key=lambda g: g[1] or 0, default=(None, 0))
    for p in prereqs:
        a, d, lv, g = chain(p)
        ancestors |= a | {p}
        depth = max(depth, d + 1)
        level = max(level, lv)
        gate = max(gate, g, key=lambda x: x[1] or 0)
    memo[qid] = (ancestors, depth, level, gate)
    return memo[qid]


def event_kind(qid):
    if qid not in EVENT:
        return None
    if EVENT[qid] == "None":
        return "event (never given)" if not SHOW_NON_SEASONAL else "event"
    return f"{EVENT[qid].lower()} event"


def difficulty(qid):
    ancestors, depth, level, gate = chain(qid)
    own = OWN[qid][0]
    gate_points = 2 * max(0, (gate[1] or 1) - 1)  # trader LL4 just to start is a long grind
    total = own + sum(OWN[a][0] for a in ancestors) + gate_points
    # 0..1 parts: the level lock, the whole chain's work, and how hard this quest is by itself.
    level_part = min(1, level / 45)
    chain_part = min(1, math.log1p(total) / math.log1p(150))
    own_part = min(1, own / 12)
    lightkeeper = QUESTS[qid].get("traderId") == LIGHTKEEPER or any(QUESTS[a].get("traderId") == LIGHTKEEPER for a in ancestors)
    score = 0.35 * level_part + 0.45 * chain_part + 0.2 * own_part
    # A high level lock is hard on its own, however short the chain: level 45 is at least "hard".
    score = max(score, level / 75)
    if lightkeeper:
        score = max(score, 0.9)
    # A hidden trader met in raid (the BTR Driver): most players never get him, so at least "hard".
    if QUESTS[qid].get("traderId") in TRADER_GATE:
        score = max(score, 0.6)
    event = event_kind(qid)
    if event == "event (never given)":
        score = 1.0
    elif event:
        score = max(score, 0.8)
    return {
        "level": level, "depth": depth, "ancestors": len(ancestors), "own": own, "chain": total,
        "score": min(1.0, score), "lightkeeper": lightkeeper, "event": event,
        "gate": f"{trader_name(gate[0])} LL{gate[1]} to start" if (gate[1] or 0) >= 3 else None,
    }


def tier(score):
    return "very hard" if score >= 0.75 else "hard" if score >= 0.55 else "medium" if score >= 0.35 else "easy"


def unlocks():
    """(trader, loyalty, quest id, root tpl) for every trader item a quest unlocks on success."""
    rows = []
    for trader_id in TRADERS:
        if trader_id == FENCE or not os.path.exists(os.path.join(DB, "traders", trader_id, "questassort.json")):
            continue
        qa = load(f"traders/{trader_id}/questassort.json").get("success", {})
        if not qa:
            continue
        assort = load(f"traders/{trader_id}/assort.json")
        by_id = {i["_id"]: i for i in assort.get("items", [])}
        loyalty = assort.get("loyal_level_items", {})
        for assort_id, qid in qa.items():
            item = by_id.get(assort_id)
            if item and qid in QUESTS:
                rows.append((trader_id, loyalty.get(assort_id, 1), qid, item["_tpl"]))
    return rows


def kind(tpl):
    seen = set()
    while tpl and tpl not in seen:
        seen.add(tpl)
        n = ITEMS.get(tpl, {}).get("_name", "")
        if n in ("Weapon", "Mod", "Ammo", "AmmoBox", "Equipment", "Armor", "ArmoredEquipment", "ArmorPlate", "Meds", "BarterItem", "Food", "Info", "Key"):
            return n
        tpl = ITEMS.get(tpl, {}).get("_parent")
    return "Other"


def main():
    rows = unlocks()
    by_quest = defaultdict(list)
    for trader_id, loyalty, qid, tpl in rows:
        by_quest[qid].append((trader_id, loyalty, tpl))

    stats = {qid: difficulty(qid) for qid in by_quest}
    # BEAR and USEC versions of a quest have the same name and unlock the same thing: show one row.
    shown = {}
    for qid in by_quest:
        key = (name(qid), QUESTS[qid]["traderId"])
        if key not in shown or stats[qid]["score"] > stats[shown[key]]["score"]:
            shown[key] = qid
    order = sorted(shown.values(), key=lambda q: -stats[q]["score"])

    lines = [
        "# Quest unlock difficulty",
        "",
        "Every quest that unlocks a trader item, scored by how hard it is to get to and finish. Written by",
        "`Research/tools/quest_difficulty.py` from the clean install's SPT database. For gun pricing: a part a hard",
        "quest unlocks should keep more of its flea price.",
        "",
        "**How it's scored** (0 to 1):",
        "- **Level, 35%:** the highest level lock anywhere in the quest's chain. Level 45+ counts in full.",
        "- **Chain, 45%:** the work in this quest plus every quest before it, on a log scale (150 points counts in full).",
        "- **This quest, 20%:** its own work. 12 points counts in full.",
        "- A level lock is worth at least level / 75 by itself (level 45 = 0.6).",
        "- Anything in the Lightkeeper chain is at least 0.9.",
        "- BTR Driver quests are at least 0.6: he's a hidden trader no quest unlocks. You reach him through Skier's",
        "  A Helping Hand (level 20) and meet him in raid, and his quests fail some of Skier's and Lightkeeper's.",
        "- Seasonal event quests are at least 0.8. Non-seasonal event quests are 1.0: SPT never gives them",
        "  (`showNonSeasonalEventQuests` is off), so what they unlock can't be bought from a trader at all.",
        "",
        "Work points: PMC kill 0.25, scav 0.04, boss 3 each (Goons 4, Zryachiy 5, followers 1.5, raiders/rogues 1),",
        "times 2 for headshots only, 100 m+ or all in one raid, 1.3 for a set weapon or target gear, 1.2 for time",
        "of day. Map: Labs/Labyrinth 3, Lighthouse/Reserve/Streets 2, Shoreline/Interchange/night Factory 1.",
        "Hand-ins: roubles / 100k found in raid (/300k otherwise), at most 5. Skill 0.4 per level, loyalty",
        "requirement 1 per level over 1, Lightkeeper's own quests +2. Needing trader LL3/LL4 to start the chain: 4/6.",
        "Fetching a quest item in raid +1, a quest that fails another quest (a choice of route) +1, hidden trader +1.",
        "",
        "Tiers: very hard 0.75+, hard 0.55+, medium 0.35+, easy below.",
        "",
        "| Score | Tier | Quest | Trader | Level | Depth | Quests before | Chain pts | Own pts | Why | Unlocks |",
        "|---|---|---|---|---|---|---|---|---|---|---|",
    ]
    tsv = ["quest_id\tquest\ttrader\tscore\ttier\tlevel\tdepth\tancestors\tchain\town\titem_tpl\titem\tkind\tloyalty"]
    for qid in order:
        s = stats[qid]
        quest = QUESTS[qid]
        items = by_quest[qid]
        notes = ([s["event"]] if s["event"] else []) + ([s["gate"]] if s["gate"] else []) + OWN[qid][1]
        why = ", ".join(dict.fromkeys(notes))[:140]
        names = sorted({name(tpl) for _, _, tpl in items})
        shown = ", ".join(names[:6]) + (f" +{len(names) - 6} more" if len(names) > 6 else "")
        lines.append(
            f"| {s['score']:.2f} | {tier(s['score'])} | {name(qid)} | {trader_name(quest['traderId'])} | {s['level']} | "
            f"{s['depth']} | {s['ancestors']} | {s['chain']:.0f} | {s['own']:.1f} | {why} | {shown} |")
        for trader_id, loyalty, tpl in items:
            tsv.append("\t".join(map(str, [qid, name(qid), trader_name(trader_id), f"{s['score']:.3f}", tier(s['score']),
                                            s["level"], s["depth"], s["ancestors"], f"{s['chain']:.1f}", f"{s['own']:.1f}",
                                            tpl, name(tpl), kind(tpl), loyalty])))

    counts = defaultdict(int)
    for qid in order:
        counts[tier(stats[qid]["score"])] += 1
    lines[2:2] = [f"{len(order)} quests unlock {len(rows)} trader items. Very hard {counts['very hard']}, hard {counts['hard']}, "
                  f"medium {counts['medium']}, easy {counts['easy']}.", ""]

    # Guns and attachments: every quest that unlocks one, and where else it can be bought without a quest.
    unlocked_by = defaultdict(list)
    for trader_id, loyalty, qid, tpl in rows:
        if kind(tpl) in ("Weapon", "Mod") and not any(name(q) == name(qid) for _, q, *_ in unlocked_by[tpl]):
            unlocked_by[tpl].append((stats[qid]["score"], qid, trader_id, loyalty))
    open_sale = defaultdict(set)
    for trader_id in TRADERS:
        folder = os.path.join(DB, "traders", trader_id)
        if trader_id == FENCE or not os.path.exists(os.path.join(folder, "assort.json")):
            continue
        qa = load(f"traders/{trader_id}/questassort.json") if os.path.exists(os.path.join(folder, "questassort.json")) else {}
        locked = {assort_id for group in qa.values() for assort_id in group}
        assort = load(f"traders/{trader_id}/assort.json")
        loyalty = assort.get("loyal_level_items", {})
        for item in assort.get("items", []):
            if item.get("parentId") == "hideout" and item["_id"] not in locked:
                open_sale[item["_tpl"]].add((loyalty.get(item["_id"], 1), trader_name(trader_id)))

    lines += ["", "## Guns and attachments", "",
              "Every gun and attachment a quest unlocks, hardest quest first. \"Also sold\" is where a trader sells it without",
              "any quest. Gun pricing goes by the easiest way to buy a part, so a part that's also sold at a low loyalty",
              "level is priced by that, however hard the quest is.", "",
              "| Score | Tier | Item | Unlocked by | Sold by (LL) | Also sold without a quest |", "|---|---|---|---|---|---|"]

    def hardest(tpl):
        return max(s for s, *_ in unlocked_by[tpl])

    for tpl in sorted(unlocked_by, key=lambda t: (-hardest(t), name(t))):
        for s, qid, trader_id, loyalty in sorted(unlocked_by[tpl], key=lambda u: -u[0]):
            extra = f" ({stats[qid]['event']})" if stats[qid]["event"] else ""
            also = ", ".join(f"{n} LL{lv}" for lv, n in sorted(open_sale.get(tpl, set()))) or "no"
            lines.append(f"| {s:.2f} | {tier(s)} | {name(tpl)} | {name(qid)}{extra} | {trader_name(trader_id)} ({loyalty}) | {also} |")

    with open(OUT, "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(lines) + "\n")
    if TSV:
        with open(TSV, "w", encoding="utf-8", newline="\n") as f:
            f.write("\n".join(tsv) + "\n")
    print(f"Wrote {OUT} ({len(order)} quests, {len(rows)} items)")


if __name__ == "__main__":
    main()
