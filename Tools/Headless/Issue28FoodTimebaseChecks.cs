using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Anthropocene.Simulation;

internal static class Issue28FoodTimebaseChecks
{
    private static int checks;

    private static void Check(bool condition, string message)
    {
        checks++;
        if (!condition) throw new Exception("Issue 28: " + message);
    }

    private static bool Near(double a, double b, double tolerance = 1e-10)
        => Math.Abs(a - b) <= tolerance * Math.Max(1, Math.Max(Math.Abs(a), Math.Abs(b)));

    private static WorldState Witness(double turnDays = 7300)
    {
        var config = SimulationConfig.Small(1);
        config.DemographicModel = DemographicModel.LegacyQoLNetGrowth;
        config.TurnDays = turnDays;
        var tiles = new Tile[config.Width * config.Height];
        for (int id = 0; id < tiles.Length; id++)
        {
            var tile = new Tile { Id = id, Environment = EnvironmentKind.Grassland, ElevationLevel = 1 };
            for (int season = 0; season < 4; season++)
                tile.Seasons[season] = new SeasonData { Temperature = 12, Moisture = .45f };
            tiles[id] = tile;
        }
        var society = new Society { Id = 2, Player = 0 };
        var camp = new Camp { Id = 1, SocietyId = 2, Tile = 150, Population = 10, Player = 0 };
        var world = new WorldState
        {
            Config = config, Tiles = tiles, Generation = new GenerationReport { GeneratorVersion = WorldState.GeneratorVersion },
            NextId = 3, Camps = new System.Collections.Generic.List<Camp> { camp },
            Societies = new System.Collections.Generic.List<Society> { society }
        };
        Ownership.Bind(world);
        return world;
    }

    private static byte[] SaveWithoutMembers(WorldState world, params string[] memberNames)
    {
        byte[] raw;
        using (var input = new MemoryStream(SaveCodec.Save(world)))
        using (var gzip = new GZipStream(input, CompressionMode.Decompress))
        using (var output = new MemoryStream()) { gzip.CopyTo(output); raw = output.ToArray(); }
        string magic;
        byte[] header, body;
        using (var input = new MemoryStream(raw))
        using (var reader = new BinaryReader(input, Encoding.UTF8, true))
        {
            magic = reader.ReadString();
            int headerLength = reader.ReadInt32();
            header = reader.ReadBytes(headerLength);
            body = reader.ReadBytes(checked((int)(input.Length - input.Position)));
        }
        XDocument document;
        using (var reader = XmlDictionaryReader.CreateBinaryReader(header, XmlDictionaryReaderQuotas.Max))
            document = XDocument.Load(reader);
        var removed = document.Descendants()
            .Where(element => memberNames.Contains(element.Name.LocalName, StringComparer.Ordinal)).ToArray();
        foreach (var element in removed) element.Remove();
        using var binaryHeader = new MemoryStream();
        using (var writer = XmlDictionaryWriter.CreateBinaryWriter(binaryHeader, null, null, false))
        {
            document.WriteTo(writer);
            writer.Flush();
        }
        using var result = new MemoryStream();
        using (var gzip = new GZipStream(result, CompressionLevel.Fastest, true))
        using (var writer = new BinaryWriter(gzip, Encoding.UTF8, true))
        {
            writer.Write(magic);
            writer.Write(checked((int)binaryHeader.Length));
            writer.Write(binaryHeader.ToArray());
            writer.Write(body);
        }
        if (removed.Length == 0) throw new Exception("Issue 28: expected old-save fields were not present in metadata");
        return result.ToArray();
    }

