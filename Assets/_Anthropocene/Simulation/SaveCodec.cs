using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text;
using System.Xml;

namespace Anthropocene.Simulation
{
    public static class SaveCodec
    {
        private const string Magic = "ANTHROPOCENE-11", LegacyMagic10 = "ANTHROPOCENE-10", LegacyMagic9 = "ANTHROPOCENE-9", LegacyMagic8 = "ANTHROPOCENE-8";
        private const string HistoryMagic = "ANTHROPOCENE-12";
        private const string GrowthHistoryMagic = "ANTHROPOCENE-13";
        private static bool History(string magic) => magic == HistoryMagic || magic == GrowthHistoryMagic;
        private static bool Modern(string magic) => magic == Magic || History(magic);
        // Benchmark-only 320-tribe openings can retain more than 128 MiB of
        // legitimate turn receipts in the binary XML header. Keep a finite cap.
        private const int MaximumMetadataBytes = 512 * 1024 * 1024;
        private static DataContractSerializer Serializer(Type type) => new DataContractSerializer(type, new DataContractSerializerSettings { MaxItemsInObjectGraph = int.MaxValue });
        public static byte[] Save(WorldState world)
        {
            FisheryState.Normalize(world);
            NormalizePhysicalContactTimes(world);
            RequireBoundOwnership(world);
            using (var output = new MemoryStream())
            {
                using (var zip = new GZipStream(output, CompressionLevel.Fastest, true)) WritePayload(zip, world);
                return output.ToArray();
            }
        }
        private static void RequireBoundOwnership(WorldState world)
        {
            var owners = world.Societies.ToDictionary(s => s.Id);
            if (world.Camps.Any(c => !owners.TryGetValue(c.SocietyId, out var owner) || !ReferenceEquals(c.People, owner))) throw new InvalidOperationException("Bind society ownership at the world-construction boundary before saving.");
        }
        // The uncompressed save payload. Save compresses it; Fingerprint hashes
        // it directly, so state hashing never depends on compressor output or cost.
        private static void WritePayload(Stream destination, WorldState world)
        {
            using (var buffered = new BufferedStream(destination, 65536))
            using (var writer = new BinaryWriter(buffered))
            {
                bool history = world.AutonomousReceipts != null && world.AutonomousReceipts.Count > 0;
                bool growth = history && world.AutonomousReceipts.Values.Any(r => r?.SchemaVersion == 2);
                writer.Write(history ? (growth ? GrowthHistoryMagic : HistoryMagic) : Magic);
                // Geography dominates the save. Fixed primitive records avoid reflection and repeated XML member names for every tile.
                var metadata = CopyWithoutTiles(world);
                using (var header = new MemoryStream())
                {
                    using (var xml = XmlDictionaryWriter.CreateBinaryWriter(header, null, null, false)) Serializer(typeof(WorldState)).WriteObject(xml, metadata);
                    writer.Write(checked((int)header.Length)); header.Position = 0; header.CopyTo(buffered);
                }
                writer.Write(world.Tiles.Length);
                // Encode each tile into one little-endian record, byte-identical to
                // BinaryWriter's per-field output, to avoid ~76 stream calls per tile.
                var record = new byte[TileRecordBytes(Magic)];
                foreach (var tile in world.Tiles)
                {
                    int o = 0;
                    PutInt(record, ref o, tile.Plate); PutInt(record, ref o, tile.Downstream); record[o++] = (byte)tile.Environment; record[o++] = (byte)tile.NaturalVegetationPotential; record[o++] = (byte)tile.CurrentVegetation; PutDouble(record, ref o, tile.LastEcologyUpdateDays);
                    record[o++] = (byte)((tile.Continental ? 1 : 0) | (tile.Oasis ? 2 : 0) | (tile.Cultivated ? 4 : 0) | (tile.Fishery ? 8 : 0) | (tile.Irrigated ? 16 : 0));
                    record[o++] = (byte)tile.ElevationLevel; record[o++] = (byte)tile.RidgeMask; record[o++] = (byte)tile.Relief; record[o++] = (byte)tile.WaterBody; record[o++] = (byte)tile.FreshwaterSource;
                    PutFloat(record, ref o, tile.DepressionDepth); PutFloat(record, ref o, tile.OasisInfluence); PutDouble(record, ref o, tile.Saltwater); PutDouble(record, ref o, tile.LandCondition); PutDouble(record, ref o, tile.AquaticCondition);
                    PutFloat(record, ref o, tile.Elevation); PutFloat(record, ref o, tile.Volcanism); PutFloat(record, ref o, tile.Flow); PutDouble(record, ref o, tile.Food); PutDouble(record, ref o, tile.PotentialFood); PutDouble(record, ref o, tile.Freshwater);
                    PutFloat(record, ref o, tile.Cultivation);
                    PutDouble(record, ref o, tile.CultivationCapital); PutInt(record, ref o, tile.CultivationOperator);
                    foreach (var s in tile.Seasons)
                    {
                        PutFloat(record, ref o, s.Temperature); PutFloat(record, ref o, s.Rain); PutFloat(record, ref o, s.Snow); PutFloat(record, ref o, s.WindX); PutFloat(record, ref o, s.Runoff); PutFloat(record, ref o, s.RegionalMoisture); PutFloat(record, ref o, s.Moisture);
                        PutFloat(record, ref o, s.AccessibleFreshwater); PutFloat(record, ref o, s.MaxFood); PutFloat(record, ref o, s.MaxPotentialFood); PutFloat(record, ref o, s.MaxFreshwater); PutFloat(record, ref o, s.MaxSaltwater); PutFloat(record, ref o, s.FoodRecoveryPerDay); PutFloat(record, ref o, s.BiomassRecoveryPerDay); PutFloat(record, ref o, s.WaterRecoveryPerDay); PutFloat(record, ref o, s.Habitat);
                    }
                    if (o != record.Length) throw new InvalidOperationException("Tile record layout does not match its declared size.");
                    writer.Write(record, 0, o);
                }
                writer.Write(world.Resources != null);
                if (world.Resources != null) ResourceCodec.Write(writer, world.Resources);
                if (history)
                {
                    foreach (var pair in world.AutonomousReceipts)
                    {
                        if (pair.Value == null || pair.Key != pair.Value.Camp)
                            throw new InvalidDataException("Invalid autonomous receipt key.");
                        pair.Value.Validate(world);
                    }
                    var state = new AutonomousElderHistorySave { SchemaVersion = growth ? 2 : 1, Receipts = world.AutonomousReceipts
                        .OrderBy(p => p.Key).Select(p => p.Value).ToList() };
                    using var extension = new MemoryStream();
                    using (var xml = XmlDictionaryWriter.CreateBinaryWriter(extension, null, null, false))
                        Serializer(typeof(AutonomousElderHistorySave)).WriteObject(xml, state);
                    if (extension.Length > 16 * 1024 * 1024) throw new InvalidDataException("Autonomous history extension is too large.");
                    writer.Write(checked((int)extension.Length)); extension.Position = 0; extension.CopyTo(buffered);
                }
            }
        }
        public static WorldState Load(byte[] bytes)
        {
            using (var input = new MemoryStream(bytes))
            using (var zip = new GZipStream(input, CompressionMode.Decompress))
            using (var buffered = new BufferedStream(zip, 65536))
            using (var reader = new BinaryReader(buffered))
            {
                string magic = reader.ReadString();
                if (!Modern(magic) && magic != LegacyMagic10 && magic != LegacyMagic9 && magic != LegacyMagic8) throw new InvalidDataException("Unsupported save format: this opening requires separate regional and local moisture. The older save is preserved.");
                int headerLength = reader.ReadInt32(); if (headerLength < 1 || headerLength > MaximumMetadataBytes) throw new InvalidDataException("Invalid save metadata size.");
                WorldState world;
                using (var xml = XmlDictionaryReader.CreateBinaryReader(reader.ReadBytes(headerLength), XmlDictionaryReaderQuotas.Max)) world = (WorldState)Serializer(typeof(WorldState)).ReadObject(xml);
                if (History(magic))
                    foreach (var camp in world.Camps) AutonomousElderHistory.ValidateMemory(world, camp, false);
                // Generators 8-11 change profiles already stored in this format. Preserve old
                // profiles and generator metadata; aquatic condition uses its original rules
                // for pre-11 saves. No implicit geography or plankton regeneration.
                if (world.Version != 7 || world.Config.Version != 7 || world.Generation.GeneratorVersion < 7 || world.Generation.GeneratorVersion > WorldState.GeneratorVersion) throw new InvalidDataException("Unsupported save version; regeneration is never implicit.");
                Ownership.Bind(world);
                MigrateGenerationalDefaults(world);
                world.Config.MigrateMissingPhysicalFoodHorizons();
                world.Config.Neolithic.MigrateLegacyMaterialReserveDuration(world.Config.TurnDays / world.Config.DaysPerYear);
                if (!Modern(magic))
                {
                    foreach (var camp in world.Camps)
                    {
                        // Legacy HousingUnits were construction progress at ten per house;
                        // finished Houses each sheltered eight people. Convert once.
                        camp.Inventory.HousingUnits = (camp.Inventory.Houses + camp.Inventory.HousingUnits / 10) * 8 / world.Config.Neolithic.HousingPeoplePerUnit;
                        camp.Inventory.Houses = 0; camp.WaterNeedsMetRatio = 1;
                        camp.StationaryYears = camp.StationaryTurns * world.Config.TurnDays / world.Config.DaysPerYear;
                        // The old automatic-village path could assign agricultural jobs
                        // without Agriculture. Release only those impossible legacy jobs.
                        if (!CultivationEconomy.Enabled(camp)) camp.Workforce.Assignments.RemoveAll(a => a.Role != WorkforceRoles.Gatherer);
                    }
                }
                // HousingUnits was the old carried representation. Transfer it
                // once to the camp's current site after legacy progress conversion;
                // the site list is the sole authority in new saves.
                foreach (var camp in world.Camps.OrderBy(c => c.Id)) NeolithicProduction.MigrateLegacyFixedHousing(world, camp);
                world.Config.Validate();
                int count = reader.ReadInt32(); if (count != world.Config.Width * world.Config.Height) throw new InvalidDataException("Invalid tile count.");
                world.Tiles = new Tile[count];
                bool hasCultivation = Modern(magic) || magic == LegacyMagic10;
                var record = new byte[TileRecordBytes(magic)];
                for (int i = 0; i < count; i++)
                {
                    ReadRecord(reader, record);
                    int o = 0;
                    var tile = new Tile { Id = i, Plate = GetInt(record, ref o), Downstream = GetInt(record, ref o), Environment = (EnvironmentKind)record[o++], NaturalVegetationPotential = (VegetationTier)record[o++], CurrentVegetation = (VegetationTier)record[o++], LastEcologyUpdateDays = GetDouble(record, ref o) };
                    byte flags = record[o++];
                    tile.Continental = (flags & 1) != 0;
                    tile.Oasis = (flags & 2) != 0;
                    tile.Cultivated = (flags & 4) != 0;
                    tile.Fishery = (flags & 8) != 0;
                    tile.Irrigated = hasCultivation && (flags & 16) != 0;
                    tile.ElevationLevel = record[o++]; tile.RidgeMask = record[o++]; tile.Relief = (Relief)record[o++]; tile.WaterBody = (WaterKind)record[o++]; tile.FreshwaterSource = (FreshwaterSource)record[o++];
                    tile.DepressionDepth = GetFloat(record, ref o); tile.OasisInfluence = GetFloat(record, ref o); tile.Saltwater = GetDouble(record, ref o); tile.LandCondition = GetDouble(record, ref o); tile.AquaticCondition = GetDouble(record, ref o);
                    tile.Elevation = GetFloat(record, ref o); tile.Volcanism = GetFloat(record, ref o); tile.Flow = GetFloat(record, ref o); tile.Food = GetDouble(record, ref o); tile.PotentialFood = GetDouble(record, ref o); tile.Freshwater = GetDouble(record, ref o);
                    tile.Cultivation = hasCultivation ? GetFloat(record, ref o) : 0;
                    tile.CultivationCapital = Modern(magic) ? GetDouble(record, ref o) : Math.Pow(tile.Cultivation / world.Config.ReferenceIntensity, 2);
                    tile.CultivationOperator = Modern(magic) ? GetInt(record, ref o) : -1;
                    for (int s = 0; s < 4; s++) tile.Seasons[s] = new SeasonData
                    {
                        Temperature = GetFloat(record, ref o), Rain = GetFloat(record, ref o), Snow = GetFloat(record, ref o), WindX = GetFloat(record, ref o), Runoff = GetFloat(record, ref o), RegionalMoisture = GetFloat(record, ref o), Moisture = GetFloat(record, ref o),
                        AccessibleFreshwater = GetFloat(record, ref o), MaxFood = GetFloat(record, ref o), MaxPotentialFood = GetFloat(record, ref o), MaxFreshwater = GetFloat(record, ref o), MaxSaltwater = GetFloat(record, ref o), FoodRecoveryPerDay = GetFloat(record, ref o), BiomassRecoveryPerDay = GetFloat(record, ref o), WaterRecoveryPerDay = GetFloat(record, ref o), Habitat = GetFloat(record, ref o)
                    };
                    world.Tiles[i] = tile;
                }
                world.Resources = Modern(magic) || magic == LegacyMagic10 ? (reader.ReadBoolean() ? ResourceCodec.Read(reader, count) : null) : (magic == LegacyMagic9 ? ResourceCodec.Read(reader, count) : null);
                if (History(magic))
                {
                    int length = reader.ReadInt32();
                    if (length < 1 || length > 16 * 1024 * 1024) throw new InvalidDataException("Invalid autonomous history extension length.");
                    var data = reader.ReadBytes(length);
                    if (data.Length != length) throw new EndOfStreamException("Truncated autonomous history extension.");
                    AutonomousElderHistorySave state;
                    using (var xml = XmlDictionaryReader.CreateBinaryReader(data, XmlDictionaryReaderQuotas.Max))
                        state = (AutonomousElderHistorySave)Serializer(typeof(AutonomousElderHistorySave)).ReadObject(xml);
                    if (state?.SchemaVersion != (magic == GrowthHistoryMagic ? 2 : 1) || state.Receipts == null || state.Receipts.Count == 0 ||
                        state.Receipts.Any(r => r == null) || state.Receipts.Select(r => r.Camp).Distinct().Count() != state.Receipts.Count)
                        throw new InvalidDataException("Invalid autonomous history extension schema/identities.");
                    if ((magic == GrowthHistoryMagic) != state.Receipts.Any(r => r.SchemaVersion == 2))
                        throw new InvalidDataException("Save envelope and action authority disagree.");
                    world.AutonomousReceipts = state.Receipts.ToDictionary(r => r.Camp);
                    foreach (var receipt in state.Receipts) receipt.Validate(world);
                }
                NeolithicProduction.NormalizeFixedHousing(world);
                FisheryState.Normalize(world);
                foreach (var edge in world.Rivers)
                {
                    if (world.Tiles[edge.TileA].RiverEdges == null) world.Tiles[edge.TileA].RiverEdges = Enumerable.Repeat(-1, 6).ToArray();
                    world.Tiles[edge.TileA].RiverEdges[edge.SideA] = edge.Id;
                    if (edge.TileB < 0) continue;
                    if (world.Tiles[edge.TileB].RiverEdges == null) world.Tiles[edge.TileB].RiverEdges = Enumerable.Repeat(-1, 6).ToArray();
                    world.Tiles[edge.TileB].RiverEdges[edge.SideB] = edge.Id;
                }
                foreach (var turn in world.History) { turn.Pressure = new System.Collections.Generic.List<PressureEntry>(); turn.EcologyBalances = new System.Collections.Generic.List<EcologyBalance>(); turn.Production = turn.Production ?? new System.Collections.Generic.List<ProductionReceipt>(); turn.MaterialTransfers = turn.MaterialTransfers ?? new System.Collections.Generic.List<MaterialReceipt>(); }
                TurnResolver.Invariants(world); ElevationLevelStage.Validate(world); BoundaryRiverStage.Validate(world); ResourceValidation.Validate(world);
                if (world.Resources != null) MaterialAccessIndex.For(world);
                return world;
            }
        }
        // Fixed tile record: 109 bytes of tile fields plus four 16-float season profiles.
        // Envelopes 8/9 lack Cultivation; envelope 10 lacks capital and operator.
        private static int TileRecordBytes(string magic) => 93 + 4 * 16 * 4
            + (Modern(magic) || magic == LegacyMagic10 ? 4 : 0) + (Modern(magic) ? 12 : 0);
        private static void ReadRecord(BinaryReader reader, byte[] record)
        {
            for (int read = 0; read < record.Length;)
            {
                int n = reader.Read(record, read, record.Length - read);
                if (n <= 0) throw new EndOfStreamException("Unable to read beyond the end of the stream.");
                read += n;
            }
        }
        private static void PutInt(byte[] b, ref int o, int value) { BinaryPrimitives.WriteInt32LittleEndian(new Span<byte>(b, o, 4), value); o += 4; }
        private static void PutFloat(byte[] b, ref int o, float value) => PutInt(b, ref o, BitConverter.SingleToInt32Bits(value));
        private static void PutDouble(byte[] b, ref int o, double value) { BinaryPrimitives.WriteInt64LittleEndian(new Span<byte>(b, o, 8), BitConverter.DoubleToInt64Bits(value)); o += 8; }
        private static int GetInt(byte[] b, ref int o) { int value = BinaryPrimitives.ReadInt32LittleEndian(new ReadOnlySpan<byte>(b, o, 4)); o += 4; return value; }
        private static float GetFloat(byte[] b, ref int o) => BitConverter.Int32BitsToSingle(GetInt(b, ref o));
        private static double GetDouble(byte[] b, ref int o) { double value = BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(new ReadOnlySpan<byte>(b, o, 8))); o += 8; return value; }
        private static void MigrateGenerationalDefaults(WorldState world)
        {
            var config = world.Config;
            world.MaterialSources = world.MaterialSources ?? new System.Collections.Generic.List<MaterialSourceState>();
            world.MaterialKnowledge = world.MaterialKnowledge ?? new System.Collections.Generic.List<MaterialKnowledge>();
            world.FisherySites = world.FisherySites ?? new System.Collections.Generic.List<FisherySite>();
            foreach (var camp in world.Camps)
            {
                if (camp.Inventory == null) camp.Inventory = new CampInventory();
                if (camp.Workforce == null) camp.Workforce = new PopulationLedger();
                if (camp.CraftingPriorities == null) camp.CraftingPriorities = new CraftingPriorities();
                camp.LastSelectedFishingGrounds = camp.LastSelectedFishingGrounds ?? new System.Collections.Generic.List<int>();
                camp.ElderMemory?.Normalize();
            }
            foreach (var soc in world.Societies)
            {
                if (!soc.OpeningTransitionProcessedInitialized)
                {
                    // Completed legacy openings cannot replay rewards even if an
                    // older save has no transition seed or compatibility grant.
                    soc.OpeningTransitionProcessed = soc.Progression.Era != Era.Paleolithic || soc.Technology != Technology.None || soc.TransitionSeeds != null && soc.TransitionSeeds.Count > 0;
                    soc.OpeningTransitionProcessedInitialized = true;
                }
                if (soc.Progression.Era == Era.Neolithic && soc.Technology != Technology.None)
                    soc.Technologies[(int)soc.Technology] = true;
                if (soc.NeolithicExperience == null || soc.NeolithicExperience.Length != 3) soc.NeolithicExperience = new double[3];
                if (soc.TechnologyExposure == null || soc.TechnologyExposure.Length != 3) soc.TechnologyExposure = new double[3];
                if (soc.CraftExperience == null) soc.CraftExperience = new System.Collections.Generic.Dictionary<string, double>();
                if (soc.UnlockedSpecialists == null) soc.UnlockedSpecialists = new System.Collections.Generic.List<string>();
                if (soc.NeolithicTechnologies == null) soc.NeolithicTechnologies = new System.Collections.Generic.List<string>();
                soc.FishingGroundKnowledge = soc.FishingGroundKnowledge ?? new System.Collections.Generic.List<FishingGroundSnapshot>();
            }
            NormalizePhysicalContactTimes(world);
            bool legacyGenerationalConfig = config.StartingReserveGenerations <= 0 && config.HunterCarryGenerations <= 0 && config.MinimumStorageGenerations <= 0 && config.MaximumStorageGenerations <= 0;
            if (config.StartingReserveGenerations <= 0) config.StartingReserveGenerations = 1;
            if (config.HunterCarryGenerations <= 0) config.HunterCarryGenerations = 2.5;
            if (config.MinimumStorageGenerations <= 0) config.MinimumStorageGenerations = 1;
            if (config.MaximumStorageGenerations < config.MinimumStorageGenerations) config.MaximumStorageGenerations = 4;
            if (config.HuntBreedingReserveFraction <= 0) config.HuntBreedingReserveFraction = .2;
            if (config.SustainableHuntPopulation <= 0) config.SustainableHuntPopulation = 16;
            if (config.SustainableHerdGrowthRetention <= 0) config.SustainableHerdGrowthRetention = .25;
            if (!config.ManagedHerdDefaultsInitialized)
            {
                if (config.AnimalsPerHerder <= 0 || double.IsNaN(config.AnimalsPerHerder) || double.IsInfinity(config.AnimalsPerHerder)) config.AnimalsPerHerder = 2;
                if (config.AnimalFoodPerManagedAnimal <= 0 || double.IsNaN(config.AnimalFoodPerManagedAnimal) || double.IsInfinity(config.AnimalFoodPerManagedAnimal)) config.AnimalFoodPerManagedAnimal = 1;
                if (config.AnimalFoodNutritionExponent <= 0 || double.IsNaN(config.AnimalFoodNutritionExponent) || double.IsInfinity(config.AnimalFoodNutritionExponent)) config.AnimalFoodNutritionExponent = 1;
                if (config.AnimalManagementExponent <= 0 || double.IsNaN(config.AnimalManagementExponent) || double.IsInfinity(config.AnimalManagementExponent)) config.AnimalManagementExponent = 1;
                config.ManagedHerdDefaultsInitialized = true;
            }
            world.History ??= new System.Collections.Generic.List<TurnRecord>();
            foreach (var turn in world.History) { turn.ManagedHerds ??= new System.Collections.Generic.List<ManagedHerdReport>(); turn.Fissions ??= new System.Collections.Generic.List<FissionReport>(); turn.Migrations ??= new System.Collections.Generic.List<MigrationReport>(); turn.Trades ??= new System.Collections.Generic.List<TradeRecord>(); }
            foreach (var herd in world.Herds)
                if (!herd.NutritionInitialized) { herd.LastNutrition = herd.LastFoodNutrition = herd.LastWaterNutrition = herd.ManagementFulfillment = 1; herd.NutritionInitialized = true; }
            foreach (var camp in world.Camps)
            {
                if (!camp.ManagedBreedingPolicyInitialized)
                    camp.ManagedBreedingIntensity = .55;
                else if (double.IsNaN(camp.ManagedBreedingIntensity) || double.IsInfinity(camp.ManagedBreedingIntensity) || camp.ManagedBreedingIntensity < 0 || camp.ManagedBreedingIntensity > 1)
                    throw new InvalidDataException("Invalid managed breeding intensity.");
                camp.ManagedBreedingPolicyInitialized = true;
                // Herd target (Docs/HerdLabour.md): absent in older saves, which load unset and so never slaughter.
                if (!camp.HerdTargetSet) camp.HerdTarget = 0;
                else if (camp.HerdTarget < 0) throw new InvalidDataException("Invalid herd target.");
            }
            if (config.ActiveFishingPracticeMultiplier <= 0) config.ActiveFishingPracticeMultiplier = 4.25;
            if (config.GrasslandHerdDensityMultiplier <= 0) config.GrasslandHerdDensityMultiplier = 1.35;
            if (config.SteppeHerdDensityMultiplier <= 0) config.SteppeHerdDensityMultiplier = 1.15;
            if (legacyGenerationalConfig)
            {
                config.HunterForagePerPerson = 1;
                foreach (var species in config.Species.Where(s => s.MaximumHerdSize == 32))
                    species.MaximumHerdSize = species.Id == "antelope" || species.Id == "bison" ? 150 : species.Id == "horse" ? 120 : species.Id == "goat" ? 90 : species.Id == "camel" ? 80 : 120;
                foreach (var herd in world.Herds.Where(h => h.DomesticCamp < 0 && h.Capacity == 32)) herd.Capacity = config.GetSpecies(herd.Species).MaximumHerdSize;
            }
        }
        private static WorldState CopyWithoutTiles(WorldState w) => new WorldState { MaterialSources = w.MaterialSources, MaterialKnowledge = w.MaterialKnowledge, FixedHousing = w.FixedHousing?.OrderBy(s => s.CampId).ThenBy(s => s.Tile).ToList(), FisherySites = w.FisherySites?.OrderBy(s => s.TileId).ToList(), Version = w.Version, Seed = w.Seed, Turn = w.Turn, NextId = w.NextId, PlayerCount = w.PlayerCount, Days = w.Days, Config = w.Config, Camps = w.Camps, Societies = w.Societies, Improvements = w.Improvements, Hunters = w.Hunters, Herds = w.Herds, Rivers = w.Rivers, History = w.History, Generation = w.Generation, Tiles = null };

