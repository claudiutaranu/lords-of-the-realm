using System.Collections.Generic;
using Godot;

/// <summary>The smithy: which weapon the forge makes. One kind at a time, every season, out of the
/// province's own stores (Smithy) — the lord chooses what, and the smiths the labour bar sends
/// choose how many. Nothing is ordered and waited on, so nothing here is paid for up front.</summary>
public partial class BlacksmithPage : ProductionPage
{
	protected override string DataPath => "res://data/weapons.json";

	protected override string RoomName => "Blacksmith";

	protected override string Tagline => "Forge stronger armies";

	// Read off a still of the forge: each sign hangs over the rack it names.
	protected override Dictionary<string, Vector2> SignSpots { get; } = new()
	{
		["bow"] = new Vector2(0.105f, 0.330f),
		["crossbow"] = new Vector2(0.225f, 0.470f),
		["sword"] = new Vector2(0.410f, 0.470f),
		["spear"] = new Vector2(0.600f, 0.560f),
		// Held clear of the panel in the corner, not just off the racks: the panel grows taller as
		// the window narrows and the blurb wraps, and these two are the ones it reaches first.
		["horse"] = new Vector2(0.745f, 0.470f),
		["mace"] = new Vector2(0.920f, 0.450f),
	};

	// The smith labels his own wall: each sign hangs over its rack.
	protected override void BuildChoosers() => HangSigns();

	private ProvinceDefinition _definition;
	private GameBalance _balance;
	private Season _season;
	private Item _chosen;

	/// <summary>The land and the season the county is working in, which choosing a weapon needs: the
	/// hands are dealt again the moment the forge wants some, or it would stand cold until the lord
	/// thought to touch the labour bar.</summary>
	public void Brief(ProvinceDefinition definition, GameBalance balance, Season season)
	{
		_definition = definition;
		_balance = balance;
		_season = season;
		ShowDetail();
	}

	// Open on what the forge is making, so the lord walks in to see it at work.
	protected override void Opened() =>
		Choose(Items.Find(item => item.Key == Province.Forging) ?? (Items.Count > 0 ? Items[0] : null));

	// The forge is never busy: a new choice simply replaces the old one.
	protected override (string Making, int TurnsLeft) InHand => ("", 0);

	protected override string BusyLine => "";

	protected override string OrderLine => AtTheAnvil(_chosen) ? "Let the forge go cold" : "Forge these";

	protected override string TermsLine(Item item) => "Each needs";

	protected override string DeliveryLine(Item item)
	{
		if (_balance == null || Province == null)
		{
			return "";
		}

		if (!AtTheAnvil(item))
		{
			return $"{_balance.SmithsPerWeapon} smiths make one a season";
		}

		int made = Mathf.Min(Province.SmithWorkers / Mathf.Max(1, _balance.SmithsPerWeapon),
			Smithy.Affordable(Province, item.Key));
		return $"{made:N0} a season, {Province.SmithWorkers:N0} at the anvil";
	}

	// Chosen with empty stores too: the forge waits for iron rather than the lord waiting to choose.
	protected override bool CanAfford(Item item) => true;

	protected override void Chosen(Item item) => _chosen = item;

	protected override string IconFor(string purse) => purse switch
	{
		"grain" => "food",
		"cattle" => "livestock",
		_ => purse,
	};

	protected override int Held(string purse) => Province?.Stored(purse) ?? 0;

	protected override void Pay(string purse, int amount) => Province.Add(purse, -amount);

	protected override void Begin(Item item, int count) => Province.Forging = item?.Key ?? "";

	protected override void PlaceOrder()
	{
		if (_chosen == null)
		{
			return;
		}

		Begin(AtTheAnvil(_chosen) ? null : _chosen, 0);
		if (_definition != null)
		{
			Labour.Deal(Province, _definition, _balance, _season);
		}

		Refresh();
	}

	private bool AtTheAnvil(Item item) => item != null && Province?.Forging == item.Key;
}
