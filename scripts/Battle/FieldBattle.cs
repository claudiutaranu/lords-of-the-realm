using System.Collections.Generic;
using Godot;

/// <summary>A battle in the open, fought by the lord himself instead of reckoned by his captain.
///
/// Lords of the Realm II offered both, and so does this: <see cref="Battle"/> is the captain, and
/// this is the field he would otherwise have fought on. It is the whole of the fighting and none of
/// the drawing, stepped in fixed slices of time so the check can run it without a screen, and it
/// ends in the same <see cref="Battle.Result"/> the captain's reckoning does — so the ledger, the
/// map and the panel afterwards cannot tell which of the two decided the day.
///
/// The men are ordered in squads, one to a kind of soldier on each side, which is how a company is
/// written down, and they fight man against man. They are decided by the same four bars on the
/// barracks cards, the same ground under the defender and the same legs under the attacker. What
/// the lord adds is where they stand and whom they fall on.
///
/// Unlike the captain's reckoning, nobody breaks: the field is won when the last man of the other
/// side is down (the user's call — a day watched man by man is not over while the enemy still has
/// men on it). A day that ends at sundown with both sides standing is won by nobody.</summary>
public sealed partial class FieldBattle
{
	/// <summary>How long a slice of the fight is, in seconds. The screen steps it this finely too, so
	/// a battle watched and a battle checked are the same battle.</summary>
	public const float Slice = 0.1f;

	/// <summary>The most figures a field is drawn and fought with. Past it a figure is a file of
	/// several men (<see cref="FieldSoldier"/>), the same number to a file on both sides.</summary>
	public const int MostFigures = 480;

	/// <summary>The day's dice, unless the check throws others. Fixed, so the same day fought the
	/// same way comes out the same, which is what lets the check hold it to anything.</summary>
	private const ulong Seed = 1268;

	/// <summary>How far apart the two lines are drawn up, and how the squads stand in them, in
	/// metres. Far enough that archers must walk before they can shoot, near enough that nobody
	/// waits a minute for the first blow.</summary>
	public const float Gap = 110f;
	private const float RowDepth = 22f;
	private const float SquadSpacing = 6f;

	/// <summary>How near a squad sent to a spot must come to count as there, in metres.</summary>
	private const float Arrived = 1.5f;

	/// <summary>How near an enemy may come to a squad left without orders before it goes for him,
	/// measured from the edge of its ranks, in metres.</summary>
	private const float Challenge = 15f;

	/// <summary>How far ahead of its front rank a squad sent at an enemy stops from the nearest of
	/// his men: inside a weapon's length, so the men at the front can reach him.</summary>
	private const float Reached = 1.3f;

	/// <summary>The gap between two bodies of men that still counts as hand to hand.</summary>
	private const float Contact = 2f;

	/// <summary>A man who carries a bow fights hand to hand at half his bar: it is a number for what
	/// he does at eighty paces, and nothing about a longbow is any use at one.</summary>
	private const float ShooterInMelee = 0.5f;

	private readonly GameBalance _balance;
	private readonly float _ground;
	private readonly float _vigour;
	private readonly int _attackBrought;
	private readonly int _defenceBrought;
	private readonly int _menPerFigure;
	private readonly RandomNumberGenerator _dice;

	/// <summary>Draws the two lines up facing each other: the attacker to the south, the defender
	/// to the north, each with its foot in front and its bows behind.</summary>
	public FieldBattle(Dictionary<string, int> attacker, Defenders against, bool marched, GameBalance balance,
		ulong seed = Seed, bool atTheWalls = false)
	{
		_balance = balance;
		// An assault is fought by the castle's own men inside its walls, where they would draw up.
		Wall = atTheWalls ? new FieldWall(new Vector2(0f, -Gap / 2f), against, balance) : null;
		_dice = new RandomNumberGenerator { Seed = seed };
		(_ground, _vigour) = Battle.FieldOdds(against, marched, balance);
		_menPerFigure = Mathf.Max(1, Mathf.CeilToInt((ProvinceEconomy.Men(attacker) + ProvinceEconomy.Men(against.Field))
			/ (float)MostFigures));
		DrawUp(attacker, true);
		DrawUp(against.Field, false);
		if (Wall != null)
		{
			Garrison();
		}
		foreach (FieldSquad squad in Squads)
		{
			Loosen(squad);
			for (int i = 0; i < squad.Soldiers.Count; i++)
			{
				squad.Soldiers[i].At = squad.Place(squad.Soldiers[i].Slot);
				squad.Soldiers[i].Was = squad.Soldiers[i].At;
				squad.Soldiers[i].Facing = squad.Facing;
			}

			squad.Was = squad.At;

			if (squad.IsAttacking)
			{
				_attackBrought += squad.Brought;
			}
			else
			{
				_defenceBrought += squad.Brought;
			}
		}

		// The besiegers' engines, behind their line: on the field like a company, but not counted in it.
		if (Wall != null)
		{
			Engines(against);
		}

		// Nobody on one side or the other is no fight at all, the same as the captain's reckoning.
		if (_attackBrought == 0 || _defenceBrought == 0)
		{
			IsDefenceFallen = _defenceBrought == 0 && _attackBrought > 0;
			IsOver = true;
		}
	}

