using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Anthropocene.Simulation;

// Opt-in native causal check for the v0.2 social-relocation forecast.  It finds
// a real saved boundary whose existing economy admits immigration, then compares
// the ordinary accepted Close commit with the same turn suppressed and with an
// independently replayed, exactly conserved forced commit.  All follow-up turns
// use the real heuristic/economy/ecology/demography with further social flows off.
internal static class SocialRelocationNativeChecks
{
    private sealed class ForcedTransfer
    {
        public MigrationReport Report;
        public WorkforceTransfer Workforce;
        public double Growth, Death, DeficitYears, DeficitSeverity;
    }

    private sealed class Observation
    {
        public string Branch;
        public int Age, Population, Camps, Workforce, WorldBirths, WorldDeaths;
        public double Exposure, Crowding, Disease, Starvation;
        public double Food, Clothing, Housing, QoL, PopQoL, Material, CumulativeEndpointWelfareProxy;
    }

    private static int checks;
    private static void Check(bool value, string message)
    {
        checks++;
        if (!value) throw new Exception("Native social relocation: " + message);
    }

    public static int Run(int followTurns = 3)
    {
        if (followTurns < 3 || followTurns > 8) throw new ArgumentOutOfRangeException(nameof(followTurns));
        checks = 0;
        byte[] boundary = FindAcceptedBoundary(out int seed, out int preparedTurns, out int expectedFlows);
        string fingerprint = SaveCodec.Fingerprint(SaveCodec.Load(boundary));
        Console.WriteLine("SOCIAL_NATIVE_BOUNDARY seed=" + seed + " preparationTurns=" + preparedTurns
            + " expectedFlows=" + expectedFlows + " fingerprint=" + fingerprint);

        WorldState accepted = SaveCodec.Load(boundary);
        accepted.Config.ImmigrationEnabled = true; accepted.Config.FissionEnabled = false;
        TurnRecord acceptedEvent = ResolveNative(accepted, out string acceptedOrders);
        Check(acceptedEvent.Migrations.Count == expectedFlows && expectedFlows > 0,
            "the selected saved boundary deterministically reproduces accepted immigration");
        Check(acceptedEvent.Fissions.Count == 0, "the accepted causal event isolates immigration");

        WorldState control = SaveCodec.Load(boundary);
        control.Config.ImmigrationEnabled = false; control.Config.FissionEnabled = false;
        TurnRecord controlEvent = ResolveNative(control, out string controlOrders);
        Check(acceptedOrders == controlOrders, "accepted and suppressed event turns start from identical heuristic orders");
        Check(controlEvent.Migrations.Count == 0 && controlEvent.Fissions.Count == 0,
            "the suppressed branch commits no social transfer");
        Check(DemographySignature(acceptedEvent) == DemographySignature(controlEvent),
            "Close-phase social switches do not alter the event turn's births or deaths");

        WorldState forced = SaveCodec.Load(boundary);
        forced.Config.ImmigrationEnabled = false; forced.Config.FissionEnabled = false;
        TurnRecord forcedEvent = ResolveNative(forced, out string forcedOrders);
        Check(controlOrders == forcedOrders, "forced and suppressed event turns start from identical heuristic orders");
        ApplyForcedImmigration(forced, forcedEvent, acceptedEvent.Migrations);
        Check(TransferStateSignature(accepted) == TransferStateSignature(forced),
            "the forced transaction reproduces the accepted transfer ledgers exactly");
        Check(ContactSignature(accepted) == ContactSignature(forced),
            "the forced transaction reproduces accepted post-migration contact chronology");
        Check(forced.Camps.Sum(forced.LinkedPopulation) == control.Camps.Sum(control.LinkedPopulation),
            "forced immigration conserves global population");
        Check(forced.Camps.Sum(c => c.Workforce.Assigned) == accepted.Camps.Sum(c => c.Workforce.Assigned),
            "forced and accepted transactions preserve the same assigned workforce");

        int[] affected = acceptedEvent.Migrations.SelectMany(m => new[] { m.SourceCamp, m.DestinationCamp })
            .Distinct().OrderBy(id => id).ToArray();
        foreach (MigrationReport flow in acceptedEvent.Migrations)
            Console.WriteLine("SOCIAL_NATIVE_FLOW source=" + flow.SourceCamp + " destination=" + flow.DestinationCamp
                + " migrants=" + flow.Migrants + " moverDelta=" + F(flow.MoverUtilityChange)
                + " sourceDelta=" + F(flow.SourceUtilityChange) + " incumbentDelta=" + F(flow.IncumbentUtilityChange)
                + " aggregateDelta=" + F(flow.AggregateWelfareChange)
                + " forecastQ=" + F(flow.ForecastDestinationQoL)
                + " forecastFCH=" + F(flow.ForecastDestinationNutrition) + "/" + F(flow.ForecastDestinationClothing)
                    + "/" + F(flow.ForecastDestinationHousing)
                + " risk=" + F(flow.ForecastDestinationMortalityRisk)
                + " stability=" + F(flow.ForecastDestinationStability)
                + " opportunities=" + flow.MatchedOpportunities);

        accepted.Config.ImmigrationEnabled = accepted.Config.FissionEnabled = false;
        control.Config.ImmigrationEnabled = control.Config.FissionEnabled = false;
        forced.Config.ImmigrationEnabled = forced.Config.FissionEnabled = false;
        byte[] forcedEventBoundary = SaveCodec.Save(forced);
        var worlds = new Dictionary<string, WorldState>
        {
            ["accepted"] = accepted,
            ["suppressed"] = control,
            ["forced"] = forced
        };
        var cumulative = worlds.Keys.ToDictionary(k => k, _ => 0.0);
        var observations = new List<Observation>();
        observations.Add(Observe("accepted", 0, accepted, acceptedEvent, affected, cumulative));
        observations.Add(Observe("suppressed", 0, control, controlEvent, affected, cumulative));
        observations.Add(Observe("forced", 0, forced, forcedEvent, affected, cumulative));
        for (int age = 1; age <= followTurns; age++)
        {
            foreach (string branch in worlds.Keys.OrderBy(k => k, StringComparer.Ordinal))
            {
                WorldState world = worlds[branch]; int before = world.Camps.Sum(world.LinkedPopulation);
                TurnRecord record = ResolveNative(world, out _);
                Check(world.Camps.Sum(world.LinkedPopulation) == before + record.Growth - record.Deaths,
                    branch + " follow-up " + age + " conserves global population");
                Check(record.Migrations.Count == 0 && record.Fissions.Count == 0,
                    branch + " follow-up " + age + " keeps the causal continuation socially closed");
                observations.Add(Observe(branch, age, world, record, affected, cumulative));
            }
            Check(TransferStateSignature(accepted) == TransferStateSignature(forced),
                "accepted and replayed-forced transfer trajectories match at follow-up " + age);
            Check(ContactSignature(accepted) == ContactSignature(forced),
                "accepted and replayed-forced contact trajectories match at follow-up " + age);
        }

        double acceptedWelfare = observations.Last(o => o.Branch == "accepted").CumulativeEndpointWelfareProxy;
        double controlWelfare = observations.Last(o => o.Branch == "suppressed").CumulativeEndpointWelfareProxy;
        Console.WriteLine("SOCIAL_NATIVE_CAUSAL followTurns=" + followTurns
            + " followupAcceptedMinusSuppressedEndpointWelfareProxy=" + F(acceptedWelfare - controlWelfare)
            + " acceptedForcedTransferState=IDENTICAL contacts=IDENTICAL conservation=PASS opportunityProvider=ZERO");

        // These are deliberately compound exogenous validation shocks, not a
        // percentage forecast-error claim and not a free correction. They scale
        // global production coefficients plus unique receiver-local stocks,
        // ecology, capital, housing and herds at the event boundary or after one
        // native follow-up turn.
        RunSupplyStress(forcedEventBoundary, acceptedEvent.Migrations, affected, followTurns, .20, false);
        RunSupplyStress(forcedEventBoundary, acceptedEvent.Migrations, affected, followTurns, .40, false);
        RunSupplyStress(forcedEventBoundary, acceptedEvent.Migrations, affected, followTurns, .40, true);

        Console.WriteLine("PASS: " + checks + " native social-relocation assertions");
        return checks;
    }

