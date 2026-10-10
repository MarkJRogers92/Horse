#!/usr/bin/env python3
"""Validate the Cloverbrook handoff package and the coat support matrix.

Usage: python3 tools/validate_handoff.py   (from repo root; stdlib only)
Exit code 0 = all checks pass.
"""
import hashlib, json, sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
H = ROOT / "handoff"
errors, notes = [], []

def load(p):
    try:
        return json.loads(p.read_text(encoding="utf-8"))
    except Exception as e:
        errors.append(f"{p.relative_to(ROOT)}: JSON parse failed: {e}")
        return None

# 1. Manifest hashes (detects accidental edits to the baseline spec)
man = load(H / "PACKAGE_MANIFEST.json")
if man:
    for f in man["contents"]:
        p = H / f["file"]
        if not p.exists():
            errors.append(f"manifest file missing: {f['file']}")
        elif hashlib.sha256(p.read_bytes()).hexdigest() != f["sha256"]:
            notes.append(f"{f['file']} differs from manifest hash (edited since v1 - record a decision)")

# 2. Task board: ids unique, deps exist, acyclic, deps never point to a later phase
board = load(H / "TASK_BOARD.json")
if board:
    tasks = {t["id"]: t for t in board["tasks"]}
    if len(tasks) != len(board["tasks"]):
        errors.append("duplicate task ids")
    for t in tasks.values():
        for d in t.get("depends_on", []):
            if d not in tasks:
                errors.append(f"{t['id']} depends on unknown {d}")
            elif tasks[d]["phase"] > t["phase"]:
                errors.append(f"{t['id']} (phase {t['phase']}) depends on later-phase {d}")
    state = {}
    def visit(n, stack=()):
        if state.get(n) == 2: return
        if state.get(n) == 1:
            errors.append("dependency cycle: " + " -> ".join(stack + (n,))); return
        state[n] = 1
        for d in tasks[n].get("depends_on", []):
            if d in tasks: visit(d, stack + (n,))
        state[n] = 2
    for n in tasks: visit(n)
    notes.append(f"task board: {len(tasks)} tasks, statuses = "
                 + ", ".join(sorted({t['status'] for t in tasks.values()})))

# 3. Other JSON files parse
for name in ["CONTRACT_CATALOG.json", "GENETICS_FIXTURES.json", "ASSET_REGISTRY_TEMPLATE.json"]:
    load(H / name)

# 4. Coat support matrix: every class's layers exist; live classes need visual approval
m = load(ROOT / "docs/contracts/COAT_SUPPORT_MATRIX.json")
if m:
    layers = {l["id"] for l in m["layers"]}
    order = {s: i for i, s in enumerate(m["statusOrder"])}
    for c in m["coatClasses"]:
        for l in c["layers"]:
            if l not in layers:
                errors.append(f"coat class {c['id']} uses unknown layer {l}")
        for k in ("geneticStatus", "visualStatus", "releaseStatus"):
            if c[k] not in order:
                errors.append(f"coat class {c['id']} bad {k}: {c[k]}")
        # release status may never exceed the weaker of genetics and visuals
        if order.get(c["releaseStatus"], 0) > min(order.get(c["geneticStatus"], 0), order.get(c["visualStatus"], 0)):
            errors.append(f"coat class {c['id']} released beyond its genetic/visual support")
    notes.append(f"coat matrix: {len(m['coatClasses'])} classes over {len(layers)} render layers")

for n in notes: print("note:", n)
for e in errors: print("ERROR:", e)
print("OK" if not errors else f"FAILED ({len(errors)} errors)")
sys.exit(1 if errors else 0)
