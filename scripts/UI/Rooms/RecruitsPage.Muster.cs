using System.Collections.Generic;

/// <summary>The order placed: the men counted into the muster and raised as a new company at the
/// seat.</summary>
public partial class RecruitsPage
{
	/// <summary>The order in the yard is not "raise these men", it is "put them on the list". Nothing
	/// is paid for and nobody is raised until the muster is called — so a lord picks his forty
	/// spearmen and his twenty archers, looks at what the whole company will cost him, and only then
	/// commits to it. Raising them one card at a time gave him no moment to see the total.</summary>
	protected override void PlaceOrder()
	{
		if (_lit == null)
		{
			return;
		}

		// A band is hired once: while it stands in the county and is not on the table already. Its
		// price is for the company, so a second press put a second hundred men on the same 5,500, and
		// the card stayed lit after the muster for a third.
		if (_lit.Key == _band?.Key
			&& (_muster.ContainsKey(_lit.Key) || Mercenaries.Standing(Province)?.Key != _lit.Key))
		{
			return;
		}

		int count = Sized(_lit) ? OrderSize : _lit.Batch;
		if (count <= 0 || !Affordable(_lit.Key, count))
		{
			return;
		}

		_muster[_lit.Key] = _muster.GetValueOrDefault(_lit.Key) + count;
		ShowDetail();
	}

	/// <summary>Raised once a muster is called and the company stands: the lord goes out to it.</summary>
	public event System.Action ArmyRaised;

	/// <summary>Calls the muster: everything on the list is paid for and raised, in one go. This is
	/// the moment the army exists — before it, nothing has been spent and nobody has left the
	/// fields.</summary>
	private void Raise()
	{
		foreach ((string purse, int owed) in Bill(_muster))
		{
			if (Held(purse) < owed)
			{
				return; // the county cannot carry it after all; nothing is taken
			}
		}

		foreach ((string purse, int owed) in Bill(_muster))
		{
			Pay(purse, owed);
		}

		// One muster, one company: everything raised together falls in under one banner, and it is a
		// NEW banner. What the yard turns out does not walk into whatever army the county already
		// has standing — the lord decides whether the two become one (see the map's join).
		_raised = null;
		foreach ((string key, int men) in _muster)
		{
			Item item = Items.Find(card => card.Key == key);
			if (item != null)
			{
				Begin(item, men);
			}
		}

		_raised = null;
		_muster.Clear();
		ShowDetail();
		Refresh();
		ArmyRaised?.Invoke();
	}

	private readonly Dictionary<string, int> _muster = new();

	/// <summary>The company this muster is falling in under, made by the first card that raises
	/// anybody and let go the moment the muster is over. Forty spears and twenty bows ordered
	/// together are one army, not two.</summary>
	private FieldArmy _raised;

	protected override void Begin(Item item, int count)
	{
		// Raised the day they are paid for. Hired men fall in as the band they are: a hundred Scots
		// stand on the roster as Scottish Pikemen, on their own bars, and not as a hundred of the
		// county's spearmen nobody can tell apart from the ones it raised. A band is its own company,
		// under its own banner, whatever else is raised the same day.
		if (item.Key == _band?.Key)
		{
			Province.Raise(GameBalance.Engine.MarchReach).Men[item.Key] = count;
			Mercenaries.Hire(Province, count);
			return;
		}

		_raised ??= Province.Raise(GameBalance.Engine.MarchReach);
		_raised.Men[item.Key] = _raised.Men.GetValueOrDefault(item.Key) + count;
	}
}