    private static byte[] FindAcceptedBoundary(out int selectedSeed, out int preparedTurns, out int flows)
    {
        foreach (int seed in new[] { 17, 53, 991 })
        {
            byte[] opening = OpeningBoundary(seed);
            WorldState evolving = SaveCodec.Load(opening);
            evolving.Config.ImmigrationEnabled = true; evolving.Config.FissionEnabled = true;
            for (int prepared = 0; prepared < 25; prepared++)
            {
                byte[] candidate = SaveCodec.Save(evolving);
                WorldState trial = SaveCodec.Load(candidate);
                trial.Config.ImmigrationEnabled = true; trial.Config.FissionEnabled = false;
                TurnRecord record = ResolveNative(trial, out _);
                if (record.Migrations.Count > 0)
                {
                    selectedSeed = seed; preparedTurns = prepared; flows = record.Migrations.Count;
                    return candidate;
                }
                ResolveNative(evolving, out _);
            }
        }
        throw new Exception("Native social relocation: no accepted immigration boundary found in the declared seed/horizon set.");
    }

    private static byte[] OpeningBoundary(int seed)
    {
        var config = SimulationConfig.Small(8);
        config.DemographicModel = DemographicModel.PhysicalMortalityV1;
        config.QoLMortalityEnabled = false;
        config.FissionEnabled = false; config.ImmigrationEnabled = false; config.TradeEnabled = true;
        config.EconomicAllocation = EconomicAllocationStrategy.DemandControl;
        config.Validate();
        var world = new WorldGenerator().Generate(seed, config);
        int turns = 0;
        while (!world.Finished && turns < 30) { ResolveNative(world, out _); turns++; }
        Check(world.Finished, "the native causal fixture reaches its saved post-opening boundary");
        byte[] bytes = SaveCodec.Save(world);
        Check(SaveCodec.Fingerprint(world) == SaveCodec.Fingerprint(SaveCodec.Load(bytes)),
            "the native causal boundary round-trips exactly");
        return bytes;
    }

