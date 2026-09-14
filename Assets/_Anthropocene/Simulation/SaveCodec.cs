using System;
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
        private const string Magic = "ANTHROPOCENE-8";
        private static DataContractSerializer Serializer(Type type) => new DataContractSerializer(type, new DataContractSerializerSettings { MaxItemsInObjectGraph = int.MaxValue });
        public static byte[] Save(WorldState world)
        {
            var owners = world.Societies.ToDictionary(s => s.Id);
            if (world.Camps.Any(c => !owners.TryGetValue(c.SocietyId, out var owner) || !ReferenceEquals(c.People, owner))) throw new InvalidOperationException("Bind society ownership at the world-construction boundary before saving.");
            using (var output = new MemoryStream())
            {
                using (var zip = new GZipStream(output, CompressionLevel.Fastest, true))
                using (var buffered = new BufferedStream(zip, 65536))
                using (var writer = new BinaryWriter(buffered))
                {
                    writer.Write(Magic);
                    // Geography dominates the save. Fixed primitive records avoid reflection and repeated XML member names for every tile.
                    var metadata = CopyWithoutTiles(world);
                    using (var header = new MemoryStream())
                    {
                        using (var xml = XmlDictionaryWriter.CreateBinaryWriter(header, null, null, false)) Serializer(typeof(WorldState)).WriteObject(xml, metadata);
                        writer.Write(checked((int)header.Length)); header.Position = 0; header.CopyTo(buffered);
                    }
                    writer.Write(world.Tiles.Length);
                    foreach (var tile in world.Tiles)
                    {
                        writer.Write(tile.Plate); writer.Write(tile.Downstream); writer.Write((byte)tile.Environment); writer.Write((byte)tile.NaturalVegetationPotential); writer.Write((byte)tile.CurrentVegetation); writer.Write(tile.LastEcologyUpdateDays);
                        writer.Write((byte)((tile.Continental ? 1 : 0) | (tile.Oasis ? 2 : 0) | (tile.Cultivated ? 4 : 0) | (tile.Fishery ? 8 : 0)));
                        writer.Write((byte)tile.ElevationLevel); writer.Write((byte)tile.RidgeMask); writer.Write((byte)tile.Relief); writer.Write((byte)tile.WaterBody); writer.Write((byte)tile.FreshwaterSource);
                        writer.Write(tile.DepressionDepth); writer.Write(tile.OasisInfluence); writer.Write(tile.Saltwater); writer.Write(tile.LandCondition); writer.Write(tile.AquaticCondition);
                        writer.Write(tile.Elevation); writer.Write(tile.Volcanism); writer.Write(tile.Flow); writer.Write(tile.Food); writer.Write(tile.PotentialFood); writer.Write(tile.Freshwater);
                        foreach (var s in tile.Seasons)
                        {
                            writer.Write(s.Temperature); writer.Write(s.Rain); writer.Write(s.Snow); writer.Write(s.WindX); writer.Write(s.Runoff); writer.Write(s.RegionalMoisture); writer.Write(s.Moisture);
                            writer.Write(s.AccessibleFreshwater); writer.Write(s.MaxFood); writer.Write(s.MaxPotentialFood); writer.Write(s.MaxFreshwater); writer.Write(s.MaxSaltwater); writer.Write(s.FoodRecoveryPerDay); writer.Write(s.BiomassRecoveryPerDay); writer.Write(s.WaterRecoveryPerDay); writer.Write(s.Habitat);
                        }
                    }
                }
                return output.ToArray();
            }
        }
        public static WorldState Load(byte[] bytes)
        {
            using (var input = new MemoryStream(bytes))
            using (var zip = new GZipStream(input, CompressionMode.Decompress))
            using (var buffered = new BufferedStream(zip, 65536))
            using (var reader = new BinaryReader(buffered))
            {
                if (reader.ReadString() != Magic) throw new InvalidDataException("Unsupported save format: this opening requires separate regional and local moisture. Start a new opening; the older save is preserved.");
                int headerLength = reader.ReadInt32(); if (headerLength < 1 || headerLength > 128 * 1024 * 1024) throw new InvalidDataException("Invalid save metadata size.");
                WorldState world;
                using (var xml = XmlDictionaryReader.CreateBinaryReader(reader.ReadBytes(headerLength), XmlDictionaryReaderQuotas.Max)) world = (WorldState)Serializer(typeof(WorldState)).ReadObject(xml);
                // Generators 8-11 change profiles already stored in this format. Preserve old
                // profiles and generator metadata; aquatic condition uses its original rules
                // for pre-11 saves. No implicit geography or plankton regeneration.
                if (world.Version != 7 || world.Config.Version != 7 || world.Generation.GeneratorVersion < 7 || world.Generation.GeneratorVersion > WorldState.GeneratorVersion) throw new InvalidDataException("Unsupported save version; regeneration is never implicit.");
                Ownership.Bind(world);
                MigrateGenerationalDefaults(world);
                world.Config.Validate();
                int count = reader.ReadInt32(); if (count != world.Config.Width * world.Config.Height) throw new InvalidDataException("Invalid tile count.");
                world.Tiles = new Tile[count];
                for (int i = 0; i < count; i++)
                {
                    var tile = new Tile { Id = i, Plate = reader.ReadInt32(), Downstream = reader.ReadInt32(), Environment = (EnvironmentKind)reader.ReadByte(), NaturalVegetationPotential = (VegetationTier)reader.ReadByte(), CurrentVegetation = (VegetationTier)reader.ReadByte(), LastEcologyUpdateDays = reader.ReadDouble() };
                    byte flags = reader.ReadByte(); tile.Continental = (flags & 1) != 0; tile.Oasis = (flags & 2) != 0; tile.Cultivated = (flags & 4) != 0; tile.Fishery = (flags & 8) != 0;
                    tile.ElevationLevel = reader.ReadByte(); tile.RidgeMask = reader.ReadByte(); tile.Relief = (Relief)reader.ReadByte(); tile.WaterBody = (WaterKind)reader.ReadByte(); tile.FreshwaterSource = (FreshwaterSource)reader.ReadByte();
                    tile.DepressionDepth = reader.ReadSingle(); tile.OasisInfluence = reader.ReadSingle(); tile.Saltwater = reader.ReadDouble(); tile.LandCondition = reader.ReadDouble(); tile.AquaticCondition = reader.ReadDouble();
                    tile.Elevation = reader.ReadSingle(); tile.Volcanism = reader.ReadSingle(); tile.Flow = reader.ReadSingle(); tile.Food = reader.ReadDouble(); tile.PotentialFood = reader.ReadDouble(); tile.Freshwater = reader.ReadDouble();
                    for (int s = 0; s < 4; s++) tile.Seasons[s] = new SeasonData
                    {
                        Temperature = reader.ReadSingle(), Rain = reader.ReadSingle(), Snow = reader.ReadSingle(), WindX = reader.ReadSingle(), Runoff = reader.ReadSingle(), RegionalMoisture = reader.ReadSingle(), Moisture = reader.ReadSingle(),
                        AccessibleFreshwater = reader.ReadSingle(), MaxFood = reader.ReadSingle(), MaxPotentialFood = reader.ReadSingle(), MaxFreshwater = reader.ReadSingle(), MaxSaltwater = reader.ReadSingle(), FoodRecoveryPerDay = reader.ReadSingle(), BiomassRecoveryPerDay = reader.ReadSingle(), WaterRecoveryPerDay = reader.ReadSingle(), Habitat = reader.ReadSingle()
                    };
                    world.Tiles[i] = tile;
                }
                foreach (var edge in world.Rivers)
                {
                    if (world.Tiles[edge.TileA].RiverEdges == null) world.Tiles[edge.TileA].RiverEdges = Enumerable.Repeat(-1, 6).ToArray();
                    world.Tiles[edge.TileA].RiverEdges[edge.SideA] = edge.Id;
                    if (edge.TileB < 0) continue;
                    if (world.Tiles[edge.TileB].RiverEdges == null) world.Tiles[edge.TileB].RiverEdges = Enumerable.Repeat(-1, 6).ToArray();
                    world.Tiles[edge.TileB].RiverEdges[edge.SideB] = edge.Id;
                }
                foreach (var turn in world.History) { turn.Pressure = new System.Collections.Generic.List<PressureEntry>(); turn.EcologyBalances = new System.Collections.Generic.List<EcologyBalance>(); }
                TurnResolver.Invariants(world); ElevationLevelStage.Validate(world); BoundaryRiverStage.Validate(world); return world;
            }
        }
        private static void MigrateGenerationalDefaults(WorldState world)
        {
            var config = world.Config;
            foreach (var camp in world.Camps.Where(c => c.DemographicGrowthMultiplier <= 0)) camp.DemographicGrowthMultiplier = 1;
            bool legacyGenerationalConfig = config.StartingReserveGenerations <= 0 && config.HunterCarryGenerations <= 0 && config.MinimumStorageGenerations <= 0 && config.MaximumStorageGenerations <= 0;
            if (config.StartingReserveGenerations <= 0) config.StartingReserveGenerations = 1;
            if (config.HunterCarryGenerations <= 0) config.HunterCarryGenerations = 2.5;
            if (config.MinimumStorageGenerations <= 0) config.MinimumStorageGenerations = 1;
            if (config.MaximumStorageGenerations < config.MinimumStorageGenerations) config.MaximumStorageGenerations = 4;
            if (config.HuntBreedingReserveFraction <= 0) config.HuntBreedingReserveFraction = .2;
            if (config.SustainableHuntPopulation <= 0) config.SustainableHuntPopulation = 16;
            if (config.SustainableHerdGrowthRetention <= 0) config.SustainableHerdGrowthRetention = .25;
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
        private static WorldState CopyWithoutTiles(WorldState w) => new WorldState { Version = w.Version, Seed = w.Seed, Turn = w.Turn, NextId = w.NextId, Days = w.Days, Config = w.Config, Camps = w.Camps, Societies = w.Societies, Improvements = w.Improvements, Hunters = w.Hunters, Herds = w.Herds, Rivers = w.Rivers, History = w.History, Generation = w.Generation, Tiles = null };
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
            var copy = new WorldState { Version = w.Version, Seed = w.Seed, Turn = w.Turn, NextId = w.NextId, Days = w.Days, Config = w.Config, Tiles = w.Tiles, Camps = w.Camps.OrderBy(c => c.Id).ToList(), Societies = w.Societies.OrderBy(s => s.Id).ToList(), Improvements = w.Improvements.OrderBy(i => i.Id).ToList(), Hunters = w.Hunters.OrderBy(h => h.Id).ToList(), Herds = w.Herds.OrderBy(h => h.Id).ToList(), Rivers = w.Rivers, History = w.History, Generation = null };
            using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(Save(copy))).Replace("-", "");
        }
    }
}
