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
from collections import Counter
from pathlib import Path

PROJECT_DIR = Path(__file__).resolve().parent.parent


def generator_provinces(source_path):
    """(name, x, y, owner) per province, read out of the generator's PROVINCES literal without
    importing it - the check must run without numpy and Pillow installed. owner is the OWNER_* name
    as written, or None where the table carries no ownership column."""
    tree = ast.parse(source_path.read_text())
    for node in ast.walk(tree):
        if isinstance(node, ast.Assign) and any(
                isinstance(t, ast.Name) and t.id == "PROVINCES" for t in node.targets):
            return [(e.elts[0].value, e.elts[1].value, e.elts[2].value,
                     e.elts[-1].id if isinstance(e.elts[-1], ast.Name) else None)
                    for e in node.value.elts]

    raise AssertionError(f"no PROVINCES list in {source_path}")


def check(campaign="royal-crown"):
    data_dir = PROJECT_DIR / "data" / "campaigns" / campaign
    data = json.loads((data_dir / "provinces.json").read_text())
    drawn = generator_provinces(PROJECT_DIR / "tools" / "generate_campaign_map.py")
    played = data["provinces"]

    assert data["player"] in data["realms"], f"played realm '{data['player']}' is not one of the realms"
    assert len(played) == len(drawn), f"{len(played)} provinces played, {len(drawn)} drawn"

    # The generator paints ownership into the ID map's green channel, so its owner column and the
    # realm here have to name the same thing every time - one owner, one realm, both ways round.
    realm_of_owner, owner_of_realm = {}, {}

    for index, (province, (name, x, y, owner)) in enumerate(zip(played, drawn)):
        assert province["name"] == name, \
            f"index {index}: '{province['name']}' played, '{name}' drawn - the ID map disagrees"
        assert (province["x"], province["y"]) == (x, y), \
            f"{name}: seat at ({province['x']}, {province['y']}) played, ({x}, {y}) drawn"

        realm = province["realm"]
        assert realm in data["realms"], f"{name}: unknown realm '{realm}'"
        if owner:
            assert realm_of_owner.setdefault(owner, realm) == realm, \
                f"{name}: {owner} is '{realm}' here but '{realm_of_owner[owner]}' elsewhere"
            assert owner_of_realm.setdefault(realm, owner) == owner, \
                f"{name}: '{realm}' is {owner} here but {owner_of_realm[realm]} elsewhere"

        economy = province.get("economy")
        if economy:
            definition = data_dir / "provinces" / f"{economy}.tres"
            assert definition.exists(), f"{name}: no {definition.relative_to(PROJECT_DIR)}"

    standing = Counter(province["realm"] for province in played)
    print(f"{campaign}: {len(played)} provinces in step with the map generator — "
          + ", ".join(f"{realm} {count}" for realm, count in standing.items()))


if __name__ == "__main__":
    check(*sys.argv[1:])
