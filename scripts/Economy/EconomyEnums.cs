/// <summary>Resources tracked by the economy in Phase 1. Weapons/horses join once the
/// Blacksmith (Phase 2) exists — no unused members before there's a producer for them.</summary>
public enum ResourceType
{
	Gold,
	Grain,
	Cattle,
	Wood,
	Stone,
	Iron,
}

/// <summary>Indexes GameBalance's per-season multiplier arrays; order must match them.</summary>
public enum Season
{
	Spring,
	Summer,
	Autumn,
	Winter,
}

/// <summary>Indexes GameBalance's tax tables; order must match them.</summary>
public enum TaxRate
{
	None,
	Low,
	Normal,
	High,
	Severe,
}

/// <summary>Indexes GameBalance's ration tables; order must match them.</summary>
public enum RationLevel
{
	Low,
	Normal,
	High,
}
