using CDRIconExtractor.Core.Parsing;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CDRIconExtractor.Core.Tests.Parsing;

[TestClass]
public sealed class CrlIconGuidMapParserTests
{
    [TestMethod]
    public void Parse_Modern76ByteRecord_MapsGuidToUInt16IconId()
    {
        var map = CrlIconGuidMapParser.Parse(new[] { (ReadOnlyMemory<byte>)File.ReadAllBytes(Fixture("guid-map-modern.bin")) });
        CollectionAssert.Contains(map[42].ToList(), "11111111-1111-1111-1111-111111111111");
    }

    [TestMethod]
    public void Parse_LegacyNullSeparatedRecords_MapsMultipleGuidsToSameId()
    {
        var map = CrlIconGuidMapParser.Parse(new[] { (ReadOnlyMemory<byte>)File.ReadAllBytes(Fixture("guid-map-legacy.bin")) });
        Assert.AreEqual(2, map[7].Count);
    }


    [TestMethod]
    public void Parse_DelimitedResource_ParsesOnlyDelimitedRecordsAndIgnoresTrailingNoise()
    {
        const string mappedGuid = "44444444-4444-4444-4444-444444444444";
        const string trailingGuid = "55555555-5555-5555-5555-555555555555";
        var delimiter = new byte[] { 0x00, 0x00, 0x24, 0x00 };
        var record = CreateModernRecord(mappedGuid, 51);
        var trailingNoise = CreateModernRecord(trailingGuid, 99);

        var blob = new byte[2 + delimiter.Length + record.Length + delimiter.Length + trailingNoise.Length];
        var cursor = 2;
        delimiter.CopyTo(blob, cursor);
        cursor += delimiter.Length;
        record.CopyTo(blob, cursor);
        cursor += record.Length;
        delimiter.CopyTo(blob, cursor);
        cursor += delimiter.Length;
        trailingNoise.CopyTo(blob, cursor);

        var map = CrlIconGuidMapParser.Parse(new[] { (ReadOnlyMemory<byte>)blob });

        CollectionAssert.Contains(map[51].ToList(), mappedGuid);
        Assert.IsFalse(map.ContainsKey(99), "Bytes after the last delimiter are not a Corel GUID-map record.");
    }

    private static byte[] CreateModernRecord(string guid, ushort iconId)
    {
        var guidBytes = System.Text.Encoding.Unicode.GetBytes(guid);
        Assert.AreEqual(72, guidBytes.Length);
        var record = new byte[76];
        record[0] = 1;
        record[1] = 0;
        guidBytes.CopyTo(record, 2);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(74, 2), iconId);
        return record;
    }


    [TestMethod]
    public void Parse_RepeatingGuidPrefix_IsNotMistakenForDelimiter()
    {
        var record = CreateModernRecord("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", 73);
        var map = CrlIconGuidMapParser.Parse(new[] { (ReadOnlyMemory<byte>)record });

        CollectionAssert.Contains(map[73].ToList(), "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    }

    [TestMethod]
    public void Parse_MalformedBlob_DoesNotThrow()
    {
        var map = CrlIconGuidMapParser.Parse(new[] { (ReadOnlyMemory<byte>)new byte[] { 1, 2, 3, 4, 5 } });
        Assert.AreEqual(0, map.Count);
    }

    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "fixtures", "CrlIcons", name);
}