	public List<FieldSquad> Squads { get; } = new();

	/// <summary>How long a man may take to step off on an order, the more the further back his rank;
	/// how much his walk may differ from his company's; and how far off his exact place he stands.
	/// The seeded dice, so a day fought again is the same day. [I] — after Manor Lords' companies,
	/// which move as men and not as a block.</summary>
	private const float SlowestStart = 0.8f;
	private const float SlowerPerRank = 0.12f;
	private const float StrideSpread = 0.1f;
	private const float LooseReach = 0.35f;

	private void Loosen(FieldSquad squad)
	{
		if (squad.Kind.IsEngine)
		{
			return;
		}

		foreach (FieldSoldier man in squad.Soldiers)
		{
			int rank = man.Slot / Mathf.Max(1, squad.Files);
			man.Slow = (_dice.Randf() * SlowestStart) + (rank * SlowerPerRank);
			man.Stride = 1f + ((_dice.Randf() * 2f) - 1f) * StrideSpread;
			man.Loose = new Vector2((_dice.Randf() * 2f) - 1f, (_dice.Randf() * 2f) - 1f) * LooseReach;
		}
	}

	/// <summary>The castle's wall, when this is an assault on it; null in the open field.</summary>
	public FieldWall Wall { get; }

	/// <summary>How long the day is: a day at the walls is shorter, as the captain's is, and an assault
	/// that has not cleared them by nightfall has failed.</summary>
	private float DayLong => Wall == null ? _balance.FieldDaySeconds : _balance.AssaultDaySeconds;

	/// <summary>Whether each side's squads take their orders from the captain rather than from the
	/// lord. The defence always does; the lord may hand the attack over at any moment.</summary>
	public bool IsAttackCaptained { get; set; }

	public bool IsDefenceCaptained { get; set; } = true;

	/// <summary>How long they have been at it, in seconds.</summary>
	public float Clock { get; private set; }

	/// <summary>Whether the last man of a side is down.</summary>
	public bool IsAttackFallen { get; private set; }

	public bool IsDefenceFallen { get; private set; }

	public bool IsOver { get; private set; }

	/// <summary>What share of what it brought each side has lost. What the lord watches.</summary>
	public float AttackLoss => Loss(true, _attackBrought);

	public float DefenceLoss => Loss(false, _defenceBrought);

	/// <summary>One slice of the day.</summary>
	public void Step()
	{
		if (IsOver)
		{
			return;
		}

		Clock += Slice;
		Wall?.Pound(Slice, Squads);
		foreach (FieldSquad squad in Squads)
		{
			squad.Was = squad.At;
		}

		foreach (FieldSquad squad in Squads)
		{
			if (squad.IsStanding && (squad.IsAttacking ? IsAttackCaptained : IsDefenceCaptained))
			{
				Captain(squad);
			}
		}

		foreach (FieldSquad squad in Squads)
		{
			Walk(squad);
		}

		Spread();
		Engage();
		Fight();
		HoldTheFlag();

		IsAttackFallen = Lost(true) >= _attackBrought;
		IsDefenceFallen = Lost(false) >= _defenceBrought || IsFlagTaken;
		IsOver = IsAttackFallen || IsDefenceFallen || Clock >= DayLong;
	}

	/// <summary>The day, fought to its end by both captains. What the check runs.</summary>
	public Battle.Result Fought()
	{
		IsAttackCaptained = true;
		IsDefenceCaptained = true;
		while (!IsOver)
		{
			Step();
		}

		return Result();
	}

	/// <summary>What the day cost, in the captain's own terms: every man who fell, squad by squad.</summary>
	public Battle.Result Result()
	{
		var attackLost = new Dictionary<string, int>();
		var defenceLost = new Dictionary<string, int>();
		foreach (FieldSquad squad in Squads)
		{
			if (squad.Fallen > 0 && !squad.Kind.IsEngine)
			{
				Dictionary<string, int> lost = squad.IsAttacking ? attackLost : defenceLost;
				lost[squad.Unit] = lost.GetValueOrDefault(squad.Unit) + squad.Fallen;
			}
		}

		return new Battle.Result(IsDefenceFallen && !IsAttackFallen, attackLost, defenceLost,
			Mathf.RoundToInt(Clock / _balance.FieldRoundSeconds));
	}

	private float Lost(bool attacking)
	{
		float lost = 0f;
		foreach (FieldSquad squad in Squads)
		{
			lost += squad.IsAttacking == attacking && !squad.Kind.IsEngine ? squad.Fallen : 0f;
		}

		return lost;
	}

	private float Loss(bool attacking, int brought) =>
		brought == 0 ? 0f : Mathf.Clamp(Lost(attacking) / brought, 0f, 1f);
}
