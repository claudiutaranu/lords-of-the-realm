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

/// <summary>How well the lords who are not the player play their own counties. Indexes
/// GameBalance's lord tables; order must match them. It is competence and not a handicap — see
/// <see cref="LordAI"/> for why that line is worth holding.</summary>
public enum Difficulty
{
	Easy,
	Medium,
	Hard,
}

/// <summary>What the lord allows each of his people to eat, as a multiple of what one of them needs
/// to be fed properly: Lords of the Realm's six steps, in its order (Livelihood.Tier reads it).
///
/// Multiples and not adjectives, because that is the decision: a county fed double is a county that
/// eats twice the bread and thinks the better of its lord for it, and both halves of that have to be
/// a number the player can weigh against his granary.</summary>
public enum RationLevel
{
	None,
	Quarter,
	Half,
	Normal,
	Double,
	Triple,
}

/// <summary>What one of a province's fields is under this year. A field is the unit the land is
/// worked in: grain is sown on it and reaped off it, a herd grazes on it, or it is left to rest.
/// Order matters only to whatever iterates it; nothing indexes a balance table by it.</summary>
public enum FieldUse
{
	Fallow,
	Grain,
	Pasture,
	/// <summary>Torn up by a flood: nothing grows on it and nothing grazes it until the reclaimers
	/// have put it right, and the lord cannot order it under anything else in the meantime.</summary>
	Waste,
}
