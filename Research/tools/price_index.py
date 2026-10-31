"""Writes the Better Flea price index: one fixed price for every base-game item, used as the base price when
"Use Better Flea pricing" is on (configs/economy.jsonc). It doesn't move; the mod's own pricing (levels, multipliers,
demand, drift, Wipe Cycle) goes on top, the way it does with SPT's prices.

Sources, best first, all from tarkov.dev (PvP "regular" and PvE):
- The flea's 24 hour average in both modes. Close together: their geometric mean. Far apart: the one nearer to what
  traders say the item is worth (trader cash price, or trader sell price times what the flea pays over that for the
  same kind of item).
- Flea-banned items: the trader's cash price, then the cheapest barter (its items at their index prices), then the
  trader sell price times the flea's markup for that kind of item.
- Guns: the gun item is priced so its default build costs what that build sells for on the flea.
Never below the best trader sell price. Rounded to 3 significant figures.

Few listings are mostly the ones nobody buys, so a thin day counts near its cheapest listing, not the average. Weapons
and weapon parts without plenty of listings are held to what traders say they're worth: twice the trader's price or
markup at most, and a part that comes on a build a trader sells to twice its share of that build.

SPT's prices and the handbook are only shown next to it in the review, never used.

Writes Prices/price-index.json (embedded in the mod), Research/price-index.tsv (every item and where its price came
from) and Research/price-index-review.md (items where the sources disagree too much to be sure). Run with:
    python -I Research/tools/price_index.py [path-to-SPT_Data/database] [download-folder] [WTT-ContentBackport/db]
The download folder keeps tarkov.dev's files; delete it to fetch them again.
"""
import json
import math
import os
import statistics
import sys
import tempfile
import time
import urllib.request

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.join(HERE, "..", "..")
DB = sys.argv[1] if len(sys.argv) > 1 else "SPT_Runtime/SPT_Data/database/"
CACHE = sys.argv[2] if len(sys.argv) > 2 else os.path.join(tempfile.gettempdir(), "betterflea-tarkovdev")

FENCE = "579dc571d53a0658a154fbec"
AMMO = "5485a8684bdc2da71d8b4567"
AMMO_BOX = "543be5cb4bdc2deb348b4568"


def load(path):
    with open(os.path.join(DB, path), encoding="utf-8") as f:
        return json.load(f)


def fetch(name):
    os.makedirs(CACHE, exist_ok=True)
    path = os.path.join(CACHE, name.replace("/", "-") + ".json")
    if not os.path.exists(path):
        print(f"downloading {name}")
        request = urllib.request.Request(f"https://json.tarkov.dev/{name}",
                                         headers={"User-Agent": "BetterFlea-price-index/1.0 (+https://github.com/MrFums/BetterFlea)"})
        with urllib.request.urlopen(request, timeout=120) as response, open(path, "wb") as f:
            f.write(response.read())
    with open(path, encoding="utf-8") as f:
        return json.load(f)["data"]


ITEMS = load("templates/items.json")
LOCALE = load("locales/global/en.json")
HANDBOOK = {e["Id"]: e["Price"] for e in load("templates/handbook.json")["Items"]}
SPT_PRICES = load("templates/prices.json")
PRESETS = load("globals.json")["ItemPresets"]

# WTT ContentBackport adds most of the base-game items SPT 4.1.6 doesn't have yet. Its items are read from its own
# files (a clone of an existing item, with overrides), so they're priced like any other. Its own flea and handbook
# prices stand in for SPT's and the handbook's in the review.
BACKPORT_DB = sys.argv[3] if len(sys.argv) > 3 else "SPT_Runtime/user/mods/WTT-ContentBackport/db/"
BACKPORT = set()


def load_backport():
    found = {}
    for folder in ("CustomItems", "CustomQuestItems"):
        for path, _, files in os.walk(os.path.join(BACKPORT_DB, folder)):
            for file in files:
                if file.endswith((".json", ".jsonc")):
                    with open(os.path.join(path, file), encoding="utf-8-sig") as f:
                        found.update({tpl: v for tpl, v in json.load(f).items() if isinstance(v, dict) and "itemTplToClone" in v})
    for _ in range(3):
        for tpl, v in found.items():
            clone = ITEMS.get(v["itemTplToClone"])
            if tpl in ITEMS or clone is None:
                continue
            ITEMS[tpl] = {"_id": tpl, "_parent": v["parentId"], "_name": tpl,
                          "_props": {**clone.get("_props", {}), **(v.get("overrideProperties") or {})}}
            BACKPORT.add(tpl)
            LOCALE.setdefault(f"{tpl} Name", ((v.get("locales") or {}).get("en") or {}).get("name") or tpl)
            if v.get("handbookPriceRoubles"):
                HANDBOOK[tpl] = v["handbookPriceRoubles"]
            if v.get("fleaPriceRoubles"):
                SPT_PRICES[tpl] = v["fleaPriceRoubles"]
            for preset in v.get("weaponPresets") or []:
                PRESETS.setdefault(preset["_id"], preset)


if os.path.isdir(BACKPORT_DB):
    load_backport()