        private static void NormalizePhysicalContactTimes(WorldState world)
        {
            foreach (var society in world.Societies ??
                new System.Collections.Generic.List<Society>())
                foreach (var contact in society.Memory?.Contacts ??
                    new System.Collections.Generic.List<SettlementContact>())
                {
                    // Absent OptionalField data and the retained legacy helper
                    // both use NaN as an explicit migration sentinel.
                    if (double.IsNaN(contact.ObservedDays))
                        contact.ObservedDays = Math.Min(world.Days,
                            Math.Max(0, contact.Turn) *
                            world.Config.TurnDays);
                    if (double.IsInfinity(contact.ObservedDays) ||
                        contact.ObservedDays < 0 ||
                        contact.ObservedDays > world.Days + 1e-8)
                        throw new InvalidDataException(
                            "Invalid physical contact timestamp.");
                }
        }
        public static string ConfigurationHash(SimulationConfig config)
        {
            using (var output = new MemoryStream())
            {
                using (var writer = XmlDictionaryWriter.CreateBinaryWriter(output, null, null, false)) Serializer(typeof(SimulationConfig)).WriteObject(writer, config);
                using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(output.ToArray())).Replace("-", "");
            }
        }
        public static string Fingerprint(WorldState w)
        {
            // Exclude wall-clock generation timings and memory warnings from authoritative state hashing.
            FisheryState.Normalize(w);
            NormalizePhysicalContactTimes(w);
            var copy = new WorldState { MaterialSources = w.MaterialSources, MaterialKnowledge = w.MaterialKnowledge, FixedHousing = w.FixedHousing?.OrderBy(s => s.CampId).ThenBy(s => s.Tile).ToList(), FisherySites = w.FisherySites?.OrderBy(s => s.TileId).ToList(), Version = w.Version, Seed = w.Seed, Turn = w.Turn, NextId = w.NextId, PlayerCount = w.PlayerCount, Days = w.Days, Config = w.Config, Tiles = w.Tiles, Camps = w.Camps.OrderBy(c => c.Id).ToList(), Societies = w.Societies.OrderBy(s => s.Id).ToList(), Improvements = w.Improvements.OrderBy(i => i.Id).ToList(), Hunters = w.Hunters.OrderBy(h => h.Id).ToList(), Herds = w.Herds.OrderBy(h => h.Id).ToList(), Rivers = w.Rivers, History = w.History, Generation = null, Resources = w.Resources };
            RequireBoundOwnership(copy);
            copy.AutonomousReceipts = w.AutonomousReceipts;
            using (var hash = SHA256.Create())
            {
                using (var hashing = new CryptoStream(Stream.Null, hash, CryptoStreamMode.Write)) WritePayload(hashing, copy);
                return BitConverter.ToString(hash.Hash).Replace("-", "");
            }
        }
        // Compare pre-overlay authoritative fields without mutating the original world.
        public static string LegacyFingerprint(WorldState w)
        {
            var copy = CopyWithoutTiles(w); copy.Tiles = w.Tiles; return Fingerprint(copy);
        }
    }
}
