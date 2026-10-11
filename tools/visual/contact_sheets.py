#!/usr/bin/env python3
"""Assemble labelled contact sheets + a Markdown summary from capture_report.json.

    python3 tools/visual/contact_sheets.py evidence/

Writes evidence/sheets/*.png and evidence/SUMMARY.md (also used as the CI job summary).
Requires Pillow.
"""
import json
import sys
from collections import defaultdict
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

ROOT = Path(sys.argv[1] if len(sys.argv) > 1 else "evidence")
OUT = ROOT / "sheets"
TW, TH = 448, 252
BG = (251, 243, 238)
INK = (74, 48, 80)


def font(size):
    try:
        return ImageFont.load_default(size=size)
    except TypeError:
        return ImageFont.load_default()


def thumb(path):
    try:
        im = Image.open(ROOT / path).convert("RGB")
    except FileNotFoundError:
        im = Image.new("RGB", (TW, TH), (200, 80, 80))
        ImageDraw.Draw(im).text((10, 10), "MISSING " + path, fill="white", font=font(16))
        return im
    return im.resize((TW, TH), Image.LANCZOS)


def grid(title, rows, col_titles, name, note=""):
    """rows: list of (row_label, [paths or None])"""
    lab_w = 210; head = 70 + (24 if note else 0); cols = len(col_titles)
    sheet = Image.new("RGB", (lab_w + cols * (TW + 8), head + len(rows) * (TH + 8)), BG)
    d = ImageDraw.Draw(sheet)
    d.text((14, 10), title, fill=INK, font=font(30))
    if note:
        d.text((14, 48), note, fill=(150, 60, 90), font=font(16))
    for c, t in enumerate(col_titles):
        d.text((lab_w + c * (TW + 8) + 6, head - 24), t, fill=INK, font=font(17))
    for r, (label, paths) in enumerate(rows):
        y = head + r * (TH + 8)
        d.text((12, y + TH // 2 - 30), label, fill=INK, font=font(19))
        for c, p in enumerate(paths):
            if p:
                sheet.paste(thumb(p), (lab_w + c * (TW + 8), y))
    OUT.mkdir(parents=True, exist_ok=True)
    sheet.save(OUT / name, optimize=True)
    return name


def main():
    report = json.loads((ROOT / "capture_report.json").read_text())
    imgs = report["images"]
    by_set = defaultdict(list)
    for im in imgs:
        by_set[im["set"]].append(im)
    made = []

    if by_set["sheet"]:
        classes = []
        for im in by_set["sheet"]:
            cid = im["file"].split("/")[1].split("__")[0]
            if cid not in classes:
                classes.append(cid)
        cols = ["adult_q3__neutral", "adult_side__neutral", "foal_q3__neutral", "adult_q3__stable", "foal_q3__stable"]
        files = {im["file"] for im in by_set["sheet"]}
        label = {im["file"].split("/")[1].split("__")[0]: im["label"] for im in by_set["sheet"]}
        rows = [(label[c], [f"sheet/{c}__{col}.png" if f"sheet/{c}__{col}.png" in files else None for col in cols]) for c in classes]
        families = report.get("meshFamilies", {})
        foal_label = "Foal placeholder" if families.get("horse.standard.foal.v1", {}).get("placeholder") else "Foal"
        made.append(grid("Phase-1 coat contact sheet", rows,
                         ["Adult 3/4 - neutral", "Adult side - neutral", f"{foal_label} - neutral", "Adult 3/4 - stable", f"{foal_label} - stable"],
                         "coat_contact_sheet.png", "Render comparison only. Matrix rows remain unapproved. Asset sources are listed in SUMMARY.md."))
    if by_set["markings"]:
        ims = by_set["markings"]
        rows = []
        files = {im["file"] for im in ims}
        for im in ims:
            if im["view"] != "q3":
                continue
            stem = Path(im["file"]).stem.split("__")[0]
            label = "same key #2 - foal" if "foal" in im["file"] else stem.replace("test_", "key #")
            head = im["file"].replace("q3.png", "head.png")
            rows.append((f"{label}\n{im['markings']}"[:80], [im["file"], head if head in files else None]))
        made.append(grid("Deterministic white markings (TEST-ONLY keys)", rows, ["3/4", "head"], "markings_sheet.png",
                         "Markings come only from markingRealizationKey; adult and foal with the same key must match."))
    for setname, title, per_row in (("clay", "Clay form review - no coat", 3), ("turntable", "Turntables", 4), ("detail", "Detail views (neutral)", 3),
                                    ("pair", "Adult + foal (stable lighting)", 2), ("stable", "Stable environment", 2),
                                    ("probes", "Unsupported probes (must be EMPTY - no substitute coat)", 3)):
        ims = by_set[setname]
        if not ims:
            continue
        rows = []
        for i in range(0, len(ims), per_row):
            chunk = ims[i:i + per_row]
            rows.append((chunk[0].get("label") or "", [c["file"] for c in chunk]))
        made.append(grid(title, rows, [""] * per_row, f"{setname}_sheet.png"))
    if by_set["anim"]:
        anims = defaultdict(list)
        for im in by_set["anim"]:
            anims[im["file"].split("/")[1].split("_")[0]].append(im["file"])
        made.append(grid("Animation pose samples (NOT proof of motion quality)", [(k, v) for k, v in anims.items()],
                         [f"t{i}" for i in range(6)], "anim_sheet.png"))

    probes = by_set["probes"]
    lines = [
        "# Cloverbrook visual evidence",
        "",
        f"- Godot: `{report['godot']}`  ",
        f"- Renderer: `{report['renderer']}`  ",
        f"- Visual catalog: `{report['catalog']}`  ",
        f"- Images: {len(imgs)} · capture failures: **{report['failures']}**",
        "",
        "> " + report["warning"],
        "> No coat-matrix row is approved by these renders.",
        "",
        "## Mesh sources",
        *[f"- `{key}`: `{value['scene']}` (placeholder: {value['placeholder']})" for key, value in report.get('meshFamilies', {}).items()],
        *(["- Legacy capture: mesh source was not recorded; see its original catalog."] if not report.get('meshFamilies') else []),
        "",
        "## Unavailable animations (skipped)",
        *[f"- `{name}`" for name in report.get('unavailableAnimations', [])],
        "",
        "## Sheets",
        *[f"- `sheets/{m}`" for m in made],
        "",
        "## Unsupported probes (expected refusals)",
        *[f"- `{p['fixtureId']}` → {p['support']}" for p in probes],
        "",
        "## Coat classes rendered",
        *sorted({f"- {im['coatClassId']}: {im['label']} — {im['support']}" for im in by_set['sheet']}),
    ]
    (ROOT / "SUMMARY.md").write_text("\n".join(lines) + "\n")
    print("\n".join(lines))


if __name__ == "__main__":
    main()