else:
    print(f"WTT ContentBackport not found at {BACKPORT_DB}: its items are left out")

PVP = fetch("regular/items")["items"]
PVE = fetch("pve/items")["items"]
BARTERS = fetch("regular/barters") + fetch("pve/barters")


def name(tpl):
    return " ".join((LOCALE.get(f"{tpl} Name") or ITEMS.get(tpl, {}).get("_name") or tpl).split())


def kind(tpl):
    parent = ITEMS.get(tpl, {}).get("_parent")
    return parent


def kind_name(tpl):
    parent = kind(tpl)
    return name(parent) if parent else "?"


def is_ammo(tpl):
    return kind(tpl) == AMMO


def round3(value):
    if value <= 0:
        return 0
    if value < 1000:
        return max(1, round(value))
    step = 10 ** (int(math.floor(math.log10(value))) - 2)
    return int(round(value / step) * step)


def log_gap(a, b):
    return abs(math.log(a / b))


def is_preset(entry):
    return "preset" in (entry.get("types") or [])


def is_gun(entry):
    return "gun" in (entry.get("types") or [])


def trader_offers(tpl):
    return [offer for mode in (PVP, PVE) for offer in (mode.get(tpl) or {}).get("buyFromTrader") or [] if offer.get("priceRUB")]


def trader_buy(tpl):
    prices = [offer["priceRUB"] for offer in trader_offers(tpl)]
    return min(prices) if prices else None


# How much of the flea's price over a trader's cash price an item keeps, by how hard it is to buy from the trader:
# the same shares Better Flea's gun pricing uses (prices.jsonc, Gun prices). Loyalty 3 keeps a fifth, loyalty 4 half;
# a quest lock keeps as much as its difficulty says (Research/quest-unlock-difficulty.md: medium a fifth, hard half,
# very hard 90%); a hidden trader is at least hard (BTR Driver) or very hard (Lightkeeper). An item several traders
# sell goes by the easiest one to buy from.
LOYALTY_SHARE = {1: 0, 2: 0, 3: 0.2, 4: 0.5}
TIER_SHARE = [(0.75, 0.9), (0.55, 0.5), (0.35, 0.2), (0, 0)]
HIDDEN_TRADER = {"656f0f98d80a697f855d34b1": 0.55, "638f541a29ffd1183d187f57": 0.75}
QUEST_SCORE = {}
_QUESTS = os.path.join(CACHE, "quest-difficulty.tsv")
if os.path.exists(_QUESTS):
    with open(_QUESTS, encoding="utf-8") as f:
        next(f)
        for line in f:
            cells = line.rstrip("\n").split("\t")
            QUEST_SCORE[cells[0]] = float(cells[3])
else:
    print(f"No quest scores at {_QUESTS}: run quest_difficulty.py with it as the TSV output. Quest locks count as easy.")


def tier_share(score):
    return next(share for floor, share in TIER_SHARE if score >= floor)


def unlock_share(tpl):
    shares = []
    for offer in trader_offers(tpl):
        score = max(QUEST_SCORE.get(offer.get("taskUnlock") or "", 0), HIDDEN_TRADER.get(offer.get("trader"), 0))
        shares.append(max(LOYALTY_SHARE.get(offer.get("minTraderLevel") or 1, 0), tier_share(score)))
    return min(shares) if shares else None


# What traders pay for it. Fence pays a fraction of what the rest do, so estimates leave him out (a Nut can only Fence
# buys, for 123, isn't worth 172); the floor counts him.
def trader_sell(tpl, fence=True):
    prices = [offer["priceRUB"] for mode in (PVP, PVE) for offer in (mode.get(tpl) or {}).get("sellToTrader") or []
              if offer.get("priceRUB") and (fence or offer.get("trader") != FENCE)]
    return max(prices) if prices else None


# Flea prices come from tarkov.dev's price history (fetch_price_history.py), not one day's average: over the last
# HISTORY_DAYS, each day's median reading, then the median of those days. Only readings backed by MIN_LISTINGS or more
# listings count; an item needs GOOD_DAYS such days for a solid price. With fewer, its thin readings are used, as long
# as there are THIN_DAYS of them, and they're checked against traders below. Without history, the 24 hour average
# stands in, if it had enough listings.
MIN_LISTINGS = 3
HISTORY_DAYS = 365
GOOD_DAYS = 14
THIN_DAYS = 5
# A reading's price is the average of what's listed, and with only a few listings that's mostly the ones nobody buys
# (an AK-545 Mk.3 handguard: one listing at 99,999 for weeks, while the few at 40-50k sold). Buyers take the cheapest.
# Below DEEP listings a reading counts for no more than its cheapest listing times CHEAPEST_TO_AVERAGE, which is how far
# the average sits over the cheapest where there are plenty of listings (about 3/4), so a thin item and a well-listed
# one come out on the same scale. Ammo is left alone: it sells by the round, and its cheapest is often a few odd ones.
DEEP = 10
CHEAPEST_TO_AVERAGE = 4 / 3


def reading(p):
    if (p.get("offerCount") or 0) >= DEEP:
        return p["price"]
    return min(p["price"], (p.get("priceMin") or p["price"]) * CHEAPEST_TO_AVERAGE)



