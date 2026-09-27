# Realm Prototype

Lords of the Realm II, rebuilt in Godot 4.7.2 (C#/mono, Forward+). The campaign map is Terrain3D;
everything else is hand-built UI over a turn-based economy.

## Working here

- Build with `dotnet build`. Scenes run through the Godot mono binary (find it with
  `mdfind -name Godot_mono.app`; on this machine it lives under an AppTranslocation temp path that
  changes when the app is moved).
- **Seven check suites, and they all have to pass before anything is called done:**
  `scene/checks/{economy,lord,battle,fortification,market,events,diplomacy}-check.tscn`, each run headless:
  `"$GODOT" --headless --path . scene/checks/battle-check.tscn`. They print `all checks passed` or
  `N FAILED`.
- Checks share `user://saves` with the player. Write a save in a check only through `SaveGame.Write`
  and take it away again with `SaveGame.Forget` — never clear the folder.
- Renders for eyeballing UI go through a throwaway harness (`scene/shot.tscn` +
  `scripts/UI/ShotHarness.cs`), kept out of git via `.git/info/exclude`. Delete the PNGs after
  looking at them.

## House style

- Every modal wears the army table's painted frame: `Chrome.Painted(column)` (or `Chrome.PaintedStyle()`
  on a scene's PanelContainer), the column's first line being the title that sits on the ribbon. The
  army and battle tables draw the same art at full size through `PaintedPanel`.

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

The county's year is Lords of the Realm II's own rules, in whole numbers (docs/lotr2-engine-checklist.md):
`Livelihood` — the table (dairy 5 a cow free, then beef 10 a head and bread 6 a sack by the lord's
share, a step down until it can be served), health bands, happiness (5 − tax + health + 3×ration − 8,
plus the realm's rates via `TurnManager.Resented`), births and deaths off the original ladders, tax as
`pop × taxBase × rate` (fortifications.json "taxBase", 320 on open ground), recruiting cost; and
`Husbandry` — grain sown at the end of winter (10 sacks a field down to 1, crop ×12), capped and
grown by half the county's `Soil` in spring and summer, reaped 3 sacks per 2 reapers in autumn; soil
+6 a fallow field, −3 a grain field, every season; the herd bred and killed by crowding per pasture
field and herdsmen (three a head, six useful; a cow feeds five off her milk). Moving house goes to the happiest neighbour
(provinces.json "neighbours"). Market prices are the original's (sell/buy, spread ⅓).

The weather is the original's (`Climate`), not a random event: every held county keeps a `Dryness`
(+8 spring, +24 summer, +12 autumn, −12 winter, less 0–15 by chance, an extra swing over one county
and half of it over its neighbours) and the season's `Weather` is read off its band — flood, storms,
cloudy, sunny, drought, frost in the cold seasons. `TurnManager` rolls it before the counties' season
(pipeline step 3). A flood or a drought ruins a field (`FieldUse.Waste`, and it takes no order the
season it is struck); the weather moves the sowing, growing and reaping and the herd. Waste lies until
the lord orders it `Reclaiming` (`FieldPanel`: Reclaim/Abandon): `FieldReclaimWork` (400, the original's 800 halved at
the user's word) hand-seasons a field, 200 at most
a season, furthest-on first (`Husbandry.Reclaim`). An army's march is cut at the first of another
lord's sown or grazed fields on its road (`TurnManager.FirstSpoil`): it lays that one field waste —
its corn or its herd lost — and that is the company's season (`Trample`, `MarchLeft` 0; one field a
season). The field is `Waste`, drawn black like any ruined field, and is reclaimed like one but with
only `TrampledReclaimWork` (200) of it to do (the user's call: the original only takes the crop). One halted on a quarry, mine or wood shuts it for `OccupiedSeasons`. The map's plots and
sites reach the turn through `Survey`.

Labour is Lords of the Realm's allocator (`Labour`): nine jobs (grain, cattle, reclaim, castle, iron,
stone, wood, smith, idle), dealt from scratch against saved proportions — `IndustryShare` (25 to
open) and each job's part of its half (`Shares`, in hundredths of a percent so a moved figure does
not round the others about) — twice a season inside `RunTurn` and again at the end of
`TurnManager`'s season. A job takes its share or its useful ceiling, whichever is less; a share a
job cannot use walks on inside the half; shares the lord left unassigned are idle by his choice.
`Labour.Ask` (a figure moved): taken off stands idle; put on comes from the idle and only the idle
(either half, the bar moves), as in the original; `Labour.Most` is where + stops. The diggings have no ceiling, only presence and
the lord's on/off (`Shut`); each site has an efficiency that climbs from 15% while it is worked.
Nobody writes a `*Workers` field by hand any more: the screens call `Labour.Ask`/`Divide`/`Toggle`,
the rivals `Labour.FarmsFirst`. A county digs stone or iron, never both (`EconomyCheck.StoneOrIron`).

Stores move between the lord's counties only by cart (`Shipment`, `TurnManagerSupply`, `SupplyPanel`,
opened from a store's panel): anything a county keeps but gold — the five stores and its armoury
(the original carted only grain and cattle; the user's call). The goods leave on dispatch, roll a
`MarchReach` a season along `MarchGrid`'s roads (`Haul`, at the start of `AdvanceTurn`), unload on
arrival, turn home if the destination falls, and are lost to another lord's company on the road.

The smithy works as Lords of the Realm's does: `Forging` names the one weapon a county makes, every
season, as many as its `SmithWorkers` (a labour-bar trade like the masons) and its stores pay for,
per piece (`Smithy`, `weapons.json`), before the mine digs. Rivals do not staff a smithy yet —
`LordArms` buys their batches outright.

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
  `Lord*` difficulty table in `GameBalance`. Every company of a lord standing in one county merges into the
  largest (`LordsCampaign.Gather`, from the first season) and his smaller companies march to join his
  main host (`Rally`) — a merged company is fed by the county that raised the largest. A county over
  its share sends the surplus home.
- The rivals take their turn after the player's, in front of him, as in Lords of the Realm: End Turn
  calls `TurnManager.RivalsTurn()` (orders, muster, marches), the map walks their banners along the
  roads they took, and only then does the season turn over (`AdvanceTurn`, which runs `RivalsTurn`
  itself if nobody watched). Map input is shut while they walk (`_rivalsMarching`), with a deadline
  in case a banner never reports in.
- A rival sizes his army by his taxes AND a share of his treasury, buys spears at market and hires a
  band standing in his county out of a war chest (`LordArms`; `LordTreasuryShare`/`LordWarChest` by
  difficulty — spending half a chest a season took the player's seat in five years on Medium). He
  takes the empty country before he comes for the player (no player county is on his list while an
  unclaimed one is), marches only on counties that can change hands (`TurnManager.CanBeTaken`), picking the likeliest
  of the three nearest, and mans his walls to `LordWatchShare` of what they hold — never through a
  gate that is under siege.
- Rivals build walls too (`LordWalls`): each season a county with the stores for the next rung may
  order it (`LordBuildChance` by difficulty, off TurnManager's dice), saving the timber for it
  rather than selling it, and puts `LordWatch` men on a wall once it stands. How high they climb is
  the campaign's (`provinces.json` "rivalWallsUpTo"; the first map stops at the two timber rungs).
  Without dice `LordAI` builds nothing, which is how the older checks still run it. An open town
  (a county he has just taken) skips the dice: its palisade goes up the first season he can pay,
  buying the timber at market out of what is above his reserve.
- Each lord keeps one raid out (`LordsCampaign.Raid`, the original AI's step 10): `LordRaidMen`
  peasants from his most peopled county, walking over his nearest enemy's fields and diggings for
  `LordRaidSeasons` (`Trample` does the harm), then home, where they go back to the county. A raid
  (`FieldArmy.Raider`) keeps its banner like a hired band, takes no gate, and can be caught in the open.
- A lord who takes a county settles it for `LordSettles` seasons before marching on the next, and
  the player hears of it ("rival-took"). Without it the Northern Watch had the whole empty country
  in five seasons on Medium and the player's seat by the fourth year.
- Losing the last county is the end: `TurnManager.PlayerFallen`, asked after the rivals march and
  after the season, puts `FallenPanel` over the map (load a save, or the main menu).
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

## Diplomacy

- There are four lords in the whole game, written once in `data/lords.json` (`Lords`): the Marshal,
  the Margrave, the Duchess and the Abbot, carrying Lords of the Realm's Knight, Baron, Countess and
  Bishop. A campaign never describes a lord; its realm names one (`provinces.json` `"lord"`), and
  `TurnManager.LordOf` seats him. The first map has one: the Northern Watch is the Margrave.
- `Diplomacy` is keyed by realm, so one rival or four play the same rules: standing −30..+30 a
  pair, the seven letters, one alliance a lord, the grudge that ends it, two warnings and then a war
  that is never made up. The player's letters go out through `TurnManager.Write` (a gift is paid
  as it is sent) and are answered at the start of `RivalsTurn`; the replies wait in `Inbox`.
- No alliance with fewer than two rival lords (`Diplomacy.IsAllianceOpen`): swearing to the only
  other lord would leave nobody to fight and the map unwinnable, so on the first map the Margrave
  neither offers nor is offered one (the user's call).
- The original's numbers are kept where they are known; the rest are `GameBalance` and marked [I].
- The lords' letters are laid straight on the map when the season opens, after the advisor
  (`LetterPanel`, one at a time; an offer carries Accept/Refuse). `DiplomacyPanel` (the scroll on the
  nav rail) is each lord's card: regard, alliance/warnings/war, the letters he can be sent this
  season, and the last of `Diplomacy.Kept`, the correspondence both ways.

## Saves

`SaveGame` writes `user://saves/<unix>.json`, version 5 (the lords' letters, `Diplomacy`; a version-4
file opens with nobody having written anybody). Companies are saved; a version-3 file still
opens — `ProvinceEconomy.Garrison/MarchLeft/ArmyX/ArmyY` are read-only-on-the-way-in properties that
fill the one company such a file describes.

Gotcha worth keeping: System.Text.Json will not fill a **collection** property that has no getter.
`Garrison` needs its `get => null` (with `JsonIgnoreCondition.WhenWritingNull`) or old saves load
with empty armies and nobody notices until a campaign is opened.
