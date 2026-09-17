"""Checks every .tscn for references it cannot resolve.

Godot fails a scene at parse time when a node asks for an ExtResource or SubResource the file never
declared, and the whole campaign then refuses to open — a break that stays invisible until launch.
This reads the scenes the way the parser does: gather what is declared, then look at what is used.

Run: python tools/check_scenes.py
"""

import pathlib
import re
import sys

PROJECT_DIR = pathlib.Path(__file__).resolve().parent.parent


def faults_in(scene):
    text = scene.read_text()
    declared = {
        "ExtResource": set(re.findall(r'^\[ext_resource [^\n]*\bid="([^"]+)"', text, re.M)),
        "SubResource": set(re.findall(r'^\[sub_resource [^\n]*\bid="([^"]+)"', text, re.M)),
    }

    found = []
    for kind, names in declared.items():
        for used in sorted(set(re.findall(rf'{kind}\("([^"]+)"\)', text))):
            if used not in names:
                found.append(f'{kind}("{used}") is used but never declared')

    for path in re.findall(r'^\[ext_resource [^\n]*\bpath="res://([^"]+)"', text, re.M):
        if not (PROJECT_DIR / path).exists():
            found.append(f"{path} is declared but is not on disk")

    return found


def main():
    scenes = sorted(PROJECT_DIR.glob("scene/**/*.tscn"))
    broken = 0
    for scene in scenes:
        for fault in faults_in(scene):
            print(f"{scene.relative_to(PROJECT_DIR)}: {fault}")
            broken += 1

    print(f"{len(scenes)} scenes checked, {broken} broken reference{'' if broken == 1 else 's'}")
    return 1 if broken else 0


if __name__ == "__main__":
    sys.exit(main())