# The year runs back from when tarkov.dev's files were downloaded, not from today, so a rebuild on another day gives the
# same prices.
NOW_MS = os.path.getmtime(os.path.join(CACHE, "regular-items.json")) * 1000 if os.path.exists(os.path.join(CACHE, "regular-items.json")) else time.time() * 1000
_HISTORY = {}


def history(mode, tpl):
    key = (mode, tpl)
    if key not in _HISTORY:
        _HISTORY[key] = summarise(os.path.join(CACHE, "history", mode, f"{tpl}.json"), kind(tpl) != AMMO)
    return _HISTORY[key]


def summarise(path, cheapest=True):
    if not os.path.exists(path):
        return None
    with open(path, encoding="utf-8") as f:
        data = json.load(f)["data"]
    points = next((v for v in data.values() if isinstance(v, list)), []) if isinstance(data, dict) else data
    recent = [p for p in points if p.get("price") and p["timestamp"] >= NOW_MS - HISTORY_DAYS * 864e5]
    if not recent:
        return None

    def daily(readings):
        days = {}
        for p in readings:
            days.setdefault(int(p["timestamp"] // 864e5), []).append(p)
        return [(statistics.median(reading(p) if cheapest else p["price"] for p in ps),
                 statistics.median(p.get("priceMin") or p["price"] for p in ps), statistics.median(p["price"] for p in ps))
                for _, ps in sorted(days.items())]

    good = daily([p for p in recent if (p.get("offerCount") or 0) >= MIN_LISTINGS])
    days = good if len(good) >= GOOD_DAYS else daily(recent)
    if len(days) < (GOOD_DAYS if days is good else THIN_DAYS):
        return None
    prices = sorted(d[0] for d in days)
    last30 = [d[0] for d in days[-30:]]
    return {"price": statistics.median(prices), "low": statistics.median(d[1] for d in days),
            "p25": prices[len(prices) // 4], "p75": prices[len(prices) * 3 // 4], "p80": prices[int(len(prices) * 0.8)],
            "recent": statistics.median(last30), "days": len(days), "good": days is good,
            "depth": statistics.median((p.get("offerCount") or 0) for p in recent), "average": statistics.median(d[2] for d in days)}


_MODE = {}
for _tpl, _entry in PVP.items():
    _MODE[id(_entry)] = ("regular", _tpl)
for _tpl, _entry in PVE.items():
    _MODE[id(_entry)] = ("pve", _tpl)


def banned(entry):
    return "noFlea" in (entry.get("types") or [])


# Flea-banned in that mode now: its old prices are from before the ban (a THICC item case at 14.3M on PvE), so they
# don't count.
def solid(entry):
    if not entry or banned(entry):
        return None
    h = history(*_MODE[id(entry)])
    if h:
        return h["price"] if h["good"] else None
    return entry.get("avg24hPrice") if (entry.get("lastOfferCount") or 0) >= MIN_LISTINGS else None


flea = solid


# The plain average of what was listed, as prices were worked out before the cheapest-listing rule above.
def average(entry):
    h = history(*_MODE[id(entry)]) if entry else None
    return h["average"] if h and h["good"] else solid(entry)


# Plenty of listings on a typical day: its price stands as the market says, however far from what traders pay.
def deep(entry):
    if not entry:
        return False
    h = history(*_MODE[id(entry)])
    return (h["depth"] if h else entry.get("lastOfferCount") or 0) >= DEEP


# Weapons, weapon parts and builds. What they're worth follows what traders pay for them, so a thin price far over that
# is somebody's bait. Keys, collectibles and barter goods are worth what collectors pay, so those are left to the
# looser checks further down.
WEAPON_PART = "5448fe124bdc2da5018b4567"
WEAPON_KINDS = {WEAPON_PART, "5422acb9af1c889c16000029"}
# A weapon item without plenty of listings counts for no more than this times what traders say it's worth. Parts with
# plenty of listings sit under 2.5x of it nine times in ten.
WEAPON_CAP = 2


def ancestors(tpl):
    seen = []
    while tpl and tpl in ITEMS:
        tpl = ITEMS[tpl].get("_parent")
        seen.append(tpl)
    return seen


def checked(tpl, entry):
    return is_preset(entry or {}) or bool(WEAPON_KINDS & set(ancestors(tpl)))


BASE = [tpl for tpl, entry in PVP.items() if not is_preset(entry) and tpl in ITEMS]
BASE += [tpl for tpl, entry in PVE.items() if not is_preset(entry) and tpl in ITEMS and tpl not in PVP]

# How far over the best trader sell price the flea pays, per kind of item (median of items with both), from the plain
# average.
markups = {}
for tpl in BASE:
    sell = trader_sell(tpl, fence=False)
    prices = [average(e) for e in (PVP.get(tpl), PVE.get(tpl)) if e and flea(e)]
    if sell and prices:
        markups.setdefault(kind(tpl), []).append(prices[0] / sell)
ALL_MARKUP = statistics.median([m for ms in markups.values() for m in ms])
MARKUP = {k: statistics.median(ms) for k, ms in markups.items() if len(ms) >= 5}
# Weapon kinds from items with plenty of listings only: the rest are mostly bait, and counting them gave weapon parts
# like triggers and hammers 34 times the trader sell price, so a 30,000 HK USP trigger passed as fair. A kind with too
# few goes by all weapon items with plenty of listings.
weapon_markups = {}
for tpl in BASE:
    sell = trader_sell(tpl, fence=False)
    entries = [e for e in (PVP.get(tpl), PVE.get(tpl)) if e and flea(e) and deep(e)]
    if sell and entries and checked(tpl, entries[0]):
        weapon_markups.setdefault(kind(tpl), []).append(flea(entries[0]) / sell)
WEAPON_MARKUP = statistics.median([m for ms in weapon_markups.values() for m in ms])
for _kind in {kind(tpl) for tpl in BASE if checked(tpl, PVP.get(tpl) or PVE.get(tpl))}:
    _ms = weapon_markups.get(_kind, [])
    MARKUP[_kind] = statistics.median(_ms) if len(_ms) >= 5 else WEAPON_MARKUP


def markup(tpl):
    return MARKUP.get(kind(tpl), ALL_MARKUP)


def anchor(tpl):
    buy = trader_buy(tpl)
    if buy:
        return buy, "trader price"
    sell = trader_sell(tpl, fence=False)
    if sell:
        return sell * markup(tpl), "trader sell price x markup"
    return None, None


# From here on, thin prices (too few well-listed days) count only if they aren't more than 3x what traders say the
# item is worth (their price, or their sell price times the markup). Those are somebody's bait: an 18,888 NSPU-M
# eyecup Prapor sells for 355. A cheap one stands; bait is never cheap. With nothing from traders, it counts only if
# the other mode's agrees (within 2x). Rounds are left alone. The markups above came from solid prices only.
# Weapon items without plenty of listings are checked the same way even when their days are solid, and more tightly
# (WEAPON_CAP): enough listed days doesn't make one listing at 99,999 a price. One a trader sells counts up to
# WEAPON_CAP times the trader's price, and the trader-price rule further down takes most of what's over.
LOW_LISTINGS = {}
ACCEPTED = {}
for _mode, _other in ((PVP, PVE), (PVE, PVP)):
    for _tpl, _entry in _mode.items():
        _solid = solid(_entry)
        _weapon = checked(_tpl, _entry) and not deep(_entry)
        if _solid and _weapon and trader_buy(_tpl):
            ACCEPTED[id(_entry)] = min(_solid, trader_buy(_tpl) * WEAPON_CAP)
            continue
        if _solid and not _weapon:
            ACCEPTED[id(_entry)] = _solid
            continue
        _h = history(*_MODE[id(_entry)])
        _price = _solid or (_h["price"] if _h else _entry.get("avg24hPrice"))
        if not _price or banned(_entry):
            continue
        if kind(_tpl) == AMMO:
            ACCEPTED[id(_entry)] = _price
            continue
        _target, _ = anchor(_tpl)
        _other_entry = _other.get(_tpl)
        # The two modes compared on their plain averages: the cheapest-listing rule can move them apart.
        _twin = (history(*_MODE[id(_other_entry)]) or {}).get("average") if _other_entry else None
        _plain = _h["average"] if _h else _price
        if (_price <= _target * (WEAPON_CAP if _weapon else 3)) if _target else bool(_twin and max(_plain, _twin) / min(_plain, _twin) <= 2):
            ACCEPTED[id(_entry)] = _price
        else:
            LOW_LISTINGS.setdefault(_tpl, []).append(_price)


def flea(entry):
    return ACCEPTED.get(id(entry)) if entry else None


rows = {}


def record(tpl, price, source, note="", review=None):
    rows[tpl] = {"price": round3(price), "source": source, "note": note, "review": review}


# PvP's flea is the price. PvE's is thrown off too often (a 30,000 AK-105 muzzle brake that sells for 300 on PvP), so
# it's only used for items PvP has no price for. Keys are the exception: in PvE a key's rooms can be looted without
# getting killed for it, so its value is safer to count on there. A key gets the geometric mean of the two.
KEY_KINDS = {"5c99f98d86f7745c314214b3", "5c164d2286f774194c5e69fa"}


def price_from_flea(tpl, pvp, pve):
    if pvp and pve and kind(tpl) in KEY_KINDS:
        return math.sqrt(pvp * pve), "flea, PvP and PvE (key)", None
    price, mode = (pvp, "PvP") if pvp else (pve, "PvE only")
    target, _ = anchor(tpl)
    # Judged on the plain average too, so the cheapest-listing rule can't bring a price just under the line and skip
    # the review it got before. Weapon items by the tighter WEAPON_CAP.
    gap = log_gap(max(price, average(PVE.get(tpl)) or 0), target) if target else 0
    review = "PvE price only, far from what traders say" if not pvp and target and price >= 5000 and gap > math.log(WEAPON_CAP if checked(tpl, PVE.get(tpl)) else 5) else None
    if not pvp and not target and price >= 100000:
        review = "PvE price only, nothing from traders to check against"
    return price, f"flea, {mode}", review


# Offers seen in the last scan, in both modes together.
def offers(tpl):
    return sum((mode.get(tpl) or {}).get("lastOfferCount") or 0 for mode in (PVP, PVE))


# Loose items first; guns need their parts' prices.
guns = []
for tpl in BASE:
    pvp_entry, pve_entry = PVP.get(tpl), PVE.get(tpl)
    if is_gun(pvp_entry or pve_entry):
        guns.append(tpl)
        continue
    pvp, pve = flea(pvp_entry), flea(pve_entry)
    if pvp or pve:
        price, source, review = price_from_flea(tpl, pvp, pve)
        record(tpl, price, source, review=review)
    elif trader_buy(tpl):
        record(tpl, trader_buy(tpl), "trader price (flea-banned)")

# Barters, priced by what they cost, for flea-banned items with no trader cash price. Twice, since a barter can
# need another barter-only item.
for _ in range(2):
    for barter in BARTERS:
        tpl = barter["offeredItem"]["item"]
        if tpl not in ITEMS or tpl in rows and not rows[tpl]["source"].startswith("barter") or tpl not in PVP and tpl not in PVE:
            continue
        if is_gun(PVP.get(tpl) or PVE.get(tpl)) or is_preset(PVP.get(tpl) or PVE.get(tpl) or {}):
            continue
        needs = barter["requiredItems"]
        if not all(need["item"] in rows for need in needs):
            continue
        cost = sum(rows[need["item"]]["price"] * need["count"] for need in needs) / max(1, barter["offeredItem"]["count"])
        if cost > 0 and (tpl not in rows or cost < rows[tpl]["price"]):
            record(tpl, cost, "barter cost (flea-banned)")

# A barter can be a deliberately bad deal (Lightkeeper's microcontroller board takes a GPU and 3 Virtex processors,
# 1.26M, for an item that sold for about 80k). Over 5x what traders pay for it times the markup, that's used instead.
# Below that the barter is how people really get it (a THICC item case, about 10M).
for tpl, row in rows.items():
    sell = trader_sell(tpl, fence=False)
    if row["source"].startswith("barter") and sell and sell * markup(tpl) * 5 < row["price"]:
        record(tpl, sell * markup(tpl), f"trader sell price x {markup(tpl):.1f} (flea-banned, its barter costs far more)")

for tpl in BASE:
    if tpl in rows or tpl in guns:
        continue
    sell = trader_sell(tpl, fence=False)
    if sell:
        why = "too few listings, far over what traders say" if tpl in LOW_LISTINGS else "flea-banned"
        record(tpl, sell * markup(tpl), f"trader sell price x {markup(tpl):.1f} ({why})")

# Ammo packs: their rounds. Packs are rarely listed and their averages are mostly a few odd listings.
for tpl, row in rows.items():
    if kind(tpl) != AMMO_BOX:
        continue
    slot = (ITEMS[tpl]["_props"].get("StackSlots") or [{}])[0]
    rounds = (slot.get("_props", {}).get("filters") or [{}])[0].get("Filter") or []
    if rounds and rounds[0] in rows and slot.get("_max_count"):
        value = rows[rounds[0]]["price"] * slot["_max_count"]
        row["note"] = f"flea {row['price']:,}" if row["source"].startswith("flea") else ""
        row["price"], row["source"], row["review"] = round3(value), f"its {slot['_max_count']} rounds", None

# Over a trader's cash price, an item keeps only its unlock share of the difference: anyone who can buy it from the
# trader does. Guns are left alone here, since Better Flea's gun pricing does this to them itself.
for tpl, row in rows.items():
    share, buy = unlock_share(tpl), trader_buy(tpl)
    if share is not None and buy and row["price"] > buy and row["source"].startswith("flea") and tpl not in guns:
        row["note"] = f"flea {row['price']:,}, keeps {share:.0%} over the trader price"
        row["price"] = round3(buy + (row["price"] - buy) * share)
    sell = trader_sell(tpl)
    if sell and row["price"] < sell:
        row["note"] = (row["note"] + "; " if row["note"] else "") + "raised to the trader sell price"
        row["price"] = round3(sell)

# A weapon part that comes on a build a trader sells can be had by buying that build and taking it off, so without
# plenty of listings it's worth no more than twice its share of the build (by handbook price) of the cheapest such
# build. Stock parts nobody lists on their own (a Vityaz metal skeleton stock at 10,000) come down to what the gun
# they come on says they're worth.
BUILD_SHARE = {}
for _entry in list(PVP.values()) + list(PVE.values()):
    _offers = [o["priceRUB"] for o in _entry.get("buyFromTrader") or [] if o.get("priceRUB")]
    if not is_preset(_entry) or not _offers:
        continue
    _parts = [c["item"] for c in _entry.get("containsItems") or [] if c["item"] in rows]
    _total = sum(HANDBOOK.get(p, 0) for p in _parts)
    for _p in _parts:
        if _total and HANDBOOK.get(_p) and WEAPON_PART in ancestors(_p):
            BUILD_SHARE[_p] = min(BUILD_SHARE.get(_p, math.inf), min(_offers) * HANDBOOK[_p] / _total)
for tpl, share in BUILD_SHARE.items():
    row = rows[tpl]
    if not deep(PVP.get(tpl)) and not deep(PVE.get(tpl)) and row["price"] > share * 2:
        row["note"] = (row["note"] + "; " if row["note"] else "") + f"was {row['price']:,}, over twice its share of a trader's build"
        row["price"] = round3(max(share * 2, trader_sell(tpl) or 0))

# Guns: the default build's flea price, less its parts.
DEFAULT_PRESET = {p["_encyclopedia"]: pid for pid, p in PRESETS.items() if p.get("_encyclopedia")}
# ContentBackport's presets don't all say which is the default: its first unnamed one for that gun.
for pid, p in PRESETS.items():
    root = next((item["_tpl"] for item in p["_items"] if item["_id"] == p.get("_parent")), None)
    if root in BACKPORT and not p.get("_changeWeaponName"):
        DEFAULT_PRESET.setdefault(root, pid)


def build_parts(preset_id):
    preset = PRESETS[preset_id]
    root = preset.get("_parent")
    return [item["_tpl"] for item in preset["_items"] if item["_id"] != root and not is_ammo(item["_tpl"])]


for tpl in guns:
    pvp_entry, pve_entry = PVP.get(tpl), PVE.get(tpl)
    sell = trader_sell(tpl, fence=False) or 0
    preset_id = DEFAULT_PRESET.get(tpl)
    low = [p for p in ((history(*_MODE[id(e)]) or {}).get("low") or e.get("low24hPrice") for e in (pvp_entry, pve_entry) if e and flea(e)) if p]
    fallback = low[0] if low else trader_buy(tpl) or sell * markup(tpl)
    if preset_id:
        parts = build_parts(preset_id)
        missing = [p for p in parts if p not in rows]
        build_pvp, build_pve = flea(PVP.get(preset_id)), flea(PVE.get(preset_id))
        if (build_pvp or build_pve) and not missing:
            build, source, review = price_from_flea(preset_id, build_pvp, build_pve)
            parts_cost = sum(rows[p]["price"] for p in parts)
            gun = build - parts_cost
            floor = max(sell, build * 0.1)
            if gun < floor:
                record(tpl, max(floor, min(fallback or floor, build)), "gun: cheapest listing (default build's parts cost more than the build)",
                       f"build {round3(build)}, parts {round3(parts_cost)}")
            else:
                record(tpl, gun, f"gun: default build on the {source.split(', ', 1)[1]} less its parts",
                       f"build {round3(build)}, parts {round3(parts_cost)}", review)
            continue
    if low:
        record(tpl, max(fallback, sell), "gun: cheapest listing (no default build price)")
    elif fallback:
        record(tpl, max(fallback, sell), "gun: flea-banned", review="flea-banned gun")

# Unsure: the flea modes disagree with nothing to settle it, a single flea price is far from what traders say, or a
# flea-banned gun. These are listed for review and priced from the market: PvP flea, PvE flea and the trader's cash
# price count in full, the trader sell price times its markup at half. Three or more: their median. Fewer: their
# geometric mean, with the handbook in at a fifth of a market price's weight, so it can nudge but never decide.
# SPT's price isn't used at all: it's wrong too often.
HANDBOOK_WEIGHT = 0.2
# Possibly rare: both modes list it far above what traders say, but from only one or two listings each.
for tpl, prices in LOW_LISTINGS.items():
    if tpl in rows and not rows[tpl]["review"] and len(prices) == 2 and max(prices) / min(prices) <= 3 and min(prices) >= 5000:
        rows[tpl]["review"] = "one or two listings in each mode, both far above what traders say"
for tpl, row in rows.items():
    if not row["review"]:
        continue
    target, why = anchor(tpl)
    # A flea price from a listing or two counts for little more than the handbook.
    market = [(flea(mode.get(tpl)), min(1.0, max(HANDBOOK_WEIGHT, ((mode.get(tpl) or {}).get("lastOfferCount") or 0) / 5)))
              for mode in (PVP, PVE) if flea(mode.get(tpl))][:1]
    if target:
        market.append((target, 1.0 if why == "trader price" else 0.5))
    if len(market) >= 3 and all(w == 1.0 for _, w in market):
        price = statistics.median(p for p, _ in market)
    else:
        book = HANDBOOK.get(tpl)
        weighted = market + ([(book, HANDBOOK_WEIGHT)] if book else [])
        if not weighted:
            continue
        price = math.exp(sum(math.log(p) * w for p, w in weighted) / sum(w for _, w in weighted))
        # Flea-banned: nothing on the flea to say it's worth less than traders do, so the handbook can't pull it under.
        if target and not flea(PVP.get(tpl)) and not flea(PVE.get(tpl)):
            price = max(price, target)
    row["index"] = row["price"]
    row["price"] = round3(price)

# Armour, helmets and anything else that comes with parts: the flea and traders price the whole thing, but in SPT
# its plates, visors and other removable parts are items of their own, priced on top. Those come off at their own
# price. Built-in soft armour stays in: SPT and Better Flea don't price it, the armour's own price covers it.
BUILT_IN = "65649eb40bf0ed77b8044453"
for tpl, preset_id in DEFAULT_PRESET.items():
    if tpl not in rows or tpl in guns:
        continue
    preset = PRESETS[preset_id]
    parts = [item["_tpl"] for item in preset["_items"] if item["_id"] != preset.get("_parent") and kind(item["_tpl"]) != BUILT_IN]
    priced = [p for p in parts if p in rows]
    if not priced:
        continue
    whole = rows[tpl]["price"]
    parts_cost = sum(rows[p]["price"] for p in priced)
    # Parts worth most of the price: it's sold without them (plate carriers mostly are), so it stands as it is.
    if parts_cost >= whole * 0.75:
        continue
    rows[tpl]["note"] = f"whole {whole:,}, less its parts" + (f"; {rows[tpl]['note']}" if rows[tpl]["note"] else "")
    rows[tpl]["price"] = round3(whole - parts_cost)

OVERRIDES = os.path.join(HERE, "price-index-overrides.json")
if os.path.exists(OVERRIDES):
    with open(OVERRIDES, encoding="utf-8") as f:
        overrides = json.load(f)
    for tpl, price in overrides.items():
        if not tpl.startswith("_") and tpl in rows:
            rows[tpl].update(price=round3(price), source="set by hand", review=None)
    # Checked by hand and the rules' price kept: off the review list.
    for tpl in overrides.get("_keep", {}):
        if tpl in rows and rows[tpl]["review"]:
            rows[tpl]["review"] = None
            rows[tpl]["note"] = (rows[tpl]["note"] + "; " if rows[tpl]["note"] else "") + "checked by hand"

# Output.
prices ={tpl: row["price"] for tpl, row in sorted(rows.items()) if row["price"] > 0}
known = sorted(tpl for tpl in set(PVP) | set(PVE) if not is_preset(PVP.get(tpl) or PVE.get(tpl)))
levels = {tpl: e["minLevelForFlea"] for tpl, e in sorted(PVP.items()) if (e.get("minLevelForFlea") or 0) > 0 and not is_preset(e)}
backport = sorted(tpl for tpl in prices if tpl in BACKPORT)
# What each trader keeps of an item's base price when buying it from a player, in the live game (tarkov.dev's sell
# prices over base prices, median over every item). SPT's traders pay 10 to 16 points more (Therapist 63%, live 51%).
# Stored as SPT's buy_price_coef: the cut the trader takes, in percent.
_kept = {}
for entry in PVP.values():
    base = entry.get("basePrice") or 0
    if base >= 1000:
        for offer in entry.get("sellToTrader") or []:
            if offer.get("priceRUB") and offer.get("trader"):
                _kept.setdefault(offer["trader"], []).append(offer["priceRUB"] / base)
trader_cuts = {trader: round(100 - statistics.median(kept) * 100) for trader, kept in sorted(_kept.items()) if len(kept) >= 50}
# Ref is the one trader who pays more at higher loyalty levels (live 40%, 45%, 45%, 50%; 0.16.9 and SPT 50%, 56%, 56%,
# 62%). tarkov.dev only has his level 1 price, so the steps come from the wiki's Trading page (August 2026, patch 1.1).
REF = "6617beeaa9cfa777ca915b7c"
trader_level_cuts = {}
if REF in trader_cuts:
    trader_level_cuts[REF] = [trader_cuts[REF], trader_cuts[REF] - 5, trader_cuts[REF] - 5, trader_cuts[REF] - 10]

index = {"Modes": ["betterflea"], "Fetched": int(time.time()), "Prices": prices, "Known": known, "Levels": levels,
         "Backport": backport, "TraderCuts": trader_cuts, "TraderLevelCuts": trader_level_cuts}
print("trader cuts", trader_cuts, "by level", trader_level_cuts)
with open(os.path.join(ROOT, "Prices", "price-index.json"), "w", encoding="utf-8", newline="\n") as f:
    json.dump(index, f, separators=(",", ":"))


def fmt(value):
    return f"{round3(value):,}" if value else ""


with open(os.path.join(HERE, "..", "price-index.tsv"), "w", encoding="utf-8", newline="\n") as f:
    f.write("id\titem\tkind\tWTT ContentBackport\tindex\tsource\tPvP flea\tPvE flea\ttrader price\tbest trader sell\tSPT price\thandbook\tnote\n")
    for tpl, row in sorted(rows.items(), key=lambda kv: (kind_name(kv[0]), name(kv[0]))):
        f.write("\t".join(str(x) for x in [tpl, name(tpl), kind_name(tpl), "yes" if tpl in BACKPORT else "", row["price"], row["source"],
                                          flea(PVP.get(tpl)) or "", flea(PVE.get(tpl)) or "", trader_buy(tpl) or "",
                                          trader_sell(tpl) or "", round(SPT_PRICES.get(tpl) or 0) or "", HANDBOOK.get(tpl, ""),
                                          row["note"]]) + "\n")

def apart(tpl):
    pvp, pve = flea(PVP.get(tpl)), flea(PVE.get(tpl))
    return f"{max(pvp, pve) / min(pvp, pve):.1f}x" if pvp and pve else ""


review = sorted(((tpl, row) for tpl, row in rows.items() if row["review"]), key=lambda kv: (kv[1]["review"], -kv[1]["price"]))
with open(os.path.join(HERE, "..", "price-index-review.md"), "w", encoding="utf-8", newline="\n") as f:
    f.write("# Price index: items to review\n\n")
    f.write(f"{len(review)} items where the sources disagree too much to be sure. Each is priced from the market: "
            "the PvP flea, PvE flea and trader prices (the median of them when there are three, otherwise their "
            "average, with the handbook counting for a fifth of a market price so it can only nudge). **SPT's price "
            "isn't used**; it's shown for comparison only. \"First pick\" is what the normal rules chose before that. "
            "For items marked (ContentBackport), the SPT and Handbook columns are WTT ContentBackport's own prices. "
            "To set a price by hand, add it to `Research/tools/price-index-overrides.json` and run `price_index.py` "
            "again.\n\n")
    reason = None
    for tpl, row in review:
        if row["review"] != reason:
            reason = row["review"]
            f.write(f"\n## {reason[0].upper() + reason[1:]}\n\n")
            f.write("| Item | Kind | PvP flea | PvE flea | Apart | Trader | SPT (not used) | Handbook | First pick | **Index** |\n"
                    "|---|---|--:|--:|--:|--:|--:|--:|--:|--:|\n")
        f.write(f"| {name(tpl)}{' (ContentBackport)' if tpl in BACKPORT else ''} | {kind_name(tpl)} | {fmt(flea(PVP.get(tpl)))} | {fmt(flea(PVE.get(tpl)))} | {apart(tpl)} | "
                f"{fmt(trader_buy(tpl))} | {fmt(SPT_PRICES.get(tpl))} | {fmt(HANDBOOK.get(tpl))} | "
                f"{fmt(row.get('index'))} | **{fmt(row['price'])}** |\n")

with open(os.path.join(HERE, "..", "price-history.tsv"), "w", encoding="utf-8", newline="\n") as f:
    f.write("id\titem\tkind\tWTT ContentBackport\t"
            + "\t".join(f"{m} {c}" for m in ("PvP", "PvE") for c in ("year median", "25%", "75%", "last 30 days", "days", "solid"))
            + "\tindex\n")
    for tpl in sorted(set(BASE) | set(guns), key=lambda t: (kind_name(t), name(t))):
        cells = []
        for mode in ("regular", "pve"):
            h = history(mode, tpl) if tpl in (PVP if mode == "regular" else PVE) else None
            cells += [round(h[k]) for k in ("price", "p25", "p75", "recent")] + [h["days"], "yes" if h["good"] else "thin"] if h else [""] * 6
        f.write("\t".join(str(x) for x in [tpl, name(tpl), kind_name(tpl), "yes" if tpl in BACKPORT else ""] + cells
                          + [rows[tpl]["price"] if tpl in rows else ""]) + "\n")

details = []
for tpl, row in review:
    target, why = anchor(tpl)
    details.append({"id": tpl, "name": name(tpl), "kind": kind_name(tpl), "reason": row["review"], "backport": tpl in BACKPORT,
                    "pvp": flea(PVP.get(tpl)), "pve": flea(PVE.get(tpl)),
                    "pvpRaw": (history("regular", tpl) or {}).get("price") or (PVP.get(tpl) or {}).get("avg24hPrice"),
                    "pveRaw": (history("pve", tpl) or {}).get("price") or (PVE.get(tpl) or {}).get("avg24hPrice"),
                    "pvpDays": (history("regular", tpl) or {}).get("days") or 0, "pveDays": (history("pve", tpl) or {}).get("days") or 0,
                    "pvpOffers": (PVP.get(tpl) or {}).get("lastOfferCount") or 0, "pveOffers": (PVE.get(tpl) or {}).get("lastOfferCount") or 0,
                    "trader": trader_buy(tpl), "sell": trader_sell(tpl), "anchor": target, "anchorKind": why,
                    "spt": SPT_PRICES.get(tpl), "handbook": HANDBOOK.get(tpl), "firstPick": row.get("index"), "index": row["price"]})
with open(os.path.join(CACHE, "review-details.json"), "w", encoding="utf-8") as f:
    json.dump(details, f)

STATS = {
    "items": len(rows),
    "known": len(known),
    "sources": {},
    "review": sum(1 for r in rows.values() if r["review"]),
    "backport": len(backport),
    "markup": ALL_MARKUP,
}
for row in rows.values():
    key = row["source"].split(" (")[0].split(": ")[0] if row["source"].startswith("gun") else row["source"].split(" (")[0]
    key = "trader sell price x markup" if key.startswith("trader sell price x") else key
    STATS["sources"][key] = STATS["sources"].get(key, 0) + 1
json.dump({"rows": rows, "stats": STATS}, open(os.path.join(CACHE, "index-rows.json"), "w", encoding="utf-8"))
print(json.dumps(STATS, indent=1))
