using System;
using System.IO;
using System.Linq;

namespace Anthropocene.Simulation
{
    // Resource arrays live outside the bounded XML metadata header. Indexes are not serialized.
    public static class ResourceCodec
    {
        public static void Write(BinaryWriter writer, WorldResourceState r)
        {
            if (r.Version < 1 || r.Version > WorldResourceState.CurrentVersion) throw new InvalidDataException("Unsupported resource schema.");
            writer.Write(r.Version); writer.Write(r.GenerationVersion); writer.Write(r.CoreGeneratorVersion); writer.Write(r.CatalogHash); writer.Write(r.ProfileHash); writer.Write(r.ConfigurationHash);
            writer.Write(r.MaximumQuantity); writer.Write(r.WoodRecoveryPerYear);
            if (r.Version >= 3) writer.Write(r.SurfaceUnit);
            if (r.Version >= 4) { writer.Write(r.DeepReserveMultiplier); writer.Write(r.DeepMiningTechnology ?? ""); }
            writer.Write(r.AccessibleParts.Length);
            for (int i = 0; i < r.AccessibleParts.Length; i++)
            {
                writer.Write(r.AccessibleParts[i]);
                writer.Write(r.DiscoveryTechnologies[i] ?? "");
                if (r.Version >= 4) writer.Write(r.Concentrations != null && i < r.Concentrations.Length ? r.Concentrations[i] : 1.0);
            }
            writer.Write(r.Provinces.Length);
            foreach (var p in r.Provinces.OrderBy(p => p.Id))
            { writer.Write(p.Id); writer.Write(p.X); writer.Write(p.Y); writer.Write(p.RadiusX); writer.Write(p.RadiusY); writer.Write(p.Angle); writer.Write((byte)p.Host); writer.Write((ushort)p.History); }
            writer.Write(r.Province.Length);
            for (int i = 0; i < r.Province.Length; i++)
            { writer.Write(r.Province[i]); writer.Write(r.Host[i]); writer.Write(r.History[i]); writer.Write(r.Marine[i]); writer.Write(r.Stone[i]); writer.Write(r.Clay[i]); writer.Write(r.Wood[i]); if (r.Version >= 4) { writer.Write(r.DeepStone[i]); writer.Write(r.DeepClay[i]); } }
            writer.Write(r.Bodies.Length);
            foreach (var b in r.Bodies.OrderBy(b => b.Id))
            {
                writer.Write(b.Id); writer.Write(b.SourceBody); writer.Write(b.Profile); writer.Write(b.OriginTile); writer.Write(b.InitialQuantity); writer.Write(b.Quantity); writer.Write((byte)b.Shape); writer.Write(b.Hardness); writer.Write(b.Quality);
                if (r.Version >= 2) writer.Write((byte)b.Concentration);
                writer.Write(b.Components.Length);
                foreach (var part in b.Components.OrderBy(part => part.Material)) { writer.Write((ushort)part.Material); writer.Write(part.PartsPerTenThousand); if (r.Version >= 4) { writer.Write(part.InitialAccessibleQuantity); writer.Write(part.AccessibleQuantity); writer.Write(part.InitialDeepQuantity); writer.Write(part.DeepQuantity); } }
                writer.Write(b.Sites.Length);
                foreach (var s in b.Sites.OrderBy(s => s.Tile)) { writer.Write(s.Tile); writer.Write(s.Share); writer.Write(s.Exposure); writer.Write((byte)s.Burial); writer.Write((byte)s.Setting); }
                writer.Write(b.TransportPath.Length); foreach (int tile in b.TransportPath) writer.Write(tile);
            }
        }
        private static int Count(BinaryReader reader, int maximum)
        { int count = reader.ReadInt32(); if (count < 0 || count > maximum) throw new InvalidDataException("Invalid resource record count."); return count; }
        private static string Identity(BinaryReader reader)
        { var value = reader.ReadString(); if (value.Length != 64 || value.Any(c => !Uri.IsHexDigit(c))) throw new InvalidDataException("Invalid resource identity."); return value; }
        public static WorldResourceState Read(BinaryReader reader, int tiles)
        {
            var r = new WorldResourceState { Version = reader.ReadInt32(), GenerationVersion = reader.ReadInt32(), CoreGeneratorVersion = reader.ReadInt32() };
            if (r.Version < 1 || r.Version > WorldResourceState.CurrentVersion) throw new InvalidDataException("Unsupported resource schema; regeneration is never implicit.");
            r.CatalogHash = Identity(reader); r.ProfileHash = Identity(reader); r.ConfigurationHash = Identity(reader);
            r.MaximumQuantity = reader.ReadInt32(); r.WoodRecoveryPerYear = reader.ReadDouble();
            r.SurfaceUnit = r.Version >= 3 ? reader.ReadInt32() : WorldResourceState.LegacySurfaceUnit;
            if (r.Version >= 4) { r.DeepReserveMultiplier = reader.ReadDouble(); r.DeepMiningTechnology = reader.ReadString(); if (r.DeepMiningTechnology.Length > 128) throw new InvalidDataException("Invalid deep mining capability."); }
            int materials = Count(reader, 69); if (materials != 69) throw new InvalidDataException("Invalid saved material catalog size.");
            r.AccessibleParts = new ushort[materials]; r.DiscoveryTechnologies = new string[materials];
            if (r.Version >= 4) r.Concentrations = new double[materials];
            for (int i = 0; i < materials; i++)
            {
                r.AccessibleParts[i] = reader.ReadUInt16();
                r.DiscoveryTechnologies[i] = reader.ReadString();
                if (r.DiscoveryTechnologies[i].Length > 128) throw new InvalidDataException("Invalid discovery capability.");
                if (r.Version >= 4) r.Concentrations[i] = reader.ReadDouble();
            }
            r.Provinces = new GeologicalProvince[Count(reader, 4096)];
            for (int i = 0; i < r.Provinces.Length; i++) r.Provinces[i] = new GeologicalProvince { Id = reader.ReadInt32(), X = reader.ReadDouble(), Y = reader.ReadDouble(), RadiusX = reader.ReadDouble(), RadiusY = reader.ReadDouble(), Angle = reader.ReadDouble(), Host = (HostRock)reader.ReadByte(), History = (GeologicalHistory)reader.ReadUInt16() };
            if (Count(reader, tiles) != tiles) throw new InvalidDataException("Resource dimensions do not match geography.");
            r.Province = new ushort[tiles]; r.Host = new byte[tiles]; r.History = new ushort[tiles]; r.Marine = new byte[tiles]; r.Stone = new ushort[tiles]; r.Clay = new ushort[tiles]; r.Wood = new ushort[tiles];
            if (r.Version >= 4) { r.DeepStone = new ushort[tiles]; r.DeepClay = new ushort[tiles]; }
            for (int i = 0; i < tiles; i++) { r.Province[i] = reader.ReadUInt16(); r.Host[i] = reader.ReadByte(); r.History[i] = reader.ReadUInt16(); r.Marine[i] = reader.ReadByte(); r.Stone[i] = reader.ReadUInt16(); r.Clay[i] = reader.ReadUInt16(); r.Wood[i] = reader.ReadUInt16(); if (r.Version >= 4) { r.DeepStone[i] = reader.ReadUInt16(); r.DeepClay[i] = reader.ReadUInt16(); } }
            r.Bodies = new DepositBody[Count(reader, 200000)]; long sites = 0, paths = 0;
            for (int i = 0; i < r.Bodies.Length; i++)
            {
                var b = new DepositBody { Id = reader.ReadInt64(), SourceBody = reader.ReadInt64(), Profile = reader.ReadInt32(), OriginTile = reader.ReadInt32(), InitialQuantity = reader.ReadDouble(), Quantity = reader.ReadDouble(), Shape = (DepositShape)reader.ReadByte(), Hardness = reader.ReadByte(), Quality = reader.ReadByte() };
                if (r.Version >= 2) b.Concentration = (MineralConcentration)reader.ReadByte();
                b.Components = new DepositComponent[Count(reader, ResourceCatalog.Materials.Count)];
                for (int j = 0; j < b.Components.Length; j++)
                {
                    var part = new DepositComponent((MaterialId)reader.ReadUInt16(), reader.ReadUInt16());
                    if (r.Version >= 4) { part.InitialAccessibleQuantity = reader.ReadDouble(); part.AccessibleQuantity = reader.ReadDouble(); part.InitialDeepQuantity = reader.ReadDouble(); part.DeepQuantity = reader.ReadDouble(); }
                    b.Components[j] = part;
                }
                int count = Count(reader, Math.Min(10000, tiles)); sites += count;
                if (sites > Math.Max(65536, tiles * 8L)) throw new InvalidDataException("Resource footprint budget exceeded.");
                b.Sites = new DepositSite[count];
                for (int j = 0; j < count; j++) b.Sites[j] = new DepositSite { Tile = reader.ReadInt32(), Share = reader.ReadUInt16(), Exposure = reader.ReadUInt16(), Burial = (BurialDepth)reader.ReadByte(), Setting = (MarineSetting)reader.ReadByte() };
                int pathCount = Count(reader, tiles); paths += pathCount; if (paths > Math.Max(65536, tiles * 4L)) throw new InvalidDataException("Resource transport budget exceeded.");
                b.TransportPath = new int[pathCount]; for (int j = 0; j < pathCount; j++) b.TransportPath[j] = reader.ReadInt32();
                if (r.Version >= 4) b.SiteExposureWeightTotal = b.Sites.Sum(site => site.Share * (double)site.Exposure);
                r.Bodies[i] = b;
            }
            return r;
        }
        public static string Fingerprint(WorldResourceState r)
        {
            using (var memory = new MemoryStream())
            {
                using (var writer = new BinaryWriter(memory, System.Text.Encoding.UTF8, true)) Write(writer, r);
                using (var hash = System.Security.Cryptography.SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(memory.ToArray())).Replace("-", "");
            }
        }
    }
}
