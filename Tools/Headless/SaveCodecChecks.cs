using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.Serialization;
using System.Xml;
using Anthropocene.Simulation;

internal static class SaveCodecChecks
{
    private static int checks;
    private static void Check(bool ok, string message) { checks++; if (!ok) throw new Exception("Save codec: " + message); }
    private static void Reject(Action action, string message)
    {
        bool rejected = false;
        try { action(); }
        catch (InvalidDataException) { rejected = true; }
        catch (EndOfStreamException) { rejected = true; }
        catch (SerializationException) { rejected = true; }
        catch (XmlException) { rejected = true; }
        Check(rejected, message);
    }
    private static byte[] Unzip(byte[] bytes)
    {
        using var source = new MemoryStream(bytes); using var zip = new GZipStream(source, CompressionMode.Decompress);
        using var result = new MemoryStream(); zip.CopyTo(result); return result.ToArray();
    }
    private static byte[] Zip(byte[] bytes)
    {
        using var result = new MemoryStream();
        using (var zip = new GZipStream(result, CompressionLevel.Fastest, true)) zip.Write(bytes, 0, bytes.Length);
        return result.ToArray();
    }
    private static byte[] Reframe(byte[] payload, string envelope, long delta = 0, bool truncate = false)
    {
        using var input = new MemoryStream(payload); using var reader = new BinaryReader(input);
        reader.ReadString(); int size = reader.ReadInt32(); byte[] rest = reader.ReadBytes((int)(input.Length - input.Position));
        using var result = new MemoryStream(); using var writer = new BinaryWriter(result);
        writer.Write(envelope);
        if (envelope == "ANTHROPOCENE-12") writer.Write(size + delta); else writer.Write(checked(size + (int)delta));
        writer.Write(rest, 0, truncate ? size / 2 : rest.Length); writer.Flush(); return Zip(result.ToArray());
    }
    private sealed class FragmentedStream : Stream
    {
        private readonly MemoryStream data;
        internal bool Closed;
        internal FragmentedStream(byte[] bytes) { data = new MemoryStream(bytes); }
        public override int Read(byte[] buffer, int offset, int count) => data.Read(buffer, offset, Math.Min(7, count));
        public override bool CanRead => !Closed;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { Closed = true; if (disposing) data.Dispose(); base.Dispose(disposing); }
    }
    public static int Run()
    {
        checks = 0;
        var w = AgriculturalTechnologyChecks.Fixture(); var c = w.Camps[0];
        int tileCount = w.Tiles.Length;
        w.Resources = new WorldResourceState { MaximumQuantity = 100000, CatalogHash = ResourceCatalog.Hash,
            ProfileHash = ResourceCatalog.Hash, ConfigurationHash = ResourceCatalog.Hash,
            Provinces = Array.Empty<GeologicalProvince>(), Province = new ushort[tileCount], History = new ushort[tileCount],
            Host = new byte[tileCount], Marine = new byte[tileCount], Stone = new ushort[tileCount], Clay = new ushort[tileCount],
            Wood = new ushort[tileCount], WoodRecoveryPerYear = .05 };
        w.Resources.Stone[c.Tile] = 123;
        c.StationaryYears = 77; c.Inventory.HousingUnits = 0;
        NeolithicProduction.AddFixedHousing(w, c, 7, w.Days);
        var tile = w.Tiles[c.Tile]; tile.CultivationCapital = .4; tile.CultivationOperator = c.Id;
        byte[] saved = SaveCodec.Save(w), payload = Unzip(saved); string expected = SaveCodec.Fingerprint(w);
        using (var reader = new BinaryReader(new MemoryStream(payload))) Check(reader.ReadString() == "ANTHROPOCENE-11", "ordinary saves retain envelope 11");
        using (var output = new MemoryStream())
        {
            SaveCodec.Save(w, output);
            Check(output.CanWrite, "saving leaves the caller's stream open");
            Check(Unzip(output.ToArray()).SequenceEqual(payload), "byte-array and streaming save payloads agree");
            output.Position = 0; var restored = SaveCodec.Load(output);
            Check(output.CanRead, "loading leaves the caller's stream open");
            Check(SaveCodec.Fingerprint(restored) == expected, "streaming roundtrip preserves all authoritative fields");
        }
        foreach (string format in new[] { "ANTHROPOCENE-11", "ANTHROPOCENE-12" })
        {
            byte[] framed = Reframe(payload, format);
            using var fragmented = new FragmentedStream(framed);
            var loaded = SaveCodec.Load(fragmented);
            Check(loaded.Resources != null && loaded.Resources.Stone[c.Tile] == 123, format + " preserves the separate resource block");
            Check(!fragmented.Closed, format + " does not close a non-seekable source");
            Check(SaveCodec.Fingerprint(loaded) == expected, format + " fragmented load preserves state without legacy migrations");
            Check(loaded.Camps[0].StationaryYears == 77, format + " retains physical stationary time");
            Check(loaded.Tiles[c.Tile].CultivationCapital == .4 && loaded.Tiles[c.Tile].CultivationOperator == c.Id, format + " retains capital/operator layout");
            var reference = SaveCodec.Load(saved);
            new TurnResolver().Resolve(reference, new System.Collections.Generic.Dictionary<int, System.Collections.Generic.List<Order>>());
            new TurnResolver().Resolve(loaded, new System.Collections.Generic.Dictionary<int, System.Collections.Generic.List<Order>>());
            Check(SaveCodec.Fingerprint(reference) == SaveCodec.Fingerprint(loaded), format + " exact next-turn continuation");
            Reject(() => SaveCodec.Load(Reframe(payload, format, -1)), format + " rejects undersized metadata");
            Reject(() => SaveCodec.Load(Reframe(payload, format, 1)), format + " cannot consume tile bytes as metadata");
            Reject(() => SaveCodec.Load(Reframe(payload, format, truncate: true)), format + " rejects truncated metadata");
        }
        foreach (long size in new[] { 1L, 512L * 1024 * 1024 + 1, int.MaxValue, (long)int.MaxValue + 1, SaveCodec.MaximumMetadataBytes })
        {
            using var buffer = new MemoryStream(); using var writer = new BinaryWriter(buffer);
            SaveCodec.WriteMetadataPrefix(writer, size); writer.Flush(); buffer.Position = 0;
            using var reader = new BinaryReader(buffer);
            string magic = reader.ReadString();
            Check(magic == (size <= int.MaxValue ? "ANTHROPOCENE-11" : "ANTHROPOCENE-12"), "length chooses compatible envelope at " + size);
            Check((magic == "ANTHROPOCENE-11" ? reader.ReadInt32() : reader.ReadInt64()) == size, "length survives prefix at " + size);
            SaveCodec.ValidateMetadataLength(size);
        }
        foreach (long size in new[] { -1L, 0, SaveCodec.MaximumMetadataBytes + 1, long.MaxValue })
        {
            using var buffer = new MemoryStream(); using var writer = new BinaryWriter(buffer);
            Reject(() => SaveCodec.WriteMetadataPrefix(writer, size), "writer rejects unsupported length " + size);
            Check(buffer.Length == 0, "invalid metadata length writes no prefix");
            writer.Write("ANTHROPOCENE-12"); writer.Write(size); writer.Flush();
            using var input = new MemoryStream(Zip(buffer.ToArray()));
            Reject(() => SaveCodec.Load(input), "loader rejects unsupported length before allocation " + size);
            Check(input.CanRead, "failed load leaves its input open");
        }
        byte[] block = new byte[1024 * 1024];
        using (var count = new SaveCodec.MetadataWriteStream(null, (long)int.MaxValue + 1))
        {
            for (int i = 0; i < 2048; i++) count.Write(block, 0, block.Length);
            Check(count.Length == (long)int.MaxValue + 1, "counting exceeds Int32 without a metadata-sized buffer");
            Reject(() => count.WriteByte(1), "writer enforces its measured limit before emitting an extra byte");
        }
        using (var source = new MemoryStream(new byte[] { 1, 2, 3, 99 }))
        {
            using (var bounded = new SaveCodec.MetadataReadStream(source, 3))
            {
                Check(bounded.Read(block, 0, block.Length) == 3, "reader stops at the declared metadata boundary");
                Check(bounded.Read(block, 0, block.Length) == 0, "metadata EOF does not advance to tiles");
            }
            Check(source.ReadByte() == 99, "next block is untouched and caller stream stays open");
        }
        Console.WriteLine("PASS: " + checks + " save codec assertions"); return checks;
    }
}
