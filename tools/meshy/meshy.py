#!/usr/bin/env python3
"""Generate scenery models with the owner's Meshy account (D-012).

The API key is injected by the environment's network-secret proxy, so this script
never reads or prints it. If MESHY_API_KEY is set (local use), it is sent instead.

Every generation is appended to the ledger with the actual balance before and after,
and the D-012 budget (500 credits, no further check-in) is enforced before each call.

Usage:
  python3 tools/meshy/meshy.py balance
  python3 tools/meshy/meshy.py generate --name barn --prompt "..." [--texture-prompt "..."] [--no-refine]
  python3 tools/meshy/meshy.py ledger
"""
import argparse
import datetime as dt
import json
import os
import pathlib
import sys
import time

import requests

API = "https://api.meshy.ai/openapi"
ROOT = pathlib.Path(__file__).resolve().parents[2]
LEDGER = ROOT / "art/scenery/meshy/ledger.json"
OUT = ROOT / "game/assets/scenery"
BUDGET = 500  # D-012: 300, raised by the owner after the 150 check-in
CHECKPOINT = 500  # owner authorised the full raised budget
DECISION = "D-012"


def headers():
    key = os.environ.get("MESHY_API_KEY")
    return {"Authorization": f"Bearer {key}"} if key else {}


def call(method, path, **kwargs):
    for attempt in range(4):
        try:
            r = requests.request(method, API + path, headers=headers(), timeout=60, **kwargs)
        except requests.RequestException:
            if attempt == 3:
                raise
            time.sleep(2 ** (attempt + 1))
            continue
        if r.status_code >= 500 and attempt < 3:
            time.sleep(2 ** (attempt + 1))
            continue
        if r.status_code >= 400:
            sys.exit(f"Meshy {method} {path} failed: HTTP {r.status_code} {r.text[:300]}")
        return r.json()


def balance():
    return int(call("GET", "/v1/balance")["balance"])


def load_ledger():
    if LEDGER.exists():
        return json.loads(LEDGER.read_text())
    return {"decision": DECISION, "budgetCredits": BUDGET, "checkpointCredits": CHECKPOINT, "entries": []}


def save_ledger(ledger):
    ledger["spentCredits"] = sum(e.get("chargedCredits", 0) for e in ledger["entries"])
    LEDGER.parent.mkdir(parents=True, exist_ok=True)
    LEDGER.write_text(json.dumps(ledger, indent=2) + "\n")


def wait(task_id, label):
    while True:
        task = call("GET", f"/v2/text-to-3d/{task_id}")
        status = task.get("status")
        if status == "SUCCEEDED":
            return task
        if status in ("FAILED", "CANCELED", "EXPIRED"):
            sys.exit(f"{label} {task_id} ended {status}: {task.get('task_error')}")
        print(f"  {label}: {status} {task.get('progress', 0)}%", flush=True)
        time.sleep(10)


def download(url, path):
    r = requests.get(url, timeout=300)
    r.raise_for_status()
    path.write_bytes(r.content)
    return len(r.content)


def generate(args):
    ledger = load_ledger()
    spent = sum(e.get("chargedCredits", 0) for e in ledger["entries"])
    if spent >= BUDGET:
        sys.exit(f"Budget exhausted: {spent}/{BUDGET} credits spent under {DECISION}.")
    if spent >= CHECKPOINT and not args.past_checkpoint:
        sys.exit(f"{spent} credits spent: owner check-in at {CHECKPOINT} is required (rerun with --past-checkpoint once approved).")

    before = balance()
    entry = {"name": args.name, "prompt": args.prompt, "createdAt": dt.datetime.now(dt.timezone.utc).isoformat(timespec="seconds"),
             "balanceBefore": before, "aiModel": args.model}
    ledger["entries"].append(entry)
    try:
        body = {"mode": "preview", "prompt": args.prompt, "art_style": "realistic", "ai_model": args.model,
                "topology": "triangle", "target_polycount": args.polycount, "should_remesh": True,
                "moderation": False}
        entry["previewTaskId"] = call("POST", "/v2/text-to-3d", json=body)["result"]
        print(f"{args.name}: preview {entry['previewTaskId']}", flush=True)
        task = wait(entry["previewTaskId"], "preview")
        if not args.no_refine:
            body = {"mode": "refine", "preview_task_id": entry["previewTaskId"], "enable_pbr": True}
            if args.texture_prompt:
                body["texture_prompt"] = args.texture_prompt
            entry["refineTaskId"] = call("POST", "/v2/text-to-3d", json=body)["result"]
            print(f"{args.name}: refine {entry['refineTaskId']}", flush=True)
            task = wait(entry["refineTaskId"], "refine")

        folder = OUT / args.name
        folder.mkdir(parents=True, exist_ok=True)
        entry["glbBytes"] = download(task["model_urls"]["glb"], folder / f"{args.name}.glb")
        if task.get("thumbnail_url"):
            download(task["thumbnail_url"], folder / "thumbnail.png")
        entry["files"] = [str((folder / f"{args.name}.glb").relative_to(ROOT))]
        entry["status"] = "downloaded"
    except SystemExit as failure:
        entry["status"] = f"failed: {failure}"
        raise
    finally:
        time.sleep(3)  # let the debit settle before reading the balance
        entry["balanceAfter"] = balance()
        entry["chargedCredits"] = before - entry["balanceAfter"]
        save_ledger(ledger)
        print(f"{args.name}: charged {entry['chargedCredits']} credits; balance {entry['balanceAfter']}; "
              f"{DECISION} spent {ledger['spentCredits']}/{BUDGET}", flush=True)


def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = p.add_subparsers(dest="command", required=True)
    sub.add_parser("balance")
    sub.add_parser("ledger")
    g = sub.add_parser("generate")
    g.add_argument("--name", required=True, help="asset folder name, e.g. barn")
    g.add_argument("--prompt", required=True)
    g.add_argument("--texture-prompt")
    g.add_argument("--model", default="latest")
    g.add_argument("--polycount", type=int, default=20000)
    g.add_argument("--no-refine", action="store_true", help="geometry only, no textures")
    g.add_argument("--past-checkpoint", action="store_true", help="owner approved spending past the check-in")
    args = p.parse_args()
    if args.command == "balance":
        print(balance())
    elif args.command == "ledger":
        print(json.dumps(load_ledger(), indent=2))
    else:
        generate(args)


if __name__ == "__main__":
    main()