    private static TurnRecord ResolveNative(WorldState world, out string orders)
    {
        PlanningResult plan = AiPlanning.Decide(world);
        orders = string.Join("|", plan.Orders.OrderBy(p => p.Key)
            .SelectMany(p => p.Value.Select(o => p.Key + ":" + o)));
        return new TurnResolver().Resolve(world, plan.Orders, plan.Traces);
    }

    private static void ApplyForcedImmigration(WorldState world, TurnRecord record,
        IReadOnlyList<MigrationReport> migrations)
    {
        int committedTurn = world.Turn;
        var demographics = record.Demographics.ToDictionary(d => d.Camp);
        var transfers = new List<ForcedTransfer>();
        foreach (MigrationReport report in migrations.OrderBy(m => m.DestinationCamp).ThenBy(m => m.SourceCamp))
        {
            Camp source = world.Camps.Single(c => c.Id == report.SourceCamp);
            Check(source.Population == report.SourceBefore,
                "forced source " + source.Id + " matches the accepted frozen resident boundary");
            var workforce = WorkforceTransfer.Split(source.Workforce, source.Population, report.Migrants);
            double fraction = report.Migrants / (double)report.SourceBefore;
            transfers.Add(new ForcedTransfer
            {
                Report = report, Workforce = workforce,
                Growth = source.GrowthRemainder * fraction,
                Death = source.DeathRemainder * fraction,
                DeficitYears = source.FoodDeficitYears,
                DeficitSeverity = source.DeficitSeverity
            });
        }

        foreach (ForcedTransfer transfer in transfers.OrderBy(t => t.Report.SourceCamp))
        {
            MigrationReport report = transfer.Report;
            Camp source = world.Camps.Single(c => c.Id == report.SourceCamp);
            source.Workforce = transfer.Workforce.Stayers;
            source.Population -= report.Migrants;
            source.PlantFood -= report.Plant; source.AnimalFood -= report.Animal; source.AquaticFood -= report.Aquatic;
            source.GrowthRemainder -= transfer.Growth; source.DeathRemainder -= transfer.Death;
            demographics[source.Id].Emigrated += report.Migrants;
        }
        foreach (ForcedTransfer transfer in transfers)
        {
            MigrationReport report = transfer.Report;
            Camp source = world.Camps.Single(c => c.Id == report.SourceCamp);
            Camp destination = world.Camps.Single(c => c.Id == report.DestinationCamp);
            int before = destination.Population;
            destination.FoodDeficitYears = (destination.FoodDeficitYears * before
                + transfer.DeficitYears * report.Migrants) / (before + report.Migrants);
            destination.DeficitSeverity = (destination.DeficitSeverity * before
                + transfer.DeficitSeverity * report.Migrants) / (before + report.Migrants);
            destination.Population += report.Migrants;
            destination.Workforce = WorkforceTransfer.Merge(destination.Workforce,
                transfer.Workforce.Movers, destination.Population);
            destination.GrowthRemainder += transfer.Growth; destination.DeathRemainder += transfer.Death;
            destination.PlantFood += report.Plant; destination.AnimalFood += report.Animal;
            destination.AquaticFood += report.Aquatic;
            demographics[destination.Id].Immigrated += report.Migrants;
            world.Turn = report.Turn;
            ContactKnowledge.RecordMigration(world, source.People, destination);
            ContactKnowledge.RecordMigration(world, destination.People, source);
            world.Turn = committedTurn;
            Remember(world, source, destination.Id, +1, report.Turn);
            Remember(world, destination, source.Id, -1, report.Turn);
            record.Migrations.Add(report);
        }
        int cooldown = world.Config.ImmigrationReturnCooldownTurns;
        foreach (Camp camp in world.Camps)
            if (camp.RecentMigration != null)
            {
                camp.RecentMigration.RemoveAll(m => migrations[0].Turn - m.Turn > cooldown);
                if (camp.RecentMigration.Count == 0) camp.RecentMigration = null;
            }
        // The suppressed native turn already ran EndTurn before this independent
        // forced commit. Re-run that pure/canonical update at the event turn from
        // the post-transfer boundary; same-turn marks and upserts are idempotent.
        world.Turn = migrations[0].Turn;
        ContactKnowledge.EndTurn(world);
        world.Turn = committedTurn;
        WelfareDiagnostics.Capture(world, record);
    }

