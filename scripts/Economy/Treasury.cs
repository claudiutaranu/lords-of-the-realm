/// <summary>A realm's money. One purse per realm, shared by every county it holds, which is why it
/// is an object rather than a number on the province: two counties have to be able to hold the same
/// one, and a value copied into each of them would be two purses that drift apart on the first tax
/// day.</summary>
public sealed class Treasury
{
	public int Gold;
}
