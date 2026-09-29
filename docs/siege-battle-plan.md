# Storming the walls by hand — the plan

The open field can be fought by hand now (`FieldBattle`, `Battlefield`). The walls are still the
captain's (`Battle.OnTheWalls`). This is how the second fight comes down onto the field too, in the
order it should be built. Each stage ends with the seven check suites green and something the lord
can play.

## What does not change

- **The captain's rules are the law.** A wall is `defence` (how hard its men are to kill) and
  `frontage` (how many attackers can be at it at once), plus `AssaultExposure` (a man on a ladder
  cannot defend himself) and `AssaultVolley` (half a bow against a parapet). Horses do not climb.
  The hand-fought assault is built out of exactly these, so its answer agrees with the captain's
  wherever his is not in doubt. `BattleCheck` holds it to that, as it already does for the field.
- **Same result, same ledger.** The assault ends in a `Battle.Result` and goes through
  `TurnManager.Attack(..., walls: true, fought)`, which already accepts it.
- **Siege by starvation stays as it is.** Storming is the other answer, not a replacement.

## Stage 1 — the walls as ground (sim only, no art)

`SiegeBattle` beside `FieldBattle`, sharing `FieldSquad`:

- The wall is a closed ring of **segments** around the town square, one ring per rung, its size
  from the rung (a palisade is small, the royal castle large). Each segment has a walkway the
  defenders stand on and a foot the attackers stand at.
- **Ladder points**: the wall offers `frontage` places to climb, spread round it. A squad sent at
  the wall takes the nearest free point; only the men on ladders fight the walkway, which is the
  frontage rule made visible. Horse cannot be sent at a wall.
- **The gate** is a segment with its own strength. Broken (by a ram, stage 3), it is open ground
  and the frontage there is a gateway's, not a ladder's.
- Defenders on the walkway take hits divided by `defence`; attackers on ladders take them
  multiplied by `1 / AssaultExposure`; bows below shoot at half (`AssaultVolley`).
- Once a squad has men over the wall, it fights in the bailey on open-field terms.
- Walls block movement. Pathing round them is Godot's own `AStarGrid2D` over the field, with the
  wall cells solid and the gate and breaches open. No navigation meshes.
- The captain's AI for both sides: the defence mans the walkway facing the enemy and keeps a
  reserve at the gate; the attack goes for the nearest ladders and the gate.

Checks: a host of 300 swords takes a palisade off 40 spears by hand, and does not take the royal
castle off the same 40; past the frontage, more men buy nothing; the same shapes agree with
`OnTheWalls` the way the field shapes agree with `InTheField`.

## Stage 2 — the walls drawn

- **Placeholder kit first**, the same way the field used the one soldier: wall segments and towers
  from the Quaternius pack already in `assets/models/rts` (`WallTowers_FirstAge`, `WallTowers_SecondAge`),
  a palisade of stakes built in code for the two timber rungs, and a gatehouse from the
  towers. Good enough to play the assault on.
- Defenders stand on the walkway (same `BattlefieldSquads`, raised to the wall's height), ladders
  are drawn where a squad is climbing, and the men on a ladder are drawn on it.
- The town square inside is the settlement model the map already uses for the county.
- The camera's height clamp rises with the wall.

## Stage 3 — siege engines

Lords of the Realm II built its engines **while besieging**: the longer the army sat outside, the
more ladders, towers, rams and catapults it had when it went in. That fits what is already here:

- Every season of `Besiege` the besiegers build engines out of their own men's time (and timber
  from the home county, if the user wants it to cost stores).
- **Ladder**: one more climbing point than the frontage gives, each.
- **Ram**: works the gate down; the men pushing it are exposed.
- **Siege tower**: docks at a segment and is a wide, safe climbing point, more frontage than a
  ladder, and slow.
- **Catapult**: knocks segments down into breaches. A breach is open ground with a gateway's
  frontage.

This is where the fortifications.json note ("siege engines widen the frontage rather than adding
another multiplier") gets built. The captain's `OnTheWalls` learns engines at the same time, so the
two stay in agreement.

## Stage 4 — the lord's own models

The user sends the real models one kind at a time. `SoldierFigure` becomes a figure per kind (the
same banner and livery rules, found the same way), `BattlefieldSquads` takes the figure from the
squad's unit, and the castle kit is swapped out rung by rung. No sim change.

## What the user is asked to decide

1. Are siege engines built during a siege, as in the original, or bought?
2. Do they cost timber from the county, or only the besiegers' time?
3. Can a lord storm without having sat down first (ladders only), or only after at least one season
   of siege?

## Models wanted, in order of use

Wall segment (timber and stone), corner tower, gatehouse with a gate that can be broken, ladder,
ram, siege tower, catapult — then the soldiers: peasant, spearman, archer, crossbowman, swordsman,
maceman, and a rider on a horse.
