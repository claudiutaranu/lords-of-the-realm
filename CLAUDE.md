# Realm Prototype

Lords of the Realm II, rebuilt in Godot 4.7.2 (C#/mono, Forward+). The campaign map is Terrain3D;
everything else is hand-built UI over a turn-based economy.

## Working here

- Build with `dotnet build`. Scenes run through the Godot mono binary (find it with
  `mdfind -name Godot_mono.app`; on this machine it lives under an AppTranslocation temp path that
  changes when the app is moved).
- **Six check suites, and they all have to pass before anything is called done:**
  `scene/checks/{economy,lord,battle,fortification,market,events}-check.tscn`, each run headless:
  `"$GODOT" --headless --path . scene/checks/battle-check.tscn`. They print `all checks passed` or
  `N FAILED`.
- Checks share `user://saves` with the player. Write a save in a check only through `SaveGame.Write`
  and take it away again with `SaveGame.Forget` — never clear the folder.
- Renders for eyeballing UI go through a throwaway harness (`scene/shot.tscn` +
  `scripts/UI/ShotHarness.cs`), kept out of git via `.git/info/exclude`. Delete the PNGs after
  looking at them.

## House style

- Comments are prose and say WHY, in the voice of the game. A doc comment that only restates the
  signature is not worth its line.
- No dead code, no commented-out code, no `TODO: fix later`. A deliberate corner cut is marked
  `ponytail:` with its ceiling and the upgrade path.
- Files over ~300 lines want splitting. `CampaignMapPage`, `MapDecoration` and
  `tools/generate_campaign_map.py` are over it and known to be.
- Commit messages read like chapter titles with a body that explains what changed and why.

## The economy

`TurnManager` owns every held county (`ProvinceEconomy`) and advances them one season per End Turn;
UI never calls `EconomySimulation` directly. An unheld county is not in the turn at all until
somebody takes it. A realm keeps one purse; stores stay where they were reaped.

Rival lords run through the same simulation, with `LordAI` giving the orders. It keeps back its
reserve AND its next meal before selling anything, and sells what it is over-stocked in before
buying — both learned the hard way, from a lord who starved his own county in forty turns. It also
rests worn fields a quarter at a time, sows no more fields than autumn has hands to reap (240 a
field), and sells what it digs above a working stock every season; without those, every rival
emptied its county within fifty turns. `LordCheck.FiftyYears` holds it to that.

## Armies

The men live in **companies** (`FieldArmy`), not in one roster per county:

- `Home` is the county that raised them and goes on paying and feeding them, wherever they walk.
  `County` is where they are standing. `Key` (`"Kingsreach#2"`) is how the map and the screens name
  one; ids are never reused.
- Each company has its own `MarchLeft`, its own position, its own banner on the map. One company's
  march spends one company's season.
- The barracks turns out a NEW company per muster. The walls (`FortificationsPage`) take from and
  give back to the company at the seat, through `ProvinceEconomy.Muster/Mustered`.
- Two companies halting in the same field are joined only if the player says so (`JoinPanel` →
  `TurnManager.Merge`, which keeps the slower pair of legs). Splitting is `SplitPanel`. Disbanding
  returns the men to `Population` — they were taken out of it when they were raised.
- `ProvinceEconomy.Readiest()` is what a county-wide order (the sidebar's March) means.

## Battles

- A county is taken at its own gate. The battle panel opens when one of the player's companies
  halts on another lord's **seat** — its town, or the walls raised on it, which are the same square
  of ground (`CampaignMapPage.Contested`, `MapDecoration.TownAt`).
- Two fights in order: whatever stands in the open, then whatever is on the walls. The county does
  not change hands until both are done, which is what a castle is for. Sitting down in front of the
  gate (`Besiege`) is the other way, and the only way against the top rungs.
- **A side that breaks is finished.** `BattleBreakPoint` (0.35) is when it comes apart, and what the
  fighting did not kill is written off with it: the loser's whole company is gone, and the losses
  the panel prints are the whole company. A day neither side breaks leaves both standing.
- Nothing falls back behind the walls. The gate watch is the men who were always on it.
- A county that falls loses the men standing in it; the companies it raised that were elsewhere pass
  to another county of the same lord (`TurnManager.Refuge` → `ProvinceEconomy.Adopt`).
- Rivals make war too. `LordArms` forges and raises companies for every rival county each season
  (called by `TurnManager`, not by `LordAI`, so the harnesses can run the player's counties through
  `LordAI` without raising armies for him). `LordsCampaign` marches them over the map's own march
  grid, which `CampaignMapPage` hands over with `TurnManager.Survey`; with no survey (the checks),
  nobody marches. How many, how soon, how sure and whether they come for the player is the
  `Lord*` difficulty table in `GameBalance`. Companies of different counties merge only at the gate
  they attack — a merged company is fed by one county — and a county over its share sends the
  surplus home.
- The rivals take their turn after the player's, in front of him, as in Lords of the Realm: End Turn
  calls `TurnManager.RivalsTurn()` (orders, muster, marches), the map walks their banners along the
  roads they took, and only then does the season turn over (`AdvanceTurn`, which runs `RivalsTurn`
  itself if nobody watched). Map input is shut while they walk (`_rivalsMarching`), with a deadline
  in case a banner never reports in.
- Nobody opens with a field army: the player raises his first company himself. Walls keep their
  authored watch. A neutral county's militia is `MilitiaShare` of its people, by difficulty.

## The map

- `MarchGrid` cuts the country into cells with a cost each; roads are cheap, border ditches are
  impassable except at a ford or a road. A rival's ground is open — it used to be shut outright,
  back when there was no way to fight for it.
- A county's marker (`ProvinceData.MapPosition`, from `provinces.json`) and its village
  (`TownPosition`, from `map-yards.json`) are NOT the same point: seats that overhung the sea were
  moved inland. Anything about the village — entering the town, fighting for it — uses the village.
- The map's images are generated by `tools/generate_campaign_map.py`; models are cut down and their
  normals baked by `tools/decimate_meshy.py`. Re-run the tool, then reopen Godot to reimport.
- `tools/check_marches.py` walks every county's village from the capital over the generated images,
  by `MarchGrid`'s own rules, and fails if one cannot be reached (`rebuild-map.sh` runs it). Its
  constants are copied from the C#; change `MarchableRise` or the cell size and change them there
  too. A road steeper than the grid allows is drawn but cannot be walked — that once shut the whole
  north off.

## Saves

`SaveGame` writes `user://saves/<unix>.json`, version 4. Companies are saved; a version-3 file still
opens — `ProvinceEconomy.Garrison/MarchLeft/ArmyX/ArmyY` are read-only-on-the-way-in properties that
fill the one company such a file describes.

Gotcha worth keeping: System.Text.Json will not fill a **collection** property that has no getter.
`Garrison` needs its `get => null` (with `JsonIgnoreCondition.WhenWritingNull`) or old saves load
with empty armies and nobody notices until a campaign is opened.
