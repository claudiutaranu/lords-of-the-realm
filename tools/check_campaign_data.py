"""Checks a campaign's provinces.json against the map generator that drew its provinces.

The one thing here that breaks silently: a province's place in provinces.json IS its index on
map-ids.png (red channel = index + 1), so if the two lists fall out of order the game keeps
running and simply attributes every click to the wrong province. Same for a seat that moved in
one file and not the other - the marker ends up in a neighbour's territory.

Ownership is deliberately not compared: the generator's `owner` column and the game's `realm` are
allowed to disagree while the campaign's opening position is still being decided.

Run: python tools/check_campaign_data.py
"""

import ast
import json
import sys
from pathlib import Path

PROJECT_DIR = Path(__file__).resolve().parent.parent


def generator_provinces(source_path):
    """(name, x, y) per province, read out of the generator's PROVINCES literal without importing
    it - the check must run without numpy and Pillow installed."""
    tree = ast.parse(source_path.read_text())
    for node in ast.walk(tree):
        if isinstance(node, ast.Assign) and any(
                isinstance(t, ast.Name) and t.id == "PROVINCES" for t in node.targets):
            return [(e.elts[0].value, e.elts[1].value, e.elts[2].value) for e in node.value.elts]

    raise AssertionError(f"no PROVINCES list in {source_path}")


def check(campaign="royal-crown"):
    data_dir = PROJECT_DIR / "data" / "campaigns" / campaign
    data = json.loads((data_dir / "provinces.json").read_text())
    drawn = generator_provinces(PROJECT_DIR / "tools" / "generate_campaign_map.py")
    played = data["provinces"]

    assert len(played) == len(drawn), f"{len(played)} provinces played, {len(drawn)} drawn"
    for index, (province, (name, x, y)) in enumerate(zip(played, drawn)):
        assert province["name"] == name, \
            f"index {index}: '{province['name']}' played, '{name}' drawn - the ID map disagrees"
        assert (province["x"], province["y"]) == (x, y), \
            f"{name}: seat at ({province['x']}, {province['y']}) played, ({x}, {y}) drawn"

        assert province["realm"] in data["realms"], f"{name}: unknown realm '{province['realm']}'"
        economy = province.get("economy")
        if economy:
            definition = data_dir / "provinces" / f"{economy}.tres"
            assert definition.exists(), f"{name}: no {definition.relative_to(PROJECT_DIR)}"

    print(f"{campaign}: {len(played)} provinces, in step with the map generator")


if __name__ == "__main__":
    check(*sys.argv[1:])
