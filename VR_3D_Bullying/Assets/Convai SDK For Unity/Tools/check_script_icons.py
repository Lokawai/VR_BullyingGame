#!/usr/bin/env python3
"""
Check that all .cs.meta script files in the Convai SDK package have the standard
MonoImporter icon. Excludes Plugins/ and Reallusion paths.

Usage:
    python Tools/check_script_icons.py
    python Tools/check_script_icons.py --fix   # optional: fix icon in place (not implemented)
"""

from pathlib import Path
import sys
import os

# Expected icon line in MonoImporter (Unity script icon)
EXPECTED_ICON = "guid: 11be21f3f1f4bac46a48d14a031f3d60"
EXPECTED_ICON_FULL = "icon: {fileID: 2800000, guid: 11be21f3f1f4bac46a48d14a031f3d60, type: 3}"

# Path segments to exclude (package root Plugins, and any Reallusion)
EXCLUDE_SEGMENTS = ("Plugins", "Reallusion")


def should_skip(rel_path: str) -> bool:
    parts = Path(rel_path).parts
    # Exclude top-level Plugins (first segment after package root)
    if "Plugins" in parts and parts[0] == "Plugins":
        return True
    # Exclude any path containing Reallusion or Plugins
    for segment in EXCLUDE_SEGMENTS:
        if segment in parts:
            return True
    return False


def main() -> int:
    script_dir = Path(__file__).resolve().parent
    package_root = script_dir.parent

    meta_files = sorted(package_root.rglob("*.cs.meta"))
    failed = []
    skipped = 0

    for meta_path in meta_files:
        try:
            rel = meta_path.relative_to(package_root)
        except ValueError:
            continue
        rel_str = str(rel).replace("\\", "/")

        if should_skip(rel_str):
            skipped += 1
            continue

        text = meta_path.read_text(encoding="utf-8", errors="replace")
        if EXPECTED_ICON not in text:
            failed.append(rel_str)

    if failed:
        print("Script .meta files missing expected icon (excl. Plugins & Reallusion):")
        for f in failed:
            print(f"  {f}")
        print(f"\nTotal: {len(failed)} file(s) need the icon line.")
        return 1

    print(f"OK: All checked .cs.meta files have icon {EXPECTED_ICON[:8]}... (skipped Plugins/Reallusion: {skipped})")
    return 0


if __name__ == "__main__":
    sys.exit(main())
