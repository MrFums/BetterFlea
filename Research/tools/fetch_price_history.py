"""Downloads tarkov.dev's flea price history (json.tarkov.dev/{regular,pve}/prices/{itemId}, "all flea market price
history for an item") for every base-game item and default gun build, into the download folder price_index.py uses.
Files already there are kept, so it can be stopped and run again. A few requests at a time, with a pause between.
    python -I Research/tools/fetch_price_history.py [download-folder]
"""
import concurrent.futures
import json
import os
import sys
import tempfile
import time
import urllib.error
import urllib.request

CACHE = sys.argv[1] if len(sys.argv) > 1 else os.path.join(tempfile.gettempdir(), "betterflea-tarkovdev")
WORKERS = 8
PAUSE = 0.05
AGENT = "BetterFlea-price-index/1.0 (+https://github.com/MrFums/BetterFlea)"


def ids(mode):
    with open(os.path.join(CACHE, f"{mode}-items.json"), encoding="utf-8") as f:
        return list(json.load(f)["data"]["items"])


def fetch(job):
    mode, tpl = job
    path = os.path.join(CACHE, "history", mode, f"{tpl}.json")
    if os.path.exists(path):
        return "kept"
    for attempt in range(4):
        try:
            request = urllib.request.Request(f"https://json.tarkov.dev/{mode}/prices/{tpl}", headers={"User-Agent": AGENT})
            with urllib.request.urlopen(request, timeout=60) as response:
                body = response.read()
            with open(path, "wb") as f:
                f.write(body)
            time.sleep(PAUSE)
            return "got"
        except urllib.error.HTTPError as ex:
            if ex.code == 404:
                with open(path, "w", encoding="utf-8") as f:
                    f.write('{"data": {}}')
                return "none"
            time.sleep(5 * (attempt + 1))
            error = ex
        except Exception as ex:
            time.sleep(5 * (attempt + 1))
            error = ex
    return f"failed: {error}"


def main():
    jobs = []
    for mode in ("regular", "pve"):
        os.makedirs(os.path.join(CACHE, "history", mode), exist_ok=True)
        jobs += [(mode, tpl) for tpl in ids(mode)]
    done, results = 0, {}
    with concurrent.futures.ThreadPoolExecutor(WORKERS) as pool:
        for result in pool.map(fetch, jobs):
            done += 1
            key = result.split(":")[0]
            results[key] = results.get(key, 0) + 1
            if done % 500 == 0:
                print(f"{done}/{len(jobs)} {results}", flush=True)
    print(f"done {done}/{len(jobs)} {results}", flush=True)


main()
