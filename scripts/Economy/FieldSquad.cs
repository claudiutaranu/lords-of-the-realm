using System.Collections.Generic;
using Godot;

/// <summary>One kind of soldier on one side of a <see cref="FieldBattle"/>, standing together — which
/// is how a company is written down, so a squad is a line of its roster. The lord's orders are
/// given to squads; the fighting is done by the men in them (<see cref="FieldSoldier"/>).
///
/// The squad is drawn up in ranks and files once, and every man keeps his place in them. When a
/// man falls, the man behind him in his file steps into his place and the file closes up behind —
/// nobody else moves. That is what keeps a line a line through a fight.</summary>
public sealed partial class FieldSquad
{
	private const float MetresPerRange = 10f;
	private const float WalkBase = 0.8f;
	private const float MetresPerSpeed = 0.35f;

	/// <summary>How far apart the men of a rank stand, in metres, when a figure is one man, and how
	/// much wider than deep a squad is drawn up.</summary>
	private const float Spacing = 1.1f;
	private const float Frontage = 2.5f;

	/// <summary>How deep a squad is drawn up at most. A big body of men is drawn up wider rather
	/// than deeper: the ranks behind the fourth or fifth only wait, and a mob ten deep is a mob
	/// most of which never reaches anybody.</summary>
	private const int MostRanks = 6;

	/// <summary>How tightly an enveloping file turns a corner of the enemy's line, in metres, and how
	/// far it stands off his flank — the same weapon's length two front ranks keep.</summary>
	private const float Turn = 1.5f;
	internal const float LineGap = 1.2f;

	/// <summary>The captain's place: not in the ranks, but behind them with the standard. He is a
	/// man of the squad like any other — one of those it brought — a little harder to kill, and when
	/// he falls the standard falls with him. How much harder, and how far behind the ranks he stands.</summary>
	internal const int CaptainSlot = -1;
	private const float CaptainHealth = 1.25f;
	private const float CaptainBehind = 1f;

	private int _files;
	private int _ranks;

	public FieldSquad(string unit, bool attacking, int brought, int menPerFigure, float health)
	{
		Unit = unit;
		Kind = Units.Of(unit);
		IsAttacking = attacking;
		Brought = brought;
		Gap = Spacing * Mathf.Sqrt(menPerFigure);
		if (brought > 0)
		{
			Captain = new FieldSoldier(this, 1, health * CaptainHealth) { Slot = CaptainSlot };
		}

		int ranked = 0;
		for (int left = brought - 1; left > 0; left -= menPerFigure)
		{
			Soldiers.Add(new FieldSoldier(this, Mathf.Min(menPerFigure, left), health) { Slot = ranked++ });
		}

		_files = Mathf.Max(1, Mathf.Max(Mathf.CeilToInt(Mathf.Sqrt(ranked * Frontage)),
			Mathf.CeilToInt(ranked / (float)MostRanks)));
		_ranks = Mathf.Max(1, Mathf.CeilToInt(ranked / (float)_files));
		if (Captain != null)
		{
			Soldiers.Add(Captain);
		}

		Measure();
	}

	/// <summary>The man who carries the standard. He falls like anybody else.</summary>
	public FieldSoldier Captain { get; }

	public string Unit { get; }

	public Units.Unit Kind { get; }

	public bool IsAttacking { get; }

	public int Brought { get; }

	/// <summary>The men still on their feet.</summary>
	public List<FieldSoldier> Soldiers { get; } = new();

	/// <summary>Where the squad's standard is, and which way its front rank is looking.</summary>
	public Vector2 At { get; internal set; }

	/// <summary>Where the standard stood a slice ago, so the screen can walk it between the two.</summary>
	public Vector2 Was { get; internal set; }

	public Vector2 Facing { get; internal set; }

	/// <summary>Where he was told to walk to, if anywhere.</summary>
	public Vector2? Goal { get; internal set; }

	/// <summary>Which way he was told to face once he gets where he is going, if the lord said.</summary>
	public Vector2? FaceGoal { get; internal set; }

	/// <summary>Whether he stands on a front the lord drew for him: he keeps it, and does not wheel
	/// toward an enemy coming up, until he is at grips or given another order.</summary>
	public bool KeepsFront { get; internal set; }

	/// <summary>How many men wide his front is, and so how many ranks deep he stands.</summary>
	public int Files => _files;

	/// <summary>Whom he was told to fall on, if anybody.</summary>
	public FieldSquad Target { get; internal set; }

	public bool IsMoving { get; internal set; }

	/// <summary>The enemy squad it is at grips with this slice, or loosing at. Null for neither.</summary>
	public FieldSquad InMelee { get; internal set; }

	public FieldSquad ShootingAt { get; internal set; }

	/// <summary>Whether any of his men had an enemy in reach last slice — not merely that an enemy
	/// squad is close by, which a squad passing another on its way to its own enemy also is.</summary>
	public bool IsFighting { get; internal set; }

	public int Fallen { get; internal set; }

	public int Standing => Brought - Fallen;

	public bool IsStanding => Standing > 0;

	public bool Shoots => Battle.Shoots(Kind);

	/// <summary>How far apart his figures stand, in metres.</summary>
	public float Gap { get; }

