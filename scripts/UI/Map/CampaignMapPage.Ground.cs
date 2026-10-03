using Godot;

/// <summary>Where things stand on the map: a county's seat, where a company's banner is drawn, what
/// is under a pixel, and who an army halting there would meet.</summary>
public partial class CampaignMapPage
{
	/// <summary>Where freshly raised men stand, from their county's seat: north-west of the town,
	/// opposite the corner the castle is built on.</summary>
	private static readonly Vector2 MusterGround = new(-37f, -37f);

	/// <summary>How far round the town each company after the first musters, in radians, so two
	/// banners raised in the same county are two things to point at.</summary>
	private const float MusterApart = 0.9f;

	/// <summary>How close two of a lord's companies have to halt before he is asked whether they are
	/// one army, in map pixels. A field, not a county: men who cannot see each other are not
	/// standing together.</summary>
	private const float JoinReach = 70f;

	private Vector2 SeatOf(string county)
	{
		foreach (ProvinceData province in _provinces)
		{
			if (province.Name == county)
			{
				return province.MapPosition;
			}
		}

		return Vector2.Zero;
	}

	/// <summary>Where a company is standing. Men just raised have never been put anywhere, and they
	/// are at their own county's seat — which is where they were raised.</summary>
	/// <summary>Where a company's banner is drawn: where it stands, unless that is a town's own square,
	/// where the village and its ring would swallow it — which is exactly where a company that has
	/// just carried a town is standing, and two hundred Swiss "vanished" the day they won. Drawn on
	/// the town's muster ground instead; the company itself is still in the square, for every
	/// march, fight and gate that reads its position.</summary>
	private Vector2 BannerPixel(FieldArmy army)
	{
		Vector2 at = ArmyPixel(army);
		string town = _world.TownAt(at);
		ProvinceData village = town.Length == 0 ? null : _provinces.Find(province => province.Name == town);
		return village == null ? at
			: village.TownPosition + (MusterGround.Normalized() * BesideTheTown).Rotated((army.Id - 1) * MusterApart);
	}

	/// <summary>How far off a town's square a company standing in it is drawn: past the village's own
	/// ring, and inside the clearing the woods leave round it (TownRing + 14), so the banner is
	/// neither in the houses nor in the trees.</summary>
	private const float BesideTheTown = MapDecoration.TownRing + 8f;

	private Vector2 ArmyPixel(FieldArmy army)
	{
		var at = new Vector2(army.X, army.Y);
		if (at != Vector2.Zero)
		{
			return at;
		}

		// Men who have never been sent anywhere are mustered beside their own town rather than on top
		// of its pin — the pin is what the lord clicks to read the county, and a banner standing in
		// it would be in the way of the one thing that square of ground is already for. Each company
		// takes its own ground around the town, so a second one raised there is a second banner the
		// lord can point at rather than a figure hidden inside the first.
		return SeatOf(army.Home) + MusterGround.Rotated((army.Id - 1) * MusterApart);
	}

	/// <summary>Whether halting here means a fight: the lord's own men, standing on the seat of a
	/// county that is not his. Its town and its castle are the same square of ground — the walls are
	/// raised on the seat — so one question covers both, and everything else in the county is ground
	/// to be walked over.</summary>
	private bool Contested(FieldArmy army, string county, Vector2 at)
	{
		if (county.Length == 0 || army.Strength == 0 || _turnManager.RealmOf(army) != _playerRealm
			|| _world.TownAt(at) != county)
		{
			return false;
		}

		ProvinceEconomy theirs = _turnManager.AnyProvince(county);
		return theirs == null || theirs.Realm != _playerRealm;
	}

	/// <summary>What sending a company onto another of the lord's own comes to, told before he says
	/// whether they are to be one army. Two of the same county are the usual case, and naming it three
	/// times in one sentence reads like a ledger rather than like a lord being told something.</summary>
	private static string JoinReading(FieldArmy coming, FieldArmy standing, string county)
	{
		string who = coming.Home == standing.Home
			? $"{coming.Strength:N0} men of {coming.Home} will halt beside {standing.Strength:N0} more of their own"
			: $"{coming.Strength:N0} men of {coming.Home} will halt beside {standing.Strength:N0} of {standing.Home}";
		return $"{who}, in {county}. Under one banner they march as one company, with the ground the slower "
			+ "of them has left.";
	}

	/// <summary>The lord's own company standing where another of his is being sent, which the two could
	/// be joined to, or null. A hired band is never joined to anybody (TurnManager.Merge).</summary>
	private FieldArmy Joinable(FieldArmy army, string county, Vector2 at)
	{
		if (army.IsHired)
		{
			return null;
		}

		foreach (FieldArmy other in _turnManager.Armies())
		{
			if (other != army && other.Strength > 0 && !other.IsHired && other.County == county
				&& _turnManager.RealmOf(other) == _playerRealm && ArmyPixel(other).DistanceTo(at) <= JoinReach)
			{
				return other;
			}
		}

		return null;
	}

	/// <summary>Another lord's company standing where this one has just halted, or null — the one a
	/// lord who marched onto its banner came to fight.</summary>
	private FieldArmy Foe(FieldArmy army)
	{
		FieldArmy nearest = null;
		float best = JoinReach;
		foreach (FieldArmy other in _turnManager.Armies())
		{
			string realm = _turnManager.RealmOf(other);
			if (other.Strength <= 0 || realm == _playerRealm || realm.Length == 0)
			{
				continue;
			}

			float far = ArmyPixel(other).DistanceTo(ArmyPixel(army));
			if (far <= best)
			{
				best = far;
				nearest = other;
			}
		}

		return nearest;
	}

	private string CountyNameAt(Vector2 pixel)
	{
		int county = _world.CountyAt(pixel);
		return county >= 0 && county < _provinces.Count ? _provinces[county].Name : "";
	}

	/// <summary>The county field under a map pixel, for a march to trample: the county's own plots,
	/// read off whichever county the ground is in.</summary>
	private (string County, int Field) FieldUnder(Vector2 pixel)
	{
		string county = CountyNameAt(pixel);
		return county.Length == 0 ? ("", -1) : (county, _world.PlotAt(county, pixel));
	}

	/// <summary>The diggings under a map pixel, by the labour job that works it.</summary>
	private (string County, string Site) SiteUnder(Vector2 pixel) => _world.SiteAt(pixel) switch
	{
		(string county, MapDecoration.SiteKind.Wood) => (county, Labour.Wood),
		(string county, MapDecoration.SiteKind.Stone) => (county, Labour.Stone),
		(string county, MapDecoration.SiteKind.Iron) => (county, Labour.Iron),
		_ => ("", ""),
	};
}
