#!/usr/bin/env python3
# MARK: ! AI disclaimer
# All code in this file was written by Claude Opus 5.5
#
# progression-tree.py — check docs/progression-tree.json and render it as Mermaid.
#
# The JSON is the only source of truth; the diagram is always generated from it and is gitignored.
#
# Usage:
#   tools/progression-tree.py check              validate references, cycles, tiers and gates
#   tools/progression-tree.py mermaid            print a Mermaid flowchart (paste into mermaid.live)
#   tools/progression-tree.py render             write docs/progression-tree.mmd (gitignored); only
#                                                rewrites it when the output changed
#
# Exits non-zero when check finds a problem.

import collections
import json
import pathlib
import sys

TREE = pathlib.Path(__file__).resolve().parent.parent / "docs" / "progression-tree.json"
DIAGRAM = TREE.with_suffix(".mmd")


def load():
    return json.loads(TREE.read_text())


def check(tree):
    nodes = {n["id"]: n for n in tree["nodes"]}
    states = {s["id"]: s for s in tree.get("states", [])}
    problems = []

    if len(nodes) != len(tree["nodes"]):
        problems.append("duplicate node ids")

    for n in tree["nodes"]:
        for r in n.get("requires", []):
            if r not in nodes:
                problems.append(f"{n['id']} requires unknown node '{r}'")
        for g in n.get("gates", []):
            if g["state"] not in states:
                problems.append(f"{n['id']} is gated on unknown state '{g['state']}'")
        if "tier" not in n:
            problems.append(f"{n['id']} has no tier")

    for s in states.values():
        for key in ("startsOn", "endsOn"):
            if key in s and s[key] not in nodes:
                problems.append(f"state {s['id']} {key} unknown node '{s[key]}'")

    if problems:
        return problems

    # every node reachable from the roots, and no cycles
    children = collections.defaultdict(list)
    indegree = {i: len(n.get("requires", [])) for i, n in nodes.items()}
    for n in tree["nodes"]:
        for r in n.get("requires", []):
            children[r].append(n["id"])
    queue = [i for i, d in indegree.items() if d == 0]
    seen = 0
    while queue:
        x = queue.pop()
        seen += 1
        for k in children[x]:
            indegree[k] -= 1
            if indegree[k] == 0:
                queue.append(k)
    if seen != len(nodes):
        problems.append("cycle, or nodes unreachable from the roots")

    # chronology: nothing happens before what it needs
    for n in tree["nodes"]:
        for r in n.get("requires", []):
            if nodes[r]["tier"] > n["tier"]:
                problems.append(f"{n['id']} (tier {n['tier']}) requires {r}, which is later (tier {nodes[r]['tier']})")
        for g in n.get("gates", []):
            s = states[g["state"]]
            trigger = s.get("endsOn") if g["when"] == "ended" else s["startsOn"]
            if trigger and nodes[trigger]["tier"] > n["tier"]:
                problems.append(f"{n['id']} (tier {n['tier']}) is gated on {g['state']} {g['when']}, which fires later at {trigger}")

    return problems


def mermaid(tree):
    shapes = {
        "animal": '{i}(["{label}"])',
        "tile": '{i}[["{label}"]]',
        "sfx": '{i}{{{{"{label}"}}}}',
    }
    lines = ["flowchart TD"]

    for tier in sorted({n["tier"] for n in tree["nodes"]}):
        lines.append(f'  subgraph T{tier}["tier {tier}"]')
        for n in tree["nodes"]:
            if n["tier"] != tier:
                continue
            shape = shapes.get(n["kind"], '{i}["{label}"]')
            lines.append("    " + shape.format(i=n["id"], label=n["label"].replace('"', "'")))
        lines.append("  end")

    states = {s["id"]: s for s in tree.get("states", [])}
    for n in tree["nodes"]:
        for r in n.get("requires", []):
            arrow = "-.->" if n.get("confidence") else "-->"
            lines.append(f"  {r} {arrow} {n['id']}")
        for g in n.get("gates", []):
            s = states[g["state"]]
            trigger = s.get("endsOn") if g["when"] == "ended" else s["startsOn"]
            lines.append(f'  {trigger} ==>|"{g["state"]} {g["when"]}"| {n["id"]}')

    return "\n".join(lines)


def main():
    command = sys.argv[1] if len(sys.argv) > 1 else "check"
    tree = load()

    if command == "check":
        problems = check(tree)
        for p in problems:
            print(f"progression-tree: {p}", file=sys.stderr)
        print(f"progression-tree: {'FAILED' if problems else 'OK'} "
              f"({len(tree['nodes'])} nodes, {len(tree.get('states', []))} states)")
        sys.exit(1 if problems else 0)
    elif command == "mermaid":
        print(mermaid(tree))
    elif command == "render":
        text = mermaid(tree) + "\n"
        # idempotent: an unchanged tree leaves the file (and its timestamp) alone
        if DIAGRAM.exists() and DIAGRAM.read_text() == text:
            print(f"progression-tree: {DIAGRAM.name} already up to date")
        else:
            DIAGRAM.write_text(text)
            print(f"progression-tree: wrote {DIAGRAM.name}")
    else:
        print(__doc__ if __doc__ else "usage: progression-tree.py check|mermaid|render", file=sys.stderr)
        sys.exit(2)


if __name__ == "__main__":
    main()