    private static void Migration()
    {
        var absent = new SimulationConfig();
        absent.GrowthReserveYears = absent.MinimumStorageYears = absent.MaximumStorageYears = double.NaN;
        absent.MigrateMissingPhysicalFoodHorizons();
        Check(absent.GrowthReserveYears == 30 && absent.MinimumStorageYears == 20 && absent.MaximumStorageYears == 80,
            "absent physical fields use default legacy horizons and the reference years");

        var custom = new SimulationConfig();
        custom.Neolithic.ReferenceTurnYears = 24;
        custom.TurnDays = 7 * custom.DaysPerYear;
        custom.GrowthReserveTurns = .75;
        custom.MinimumStorageGenerations = 2.5;
        custom.MaximumStorageGenerations = 3.5;
        custom.GrowthReserveYears = custom.MinimumStorageYears = custom.MaximumStorageYears = double.NaN;
        custom.MigrateMissingPhysicalFoodHorizons();
        Check(custom.GrowthReserveYears == 18 && custom.MinimumStorageYears == 60 && custom.MaximumStorageYears == 84,
            "custom old multipliers use the fixed Neolithic reference, not loaded TurnDays");
        custom.TurnDays = 1;
        custom.MigrateMissingPhysicalFoodHorizons();
        Check(custom.GrowthReserveYears == 18 && custom.MinimumStorageYears == 60 && custom.MaximumStorageYears == 84,
            "repeated migration does not convert physical values twice");

        var zero = new SimulationConfig { GrowthReserveTurns = 0, GrowthReserveYears = double.NaN };
        zero.MigrateMissingPhysicalFoodHorizons();
        Check(zero.GrowthReserveYears == 0, "an explicit zero legacy reserve remains a zero-year target");
        zero.Validate();

        var explicitPhysical = new SimulationConfig { GrowthReserveTurns = 2, GrowthReserveYears = 0 };
        explicitPhysical.GrowthReserveYears = 0;
        explicitPhysical.MigrateMissingPhysicalFoodHorizons();
        Check(explicitPhysical.GrowthReserveYears == 0, "an explicit zero physical target overrides legacy defaults");

        var oldCustom = Witness(7 * 365);
        oldCustom.Config.Neolithic.ReferenceTurnYears = 24;
        oldCustom.Config.GrowthReserveTurns = .75;
        oldCustom.Config.MinimumStorageGenerations = 2.5;
        oldCustom.Config.MaximumStorageGenerations = 3.5;
        oldCustom.Camps[0].Food = 1234.5;
        oldCustom.Camps[0].Inventory.Pots = 7.25;
        byte[] customLegacyBytes = SaveWithoutMembers(oldCustom,
            "GrowthReserveYears", "MinimumStorageYears", "MaximumStorageYears");
        var customLoaded = SaveCodec.Load(customLegacyBytes);
        Check(customLoaded.Config.GrowthReserveYears == 18 && customLoaded.Config.MinimumStorageYears == 60
            && customLoaded.Config.MaximumStorageYears == 84,
            "a serialized legacy custom configuration migrates using its saved reference years");
        var customReloaded = SaveCodec.Load(SaveCodec.Save(customLoaded));
        Check(customReloaded.Config.GrowthReserveYears == 18 && customReloaded.Config.MinimumStorageYears == 60
            && customReloaded.Config.MaximumStorageYears == 84 && customReloaded.Camps[0].Food == 1234.5
            && customReloaded.Camps[0].Inventory.Pots == 7.25,
            "repeated load preserves migrated horizons and physical inventories without re-conversion");

        var oldZero = Witness();
        oldZero.Config.GrowthReserveTurns = 0;
        var zeroLoaded = SaveCodec.Load(SaveWithoutMembers(oldZero,
            "GrowthReserveYears", "MinimumStorageYears", "MaximumStorageYears"));
        Check(zeroLoaded.Config.GrowthReserveYears == 0,
            "a serialized explicit zero legacy reserve migrates to zero years");

        var absentLegacy = Witness();
        var absentLoaded = SaveCodec.Load(SaveWithoutMembers(absentLegacy,
            "GrowthReserveYears", "MinimumStorageYears", "MaximumStorageYears", "GrowthReserveTurns",
            "MinimumStorageGenerations", "MaximumStorageGenerations"));
        Check(absentLoaded.Config.GrowthReserveYears == 30 && absentLoaded.Config.MinimumStorageYears == 20
            && absentLoaded.Config.MaximumStorageYears == 80,
            "absent old configuration fields receive the original shipped defaults");
    }

    private static void HorizonInvariance()
    {
        double[] turnDays = { 7300, 1825, 365, 7 };
        double expectedReserve = -1, expectedStorage = -1, expectedPots = -1;
        for (int i = 0; i < turnDays.Length; i++)
        {
            var world = Witness(turnDays[i]);
            var camp = world.Camps[0];
            double intervals = world.Config.GenerationIntervals;
            double reserve = NeolithicProduction.DesiredReserveFood(world, camp.Population, intervals);
            double storage = NeolithicProduction.NaturalStorageCapacity(world, camp, intervals);
            double pots = NeolithicProduction.TargetPots(world, camp, turnDays[i] / world.Config.DaysPerYear);
            if (i == 0)
            {
                double legacyReserve = camp.Population * world.Config.Consumption * intervals * world.Config.GrowthReserveTurns;
                double storageFraction = Math.Max(0, (.45 - .45f) / .45);
                double legacyStorageGenerations = world.Config.MinimumStorageGenerations
                    + (world.Config.MaximumStorageGenerations - world.Config.MinimumStorageGenerations) * storageFraction;
                double legacyStorage = camp.Population * world.Config.Consumption * intervals * legacyStorageGenerations;
                expectedReserve = legacyReserve;
                expectedStorage = legacyStorage;
                expectedPots = Math.Max(0, legacyReserve - legacyStorage) / world.Config.Neolithic.PotStorageFoodPerUnit;
                Check(Near(reserve, legacyReserve), "the shipped 20-year reserve target matches the prior formula");
                Check(storage == legacyStorage, "the shipped 20-year natural capacity exactly matches the prior formula");
                Check(Near(pots, expectedPots), "the shipped Pots target matches the prior formula");
            }
            else
            {
                Check(Near(reserve, expectedReserve) && Near(storage, expectedStorage) && Near(pots, expectedPots),
                    "reserve, natural storage and Pots targets stay fixed at turn days=" + turnDays[i]);
            }
            double meals = camp.Population * world.Config.Consumption * intervals;
            Check(Near(meals, camp.Population * world.Config.Consumption * turnDays[i] / world.Config.BaseDays),
                "current-turn meals still scale with elapsed days=" + turnDays[i]);
            Check(world.Config.Neolithic.PotStorageFoodPerUnit == 80,
                "Pot capacity remains an absolute physical food quantity");

            camp.Inventory.Pots = 10;
            double before = camp.Inventory.Pots;
            NeolithicProduction.DepreciatePots(world, camp, turnDays[i] / world.Config.DaysPerYear);
            double expectedAfter = before * Math.Pow(.9, (turnDays[i] / world.Config.DaysPerYear) / world.Config.Neolithic.ReferenceTurnYears);
            Check(Near(camp.Inventory.Pots, expectedAfter), "Pot depreciation follows elapsed years=" + turnDays[i]);
        }
    }