	/// <summary>How far out his ranks reach from the standard to either side, in metres.</summary>
	public float Reach => (_files * Gap / 2f) + 1f;

	/// <summary>How far forward of the standard his foremost rank still standing is, in metres. Two
	/// squads that close with each other stop when those ranks are a weapon's length apart — and
	/// when a rank is cut down to the last man, the squad that cut it steps up to the next.</summary>
	public float Front { get; private set; }

	/// <summary>Works <see cref="Front"/> out afresh: called whenever the ranks change, rather than on
	/// every one of the many times a slice asks for it.</summary>
	private void Measure()
	{
		int foremost = _ranks - 1;
		foreach (FieldSoldier man in Soldiers)
		{
			foremost = man.Slot < 0 ? foremost : Mathf.Min(foremost, man.Slot / _files);
		}

		Front = (((_ranks - 1) / 2f) - foremost) * Gap;
	}

	/// <summary>How far his ranks reach from the standard in a direction on the field: his front
	/// toward the enemy ahead of him, his half-width toward one on his flank, and between the two
	/// for anyone at an angle. What two squads close to, whichever way they meet — measured by the
	/// front alone, a squad that took another in the flank ended up standing inside it.</summary>
	public float Extent(Vector2 toward)
	{
		var right = new Vector2(-Facing.Y, Facing.X);
		return (Mathf.Abs(toward.Dot(Facing)) * Front) + (Mathf.Abs(toward.Dot(right)) * (_files - 1) * Gap / 2f);
	}

	/// <summary>How far his ranks reach forward of the standard, front rank and all.</summary>
	public float Depth => Front + (Gap / 2f) + 0.5f;

	/// <summary>How far he can loose, in metres.</summary>
	public float Range => Kind.Range * MetresPerRange;

	/// <summary>How fast he walks, in metres a second.</summary>
	public float Pace => WalkBase + (Kind.Speed * MetresPerSpeed);

	/// <summary>Where a place in the ranks is on the field.</summary>
	public Vector2 Place(int slot) => Posture(slot).At;

	/// <summary>Where a place in the ranks is on the field, and which way the man in it faces: across
	/// the front, the first rank nearest the enemy, each a hand's width off his mark so a squad is
	/// not a grid. The captain's place is behind them all, with the standard.
	///
	/// At grips with a narrower enemy, the men of the files that overhang him do not stand idle in
	/// their files: every one of them, rank after rank, takes the next place round the enemy — along
	/// his flank, then across his rear, a second ring outside the first when the first is full
	/// (<see cref="Encircle"/>). That is an encirclement; a file that only bent round his corner with
	/// its ranks still behind it was a spoke sticking out of him.</summary>
	public (Vector2 At, Vector2 Facing) Posture(int slot)
	{
		if (slot == CaptainSlot)
		{
			// Behind his men — or, with none of them left, where they stood.
			return Soldiers.Count > 1 ? (At - (Facing * (Depth + CaptainBehind)), Facing) : (At, Facing);
		}

		int file = slot % _files;
		int rank = slot / _files;
		var off = new Vector2(Mathf.Sin((slot + 1) * 4.79f), Mathf.Sin((slot + 1) * 11.8f)) * Gap * 0.1f;
		var right = new Vector2(-Facing.Y, Facing.X);
		Vector2 frontLine = At + (Facing * ((_ranks - 1) * Gap / 2f));

		if (InMelee is FieldSquad foe && Encircle(foe, file, rank, right, frontLine) is var (round, outward))
		{
			return (round + off, -outward);
		}

		return (frontLine + (right * (Across(file) + off.X)) - (Facing * ((rank * Gap) + off.Y)), Facing);
	}

	private float Across(int file) => (file - ((_files - 1) / 2f)) * Gap;

	/// <summary>Draws the squad up this many men wide — one long rank, or a deep column, or anything
	/// between — the men still standing taking the places front rank first, in the order they had.</summary>
	internal void Reform(int files)
	{
		var ranked = Soldiers.FindAll(man => man.Slot >= 0);
		ranked.Sort((a, b) => a.Slot.CompareTo(b.Slot));
		for (int i = 0; i < ranked.Count; i++)
		{
			ranked[i].Slot = i;
		}

		_files = Mathf.Clamp(files, 1, Mathf.Max(1, ranked.Count));
		_ranks = Mathf.Max(1, Mathf.CeilToInt(ranked.Count / (float)_files));
		Measure();
	}

	/// <summary>A man has fallen: the man behind him in his file steps into his place, and the one
	/// behind him into his, to the back of the file.</summary>
	internal void CloseUp(FieldSoldier fallen)
	{
		int empty = fallen.Slot;
		if (empty < 0)
		{
			return;
		}


		for (FieldSoldier behind = Behind(empty); behind != null; behind = Behind(empty))
		{
			int left = behind.Slot;
			behind.Slot = empty;
			empty = left;
		}

		Measure();
	}

	/// <summary>The man in the place behind this one in the file, standing or not: two of a file
	/// fallen in the same slice are buried one at a time, and stopping at the dead one left a hole in
	/// the front rank for good.</summary>
	private FieldSoldier Behind(int slot)
	{
		foreach (FieldSoldier man in Soldiers)
		{
			if (man.Slot == slot + _files)
			{
				return man;
			}
		}

		return null;
	}
}