    private static void Remember(WorldState world, Camp camp, int other, int direction, int turn)
    {
        if (world.Config.ImmigrationReturnCooldownTurns <= 0) return;
        if (camp.RecentMigration == null) camp.RecentMigration = new List<MigrationMemory>();
        camp.RecentMigration.RemoveAll(m => m.Other == other && m.Direction == direction);
        camp.RecentMigration.Add(new MigrationMemory { Other = other, Turn = turn, Direction = direction });
    }

    private static Observation Observe(string branch, int age, WorldState world, TurnRecord record,
        IReadOnlyCollection<int> affected, Dictionary<string, double> cumulative)
    {
        Camp[] camps = world.Camps.Where(c => affected.Contains(c.Id) && world.LinkedPopulation(c) > 0).ToArray();
        int population = camps.Sum(world.LinkedPopulation);
        double popQol = camps.Sum(c => world.LinkedPopulation(c) * c.QoL);
        double welfare = world.Camps.Sum(c => world.LinkedPopulation(c) * c.QoL);
        // Age zero is a transfer boundary: Camp.QoL was experienced and resolved
        // before Close moved people. Reweighting it by the post-transfer camp
        // populations is useful as a boundary proxy but is not causal welfare.
        if (age > 0) cumulative[branch] += welfare;
        double Weight(Func<Camp, double> value) => population > 0
            ? camps.Sum(c => world.LinkedPopulation(c) * value(c)) / population : 0;
        var result = new Observation
        {
            Branch = branch, Age = age, Population = population, Camps = camps.Length,
            Workforce = camps.Sum(c => c.Workforce.Assigned), WorldBirths = record.Growth, WorldDeaths = record.Deaths,
            Exposure = record.Demographics.Sum(d => d.ExposureDeaths),
            Crowding = record.Demographics.Sum(d => d.CrowdingDeaths),
            Disease = record.Demographics.Sum(d => d.DiseaseDeaths),
            Starvation = record.Demographics.Sum(d => d.StarvationDeaths),
            Food = Weight(c => c.FoodNeedsMetRatio), Clothing = Weight(c => c.ClothPerCapita),
            Housing = Weight(c => c.HousingPerCapita), QoL = population > 0 ? popQol / population : 0,
            PopQoL = popQol,
            Material = camps.Sum(c => c.Inventory.Cloth + c.Inventory.AnimalFabric
                + c.Inventory.LightHousingUnits + c.Inventory.HousingMaterial),
            CumulativeEndpointWelfareProxy = cumulative[branch]
        };
        Console.WriteLine("SOCIAL_NATIVE_TRAJECTORY branch=" + branch + " age=" + age
            + " camps=" + result.Camps + " population=" + result.Population + " workforce=" + result.Workforce
            + " worldGrossBirths=" + result.WorldBirths + " worldDeaths=" + result.WorldDeaths
            + " exposure=" + F(result.Exposure) + " crowding=" + F(result.Crowding)
            + " disease=" + F(result.Disease) + " starvation=" + F(result.Starvation)
            + " FCH=" + F(result.Food) + "/" + F(result.Clothing) + "/" + F(result.Housing)
            + " qol=" + F(result.QoL) + " popQol=" + F(result.PopQoL)
            + " material=" + F(result.Material) + " boundaryProxy=" + (age == 0 ? "YES" : "NO")
            + " cumulativeFollowupWorldEndpointWelfareProxy=" + F(result.CumulativeEndpointWelfareProxy));
        return result;
    }

