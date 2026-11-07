using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.Serialization;
using System.Xml;
using System.Xml.Linq;
using Anthropocene.Simulation;

// Sparse fishery state, save migration and portability boundaries.
internal static class FishingStateChecks
{
    private static int checks;
    private static void Check(bool ok, string message) { checks++; if (!ok) throw new Exception("Fishing state: " + message); }
    private static void Throws(Action action, string message)
    {
        bool threw = false;
        try { action(); } catch (InvalidOperationException) { threw = true; }
        Check(threw, message);
    }
    private static void ThrowsArgument(Action action, string message)
    {
        bool threw = false;
        try { action(); } catch (ArgumentException) { threw = true; }
        Check(threw, message);
    }

    private static WorldState World()
    {
        var world = NeolithicChecks.Fixture();
        world.Config.NeolithicFishingEnabled = true;
        world.Config.CorrectedAquaticSeasonalPressureEnabled = true;
        world.Config.EconomicAllocation = EconomicAllocationStrategy.DemandControl;
        world.Days = 1000;
        int water = world.Map.Neighbors(world.Camps[0].Tile).OrderBy(tile => tile).First();
        world.Tiles[water].Terrain = Terrain.Shallows;
        return world;
    }

    private static WorldState PopulatedWorld()
    {
        var world = World(); var camp = world.Camps[0];
        int ground = world.Map.Neighbors(camp.Tile).Where(tile => world.Tiles[tile].Environment == EnvironmentKind.Shallows).Single();
        camp.LastSelectedFishingGrounds.Add(ground);
        camp.Inventory.FishingTools = 12.75;
        camp.CraftingPriorities.FishingTools = .37;
        world.FisherySites.Add(new FisherySite { TileId = ground, OperatorCampId = camp.Id, Capital = 2.25, LastUpdatedDays = 900 });
        camp.People.FishingGroundKnowledge.Add(new FishingGroundSnapshot
        {
            TileId = ground, OperatorCampId = camp.Id, AccessibleFood = 71.5, Biomass = 140.25,
            Condition = .73, Capital = 2.25, ObservedDays = 875, OperationFraction = .625,
            ProductiveProfile = new[] { 1.0, 2.0, 3.0, 4.0 }
        });
        return world;
    }

    private static byte[] RemoveSaveFields(WorldState world, params string[] fields)
    {
        using (var input = new MemoryStream(SaveCodec.Save(world)))
        using (var unzip = new GZipStream(input, CompressionMode.Decompress))
        using (var reader = new BinaryReader(unzip))
        using (var output = new MemoryStream())
        {
            using (var zip = new GZipStream(output, CompressionLevel.Fastest, true))
            using (var writer = new BinaryWriter(zip))
            {
                writer.Write(reader.ReadString()); int headerLength = reader.ReadInt32();
                using (var xml = XmlDictionaryReader.CreateBinaryReader(reader.ReadBytes(headerLength), XmlDictionaryReaderQuotas.Max))
                using (var metadata = new MemoryStream())
                {
                    var document = XDocument.Load(xml);
                    var absent = new HashSet<string>(fields, StringComparer.Ordinal);
                    document.Descendants().Where(element => absent.Contains(element.Name.LocalName)).Remove();
                    using (var binary = XmlDictionaryWriter.CreateBinaryWriter(metadata, null, null, false)) document.WriteTo(binary);
                    writer.Write((int)metadata.Length); writer.Write(metadata.ToArray());
                }
                unzip.CopyTo(zip);
            }
            return output.ToArray();
        }
    }

