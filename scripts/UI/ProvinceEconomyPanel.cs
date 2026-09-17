using System;
using System.Collections.Generic;
using Godot;

/// <summary>Worker allocation rows for a selected province, with a live per-industry yield
/// preview that recomputes on every +/- click — no End Turn needed (design doc point 32).
/// Caller hides this entirely for provinces with no ProvinceEconomy (Northern Watch).</summary>
public partial class ProvinceEconomyPanel : VBoxContainer
{
	private static readonly (string Label, ResourceType Type)[] Industries =
	{
		("Grain", ResourceType.Grain),
		("Cattle", ResourceType.Cattle),
		("Wood", ResourceType.Wood),
		("Stone", ResourceType.Stone),
		("Iron", ResourceType.Iron),
	};

	/// <summary>Raised when a worker is moved, so anything else showing the same projection — the
	/// sidebar's yields — follows the change instead of going stale until the next turn.</summary>
	public event Action WorkersMoved;

	private ProvinceEconomy _economy;
	private ProvinceDefinition _definition;
	private GameBalance _balance;
	private Season _season;

	private Label _availableLabel;
	private readonly Dictionary<ResourceType, Label> _countLabels = new();
	private readonly Dictionary<ResourceType, Label> _previewLabels = new();

	public override void _Ready()
	{
		AddThemeConstantOverride("separation", 4);

		_availableLabel = new Label();
		AddChild(_availableLabel);

		foreach ((string label, ResourceType type) in Industries)
		{
			var row = new HBoxContainer();
			row.AddThemeConstantOverride("separation", 6);
			AddChild(row);

			var minus = new Button { Text = "-", CustomMinimumSize = new Vector2(28, 0) };
			var name = new Label { Text = label, CustomMinimumSize = new Vector2(48, 0) };
			var count = new Label { CustomMinimumSize = new Vector2(28, 0), HorizontalAlignment = HorizontalAlignment.Center };
			var plus = new Button { Text = "+", CustomMinimumSize = new Vector2(28, 0) };
			var preview = new Label();
			preview.AddThemeColorOverride("font_color", new Color(0.78f, 0.729f, 0.6f));

			ResourceType capturedType = type;
			minus.Pressed += () => Adjust(capturedType, -1);
			plus.Pressed += () => Adjust(capturedType, 1);

			row.AddChild(minus);
			row.AddChild(name);
			row.AddChild(count);
			row.AddChild(plus);
			row.AddChild(preview);

			_countLabels[type] = count;
			_previewLabels[type] = preview;
		}
	}

	public void Configure(ProvinceEconomy economy, ProvinceDefinition definition, GameBalance balance, Season season)
	{
		_economy = economy;
		_definition = definition;
		_balance = balance;
		_season = season;
		Refresh();
	}

	public void Refresh()
	{
		if (_economy == null)
		{
			return;
		}

		int available = _economy.AvailableWorkers(_balance) - _economy.AllocatedWorkers;
		_availableLabel.Text = $"Available Workers: {available}";

		foreach ((string _, ResourceType type) in Industries)
		{
			int workers = GetWorkers(type);
			_countLabels[type].Text = workers.ToString();
			int yield = EconomySimulation.ProjectedYield(type, workers, GetStock(type), _definition, _balance, _season);
			_previewLabels[type].Text = $"→ +{yield} next turn";
		}
	}

	private void Adjust(ResourceType type, int delta)
	{
		int next = GetWorkers(type) + delta;
		if (next < 0 || next > GetCapacity(type))
		{
			return;
		}

		if (delta > 0 && _economy.AllocatedWorkers >= _economy.AvailableWorkers(_balance))
		{
			return;
		}

		SetWorkers(type, next);
		Refresh();
		WorkersMoved?.Invoke();
	}

	private int GetWorkers(ResourceType type) => type switch
	{
		ResourceType.Grain => _economy.GrainWorkers,
		ResourceType.Cattle => _economy.CattleWorkers,
		ResourceType.Wood => _economy.WoodWorkers,
		ResourceType.Stone => _economy.StoneWorkers,
		ResourceType.Iron => _economy.IronWorkers,
		_ => 0,
	};

	private void SetWorkers(ResourceType type, int value)
	{
		switch (type)
		{
			case ResourceType.Grain: _economy.GrainWorkers = value; break;
			case ResourceType.Cattle: _economy.CattleWorkers = value; break;
			case ResourceType.Wood: _economy.WoodWorkers = value; break;
			case ResourceType.Stone: _economy.StoneWorkers = value; break;
			case ResourceType.Iron: _economy.IronWorkers = value; break;
		}
	}

	private int GetCapacity(ResourceType type) => type switch
	{
		ResourceType.Grain => _definition.GrainWorkerCapacity,
		ResourceType.Cattle => _definition.CattleWorkerCapacity,
		ResourceType.Wood => _definition.WoodWorkerCapacity,
		ResourceType.Stone => _definition.StoneWorkerCapacity,
		ResourceType.Iron => _definition.IronWorkerCapacity,
		_ => 0,
	};

	// Only Cattle's projection needs the current stock (base regrowth is a % of what's already there).
	private int GetStock(ResourceType type) => type == ResourceType.Cattle ? _economy.Cattle : 0;
}