    private static void PreparedResolution()
    {
        double[] turnDays = { 7300, 1825, 365, 7 };
        double expectedReserveTarget = -1, expectedPots = -1;
        foreach (double days in turnDays)
        {
            var world = Witness(days);
            world.Config.AnnualHumanGrowthRate = 0;
            world.Config.GrowthReserveYears = 30;
            foreach (var tile in world.Tiles)
            {
                tile.Food = tile.PotentialFood = tile.Freshwater = 0;
                for (int season = 0; season < 4; season++)
                {
                    var profile = tile.Seasons[season];
                    profile.MaxFood = profile.MaxPotentialFood = profile.MaxFreshwater = 0;
                    profile.FoodRecoveryPerDay = profile.BiomassRecoveryPerDay = profile.WaterRecoveryPerDay = 0;
                    tile.Seasons[season] = profile;
                }
            }
            var camp = world.Camps[0];
            camp.Food = 100000;
            camp.Inventory.Pots = 10;
            var connected = new Hunter { Id = world.NextId++, Player = 0, SourceCamp = camp.Id, SocietyId = camp.SocietyId, Tile = camp.Tile, Population = 2 };
            int remoteTile = world.Map.Radius(camp.Tile, 8).First(tile => world.Map.Distance(camp.Tile, tile) > world.Config.SupplyRange + 1);
            var remote = new Hunter { Id = world.NextId++, Player = 0, SourceCamp = camp.Id, SocietyId = camp.SocietyId, Tile = remoteTile, Population = 3, CarriedFood = 10000 };
            world.Hunters.Add(connected);
            world.Hunters.Add(remote);

            Check(Supply.Connected(world, connected) && !Supply.Connected(world, remote),
                "fixture keeps one hunter connected and one remote at " + days + " days");
            Check(NeolithicProduction.ReservePopulation(world, camp) == camp.Population + connected.Population,
                "food reserve counts connected hunters once and excludes remote hunters");
            var orders = new System.Collections.Generic.Dictionary<int, System.Collections.Generic.List<Order>>
            {
                [0] = new System.Collections.Generic.List<Order>()
            };
            var prepared = TurnPlanner.Prepare(world, orders);
            var needs = prepared.Needs[camp.Id];
            double intervals = days / world.Config.BaseDays;
            double expectedConsumption = (camp.Population + connected.Population) * world.Config.Consumption * intervals;
            Check(Near(needs.ConsumptionRequired, expectedConsumption),
                "prepared prior cohort consumption scales with actual turn days=" + days);
            if (expectedReserveTarget < 0)
            {
                expectedReserveTarget = needs.ReserveTarget;
                expectedPots = NeolithicProduction.TargetPots(world, camp, days / world.Config.DaysPerYear);
            }
            else
                Check(Near(needs.ReserveTarget, expectedReserveTarget)
                    && Near(NeolithicProduction.TargetPots(world, camp, days / world.Config.DaysPerYear), expectedPots),
                    "prepared reserve and Pots targets remain invariant at turn days=" + days);

            var record = new TurnResolver().Resolve(world, prepared);
            Check(Near(camp.LastConsumed, expectedConsumption)
                && Near(connected.LastConsumed, connected.Population * world.Config.Consumption * intervals)
                && Near(remote.LastConsumed, remote.Population * world.Config.Consumption * intervals),
                "resolution consumes the prepared camp/connected cohort and remote provisions once at " + days + " days");
            double expectedPotStock = 10 * Math.Pow(.9, (days / world.Config.DaysPerYear) / world.Config.Neolithic.ReferenceTurnYears);
            Check(Near(camp.Inventory.Pots, expectedPotStock), "resolved Pot depreciation follows actual elapsed years=" + days);
            Check(Near(record.Demographics.Single().FoodConsumed,
                    (camp.Population + connected.Population + remote.Population) * world.Config.Consumption * intervals),
                "demographic receipt counts connected camp needs and remote provisions once");
        }
    }

    public static int Run()
    {
        Migration();
        HorizonInvariance();
        PreparedResolution();
        Console.WriteLine("PASS: " + checks + " Issue 28 physical food timebase assertions");
        return checks;
    }
}
