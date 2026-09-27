using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>The lord's carts on the map: the first thing on each one, on the road where it stands.
///
/// Read off the ledger every frame rather than told when a cart sets out or comes in. There are a
/// handful of them at most, and a mark the page forgot to put up after a battle or a load is a cart
/// the lord thinks has gone missing.
/// ponytail: a cart is a flat mark that jumps to where it halted behind the season's curtain, not a
/// figure walked along its road; give it a model and MapDecoration.WalkArmy if carts want watching.</summary>
public partial class CartMarkers : Control
{
	private const int Side = 44;

	private CampaignMap3D _world;
	private System.Func<List<Shipment>> _carts;

	public void Watch(CampaignMap3D world, System.Func<List<Shipment>> carts)
	{
		_world = world;
		_carts = carts;
		MouseFilter = MouseFilterEnum.Ignore;
	}

	public override void _Process(double delta)
	{
		if (_carts == null)
		{
			return;
		}

		List<Shipment> carts = _carts();
		while (GetChildCount() < carts.Count)
		{
			TextureRect mark = Chrome.Icon("food", Side);
			mark.Size = new Vector2(Side, Side);
			AddChild(mark);
		}

		for (int index = GetChildCount() - 1; index >= carts.Count; index--)
		{
			Node spare = GetChild(index);
			RemoveChild(spare);
			spare.QueueFree();
		}

		for (int index = 0; index < carts.Count; index++)
		{
			TextureRect mark = GetChild<TextureRect>(index);
			Shipment cart = carts[index];
			// Marked by what it carries: the lord should be able to tell his herd on the road from his
			// harvest, and both from the timber for the wall.
			Texture2D look = GD.Load<Texture2D>($"{Chrome.IconDirectory}/{SupplyPanel.IconOf(cart.Goods.Keys.First())}.png");
			if (mark.Texture != look)
			{
				mark.Texture = look;
			}

			mark.Visible = _world.TryScreenPosition(new Vector2(cart.X, cart.Y), out Vector2 onScreen);
			mark.Position = onScreen - (mark.Size / 2f);
		}
	}
}