    private static void RoundTrip()
    {
        var world = PopulatedWorld();
        string fingerprint = SaveCodec.Fingerprint(world);
        byte[] sparseBytes = SaveCodec.Save(world);
        var control = SaveCodec.Load(sparseBytes);
        control.FisherySites.Clear();
        control.Camps.Single().LastSelectedFishingGrounds.Clear();
        control.Camps.Single().Inventory.FishingTools = 0;
        control.Camps.Single().CraftingPriorities.FishingTools = .10;
        control.Camps.Single().People.FishingGroundKnowledge.Clear();
        int sparseByteDelta = sparseBytes.Length - SaveCodec.Save(control).Length;
        Check(sparseByteDelta >= 0 && sparseByteDelta < 4096,
            "one complete sparse fishery site/knowledge record has bounded save-size overhead");
        Console.WriteLine("FISHING_SAVE_SIZE sites=1 deltaBytes=" + sparseByteDelta);
        var loaded = SaveCodec.Load(sparseBytes);
        var camp = loaded.Camps.Single(); int ground = world.Camps[0].LastSelectedFishingGrounds.Single();
        var snapshot = camp.People.FishingGroundKnowledge.Single();
        Check(SaveCodec.Fingerprint(loaded) == fingerprint, "sparse sites, remembered snapshots and camp fishing state round-trip into the fingerprint");
        Check(loaded.FisherySites.Count == 1 && loaded.FisherySites[0].TileId == ground && loaded.FisherySites[0].OperatorCampId == camp.Id && loaded.FisherySites[0].Capital == 2.25, "fishery site tile, operator and capital round-trip");
        Check(camp.LastSelectedFishingGrounds.SequenceEqual(new[] { ground }) && camp.Inventory.FishingTools == 12.75 && camp.CraftingPriorities.FishingTools == .37, "selected grounds, inventory and recipe priority round-trip");
        Check(snapshot.TileId == ground && snapshot.AccessibleFood == 71.5 && snapshot.Biomass == 140.25 && snapshot.Condition == .73 && snapshot.Capital == 2.25 && snapshot.OperationFraction == .625 && snapshot.ProductiveProfile.SequenceEqual(new[] { 1.0, 2.0, 3.0, 4.0 }), "remembered snapshot economics and profile round-trip");
    }

    private static void OldSaveDefaults()
    {
        var original = PopulatedWorld();
        var oldBytes = RemoveSaveFields(original,
            "NeolithicFishingEnabled", "CorrectedAquaticSeasonalPressureEnabled", "FishingTools",
            "FishingToolFisherYearsPerUnit", "FishingToolHarvestMultiplier", "FisherySites",
            "LastSelectedFishingGrounds", "FishingGroundKnowledge", "OperationFraction");
        var old = SaveCodec.Load(oldBytes); var camp = old.Camps.Single();
        Check(!old.Config.NeolithicFishingEnabled && !old.Config.CorrectedAquaticSeasonalPressureEnabled, "absent feature gates migrate disabled");
        Check(camp.Inventory.FishingTools == 0 && camp.CraftingPriorities.FishingTools == .10, "absent inventory is zero and the new recipe priority uses its default");
        Check(old.Config.Neolithic.FishingToolFisherYearsPerUnit == 20 && old.Config.Neolithic.FishingToolHarvestMultiplier == 1.5, "absent Fishing tool tuning uses its migration defaults");
        Check(old.FisherySites.Count == 0 && camp.LastSelectedFishingGrounds.Count == 0 && camp.People.FishingGroundKnowledge.Count == 0, "absent sparse fishery lists migrate empty");

        original.Config.NeolithicSailingEnabled = true;
        var absentSailing = SaveCodec.Load(RemoveSaveFields(original, "NeolithicSailingEnabled"));
        Check(!absentSailing.Config.NeolithicSailingEnabled && absentSailing.Config.NeolithicFishingEnabled, "old resident-Fishing saves do not acquire Sailing activation");
        original.Config.NeolithicSailingEnabled = false;
        var missingOperation = RemoveSaveFields(original, "OperationFraction");
        var oldSnapshot = SaveCodec.Load(missingOperation).Camps.Single().People.FishingGroundKnowledge.Single();
        Check(oldSnapshot.OperationFraction == 0, "an absent remembered operation fraction defaults to zero");
    }

    private static void RejectInvalidState()
    {
        var duplicateSite = PopulatedWorld(); duplicateSite.FisherySites.Add(duplicateSite.FisherySites[0]);
        Throws(() => SaveCodec.Save(duplicateSite), "duplicate fishery-site tiles are rejected");
        var invalidSite = PopulatedWorld(); invalidSite.FisherySites[0].Capital = double.NaN;
        Throws(() => SaveCodec.Save(invalidSite), "non-finite fishery capital is rejected");
        var duplicateSelection = PopulatedWorld(); duplicateSelection.Camps[0].LastSelectedFishingGrounds.Add(duplicateSelection.Camps[0].LastSelectedFishingGrounds[0]);
        Throws(() => SaveCodec.Save(duplicateSelection), "duplicate last-selected grounds are rejected");
        var invalidSnapshot = PopulatedWorld(); invalidSnapshot.Camps[0].People.FishingGroundKnowledge[0].OperationFraction = 1.01;
        Throws(() => SaveCodec.Save(invalidSnapshot), "out-of-range snapshot operation fraction is rejected");
        var duplicateSnapshot = PopulatedWorld(); duplicateSnapshot.Camps[0].People.FishingGroundKnowledge.Add(duplicateSnapshot.Camps[0].People.FishingGroundKnowledge[0].Copy());
        Throws(() => SaveCodec.Save(duplicateSnapshot), "duplicate remembered snapshots are rejected");
    }

