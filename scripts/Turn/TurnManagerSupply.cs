using System.Collections.Generic;
using Godot;

/// <summary>The lord's carts: any of his stores sent from one of his counties to another over the
/// map's own roads, a march's reach a season, unloaded where they are going (Shipment).</summary>
public partial class TurnManager
{
	/// <summary>Every cart on the road, the player's only: the rivals feed each county off its own.</summary>
	public List<Shipment> Shipments { get; private set; } = new();

	/// <summary>The road the map lays out and the villages it runs between, kept from
	/// <see cref="Survey"/> for the carts. Null without a map, and then nothing can be sent.</summary>
	private LordsCampaign.Way _way;
	private Dictionary<string, Vector2> _towns;

	/// <summary>How close another lord's company has to come to a cart to take it: the same ground a
	/// gate is held from.</summary>
	private float _gateReach;

	/// <summary>How many seasons a cart from one county to another is on the road, or -1 where there
	/// is no road (or no map). What the panel shows before the lord sends anything, worked out by the
	/// same walk <see cref="Haul"/> makes, so the two cannot disagree.</summary>
	public int SupplySeasons(string from, string to)
	{
		if (_way == null || !_towns.TryGetValue(from, out Vector2 start) || !_towns.TryGetValue(to, out Vector2 end))
		{
			return -1;
		}

		List<(Vector2 At, float Spent)> road = _way(start, end);
		int seasons = 0;
		int reached = -1;
		while (road.Count > 0 && reached < road.Count - 1)
		{
			int halt = Halt(road, reached < 0 ? 0f : road[reached].Spent);
			if (halt <= reached)
			{
				return -1; // a step longer than a season's legs: the cart would stand there for ever
			}

			reached = halt;
			seasons++;
		}

		return road.Count == 0 ? -1 : seasons;
	}

	/// <summary>The last step of a road a cart that has already come <paramref name="from"/> along it
	/// reaches this season.</summary>
	private int Halt(List<(Vector2 At, float Spent)> road, float from)
	{
		int halt = -1;
		for (int step = 0; step < road.Count; step++)
		{
			if (road[step].Spent - from <= _balance.MarchReach)
			{
				halt = step;
			}
		}

		return halt;
	}

	/// <summary>Loads a cart and sends it. The goods leave the source's stores now, which is what
	/// makes it a decision: until they arrive, neither county can use them. Null, and nothing taken,
	/// where it cannot go — not both the lord's counties, nothing to send, more than there is, gold
	/// (the realm keeps one purse, it has nowhere to be carted to) or no road between them.</summary>
	public Shipment Dispatch(string from, string to, Dictionary<string, int> goods)
	{
		ProvinceEconomy source = GetProvince(from);
		if (source == null || GetProvince(to) == null || from == to || SupplySeasons(from, to) < 0)
		{
			return null;
		}

		var load = new Dictionary<string, int>();
		foreach ((string store, int amount) in goods)
		{
			if (amount < 0 || amount > source.Stored(store) || store is "gold" or "people")
			{
				return null;
			}

			if (amount > 0)
			{
				load[store] = amount;
			}
		}

		if (load.Count == 0)
		{
			return null;
		}

		foreach ((string store, int amount) in load)
		{
			source.Add(store, -amount);
		}

		Vector2 at = _towns[from];
		var cart = new Shipment { From = from, To = to, Goods = load, X = at.X, Y = at.Y };
		Shipments.Add(cart);
		return cart;
	}

	/// <summary>Every cart's season: a march's reach along the road to where it is going, unloaded
	/// if it gets there. A county it was bound for that is not the lord's any more turns it for home;
	/// with neither end his, it has nowhere to go and is lost. Another lord's company standing
	/// anywhere on the road it rolls this season takes it, goods and all.
	///
	/// Run by <see cref="AdvanceTurn"/>, and on its own by the checks, which could not otherwise tell
	/// a cart's grain from a season's harvest in the barn.
	/// ponytail: what a company takes is spoiled, not carried into its lord's stores; pay it into his
	/// county's barn if raiding carts is ever worth a rival's while.</summary>
	public void Haul()
	{
		if (_way == null)
		{
			return;
		}

		foreach (Shipment cart in new List<Shipment>(Shipments))
		{
			if (GetProvince(cart.To) == null)
			{
				if (GetProvince(cart.From) == null)
				{
					Lost(cart, "there is no county of ours left at either end of their road");
					continue;
				}

				Told(cart.To, "supply-turned", new Dictionary<string, string>
				{
					["county"] = cart.To,
					["from"] = cart.From,
				});
				cart.To = cart.From;
			}

			List<(Vector2 At, float Spent)> road = _way(new Vector2(cart.X, cart.Y), _towns[cart.To]);
			int halt = Halt(road, 0f);
			for (int step = 0; step <= halt; step++)
			{
				if (Waylaid(road[step].At))
				{
					Lost(cart, "another lord's soldiers caught them on the road");
					break;
				}
			}

			if (!Shipments.Contains(cart) || halt < 0)
			{
				continue;
			}

			cart.X = road[halt].At.X;
			cart.Y = road[halt].At.Y;
			if (halt < road.Count - 1)
			{
				continue;
			}

			ProvinceEconomy there = GetProvince(cart.To);
			foreach ((string store, int amount) in cart.Goods)
			{
				there.Add(store, amount);
			}

			Shipments.Remove(cart);
			Told(cart.To, "supply-arrived", new Dictionary<string, string>
			{
				["county"] = cart.To,
				["from"] = cart.From,
				["goods"] = Manifest(cart),
			});
		}
	}

	/// <summary>Whether a company of another lord — not the one the player is sworn to — stands close
	/// enough to this point of road to stop a cart on it.</summary>
	private bool Waylaid(Vector2 at)
	{
		foreach (FieldArmy army in Armies())
		{
			string realm = RealmOf(army);
			if (army.Strength > 0 && realm.Length > 0 && realm != _playerRealm
				&& realm != Diplomacy.AllyOf(_playerRealm) && _campaign.Pixel(army).DistanceTo(at) <= _gateReach)
			{
				return true;
			}
		}

		return false;
	}

	private void Lost(Shipment cart, string why)
	{
		Shipments.Remove(cart);
		Told(cart.To, "supply-lost", new Dictionary<string, string>
		{
			["from"] = cart.From,
			["goods"] = Manifest(cart),
			["why"] = why,
		});
	}

	/// <summary>The load as the steward reads it out: "200 sacks of grain, 10 head of cattle and 50 wood".</summary>
	private static string Manifest(Shipment cart)
	{
		var said = new List<string>();
		foreach ((string store, int amount) in cart.Goods)
		{
			said.Add(store switch
			{
				"grain" => $"{amount:N0} sacks of grain",
				"cattle" => $"{amount:N0} head of cattle",
				"wood" => $"{amount:N0} loads of timber",
				"stone" or "iron" => $"{amount:N0} {store}",
				_ => $"{amount:N0} {store}s",
			});
		}

		return said.Count == 1 ? said[0] : $"{string.Join(", ", said.GetRange(0, said.Count - 1))} and {said[^1]}";
	}
}
