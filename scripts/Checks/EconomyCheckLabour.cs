using Godot;

/// <summary>The labour bar held to Lords of the Realm's allocator, and a county that digs stone or
/// iron, never both.</summary>
public partial class EconomyCheck
{
	private void LabourBar(GameBalance b, ProvinceDefinition def)
	{
		// The bar hard over to the farm: the industry stands empty, the fields take what they can
		// use and whoever is left over stands idle rather than being lent to the mine.
		ProvinceEconomy farm = Province();
		Labour.Divide(farm, def, b, Season.Winter, 0);
		Is("the bar hard over to the farm empties the industry",
			farm.WoodWorkers + farm.StoneWorkers + farm.IronWorkers + farm.BuildWorkers + farm.SmithWorkers, 0);
		Is("  and no job has more than it can use", farm.GrainWorkers <= EconomySimulation.Demand(ResourceType.Grain, farm, def, b, Season.Winter), true);
		Is("  and the rest stand idle", EconomySimulation.Idle(farm, def, b, Season.Winter),
			farm.Workers - farm.AllocatedWorkers);

		// Hard over to the industry: nobody in the fields, and the diggings take every one of them.
		ProvinceEconomy works = Province();
		Labour.Divide(works, def, b, Season.Autumn, 100);
		Is("the bar hard over to the industry empties the farm",
			works.GrainWorkers + works.CattleWorkers + works.ReclaimWorkers, 0);
		Is("  and the diggings take all comers", works.AllocatedWorkers, works.Workers);

		// A quarter to the industry, the original's opening: each half is dealt by its shares.
		ProvinceEconomy quarter = Province();
		Labour.Divide(quarter, def, b, Season.Spring, 25);
		int industry = quarter.WoodWorkers + quarter.StoneWorkers + quarter.IronWorkers + quarter.BuildWorkers
			+ quarter.SmithWorkers;
		Is("a quarter of the county goes to the industry", industry, Labour.Pct(quarter.Workers, 25));

		// The bar's arrows move one man across, never a percent of the county.
		for (int people = 1; people <= 1000; people++)
		{
			for (int half = 0; half <= people; half++)
			{
				if (Labour.Pct(people, Labour.ShareOf(half, people)) != half)
				{
					Is($"the share of {half} in {people} gives {half} back", Labour.Pct(people, Labour.ShareOf(half, people)), half);
				}
			}
		}

		// The leftovers of a job that cannot use its share walk on to the jobs with room...
		ProvinceEconomy herd = Province();
		herd.Shares[Labour.Grain] = Labour.Whole / 10;
		herd.Shares[Labour.Cattle] = Labour.Whole * 9 / 10;
		herd.Shares[Labour.Reclaim] = 0;
		Labour.Divide(herd, def, b, Season.Winter, 0);
		Is("a share the herd cannot use walks on to the fields",
			herd.CattleWorkers == EconomySimulation.Demand(ResourceType.Cattle, herd, def, b, Season.Winter)
			&& herd.GrainWorkers == EconomySimulation.Demand(ResourceType.Grain, herd, def, b, Season.Winter), true);

		// ...but never to one the lord has set at nothing: those stand idle instead.
		ProvinceEconomy none = Province();
		none.Shares[Labour.Grain] = 0;
		none.Shares[Labour.Cattle] = Labour.Whole;
		none.Shares[Labour.Reclaim] = 0;
		Labour.Divide(none, def, b, Season.Spring, 0);
		Is("  and a job set at nothing is given nobody", none.GrainWorkers, 0);
		EconomySimulation.RunTurn(none, def, b, Season.Spring);
		Is("  not even when the season turns", none.GrainWorkers, 0);

		// A shut site is dealt nobody, and its share goes to the sites still open.
		ProvinceEconomy shut = Province();
		Labour.Divide(shut, def, b, Season.Summer, 40);
		int woodBefore = shut.WoodWorkers;
		Labour.Toggle(shut, def, b, Season.Summer, Labour.Iron);
		Is("a shut mine is dealt nobody", shut.IronWorkers, 0);
		Is("  and the woods take its men", shut.WoodWorkers > woodBefore, true);
		Labour.Ask(shut, def, b, Season.Summer, Labour.Iron, 20);
		Is("asking for men at a shut mine opens it", shut.IsShut(Labour.Iron), false);

		// Asking for hands rewrites the shares, and the deal gives them.
		ProvinceEconomy asked = Province();
		Labour.Divide(asked, def, b, Season.Summer, 40);
		Labour.Ask(asked, def, b, Season.Summer, Labour.Wood, 60);
		Is("a figure moved by the lord is where he put it", Mathf.Abs(asked.WoodWorkers - 60) <= 2, true);

		// A figure taken off stands idle and stays idle; one put on comes from the idle first, and
		// nobody else on the county moves either way.
		ProvinceEconomy winter = Province();
		Labour.Divide(winter, def, b, Season.Winter, 25);
		int woodNow = winter.WoodWorkers;
		int ironNow = winter.IronWorkers;
		int idleNow = EconomySimulation.Idle(winter, def, b, Season.Winter);
		Labour.Ask(winter, def, b, Season.Winter, Labour.Grain, winter.GrainWorkers - 20);
		Is("a figure taken off the fields stands idle", EconomySimulation.Idle(winter, def, b, Season.Winter), idleNow + 20);
		Labour.Ask(winter, def, b, Season.Winter, Labour.Wood, woodNow + 20);
		Is("a figure put on the woods comes from the idle", EconomySimulation.Idle(winter, def, b, Season.Winter), idleNow);
		Is("  and not off the mine", winter.IronWorkers, ironNow);
		Is("  and is exactly one figure", winter.WoodWorkers, woodNow + 20);

		// Only the idle can be put to work, as in Lords of the Realm: asking for more than stand
		// about gives what idle there are, and nobody is pulled off another job for the rest.
		ProvinceEconomy busy = Province();
		Labour.Divide(busy, def, b, Season.Spring, 25);
		int herdNow = busy.CattleWorkers;
		int busyWood = busy.WoodWorkers;
		int busyIdle = EconomySimulation.Idle(busy, def, b, Season.Spring);
		int busyGrain = busy.GrainWorkers;
		Labour.Ask(busy, def, b, Season.Spring, Labour.Grain, busy.Workers);
		Is("a figure for the fields comes only from the idle", busy.GrainWorkers,
			Mathf.Min(busyGrain + busyIdle, Labour.Ceiling(busy, def, b, Season.Spring, Labour.Grain)));
		Is("  and leaves the herd alone", busy.CattleWorkers, herdNow);
		Is("  and the woods", busy.WoodWorkers, busyWood);

		// Waste being reclaimed has reclaimers of its own, and the reapers are not taken off the harvest
		// for it: a season's most for the one field.
		ProvinceEconomy torn = Province();
		torn.Fields[0] = FieldUse.Reclaiming;
		Labour.Divide(torn, def, b, Season.Summer, 0);
		Is("waste being reclaimed is dealt reclaimers", torn.ReclaimWorkers, b.ReclaimPerSeason);

		// Every county, every season, in a year of turns: nine jobs, and every hand on exactly one.
		ProvinceEconomy year = Province();
		Labour.Divide(year, def, b, Season.Spring, 25);
		bool counted = true;
		for (int season = 0; season < 8; season++)
		{
			EconomySimulation.RunTurn(year, def, b, (Season)(season % 4));
			counted &= year.AllocatedWorkers <= year.Workers
				&& EconomySimulation.Idle(year, def, b, (Season)((season + 1) % 4)) == year.Workers - year.AllocatedWorkers;
		}

		Is("every hand is on one job and no job has more than it can use, season after season", counted, true);

		// A site worked grows practised; one left empty falls back to where a new one starts.
		ProvinceEconomy practised = Province();
		practised.Efficiency[Labour.Wood] = b.SiteEfficiencyFloor;
		practised.WoodWorkers = 10;
		Labour.Practise(practised, b);
		Is("a worked site grows more practised", practised.EfficiencyOf(Labour.Wood) > b.SiteEfficiencyFloor, true);
		practised.WoodWorkers = 0;
		Labour.Practise(practised, b);
		Is("  and an abandoned one starts over", practised.EfficiencyOf(Labour.Wood), b.SiteEfficiencyFloor);
	}

	/// <summary>As in Lords of the Realm, a county has a quarry or a mine and never both: what it
	/// cannot dig it buys, or takes from the neighbour who can. Every campaign's every county.</summary>
	private void StoneOrIron()
	{
		const string Campaigns = "res://data/campaigns";
		foreach (string campaign in DirAccess.GetDirectoriesAt(Campaigns))
		{
			string folder = $"{Campaigns}/{campaign}/provinces";
			if (!DirAccess.DirExistsAbsolute(folder))
			{
				continue;
			}

			foreach (string file in DirAccess.GetFilesAt(folder))
			{
				if (!file.EndsWith(".tres"))
				{
					continue;
				}

				var county = GD.Load<ProvinceDefinition>($"{folder}/{file}");
				Is($"{county.ProvinceName} digs stone or iron, not both",
					county.StoneWorkerCapacity > 0 && county.IronWorkerCapacity > 0, false);
			}
		}
	}
}