    private static void ConfigGates()
    {
        var missingCorrection = new SimulationConfig { NeolithicFishingEnabled = true };
        ThrowsArgument(() => missingCorrection.Validate(), "Fishing requires corrected aquatic seasonal pressure");
        var wrongPlanner = new SimulationConfig { NeolithicFishingEnabled = true, CorrectedAquaticSeasonalPressureEnabled = true, EconomicAllocation = EconomicAllocationStrategy.Legacy };
        ThrowsArgument(() => wrongPlanner.Validate(), "Fishing requires DemandControl allocation");
        var enabled = new SimulationConfig { NeolithicFishingEnabled = true, CorrectedAquaticSeasonalPressureEnabled = true, EconomicAllocation = EconomicAllocationStrategy.DemandControl };
        enabled.Validate();
        var invalidTools = new NeolithicConfig { FishingToolFisherYearsPerUnit = 0 };
        ThrowsArgument(() => invalidTools.Validate(), "nonpositive Fishing tool service is rejected");
    }

    private static void PortabilityBoundaries()
    {
        var world = PopulatedWorld(); var parent = world.Camps[0];
        parent.Population = 100; parent.Inventory.FishingTools = 10;
        parent.FissionPolicy = FissionPolicy.Encourage;
        world.Config.FissionEnabled = true; world.Config.FissionRadius = 3;
        world.Config.FissionHousingRelief = 1; world.Config.FissionDaughterMargin = 1;
        world.Config.FissionGainThreshold = world.Config.FissionGainDiscourage = world.Config.FissionGainEncourage = 0;
        var memory = ContactKnowledge.MemoryOf(world, parent.People);
        foreach (int tile in world.Map.Radius(parent.Tile, 4)) memory.Explored.Add(tile);
        foreach (var tile in world.Tiles)
        {
            tile.Food = 1000; tile.PotentialFood = 8000; tile.Freshwater = 100;
            for (int season = 0; season < 4; season++)
            {
                tile.Seasons[season].MaxFood = 1000; tile.Seasons[season].MaxPotentialFood = 8000;
                tile.Seasons[season].MaxFreshwater = 100;
            }
        }
        var beforeGround = parent.People.FishingGroundKnowledge.Single();
        var record = new TurnRecord { Turn = world.Turn };
        record.Demographics.Add(new DemographicReport { Camp = parent.Id, StartingPopulation = parent.Population, EndingPopulation = parent.Population });
        Fission.Resolve(world, record, new PhaseExecution(), new ResolutionMetrics());
        Check(record.Fissions.Count == 1, "portable-goods fixture founds one daughter");
        var daughter = world.Camps.Single(camp => camp.Id != parent.Id);
        Check(Math.Abs(daughter.Inventory.FishingTools - 4) < 1e-9 && Math.Abs(parent.Inventory.FishingTools - 6) < 1e-9, "Fishing Tools split proportionally on fission");
        var daughterMemory = daughter.People.FishingGroundKnowledge.Single();
        Check(daughterMemory.TileId == beforeGround.TileId && daughterMemory.OperationFraction == beforeGround.OperationFraction && daughterMemory.ProductiveProfile.SequenceEqual(beforeGround.ProductiveProfile) && !ReferenceEquals(daughterMemory.ProductiveProfile, beforeGround.ProductiveProfile), "fission copies remembered ground knowledge without sharing its profile array");
        Check(world.FisherySites.Count == 1 && world.FisherySites[0].OperatorCampId == parent.Id && daughter.LastSelectedFishingGrounds.Count == 0, "physical site capital and selected grounds stay with the parent on fission");

        Check(!Enum.GetNames(typeof(TradeGood)).Contains("FishingTools", StringComparer.Ordinal) &&
            Trade.Registry.All(descriptor => descriptor.Recipe != CraftGood.FishingTools && descriptor.Parts.All(part => part.Name != "FishingTools")),
            "Fishing Tools are excluded from trade goods and trade stock descriptors");
    }

    public static int Run()
    {
        checks = 0; RoundTrip(); OldSaveDefaults(); RejectInvalidState(); ConfigGates(); PortabilityBoundaries();
        Console.WriteLine("PASS: " + checks + " fishing state assertions"); return checks;
    }
}
