using Godot;

/// <summary>One man on the field — or one file of men, where the field holds more than can be drawn
/// (<see cref="FieldBattle.MostFigures"/>): then a figure is a few men standing one behind the
/// other, and his health is all of theirs, and they go down one at a time.
///
/// He has his own place, his own enemy and his own health, and the day is the sum of what happens
/// to men like him: a swing lands or it does not, by his arm against the other man's guard, and
/// the man it lands on is one man, not a share of a squad.</summary>
public sealed class FieldSoldier
{
	public FieldSoldier(FieldSquad squad, int men, float health)
	{
		Squad = squad;
		Men = men;
		MostHealth = men * health;
		Health = MostHealth;
	}

	public FieldSquad Squad { get; }

	/// <summary>How many men this figure still is. One, unless the field is a big one.</summary>
	public int Men { get; internal set; }

	public float Health { get; internal set; }

	public float MostHealth { get; }

	public Vector2 At { get; internal set; }

	/// <summary>Where he stood a slice ago, so the screen can walk him between the two.</summary>
	public Vector2 Was { get; internal set; }

	public Vector2 Facing { get; internal set; }

	/// <summary>The man he has picked out to fight, if anybody.</summary>
	public FieldSoldier Foe { get; internal set; }

	/// <summary>What makes him one man and not a peg in a board (FieldBattle.Loosen): how long after an
	/// order he takes to move off, how much faster or slower than his company he walks, and how far
	/// from his exact place in the ranks he stands. So a company does not set off as one, some fall a
	/// little behind, and the ranks are ranks of men, not a grid.</summary>
	public float Slow { get; internal set; }

	public float Stride { get; internal set; } = 1f;

	public Vector2 Loose { get; internal set; }

	/// <summary>How long he still stands before stepping off on the order just given.</summary>
	public float Waiting { get; internal set; }

	/// <summary>When he last swung or loosed, on the battle's clock — what the screen lunges him by.</summary>
	public float Struck { get; internal set; } = -100f;

	public bool IsMoving { get; internal set; }

	public bool IsStanding => Men > 0;

	/// <summary>His place in the ranks (<see cref="FieldSquad.Place"/>). It changes only when the man
	/// in front of him falls and he steps up into the gap.</summary>
	internal int Slot { get; set; }

	/// <summary>Time until he can swing again, or loose again.</summary>
	internal float Ready { get; set; }
}
