#!/usr/bin/env python3
import json
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[1]
LANG = ROOT / "languages"
FALLBACK = LANG / "en-US.json"

with FALLBACK.open(encoding="utf-8") as f:
    fallback = json.load(f)
expected = set(fallback["strings"])
failed = False

for path in sorted(LANG.glob("*.json")):
    with path.open(encoding="utf-8") as f:
        data = json.load(f)
    actual = set(data.get("strings", {}))
    missing = sorted(expected - actual)
    extra = sorted(actual - expected)
    empty = sorted(k for k,v in data.get("strings", {}).items() if not isinstance(v,str) or not v.strip())
    if missing or extra or empty:
        failed = True
        print(f"FAIL {path.name}")
        if missing: print("  missing:", ", ".join(missing))
        if extra: print("  extra:", ", ".join(extra))
        if empty: print("  empty:", ", ".join(empty))
    else:
        print(f"OK   {path.name}: {len(actual)} keys")

sys.exit(1 if failed else 0)