    private static void RunSupplyStress(byte[] eventBoundary, IReadOnlyList<MigrationReport> migrations,
        IReadOnlyCollection<int> affected, int turns, double miss, bool delayed)
    {
        WorldState world = SaveCodec.Load(eventBoundary);
        world.Config.ImmigrationEnabled = world.Config.FissionEnabled = false;
        string branch = (delayed ? "delayed" : "immediate") + "-compound-shock-" + (int)Math.Round(100 * miss);
        var cumulative = new Dictionary<string, double> { [branch] = 0 };
        if (!delayed) ApplySupplyShock(world, migrations, 1 - miss);
        for (int age = 1; age <= turns; age++)
        {
            TurnRecord record = ResolveNative(world, out _);
            Observe(branch, age, world, record, affected, cumulative);
            if (delayed && age == 1) ApplySupplyShock(world, migrations, 1 - miss);
        }
        Console.WriteLine("SOCIAL_NATIVE_STRESS branch=" + branch + " turns=" + turns
            + " cumulativeFollowupWorldEndpointWelfareProxy=" + F(cumulative[branch])
            + " intervention=EXOGENOUS_COMPOUND_NO_CORRECTION");
    }

    private static void ApplySupplyShock(WorldState world, IReadOnlyList<MigrationReport> migrations, double retained)
    {
        retained = Math.Max(0, Math.Min(1, retained));
        // A declared forecast-capacity miss: native production still executes,
        // but realized gathering, farming, textile and shelter productivity are
        // lower by the tested fraction.  This is a test-world intervention only.
        world.Config.GatherPerPerson *= retained;
        world.Config.FarmerProductivity *= retained;
        world.Config.Neolithic.WeaverFabricOutputPerBaseDays *= retained;
        world.Config.Neolithic.BuilderHousingOutputPerBaseDays *= retained;
        world.Config.Neolithic.LightHousingBuilderOutputPerBaseDays *= retained;
        int[] destinations = migrations.Select(m => m.DestinationCamp).Distinct().OrderBy(id => id).ToArray();
        foreach (int id in destinations)
        {
            Camp camp = world.Camps.Single(c => c.Id == id);
            camp.PlantFood *= retained; camp.AnimalFood *= retained; camp.AquaticFood *= retained;
            camp.Inventory.Cloth *= retained; camp.Inventory.AnimalFabric *= retained;
            camp.Inventory.LightHousingUnits *= retained; camp.Inventory.HousingMaterial *= retained;
            camp.Inventory.Wood *= retained; camp.Inventory.Clay *= retained;
            foreach (FixedHousingStock stock in world.FixedHousing.Where(h => h.CampId == camp.Id))
                stock.Units *= retained;
            foreach (Herd herd in world.Herds.Where(h => h.DomesticCamp == camp.Id))
            {
                herd.Animals = (int)Math.Floor(herd.Animals * retained);
                herd.GrowthRemainder *= retained; herd.DeathRemainder *= retained;
                herd.UnmetFood *= retained; herd.UnmetFreshwater *= retained;
                herd.AnimalFoodPotential *= retained; herd.AnimalFoodProduced *= retained;
            }
        }
        var affectedTiles = new HashSet<int>();
        foreach (int id in destinations)
        {
            Camp camp = world.Camps.Single(c => c.Id == id);
            affectedTiles.UnionWith(world.Map.Radius(camp.Tile, Math.Max(1, world.Config.GatherRadius)));
        }
        foreach (int tileId in affectedTiles.OrderBy(id => id))
        {
            Tile tile = world.Tiles[tileId]; tile.Food *= retained; tile.PotentialFood *= retained;
            tile.CultivationCapital *= retained;
            for (int season = 0; season < tile.Seasons.Length; season++)
            {
                SeasonData profile = tile.Seasons[season];
                profile.MaxFood *= (float)retained; profile.MaxPotentialFood *= (float)retained;
                profile.FoodRecoveryPerDay *= (float)retained;
                profile.BiomassRecoveryPerDay *= (float)retained;
                tile.Seasons[season] = profile;
            }
        }
        NeolithicProduction.FixedHousingChanged(world);
    }

