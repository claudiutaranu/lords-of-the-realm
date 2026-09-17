"""Checks a campaign's data against what the game, the map generator and the terrain shader each
quietly assume about it.

There is one copy of the provinces now - generate_campaign_map.py reads the same provinces.json the
game does - so nothing can drift out of order any more. What is left are the encodings none of the
three states out loud:

  - a province's place in the list is its index on map-ids.png (red channel = index + 1), so the
    order is data, not presentation;
  - a realm's place in the list is the owner written into that map's green channel (+1), and the
    terrain shader paints 1, 2 and anything beyond as realm one, realm two and unclaimed - three
    realms, the played one first;
  - the game keys a province's economy by the name inside its .tres, and looks it up by the name in
    provinces.json, so those two names have to be the same string.

Break any of these and nothing errors: the map just attributes clicks, colours or income to the
wrong province.

Run: python tools/check_campaign_data.py [campaign]
"""

import json
import re
import sys
from collections import Counter
from pathlib import Path

PROJECT_DIR = Path(__file__).resolve().parent.parent


def check(campaign="royal-crown"):
    data_dir = PROJECT_DIR / "data" / "campaigns" / campaign
    data = json.loads((data_dir / "provinces.json").read_text())
    width, height = json.loads((data_dir / "map.json").read_text())["size"]
    provinces = data["provinces"]
    realms = list(data["realms"])

    assert data["player"] in realms, f"the played realm '{data['player']}' is not one of the realms"
    assert realms[0] == data["player"], \
        f"'{data['player']}' is played but '{realms[0]}' is listed first - the shader paints the first as yours"
    assert len(realms) <= 3, f"{len(realms)} realms listed; the terrain shader paints three"

    repeated = [name for name, count in Counter(p["name"] for p in provinces).items() if count > 1]
    assert not repeated, f"two provinces share a name: {', '.join(repeated)}"

    for province in provinces:
        name = province["name"]
        assert province["realm"] in realms, f"{name}: unknown realm '{province['realm']}'"
        assert 0 <= province["x"] < width and 0 <= province["y"] < height, \
            f"{name}: seat at ({province['x']}, {province['y']}) is off the {width}x{height} map"
        assert province.get("region") in (None, "north", "south"), \
            f"{name}: region '{province['region']}' is neither north nor south"

        economy = province.get("economy")
        if not economy:
            continue

        definition = data_dir / "provinces" / f"{economy}.tres"
        assert definition.exists(), f"{name}: no {definition.relative_to(PROJECT_DIR)}"
        named = re.search(r'ProvinceName = "([^"]*)"', definition.read_text())
        assert named and named.group(1) == name, \
            f"{name}: {definition.name} calls itself '{named.group(1) if named else ''}'"

    standing = Counter(province["realm"] for province in provinces)
    print(f"{campaign}: {len(provinces)} provinces on a {width}x{height} map — "
          + ", ".join(f"{realm} {count}" for realm, count in standing.items()))


if __name__ == "__main__":
    check(*sys.argv[1:])
