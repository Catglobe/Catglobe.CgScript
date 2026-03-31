#!/usr/bin/env python3
"""
Strip redundant/dead fields from CgScriptDefinitions.json.

From every function entry (objects under "functions"):
  - name                      (duplicates the dictionary key)
  - returnType                (always null)
  - numberOfRequiredArguments (always 0)
  - parameters                (always null)
  - isNewStyle                (always true)

From every variant entry (objects in each "variants" array):
  - name                      (never used, duplicates parent key)

Writes back as compact JSON (no extra whitespace, all on one line).
"""
import json

JSON_PATH = r"D:\Catglobe.ScriptDeployer\Catglobe.CgScript.EditorSupport.Parsing\Resources\CgScriptDefinitions.json"

FN_DROP  = {"name", "returnType", "numberOfRequiredArguments", "parameters", "isNewStyle"}
VAR_DROP = {"name"}

with open(JSON_PATH, "r", encoding="utf-8") as f:
    data = json.load(f)

functions = data.get("functions", {})
for fn in functions.values():
    for field in FN_DROP:
        fn.pop(field, None)
    for variant in fn.get("variants", []):
        for field in VAR_DROP:
            variant.pop(field, None)

with open(JSON_PATH, "w", encoding="utf-8", newline="") as f:
    json.dump(data, f, separators=(",", ":"), ensure_ascii=False)

print(f"Done. Functions processed: {len(functions)}")