    private static string DemographySignature(TurnRecord record)
        => string.Join("|", record.Demographics.OrderBy(d => d.Camp).Select(d => d.Camp + ":"
            + d.StartingPopulation + ":" + d.GrossBirths + ":" + d.GrossDeaths + ":" + d.EndingPopulation));

    private static string TransferStateSignature(WorldState world)
        => string.Join("|", world.Camps.OrderBy(c => c.Id).Select(c => c.Id + ":" + c.Population + ":"
            + Bits(c.PlantFood) + ":" + Bits(c.AnimalFood) + ":" + Bits(c.AquaticFood) + ":"
            + Bits(c.GrowthRemainder) + ":" + Bits(c.DeathRemainder) + ":"
            + Bits(c.FoodDeficitYears) + ":" + Bits(c.DeficitSeverity) + ":"
            + string.Join(",", typeof(CampInventory).GetFields().Where(f => f.FieldType == typeof(double))
                .OrderBy(f => f.Name, StringComparer.Ordinal).Select(f => f.Name + "=" + Bits((double)f.GetValue(c.Inventory)))) + ":"
            + string.Join(",", c.Workforce.Assignments.Where(a => a.Population > 0)
                .OrderBy(a => a.Role, StringComparer.Ordinal).Select(a => a.Role + "=" + a.Population)) + ":"
            + string.Join(",", (c.RecentMigration ?? new List<MigrationMemory>()).OrderBy(m => m.Other)
                .ThenBy(m => m.Direction).Select(m => m.Other + "=" + m.Turn + "=" + m.Direction))));

    private static string ContactSignature(WorldState world)
        => string.Join("|", world.Societies.OrderBy(s => s.Id).Select(s =>
        {
            ContactMemory memory = s.Memory;
            if (memory == null) return s.Id + ":none";
            string explored = string.Join(",", memory.Explored.Chunks.Select((chunk, i) => chunk + "="
                + memory.Explored.Words[i] + "="
                + (memory.Explored.Turns != null && i < memory.Explored.Turns.Count ? memory.Explored.Turns[i] : -1)));
            string contacts = string.Join(",", memory.Contacts.OrderBy(c => c.Camp).Select(c => c.Camp + "="
                + c.Owner + "=" + c.Tile + "=" + c.Turn + "=" + (int)c.Source + "=" + c.Moved));
            return s.Id + ":" + explored + ":" + contacts;
        }));

    private static long Bits(double value) => BitConverter.DoubleToInt64Bits(value);
    private static string F(double value) => value.ToString("F6", CultureInfo.InvariantCulture);
}
