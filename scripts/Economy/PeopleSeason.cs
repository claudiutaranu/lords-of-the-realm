/// <summary>One season of a county's people, as it ended: how many there were, how healthy, and how
/// many were born and died in it. Written by the turn when the season is done and never again —
/// nothing else can say afterwards what a season was — and drawn by the people table.</summary>
public sealed class PeopleSeason
{
	/// <summary>The turn the season was, counted from the first (the people table works out the
	/// season and year from it).</summary>
	public int Turn;
	public int Population;
	public int Health;
	public int Born;
	public int Died;
}
